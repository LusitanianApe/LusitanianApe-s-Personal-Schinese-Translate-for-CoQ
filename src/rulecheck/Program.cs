// ─────────────────────────────────────────────────────────────────────────────
// RuleCheck —— **离线跑真实规则**（不启动游戏、不进 Unity）
//
// ★ 核心思想：规则是纯字符串函数 ⇒ 只要把**游戏里那一份 IL** 加载进来，
//   就能在控制台里成千上万次地跑它，不用人肉进游戏试。
//
// ★ 加载方式：**反射**加载 `mods\_compilecheck\bin\Release\net8.0\QudZhsCookbook.dll`
//   （这是 `dotnet build mods\_compilecheck` 的产物，与游戏里跑的源码同源）
//   依赖程序集统一从游戏的 `CoQ_Data\Managed\` 解析。
//
// ★ 两种模式
//   ① fixtures.tsv（默认）  每行 `英文<TAB>期望中文`；期望留空 ⇒ 只查英文残留
//   ② --residue             corpus.txt 每行一句英文；只报**译完仍含拉丁字母**的
//
// ★ 英文残留判定：先剥掉 `{{标记}}` / `=槽=` / `[方括号]` / URL，
//   再看是否还有 `[A-Za-z]{3,}` 的连续段 —— 有就是一处在漏。
//
// 用法：
//   dotnet run -c Release --project _tools\rulecheck -- _tools\_msg_fixtures.tsv
//   dotnet run -c Release --project _tools\rulecheck -- _tools\_msg_corpus.txt --residue
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

internal static class RuleCheckProgram
{
    private static readonly string ManagedDir =
        Environment.GetEnvironmentVariable("COQ_MANAGED")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\CoQ_Data\Managed";

    private static readonly Regex Markup =
        new Regex(@"\{\{.*?\}\}|=[^=\s]+=|\[[^\]]*\]|https?://\S+", RegexOptions.Compiled);

    private static readonly Regex Latin = new Regex("[A-Za-z]{3,}", RegexOptions.Compiled);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine("用法：rulecheck <fixtures.tsv|corpus.txt> [--residue] [--verbose]");
            return 2;
        }

        string path = args[0];
        if (Environment.GetEnvironmentVariable("RULECHECK_DEBUG_ARGS") == "1")
            Console.WriteLine("args=[" + string.Join("|", args) + "]");
        bool residue = args.Contains("--residue");
        bool verbose = args.Contains("--verbose");

        if (!File.Exists(path))
        {
            Console.WriteLine("找不到文件：" + path);
            return 2;
        }

        // ── 依赖解析：一切游戏程序集都从 Managed\ 找 ──
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(ManagedDir, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };

        Func<string, string> translate = LoadTranslator();

        // ── --trace：把一条输入逐规则过一遍，报「哪几条正则命中」──
        if (args.Contains("--trace"))
        {
            string line = File.ReadAllText(path, Encoding.UTF8).Split('\n')[0].TrimEnd('\r');
            TraceRules(line);
            return 0;
        }

        // ── --call <Type>.<Method>：直接调**任意** `static string f(string)` 静态纯函数 ──
        //   ★ 用途：某些补丁的翻译逻辑不在 `TranslateText` 里（例如 `ZhsWishMenuPatch.Translate`、
        //     `ZhsSkillReqTable.Apply`）—— 有了这个模式，任何纯函数都能离线跑，
        //     不必为了"测一下"临时改 harness。
        int ci = Array.IndexOf(args, "--call");
        if (ci >= 0 && ci + 1 < args.Length)
        {
            string full = args[ci + 1];
            int dot = full.LastIndexOf('.');
            if (dot <= 0) { Console.WriteLine("--call 需要 <全限定类型>.<方法>"); return 2; }
            string typeName = full.Substring(0, dot), methodName = full.Substring(dot + 1);
            Assembly asm0 = LoadDll();
            if (asm0 == null) return 3;
            Type t0 = asm0.GetType(typeName, false);
            if (t0 == null) { Console.WriteLine("找不到类型 " + typeName); return 3; }
            MethodInfo m0 = t0.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m0 == null) { Console.WriteLine("找不到静态方法 " + methodName); return 3; }
            Console.WriteLine("调用：" + typeName + "." + methodName);
            Console.WriteLine("────────────────────────────────────────────────────────────");
            int n0 = 0, ch0 = 0;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                n0++;
                object ret;
                try { ret = m0.Invoke(null, new object[] { raw }); }
                catch (Exception e)
                {
                    Console.WriteLine("EX   " + raw + "\n     " + e.GetType().Name);
                    continue;
                }
                string got = ret as string;
                if (got == null) { Console.WriteLine("null " + raw); continue; }
                if (got != raw) ch0++;
                Console.WriteLine("     " + raw + "\n  →  " + got);
            }
            Console.WriteLine("────────────────────────────────────────────────────────────");
            Console.WriteLine("样本 " + n0 + " · 有变化 " + ch0);
            return 0;
        }

        // ── --pluralize <words.txt>：验证「裸 ID 补齐后到底渲染成什么」──
        //   ★ 2026-10-17：`=N.pluralize:X=` / `=X.things:N=` 的输出由两段决定：
        //       ① `Grammar.Pluralize(X)`（N>1 时变复数）—— **真游戏代码**，这里反射调用
        //       ② 拿结果当**裸 ID** 查字符串表（我方 Strings.xml 里无 Context 的条目）
        //   只补单数或只补复数 ⇒ 另一条会露英文。本模式把两段都跑真数据。
        int pi = Array.IndexOf(args, "--pluralize");
        if (Environment.GetEnvironmentVariable("RULECHECK_DEBUG_ARGS") == "1")
            Console.WriteLine("PI=" + pi + " translateNull=" + (translate == null));
        // ★ 两种写法都支持：`<words.txt> --pluralize`（开关式）与 `--pluralize <words.txt>`
        if (pi >= 0)
        {
            string wf = (pi + 1 < args.Length) ? args[pi + 1] : path;
            return PluralizeProbe(wf);
        }

        // ── --rulecover <语料>：**全量规则覆盖回放**（2026-10-17）──
        //   对 `Rules` 里**每一张**正则，用整份 DLL 字面量语料跑一遍，统计命中数。
        //   判据分两类（这是关键，否则会产出大量假阳性）：
        //     · **纯 ASCII 锚点**规则 ⇒ 英文原文语料里**应当命中**；命中 0 ⇒ **可疑**
        //       （要么锚点已被别的补丁改掉 —— fail/miss/hits 三例就是这样；
        //         要么该消息是运行时拼接、语料里没有）
        //     · **含汉字锚点**规则 ⇒ 是「镜像链路」（给已被译的形态用），
        //       英文语料里 0 命中**完全正常** ⇒ 只统计、不报警
        int rci = Array.IndexOf(args, "--rulecover");
        if (rci >= 0)
        {
            string corpus = (rci + 1 < args.Length) ? args[rci + 1] : path;
            return RuleCover(corpus);
        }

        if (translate == null) return 3;

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        int total = 0, changed = 0, hit = 0, miss = 0, err = 0, resid = 0;
        var bad = new List<string>();
        var residSamples = new List<string>();

        foreach (string raw in lines)
        {
            if (raw.Length == 0 || raw.StartsWith("#")) continue;

            string en = raw, expect = null;
            if (!residue)
            {
                int t = raw.IndexOf('\t');
                if (t >= 0) { en = raw.Substring(0, t); expect = raw.Substring(t + 1).Trim(); }
            }
            en = en.Trim();
            if (en.Length == 0) continue;

            total++;
            string got;
            try { got = translate(en); }
            catch (Exception e)
            {
                err++;
                if (bad.Count < 25)
                    bad.Add("EX   " + en + "\n     " + e.GetType().Name + ": " + e.Message);
                continue;
            }

            if (got != en) changed++;

            if (!string.IsNullOrEmpty(expect))
            {
                if (got == expect) hit++;
                else
                {
                    miss++;
                    if (bad.Count < 25)
                        bad.Add("MISS\n     英: " + en + "\n     期: " + expect + "\n     实: " + got);
                }
                continue;
            }

            string probe = Markup.Replace(got, " ");
            Match m = Latin.Match(probe);
            if (m.Success)
            {
                resid++;
                if (residSamples.Count < 40)
                    residSamples.Add("RES  " + en + "\n     → " + got + "\n     ↑残留「" + m.Value + "」");
            }
            else if (verbose)
            {
                Console.WriteLine("OK   " + en + "\n     → " + got);
            }
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine("样本 " + total + " · 有变化 " + changed + " · 调用异常 " + err);
        if (residue) Console.WriteLine("★ 译后仍含英文残留 = " + resid);
        else Console.WriteLine("命中期望 " + hit + " · 不符 " + miss);

        if (bad.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("── 不符/异常样例 ──");
            foreach (string s in bad) Console.WriteLine(s);
        }
        if (residSamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("── 英文残留样例 ──");
            foreach (string s in residSamples) Console.WriteLine(s);
        }
        return resid > 0 ? 1 : 0;
    }

    /// <summary>
    /// `--trace`：逐条规则试一遍，报**哪几条命中**、命中在哪个下标。
    /// ★ 用途：规则写得对、顺序也对，但结果不对时 —— 一眼看出是不是
    ///   「前一条把它吃了」「下标对了但 Value 写错」「根本没进数组」。
    /// </summary>
    private static void TraceRules(string input)
    {
        Assembly asm = LoadDll();
        if (asm == null) return;
        Type t = asm.GetType("Nattens.QudZhsCookbook.ZhsCombatMsgPatch", false);
        if (t == null) { Console.WriteLine("找不到类型"); return; }
        FieldInfo f = t.GetField("Rules", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) { Console.WriteLine("找不到 Rules 字段"); return; }

        var rules = f.GetValue(null) as System.Collections.IEnumerable;
        if (rules == null) { Console.WriteLine("Rules 不是可枚举的"); return; }

        Console.WriteLine("输入：" + input);
        Console.WriteLine("────────────────────────────────────────────────────────────");
        int i = 0, matched = 0;
        Regex inputRx = null;
        foreach (object kv in rules)
        {
            Type kvt = kv.GetType();
            Regex re = (Regex)kvt.GetProperty("Key").GetValue(kv, null);
            string rep = (string)kvt.GetProperty("Value").GetValue(kv, null);
            bool hit = re.IsMatch(input);
            if (hit)
            {
                matched++;
                Console.WriteLine("#" + i + " 命中  " + re + "\n      → " + rep);
                if (inputRx == null) inputRx = re;
            }
            i++;
        }
        Console.WriteLine("────────────────────────────────────────────────────────────");
        Console.WriteLine("规则总数 " + i + " · 命中 " + matched
            + (matched > 0 ? " · **第一条命中的是** " + inputRx : ""));

        // 再串一遍碎片表（它常是「以为没译，其实在下一道」的元凶）
        Type ft = asm.GetType("Nattens.QudZhsCookbook.ZhsFragmentDictTable", false);
        if (ft != null)
        {
            MethodInfo fa = ft.GetMethod("Apply",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (fa != null)
            {
                string after = (string)fa.Invoke(null, new object[] { input });
                Console.WriteLine();
                Console.WriteLine("碎片表后：" + (after == input ? "（无变化）" : after));
            }
        }
    }

    /// <summary>
    /// `--pluralize &lt;words.txt&gt;`：把 `=N.pluralize:X=` / `=X.things:N=` 的**两种形态**算出来。
    /// 判据：单数形与复数形**都必须**有裸 ID，否则 N&gt;1 那条露英文。
    /// </summary>
    private static int PluralizeProbe(string path)
    {
        string xml = FindModFile("Strings.xml");
        if (xml == null)
        {
            Console.WriteLine("找不到 mods\\QudZhsCookbook\\Strings.xml");
            return 3;
        }
        var bare = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = new System.Xml.XmlDocument();
        doc.Load(xml);
        System.Xml.XmlNodeList nodes = doc.SelectNodes("//string");
        foreach (System.Xml.XmlNode n in nodes)
        {
            System.Xml.XmlElement el = n as System.Xml.XmlElement;
            if (el == null) continue;
            if (el.GetAttributeNode("Context") != null) continue;      // 只收裸 ID
            string id = el.GetAttribute("ID");
            if (string.IsNullOrEmpty(id)) continue;
            bare[id] = el.InnerText;
        }
        Console.WriteLine("我方裸 ID：" + bare.Count + " 条（" + Path.GetFileName(xml) + "）");

        MethodInfo pl = ResolveGamePluralize();
        if (pl == null)
        {
            Console.WriteLine("找不到 XRL.Language.Grammar::Pluralize");
            return 3;
        }
        Console.WriteLine("Grammar.Pluralize：**真游戏代码**（反射调用）✅");
        Console.WriteLine();
        Console.WriteLine("词".PadRight(18) + "复数形".PadRight(20) + "裸ID(单)".PadRight(10)
            + "裸ID(复)".PadRight(10) + "N=1 →".PadRight(16) + "N=3 →");
        Console.WriteLine(new string('─', 96));

        int ok = 0, bad = 0;
        var badWords = new List<string>();
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string w = raw.Trim();
            if (w.Length == 0 || w.StartsWith("#")) continue;
            string plural;
            try { plural = (string)pl.Invoke(null, new object[] { w }); }
            catch { plural = "?"; }
            string s1, s2;
            bare.TryGetValue(w, out s1);
            bare.TryGetValue(plural, out s2);
            bool good = s1 != null && s2 != null;
            if (good) ok++; else { bad++; badWords.Add(w); }
            Console.WriteLine(w.PadRight(18) + plural.PadRight(20)
                + (s1 ?? "（缺）").PadRight(10) + (s2 ?? "（缺）").PadRight(10)
                + ("1 " + (s1 ?? w)).PadRight(16) + ("3 " + (s2 ?? plural)));
        }
        Console.WriteLine(new string('─', 96));
        Console.WriteLine("单复数两条都齐：" + ok + " · 缺：" + bad
            + (bad > 0 ? "（" + string.Join(",", badWords.ToArray()) + "）" : ""));
        return bad == 0 ? 0 : 1;
    }

    /// <summary>从当前目录往上找工作区里的模组文件（不写死路径）。</summary>
    private static string FindModFile(string name)
    {
        string d = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (d == null) break;
            string p = Path.Combine(d, "mods", "QudZhsCookbook", name);
            if (File.Exists(p)) return p;
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    /// <summary>反射拿游戏里的 `XRL.Language.Grammar::Pluralize(string)`（真形态学）。</summary>
    private static MethodInfo ResolveGamePluralize()
    {
        try
        {
            Assembly game = FindAsm("Assembly-CSharp");
            if (game == null)
            {
                // ★ `Assembly-CSharp` 是**惰性加载**的：反射拿到我方 DLL 的类型不会触发它，
                //   必须显式按名加载（Main 里注册的 Resolving 会把它指到游戏 Managed 目录）。
                try
                {
                    game = AssemblyLoadContext.Default.LoadFromAssemblyName(
                        new AssemblyName("Assembly-CSharp"));
                }
                catch { }
            }
            if (game == null)
            {
                LoadDll();
                game = FindAsm("Assembly-CSharp");
            }
            if (game == null) return null;
            Type g = game.GetType("XRL.Language.Grammar", false);
            return g == null ? null : g.GetMethod("Pluralize", new[] { typeof(string) });
        }
        catch { return null; }
    }

    private static Assembly FindAsm(string simpleName)
    {
        foreach (Assembly a in AssemblyLoadContext.Default.Assemblies)
            if (a.GetName().Name == simpleName) return a;
        return null;
    }

    /// <summary>
    /// `--rulecover &lt;语料&gt;`：全量规则覆盖回放。每条规则在整份语料上的命中数。
    /// 产出 <c>_tools\_rulecover.tsv</c>（逐条）+ 控制台摘要。
    /// </summary>
    private static int RuleCover(string corpus)
    {
        Assembly asm = LoadDll();
        if (asm == null) return 3;
        Type t = asm.GetType("Nattens.QudZhsCookbook.ZhsCombatMsgPatch", false);
        if (t == null) { Console.WriteLine("找不到 ZhsCombatMsgPatch"); return 3; }
        FieldInfo f = t.GetField("Rules", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) { Console.WriteLine("找不到 Rules 字段"); return 3; }

        var list = new List<Regex>();
        var reps = new List<string>();
        foreach (object kv in (System.Collections.IEnumerable)f.GetValue(null))
        {
            Type kvt = kv.GetType();
            list.Add((Regex)kvt.GetProperty("Key").GetValue(kv, null));
            reps.Add((string)kvt.GetProperty("Value").GetValue(kv, null));
        }

        string[] lines = File.ReadAllLines(corpus, Encoding.UTF8);
        var corpusLines = new List<string>();
        foreach (string l in lines)
            if (l.Length > 0 && !l.StartsWith("#")) corpusLines.Add(l);
        Console.WriteLine("语料：" + corpusLines.Count + " 条 · 规则：" + list.Count + " 条");
        Console.WriteLine("（跑全量回放，可能要几十秒…）");

        int cjkCount = 0, asciiCount = 0, asciiZero = 0, cjkZero = 0;
        var rows = new List<string>();
        var zeroAscii = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            Regex re = list[i];
            string pat = re.ToString();
            bool cjk = false;
            foreach (char ch in pat)
                if (ch >= '\u4e00' && ch <= '\u9fff') { cjk = true; break; }
            int hits = 0;
            foreach (string l in corpusLines)
                if (re.IsMatch(l)) hits++;
            if (cjk)
            {
                cjkCount++;
                if (hits == 0) cjkZero++;
            }
            else
            {
                asciiCount++;
                if (hits == 0)
                {
                    asciiZero++;
                    string p = pat.Length > 110 ? pat.Substring(0, 110) + "…" : pat;
                    zeroAscii.Add("#" + i + "\t" + hits + "\t" + p.Replace("\t", " "));
                }
            }
            string row = "#" + i + "\t" + (cjk ? "ZH" : "EN") + "\t" + hits + "\t"
                + pat.Replace("\t", " ").Replace("\n", "\\n") + "\t" + reps[i].Replace("\t", " ");
            rows.Add(row);
        }

        string outp = FindToolFile("_rulecover.tsv");
        if (outp != null)
        {
            File.WriteAllText(outp,
                "idx\tkind\thits\tpattern\treplacement\n" + string.Join("\n", rows.ToArray()) + "\n",
                new UTF8Encoding(false));
        }

        Console.WriteLine(new string('─', 72));
        Console.WriteLine("纯 ASCII 锚点规则：" + asciiCount + " · 其中命中 0：" + asciiZero);
        Console.WriteLine("含汉字锚点规则：" + cjkCount + " · 其中命中 0：" + cjkZero
            + "（镜像链路，英文语料里 0 命中**正常**）");
        Console.WriteLine();
        if (zeroAscii.Count > 0)
        {
            Console.WriteLine("★★ 可疑：纯 ASCII 锚点但**在整份语料里一条都命中不了**（" + zeroAscii.Count + " 条）");
            Console.WriteLine("   可能原因：① 锚点已被别的补丁改掉（fail/miss/hits 那种）");
            Console.WriteLine("             ② 该消息是运行时拼接、字面量里没有");
            Console.WriteLine("             ③ 规则写错 / 已废弃");
            foreach (string z in zeroAscii) Console.WriteLine("   " + z);
        }
        else
        {
            Console.WriteLine("✅ 纯 ASCII 锚点规则**全部**在语料里有命中");
        }
        if (outp != null) Console.WriteLine("\n逐条清单：" + outp);
        return 0;
    }

    /// <summary>在工具目录（`_tools`）里定位/命名输出文件。</summary>
    private static string FindToolFile(string name)
    {
        string d = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (d == null) break;
            string tools = Path.Combine(d, "_tools");
            if (Directory.Exists(tools)) return Path.Combine(tools, name);
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    private static Assembly LoadDll()
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(ManagedDir, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        string dll = Path.Combine(AppContext.BaseDirectory, "QudZhsCookbook.dll");
        if (!File.Exists(dll)) { Console.WriteLine("找不到 QudZhsCookbook.dll"); return null; }
        Console.WriteLine("加载：" + dll);
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
    }

    /// <summary>反射拿到 `Nattens.QudZhsCookbook.ZhsCombatMsgPatch.TranslateText`。</summary>
    private static Func<string, string> LoadTranslator()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "QudZhsCookbook.dll");
        if (!File.Exists(dll))
        {
            // 退回工作区里 compilecheck 的产物
            dll = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "mods", "_compilecheck", "bin", "Release", "net8.0", "QudZhsCookbook.dll"));
        }
        if (!File.Exists(dll))
        {
            Console.WriteLine("找不到 QudZhsCookbook.dll —— 先跑：dotnet build mods\\_compilecheck");
            return null;
        }
        Console.WriteLine("加载：" + dll);

        Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        Type t = asm.GetType("Nattens.QudZhsCookbook.ZhsCombatMsgPatch", false);
        if (t == null)
        {
            Console.WriteLine("找不到类型 ZhsCombatMsgPatch");
            return null;
        }
        MethodInfo m = t.GetMethod("TranslateText",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (m == null)
        {
            Console.WriteLine("找不到 TranslateText");
            return null;
        }
        Console.WriteLine("规则入口：TranslateText ✅");
        Func<string, string> ruleFn = en => (string)m.Invoke(null, new object[] { en });

        // ★★ 串上**碎片字典**（`ZhsFragmentDictTable.Apply`）——
        //   真实管线是 `TranslateText`（规则）→ 消息定稿 → `Apply`（碎片兜底）。
        //   只测前者会**假报残留**（例如 `Because they despise` 是碎片表的活）。
        Type ft = asm.GetType("Nattens.QudZhsCookbook.ZhsFragmentDictTable", false);
        MethodInfo fa = ft == null ? null : ft.GetMethod("Apply",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (fa == null)
        {
            Console.WriteLine("碎片字典：未串（只测规则）");
            return ruleFn;
        }
        Console.WriteLine("碎片字典：ZhsFragmentDictTable.Apply ✅（已串进管线）");
        // ★ `Apply` 在 `Rules.Count == 0` 时**直接放行** ⇒ 必须先 `Load()` 建表。
        //   （离线第一版没调 Load，看起来"碎片表没生效"，其实是空表 —— 这个坑很容易再踩。）
        MethodInfo fl = ft.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (fl != null)
        {
            try { fl.Invoke(null, null); Console.WriteLine("碎片字典：Load() ✅"); }
            catch (Exception e) { Console.WriteLine("碎片字典：Load() 失败 " + e.GetType().Name); }
        }
        // ★★ 三阶段按**真实管线顺序**串：
        //   ① `ZhsJournalNote.Apply`          ← `IBaseJournalEntry.DisplayMessage`（最早）
        //   ② `ZhsCombatMsgPatch.TranslateText` ← 规则表
        //   ③ `ZhsFragmentDictTable.Apply`      ← 消息定稿处兜底
        //   顺序错了会**假报缺陷**（日志提示被规则抢先吃掉，看起来像规则写坏）。
        Type jt = asm.GetType("Nattens.QudZhsCookbook.ZhsJournalNote", false);
        MethodInfo jn = jt == null ? null
            : jt.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine("日志提示：" + (jn == null ? "未串（规则不会经过它）" : "ZhsJournalNote.Apply ✅"));

        return en =>
        {
            string a = en;
            if (jn != null) a = (string)jn.Invoke(null, new object[] { a });
            a = ruleFn(a);
            return (string)fa.Invoke(null, new object[] { a });
        };
    }
}
