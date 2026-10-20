// ═══════════════════════════════════════════════════════════════════════════
// 属性/突变点数弹窗汉化 —— 接管 `Popup.Show` 里**运行时拼出来的英文骨架**
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图（严查）】
//     Your Intelligence is 13.
//
//     It will cost 1 attribute point to increase Intelligence by 1.
//     Do you wish to increase this attribute?
//     [y] 是   [n] 否              ← 底栏是中文（说明弹窗本身那套是好的）
//
// 【反编译实证 —— `XRL.UI.StatusScreen::BuyStat`】
//     string text = ((value == baseValue)
//         ? ("Your " + ChosenStat + " is {{C|" + baseValue + "}}")
//         : ("Your base " + ChosenStat + " is {{C|" + baseValue
//            + "}}, modified to {{G|" + value + "}}"));
//     text += ".\n\n";
//     if (statistic.BaseValue >= 100)
//         Popup.Show(text + "You may not raise an attribute above 100.");
//     else if (GO.Stat("AP") >= 1)
//         Popup.ShowYesNo(text + "It will cost {{C|1}} attribute point to increase "
//                         + ChosenStat + " by 1.\nDo you wish to increase this attribute?");
//     else
//         Popup.Show(text + "You have no attribute points to raise this attribute.");
//     …
//     Popup.Show("You have increased your " + ChosenStat + " to {{C|" + statistic.Value + "}}!");
//   ⇒ **全是运行时字符串拼接**，既不进字符串表、也不带 `_T(Context, ID)`
//     ⇒ 任何 XML 都改不到。
//   `XRL.UI.StatusScreen::ShowMutationPopup` 里也有同样的 `will cost` 拼接。
//
// 【做法】Postfix 挂 `XRL.UI.Popup.Show(string, …)`，对**形态明确**的骨架做定点重写：
//     · 规则用**锚定正则**，且必须整段匹配关键结构 ⇒ 不会误伤普通弹窗
//     · `ChosenStat`（Intelligence 等）走我方已有的 `Statistics.xml` 译名
//       映射（拿不到就保留英文，绝不硬编造）
//     · 只改文本，不动弹窗行为（`ShowYesNo` 的返回值不受影响）
//
// 【安全】`_zhs_statpopup_off.txt` 可停用；异常一律放行原文。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsStatPopup
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>属性英文名 → 中文（与 `Statistics.xml` 的 displayName 对齐）。</summary>
        internal static readonly Dictionary<string, string> STAT =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Strength", "力量" }, { "Agility", "敏捷" },
            { "Toughness", "坚韧" }, { "Intelligence", "智力" },
            { "Willpower", "意志" }, { "Ego", "自我" },
            { "Hitpoints", "生命值" }, { "AV", "护甲值" }, { "DV", "闪避值" },
            { "MoveSpeed", "移动速度" }, { "MP", "突变点" }, { "AP", "属性点" },
        };

        internal static string St(string en)
        {
            string zh;
            return STAT.TryGetValue(en ?? "", out zh) ? zh : (en ?? "");
        }

        // ★★ 注意：`BuyStat` 里有 `text += ".\n\n";`
        //   ⇒ 第一段实际是 **`Your Intelligence is {{C|13}}.`（带句点）**
        //   ⇒ 正则必须容忍可选的结尾句点，否则第一句漏译（离线验证抓到过这个 bug）。
        // ── ① `Your X is {{C|n}}.` ──
        private static readonly Regex R1 = new Regex(
            @"^Your (?<stat>[A-Za-z]+) is (?<v>\{\{[^}]*\}\}|\d+)\.?$",
            RegexOptions.Compiled);
        // ── ①b `Your base X is {{C|n}}, modified to {{G|n}}.` ──
        private static readonly Regex R1b = new Regex(
            @"^Your base (?<stat>[A-Za-z]+) is (?<b>\{\{[^}]*\}\}|\d+), modified to (?<m>\{\{[^}]*\}\}|\d+)\.?$",
            RegexOptions.Compiled);
        // ── ② `It will cost {{C|1}} attribute point to increase X by 1.\nDo you wish to increase this attribute?` ──
        private static readonly Regex R2 = new Regex(
            @"^It will cost (?<cost>\{\{[^}]*\}\}|\d+) attribute points? to increase (?<stat>[A-Za-z]+) by (?<by>\d+)\.\s*\n\s*Do you wish to increase this attribute\?$",
            RegexOptions.Compiled);
        // ── ③ 其它固定的三句 ──
        private static readonly Dictionary<string, string> EXACT =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "You may not raise an attribute above 100.", "属性不能提升到 100 以上。" },
            { "You have no attribute points to raise this attribute.",
              "你没有可用于提升此属性的属性点。" },
        };
        private static readonly Regex R4 = new Regex(
            @"^You have increased your (?<stat>[A-Za-z]+) to (?<v>\{\{[^}]*\}\}|\d+)!$",
            RegexOptions.Compiled);
        private static readonly Regex R5 = new Regex(
            @"^You have increased your (?<stat>[A-Za-z]+) to (?<v>\{\{[^}]*\}\}|\d+)\.$",
            RegexOptions.Compiled);

        /// <summary>把整个弹窗文本按 `\n\n` 分段，逐段尝试重写。</summary>
        internal static string Fix(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            string[] parts = msg.Split(new string[] { "\n\n" }, StringSplitOptions.None);
            bool any = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string one = FixOne(parts[i].Trim());
                if (one != null)
                {
                    parts[i] = one;
                    any = true;
                }
            }
            return any ? string.Join("\n\n", parts) : null;
        }

        private static string FixOne(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string e;
            if (EXACT.TryGetValue(s, out e)) return e;
            Match m = R2.Match(s);
            if (m.Success)
            {
                return "提升 1 点" + St(m.Groups["stat"].Value) + "需要 " + m.Groups["cost"].Value
                     + " 点属性点。\n是否提升此属性？";
            }
            m = R1.Match(s);
            if (m.Success)
                return "你的" + St(m.Groups["stat"].Value) + "为 " + m.Groups["v"].Value + "。";
            m = R1b.Match(s);
            if (m.Success)
                return "你的" + St(m.Groups["stat"].Value) + "基础值为 " + m.Groups["b"].Value
                     + "，修正为 " + m.Groups["m"].Value + "。";
            m = R4.Match(s);
            if (m.Success)
                return "你把" + St(m.Groups["stat"].Value) + "提升到了 " + m.Groups["v"].Value + "！";
            m = R5.Match(s);
            if (m.Success)
                return "你把" + St(m.Groups["stat"].Value) + "提升到了 " + m.Groups["v"].Value + "。";
            return null;
        }
    }

    /// <summary>挂 `XRL.UI.Popup.Show` / `ShowYesNo` 的 Prefix（改文本，不动行为）。</summary>
    internal static class ZhsStatPopupPatch
    {
        private const string Tag = "[QudZhsCookbook] 属性弹窗：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsStatPopup.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.UI.Popup", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.UI.Popup");
                    return;
                }
                MethodInfo pre = typeof(ZhsStatPopupPatch).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.statpopup");
                int n = 0;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (mi.Name != "Show" && mi.Name != "ShowYesNo") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(pre));
                        n++;
                    }
                    catch { }
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 Popup 出口"
                    + "（属性/突变点数弹窗的拼接骨架）");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + ex);
            }
        }

        private static void Prefix(ref string __0)
        {
            if (ZhsStatPopup.Disabled) return;
            try
            {
                string zh = ZhsStatPopup.Fix(__0);
                if (zh == null) return;
                if (ZhsStatPopup.Hits < 8)
                {
                    UnityEngine.Debug.Log(Tag + "重写：" + __0.Replace("\n", "⏎")
                        + "  =>  " + zh.Replace("\n", "⏎"));
                }
                ZhsStatPopup.Hits++;
                __0 = zh;
            }
            catch { }
        }
    }
}
