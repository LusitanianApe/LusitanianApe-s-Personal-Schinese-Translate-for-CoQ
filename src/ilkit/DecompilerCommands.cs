using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

// 用法：
//   decomp list  <名字片段>                 列出匹配的类型全名
//   decomp type  <类型全名>                 反编译整个类型
//   decomp member <类型全名> <成员名片段>    只反编译匹配的成员
//   decomp find  <字符串片段>               全程序集：哪些成员引用了这个字符串
internal static class DecompilerCommands
{
    // ★★ 2026-10-17：**改为可用环境变量 `COQ_DLL` 覆盖**（默认仍是 Steam 那份 lang 分支）。
    //   起因：双版本并存调研时，本工具**硬编码**只读 Steam 那份 ⇒ 拿它"对比两版类型"
    //   实际是**拿 lang 跟自己比** ⇒ 得出"两版类型完全一样"的假结论。
    //   ★ 判据：**工具的"输入"必须可见/可换** —— 否则对比类结论全是假的。
    /// <summary>目标程序集路径（由 `Program.Main` 注入：`--dll` / `ILKIT_DLL` / `COQ_MANAGED`）。</summary>
    internal static string Asm;

    private static CSharpDecompiler _dec;
    private static ITypeDefinition[] _types;

    internal static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // ★★ 2.16.118：**强制 LF 行尾**。Windows 上 `Console.WriteLine` 默认发 CRLF，
        //   而下游 Python 用 `split("\n")` 解析 ⇒ 每行都留一个尾随 CR，
        //   恰好在**字面量以 CR 结尾**时与 heap 侧对不上（实测多出/少掉 1 条）。
        //   TSV 是机器读的 ⇒ 行尾必须写死，不能跟随平台。
        Console.Out.NewLine = "\n";
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: ilkit <list|type|member|find|literals|ldstr|il|sig|callsites|calls|branches|baseargs|diag> …");
            return 2;
        }

        var settings = new DecompilerSettings(LanguageVersion.Latest)
        {
            ShowXmlDocumentation = false,
            UsingDeclarations = true,
        };
        _dec = new CSharpDecompiler(Asm, settings);
        var all = _dec.TypeSystem.MainModule.TypeDefinitions.ToList();
        _types = all.ToArray();

        switch (args[0].ToLowerInvariant())
        {
            case "list": return List(args[1]);
            case "baseargs": return BaseArgs(args[1]);
            case "type": return Type(args[1]);
            case "member": return Member(args[1], args[2]);
            case "find": return Find(args.Skip(1).ToArray());
            case "literals": return Literals();
            case "ldstr": return Ldstr();
            case "diag": return Diag();
            case "il": return IlDump(args[1]);
            case "callsites": return Callsites();
            case "sig": return Sig(args[1]);
            case "calls": return Calls();
            case "branches": return Branches();
            default:
                Console.Error.WriteLine("未知模式");
                return 2;
        }
    }

    private static int List(string frag)
    {
        var hits = _types.Where(t =>
            t.FullName.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        foreach (var t in hits)
            Console.WriteLine(t.FullName);
        Console.Error.WriteLine("共 " + hits.Count + " 个类型");
        return 0;
    }

    private static ITypeDefinition Resolve(string name)
    {
        return _types.FirstOrDefault(t => t.FullName == name)
            ?? _types.FirstOrDefault(t => t.FullName.EndsWith("." + name, StringComparison.Ordinal));
    }

    private static int Type(string name)
    {
        var t = Resolve(name);
        if (t == null) { Console.Error.WriteLine("找不到: " + name); return 1; }
        Console.WriteLine(_dec.DecompileTypeAsString(new FullTypeName(t.FullName)));
        return 0;
    }

    private static int Member(string name, string frag)
    {
        var t = Resolve(name);
        if (t == null) { Console.Error.WriteLine("找不到: " + name); return 1; }
        int n = 0;
        foreach (var m in t.Members)
        {
            if (m.Name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                Console.WriteLine("// ── " + t.FullName + "::" + m.Name);
                Console.WriteLine(_dec.DecompileAsString(m.MetadataToken));
                n++;
            }
            catch (Exception e) { Console.Error.WriteLine("失败 " + m.Name + ": " + e.Message); }
        }
        Console.Error.WriteLine("共 " + n + " 个成员");
        return 0;
    }

    /// <summary>
    /// 列出程序集 UserString heap 里的**全部字符串字面量**（每行一条）。
    ///
    /// ★ 为什么需要它（2026-10-10）：`find` 一次只吃一个片段、且要反编译整个程序集，
    ///   做「介词频率统计」这种活要跑几十次，不可用。
    ///   直接读元数据里的 UserString heap 是 **O(1) 次扫描**，快几个数量级。
    /// ★ 去重：同一字面量在 IL 里通常只存一份（UserString heap 本身按内容去重），
    ///   所以这里输出的是**唯一字面量集合**；要计「被引用次数」需另算。
    /// </summary>
    private static int Literals()
    {
        int n = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using (var fs = File.OpenRead(Asm))
        using (var pe = new PEReader(fs))
        {
            var md = pe.GetMetadataReader();
            // ★ 必须**按 heap 格式行走**，不能按字节自增：
            //   UserString 每项 = [压缩长度前缀][UTF-16LE 数据]，
            //   而 `GetUserString(任意偏移)` 会把中段当字符串头 ⇒ 乱码（实测踩过）。
            //   ⚠ 长度前缀是**压缩整数**：>0x7F 占 2 字节、>0x3FFF 占 4 字节，
            //     按固定 1 字节算会让长字符串错位（本次一并修正）。
            int i = 1;                     // 0 号是空串占位
            while (true)
            {
                string s;
                try { s = md.GetUserString(MetadataTokens.UserStringHandle(i)); }
                catch { break; }
                if (s == null) break;

                // 读这一项的长度前缀，决定步长
                int dataBytes = s.Length * 2;          // UTF-16LE 字节数
                int prefix = dataBytes < 0x80 ? 1 : (dataBytes < 0x4000 ? 2 : 4);
                i += prefix + dataBytes;
                if (i <= 0) break;                     // 防溢出

                if (s.Length == 0) continue;
                if (!seen.Add(s)) continue;            // 去重
                // ★★ 2.16.118：必须用与 `ldstr` **同一个** `Esc`（原来只换 \r \n，
                //   不换 `\` 和 `\t`）⇒ 两个命令的转义**不可逆且互不相同**：
                //     真串 `\r`（反斜杠+r）与真串 CR 都写成 `\r` ⇒ 读侧去重会**吃掉 99 条**；
                //     含 `\t` 的串在 heap 侧是裸制表符、在 ldstr 侧是 `\t` ⇒ 对账永远错。
                //   `Esc` 先换 `\` 再换其余 ⇒ 单射，两侧可无损还原。
                Console.WriteLine(Esc(s));
                n++;
            }
            // ★ 自证：走完的字节数必须**正好等于** #US heap 的大小
            int heapSize;
            try { heapSize = md.GetHeapSize(HeapIndex.UserString); }
            catch { heapSize = -1; }
            if (heapSize > 0 && i > heapSize)
                Console.Error.WriteLine("★ 警告：#US 行走超出 heap（走到 " + i + " > " + heapSize + "）");
            else if (heapSize > 0)
                Console.Error.WriteLine("★ #US heap 大小 = " + heapSize + " B，行走消耗 = " + i + " B（差 "
                    + (heapSize - i) + " B = 尾部填充）");
        }
        Console.Error.WriteLine("共 " + n + " 条字符串字面量（已去重）");
        return 0;
    }

    /// <summary>
    /// 提取**指定静态方法调用**的第一实参字面量（用于批量收集 `because` 值这类参数）。
    ///
    /// 用法：`decomp baseargs "AutoAct.Interrupt"`
    ///   ⇒ 遍历全程序集成员，反编译每个成员，正则抽出
    ///      `<方法名>(<第一实参>)` 里的第一实参**源码文本**。
    /// ★ 为什么需要它：`find` 只能按「串」找，无法批量取「调用点的第 N 个实参」。
    /// </summary>
    private static int BaseArgs(string callee)
    {
        // callee 形如 `AutoAct.Interrupt` → 匹配 `AutoAct.Interrupt(` 或 `.Interrupt(`
        string shortName = callee.Contains(".") ? callee.Substring(callee.LastIndexOf('.') + 1) : callee;
        var rx = new System.Text.RegularExpressions.Regex(
            @"(?<![\w.])" + System.Text.RegularExpressions.Regex.Escape(shortName) + @"\s*\(");
        int nHit = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in _types)
        {
            foreach (var m in t.Members)
            {
                string code;
                try { code = _dec.DecompileAsString(m.MetadataToken); }
                catch { continue; }
                if (code.IndexOf(shortName, StringComparison.Ordinal) < 0) continue;
                foreach (System.Text.RegularExpressions.Match mm in rx.Matches(code))
                {
                    // 从 '(' 开始做括号配对，取第一实参
                    int i = mm.Index + mm.Length;      // 指向 '(' 之后
                    int depth = 1, j = i;
                    bool inStr = false;
                    while (j < code.Length && depth > 0)
                    {
                        char c = code[j];
                        if (inStr)
                        {
                            if (c == '\\') { j += 2; continue; }
                            if (c == '"') inStr = false;
                        }
                        else
                        {
                            if (c == '"') inStr = true;
                            else if (c == '(') depth++;
                            else if (c == ')') depth--;
                        }
                        j++;
                    }
                    string arg = code.Substring(i, Math.Max(0, j - 1 - i)).Trim();
                    // 只报「第一实参含字符串字面量或 null/变量」的
                    if (arg.Length == 0) continue;
                    string key = t.FullName + "::" + m.Name + " ||| " + arg;
                    if (!seen.Add(key)) continue;
                    Console.WriteLine(key);
                    nHit++;
                }
            }
        }
        Console.Error.WriteLine("共 " + nHit + " 个调用点");
        return 0;
    }

    /// <summary>
    /// 全程序集搜字符串字面量：**扫 `ldstr`，不反编译**。
    ///
    /// ★ 为什么改（2026-10-18）：旧实现把**每个成员都反编译一遍**再在源码里 `IndexOf`
    ///   ⇒ 全量反编译一个 20 MB 的程序集要几分钟，长字面量（带空格）看起来像"挂住"。
    ///   实测同一程序集：反编译法 222.5s，IL 走法 2.4s（快约 100 倍）。
    ///   另一个好处是**更准**：反编译产物里的引号串有猜出来的部分（插值串会被还原成源码形态），
    ///   而 `ldstr` 指向的是二进制里真实存在的串。
    ///
    /// 用法：`ilkit find "You have struck" "some other phrase"`
    /// </summary>
    private static int Find(string[] frags)
        {
        int n = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ForEachMethodIl((fullType, member, il) =>
        {
            string type = StripGeneratedTail(fullType);
            foreach (var (off, op, kind, arg, end) in WalkIl(il))
            {
                if (op != 0x72 || kind != 9 || arg.Length != 4) continue;
                string s = StringsAtToken(BitConverter.ToInt32(arg, 0));
                if (s == null) continue;
                bool hit = false;
                foreach (var f in frags)
                {
                    if (s.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                }
                if (!hit) continue;
                string key = type + "::" + member + "|" + s;
                if (!seen.Add(key)) continue;
                n++;
                Console.WriteLine("=== " + type + "::" + member);
                Console.WriteLine("--- literal: " + Esc(s));
                try
                {
                    var td = _types.FirstOrDefault(x => x.FullName == type);
                    var md = td == null ? null : td.Members.FirstOrDefault(x => x.Name == member);
                    if (md != null)
                    {
                        string code = _dec.DecompileAsString(md.MetadataToken);
                        Console.WriteLine(code.Length > 3000
                            ? code.Substring(0, 3000) + "\n…(truncated)" : code);
                    }
                }
                catch { }
                Console.WriteLine();
            }
        });
        Console.Error.WriteLine("共 " + n + " 个命中（owner + 字面量去重）");
        return 0;
    }

    /// <summary>
    /// ★★ 2.16.117：全程序集扫 **`ldstr` 引用**，输出「字面量 ← 谁引用」的归属表（TSV）。
    ///
    /// 借鉴自矮人要塞两条本地化路线（详见 `_tools\DFCN-可借鉴清单.md` 附录二/三）：
    ///   · **dfint**（`dfint64_patch`）：数据段线性扫候选 → 代码段找 RIP 相对交叉引用
    ///     → **只留有引用的**（死串自动出局）
    ///   · **DFCN**（`pokemonchw/dfcn`）：`native-literals.tsv` 记
    ///     `owner / function_rva / literal_rva / operand_role`
    ///
    /// 本命令把两者的**关键字段**搬到 .NET IL 上：
    ///   DF 要算 RIP 相对位移、解 `.pdata` 取函数边界；
    ///   IL 里 **`ldstr <token>` 的 token 直接给地址**、MethodDef 表直接给方法边界
    ///   ⇒ 成本低一个数量级。
    ///
    /// 判据说明（**重要**，避免把工具输出误当结论）：
    ///   · 本命令**完全来自 IL 的 `ldstr`**（2.16.118 起），不再是反编译源码扫描
    ///     ⇒ 收进来的每一条都是**二进制里真实存在**的串。
    ///   · ★ **上界 = `literals` 的 heap 全集**：纯 ldstr 走法**不可能**超过它。
    ///     所以「跟反编译法比总数」是错的口径 —— 反编译法会多出**非 ldstr** 的
    ///     还原串（`{0}` 被还原成 `{x}`、默认参数值、特性实参、未使用的 const）。
    ///     正确口径：**本表 ⊇ (旧表 ∩ heap)**；判「死串/活串」用 heap 全集去减本表。
    ///   · 编译器生成的类型（`<>c` / `<>c__DisplayClass` / `<…>d__` 等）
    ///     **不跳过**（跳过会丢掉 lambda 里的字面量），
    ///     但 owner 会**归并到最外层声明类型**（`StripGeneratedTail`）。
    ///
    /// 输出（TSV）：`type \t member \t literal`  （literal 已转义 `\n` `\r` `\t`）
    /// 用法：`decomp ldstr > out.tsv`
    /// </summary>
    private static int Ldstr()
    {
        Console.WriteLine("type\tmember\tliteral");
        int nLiteral = 0, nMerged = 0;

        // ★★ 2.16.117 改为 **IL 字节码走法**（原版是反编译每个成员再扫 C# 源码）。
        //
        //   为什么换（两条都实测过）：
        //     ① **快 100 倍**：反编译法 222.5s；IL 走法 2.4s。
        //     ② **更准**：反编译产物里的引号串是**猜出来**的（实测同一方法
        //        `_T(` 出现 69 次，而 IL 里只有 48 个 callsite —— 编译器为跳转抄了副本）；
        //        插值串还会被**还原成源码形态**（`Credits: {0}` → `Credits: {x}`），
        //        而 heap 里存的是 `{0}` 形态 ⇒ 反编译法那些串**在二进制里根本不存在**。
        //     ③ 顺带消掉「多行字面量撑破 TSV」「`/* */` 注释里的引号被误计」两个已知局限。
        //
        //   ★★ 2.16.118 修覆盖率的**两条**根因（原来少收 7,425 条真 ldstr）：
        //     ① operand 表按 ECMA-335 重建：0x1E→**0x1F**(ldc.i4.s)、补 0x0F/0x27/0x29/
        //        0x70/0x71、**短跳转是 1 字节**（原来与 ldc.r4 共用 kind 而按 4 字节走）、
        //        0xFE 段补 ldftn/ldvirtftn/sizeof/ldloc 族/unaligned./no.
        //     ② **不再跳过编译器生成类型**：lambda 的 `ldstr` 物理上住在
        //        `<>c` / `<Foo>d__N` 里，跳过它们 = 把这些字面量整个丢掉。
        //        改用 `StripGeneratedTail` 把 owner **归到最外层声明类型**名下
        //        （与旧反编译法的归属口径一致，且一条不丢）。
        var types = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        ForEachMethodIl((fullType, member, il) =>
        {
            string type = StripGeneratedTail(fullType);
            if (type != fullType) nMerged++;
            string memberKey = type + "::" + member;
            foreach (var (off, op, kind, arg, end) in WalkIl(il))
            {
                if (op != 0x72 || kind != 9 || arg.Length != 4) continue;
                string s = StringsAtToken(BitConverter.ToInt32(arg, 0));
                if (s == null) continue;
                Console.WriteLine(Esc(type) + "\t" + Esc(member) + "\t" + Esc(s));
                nLiteral++;
                types.Add(type);
                members.Add(memberKey);
            }
        });

        Console.Error.WriteLine("共 " + types.Count + " 个 owner 类型（其中由生成类型归并而来 "
                                + nMerged + " 个方法）· "
                                + members.Count + " 个成员 · " + nLiteral + " 条 ldstr 引用");
        Console.Error.WriteLine("★ 行走器未走满 IL 体的方法数 = " + nWalkBad + " / " + nMethod
            + "（必须 = 0；>0 说明 operand 表有漏项，会**静默丢半段**）");
        foreach (var s in walkBadSample)
            Console.Error.WriteLine("   · " + s);
        return 0;
    }

    /// <summary>
    /// ★★ 2.16.118：把**编译器生成类型**的 owner 归到最外层声明类型名下。
    ///
    /// 反编译法的口径就是这样：`t.Members` 只到外层类型自己声明的方法，
    /// 而 lambda 的字符串**内联在外层方法的反编译源码里** ⇒ 归属 = 外层类型。
    /// IL 口径下 lambda 住在 `<>c` 里 ⇒ 必须显式归并，否则
    /// ① 丢了这些字面量（若跳过）② owner 变成不可读的 `<>c`（若不跳过）。
    ///
    /// 判据：全名的**某一层段**以 `&lt;` 开头（Roslyn 的生成名约定）。
    /// 截断到该段之前的点号 ⇒ `XRL.Foo.&lt;Bar&gt;d__12.&lt;&gt;c` → `XRL.Foo`。
    /// 顶层生成类型（无前缀可截）原样保留。
    /// </summary>
    private static string StripGeneratedTail(string full)
    {
        int start = 0;
        while (start < full.Length)
        {
            if (full[start] == '<')
                return start == 0 ? full : full.Substring(0, start - 1);
            int dot = full.IndexOf('.', start);
            if (dot < 0) break;
            start = dot + 1;
        }
        return full;
    }

    /// <summary>用 AST 抽出全部字符串字面量（含逐字串；字符字面量排除）。</summary>
    private static IEnumerable<string> LiteralStrings(ICSharpCode.Decompiler.CSharp.Syntax.AstNode root)
    {
        foreach (var node in root.Descendants)
        {
            if (node is ICSharpCode.Decompiler.CSharp.Syntax.PrimitiveExpression pe
                && pe.Value is string sv)
                yield return sv;
        }
    }

    /// <summary>
    /// ★ 行扫描器**自证**（2026-10-10）：本版本的 ICSharpCode.Decompiler **不向外部暴露
    /// C# 解析器**（`decomp probe` 实测：只有 `…CSharp.Syntax.PrimitiveExpression` /
    /// `SyntaxTree`，**没有 Parser**）⇒ 只能自己扫反编译产物。
    /// 扫错的后果是静默的（少报/多报字面量），所以**先跑固定用例自证**，
    /// 任何一条不符就让整个命令失败退出 —— 不产出可能错的表。
    /// </summary>
    private static void SelfTestStringLiterals()
    {
        var cases = new (string Code, string[] Want)[]
        {
            ("var a = \"hi\";",              new[] { "hi" }),
            ("var a = \"\";",                new[] { "" }),
            ("var a = \"a\\\"b\";",          new[] { "a\"b" }),
            ("var a = \"l1\\nl2\\tt\";",     new[] { "l1\nl2\tt" }),
            ("var a = @\"c:\\x\";",          new[] { "c:\\x" }),
            ("var a = @\"he said \"\"yo\"\"\";", new[] { "he said \"yo\"" }),
            ("a(\"x\"); b(\"y\");",          new[] { "x", "y" }),
            ("var c = 'z';",                 new string[0]),          // 字符字面量不算
            ("var a = \"u\\u4e2d\";",        new[] { "u中" }),
            ("// \"comment\"",               new string[0]),           // ★ 行注释内的引号串**不计**
        };
        int bad = 0;
        foreach (var (code, want) in cases)
        {
            var got = StringLiterals(code).ToArray();
            bool ok = got.Length == want.Length;
            if (ok)
                for (int i = 0; i < got.Length; i++)
                    if (got[i] != want[i]) { ok = false; break; }
            if (!ok)
            {
                bad++;
                Console.Error.WriteLine("★ 自证失败: " + code
                    + "  期望=[" + string.Join("|", want) + "]"
                    + "  实得=[" + string.Join("|", got) + "]");
            }
        }
        if (bad > 0)
        {
            Console.Error.WriteLine("★★ 字符串扫描器自证 " + bad + " 条不符 ⇒ **拒绝产出结果**");
            Environment.Exit(3);
        }
        Console.Error.WriteLine("✅ 字符串扫描器自证通过（" + cases.Length + " 用例）"
            + " ⚠ 已知局限：多行 `/* */` 注释内的引号串会被计入（反编译产物里量小）");
    }

    /// <summary>
    /// ★★ 2.16.117：IL **字节码行走器** —— 真正按指令边界走，不用正则猜。
    ///
    /// 借鉴 DFCN（`tools/pe_instruction_match.py` / `extract_*_sources.py`）：
    ///   他们在 x64 上要自己解 opcode 前缀 + ModRM + disp32 才能定位 RIP 相对操作数；
    ///   .NET IL 是**定长/查表**的 ⇒ 一张 operand-kind 表 + 一个指针就能走完，
    ///   **不需要反汇编器**。
    ///
    /// 产出每个指令的：(iL 偏移, 操作码, 操作数种类, 原始操作数字节)
    /// 上层据此抽 `ldstr` / `call` / `callvirt` / `newobj` / `switch` / 条件跳转。
    /// </summary>
    private static IEnumerable<(int Off, byte Op, int OpKind, byte[] Arg, int End)> WalkIl(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            int off = i;
            byte op = il[i++];
            if (op == 0xfe)
            {
                if (i >= il.Length) yield break;
                byte o2 = il[i++];
                // ★ 0xFE 段**不是**「一律无操作数」——
                //   ldftn/ldvirtftn/initobj/constrained./sizeof 带 token；
                //   ldarg/ldarga/starg/ldloc/ldloca/stloc 带 u16；unaligned./no. 带 u8。
                //   （漏项会让指针错位 ⇒ 静默丢半段，见 WalkIl 的自证）
                int fsz = FeOperandSize(o2);
                if (i + fsz > il.Length) yield break;
                var farg = new byte[fsz];
                Array.Copy(il, i, farg, 0, fsz);
                i += fsz;
                yield return (off, op, -o2, farg, i);
                continue;
            }
            int kind = OpKind(op);
            int sz = OperandSize(op, kind, il, i);
            if (sz < 0 || i + sz > il.Length) yield break;      // ★ 坏 IL/走飞 ⇒ 停止，不崩
            var arg = new byte[sz];
            Array.Copy(il, i, arg, 0, sz);
            i += sz;
            yield return (off, op, kind, arg, i);
        }
    }

    /// <summary>
    /// ★★ 0xFE 段（两字节 opcode）的操作数尺寸表 —— 按 ECMA-335 III 逐条核。
    ///
    /// 踩过的坑（2026-10-11）：原版只认 `0xfe15`/`0xfe16`（token）与 `0xfe09`（u16），
    /// **其余一律按「无操作数」**。而 `ldftn`(0xfe06) / `ldvirtftn`(0xfe07) 是
    /// **委托与方法组**最常用的指令 ⇒ 把 4 字节 token 当指令读 ⇒ 指针错位。
    /// </summary>
    private static int FeOperandSize(byte o2)
    {
        switch (o2)
        {
            // ── token（4 字节）──
            case 0x06: case 0x07:              // ldftn / ldvirtftn
            case 0x15: case 0x16:              // initobj / constrained.
            case 0x1c:                         // sizeof
                return 4;
            // ── u16（2 字节，长形局部变量/参数）──
            case 0x09: case 0x0a: case 0x0b:   // ldarg / ldarga / starg
            case 0x0c: case 0x0d: case 0x0e:   // ldloc / ldloca / stloc
                return 2;
            // ── u8（1 字节）──
            case 0x12:                         // unaligned.
            case 0x19:                         // no.
                return 1;
            // 其余（arglist/ceq/cgt/clt/ldftn 以外/…/volatile./tail./localloc/
            //       endfilter/cpblk/initblk/rethrow/refanytype/readonly.）无操作数
            default:
                return 0;
        }
    }

    /// <summary>操作数种类：0=无 1=i8 2=i16 4=i32 5=i64 6=r32/短跳转 7=switch
    /// 8=token 9=UserString-token。
    ///
    /// ★★ 本表**必须与 ECMA-335 逐条对齐**。踩过的坑（2026-10-10）：
    ///   ① 把无操作数指令（`ldc.i4.5` / `ldarg.0` / `dup` / `add` …）列进来
    ///      ⇒ 指针跑飞，`OverflowException`
    ///   ② 把 `ldc.r8` / `ldc.r4` 的 opcode 号搞混（正解：0x22=r4，0x23=r8）
    ///   ③ **漏项**（`box`/`castclass`/`isinst`/`ldtoken`/`sizeof`…）⇒ 指针错位
    ///      ⇒ 走到 `-1` **静默截断**，把一个方法后半段整段丢掉。
    ///      实测 **4,060 个方法被截断**、少收 **9,976 条**字面量 ⇒ 这才是主因。
    ///   ⇒ 现在带**自证**：`ForEachMethodIl` 统计「未走满 IL 体」的方法数 `nWalkBad`，
    ///      **必须为 0**；`ldstr` 的收尾会把它打出来。
    /// </summary>
    private static int OpKind(byte op)
    {
        if (op == 0x72) return 9;                                  // ldstr

        switch (op)
        {
            // ── 4 字节 token ──（漏一个就错位，逐条列全）
            case 0x27:                                                 // jmp        ★ 原缺
            case 0x28:                                                 // call
            case 0x29:                                                 // calli      ★ 原缺
            case 0x6f:                                                 // callvirt
            case 0x70:                                                 // cpobj      ★ 原缺
            case 0x71:                                                 // ldobj      ★ 原缺
            case 0x73: case 0x74: case 0x75:                           // newobj / castclass / isinst
            case 0x79:                                                 // unbox
            case 0x7b: case 0x7c: case 0x7d:                           // ldfld / ldflda / stfld
            case 0x7e: case 0x7f: case 0x80:                           // ldsfld / ldsflda / stsfld
            case 0x81:                                                 // stobj
            case 0x8c: case 0x8d: case 0x8f:                           // box / newarr / ldelema
            case 0xa3: case 0xa4: case 0xa5:                           // ldelem / stelem / unbox.any
            case 0xc2: case 0xc6: case 0xd0:                           // refanyval / mkrefany / ldtoken
                return 8;

            // ── 1 字节操作数 ──
            case 0x0e: case 0x0f: case 0x10:                           // ldarg.s / ldarga.s★ / starg.s
            case 0x11: case 0x12: case 0x13:                           // ldloc.s / ldloca.s / stloc.s
            case 0x1f:                                                 // ldc.i4.s   ★ 原写成 0x1e（错）
            case 0xde:                                                 // leave.s    ★ 原缺
                return 1;
        }

        // ── 短跳转（1 字节有符号相对偏移）──
        if (op >= 0x2b && op <= 0x37) return 6;

        if (op == 0x45) return 7;                                  // switch

        // ── 4 字节操作数 ──
        if (op == 0x20 || op == 0x22) return 4;                    // ldc.i4 / ldc.r4
        if (op >= 0x38 && op <= 0x44) return 4;                    // 长跳转
        if (op == 0xdd) return 4;                                  // leave      ★ 原缺

        // ── 8 字节操作数 ──
        if (op == 0x21 || op == 0x23) return 5;                    // ldc.i8 / ldc.r8

        return 0;                                                  // 其余全部无操作数
    }

    private static int OperandSize(byte op, int kind, byte[] il, int at)
    {
        switch (kind)
        {
            case 1: return 1;
            case 6: return 1;                          // ★ 短跳转 = **1 字节**（原与 ldc.r4 共用 kind 6 而返回 4，是硬错）
            case 2: return 2;
            case 4: case 8: case 9: return 4;
            case 5: return 8;
            case 7:
                if (at + 4 > il.Length) return -1;
                int n = BitConverter.ToInt32(il, at);
                if (n < 0 || n > 4096) return -1;          // ★ 不合理 ⇒ 判为坏 IL
                return 4 + n * 4;
            default: return 0;
        }
    }

    /// <summary>IL 内嵌分支指令的判别（与 OpKind 保持一致）。</summary>
    private static bool IsBranch(byte op)
    {
        return op == 0x2b || op == 0x2c || op == 0x2d || op == 0x2e || op == 0x2f
            || op == 0x30 || op == 0x31 || op == 0x32 || op == 0x33 || op == 0x34
            || op == 0x35 || op == 0x36 || op == 0x37 || op == 0x38 || op == 0x39
            || op == 0x3a || op == 0x3b || op == 0x3c || op == 0x3d || op == 0x3e
            || op == 0x3f || op == 0x40 || op == 0x41 || op == 0x42 || op == 0x43
            || op == 0x44 || op == 0x45;
    }

    /// <summary>
    /// ★★ 2.16.119：**IL 栈效应表** —— (弹出, 压入)。
    ///
    /// 用途：`callsites` 需要知道「调用点的实参分别由哪条指令压栈」。
    /// 基本块内是**直线代码** ⇒ 前向符号模拟即可精确还原（见 `Callsites()`）。
    ///
    /// ★ 返回 false = **本表没收录这个 opcode** ⇒ 调用方**必须放弃该基本块**，
    ///   绝不能按 (0,0) 蒙混过关 —— 那会让后面所有位置静默错位（§58 同族）。
    /// </summary>
    private static bool Effect(byte op, int kind, EntityHandle tok, out int pops, out int pushes)
    {
        pops = 0; pushes = 0;

        // ── 0xFE 段（kind = -第二字节）──
        if (op == 0xfe)
        {
            switch (-kind)
            {
                case 0x00: pushes = 1; return true;                       // arglist
                case 0x01: case 0x02: case 0x03: case 0x04: case 0x05:    // ceq/cgt/cgt.un/clt/clt.un
                    pops = 2; pushes = 1; return true;
                case 0x06: pushes = 1; return true;                       // ldftn
                case 0x07: pops = 1; pushes = 1; return true;             // ldvirtftn
                case 0x09: case 0x0a: pushes = 1; return true;            // ldarg / ldarga
                case 0x0b: pops = 1; return true;                         // starg
                case 0x0c: case 0x0d: pushes = 1; return true;            // ldloc / ldloca
                case 0x0e: pops = 1; return true;                         // stloc
                case 0x0f: pops = 1; pushes = 1; return true;             // localloc
                case 0x11: pops = 1; return true;                         // endfilter
                case 0x12: case 0x13: case 0x14: case 0x16: case 0x19:
                case 0x1e: return true;                                   // 前缀，无栈效应
                case 0x15: pops = 1; return true;                         // initobj
                case 0x17: case 0x18: pops = 3; return true;              // cpblk / initblk
                case 0x1a: return true;                                   // rethrow
                case 0x1c: pushes = 1; return true;                       // sizeof
                case 0x1d: pops = 1; pushes = 1; return true;             // refanytype
                default: return false;
            }
        }

        if (op <= 0x01) return true;                                      // nop / break
        // ★★ 2.16.119 修：`ldarg.0-3` **必须 pushes=1**。
        //   原来写成 `if (op <= 0x05) return true;` ⇒ 连 `ldarg.0-3` 一起吞成 (0,0)
        //   ⇒ 栈**少一个值** ⇒ 调用点的实参整体**下移一位**（实测 `_S(ctx,tpl,-40,out o)`
        //   被记成 arg1=ctx / arg2=tpl，`getLitTag` 的 IL offset 25 就是 `ldarg.1`）。
        //   ⚠ 这个错误**「ret 栈深 ≤ 1」的自证抓不到**（它只能抓多压，抓不到少压）
        //     ⇒ 已补 `nEntryUnderflow`（见 `Callsites()`）。
        if (op >= 0x02 && op <= 0x05) { pushes = 1; return true; }        // ldarg.0-3
        if (op >= 0x06 && op <= 0x09) { pushes = 1; return true; }        // ldloc.0-3
        if (op >= 0x0a && op <= 0x0d) { pops = 1; return true; }          // stloc.0-3
        if (op >= 0x15 && op <= 0x1e) { pushes = 1; return true; }        // ldc.i4.m1 .. ldc.i4.8
        if (op >= 0x38 && op <= 0x44) return true;                        // 长跳转（块尾）
        if (op >= 0x58 && op <= 0x64) { pops = 2; pushes = 1; return true; }  // add..shr.un
        if (op >= 0x67 && op <= 0x6e) { pops = 1; pushes = 1; return true; }  // conv.*
        if (op >= 0x82 && op <= 0x8b) { pops = 1; pushes = 1; return true; }  // conv.ovf.*
        if (op >= 0x90 && op <= 0x9a) { pops = 2; pushes = 1; return true; }  // ldelem.*
        if (op >= 0x9b && op <= 0xa2) { pops = 3; return true; }              // stelem.*
        if (op >= 0xb3 && op <= 0xba) { pops = 1; pushes = 1; return true; }  // conv.ovf.*
        if (op >= 0xd1 && op <= 0xda) { pops = 1; pushes = 1; return true; }  // conv.ovf.*.un
        if (op >= 0x2b && op <= 0x37) return true;                        // 短跳转（块尾）

        switch (op)
        {
            case 0x0e: case 0x0f: case 0x11: case 0x12: pushes = 1; return true;
            case 0x10: case 0x13: pops = 1; return true;
            case 0x14: pushes = 1; return true;                           // ldnull
            case 0x1f: case 0x20: case 0x21: case 0x22: case 0x23: pushes = 1; return true;
            case 0x25: pops = 1; pushes = 2; return true;                 // dup
            case 0x26: pops = 1; return true;                             // pop
            case 0x27: return true;                                       // jmp（块尾）
            case 0x28: case 0x6f:                                        // call / callvirt
            {
                if (!TrySig(tok, out int n, out bool isInst, out bool rv)) return false;
                pops = n + (isInst ? 1 : 0);
                pushes = rv ? 0 : 1;
                return true;
            }
            case 0x73:                                                   // newobj
            {
                if (!TrySig(tok, out int n, out _, out _)) return false;
                pops = n; pushes = 1; return true;
            }
            case 0x29: return false;                                      // calli（罕见，放弃）
            case 0x2a: return true;                                       // ret（块尾）
            case 0x45: pops = 1; return true;                             // switch（块尾）
            case 0x46: case 0x47: case 0x48: case 0x49: case 0x4a:
            case 0x4b: case 0x4c: case 0x4d: case 0x4e: case 0x4f: case 0x50:
                pops = 1; pushes = 1; return true;                        // ldind.*
            case 0x51: case 0x52: case 0x53: case 0x54: case 0x55:
            case 0x56: case 0x57: case 0xdf: pops = 2; return true;       // stind.*
            case 0x65: case 0x66: pops = 1; pushes = 1; return true;      // neg / not
            case 0x70: pops = 2; return true;                             // cpobj
            case 0x71: pops = 1; pushes = 1; return true;                 // ldobj
            case 0x72: pushes = 1; return true;                           // ldstr
            case 0x74: case 0x75: pops = 1; pushes = 1; return true;      // castclass / isinst
            case 0x76: pops = 1; pushes = 1; return true;                 // conv.r.un
            case 0x79: pops = 1; pushes = 1; return true;                 // unbox
            case 0x7a: pops = 1; return true;                             // throw
            case 0x7b: case 0x7c: pops = 1; pushes = 1; return true;      // ldfld / ldflda
            case 0x7d: pops = 2; return true;                             // stfld
            case 0x7e: case 0x7f: pushes = 1; return true;                // ldsfld / ldsflda
            case 0x80: pops = 1; return true;                             // stsfld
            case 0x81: pops = 2; return true;                             // stobj
            case 0x8c: pops = 1; pushes = 1; return true;                 // box
            case 0x8d: pops = 1; pushes = 1; return true;                 // newarr
            case 0x8e: pops = 1; pushes = 1; return true;                 // ldlen
            case 0x8f: pops = 2; pushes = 1; return true;                 // ldelema
            case 0xa3: pops = 2; pushes = 1; return true;                 // ldelem
            case 0xa4: pops = 3; return true;                             // stelem
            case 0xa5: pops = 1; pushes = 1; return true;                 // unbox.any
            case 0xc2: case 0xc3: case 0xc6: pops = 1; pushes = 1; return true;
            case 0xcc: return false;
            case 0xd0: pushes = 1; return true;                           // ldtoken
            case 0xdc: return true;                                       // endfinally
            case 0xdd: case 0xde: return true;                            // leave / leave.s
            default: return false;                                        // ★ 未收录 ⇒ 放弃该块
        }
    }

    /// <summary>
    /// ★★ 2.16.119：`callsites` —— **参数级**调用点表：哪个字面量喂给了谁的第几个参数。
    ///
    /// 做法（**基本块内前向符号模拟**）：
    ///   · 基本块内是直线代码 ⇒ 栈行为可精确模拟，**不需要数据流迭代**
    ///   · 栈只记「**本块内压入的已知值**」（`ldstr` 记字面量，其余记 null）：
    ///       下溢 ⇒ 该值来自本块之前，返回 null（位置仍然对得上，因为在块内它是栈顶）
    ///     ★ 不要用「负深度」编码未知前缀 —— 那会把**新压入的值也算成未知**（第一版错在这）
    ///   · 异常处理器入口必然紧跟 `leave`/`leave.s` ⇒ 已被「跳转的下一条」规则
    ///     自动标成块起点，栈随之清空（不会带着 try 块的残留栈漂进 catch）
    ///   · 遇到 `Effect()` 未收录的 opcode ⇒ **整块放弃**（绝不按 (0,0) 蒙混）
    ///
    /// 自证（stderr）：
    ///   ① 因 opcode 未收录而放弃的块数
    ///   ② 每个 `ret` 处「本块已知栈深」必须 ≤ 1（**必要**条件；>1 必是模拟错）
    ///   ③ 在 Python 侧另做**数据驱动**验证：`_S`/`_T` 的 arg0（Context）
    ///      应绝大多数含 `" - "`（这是本项目已实证的官方约定）
    ///
    /// 输出（TSV）：`type / member / il_offset / callee / arg_index / literal`
    ///   · 只输出**至少有一个实参是 `ldstr`** 的调用点
    ///   · `arg_index` 从 0 起（实例方法的 receiver 不输出）
    /// 用法：`decomp callsites > callsites.tsv`
    /// </summary>
    private static int Callsites()
    {
        Console.WriteLine("type\tmember\til_offset\tcallee\targ_index\tliteral");
        int nRow = 0, nBlockSkip = 0, nRet = 0, nRetBad = 0, nCall = 0, nNoSig = 0;
        int nEntryUnderflow = 0;
        var ufSample = new List<string>();
        var sigCache = new Dictionary<int, int[]>();

        ForEachMethodIl((type, member, il) =>
        {
            var ins = WalkIl(il).ToList();
            if (ins.Count == 0) return;

            // ── 基本块起点：0 · 跳转目标 · 跳转/ret/leave/endfinally 的下一条 ──
            var leaders = new HashSet<int> { ins[0].Off };
            for (int k = 0; k < ins.Count; k++)
            {
                var cur = ins[k];
                if (cur.Op == 0x45 && cur.OpKind == 7)
                {
                    for (int j = 0, cnt = BitConverter.ToInt32(cur.Arg, 0); j < cnt; j++)
                        leaders.Add(cur.Off + cur.Arg.Length + BitConverter.ToInt32(cur.Arg, 4 + j * 4));
                }
                else if (cur.OpKind == 6 || (cur.Op >= 0x38 && cur.Op <= 0x44))
                {
                    int rel = cur.OpKind == 6 ? (sbyte)cur.Arg[0] : BitConverter.ToInt32(cur.Arg, 0);
                    leaders.Add(cur.Off + cur.Arg.Length + rel);
                }
                // ★ 块尾：分支 / ret / leave / endfinally / **throw**
                //   `throw` 之后的代码是不可达的，**栈状态必须重置**
                //   （漏了它 ⇒ 后续不可达代码会带着残留栈继续算 ⇒ 实测多出 1 个入口块弹空）
                if ((IsBranch(cur.Op) || cur.Op == 0x2a || cur.Op == 0xdd
                     || cur.Op == 0xde || cur.Op == 0xdc || cur.Op == 0x7a)
                    && k + 1 < ins.Count)
                    leaders.Add(ins[k + 1].Off);
            }

            var known = new List<string>();
            bool blockOk = true;
            bool entryBlock = true;      // 只有 IL 偏移 0 所在的那个块，入口栈**确切为空**

            for (int k = 0; k < ins.Count; k++)
            {
                var cur = ins[k];
                if (k == 0 || leaders.Contains(cur.Off))
                {
                    known.Clear();
                    blockOk = true;
                    entryBlock = (k == 0);
                }
                if (!blockOk) continue;

                if (cur.Op == 0x72) { known.Add(StringsAtToken(BitConverter.ToInt32(cur.Arg, 0))); continue; }

                if (cur.Op == 0x28 || cur.Op == 0x6f || cur.Op == 0x73)
                {
                    int tok = BitConverter.ToInt32(cur.Arg, 0);
                    if (!sigCache.TryGetValue(tok, out var sg))
                    {
                        sg = TrySig(MetadataTokens.EntityHandle(tok), out int n0, out bool th, out bool rv)
                             ? new[] { n0, th ? 1 : 0, rv ? 1 : 0, 0 } : new[] { 0, 0, 0, 1 };
                        sigCache[tok] = sg;
                    }
                    if (sg[3] == 1) { nNoSig++; blockOk = false; continue; }

                    nCall++;
                    int nArgs = sg[0], hasThis = sg[1], retVoid = sg[2];
                    bool isNewobj = cur.Op == 0x73;
                    int recv = (!isNewobj && hasThis == 1) ? 1 : 0;
                    int nPop = nArgs + recv;
                    // ★ 调用点也要查入口块下溢（原来只查了 Effect 路径 ⇒ 漏掉这一处）
                    if (entryBlock && nPop > known.Count)
                    {
                        nEntryUnderflow++;
                        if (ufSample.Count < 6)
                            ufSample.Add(type + "::" + member + "  off=0x"
                                + cur.Off.ToString("x4") + "  CALL " + TokenName(tok)
                                + "  nPop=" + nPop + "  known=" + known.Count);
                    }
                    var vals = new string[nPop];
                    for (int j = nPop - 1; j >= 0; j--) vals[j] = Pop(known);
                    for (int j = 0; j < nArgs; j++)
                    {
                        string v = vals[j + recv];
                        if (v == null) continue;
                        Console.WriteLine(Esc(type) + "\t" + Esc(member) + "\t0x"
                            + cur.Off.ToString("x4") + "\t" + Esc(TokenName(tok)) + "\t"
                            + j + "\t" + Esc(v));
                        nRow++;
                    }
                    if (isNewobj || retVoid == 0) known.Add(null);
                    continue;
                }

                if (cur.Op == 0x2a)                       // ret
                {
                    nRet++;
                    if (known.Count > 1) nRetBad++;       // ★ 必要条件的违反者
                    continue;
                }

                int argTok = cur.Arg.Length == 4 ? BitConverter.ToInt32(cur.Arg, 0) : 0;
                if (!Effect(cur.Op, cur.OpKind, MetadataTokens.EntityHandle(argTok),
                            out int pp, out int qq))
                {
                    blockOk = false; nBlockSkip++;
                    continue;
                }
                // ★★ 双向自证：**入口块**（IL 偏移 0 所在块）的栈**确切为空**
                //   ⇒ 这里任何一次「弹空」都是真错。`ldarg` 少压一个值这类 bug
                //     只有这个计数器抓得到（「ret 栈深」只抓多压）。
                if (entryBlock && pp > known.Count)
                {
                    nEntryUnderflow++;
                    if (ufSample.Count < 6)
                        ufSample.Add(type + "::" + member + "  off=0x"
                            + cur.Off.ToString("x4") + "  op=0x" + cur.Op.ToString("x2")
                            + "  opKind=" + cur.OpKind + "  pops=" + pp + "  known=" + known.Count);
                }
                for (int j = 0; j < pp; j++) Pop(known);
                for (int j = 0; j < qq; j++) known.Add(null);
            }
        });

        Console.Error.WriteLine("共 " + nRow + " 条「实参含字面量」的调用点 · 扫描 " + nCall + " 个调用点");
        Console.Error.WriteLine("★ 因 opcode 未收录而放弃的基本块 = " + nBlockSkip
            + " · 因签名解不出而放弃 = " + nNoSig);
        Console.Error.WriteLine("★ **入口块弹空 = " + nEntryUnderflow
            + "（必须 = 0；>0 ⇒ 某条指令栈效应记错，实参编号会整体下移）**");
        Console.Error.WriteLine("★ ret 处「本块已知栈深 > 1」= " + nRetBad + " / " + nRet
            + "（只抓「多压」；「少压」靠上一行）");
        foreach (var s in ufSample) Console.Error.WriteLine("   · 入口弹空 " + s);
        return 0;
    }

    /// <summary>
    /// ★★ 2.16.119：`sig &lt;callee 片段&gt;` —— 把某类 callee 的**真实签名**打出来
    /// （参数个数 / 是否实例 / 是否返回 void / 参数名）。
    ///
    /// 为什么需要它：`callsites` 的 `arg_index` 依赖 `TrySig` 算出的**参数个数**。
    /// 同一个 callee 名若有**重载**（如 `_S` 有 1/2/3/4/5 参多个版本），
    /// 只看名字**无法判断**某个调用点用的是哪一个 ⇒ 参数号可能整体错位。
    /// 判据：**同一 callee 名出现多于一种 (nArgs, hasThis) ⇒ 存在重载 ⇒ 按名字取参数是不可靠的。**
    /// </summary>
    private static int Sig(string frag)
    {
        var seen = new Dictionary<string, HashSet<string>>();
        ForEachMethodIl((type, member, il) =>
        {
            foreach (var cur in WalkIl(il))
            {
                if (cur.Op != 0x28 && cur.Op != 0x6f && cur.Op != 0x73) continue;
                int tok = BitConverter.ToInt32(cur.Arg, 0);
                string name = TokenName(tok);
                if (name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!TrySig(MetadataTokens.EntityHandle(tok), out int n, out bool th, out bool rv))
                {
                    seen.TryAdd(name, new HashSet<string>());
                    seen[name].Add("<签名解不出>");
                    continue;
                }
                seen.TryAdd(name, new HashSet<string>());
                seen[name].Add("nArgs=" + n + " hasThis=" + (th ? 1 : 0) + " retVoid=" + (rv ? 1 : 0));
            }
        });
        Console.WriteLine("=== 匹配「" + frag + "」的 callee 及其签名 ===");
        foreach (var kv in seen.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("  " + kv.Key);
            foreach (var s in kv.Value.OrderBy(x => x, StringComparer.Ordinal))
                Console.WriteLine("      " + s
                    + (kv.Value.Count > 1 ? "   ← ★ 有重载：按名字取参数**不可靠**" : ""));
        }
        return 0;
    }

    /// <summary>从「本块已知值栈」弹一个；空则返回 null（= 该值来自本块之前）。</summary>
    private static string Pop(List<string> known)
    {
        if (known.Count == 0) return null;
        string v = known[known.Count - 1];
        known.RemoveAt(known.Count - 1);
        return v;
    }

    private static string OpName(byte op)
    {
        switch (op)
        {
            case 0x2b: return "br.s";     case 0x2c: return "brfalse.s";
            case 0x2d: return "brtrue.s"; case 0x2e: return "beq.s";
            case 0x2f: return "bge.s";    case 0x30: return "bgt.s";
            case 0x31: return "ble.s";    case 0x32: return "blt.s";
            case 0x33: return "bne.un.s"; case 0x34: return "bge.un.s";
            case 0x35: return "bgt.un.s"; case 0x36: return "ble.un.s";
            case 0x37: return "blt.un.s"; case 0x38: return "br";
            case 0x39: return "brfalse";  case 0x3a: return "brtrue";
            case 0x3b: return "beq";      case 0x3c: return "bge";
            case 0x3d: return "bgt";      case 0x3e: return "ble";
            case 0x3f: return "blt";      case 0x40: return "bne.un";
            case 0x41: return "bge.un";   case 0x42: return "bgt.un";
            case 0x43: return "ble.un";   case 0x44: return "blt.un";
            case 0x45: return "switch";
            case 0x28: return "call";     case 0x6f: return "callvirt";
            case 0x73: return "newobj";   case 0x72: return "ldstr";
            default: return "0x" + op.ToString("x2");
        }
    }

    /// <summary>把元数据 token 解成可读名字（挂在 CSharpDecompiler 的 TypeSystem 上）。</summary>
    private static string TokenName(int token)
    {
        try
        {
            var h = MetadataTokens.EntityHandle(token);
            switch (h.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var mr = _md.GetMemberReference((MemberReferenceHandle)h);
                    return NameOf(mr.Parent) + "::" + _md.GetString(mr.Name);
                }
                case HandleKind.MethodDefinition:
                {
                    var md2 = _md.GetMethodDefinition((MethodDefinitionHandle)h);
                    return NameOf(md2.GetDeclaringType()) + "::" + _md.GetString(md2.Name);
                }
                case HandleKind.MethodSpecification:
                {
                    var ms = _md.GetMethodSpecification((MethodSpecificationHandle)h);
                    return TokenName(MetadataTokens.GetToken(ms.Method));
                }
                case HandleKind.FieldDefinition:
                {
                    var fd = _md.GetFieldDefinition((FieldDefinitionHandle)h);
                    return NameOf(fd.GetDeclaringType()) + "::" + _md.GetString(fd.Name);
                }
                case HandleKind.TypeReference:
                    return NameOf(h);
                case HandleKind.TypeDefinition:
                    return NameOf((TypeDefinitionHandle)h);
                case HandleKind.TypeSpecification:
                    // ★★ 2.16.119：原来返回字面量 `<typespec>` ⇒ **所有泛型类的调用
                    //   全糊成同一个名字**（实测 `TypeSpecification::Add` 掩盖了
                    //   **10,235** 次调用、`TypeSpecification::_T` 掩盖 1,818 次
                    //   —— 其中就有真正的文本出口 `_S` / `PlayerMessage` / `EmitMessage`）。
                    //   ⇒ 必须真解 TypeSpec 签名。
                    return TypeNameOf(h);
                default:
                    return h.Kind.ToString();
            }
        }
        catch { return "<token 0x" + token.ToString("x8") + ">"; }
    }

    /// <summary>
    /// ★★ 2.16.119：类型签名解码器 —— 只把类型渲染成名字，不建类型系统。
    /// 用途：① 解 `TypeSpecification`（泛型实例化）② 数方法参数个数 / 看是否返回 void。
    /// </summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object>
    {
        public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
            => NameOf(h);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
            => NameOf((EntityHandle)h);
        public string GetTypeFromSpecification(MetadataReader r, object g,
            TypeSpecificationHandle h, byte k) => "<spec>";
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, ArrayShape s) => e + "[]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetPointerType(string e) => e + "*";
        public string GetGenericInstantiation(string g, ImmutableArray<string> a)
            => g + "<" + string.Join(",", a) + ">";
        public string GetGenericMethodParameter(object g, int i) => "!!" + i;
        public string GetGenericTypeParameter(object g, int i) => "!" + i;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetModifiedType(string mod, string un, bool req) => un;
        public string GetPinnedType(string e) => e;
    }

    private static readonly NameProvider _np = new NameProvider();

    /// <summary>把类型 token（含 TypeSpecification）解成可读名。</summary>
    private static string TypeNameOf(EntityHandle h)
    {
        try
        {
            if (h.Kind == HandleKind.TypeSpecification)
                return _md.GetTypeSpecification((TypeSpecificationHandle)h)
                          .DecodeSignature(_np, null);
            return NameOf(h);
        }
        catch { return "<typeref?>"; }
    }

    /// <summary>方法 token → (参数个数, 是否实例方法, 是否返回 void)。失败返回 false。</summary>
    private static bool TrySig(EntityHandle h, out int nArgs, out bool hasThis, out bool retVoid)
    {
        nArgs = 0; hasThis = false; retVoid = true;
        try
        {
            // ★ MethodSpecification 的签名是**泛型实参表**（ImmutableArray<string>），
            //   不是方法签名 ⇒ 必须回退到它引用的那个方法再解（踩过：CS0029）。
            if (h.Kind == HandleKind.MethodSpecification)
                h = _md.GetMethodSpecification((MethodSpecificationHandle)h).Method;

            MethodSignature<string> sig;
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    sig = _md.GetMethodDefinition((MethodDefinitionHandle)h)
                             .DecodeSignature(_np, null);
                    break;
                case HandleKind.MemberReference:
                    sig = _md.GetMemberReference((MemberReferenceHandle)h)
                             .DecodeMethodSignature(_np, null);
                    break;
                default:
                    return false;
            }
            nArgs = sig.ParameterTypes.Length;
            hasThis = sig.Header.IsInstance;
            retVoid = sig.ReturnType == "Void";
            return true;
        }
        catch { return false; }
    }

    private static string NameOf(TypeDefinitionHandle t)
    {
        try
        {
            var td = _md.GetTypeDefinition(t);
            string ns = _md.GetString(td.Namespace);
            string nm = _md.GetString(td.Name);
            return ns.Length > 0 ? ns + "." + nm : nm;
        }
        catch { return "?"; }
    }

    private static string NameOf(EntityHandle h)
    {
        if (h.Kind == HandleKind.TypeDefinition)
            return NameOf((TypeDefinitionHandle)h);
        if (h.Kind == HandleKind.TypeReference)
        {
            try
            {
                var tr = _md.GetTypeReference((TypeReferenceHandle)h);
                string ns = _md.GetString(tr.Namespace);
                string nm = _md.GetString(tr.Name);
                return ns.Length > 0 ? ns + "." + nm : nm;
            }
            catch { return "?"; }
        }
        // ★★ 2.16.119：`MemberReference.Parent` **经常是 TypeSpecification**
        //   （泛型类上的方法引用）⇒ 这里必须也解 TypeSpec，
        //   否则 `TokenName` 拼出来的是 `TypeSpecification::Add` 这种**丢失宿主**的名字。
        if (h.Kind == HandleKind.TypeSpecification)
            return TypeNameOf(h);
        return h.Kind.ToString();
    }

    /// <summary>
    /// 开一次元数据读器，遍历全部 MethodDef 的 IL，交给 <paramref name="visit"/>。
    /// visit(typeFullName, methodName, ilBytes)
    /// </summary>
    private static void ForEachMethodIl(Action<string, string, byte[]> visit)
    {
        using var fs = File.OpenRead(Asm);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        _md = md;
        foreach (var th in md.TypeDefinitions)
        {
            string full = FullNameOf(md, th);
            var td = md.GetTypeDefinition(th);
            foreach (var mh in td.GetMethods())
            {
                try
                {
                    var mdf = md.GetMethodDefinition(mh);
                    var body = pe.GetMethodBody(mdf.RelativeVirtualAddress);
                    if (body == null) continue;
                    var il = body.GetILBytes();
                    if (il == null || il.Length == 0) continue;
                    // ★ 自证：行走器必须**走满整个 IL 体**；中途停 = 表有漏项（静默丢半段）
                    nMethod++;
                    int lastEnd = 0;
                    foreach (var ins in WalkIl(il)) lastEnd = ins.End;
                    if (lastEnd != il.Length)
                    {
                        nWalkBad++;
                        if (walkBadSample.Count < 12)
                            walkBadSample.Add(full + "::" + md.GetString(mdf.Name)
                                + "  ilLen=" + il.Length + " 走到=" + lastEnd);
                    }
                    visit(full, md.GetString(mdf.Name), il);
                }
                catch { /* ★ 坏 IL / 抽象方法 ⇒ 跳过，不中断全局扫描 */ }
            }
        }
    }

    /// <summary>
    /// ★★ 2.16.118：**嵌套感知的全名** —— 元数据里嵌套类型的 `Namespace` 是空的，
    /// 只知道简单名（`Bar`），直接拼会得到 `Bar` 而不是 `XRL.Foo.Bar`
    /// ⇒ 必须沿 `GetDeclaringType()` 逐层往上拼。
    /// </summary>
    private static string FullNameOf(MetadataReader md, TypeDefinitionHandle th)
    {
        var td = md.GetTypeDefinition(th);
        string nm = md.GetString(td.Name);
        var d = td.GetDeclaringType();
        if (d.IsNil)
        {
            string ns = md.GetString(td.Namespace);
            return ns.Length > 0 ? ns + "." + nm : nm;
        }
        return FullNameOf(md, d) + "." + nm;
    }

    /// <summary>
    /// ★★ 2.16.118：`il &lt;类型&gt;::&lt;成员&gt;` —— 把某个方法的 IL **逐条指令**打出来
    /// （偏移 / opcode / operand-kind / 操作数长度 / 结束偏移）。
    /// 用途：行走器走不满时，用它找**分歧点**（第一条「不可能是真 opcode」的字节）。
    /// </summary>
    private static int IlDump(string spec)
    {
        int k = spec.LastIndexOf("::", StringComparison.Ordinal);
        if (k < 0) { Console.Error.WriteLine("用法: decomp il <类型>::<成员>"); return 2; }
        string wantType = spec.Substring(0, k), wantMember = spec.Substring(k + 2);

        using var fs = File.OpenRead(Asm);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        foreach (var th in md.TypeDefinitions)
        {
            string full = FullNameOf(md, th);
            if (!full.EndsWith(wantType, StringComparison.Ordinal)) continue;
            var td = md.GetTypeDefinition(th);
            foreach (var mh in td.GetMethods())
            {
                var mdf = md.GetMethodDefinition(mh);
                string mn = md.GetString(mdf.Name);
                if (mn != wantMember) continue;
                var body = pe.GetMethodBody(mdf.RelativeVirtualAddress);
                if (body == null) continue;
                var il = body.GetILBytes();
                Console.WriteLine("// " + full + "::" + mn + "  ilLen=" + il.Length);
                int last = 0;
                foreach (var (off, op, kind, arg, end) in WalkIl(il))
                {
                    Console.WriteLine("  {0,6}  {1,-4} kind={2,-3} argLen={3}  end={4}",
                        off, op.ToString("x2"), kind, arg.Length, end);
                    last = end;
                }
                Console.WriteLine("// 走到 " + last + " / " + il.Length
                    + (last == il.Length ? "  ✅ 走满" : "  ❌ 停在偏移 " + last));
                if (last != il.Length)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int q = Math.Max(0, last - 6); q < Math.Min(il.Length, last + 10); q++)
                        sb.Append(il[q].ToString("x2")).Append(' ');
                    Console.WriteLine("// 该处字节: " + sb);
                }
                return 0;
            }
        }
        Console.Error.WriteLine("找不到: " + spec);
        return 1;
    }

    /// <summary>
    /// ★★ 2.16.118：`diag` —— 口径自证。不产出数据，只回答
    /// 「走到多少、跳了多少、走飞多少」，用来**判定计数口径本身对不对**
    /// （§31/§32：先验证「计数方法」，再验证「数据」）。
    /// </summary>
    private static int Diag()
    {
        using var fs = File.OpenRead(Asm);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        int nAll = 0, nGenerated = 0, nTopGenerated = 0, nNested = 0;
        foreach (var th in md.TypeDefinitions)
        {
            nAll++;
            var td = md.GetTypeDefinition(th);
            string nm = md.GetString(td.Name);
            bool gen = nm.Length > 0 && nm[0] == '<';
            if (gen) nGenerated++;
            if (!td.GetDeclaringType().IsNil) nNested++;
            else if (gen) nTopGenerated++;
        }
        Console.WriteLine("TypeDef 总数（含嵌套）      " + nAll);
        Console.WriteLine("  ├ 嵌套类型                " + nNested);
        Console.WriteLine("  └ 名字以 '<' 开头（生成） " + nGenerated);
        Console.WriteLine("     其中是**顶层**的       " + nTopGenerated);

        int withIl = 0, bad = 0;
        var samples = new List<string>();
        foreach (var th in md.TypeDefinitions)
        {
            string full = FullNameOf(md, th);
            var td = md.GetTypeDefinition(th);
            foreach (var mh in td.GetMethods())
            {
                try
                {
                    var mdf = md.GetMethodDefinition(mh);
                    var body = pe.GetMethodBody(mdf.RelativeVirtualAddress);
                    if (body == null) continue;
                    var il = body.GetILBytes();
                    if (il == null || il.Length == 0) continue;
                    withIl++;
                    int lastEnd = 0;
                    foreach (var ins in WalkIl(il)) lastEnd = ins.End;
                    if (lastEnd != il.Length)
                    {
                        bad++;
                        if (samples.Count < 15)
                        {
                            var sb = new System.Text.StringBuilder();
                            for (int k = Math.Max(0, lastEnd - 4);
                                 k < Math.Min(il.Length, lastEnd + 8); k++)
                                sb.Append(il[k].ToString("x2")).Append(' ');
                            samples.Add(full + "::" + md.GetString(mdf.Name)
                                + "  ilLen=" + il.Length + " 走到=" + lastEnd
                                + "  停在偏移 " + lastEnd + " 的字节: " + sb);
                        }
                    }
                }
                catch { }
            }
        }
        Console.WriteLine("有 IL 体的方法               " + withIl);
        Console.WriteLine("★ 未走满 IL 体的方法        " + bad
            + (bad == 0 ? "  ✅" : "  ❌（operand 表仍有漏项）"));
        foreach (var s in samples) Console.WriteLine("   · " + s);
        return 0;
    }

    private static MetadataReader _md;
    private static int nWalkBad;
    private static int nMethod;
    private static readonly List<string> walkBadSample = new List<string>();

    /// <summary>
    /// ★★ 2.16.117：`calls` —— 全程序集 IL 调用链（TSV：type / member / il_offset / op / callee）。
    /// 对应 DFCN 的 `native-calls.tsv`（「这个方法调了谁」= 句子拼装链）。
    /// </summary>
    private static int Calls()
    {
        Console.WriteLine("type\tmember\til_offset\top\tcallee");
        int n = 0;
        ForEachMethodIl((type, member, il) =>
        {
            foreach (var (off, op, kind, arg, end) in WalkIl(il))
            {
                if (kind != 8) continue;
                if (op != 0x28 && op != 0x6f && op != 0x73) continue;
                int tok = BitConverter.ToInt32(arg, 0);
                Console.WriteLine(Esc(type) + "\t" + Esc(member) + "\t0x"
                    + off.ToString("x4") + "\t" + OpName(op) + "\t" + Esc(TokenName(tok)));
                n++;
            }
        });
        Console.Error.WriteLine("共 " + n + " 个调用点");
        return 0;
    }

    /// <summary>
    /// ★★ 2.16.117：`branches` —— 全程序集 IL 分支 + 每条分支路径上的字面量。
    ///
    /// 对应 DFCN 的 `native-branches.tsv` / `native-switches.tsv`
    /// —— 「这个界面的文本有哪几个变体分支」。
    ///
    /// 产出（TSV）：
    ///   `type / member / il_offset / branch / target_il / block_literals`
    ///   · `target_il` = 跳转目标（相对偏移已换成绝对 IL 偏移）
    ///   · `block_literals` = **该分支目标块内**出现的 `ldstr`（` ‖ ` 分隔）
    ///     ⇒ 直接回答「走这一路会看到哪几句文本」
    ///   · `branch=switch#k` 表示 switch 的第 k 个 case
    ///
    /// 已知边界：分支目标只取**向前**的落点块；`switch` 的 default 落在表后，不算 case。
    /// </summary>
    private static int Branches()
    {
        Console.WriteLine("type\tmember\til_offset\tbranch\ttarget_il\tblock_text");
        int n = 0;
        ForEachMethodIl((type, member, il) =>
        {
            var ins = WalkIl(il).ToList();

            // ★★ 关键签名：`_T(Context, Template)` —— Context 必须**以 " - " 开头**
            //    （全部 100% 是 "Liquid Volume - xxx" / "Physics - xxx" 这种前缀）。
            //    相邻的 ldstr 里，**不带 " - " 的那个就是模板本体**。
            //    ⚠ 为什么必须这样挑（实测教训）：
            //      只抓「分支块里的裸 ldstr」会把 `.Split(' ')` 的**单词表**
            //      （`volume` / `preposition` / `forSort`）当成界面文本 —— 那是构建
            //      `=volume.liquid.name=` 这类槽的原料，**不是**可译文本。
            // ★ 宽度可变的配对窗口：`_T` 的参数压栈序列里，两条 ldstr 之间
            //    可能夹着 `ldtoken`（泛型实例化）等指令，**间距不固定**（实测踩到：
            //    写死"相隔 3 条"⇒ 0 命中）。⇒ 在 k 后面 1..7 条内找配对的 ldstr，
            //    且要求这对之后紧跟 `call`/`callvirt`。
            var siteAt = new Dictionary<int, string>();   // 指令下标 → 该 callsite 的模板
            var ctxAt = new Dictionary<int, string>();    // 指令下标 → 该 callsite 的 Context
            for (int k = 0; k + 2 < ins.Count; k++)
            {
                if (ins[k].Op != 0x72) continue;
                for (int d = 1; d <= 7 && k + d + 1 < ins.Count; d++)
                {
                    if (ins[k + d].Op != 0x72) continue;
                    byte nx = ins[k + d + 1].Op;
                    if (nx != 0x28 && nx != 0x6f) continue;      // call / callvirt
                    string a = StringsAtToken(BitConverter.ToInt32(ins[k].Arg, 0));
                    string b = StringsAtToken(BitConverter.ToInt32(ins[k + d].Arg, 0));
                    if (a == null || b == null) continue;
                    bool aCtx = a.Contains(" - ");
                    bool bCtx = b.Contains(" - ");
                    if (aCtx == bCtx) continue;                  // 必须恰好一个是 Context
                    siteAt[k] = aCtx ? b : a;
                    ctxAt[k] = aCtx ? a : b;
                    break;
                }
            }

            // 预先收集：每个 IL 偏移处若有 ldstr，记下来（供块内查询）
            var litAt = new Dictionary<int, string>();
            foreach (var (off, op, kind, arg, end) in ins)
                if (op == 0x72 && kind == 9 && arg.Length == 4)
                    litAt[off] = StringsAtToken(BitConverter.ToInt32(arg, 0)) ?? "";

            // 每个偏移 → 指令下标（用于找「目标所在的那条指令」）
            var idxAt = new Dictionary<int, int>();
            for (int k = 0; k < ins.Count; k++) idxAt[ins[k].Off] = k;

            foreach (var (off, op, kind, arg, end) in ins)
            {
                if (!IsBranch(op)) continue;
                var targets = new List<(string Label, int Target)>();
                if (op == 0x45)                                  // switch
                {
                    int cnt = BitConverter.ToInt32(arg, 0);
                    for (int k = 0; k < cnt; k++)
                    {
                        int rel = BitConverter.ToInt32(arg, 4 + k * 4);
                        targets.Add(("switch#" + k, off + arg.Length + rel));
                    }
                }
                else
                {
                    int rel = kind == 6 ? (sbyte)arg[0] : BitConverter.ToInt32(arg, 0);
                    targets.Add((OpName(op), off + arg.Length + rel));
                }

                foreach (var (label, tgt) in targets)
                {
                    // 目标块 = 从目标指令起，直到「跳到别处」或「函数结束」为止
                    var lits = new List<string>();
                    if (idxAt.TryGetValue(tgt, out int start))
                    {
                        for (int k = start; k < ins.Count; k++)
                        {
                            var cur = ins[k];
                            if (cur.Off != tgt && (IsBranch(cur.Op) || cur.Op == 0x2a)) break;
                            // ★ 优先取 `_T(Context, 模板)` callsite 的模板；否则退回裸 ldstr
                            string add = null;
                            if (siteAt.TryGetValue(k, out var tv) && tv.Length > 0)
                                add = tv;
                            else if (litAt.TryGetValue(cur.Off, out var lv) && lv.Length > 0)
                                add = lv;
                            if (add != null && !lits.Contains(add)) lits.Add(add);   // ★ 去重
                            if (cur.Op == 0x2a) break;                 // ret 收尾
                        }
                    }
                    Console.WriteLine(Esc(type) + "\t" + Esc(member) + "\t0x"
                        + off.ToString("x4") + "\t" + label + "\t0x"
                        + tgt.ToString("x4") + "\t" + Esc(string.Join(" ‖ ", lits)));
                    n++;
                }
            }
        });
        Console.Error.WriteLine("共 " + n + " 条分支目标");
        return 0;
    }

    /// <summary>按 UserString token 取字符串（token 的低 24 位就是 heap 偏移）。</summary>
    private static string StringsAtToken(int token)
    {
        try { return _md.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)); }
        catch { return null; }
    }


    /// <summary>判断是否编译器生成的类型（含嵌套链上的任意一层）。</summary>
    private static bool IsGenerated(ITypeDefinition t)
    {
        for (ITypeDefinition cur = t; cur != null; cur = cur.DeclaringTypeDefinition)
        {
            string n = cur.Name;
            if (n.Length > 0 && (n[0] == '<' || n.StartsWith("<>", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    /// <summary>抽出**当前行**里的字符串字面量（含逐字串 `@"…"` 与 `"""…"""`）。
    /// ★ 会跳过 `//` 之后的行注释（否则注释里的引号串会被误计）。</summary>
    private static IEnumerable<string> StringLiterals(string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            // ★ 行注释：`//` 之后到行尾全部忽略（引号在注释里不成对，必须挡）
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') yield break;
            if (c != '"') { i++; continue; }

            bool verbatim = i > 0 && (line[i - 1] == '@' || line[i - 1] == '$');
            bool raw = i + 2 < line.Length && line[i + 1] == '"' && line[i + 2] == '"';

            var sb = new System.Text.StringBuilder();
            if (raw)
            {
                i += 3;
                while (i < line.Length)
                {
                    if (i + 2 < line.Length && line[i] == '"' && line[i + 1] == '"' && line[i + 2] == '"')
                    { i += 3; break; }
                    sb.Append(line[i]); i++;
                }
            }
            else if (verbatim)
            {
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++; break;
                    }
                    sb.Append(line[i]); i++;
                }
            }
            else
            {
                i++;
                while (i < line.Length)
                {
                    char d = line[i];
                    if (d == '\\' && i + 1 < line.Length)
                    {
                        char e = line[i + 1];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '0': sb.Append('\0'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case '\'': sb.Append('\''); break;
                            case 'u':
                                if (i + 5 < line.Length
                                    && ushort.TryParse(line.Substring(i + 2, 4),
                                        System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, out ushort u))
                                { sb.Append((char)u); i += 6; continue; }
                                sb.Append(e); break;
                            default: sb.Append(e); break;
                        }
                        i += 2;
                        continue;
                    }
                    if (d == '"') { i++; break; }
                    sb.Append(d); i++;
                }
            }
            yield return sb.ToString();
        }
    }

    /// <summary>TSV 转义（保持单行）。</summary>
    private static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\t", "\\t")
                .Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
