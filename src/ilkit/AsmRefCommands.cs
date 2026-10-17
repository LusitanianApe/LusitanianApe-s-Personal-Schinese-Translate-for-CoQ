// 读程序集元数据：某个类型名（如 ReadOnlySpan`1）的 TypeRef **指向哪个程序集**。
// 用途：确定 Assembly-CSharp.dll 的签名里 ReadOnlySpan<> 的真实归属程序集。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class AsmRefCommands
{
    /// <summary>查某类型名的 TypeRef **指向哪个程序集**（治 Unity 里 `ReadOnlySpan` 归属这类问题）。</summary>
    public static int Run(string asmPath, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法: ilkit asmref <类型名> [更多类型名…]（程序集由 --dll 指定）");
            return 2;
        }
        string path = asmPath;
        var wanted = new HashSet<string>(args, StringComparer.Ordinal);
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();

        Console.WriteLine("程序集: " + Path.GetFileName(path));
        var asm = md.GetAssemblyDefinition();
        Console.WriteLine("  自身: " + md.GetString(asm.Name) + "  v" + asm.Version);
        Console.WriteLine();

        Console.WriteLine("  ── 程序集引用表（AssemblyRef）");
        foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
        {
            AssemblyReference r = md.GetAssemblyReference(h);
            Console.WriteLine("     " + md.GetString(r.Name) + "  v" + r.Version);
        }
        Console.WriteLine();

        Console.WriteLine("  ── 目标类型的 TypeRef（含归属程序集）");
        int n = 0;
        foreach (TypeReferenceHandle h in md.TypeReferences)
        {
            TypeReference t = md.GetTypeReference(h);
            string name = md.GetString(t.Name);
            // ★ 用**子串**匹配，避免 PowerShell 吃掉反引号（ReadOnlySpan`1 ⇒ ReadOnlySpan1）
            if (!wanted.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            string ns = md.GetString(t.Namespace);
            string scope = DescribeScope(md, t.ResolutionScope);
            Console.WriteLine("     " + (ns.Length > 0 ? ns + "." : "") + name + "   ← " + scope);
            n++;
        }
        if (n == 0) Console.WriteLine("     （没找到 TypeRef）");

        Console.WriteLine();
        Console.WriteLine("  ── 目标类型的 TypeDef（定义在**本程序集**内）");
        int d = 0;
        foreach (TypeDefinitionHandle h in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(h);
            string name = md.GetString(t.Name);
            if (!wanted.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            string ns = md.GetString(t.Namespace);
            Console.WriteLine("     " + (ns.Length > 0 ? ns + "." : "") + name
                + "   ← TypeDef（本程序集）  attrs=" + t.Attributes
                + "  public=" + t.Attributes.HasFlag(TypeAttributes.Public)
                + "  nested=" + t.Attributes.HasFlag(TypeAttributes.NestedPublic));
            d++;
        }
        if (d == 0) Console.WriteLine("     （没找到 TypeDef）");
        Console.WriteLine();
        Console.WriteLine("  命中 " + n + " 条");
        return 0;
    }

    private static string DescribeScope(MetadataReader md, EntityHandle scope)
    {
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                AssemblyReference r = md.GetAssemblyReference((AssemblyReferenceHandle)scope);
                return "AssemblyRef: " + md.GetString(r.Name) + " v" + r.Version;
            case HandleKind.TypeReference:
                TypeReference t = md.GetTypeReference((TypeReferenceHandle)scope);
                return "嵌套于 " + md.GetString(t.Name);
            case HandleKind.TypeDefinition:
                return "TypeDef（本程序集内）";
            case HandleKind.ModuleDefinition:
                return "ModuleDef（本模块）";
            default:
                return scope.Kind.ToString();
        }
    }
}
