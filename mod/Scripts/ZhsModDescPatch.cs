// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsModDescPatch —— 物品改装（`Mod*`）描述的汉化出口
//
//  背景（2026-10-18 · GetDescription 分诊 ITEM_MOD 组）：
//    13 条 audit 命中里，**9 条是假阳性** —— 那些方法的返回值都经过
//        `_S(...)` / `_T(...)`（`ModCamo Description` / `ModWeightless Description` /
//        `ModCoProcessor Description Tier Zero` …），运行时查我方串表 ⇒ 早已是中文。
//        audit 看到的只是**英文回退字面量**。
//
//    真正**裸硬编码**的只有 4 个（反编译逐条确认）：
//        ModKeen::GetDescription            → "Keen: +2 to penetration rolls"
//        ModMetallized::GetDescription      → "Metallized: +1 AV or penetration"
//        ModNanochelated::GetDescription    → "Nanochelated: …"
//        ModOverbuilt::GetDescription       → "Overbuilt: +3 AV, -2 DV, -10 move speed, x2 weight"
//
//  出口：`XRL.World.Tinkering.ItemModding.GetModificationDescription(string, int)`
//        与 `(string, GameObject)` —— 两个重载都通过**反射**调各 mod 的静态
//        `GetDescription`，是两个最终汇合点（反编译见 `_tools\_tmp_itemmod.txt`）。
//        ⇒ 挂 Postfix，按**整串精确表**替换；查不到就原样（不半译）。
//
//  开关：装机目录放 `_zhs_moddesc_off.txt` 即停用（默认开启）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsModDesc
    {
        internal static bool Disabled;
        internal static int Hits;

        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Keen: +2 to penetration rolls",
              "锐利：穿透判定 +2" },
            { "Metallized: +1 AV or penetration",
              "金属化：护甲值或穿透 +1" },
            { "Nanochelated: This item's metals have been replaced with carbon fiber. -1 AV or penetration, +1 DV if below zero",
              "纳米镀层：此物的金属已被碳纤维取代。护甲值或穿透 -1；若护甲值低于零，闪避值 +1" },
            { "Overbuilt: +3 AV, -2 DV, -10 move speed, x2 weight",
              "超规格：护甲值 +3、闪避值 -2、移动速度 -10、重量 ×2" },
        };

        internal static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            string zh;
            return L.TryGetValue(s, out zh) ? zh : s;
        }
    }

    internal static class ZhsModDescPatch
    {
        private const string Tag = "[QudZhsCookbook] 改装描述：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                try
                {
                    string off = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                        "Mods", "QudZhsCookbook", "_zhs_moddesc_off.txt");
                    if (File.Exists(off)) ZhsModDesc.Disabled = true;
                }
                catch { }
                if (ZhsModDesc.Disabled)
                {
                    UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                    return;
                }

                Type t = AccessTools.TypeByName("XRL.World.Tinkering.ItemModding");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 ItemModding");
                    return;
                }
                MethodInfo post = typeof(ZhsModDescPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                HarmonyMethod hm = new HarmonyMethod(post);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.moddesc");

                int n = 0;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (mi.Name != "GetModificationDescription") continue;
                    if (mi.ReturnType != typeof(string)) continue;
                    try
                    {
                        h.Patch(mi, postfix: hm);
                        n++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(Tag + "挂 " + mi + " 失败：" + e.Message);
                    }
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 GetModificationDescription 出口（"
                    + ZhsModDesc.L.Count + " 条裸硬编码描述）");
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
                string zh = ZhsModDesc.Fix(__result);
                if (zh == __result) return;
                if (ZhsModDesc.Hits < 12)
                    UnityEngine.Debug.Log(Tag + __result + "  =>  " + zh);
                ZhsModDesc.Hits++;
                __result = zh;
            }
            catch { }
        }
    }
}
