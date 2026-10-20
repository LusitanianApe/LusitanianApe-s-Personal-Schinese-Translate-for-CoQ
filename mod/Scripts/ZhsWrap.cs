// 中文硬折行：CoQ 书页不会对中文自动折行（中文没有空格，引擎的按词折行找不到断点），
// 所以中文长文本必须自己插入换行符。
//
// 实测数据（玩家用 optional\Books.ruler.xml 量的）：书页每行可完整显示 50 个全角字符。
// 为了显示效果，这里按 **46 全角 = 92 半角**折行，右侧留出约 4 个全角位的留白。
//
// 规则：
//   * 可见宽度：ASCII/半角 = 1，全角（含中文标点）= 2；
//     着色器定界符 {{code| 与 }} 宽度 0；占位符 =XXX= 按预计替换出来的名字长度计。
//   * 行首禁则：。，、；：？！）】》」』… 等收尾标点不能出现在行首 → 允许本行略超宽把它带上。
//   * 行尾禁则：（【《「『 等起始标点不能留在行尾 → 推到下一行。
//   * 换行绝不插进 {{...}} 或 =...= 内部。

using System;
using System.Collections.Generic;
using System.Text;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsWrap
    {
        /// <summary>每行可见宽度上限（半角单位）。按显示效果取 46 全角 = 92 半角
        /// （实测书页能容 50 全角，右侧留出约 4 个全角位的留白）。
        /// 改这里时请同步改 _tools\wrap_zh_lines.py 的 DEFAULT_MAX_HALF。</summary>
        public const int MaxHalfWidth = 92;

        // 不能出现在行首的收尾标点：碰到就拉回本行（本行允许略超）。
        // "（"也放这里：术语后紧跟的英文括注不能被换行拆开（"铁胫" / "（Ironshank）"分行），
        // 宁可本行略超，把"（"留在术语后面，括注里的英文再到下一行继续。
        private static readonly HashSet<char> CloseForbidden =
            new HashSet<char>("。，、；：？！）】》」』〉…—～·%”’（【《「『〈“‘".ToCharArray());
        private static readonly HashSet<char> OpenForbidden = new HashSet<char>();

        /// <summary>占位符 =XXX= 预计替换出来的半角宽度。
        /// 译名加了英文括注后，这里必须跟着变大，否则含占位符的行会溢出页面：
        /// 例如 =FCL= 最坏是"原生汤（primordial soup）"，=FCC= 最坏是"环节欢蠕虫尸骸（Segmented Mirthworm Corpse）"。</summary>
        private static int TokenWidth(string token)
        {
            switch (token)
            {
                case "IS1": case "MC1":
                case "GR1": case "GR2": case "GR3": return 14; // 沥青（Asphalt）
                case "FCL": return 24;                          // 原生汤（primordial soup）
                case "FCC": return 42;                          // 环节欢蠕虫尸骸（Segmented Mirthworm Corpse）
                default: return 10;
            }
        }

        private static int CharHalf(char c)
        {
            return c < 0x2000 ? 1 : 2;
        }

        /// <summary>对一段中文文本按可见宽度折行（保留原有空行）。</summary>
        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (HalfWidth(text) <= MaxHalfWidth) return text;

            List<string> outLines = new List<string>();
            string[] rawLines = text.Split('\n');
            foreach (string raw in rawLines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) { outLines.Add(""); continue; }
                if (line.StartsWith("[[", StringComparison.Ordinal)) { outLines.Add(line); continue; }
                foreach (string piece in WrapOne(line)) outLines.Add(piece);
            }
            return string.Join("\n", outLines.ToArray());
        }

        private static List<string> WrapOne(string line)
        {
            List<string> result = new List<string>();
            StringBuilder cur = new StringBuilder();
            int curHalf = 0;
            int i = 0;

            while (i < line.Length)
            {
                string unit;
                int width;
                char unitChar = '\0';

                // 着色器开始 {{code|  → 零宽
                if (i + 1 < line.Length && line[i] == '{' && line[i + 1] == '{')
                {
                    int bar = line.IndexOf('|', i + 2);
                    int close = line.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    if (bar > 0 && (close < 0 || bar < close))
                    {
                        cur.Append(line, i, bar - i + 1);
                        i = bar + 1;
                        continue;
                    }
                }
                // 着色器结束 }} → 零宽
                if (i + 1 < line.Length && line[i] == '}' && line[i + 1] == '}')
                {
                    cur.Append("}}");
                    i += 2;
                    continue;
                }
                // 占位符 =XXX=
                if (line[i] == '=')
                {
                    int end = line.IndexOf('=', i + 1);
                    if (end > i + 1 && end - i <= 9)
                    {
                        string token = line.Substring(i + 1, end - i - 1);
                        bool ok = true;
                        foreach (char c in token)
                        {
                            if (!char.IsLetterOrDigit(c)) { ok = false; break; }
                        }
                        if (ok)
                        {
                            unit = line.Substring(i, end - i + 1);
                            width = TokenWidth(token);
                            if (cur.Length > 0 && curHalf + width > MaxHalfWidth)
                            {
                                result.Add(cur.ToString().TrimEnd());
                                cur.Length = 0;
                                curHalf = 0;
                            }
                            cur.Append(unit);
                            curHalf += width;
                            i = end + 1;
                            continue;
                        }
                    }
                }

                unitChar = line[i];
                unit = unitChar.ToString();
                width = CharHalf(unitChar);

                if (cur.Length > 0 && curHalf + width > MaxHalfWidth)
                {
                    if (CloseForbidden.Contains(unitChar))
                    {
                        // 行首禁则：收尾标点宁可留在本行（本行略超）
                        cur.Append(unit);
                        result.Add(cur.ToString().TrimEnd());
                        cur.Length = 0;
                        curHalf = 0;
                        i++;
                        continue;
                    }
                    // 英文括注优先在**空格**处断行，避免把单词劈开
                    // （只在空格位置足够靠后、超过本行 60% 时才这么做，否则白费半行）
                    string carry = "";
                    int sp = -1;
                    for (int k = cur.Length - 1; k >= 0; k--)
                    {
                        if (cur[k] == ' ') { sp = k; break; }
                    }
                    if (sp >= 0 && sp >= cur.Length * 0.6)
                    {
                        carry = cur.ToString(sp + 1, cur.Length - sp - 1);
                        cur.Length = sp + 1;
                    }
                    // 行尾禁则：起始标点跟着下一行走
                    string pending = "";
                    while (cur.Length > 0 && OpenForbidden.Contains(cur[cur.Length - 1]))
                    {
                        pending = cur[cur.Length - 1] + pending;
                        cur.Length -= 1;
                    }
                    result.Add(cur.ToString().TrimEnd());
                    cur.Length = 0;
                    cur.Append(pending);
                    cur.Append(carry);
                    curHalf = HalfWidth(cur.ToString());
                }

                cur.Append(unit);
                curHalf += width;
                i++;
            }

            if (cur.Length > 0) result.Add(cur.ToString().TrimEnd());
            return result.Count > 0 ? result : new List<string> { line };
        }

        /// <summary>估算可见宽度（半角单位）。</summary>
        public static int HalfWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            int i = 0;
            while (i < s.Length)
            {
                if (i + 1 < s.Length && s[i] == '{' && s[i + 1] == '{')
                {
                    int bar = s.IndexOf('|', i + 2);
                    int close = s.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    if (bar > 0 && (close < 0 || bar < close)) { i = bar + 1; continue; }
                }
                if (i + 1 < s.Length && s[i] == '}' && s[i + 1] == '}') { i += 2; continue; }
                w += CharHalf(s[i]);
                i++;
            }
            return w;
        }
    }
}
