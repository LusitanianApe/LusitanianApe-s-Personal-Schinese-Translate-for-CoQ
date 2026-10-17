// ============================================================================
//  ilkit —— 一个 .NET 程序集的「取证」工具箱
//
//  目标读者：做 Unity / .NET 游戏汉化、或需要回答「这句英文到底从哪来」的人。
//
//  它把四类常用动作合成一个 CLI：
//    · 反编译（ICSharpCode.Decompiler）：按类型/成员/字符串定位
//    · 字符串普查：扫 `ldstr`，给出「字面量 ← 谁引用」（不反编译，秒级）
//    · IL 反汇编：操作数解析成成员/字符串，直接看控制流
//    · 元数据探针：静态字段读写点 / TypeRef 归属
//
//  ★ 本工具不含任何游戏内容，只在**使用者本机**对**使用者自己合法持有**的程序集运行。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;

internal static class Program
{
    private const string UsageText = @"ilkit —— .NET 程序集取证工具箱

用法:
  ilkit [--dll <程序集路径>] <命令> [参数…]

反编译 / 字符串
  list <片段>                    列出匹配的类型全名
  type <类型全名>                反编译整个类型
  member <类型全名> <成员片段>    反编译匹配的成员
  find <字面量…>                 扫 ldstr：哪些成员引用了这些字符串（秒级，不反编译全量）
  literals                      列出 #US heap 里的全部字符串（按 heap 格式行走，去重）
  ldstr                         输出 type<TAB>member<TAB>literal 全表

IL / 元数据
  il <类型全名> [成员片段]        反汇编到 IL（操作数已解析成成员/字符串）
  il-find <子串>                 列出名字含该子串的方法
  field <类型名> <字段名>         扫某个静态字段的读写点（stsfld / ldsfld）
  asmref <类型名…>               查这些类型名的 TypeRef 指向哪个程序集
  sig <片段>                     打印匹配类型的方法签名表

高级 / 诊断
  callsites                      Scan 调用点（参数序号 + 字面量）
  calls / branches               调用图 / 分支
  baseargs <方法名>              抽某个方法调用的第一实参
  diag                           行走器自检
  ilwalk <类型>::<成员>          原始 IL 字节 + 偏移走查（排查 operand 表）

程序集解析顺序：--dll  >  环境变量 ILKIT_DLL  >  环境变量 COQ_MANAGED\Assembly-CSharp.dll
";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // 强制 LF：输出常被下游脚本按 \n 切分，CRLF 会留下尾随 \r
        Console.Out.NewLine = "\n";

        if (args.Length == 0)
        {
            Console.Error.WriteLine(UsageText);
            return 2;
        }
        if (args[0] == "-h" || args[0] == "--help" || args[0] == "help")
        {
            Console.WriteLine(UsageText);
            return 0;
        }

        // ── 全局选项：--dll / -d（可出现在任意位置）──
        string dll = null;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--dll" || args[i] == "-d") && i + 1 < args.Length)
            {
                dll = args[++i];
                continue;
            }
            rest.Add(args[i]);
        }
        if (rest.Count == 0)
        {
            Console.Error.WriteLine(UsageText);
            return 2;
        }

        string cmd = rest[0].ToLowerInvariant();
        rest.RemoveAt(0);
        string[] a = rest.ToArray();

        string asm = ResolveDll(dll);
        if (asm == null)
        {
            Console.Error.WriteLine("找不到目标程序集。");
            Console.Error.WriteLine("  用法：ilkit --dll <路径> " + cmd + " …");
            Console.Error.WriteLine("  或设置环境变量 ILKIT_DLL / COQ_MANAGED");
            return 2;
        }

        // ── 不需要反编译器的命令（起步更快）──
        try
        {
            switch (cmd)
            {
                case "il":
                    return IlCommands.Run(asm, a);
                case "il-find":
                    {
                        var x = new string[a.Length + 1];
                        x[0] = "--find";
                        Array.Copy(a, 0, x, 1, a.Length);
                        return IlCommands.Run(asm, x);
                    }
                case "field":
                    return FieldCommands.Run(asm, a);
                case "asmref":
                    return AsmRefCommands.Run(asm, a);
            }

            // ── 反编译族：把命令名放回 args[0] ──
            var full = new string[a.Length + 1];
            full[0] = cmd;
            Array.Copy(a, 0, full, 1, a.Length);
            DecompilerCommands.Asm = asm;
            return DecompilerCommands.Run(full);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ilkit 失败：" + e.GetType().Name + "：" + e.Message);
            return 1;
        }
    }

    /// <summary>`--dll` &gt; `ILKIT_DLL` &gt; `COQ_MANAGED\Assembly-CSharp.dll`。</summary>
    private static string ResolveDll(string cli)
    {
        if (!string.IsNullOrEmpty(cli))
        {
            if (File.Exists(cli)) return cli;
            Console.Error.WriteLine("--dll 指定的文件不存在：" + cli);
            return null;
        }
        string e = Environment.GetEnvironmentVariable("ILKIT_DLL");
        if (!string.IsNullOrEmpty(e) && File.Exists(e)) return e;

        string managed = Environment.GetEnvironmentVariable("COQ_MANAGED");
        if (!string.IsNullOrEmpty(managed))
        {
            string p = Path.Combine(managed, "Assembly-CSharp.dll");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
