// 角色创建信息面板的英文残留过滤 —— **Attribute 式补丁**（最早安装）。
//
// ★ 为什么必须用 Attribute 式（血泪教训）：
//   角色创建界面出现在**主菜单 → 新游戏**阶段，而 XRLCore.RunGame / _Start /
//   QudGameBootModule.bootGame **全都在它之后**才被调用。
//   我们原先在 Init() 里用代码挂载，无论怎么提前，都晚于玩家第一次看到该界面
//   （实测日志：引导挂载时间戳晚于角色创建界面）。
//   Attribute 式补丁由 Harmony 在**模组程序集加载时**自动安装，
//   那是我们能拿到的最早时机 → 稳定生效。
//
// 为什么需要这个过滤：
//   数据侧 <extrainfo> 是**追加**语义（Load="Merge" 只加不替换），
//   于是原版英文与我们的中文同时存在，界面上一段英文一段中文。
//   XML 去不掉本体条目（改安装目录会让 Steam 校验失败）→ 只能在返回时过滤。
//
// 注意：这里**两个类都要挂** —— 基因型界面走 XRL.GenotypeEntry，
//       子类型界面走 XRL.SubtypeEntry，只挂一个会漏。
//
// 安全设计：
//   * 只删"不含任何汉字"的条目（我们译过的都含汉字，绝不会误删）。
//   * 异常全部吞掉 —— 补丁绝不能把游戏搞崩。
//   * 开关文件 _zhs_chargen_info_filter_off.txt 可临时关闭（每次调用检查，代价极低）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsChargenInfoFilter
    {
        private static readonly Regex Han = new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
        private static readonly Regex Ascii = new Regex("[A-Za-z]", RegexOptions.Compiled);

        private static int _hits;
        private static int _removed;
        private static readonly object Gate = new object();
        private static string _modDir;
        private static bool _disableChecked;
        private static bool _disabled;

        /// <summary>引导时把模组目录告诉我们，用于读取开关文件。</summary>
        public static void SetModDirectory(string dir) { _modDir = dir; }

        public static void Filter(MethodBase __originalMethod, ref List<string> __result)
        {
            try
            {
                if (!_disableChecked && !string.IsNullOrEmpty(_modDir))
                {
                    _disableChecked = true;
                    try
                    {
                        if (System.IO.File.Exists(System.IO.Path.Combine(_modDir, "_zhs_chargen_info_filter_off.txt")))
                            _disabled = true;
                    }
                    catch { }
                }
                if (_disabled || __result == null || __result.Count == 0) return;

                int before = __result.Count;
                // 1) 剔掉纯英文条目（本体英文与我们的中文并存）
                __result.RemoveAll(s =>
                {
                    if (string.IsNullOrEmpty(s)) return false;
                    if (Han.IsMatch(s)) return false;   // 有汉字 → 我们译的，留
                    return Ascii.IsMatch(s);            // 只处理像英文句子的
                });
                // 2) 去重（extrainfo 是**追加**语义：本体条目 + 我们的条目会同时存在，
                //    两边语义重复时界面会出现两行相同的文字，例如变异人的「变异」）
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    string s = (__result[i] ?? "").Trim();
                    if (s.Length == 0) continue;
                    if (!seen.Add(s)) __result.RemoveAt(i);   // 保留靠前的那条
                }
                int removed = before - __result.Count;
                if (removed > 0)
                {
                    lock (Gate) { _hits++; _removed += removed; }
                    if (_hits <= 30)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 角色创建信息过滤 #" + _hits
                            + "：" + (__originalMethod?.DeclaringType?.Name ?? "?")
                            + " 剔除英文 " + removed + " 条（累计 " + _removed + "）");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 角色创建信息过滤异常：" + e.Message);
            }
        }
    }

    /// <summary>基因型界面（变异人 / 真正子嗣）的额外信息过滤。</summary>
    [HarmonyPatch(typeof(XRL.GenotypeEntry))]
    [HarmonyPatch("GetChargenInfo")]
    internal static class ZhsGenotypeInfoPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return AccessTools.Method(typeof(XRL.GenotypeEntry), "GetChargenInfo", Type.EmptyTypes, null) != null; }
            catch { return false; }   // 绝不抛异常（会中断 PatchAll，连累其它补丁）
        }

        [HarmonyPostfix]
        private static void Postfix(System.Reflection.MethodBase __originalMethod, ref List<string> __result)
        {
            ZhsChargenInfoFilter.Filter(__originalMethod, ref __result);
        }
    }

    /// <summary>子类型界面（种姓 / 天职）的额外信息过滤。</summary>
    [HarmonyPatch(typeof(XRL.SubtypeEntry))]
    [HarmonyPatch("GetChargenInfo")]
    internal static class ZhsSubtypeInfoPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return AccessTools.Method(typeof(XRL.SubtypeEntry), "GetChargenInfo", Type.EmptyTypes, null) != null; }
            catch { return false; }
        }

        [HarmonyPostfix]
        private static void Postfix(System.Reflection.MethodBase __originalMethod, ref List<string> __result)
        {
            ZhsChargenInfoFilter.Filter(__originalMethod, ref __result);
        }
    }
}
