// ═══════════════════════════════════════════════════════════════════════════
//  metaflags —— 裸读 CLI metadata，列出指定类型的 **TypeAttributes**
//
//  起因：`decomp` 对 internal 类型**不打印前缀**，会让人误判"类型是 public"，
//        而 Roslyn 用同一份 DLL 编译却报 CS0246（找不到该类型）。
//        要区分「类型不存在」与「类型存在但不可见」，只能读 TypeDef 的 Flags。
//
//  用法：dotnet run -c Release -- metaflags <dll> [名字子串...]
//        不给子串时默认只看 XRL.Language.* / XRL.World.Text* 里的关键类型
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

internal static class MetaFlags
{
    private static readonly HashSet<string> Defaults = new HashSet<string>
    {
        "TranslatorBase", "Translator", "LanguageLoader", "LanguageProviderAttribute",
        "LanguageProvider", "Strings", "TextConstants", "GraphemeSplitter",
        "DescriptionBuilder", "GameObjectReplacers", "StringMaker", "StringBuilderPool",
    };

    public static int Run(string[] args)
    {
        // args[0]=metaflags args[1]=dll args[2..]=名字子串（可空）
        if (args.Length < 2) { Console.WriteLine("用法: metaflags <dll> [名字...]"); return 2; }
        string path = args[1];
        var filters = args.Skip(2).ToList();
        bool useDefault = filters.Count == 0;
        // ★ 若只给了 1 个过滤词且带 `.`，视为"跨命名空间查找"：打印完整名字
        bool wide = filters.Count > 0;

        using (var fs = File.OpenRead(path))
        using (var pe = new PEReader(fs))
        {
            MetadataReader md = pe.GetMetadataReader();
            Console.WriteLine("=== " + path);
            Console.WriteLine("    程序集 = " + md.GetString(md.GetAssemblyDefinition().Name)
                              + "  类型数 = " + md.TypeDefinitions.Count);
            Console.WriteLine();
            Console.WriteLine("  {0,-34} {1,-26} {2,-18} {3}", "命名空间", "类型", "可见性", "flags");
            var rows = new List<(string ns, string nm, string vis, string flags)>();
            foreach (TypeDefinitionHandle h in md.TypeDefinitions)
            {
                TypeDefinition td = md.GetTypeDefinition(h);
                string ns = md.GetString(td.Namespace);
                string nm = md.GetString(td.Name);
                if (useDefault)
                {
                    // 默认模式：只在语言框架相关命名空间里看那批关键类型
                    if (!ns.StartsWith("XRL.Language") && !ns.StartsWith("XRL.World.Text")) continue;
                    if (!Defaults.Contains(nm)) continue;
                }
                else
                {
                    // ★ 显式给词时：**全命名空间**搜（名字或完整名含该词）
                    string full = ns.Length == 0 ? nm : ns + "." + nm;
                    if (!filters.Any(f => nm.Contains(f, StringComparison.Ordinal)
                                       || full.Contains(f, StringComparison.Ordinal))) continue;
                }
                TypeAttributes a = td.Attributes;
                string vis = (a & TypeAttributes.VisibilityMask) switch
                {
                    TypeAttributes.Public => "Public",
                    TypeAttributes.NotPublic => "NotPublic(internal)",
                    TypeAttributes.NestedPublic => "NestedPublic",
                    TypeAttributes.NestedPrivate => "NestedPrivate",
                    TypeAttributes.NestedAssembly => "NestedAssembly",
                    TypeAttributes.NestedFamily => "NestedFamily",
                    _ => "其它",
                };
                rows.Add((ns, nm, vis, "0x" + ((int)a).ToString("X8")));
            }
            foreach (var r in rows.OrderBy(x => x.ns).ThenBy(x => x.nm))
                Console.WriteLine("  {0,-34} {1,-26} {2,-18} {3}", r.ns, r.nm, r.vis, r.flags);
            if (rows.Count == 0) Console.WriteLine("  （无匹配类型）");
        }
        return 0;
    }
}
