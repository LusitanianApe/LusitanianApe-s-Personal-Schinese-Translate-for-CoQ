// ═══════════════════════════════════════════════════════════════════════════
// 英文数词 → 中文（用户 2026-10-07：「先把数词→中文做掉喵」）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】replacer 自测（`Local\VariableReplacers.txt`）实证：
//     =1.cardinal.names#"Arc Cannon"=      → "one奇异遗物"     != "one weird artifact"
//     =2.cardinal.names#"Arc Cannon"=      → "two奇异遗物"     != "two weird artifacts"
//     =37.a.or.cardinal.names#"HE Missile"=→ "thirty-seven 奇异遗物" != "thirty-seven weird artifacts"
//     =100.a.or.cardinal.names#Corpus=     → "one hundred {{W|科利伊斯医典}}的" != "one hundred copies of …"
//     =water.cardinal.drams.of.liquid:5=   → "five 德拉姆（dram）的{{B|水}}" != "five drams of …"
//   ⇒ **名词已中文，数词还是英文** ⇒ 中英混排。
//
// 【根因（反编译）】所有这一族最终都走：
//     XRL.Language.Translator.Cardinal(long num)   → provider.Cardinal(num)
//     XRL.Language.Translator.CardinalNo(long num) → provider.CardinalNo(num)
//   而 `provider` 默认是 **TranslatorEnglish** ⇒ 产出英文数词。
//   调用点（`decomp find Translator.Cardinal`）：
//     NumberReplacers.Cardinal / CardinalNo / CardinalThings / CardinalNoThings /
//     CardinalNames / AOrNumber / Penetrations …     ← 全是这一族
//     GameStateReplacers.NumeralTransform            ← 同一族
//     Grammar.IndefiniteArticleShouldBeAn            ← 判「a/an」，中文场景会被去冠词覆盖
//   ⇒ **挂 `Translator.Cardinal(long)` / `CardinalNo(long)` 一处即全覆盖**。
//
// 【做法】挂 **Postfix**：结果**恰好是英文数词**时换成中文；否则原样放行。
//     · 白名单式匹配（`one` … `ninety-nine` / `one hundred` / `one thousand` …）
//     · 只认**整串**是数词 ⇒ 绝不误伤（如 `第二` / 名词里的 `one` 不会命中，
//       因为它们不是「整串恰好等于数词」）
//     · 精确映射表：1~99 全覆盖 + 百/千/百万/十亿
//
// 【安全】全部 try/catch；`_zhs_number_off.txt` 可停用；异常一律返回原值。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsNumberZh
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>个位。</summary>
        private static readonly string[] U =
        { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

        /// <summary>英文个位 ⇒ 值。</summary>
        private static readonly Dictionary<string, int> ONES =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", 0 }, { "one", 1 }, { "two", 2 }, { "three", 3 },
            { "four", 4 }, { "five", 5 }, { "six", 6 }, { "seven", 7 },
            { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 },
            { "twelve", 12 }, { "thirteen", 13 }, { "fourteen", 14 },
            { "fifteen", 15 }, { "sixteen", 16 }, { "seventeen", 17 },
            { "eighteen", 18 }, { "nineteen", 19 },
        };

        /// <summary>英文十位 ⇒ 值。</summary>
        private static readonly Dictionary<string, int> TENS =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 },
            { "fifty", 50 }, { "sixty", 60 }, { "seventy", 70 },
            { "eighty", 80 }, { "ninety", 90 },
        };

        /// <summary>
        /// 1~99 ⇒ 中文。
        /// ★★ 十位前导「一」的规则（用户指出 `110` 要写「一百一十」而非「一百十」）：
        ///     有更高位（百/千/万）时 ⇒ **保留**前导「一」：110 → 一百**一十**
        ///     无更高位时           ⇒ **省略**前导「一」：10 → 十、11 → 十一
        ///   ⇒ 由 `lead` 参数控制，默认 false（即 10 / 11 那种省略形态）。
        /// </summary>
        private static string ZhBelow100(int n, bool lead = false)
        {
            if (n < 10) return U[n];
            if (n < 20)
            {
                return (n == 10)
                    ? (lead ? "一十" : "十")
                    : (lead ? "一十" : "十") + U[n - 10];
            }
            int t = n / 10, o = n % 10;
            return U[t] + "十" + (o == 0 ? "" : U[o]);
        }

        /// <summary>
        /// 任意值 ⇒ 中文（万进制）。
        ///
        /// ★★ 「零」占位规则（用户 2026-10-07 指出：`105` 不能写「一百五」）：
        ///     规则：高位取完后，**若剩余部分小于低一级的单位**（即中间有 0 占位），
        ///           则在拼接处插一个「零」。
        ///       例：105   → 一百 + 零 + 五      （余 5 < 10）
        ///           1005  → 一千 + 零 + 五      （余 5 < 100）
        ///           10008 → 一万 + 零 + 八      （余 8 < 1000）
        ///           1500  → 一千五百           （余 500 **不** < 100 ⇒ 不插）
        ///           1050  → 一千零五十          （余 50 < 100 ⇒ 插）
        ///   ⚠ 若不插「零」，`一百五` 会被读成 **150** —— 语义完全错。
        /// </summary>
        internal static string Zh(long n)
        {
            return Zh(n, false);
        }

        /// <summary>
        /// ★ `lead`：本次是**独立数字**（false）还是**跟在更高位之后**（true）。
        ///   跟在更高位后 ⇒ 十位保留前导「一」：1010 → 一千零**一十**（而非「一千零十」）。
        /// </summary>
        internal static string Zh(long n, bool lead)
        {
            if (n < 0) return "负" + Zh(-n, false);
            if (n < 100) return ZhBelow100((int)n, lead);
            if (n < 1000)
            {
                long h = n / 100, r = n % 100;
                if (r == 0) return U[h] + "百";
                // ★ 有百位 ⇒ 十位保留前导「一」（110 → 一百一十）
                return U[h] + "百" + (r < 10 ? "零" : "") + ZhBelow100((int)r, true);
            }
            if (n < 10000)
            {
                long k = n / 1000, r = n % 1000;
                if (r == 0) return U[k] + "千";
                // ★ 有千位 ⇒ 余数内一律按「跟在更高位后」处理
                //   （1005 → 一千零五 · 1010 → 一千零一十 · 1050 → 一千零五十）
                return U[k] + "千" + (r < 100 ? "零" : "") + Zh(r, true);
            }
            long[] units = { 100000000L, 10000L };   // 亿 / 万
            string[] names = { "亿", "万" };
            for (int i = 0; i < units.Length; i++)
            {
                if (n >= units[i])
                {
                    long q = n / units[i], r = n % units[i];
                    if (r == 0) return Zh(q, false) + names[i];
                    return Zh(q, false) + names[i]
                         + (r < units[i] / 10L ? "零" : "") + Zh(r, true);
                }
            }
            return n.ToString();
        }

        /// <summary>
        /// 英文数词整串 ⇒ 数值；不是数词返回 -1。
        /// 支持：`one` / `thirty-seven` / `one hundred` / `one hundred and five` /
        ///       `two thousand` / `one million` …
        /// </summary>
        private static readonly Regex NumWord = new Regex(
            @"^(?<neg>minus\s+)?(?<w>[A-Za-z]+(?:[-\s]+(?:and\s+)?[A-Za-z]+)*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, long> SCALES =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            { "hundred", 100L }, { "thousand", 1000L },
            { "million", 1000000L }, { "billion", 1000000000L },
        };

        internal static long ParseEnglish(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.Trim();
            bool neg = false;
            if (s.StartsWith("minus ", StringComparison.OrdinalIgnoreCase))
            {
                neg = true;
                s = s.Substring(6).Trim();
            }
            if (!NumWord.IsMatch(s)) return -1;
            // 连字符 → 空格
            s = s.Replace('-', ' ');
            string[] parts = Regex.Split(s, @"\s+");
            long total = 0, cur = 0;
            bool any = false;
            foreach (string raw in parts)
            {
                string w = raw.Trim();
                if (w.Length == 0) continue;
                if (w.Equals("and", StringComparison.OrdinalIgnoreCase)) continue;
                int v;
                long sc;
                if (ONES.TryGetValue(w, out v))
                {
                    cur += v; any = true; continue;
                }
                if (TENS.TryGetValue(w, out v))
                {
                    cur += v; any = true; continue;
                }
                if (SCALES.TryGetValue(w, out sc))
                {
                    if (sc == 100L) cur = (cur == 0 ? 1 : cur) * 100L;
                    else { total += (cur == 0 ? 1 : cur) * sc; cur = 0; }
                    any = true; continue;
                }
                return -1;                     // 有非数词 ⇒ 不是数词串
            }
            if (!any) return -1;
            long r = total + cur;
            return neg ? -r : r;
        }

        /// <summary>核心：英文数词 ⇒ 中文；不是数词则返回 null（调用方保留原文）。</summary>
        internal static string Convert(string s)
        {
            long v = ParseEnglish(s);
            if (v < 0 && !(s ?? "").TrimStart().StartsWith("minus", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (v == long.MinValue) return null;
            return Zh(v);
        }
    }

    /// <summary>把转换挂到 `Translator.Cardinal(long)` / `CardinalNo(long)` 上。</summary>
    internal static class ZhsNumberZhPatch
    {
        private const string Tag = "[QudZhsCookbook] 数词汉化：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsNumberZh.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.Language.Translator", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Translator");
                    return;
                }
                MethodInfo post = typeof(ZhsNumberZhPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.numberzh");
                int n = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                             | BindingFlags.Static))
                {
                    if (m.Name != "Cardinal" && m.Name != "CardinalNo") continue;
                    if (m.ReturnType != typeof(string)) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    // 只挂 `long` / `int` 两个标量重载（跳过 Provider 相关）
                    if (ps[0].ParameterType != typeof(long)
                        && ps[0].ParameterType != typeof(int)) continue;
                    try
                    {
                        h.Patch(m, postfix: new HarmonyMethod(post));
                        n++;
                        UnityEngine.Debug.Log(Tag + "已挂 Translator." + m.Name
                            + "(" + ps[0].ParameterType.Name + ")");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log(Tag + "跳过 " + m.Name + "："
                            + e.Message);
                    }
                }
                UnityEngine.Debug.Log(Tag + "共挂 " + n + " 个出口（英文数词 → 中文）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                if (ZhsNumberZh.Disabled || string.IsNullOrEmpty(__result)) return;
                string zh = ZhsNumberZh.Convert(__result);
                if (zh == null || zh == __result) return;
                if (ZhsNumberZh.Hits < 6)
                {
                    UnityEngine.Debug.Log(Tag + __result + " => " + zh);
                }
                ZhsNumberZh.Hits++;
                __result = zh;
            }
            catch { }
        }
    }
}
