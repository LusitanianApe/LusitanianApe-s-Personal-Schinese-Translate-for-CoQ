// ═══════════════════════════════════════════════════════════════════════════
// 按键设置界面标题汉化（`Control Mapping` 是**硬编码字面量**）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么需要它】反编译 `XRL.UI.KeyMappingUI::Show()`（**两条分支都有**）：
//     if (Options.ModernUI) { … Buffer.Write("[ {{w|Control Mapping}} ]"); … }
//     …（经典分支）…        Buffer.Write("[ {{w|Control Mapping}} ]");
//                           Buffer.Write(" {{W|ESC}} - Exit ");
//   ⇒ 标题是**纯字面量硬编码**，任何 XML 都改不到它；
//     而同屏的 `ESC - Exit` 已被我方译成「[Esc] 取消」
//     ⇒ 出现「上方英文 Control Mapping + 下方中文 取消」的**同屏中英**
//
// 【做法】挂 `KeyMappingUI.Show` 的 **Postfix**，在它把 Buffer 画出去**之前**
//   把标题替换成中文 —— 但 `Show` 会阻塞在菜单循环里，Postfix 太晚。
//   ⇒ 改用 **Prefix + 一次性的 `CtrlRef` 替换**：把 `Buffer.Write` 的字面量换掉不可行，
//     所以改为**挂 `KeyMappingUI` 的 `RenderTitle` 之类不可得** ⇒ 采用最稳的：
//     **Prefix 里把 `Buffer` 的第 0 行标题重写**（在 Show 内部的第一次 Write 之前
//     无法插入）⇒ 退一步：**每次 DrawBuffer 前后**不可控。
//
//   ★ 最终采用：**Harmony 挂 `ConsoleLib.Console.ScreenBuffer.Write(string)`** 太重。
//     ⇒ 改为挂在 `KeyMappingUI::Show` 上，Prefix 里**不做事**，而是
//       **注册一个 `Buffer` 的内容后处理器**：在 `Popup._TextConsole.DrawBuffer` 时改写。
//     ⇒ 最简可行：**挂 `XRL.UI.Popup` 的 `_TextConsole` DrawBuffer 不方便**
//       ⇒ 直接挂 **`KeyMappingUI::Show` 的 Prefix，用反射把 `Buffer` 实例的
//         内部字符串表换掉**同样过重。
//
//   ⇒ 实际上最简单可靠的是：**挂 `KeyMappingUI::Show`，Prefix 返回 false 自己重写** ——
//     但那要复刻整个菜单逻辑，风险高。
//
// 【最终方案】只替换**显示文本**：挂 `ConsoleLib.Console.ScreenBuffer.Write(string Text)`
//   的 **Prefix**，当 `Text` 恰好是我们收集的那几条硬编码界面标题时换成中文。
//   `ScreenBuffer` 是 TextConsole 的底层缓冲，`Buffer.Write` 全走它 ⇒ 覆盖面广但**精确匹配**，
//   只动白名单里的字面量 ⇒ 不影响其它文本。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsKeyMappingTitlePatch
    {
        private const string Tag = "[QudZhsCookbook] 界面标题：";

        internal static bool Disabled;
        private static bool _applied;
        private static int _hits;

        /// <summary>硬编码界面标题字面量 → 中文（**精确等于**才替换）。</summary>
        private static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // `KeyMappingUI::Show`：`Buffer.Write("[ {{w|Control Mapping}} ]")`
            { "[ {{w|Control Mapping}} ]", "[ {{w|按键设置}} ]" },

            // `XRLManual::RenderIndex` / `ShowHelp` 的硬编码提示（2026-10-08 补）
            { "[ &WHelp Index&y ]", "[ &W帮助索引&y ]" },
            { " [&W5&y or &WEnter&y] Back to Index ", " [&W5&y 或 &WEnter&y] 返回索引 " },
            { " [&W5&y] Exit Help ", " [&W5&y] 退出帮助 " },
            { " [&W>&y or &WEnter&y] Select Topic ", " [&W>&y 或 &WEnter&y] 选择条目 " },
            { " [&W>&y] Select Topic ", " [&W>&y] 选择条目 " },
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("ConsoleLib.Console.ScreenBuffer");
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 ScreenBuffer 类型 ⇒ 跳过");
                    return;
                }
                MethodInfo pre = typeof(ZhsKeyMappingTitlePatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                                                      | BindingFlags.Instance
                                                      | BindingFlags.Static))
                {
                    if (m.Name != "Write") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.uimap" + n
                                            + "_" + ps.Length);
                    h.Patch(m, prefix: new HarmonyMethod(pre));
                    n++;
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 ScreenBuffer.Write 出口（标题 "
                                      + L.Count + " 条）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>Prefix：`__0` = 要写的文本；命中白名单就换成中文。</summary>
        private static void Prefix(ref string __0)
        {
            if (Disabled || __0 == null || __0.Length < 4) return;
            string zh;
            if (!L.TryGetValue(__0, out zh)) return;
            __0 = zh;
            _hits++;
            if (_hits == 1)
                UnityEngine.Debug.Log(Tag + "首次替换：[" + zh + "]");
        }
    }


    /// <summary>
    /// ★★ 帮助界面**左侧栏**：`XRLManual::RenderIndex` 用 `Pages` 的**键**（= 官方 `name`，
    /// 英文且**不可改**，§18）渲染，而中文显示名存在 `XRLManualPage.DisplayTopic`
    /// （= 我方 `<topic DisplayName="…">`）⇒ **数据侧改了也显示不出来**。
    /// 改法：Postfix 里按同一布局**用中文重绘**（后写覆盖先写）。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsHelpIndexPatch
    {
        private static bool _logged;

        public static void TryApply()
        {
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("XRL.Help.XRLManual");
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 帮助索引：找不到 XRLManual ⇒ 跳过");
                    return;
                }
                MethodInfo m = t.GetMethod("RenderIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 帮助索引：找不到 RenderIndex ⇒ 跳过");
                    return;
                }
                MethodInfo post = typeof(ZhsHelpIndexPatch).GetMethod("Post",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.helpindex");
                h.Patch(m, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log("[QudZhsCookbook] 帮助索引：已挂 RenderIndex Postfix（左侧栏中文化）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 帮助索引：挂载失败：" + e);
            }
        }

        /// <summary>重绘左侧栏条目为中文显示名（`DisplayTopic`）。</summary>
        private static void Post(object __instance, int ScrollPosition)
        {
            try
            {
                Type t = __instance.GetType();
                var pages = t.GetField("Pages",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(__instance)
                    as System.Collections.IDictionary;
                if (pages == null) return;
                object buffer = t.GetField("Buffer",
                    BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance);
                if (buffer == null) return;

                // 收集键（顺序与 RenderIndex 的 foreach 一致）
                var keys = new List<string>();
                foreach (object k in pages.Keys) keys.Add(Convert.ToString(k));

                int start = ScrollPosition / 21 * 21;
                if (start > keys.Count - 21) start = keys.Count - 21;
                if (start <= 0) start = 0;

                MethodInfo gotoM = buffer.GetType().GetMethod("Goto",
                    new Type[] { typeof(int), typeof(int) });
                MethodInfo writeM = buffer.GetType().GetMethod("Write",
                    new Type[] { typeof(string) });
                if (gotoM == null || writeM == null) return;

                int line = 2;
                for (int i = start; i < keys.Count && line < 23; i++, line++)
                {
                    object page = pages[keys[i]];
                    string disp = null;
                    if (page != null)
                    {
                        var f = page.GetType().GetField("DisplayTopic",
                            BindingFlags.Public | BindingFlags.Instance);
                        disp = f?.GetValue(page) as string;
                    }
                    if (string.IsNullOrEmpty(disp)) disp = keys[i];

                    gotoM.Invoke(buffer, new object[] { 2, line });
                    if (i == ScrollPosition) writeM.Invoke(buffer, new object[] { "&W" + disp + "&k" });
                    else writeM.Invoke(buffer, new object[] { disp });
                }
                if (!_logged)
                {
                    _logged = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 帮助索引：首次重绘 "
                        + Math.Min(keys.Count, 21) + " 行（例：" + keys[0] + " ⇒ "
                        + (pages[keys[0]] != null ? Convert.ToString(
                            pages[keys[0]].GetType().GetField("DisplayTopic")
                                ?.GetValue(pages[keys[0]])) : "?") + "）");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 帮助索引：重绘出错：" + e.Message);
            }
        }
    }
}
