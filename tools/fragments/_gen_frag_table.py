# -*- coding: utf-8 -*-
"""
生成 `Scripts\\ZhsFragmentDictTable.cs` —— 把 `_tools\\_zh_fragments.py` 里的译文表
写成 C# 静态表（碎片 → 中文），供 `ZhsFragmentDictPatch` 在消息定稿处做兜底替换。

★ 为什么用生成器而不是手写 C#（本项目踩过 §"静态字典重复键"）：
   · 生成器**按 key 去重**并对"同 key 不同值"硬断言
   · 表按**长度降序**输出 ⇒ 运行时顺序替换即**贪心最长匹配**
   · 同时给每条编译一条**带词边界**的正则（`(?<![A-Za-z])…(?![A-Za-z])`）

用法：
    python _tools\\_gen_frag_table.py            # 干跑：报告条数与冲突
    python _tools\\_gen_frag_table.py --write     # 写文件
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GEN = os.path.join(ROOT, "_tools", "_zh_fragments.py")
OUT = os.path.join(ROOT, "mods", "QudZhsCookbook", "Scripts", "ZhsFragmentDictTable.cs")

# ★ 只有"在 C# 里拼出来"的碎片才进这张表（走的路径见 _tools\\_frag_reclass.py）：
#   A 类（走字符串表）= 已写进 Strings.xml，**不进本表**（否则重复且无必要）
A_CLASS = {"...", "DynamicQuest you recive an item", "You cannot do that here."}


def load_pairs():
    src = io.open(GEN, encoding="utf-8").read()
    ns = {}
    for name in ("TABLE", "INFINITIVE", "STATE", "COMBO0", "COMBO", "COMBO_CAP"):
        m = re.search(name + r"\s*=\s*\{", src)
        if not m:
            continue
        i = m.end() - 1
        depth, j = 0, i
        while j < len(src):
            if src[j] == "{":
                depth += 1
            elif src[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        ns[name] = eval(src[i:j + 1])
    return ns


def cs_escape(s):
    return (s.replace("\\", "\\\\").replace('"', '\\"')
             .replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t"))


def main():
    write = "--write" in sys.argv
    ns = load_pairs()
    pairs = {}
    for k, v in list(ns.get("TABLE", {}).items()) + \
                 list(ns.get("INFINITIVE", {}).items()) + \
                 list(ns.get("STATE", {}).items()) + \
                 list(ns.get("COMBO0", {}).items()) + \
                 list(ns.get("COMBO", {}).items()) + \
                 list(ns.get("COMBO_CAP", {}).items()):
        if k in pairs and pairs[k] != v:
            print("!! 冲突：%r 有两个译法 %r / %r" % (k, pairs[k], v))
            return 1
        pairs[k] = v

    before = len(pairs)
    pairs = {k: v for k, v in pairs.items() if k not in A_CLASS}
    print("译文表 %d 条 · 去掉 A 类（已在 Strings.xml）%d 条 ⇒ 本表 %d 条"
          % (before, before - len(pairs), len(pairs)))

    # 长的在前 ⇒ 贪心最长匹配
    items = sorted(pairs.items(), key=lambda kv: (-len(kv[0]), kv[0]))

    lines = [
        "// ═══════════════════════════════════════════════════════════════════════",
        "//  ZhsFragmentDictTable —— **自动生成，请勿手改**",
        "//  生成器：_tools\\_gen_frag_table.py（源表 _tools\\_zh_fragments.py）",
        "//  条数：%d · 按长度降序（运行时顺序替换 = 贪心最长匹配）" % len(items),
        "//",
        "//  ★ 为什么表与「编译/替换」逻辑都放这个文件：",
        "//    `ZhsFragmentDictPatch.cs` 引用了 `XRL.Messages.MessageQueue`，",
        "//    被本地 `mods\\_compilecheck` **排除**；本文件是**纯 C#**（只用 System.*），",
        "//    因此留在这里才能被四步收尾的 `dotnet build` 覆盖到。",
        "// ═══════════════════════════════════════════════════════════════════════",
        "using System;",
        "using System.Collections.Generic;",
        "using System.Text.RegularExpressions;",
        "",
        "namespace Nattens.QudZhsCookbook",
        "{",
        "    internal static class ZhsFragmentDictTable",
        "    {",
        "        /// <summary>编译好的碎片规则（表按长度降序 ⇒ 顺序替换即贪心最长匹配）。</summary>",
        "        internal static readonly List<KeyValuePair<Regex, string>> Rules =",
        "            new List<KeyValuePair<Regex, string>>();",
        "",
        "        /// <summary>把一条英文碎片编译成带词边界的正则。</summary>",
        "        /// ★ 左侧恒不接字母与点；**右侧只在碎片以字母结尾时**要求不接字母",
        "        ///   —— 以空格/标点结尾的是「前缀碎片」（如 `Because they despise `），",
        "        ///      后面紧跟名字（`Elder 伊鲁达德`）是**正常**的，不能拦。",
        "        internal static Regex Compile(string fragment)",
        "        {",
        "            // ★ 只看**字面最后一个字符**：以字母结尾才要求右边界。",
        "            //   \"Because they despise \" 以空格结尾 = 前缀拼接件 ⇒ **不加**（后面紧跟名字是正常的）",
        "            string right = \"\";",
        "            if (fragment.Length > 0)",
        "            {",
        "                char c = fragment[fragment.Length - 1];",
        "                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) right = \"(?![A-Za-z])\";",
        "            }",
        "            return new Regex(\"(?<![A-Za-z.])\" + Regex.Escape(fragment) + right,",
        "                RegexOptions.Compiled);",
        "        }",
        "",
        "        /// <summary>★ 对**定稿文本**做一次贪心最长匹配替换（幂等；含中文也照样只换登记的碎片）。</summary>",
        "        internal static string Apply(string Text)",
        "        {",
        "            if (Text == null || Text.Length == 0 || Rules.Count == 0) return Text;",
        "            string neo = Text;",
        "            for (int i = 0; i < Rules.Count; i++)",
        "            {",
        "                Regex rx = Rules[i].Key;",
        "                if (!rx.IsMatch(neo)) continue;",
        "                neo = rx.Replace(neo, Rules[i].Value);",
        "            }",
        "            return neo;",
        "        }",
        "",
        "        /// <summary>自检：已知句 + 已知反例，返回替换前后清单。</summary>",
        "        internal static string SelfCheck()",
        "        {",
        "            string[] cases =",
        "            {",
        "                \"湿的巨蜻蜓俯冲 down to attack.\",",
        "                \"may be ineffective unless stocked.\",",
        "                \"into pull\",",
        "                \"The 湿的 光鱼 down to attack.\",",
        "                \"你已经在地上 on the ground\",",
        "            };",
        "            var sb = new System.Text.StringBuilder();",
        "            for (int i = 0; i < cases.Length; i++)",
        "                sb.Append(\"  \").Append(cases[i]).Append(\"  ⇒  \").Append(Apply(cases[i])).Append('\\n');",
        "            return sb.ToString();",
        "        }",
        "",
        "        internal static void Load()",
        "        {",
        "            Rules.Clear();",
        "            KeyValuePair<Regex, string>[] rows =",
        "            {",
    ]
    for k, v in items:
        lines.append('                new KeyValuePair<Regex, string>(Compile("%s"), "%s"),'
                     % (cs_escape(k), cs_escape(v)))
    lines += [
        "            };",
        "            for (int i = 0; i < rows.Length; i++) Rules.Add(rows[i]);",
        "        }",
        "    }",
        "}",
        "",
    ]

    if not write:
        print("（干跑）前 8 条：")
        for k, v in items[:8]:
            print("   %-46s → %s" % (k[:46], v[:30]))
        print("加 --write 才写文件")
        return 0

    io.open(OUT, "w", encoding="utf-8", newline="\r\n").write("\n".join(lines))
    print("已写出 %s（%d 条）" % (os.path.relpath(OUT, ROOT), len(items)))

    # 自证：重新读回，条数与内容一致
    t = io.open(OUT, encoding="utf-8").read()
    got = len(re.findall(r"new KeyValuePair<Regex, string>\(", t))
    assert got == len(items), "自证失败：写回条数 %d != %d" % (got, len(items))
    print("自证 ✅ 写回条数 = %d" % got)
    return 0


if __name__ == "__main__":
    sys.exit(main())
