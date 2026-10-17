// ═══════════════════════════════════════════════════════════════════════════
//  fieldscan —— 扫 Assembly-CSharp.dll 的 IL，找**某个静态字段**的读写点。
//  用法：dotnet run -c Release -- <类型名> <字段名>
//  原理：遍历所有方法体的 IL 字节，找 0x80(stsfld)/0x7E(ldsfld) + 4 字节 token，
//        再把 token 解析成字段名比对（token 为 MemberRef 时取 Name+父类型名）。
//  ⚠ 字节扫描会有假阳性（其它指令的操作数可能恰好长得像）⇒ 命中处打印方法名，可人工核对。
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

internal static class FieldCommands
{
    /// <summary>探针：扫某静态字段的读写点（`stsfld` 0x80 / `ldsfld` 0x7E）。</summary>
    public static int Run(string asm, string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.WriteLine("用法: ilkit field <类型名> <字段名>");
            return 2;
        }
        string wantType = args[0], wantField = args[1];

        using (var fs = File.OpenRead(asm))
        using (var pe = new PEReader(fs))
        {
            MetadataReader md = pe.GetMetadataReader();
            int writers = 0, readers = 0;

            foreach (TypeDefinitionHandle tdh in md.TypeDefinitions)
            {
                TypeDefinition td = md.GetTypeDefinition(tdh);
                string tname = md.GetString(td.Name);
                string tns = td.Namespace.IsNil ? "" : md.GetString(td.Namespace);
                string full = tns.Length > 0 ? tns + "." + tname : tname;

                foreach (MethodDefinitionHandle mdh in td.GetMethods())
                {
                    MethodDefinition mdef = md.GetMethodDefinition(mdh);
                    if (mdef.RelativeVirtualAddress == 0) continue;
                    MethodBodyBlock body;
                    try { body = pe.GetMethodBody(mdef.RelativeVirtualAddress); }
                    catch { continue; }
                    var il = body.GetILBytes();
                    if (il == null) continue;
                    string mname = md.GetString(mdef.Name);

                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        byte op = il[i];
                        if (op != 0x80 && op != 0x7E) continue;   // stsfld / ldsfld
                        int tok = BitConverter.ToInt32(il, i + 1);
                        string owner, fname;
                        if (!Resolve(md, tok, out owner, out fname)) continue;
                        if (fname != wantField) continue;
                        if (wantType.Length > 0 && owner != null &&
                            owner.IndexOf(wantType, StringComparison.Ordinal) < 0) continue;
                        if (op == 0x80) { writers++; Console.WriteLine("[写] " + full + "::" + mname + "   (字段 " + owner + "::" + fname + ")"); }
                        else { readers++; if (readers <= 40) Console.WriteLine("[读] " + full + "::" + mname); }
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("合计：写 " + writers + " · 读 " + readers);
        }
        return 0;
    }

    private static bool Resolve(MetadataReader md, int tok, out string owner, out string name)
    {
        owner = null; name = null;
        // 只认字段表(0x04)与成员引用表(0x0A)，其余 token 直接丢（字节扫描会有大量假 token）
        int table = (int)((uint)tok >> 24);
        if (table != 0x04 && table != 0x0A) return false;
        try
        {
            var handle = MetadataTokens.EntityHandle(tok);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fd = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                name = md.GetString(fd.Name);
                var dt = fd.GetDeclaringType();
                var td = md.GetTypeDefinition(dt);
                string tns = td.Namespace.IsNil ? "" : md.GetString(td.Namespace);
                owner = (tns.Length > 0 ? tns + "." : "") + md.GetString(td.Name);
                return true;
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                name = md.GetString(mr.Name);
                if (mr.Parent.Kind == HandleKind.TypeReference)
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                    string tns = tr.Namespace.IsNil ? "" : md.GetString(tr.Namespace);
                    owner = (tns.Length > 0 ? tns + "." : "") + md.GetString(tr.Name);
                }
                else if (mr.Parent.Kind == HandleKind.TypeDefinition)
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent);
                    string tns = td.Namespace.IsNil ? "" : md.GetString(td.Namespace);
                    owner = (tns.Length > 0 ? tns + "." : "") + md.GetString(td.Name);
                }
                else owner = mr.Parent.Kind.ToString();
                return true;
            }
        }
        catch { }
        return false;
    }
}
