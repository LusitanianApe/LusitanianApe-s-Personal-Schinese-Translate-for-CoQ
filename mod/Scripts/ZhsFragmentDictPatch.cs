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
//   挂 `XRL.Messages.MessageQueue.AddPlayerMessage(string, …)` 的 **Prefix**（2026-10-25 由 Postfix 改正），
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
using System.IO;
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

        // ★ 2026-10-25：消息栏逐条明细（在此之前这条路**没有任何记录**）
        private static string _logPath;
        private static readonly HashSet<string> _logged = new HashSet<string>();
        private static readonly Regex LatinWord = new Regex("[A-Za-z]{2,}", RegexOptions.Compiled);

        // ★ 表与"编译/替换"纯逻辑都在 `ZhsFragmentDictTable.cs`（自动生成、纯 C#）
        //   —— 放那边才能被本地 `_compilecheck`（dotnet build）覆盖到；
        //      本文件引用 `XRL.Messages.MessageQueue`，被它排除。
        internal static void Apply(ref string Text)
        {
            if (Disabled || Text == null || Text.Length == 0) return;
            _scanned++;
            string Text0 = Text;                 // ★ 原始文本（记录用）
            try
            {
                string neo = ZhsFragmentDictTable.Apply(Text);
                if (ReferenceEquals(neo, Text) || neo == Text)
                {
                    // 拦到了、但碎片表没有可换的条目 ⇒ 含英文就是「疑似漏译」
                    if (LatinWord.IsMatch(Text)) LogLine("MISS", Esc(Text), null);
                    return;
                }
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
                    LogLine("HIT", Esc(Text0), Esc(neo));
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "替换异常（已放行原文）：" + e);
            }
        }

        /// <summary>写一条明细到 &lt;模组目录&gt;\ZhsFragmentDict.log（去重、跨局累计、上限 3000）。</summary>
        private static void LogLine(string kind, string before, string after)
        {
            try
            {
                string key = kind + "\t" + before;
                if (_logged.Contains(key)) return;
                _logged.Add(key);
                if (_logged.Count > 3000) return;
                if (_logPath == null)
                {
                    string dir = ZhsFileUtil.WriteDir;
                    if (dir == null) return;
                    _logPath = Path.Combine(dir, "ZhsFragmentDict.log");
                }
                File.AppendAllText(_logPath,
                    kind + "\t" + (before ?? "").Replace('\t', ' ')
                         + "\t" + (after ?? "-").Replace('\t', ' ') + "\n");
            }
            catch { }
        }

        // ── RTF 总闸：现代界面回流老通道的唯一入口 ────────────────────
        //
        //   Qud.UI.RTF.FormatToRTF(string s, string opacity, int blockWrap, bool stripFormatting)
        //     · 输入 s 还是 `{{W|…}}` 原始标记 ⇒ 碎片表能匹配
        //     · 输出已是 `<color=...>` 富文本 ⇒ 那时再改就匹配不上了
        //   ⇒ 必须 Prefix 改 __0（不是 Postfix 改 __result）
        private static void PatchRTF()
        {
            try
            {
                Type t = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = a.GetType("Qud.UI.RTF", false);
                    if (t != null) break;
                }
                if (t == null) { UnityEngine.Debug.LogWarning(Tag + "找不到 Qud.UI.RTF"); return; }
                MethodInfo m = t.GetMethod("FormatToRTF", BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(string), typeof(int), typeof(bool) },
                    null);
                if (m == null) { UnityEngine.Debug.LogWarning(Tag + "找不到 FormatToRTF(string,string,int,bool)"); return; }
                new Harmony("nattens.qudzheookbook.fragdict.rtf").Patch(m,
                    prefix: new HarmonyMethod(typeof(ZhsFragmentDictPatch).GetMethod(
                        "PreRTF", BindingFlags.Static | BindingFlags.NonPublic)));
                UnityEngine.Debug.Log(Tag + "已挂 RTF.FormatToRTF（Prefix）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂 FormatToRTF 失败：" + e.Message);
            }
        }

        private static void PreRTF(ref string __0)
        {
            if (Disabled || string.IsNullOrEmpty(__0)) return;
            try
            {
                string neo = ZhsFragmentDictTable.Apply(__0);
                if (ReferenceEquals(neo, __0) || neo == __0)
                {
                    if (LatinWord.IsMatch(__0)) LogLine("MISS-RTF", Esc(__0), null);
                    return;
                }
                LogLine("HIT-RTF", Esc(__0), Esc(neo));
                __0 = neo;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "RTF 替换异常（已放行原文）：" + e);
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
                // ★ 2026-10-25 修：原来挂 Postfix（消息已入队、改 __0 无效，F16 同族）
                //   改 Prefix：在 AddPlayerMessage 执行**之前**把碎片换掉
                h.Patch(mi, prefix: hm);
                PatchRTF();
                UnityEngine.Debug.Log(Tag + "已挂 2 个出口（AddPlayerMessage · FormatToRTF，均 Prefix）· 表 "
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
