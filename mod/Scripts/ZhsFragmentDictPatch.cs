// ═══════════════════════════════════════════════════════════════════════════
//  ZhsFragmentDictPatch —— 「碎片字典」兜底：把 C# 里**拼出来**的英文碎片换成中文
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么需要它】（2026-10-17 定案 · 用户点名课题「严查非谓语动词」）
//
//   反编译实证：游戏**没有**「动词 → 非谓语」的生成器。`XRL.Language.Grammar`
//   只有两处真变位（`ThirdPerson` 查表 · `PastTenseOf` 一个 switch + -ed 兜底）；
//   `to X` / `X-ing` **全是数据里的固定字面量**（`#US` heap 里 19 条不定式、
//   78 条 -ing 词形）。句子则是：
//
//       AddPlayerMessage(Object.Does("swoop") + " down to attack.");
//       //                 └ 动态动词槽（我们已汉化）   └ 固定碎片（没人管）
//
//   ⇒ 逐词接管的补丁必然把句子拆成「中文动词 + 英文碎片」。
//   ⇒ 治本只有一条：**把碎片表也接管**。
//
// 【本补丁做什么】
//   挂 `XRL.Messages.MessageQueue.AddPlayerMessage(string, …)` 的 **Postfix**，
//   对**定稿文本**做一次**贪心最长匹配**替换（表见 `ZhsFragmentDictTable.cs`）。
//   ⇒ 与 `ZhsCombatMsgPatch`（整句正则）互补：那边管"整句形态"，这边管"零散碎片"。
//
// 【安全边界】（三条，缺一不可）
//   ① 只替换**表里登记过**的碎片 ⇒ 玩家自定义名字天然安全（规则 5）
//   ② **词边界**匹配：碎片两侧不能紧邻 ASCII 字母（防 `to pull` 命中 `to pulley`）
//   ③ 幂等：文本里已无该英文碎片时零改动；异常全吞，出错就走原消息
//
// 【停用】装机目录放 `_zhs_fragdict_off.txt`
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFragmentDictPatch
    {
        internal const string Tag = "[QudZhsCookbook] 碎片字典：";

        internal static bool Disabled;

        private static bool _applied;
        private static int _hits, _scanned;

        // ★ 表与"编译/替换"纯逻辑都在 `ZhsFragmentDictTable.cs`（自动生成、纯 C#）
        //   —— 放那边才能被本地 `_compilecheck`（dotnet build）覆盖到；
        //      本文件引用 `XRL.Messages.MessageQueue`，被它排除。
        internal static void Apply(ref string Text)
        {
            if (Disabled || Text == null || Text.Length == 0) return;
            _scanned++;
            try
            {
                string neo = ZhsFragmentDictTable.Apply(Text);
                if (!ReferenceEquals(neo, Text) && neo != Text)
                {
                    _hits++;
                    if (_hits <= 20)
                    {
                        UnityEngine.Debug.Log(Tag + "首次替换#" + _hits
                            + " 原=" + Esc(Text) + " ⇒ 新=" + Esc(neo));
                    }
                    Text = neo;
                    ZhsHookTrace.Step("FragDict.post", neo);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "替换异常（已放行原文）：" + e);
            }
        }

        private static string Esc(string s)
        {
            if (s == null) return "null";
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            if (ZhsFragmentDictTable.Rules.Count == 0)
            {
                UnityEngine.Debug.LogError(Tag + "碎片表为空 —— 生成器未写入？");
                return;
            }

            MethodInfo mi = null;
            foreach (MethodInfo m in typeof(XRL.Messages.MessageQueue).GetMethods(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "AddPlayerMessage") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    mi = m;
                    break;
                }
            }
            if (mi == null)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 MessageQueue.AddPlayerMessage(string, …)");
                return;
            }

            try
            {
                var hm = new HarmonyMethod(typeof(ZhsFragmentDictPatch)
                    .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
                var h = new Harmony("nattens.qudzheookbook.fragdict");
                h.Patch(mi, postfix: hm);
                UnityEngine.Debug.Log(Tag + "已挂 1 个出口（MessageQueue.AddPlayerMessage）· 表 "
                    + ZhsFragmentDictTable.Rules.Count + " 条");
                UnityEngine.Debug.Log(Tag + "自检：\n" + ZhsFragmentDictTable.SelfCheck());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Postfix(ref string __0)
        {
            Apply(ref __0);
        }
    }
}
