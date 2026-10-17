// ═══════════════════════════════════════════════════════════════════════════
//  stablecheck —— 用**指定的 Managed 引用集**编译我方**全部** Scripts，只报错不 Emit。
//
//  为什么需要它（2026-10-17 起因）：
//    用户在 `D:\CoQ-stable`（正式版，DepotDownloader 下的 public 分支）上开游戏，
//    build_log.txt 07:25:28 报 **20 个 C# 编译错误**（CS0246/CS0115/CS0117/CS0234/CS0103）
//    ⇒ 整个补丁集等于不存在（模组在正式版上完全失效、且开局消息栏里连版本戳都没有）。
//    而 `decomp` 逐一查类型/成员时，两侧**看起来都有** ⇒ 需要**真编译**来裁决。
//
//  与 roslyntest 的区别：
//    · roslyntest 的 MANAGED 写死为 Steam(lang-experimental)，且默认 Emit + 反射自检
//    · 本工具 **Managed 目录由命令行给**，只做"编译错误计数"，两侧可对比
//
//  用法：
//    dotnet run -c Release -- "D:\CoQ-stable\CoQ_Data\Managed"
//    dotnet run -c Release -- "D:\Steam\steamapps\common\Caves of Qud\CoQ_Data\Managed"
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    private const string WS = @"C:\Users\Nattens\Downloads\CoQ-马尔科夫文本调研";
    private static readonly string SCRIPTS = Path.Combine(WS, "mods", "QudZhsCookbook", "Scripts");

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length >= 1 && args[0] == "metaflags") return MetaFlags.Run(args);
        if (args.Length < 1)
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  stablecheck <Managed 目录>              编译我方 Scripts，报错");
            Console.WriteLine("  stablecheck metaflags <dll> [名字子串]   列类型的 TypeAttributes");
            return 2;
        }
        string managed = args[0];
        if (!Directory.Exists(managed))
        {
            Console.WriteLine("!! 目录不存在: " + managed);
            return 2;
        }

        var refs = new List<MetadataReference>();
        int refFail = 0;
        foreach (string p in Directory.GetFiles(managed, "*.dll").OrderBy(x => x))
            try { refs.Add(MetadataReference.CreateFromFile(p)); }
            catch { refFail++; }

        var trees = new List<SyntaxTree>();
        foreach (string f in Directory.GetFiles(SCRIPTS, "*.cs", SearchOption.AllDirectories)
                                      .OrderBy(x => x))
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f),
                new CSharpParseOptions(LanguageVersion.Latest), f));

        Console.WriteLine("=== stablecheck ===");
        Console.WriteLine("Managed   : " + managed);
        Console.WriteLine("Roslyn    : " + typeof(CSharpCompilation).Assembly.GetName().Version);
        Console.WriteLine("引用程序集: " + refs.Count + "（读取失败 " + refFail + "）");
        Console.WriteLine("源码文件  : " + trees.Count);
        Console.WriteLine();

        var comp = CSharpCompilation.Create("StableCheck", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        int err = 0;
        var byId = new Dictionary<string, int>();
        foreach (Diagnostic d in comp.GetDiagnostics())
        {
            if (d.Severity != DiagnosticSeverity.Error) continue;
            err++;
            byId[d.Id] = byId.TryGetValue(d.Id, out int n) ? n + 1 : 1;
            var sp = d.Location.GetLineSpan();
            Console.WriteLine("  [error] " + d.Id + " @ " + Path.GetFileName(sp.Path)
                + ":" + (sp.StartLinePosition.Line + 1) + " " + d.GetMessage());
        }
        Console.WriteLine();
        foreach (var kv in byId.OrderByDescending(k => k.Value))
            Console.WriteLine("  " + kv.Key + " × " + kv.Value);
        Console.WriteLine("⇒ 编译错误 " + err);
        return err == 0 ? 0 : 1;
    }
}
