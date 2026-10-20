// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsPrefixBisect —— **二分排查用**：把三处「Postfix → Prefix」做成可开关的补充钩子
//
//  背景（2026-10-18）：把下面三处从 Postfix 改成 Prefix 后，游戏在
//  `XRLGame.LoadGame` 的 `Seed: ` 之后卡死（CPU 空闲、窗口无响应）；改回 Postfix 就能进。
//  ⇒ 需要逐个确认是哪一处，且**不想每次重编译**。
//
//  做法：原来的三个类**保持 Postfix**（可用、不卡）；这里额外挂三个**独立的 Prefix 补丁**，
//        每个只在对应开关文件存在时才挂：
//            `_zhs_prefix_journal.txt`  → IBaseJournalEntry.DisplayMessage
//            `_zhs_prefix_wish.txt`     → Popup.AskString
//            `_zhs_prefix_popup.txt`    → Popup.Show
//        ★ 同时挂 Postfix + Prefix 会让文本被处理两次，但两个 Apply 都是幂等的。
//
//  用法：一次只建一个文件 → 启动 → 能进就删掉换下一个。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPrefixBisect
    {
        internal static bool On(string name)
        {
            try
            {
                string dir = ZhsTrace.ModDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                        "Mods", "QudZhsCookbook");
                }
                return File.Exists(Path.Combine(dir, "_zhs_prefix_" + name + ".txt"));
            }
            catch { return false; }
        }
    }

    /// <summary>`_zhs_prefix_journal.txt` → `IBaseJournalEntry.DisplayMessage` 的 Prefix。</summary>
    [HarmonyPatch]
    internal static class ZhsPrefixJournalDisplay
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("Qud.API.IBaseJournalEntry");
            if (t == null) return null;
            return AccessTools.Method(t, "DisplayMessage",
                new[] { typeof(string), typeof(string) });
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            bool on = ZhsPrefixBisect.On("journal");
            if (on) UnityEngine.Debug.Log("[QudZhsCookbook] 二分：journal Prefix **已启用**");
            return on;
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            __0 = ZhsJournalNote.Apply(__0);
        }
    }

    /// <summary>`_zhs_prefix_wish.txt` → `Popup.AskString` 的 Prefix。</summary>
    [HarmonyPatch]
    internal static class ZhsPrefixWish
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("XRL.UI.Popup");
            if (t == null) return null;
            MethodBase best = null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                     | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.Name != "AskString") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    if (best == null || ps.Length < best.GetParameters().Length) best = m;
                }
            }
            return best;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            bool on = ZhsPrefixBisect.On("wish");
            if (on) UnityEngine.Debug.Log("[QudZhsCookbook] 二分：wish Prefix **已启用**");
            return on;
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            __0 = ZhsWishText.Apply(__0);
        }
    }

    /// <summary>`_zhs_prefix_popup.txt` → `Popup.Show` 的 Prefix。</summary>
    [HarmonyPatch]
    internal static class ZhsPrefixPopup
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.TypeByName("XRL.UI.Popup");
            if (t == null) return null;
            MethodBase best = null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                     | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.Name != "Show") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    if (best == null || ps.Length < best.GetParameters().Length) best = m;
                }
            }
            return best;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            bool on = ZhsPrefixBisect.On("popup");
            if (on) UnityEngine.Debug.Log("[QudZhsCookbook] 二分：popup Prefix **已启用**");
            return on;
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            __0 = ZhsJournalNote.Apply(__0);
        }
    }
}
