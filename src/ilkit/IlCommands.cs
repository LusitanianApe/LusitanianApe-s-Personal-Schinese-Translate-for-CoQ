// 极简 IL 反汇编器（只用 .NET BCL，不依赖任何 NuGet 包）。
//
// 用途：查清 Caves of Qud 里"某个方法到底做了什么"，特别是代词解析路径。
//   ildump <assembly> <TypeFullName> [methodName]
//   ildump <assembly> --find <methodNameSubstring>     （列出含该名字的方法）
//
// 输出：每条 IL 指令 + 解析后的成员引用（方法/字段/字符串），足以判断控制流。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class IlCommands
{
    private static MetadataReader md;

    /// <summary>IL 反汇编：列出某类型的每个方法（操作数已解析成成员/字符串）。</summary>
    public static int Run(string path, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  ilkit il <TypeFullName> [methodNameFilter]");
            Console.WriteLine("  ilkit il-find <nameSubstring>");
            return 2;
        }
        if (!File.Exists(path)) { Console.WriteLine("找不到: " + path); return 2; }

        using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var pe = new PEReader(fs))
        {
            md = pe.GetMetadataReader();

            if (args[0] == "--find")
            {
                FindMethods(args.Length > 1 ? args[1] : "");
                return 0;
            }

            string typeName = args[0];
            string methodFilter = args.Length > 1 ? args[1] : null;
            DumpType(pe, typeName, methodFilter);
        }
        return 0;
    }

    private static void FindMethods(string sub)
    {
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            string tn = FullName(td);
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                string mn = md.GetString(m.Name);
                if (mn.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine(tn + "::" + mn);
            }
        }
    }

    private static string FullName(TypeDefinition td)
    {
        string ns = md.GetString(td.Namespace);
        string n = md.GetString(td.Name);
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }

    private static void DumpType(PEReader pe, string typeName, string methodFilter)
    {
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            string full = FullName(td);
            if (!string.Equals(full, typeName, StringComparison.Ordinal)) continue;

            Console.WriteLine("=== " + full);
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                string mn = md.GetString(m.Name);
                if (methodFilter != null && mn.IndexOf(methodFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Console.WriteLine();
                Console.WriteLine("--- " + mn + "  (RVA 0x" + m.RelativeVirtualAddress.ToString("X") + ")");
                if (m.RelativeVirtualAddress == 0) { Console.WriteLine("    (无方法体：abstract/extern)"); continue; }
                try
                {
                    var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                    DumpIl(body.GetILBytes());
                }
                catch (Exception e) { Console.WriteLine("    读取方法体失败: " + e.Message); }
            }
            return;
        }
        Console.WriteLine("未找到类型: " + typeName);
    }

    private static void DumpIl(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            int start = i;
            int op = il[i++];
            if (op == 0xFE) { op = 0xFE00 | il[i++]; }

            string name;
            OperandKind kind;
            if (!OpCodes.TryGetValue(op, out name))
            {
                Console.WriteLine("    " + start.ToString("X4") + ": <未知操作码 0x" + op.ToString("X2") + ">");
                break;
            }
            kind = OperandOf(op);

            string operandText = "";
            switch (kind)
            {
                case OperandKind.None:
                    break;
                case OperandKind.Int8:
                    operandText = ((sbyte)il[i]).ToString(); i += 1; break;
                case OperandKind.UInt8:
                    operandText = il[i].ToString(); i += 1; break;
                case OperandKind.Int16:
                    operandText = BitConverter.ToInt16(il, i).ToString(); i += 2; break;
                case OperandKind.Int32:
                    operandText = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
                case OperandKind.Int64:
                    operandText = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
                case OperandKind.Float32:
                    operandText = BitConverter.ToSingle(il, i).ToString(); i += 4; break;
                case OperandKind.Float64:
                    operandText = BitConverter.ToDouble(il, i).ToString(); i += 8; break;
                case OperandKind.Token:
                {
                    int tok = BitConverter.ToInt32(il, i); i += 4;
                    operandText = ResolveToken(tok);
                    break;
                }
                case OperandKind.Branch8:
                    operandText = "IL_" + (i + 1 + (sbyte)il[i]).ToString("X4"); i += 1; break;
                case OperandKind.Branch32:
                    operandText = "IL_" + (i + 4 + BitConverter.ToInt32(il, i)).ToString("X4"); i += 4; break;
                case OperandKind.Switch:
                {
                    int n = BitConverter.ToInt32(il, i); i += 4;
                    var parts = new List<string>();
                    for (int k = 0; k < n; k++) { parts.Add("IL_" + (i + 4 * n + BitConverter.ToInt32(il, i)).ToString("X4")); i += 4; }
                    operandText = "[" + string.Join(", ", parts) + "]";
                    break;
                }
            }
            Console.WriteLine("    IL_" + start.ToString("X4") + ": " + name + (operandText.Length > 0 ? " " + operandText : ""));
        }
    }

    private static string ResolveToken(int token)
    {
        try
        {
            var h = (Handle)MetadataTokens.Handle(token);
            switch (h.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var mr = md.GetMemberReference((MemberReferenceHandle)h);
                    string parent = ResolveParent(mr.Parent);
                    return parent + "::" + md.GetString(mr.Name);
                }
                case HandleKind.MethodDefinition:
                {
                    var mdef = md.GetMethodDefinition((MethodDefinitionHandle)h);
                    var t = md.GetTypeDefinition(mdef.GetDeclaringType());
                    return FullName(t) + "::" + md.GetString(mdef.Name);
                }
                case HandleKind.FieldDefinition:
                {
                    var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                    string tn = "";
                    try { tn = FullName(md.GetTypeDefinition(f.GetDeclaringType())); } catch { }
                    return tn + "::" + md.GetString(f.Name);
                }
                case HandleKind.MethodSpecification:
                {
                    var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
                    return ResolveToken(MetadataTokens.GetToken(ms.Method)) + "<...>";
                }
                case HandleKind.TypeReference:
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)h);
                    string ns = md.GetString(tr.Namespace);
                    string n = md.GetString(tr.Name);
                    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
                }
                case HandleKind.TypeDefinition:
                    return FullName(md.GetTypeDefinition((TypeDefinitionHandle)h));
                case HandleKind.TypeSpecification:
                    return "<typespec>";
                case HandleKind.UserString:
                    return "\"" + md.GetUserString((UserStringHandle)h) + "\"";
                case HandleKind.String:
                    return "\"" + md.GetString((StringHandle)h) + "\"";
                default:
                    return "token(0x" + token.ToString("X8") + ")";
            }
        }
        catch { return "token(0x" + token.ToString("X8") + ")"; }
    }

    private static string ResolveParent(Handle parent)
    {
        try
        {
            switch (parent.Kind)
            {
                case HandleKind.TypeReference:
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)parent);
                    string ns = md.GetString(tr.Namespace), n = md.GetString(tr.Name);
                    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
                }
                case HandleKind.TypeDefinition:
                    return FullName(md.GetTypeDefinition((TypeDefinitionHandle)parent));
                case HandleKind.TypeSpecification:
                    return "<typespec>";
                default:
                    return "?";
            }
        }
        catch { return "?"; }
    }

    private enum OperandKind { None, Int8, UInt8, Int16, Int32, Int64, Float32, Float64, Token, Branch8, Branch32, Switch }

    // 只覆盖常用操作码（够用了）
    private static readonly Dictionary<int, string> OpCodes = new Dictionary<int, string>
    {
        {0x00,"nop"},{0x01,"break"},{0x02,"ldarg.0"},{0x03,"ldarg.1"},{0x04,"ldarg.2"},{0x05,"ldarg.3"},
        {0x06,"ldloc.0"},{0x07,"ldloc.1"},{0x08,"ldloc.2"},{0x09,"ldloc.3"},
        {0x0A,"stloc.0"},{0x0B,"stloc.1"},{0x0C,"stloc.2"},{0x0D,"stloc.3"},
        {0x0E,"ldarg.s"},{0x0F,"ldarga.s"},{0x10,"starg.s"},{0x11,"ldloc.s"},{0x12,"ldloca.s"},{0x13,"stloc.s"},
        {0x14,"ldnull"},{0x15,"ldc.i4.m1"},{0x16,"ldc.i4.0"},{0x17,"ldc.i4.1"},{0x18,"ldc.i4.2"},{0x19,"ldc.i4.3"},
        {0x1A,"ldc.i4.4"},{0x1B,"ldc.i4.5"},{0x1C,"ldc.i4.6"},{0x1D,"ldc.i4.7"},{0x1E,"ldc.i4.8"},
        {0x1F,"ldc.i4.s"},{0x20,"ldc.i4"},{0x21,"ldc.i8"},{0x22,"ldc.r4"},{0x23,"ldc.r8"},
        {0x25,"dup"},{0x26,"pop"},{0x27,"jmp"},{0x28,"call"},{0x29,"calli"},{0x2A,"ret"},
        {0x2B,"br.s"},{0x2C,"brfalse.s"},{0x2D,"brtrue.s"},{0x2E,"beq.s"},{0x2F,"bge.s"},{0x30,"bgt.s"},
        {0x31,"ble.s"},{0x32,"blt.s"},{0x33,"bne.un.s"},{0x34,"bge.un.s"},{0x35,"bgt.un.s"},{0x36,"ble.un.s"},{0x37,"blt.un.s"},
        {0x38,"br"},{0x39,"brfalse"},{0x3A,"brtrue"},{0x3B,"beq"},{0x3C,"bge"},{0x3D,"bgt"},{0x3E,"ble"},{0x3F,"blt"},
        {0x40,"bne.un"},{0x41,"bge.un"},{0x42,"bgt.un"},{0x43,"ble.un"},{0x44,"blt.un"},{0x45,"switch"},
        {0x46,"ldind.i1"},{0x47,"ldind.u1"},{0x48,"ldind.i2"},{0x49,"ldind.u2"},{0x4A,"ldind.i4"},{0x4B,"ldind.u4"},
        {0x4C,"ldind.i8"},{0x4D,"ldind.i"},{0x4E,"ldind.r4"},{0x4F,"ldind.r8"},{0x50,"ldind.ref"},
        {0x51,"stind.ref"},{0x52,"stind.i1"},{0x53,"stind.i2"},{0x54,"stind.i4"},{0x55,"stind.i8"},{0x56,"stind.r4"},{0x57,"stind.r8"},
        {0x58,"add"},{0x59,"sub"},{0x5A,"mul"},{0x5B,"div"},{0x5C,"div.un"},{0x5D,"rem"},{0x5E,"rem.un"},
        {0x5F,"and"},{0x60,"or"},{0x61,"xor"},{0x62,"shl"},{0x63,"shr"},{0x64,"shr.un"},{0x65,"neg"},{0x66,"not"},
        {0x67,"conv.i1"},{0x68,"conv.i2"},{0x69,"conv.i4"},{0x6A,"conv.i8"},{0x6B,"conv.r4"},{0x6C,"conv.r8"},
        {0x6F,"callvirt"},{0x70,"cpobj"},{0x71,"ldobj"},{0x72,"ldstr"},{0x73,"newobj"},{0x74,"castclass"},{0x75,"isinst"},
        {0x76,"conv.r.un"},{0x79,"unbox"},{0x7A,"throw"},{0x7B,"ldfld"},{0x7C,"ldflda"},{0x7D,"stfld"},
        {0x7E,"ldsfld"},{0x7F,"ldsflda"},{0x80,"stsfld"},{0x81,"stobj"},{0x8C,"box"},{0x8D,"newarr"},
        {0x8E,"ldlen"},{0x8F,"ldelema"},{0x90,"ldelem.i1"},{0x91,"ldelem.u1"},{0x92,"ldelem.i2"},{0x93,"ldelem.u2"},
        {0x94,"ldelem.i4"},{0x95,"ldelem.u4"},{0x96,"ldelem.i8"},{0x97,"ldelem.i"},{0x98,"ldelem.r4"},{0x99,"ldelem.r8"},
        {0x9A,"ldelem.ref"},{0x9B,"stelem.i"},{0x9C,"stelem.i1"},{0x9D,"stelem.i2"},{0x9E,"stelem.i4"},{0x9F,"stelem.i8"},
        {0xA0,"stelem.r4"},{0xA1,"stelem.r8"},{0xA2,"stelem.ref"},{0xA3,"ldelem"},{0xA4,"stelem"},{0xA5,"unbox.any"},
        {0xB3,"conv.ovf.i1"},{0xB4,"conv.ovf.u1"},{0xB5,"conv.ovf.i2"},{0xB6,"conv.ovf.u2"},{0xB7,"conv.ovf.i4"},{0xB8,"conv.ovf.u4"},
        {0xB9,"conv.ovf.i8"},{0xBA,"conv.ovf.u8"},{0xC2,"refanyval"},{0xC3,"ckfinite"},{0xC6,"mkrefany"},
        {0xD0,"ldtoken"},{0xD1,"conv.u2"},{0xD2,"conv.u1"},{0xD3,"conv.i"},{0xD4,"conv.ovf.i"},{0xD5,"conv.ovf.u"},
        {0xD6,"add.ovf"},{0xD7,"add.ovf.un"},{0xD8,"mul.ovf"},{0xD9,"mul.ovf.un"},{0xDA,"sub.ovf"},{0xDB,"sub.ovf.un"},
        {0xDC,"endfinally"},{0xDD,"leave"},{0xDE,"leave.s"},{0xDF,"stind.i"},{0xE0,"conv.u"},
        {0xFE01,"ceq"},{0xFE02,"cgt"},{0xFE03,"cgt.un"},{0xFE04,"clt"},{0xFE05,"clt.un"},
        {0xFE06,"ldftn"},{0xFE07,"ldvirtftn"},
        {0xFE09,"ldarg"},{0xFE0A,"ldarga"},{0xFE0B,"starg"},{0xFE0C,"ldloc"},{0xFE0D,"ldloca"},{0xFE0E,"stloc"},
        {0xFE0F,"localloc"},{0xFE11,"endfilter"},{0xFE12,"unaligned."},{0xFE13,"volatile."},{0xFE14,"tail."},
        {0xFE15,"initobj"},{0xFE16,"constrained."},{0xFE17,"cpblk"},{0xFE18,"initblk"},{0xFE19,"no."},
        {0xFE1C,"sizeof"},{0xFE1D,"refanytype"},{0xFE1E,"readonly."},
    };

    private static OperandKind OperandOf(int op)
    {
        switch (op)
        {
            case 0x0E: case 0x0F: case 0x10: case 0x11: case 0x12: case 0x13:
            case 0x1F: case 0x2B: case 0x2C: case 0x2D: case 0x2E: case 0x2F:
            case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x35:
            case 0x36: case 0x37: case 0xDE:
                return OperandKind.Int8;
            case 0xFE09: case 0xFE0A: case 0xFE0B: case 0xFE0C: case 0xFE0D: case 0xFE0E:
                return OperandKind.Int16;
            case 0x20: case 0x22: case 0x38: case 0x39: case 0x3A: case 0x3B: case 0x3C:
            case 0x3D: case 0x3E: case 0x3F: case 0x40: case 0x41: case 0x42: case 0x43:
            case 0x44: case 0xDD:
                return OperandKind.Branch32;
            case 0x21: return OperandKind.Int64;
            case 0x23: return OperandKind.Float64;
            case 0x45: return OperandKind.Switch;
            case 0x27: case 0x28: case 0x29: case 0x6F: case 0x70: case 0x71: case 0x72:
            case 0x73: case 0x74: case 0x75: case 0x79: case 0x7B: case 0x7C: case 0x7D:
            case 0x7E: case 0x7F: case 0x80: case 0x81: case 0x8C: case 0x8D: case 0x8F:
            case 0x9B: case 0xA3: case 0xA4: case 0xA5: case 0xB3: case 0xB4: case 0xB5:
            case 0xB6: case 0xB7: case 0xB8: case 0xB9: case 0xBA: case 0xC2: case 0xC6:
            case 0xD0: case 0xFE06: case 0xFE07: case 0xFE15: case 0xFE16: case 0xFE1C:
                return OperandKind.Token;
            default:
                return OperandKind.None;
        }
    }
}
