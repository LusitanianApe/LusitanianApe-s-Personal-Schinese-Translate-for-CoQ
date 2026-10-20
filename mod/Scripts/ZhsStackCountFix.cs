// ═══════════════════════════════════════════════════════════════════════════
// 数量后缀（`x2` / `x12`）与不定冠词/量词的冲突处理
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】
//   截图实证：`你经过一个bronze dagger x2。`
//
//   英文原文：`You pass by =passed.andList=.`
//   其中 `=object.a.name=` 会解析为 **不定冠词 + 名字**（"a bronze dagger"），
//   而堆叠物品在后面追加数量后缀（" x2"）。
//
//   英文里 `a bronze dagger x2` 本来就别扭；中文里「一个」带**单数语义**，
//   和「x2」直接打架 —— 读起来像「一个……两个」。
//
// 【判定依据】
//   数量后缀的形态（反编译 + 游戏文本观察）：
//       `x2`  `x12`  ` x2`  出现在**物品名之后、句末标点之前**
//
// 【做法】
//   在消息/弹窗出口做后处理：**若同一条消息里出现数量后缀**，
//   就把紧邻物品名的「一 + 量词」删掉（一个/一只/一柄/一把/一件/一台/一枚…）。
//
//   安全性：
//     * 只在整句**确实含数量后缀**时动手
//     * 只删**紧接在数量后缀前面**的那一处「一+量词」
//     * 不动英文（英文路径无「一个」）
//     * 幂等：已删过的不再匹配
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsStackCountFix
    {
        private const string Tag = "[QudZhsCookbook] 堆叠数量后缀：";

        private static bool _applied; private static int _hits, _scanned;

        // 数量后缀：x2 / x12 / ×3
        private static readonly Regex StackSuffix =
            new Regex(@"[x×]\d{1,3}(?![\d])", RegexOptions.Compiled);

        // 「一 + 量词」（后接物品名）
        private static readonly Regex OneCounter = new Regex(
            @"一[个只柄把件台枚条根块张面把支颗片层团堆群列行道缕束组套副对双顶辆艘间座扇枚格射支]",
            RegexOptions.Compiled);


        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            int n = 0;
            // 消息日志出口
            n += Patch(typeof(XRL.Messages.MessageQueue), "AddPlayerMessage", "MsgPrefix");
            // 弹窗出口（三种：Show / ShowBlock / ShowSpace —— ShowSpace 是 lost 弹窗用的）
            n += Patch(typeof(XRL.UI.Popup), "Show", "PopupPrefix");
            n += Patch(typeof(XRL.UI.Popup), "ShowBlock", "PopupPrefix");
            n += Patch(typeof(XRL.UI.Popup), "ShowSpace", "PopupPrefix");

            UnityEngine.Debug.Log(Tag + "已挂 " + n
                + " 个出口（含数量后缀时删「一+量词」）");
        }

        private static int Patch(Type t, string name, string prefixName)
        {
            if (t == null) return 0;
            MethodInfo pre = typeof(ZhsStackCountFix).GetMethod(
                prefixName, BindingFlags.NonPublic | BindingFlags.Static);
            if (pre == null) return 0;
            int n = 0;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static))
            {
                if (mi.Name != name) continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 0) continue;
                if (ps[0].ParameterType != typeof(string)) continue;
                if (ps[0].IsOut) continue;
                try
                {
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.stackcount."
                        + t.Name + "." + name + "." + ps.Length);
                    h.Patch(mi, prefix: new HarmonyMethod(pre));
                    n++;
                }
                catch { }
            }
            return n;
        }

        private static bool MsgPrefix(ref string Message)
        {
            try
            {
                if (!string.IsNullOrEmpty(Message))
                {
                    _scanned++;
                    string fixedText = Clean(Message);
                    if (fixedText != Message) Message = fixedText;
                }
            }
            catch { }
            return true;
        }

        private static bool PopupPrefix(ref string Message)
        {
            try
            {
                if (!string.IsNullOrEmpty(Message))
                {
                    _scanned++;
                    string fixedText = Clean(Message);
                    if (fixedText != Message) Message = fixedText;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// 若文本含数量后缀，则删掉紧接其前的「一+量词」。
        /// </summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (!ContainsCjk(s)) return s;                 // 英文路径不动

            string result = s;

            // ── 甲：数量后缀 x2 → 删前面的「一+量词」 ──
            if (StackSuffix.IsMatch(result))
            {
                Match suf = StackSuffix.Match(result);
                string head = result.Substring(0, suf.Index);
                MatchCollection ms = OneCounter.Matches(head);
                if (ms.Count > 0)
                {
                    Match last = ms[ms.Count - 1];
                    string between = head.Substring(last.Index + last.Length);
                    if (between.Length <= 80
                        && between.IndexOfAny(new[] { '。', '！', '？', '\n', '；' }) < 0)
                    {
                        result = result.Remove(last.Index, last.Length);
                        _hits++;
                        if (_hits <= 25)
                            UnityEngine.Debug.Log(Tag + "删量词(x2)：" + s + "  =>  " + result);
                    }
                }
            }

            // ── 乙：**已移除** ──
            //
            // 原逻辑：删掉「英文物品名前的 一+量词」（如「一个box of crayons」→「box…」）。
            //
            // ★★ 移除原因（截图实证 '你 看见你看到在西方向的一个snapjaw brute，于是停止移动。'）：
            //   ① **Groups[3] 越界**：正则只有 2 个捕获组，替换器却访问 Groups[3]
            //      ⇒ 抛 ArgumentException ⇒ `Regex.Replace` 吞掉后保留匹配原文
            //      ⇒ 产出「的snapjaw」这类畸形文本
            //   ② **即便修好越界，设计本身也有害**：
            //      它删掉「一个」后，紧邻的「的」就**悬空**了
            //      （「…的一个snapjaw」→「…的snapjaw」）
            //   ③ 而且它与「冠词->量词」补丁**职责重叠**：
            //      后者负责把 `a/an` 译成量词，本段却又把量词删掉 ⇒ 互相打架
            //
            // ⇒ 内容照实保留（不删「一+量词」），交给「冠词->量词」补丁统一处理。
            //   若将来确实需要「英文名前的量词」特例，应在**冠词补丁**里做，
            //   而不是在这里做二次删除。

            return result;
        }

        private static bool ContainsCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '一' && c <= '鿿') || (c >= '＀' && c <= '￯'))
                    return true;
            }
            return false;
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：扫描 " + _scanned
                + " 条，删量词 " + _hits + " 次");
        }
    }
}
