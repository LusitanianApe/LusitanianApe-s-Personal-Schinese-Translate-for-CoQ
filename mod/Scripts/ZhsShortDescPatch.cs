// ═══════════════════════════════════════════════════════════════════════════
// 短描述后置串汉化 —— 挂 `TextBuilder.Append(string)` 的**入口替换**
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图「严查」】查看生物卡片里两行英文：
//     Base demeanor: {{g|docile}}
//     （以及 `[swimming]` —— 那条是效果名硬编码，已由 `ZhsEffectNameCollectPatch` 修）
//
// 【反编译实证 —— `XRL.World.Parts.Brain::HandleEvent(GetShortDescriptionEvent E)`】
//     if (Hostile)  E.Postfix.Append("\nBase demeanor: {{r|aggressive}}");
//     if (Calm)     E.Postfix.Append("\nBase demeanor: {{g|docile}}");
//     if (Passive)  E.Postfix.Append("\nEngagement style: {{g|defensive}}");
//     else if (IsPlayerLed())
//                   E.Postfix.Append("\nEngagement style: {{r|aggressive}}");
//   ⇒ **4 条裸字面量**。`decomp find "Base demeanor"` / `"Engagement style"`
//     全仓**只命中 `Brain::HandleEvent` 一处** ⇒ 无遗漏。
//   ⇒ 它们**不走 `MessageQueue`**（是短描述后置串），所以 `ZhsFlightMsgPatch`
//     那个消息口钩子够不着 ⇒ 需要本补丁。
//
// 【做法】Postfix 挂 `XRL.World.Text.TextBuilder.Append(string)`：
//     · `Postfix` 字段的真实类型是 `CompositedTextBuilder : TextBuilder`
//     · `Append(string)` 定义在**基类** `TextBuilder` 上 ⇒ 挂一处覆盖全部调用点
//     · 在**字符串进入缓冲区之前**改掉 ⇒ 不必碰 `TextBuilder` 的内部 API
//       （它没有公开的 Replace；事后改要另想办法）
//     · 判据：**整串完全相等**（含前导 `\n`）⇒ 零误伤
//
// 【安全】只认那 4 个精确串；`_zhs_shortdesc_off.txt` 可停用；异常一律放行原值。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsShortDesc
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>短描述后置串：精确原文 → 中文（键含前导 `\n`）。</summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "\nBase demeanor: {{r|aggressive}}", "\n基础性情：{{r|好斗}}" },
            { "\nBase demeanor: {{g|docile}}", "\n基础性情：{{g|温顺}}" },
            { "\nEngagement style: {{g|defensive}}", "\n交战风格：{{g|防守}}" },
            { "\nEngagement style: {{r|aggressive}}", "\n交战风格：{{r|好斗}}" },

            // ★ 2026-10-18（#21）：`ArtificialIntelligence::HandleEvent(GetShortDescriptionEvent)`
            //   里三条硬编码（`if (ShowAsDeactivated && IsDisabled(...))`）。带不带前导 `\n` 都收。
            { "\nDeactivated: Currently without power.", "\n未激活：当前无动力。" },
            { "\nDeactivated: Currently switched off.", "\n未激活：当前已关闭。" },
            { "\nDeactivated: Currently non-functional.", "\n未激活：当前无法运作。" },
            { "Deactivated: Currently without power.", "未激活：当前无动力。" },
            { "Deactivated: Currently switched off.", "未激活：当前已关闭。" },
            { "Deactivated: Currently non-functional.", "未激活：当前无法运作。" },
        };

        /// <summary>精确查表；未命中原样返回。</summary>
        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            return L.TryGetValue(en, out zh) ? zh : en;
        }
    }

    internal static class ZhsShortDescPatch
    {
        private const string Tag = "[QudZhsCookbook] 短描述：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsShortDesc.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.World.Text.TextBuilder", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Text.TextBuilder");
                    return;
                }
                MethodInfo m = t.GetMethod("Append",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 TextBuilder.Append(string)");
                    return;
                }
                MethodInfo pre = typeof(ZhsShortDescPatch).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.shortdesc")
                    .Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 TextBuilder.Append(string)"
                    + "（4 条短描述后置串 · 整串精确匹配）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Prefix(ref string __0)
        {
            if (ZhsShortDesc.Disabled) return;
            try
            {
                if (string.IsNullOrEmpty(__0)) return;
                if (!ZhsShortDesc.L.ContainsKey(__0)) return;   // 快筛，避免每条都查表
                string zh = ZhsShortDesc.Tr(__0);
                if (zh == __0) return;
                if (ZhsShortDesc.Hits < 6)
                {
                    UnityEngine.Debug.Log(Tag + __0.Replace("\n", "⏎")
                        + "  =>  " + zh.Replace("\n", "⏎"));
                }
                ZhsShortDesc.Hits++;
                __0 = zh;
            }
            catch { }
        }
    }
}
