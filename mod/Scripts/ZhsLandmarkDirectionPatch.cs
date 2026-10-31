// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsLandmarkDirectionPatch —— 地标方位距离（`2 parasangs east of Joppa`）汉化
// ----------------------------------------------------------------------------
//  【来源】日志界面 → 地点页 → 地图注释条目
//      `XRL.UI.JournalScreen.UpdateEntries`：
//          journalEntry.entry = item2.GetDisplayText();
//      `Qud.API.JournalMapNote.GetDisplayText`：
//          Strings._T("Journal Map Note, describe directions", "=note.location=")
//              .SetArgument("note", this)
//      `=note.location=` 的 replacer：
//          XRL.World.Text.Delegates.JournalReplacers.Location(Context, JournalMapNote Entry)
//              => XRL.LoreGenerator.GenerateLandmarkDirectionsTo(Entry.ZoneID)
//
//  ⇒ 真正生成英文的是
//      `XRL.LoreGenerator.GenerateLandmarkDirectionsTo(string ZoneID, bool bAltStarts)`
//    反编译实证：**纯 StringBuilder 拼接**，没有字符串表、没有 XML 出口。
//    全部形态（`parasang` 单复数 + `strata deep` 后缀）：
//      near Joppa
//      near Joppa, 3 strata deep
//      2 parasangs east of Joppa
//      2 parasangs east and 1 parasang north of Joppa          （同一地标）
//      2 parasangs east of Joppa, 3 parasangs north of Kyakukya （不同地标）
//      以上任意一条 + `, N strata deep`
//
//  【为什么不在 `ZhsCombatMsgPatch` 加规则】
//    那套规则只作用于**消息/弹窗**（AddPlayerMessage / Popup）；
//    journal 列表不经过那些方法（见 `handoff\接续-20261028.md` §四①），
//    所以消息栏里的同类形态早就译了、地点页里仍是英文。
//
//  【安全性】
//    ① 只改返回值（Postfix），不碰 `LoreGenerator.landmarks` / 存档
//    ② 未命中的形态**原样返回**，不猜
//    ③ 停用：装机目录放 `_zhs_landmarkdir_off.txt`
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsLandmarkDirectionPatch
    {
        private const string Tag = "[QudZhsCookbook] 地标方向：";

        private static bool _applied;
        private static int _hits;

        // `, 3 strata deep`（`ZoneZ > 10` 时追加）
        private static readonly Regex RxStrata = new Regex(
            @",\s*(\d+)\s+strata\s+deep$", RegexOptions.Compiled);

        private static readonly Regex RxNear = new Regex(
            @"^near\s+(.+)$", RegexOptions.Compiled);

        // 不同地标：`2 parasangs east of Joppa, 3 parasangs north of Kyakukya`
        private static readonly Regex RxTwinDiff = new Regex(
            @"^(\d+)\s+parasangs?\s+(east|west|north|south)\s+of\s+(.+?),\s*"
            + @"(\d+)\s+parasangs?\s+(east|west|north|south)\s+of\s+(.+)$",
            RegexOptions.Compiled);

        // 同一地标：`2 parasangs east and 1 parasang north of Joppa`
        private static readonly Regex RxTwinSame = new Regex(
            @"^(\d+)\s+parasangs?\s+(east|west|north|south)\s+and\s+"
            + @"(\d+)\s+parasangs?\s+(east|west|north|south)\s+of\s+(.+)$",
            RegexOptions.Compiled);

        // 单段：`2 parasangs east of Joppa`
        private static readonly Regex RxSingle = new Regex(
            @"^(\d+)\s+parasangs?\s+(east|west|north|south)\s+of\s+(.+)$",
            RegexOptions.Compiled);

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (IsOff()) { UnityEngine.Debug.Log(Tag + "已按 _zhs_landmarkdir_off.txt 停用"); return; }

            Type t = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType("XRL.LoreGenerator", false);
                if (t != null) break;
            }
            if (t == null) { UnityEngine.Debug.LogWarning(Tag + "找不到 XRL.LoreGenerator"); return; }

            MethodInfo m = t.GetMethod("GenerateLandmarkDirectionsTo",
                BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(string), typeof(bool) }, null);
            if (m == null)
            {
                UnityEngine.Debug.LogWarning(Tag + "找不到 GenerateLandmarkDirectionsTo(string, bool)");
                return;
            }

            try
            {
                new Harmony("Nattens.QudZhsCookbook.landmarkdir").Patch(m,
                    postfix: new HarmonyMethod(typeof(ZhsLandmarkDirectionPatch).GetMethod(
                        "Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
                UnityEngine.Debug.Log(Tag + "已挂 XRL.LoreGenerator.GenerateLandmarkDirectionsTo（Postfix）");
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
                string zh = Tr(__result);
                if (ReferenceEquals(zh, __result) || zh == __result) return;
                __result = zh;
                _hits++;
                if (_hits <= 5)
                    UnityEngine.Debug.Log(Tag + "首次替换#" + _hits + " " + zh);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "替换异常（放行原文）：" + e.Message);
            }
        }

        /// <summary>英文方位句 → 中文；未命中原样返回。</summary>
        internal static string Tr(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;

            string suffix = "";
            string body = en;
            Match ms = RxStrata.Match(body);
            if (ms.Success)
            {
                suffix = "，地下 " + ms.Groups[1].Value + " 层";
                body = body.Substring(0, ms.Index).TrimEnd();
            }

            Match m;
            string zh = null;

            if ((m = RxTwinDiff.Match(body)).Success)
            {
                zh = "距 " + m.Groups[3].Value + " " + Dir(m.Groups[2].Value) + " " + m.Groups[1].Value + " 帕拉桑，距 "
                   + m.Groups[6].Value + " " + Dir(m.Groups[5].Value) + " " + m.Groups[4].Value + " 帕拉桑";
            }
            else if ((m = RxTwinSame.Match(body)).Success)
            {
                zh = "距 " + m.Groups[5].Value + " " + Dir(m.Groups[2].Value) + " " + m.Groups[1].Value + " 帕拉桑、"
                   + Dir(m.Groups[4].Value) + " " + m.Groups[3].Value + " 帕拉桑";
            }
            else if ((m = RxSingle.Match(body)).Success)
            {
                zh = "距 " + m.Groups[3].Value + " " + Dir(m.Groups[2].Value) + " " + m.Groups[1].Value + " 帕拉桑";
            }
            else if ((m = RxNear.Match(body)).Success)
            {
                zh = "在 " + m.Groups[1].Value + " 附近";
            }

            if (zh == null) return en;   // 未识别 ⇒ 原串（含 strata 后缀）整体返回
            return LocalizeLandmarks(zh) + suffix;
        }

        /// <summary>`XRL.LoreGenerator.landmarks` / `landmarks_altStart` 的**硬编码键** → 既定中文名。</summary>
        /// <remarks>
        ///   字典键就是英文地名（反编译实证：`"Joppa"` / `"Red Rock"` / `"Grit Gate"` …），
        ///   生成句子里 `text = item.Key` ⇒ 不换就是「距 Joppa 以东」。
        ///   译名依据：`_tools\术语表.md`（Joppa / Grit Gate / Six Day Stilt）
        ///           + 既有数据（Books / Conversations / Naming 里的既定写法）。
        ///   顺序：**长名在前**（`the ruins of Joppa` 必须先于 `Joppa`）。
        /// </remarks>
        private static readonly string[,] Landmarks =
        {
            { "the ruins of Joppa", "约帕的废墟" },
            { "the Six Day Stilt",  "六日高跷" },
            { "the Yd Freehold",    "伊德自由邦" },
            { "Bethesda Susa",      "贝塞斯达·苏萨" },
            { "Grit Gate",          "砂砾门" },
            { "Red Rock",           "红岩" },
            { "Golgotha",           "戈耳戈塔" },
            { "Kyakukya",           "基亚库基亚" },
            { "Omonporch",          "奥蒙波奇" },
            { "Joppa",              "约帕" },
        };

        private static string LocalizeLandmarks(string s)
        {
            for (int i = 0; i < Landmarks.GetLength(0); i++)
                s = s.Replace(Landmarks[i, 0], Landmarks[i, 1]);
            return s;
        }

        private static string Dir(string en)
        {
            switch (en)
            {
                case "east": return "以东";
                case "west": return "以西";
                case "north": return "以北";
                case "south": return "以南";
                default: return en;
            }
        }

        private static bool IsOff()
        {
            try
            {
                string dir = ZhsFileUtil.WriteDir;
                return dir != null && File.Exists(Path.Combine(dir, "_zhs_landmarkdir_off.txt"));
            }
            catch { return false; }
        }
    }
}
