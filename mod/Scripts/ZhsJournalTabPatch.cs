// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsJournalTabPatch —— 日志界面（JournalScreen）的**页签标题**汉化
// ----------------------------------------------------------------------------
//  【为什么需要】
//    截图：日志 → 地点页，标题仍是 `Locations`。
//    反编译 `XRL.UI.JournalScreen`：
//      public static readonly string STR_LOCATIONS = "Locations";   ← 静态字段
//      string[] array = { STR_LOCATIONS, … };
//      if (array[num] == STR_LOCATIONS) { … }                       ← 兼作**比较键**
//    ⇒ **不能改 `STR_LOCATIONS` 本身**（同 `<step Name>` 的坑：改了比较就失效）。
//
//  【出口】`JournalScreen.GetTabDisplayName(string Tab)`
//      public static string GetTabDisplayName(string Tab)
//      {
//          if (Tab == STR_SULTANS) return GetSultansDisplayName();
//          return Tab;                        ← ★ 其他页签原样返回
//      }
//    ⇒ 挂 **Postfix 改 `__result`**：只改显示，不碰键。
//    ⇒ 一个点覆盖全部 7 个页签（Locations / Chronology / Gossip and Lore /
//       Sultan Histories / Village Histories / General Notes / Recipes）。
//
//  【安全性】
//    ① 只改返回值，`STR_*` 字段一字不动 ⇒ 比较/存档分类键不受影响
//    ② 幂等：返回值含中文时直接放行
//    ③ 表与 `ZhsCombatMsgPatch.PageNames` 保持一致（同一套译名）
//
//  【停用】装机目录放 `_zhs_journaltab_off.txt`
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsJournalTabPatch
    {
        private const string Tag = "[QudZhsCookbook] 日志页签：";

        private static bool _applied;
        private static int _hits;

        /// <summary>与 `ZhsCombatMsgPatch.PageNames` 同口径（7 个页签 + 2 个兼容名）。</summary>
        private static readonly Dictionary<string, string> Tabs =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Locations",         "地点" },
                { "Chronology",        "年表" },
                { "Gossip and Lore",   "流言与传说" },
                { "Sultan Histories",  "苏丹史" },
                { "Village Histories", "村落史" },
                { "General Notes",     "一般笔记" },
                { "Recipes",           "食谱" },
                // 兼容：某些路径用短名
                { "Sultans",           "苏丹史" },
                { "Villages",          "村落史" },
                { "General",           "一般笔记" },
            };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (IsOff()) { UnityEngine.Debug.Log(Tag + "已按 _zhs_journaltab_off.txt 停用"); return; }

            Type t = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType("XRL.UI.JournalScreen", false);
                if (t != null) break;
            }
            if (t == null) { UnityEngine.Debug.LogWarning(Tag + "找不到 XRL.UI.JournalScreen"); return; }

            MethodInfo m = t.GetMethod("GetTabDisplayName",
                BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(string) }, null);
            if (m == null)
            {
                UnityEngine.Debug.LogWarning(Tag + "找不到 GetTabDisplayName(string)");
                return;
            }

            try
            {
                new Harmony("Nattens.QudZhsCookbook.journaltab").Patch(m,
                    postfix: new HarmonyMethod(typeof(ZhsJournalTabPatch).GetMethod(
                        "Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
                UnityEngine.Debug.Log(Tag + "已挂 JournalScreen.GetTabDisplayName（Postfix）· 表 " + Tabs.Count + " 条");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e.Message);
            }
        }

        private static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            try
            {
                if (HasCjk(__result)) return;          // 已是中文（如 GetSultansDisplayName 的产物）
                string zh;
                if (!Tabs.TryGetValue(__result, out zh)) return;
                __result = zh;
                _hits++;
                if (_hits <= 3)
                    UnityEngine.Debug.Log(Tag + "首次替换#" + _hits + " " + __result);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "替换异常（放行原文）：" + e.Message);
            }
        }

        private static bool HasCjk(string s)
        {
            foreach (char c in s)
                if (c >= '\u4e00' && c <= '\u9fff') return true;
            return false;
        }

        private static bool IsOff()
        {
            try
            {
                string dir = ZhsFileUtil.WriteDir;
                return dir != null && File.Exists(Path.Combine(dir, "_zhs_journaltab_off.txt"));
            }
            catch { return false; }
        }
    }
}
