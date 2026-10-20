// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsTerminalPatch —— 终端屏内容接管（测试窗口 #40）
//
//  背景（2026-10-17 实机）：
//    Ereshkigal 终端整屏英文，只有底部按钮（导航/接受/退出）是中文。
//    `XRL.UI.GritGateTerminalScreen*` 共 **9 个类 / 78 条字面量**，
//    全部写在 `.ctor` / `BeforeRender` 里，不经过 XML 串表，我方没有任何覆盖。
//
//  为什么**不**做 78 条逐句补丁（反编译实证）：
//    `XRL.UI.TerminalScreen` 是 9 个类的共同基类，字段 **public**：
//        public string MainText;
//        public List<string> Options = new List<string>();
//        public string RenderedText;
//    `TerminalScreen::Update()` 先调 `OnUpdate()`，再把 `MainText` + `Options`
//    拼成 `RenderedText`。子类的文本在**构造函数**里就设好了。
//    ⇒ 挂 **`Update()` 的 Postfix**，把 `MainText` / 每个 `Options[i]` /
//      `RenderedText` 各过一遍表 ⇒ **9 个类（以及其它终端屏）一次覆盖**。
//
//  ★ 幂等：含汉字的串一律跳过（重复渲染不会二次翻译）。
//  ★ 不碰 `TerminalScreen` 的按钮/热键（那些走通用 UI，本来就是中文）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsTerminalPatch
    {
        public static bool Disabled;
        private static bool _applied;
        private static int _hits;
        private const string Tag = "[QudZhsCookbook] 终端接管：";

        /// <summary>精确表（玩家可见的主文本 / 选项 / 报错行）。</summary>
        private static readonly Dictionary<string, string> T =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 主文本 ──
            { "Sensor access granted. The Thin World is open to you. What do you wish?",
              "传感器访问已授权。薄世界已向你敞开。你意欲何为？" },
            { "Basic user access grant. What do you wish from the Thin World?",
              "已授予基础用户权限。你想从薄世界中求取什么？" },
            { "I am Ereshkigal, your liaison with the Thin World. Ask of me.",
              "我是埃列什基伽勒，你与薄世界的联络人。向我提问吧。" },
            { "Our respective ways of reckoning that inquiry are irreconcilable. I have no answer.",
              "你我衡量此问的尺度无法调和。我没有答案。" },
            { "The Thin World is where I traverse, as the Thick World is where you do. I've no domain in your world, and but for me, you've none in mine.",
              "我穿行于薄世界，正如你行走于厚世界。我在你的世界没有疆域；而若非有我，你在我的世界亦无。" },
            { "I've done as you asked. Your repetition is ungainly.",
              "我已照办。重复同样的话很不体面。" },
            { "It is done.", "办妥了。" },
            { "Thank you, Ereshkigal.", "谢谢你，埃列什基伽勒。" },
            { "Access denied. Continue on your path through the Thick World.",
              "拒绝访问。请继续你在厚世界中的旅途。" },
            { "Alarms blare across the enclave.", "警报声响彻整座飞地。" },
            { "Terminal Interface", "终端界面" },

            // ── 选项 ──
            { "Scan surroundings, Ereshkigal.", "扫描周围环境，埃列什基伽勒。" },
            { "Who are you?", "你是谁？" },
            { "What is the Thin World?", "薄世界是什么？" },
            { "Deliver me a bit of knowledge from the Thin World.",
              "从薄世界传递一点知识给我。" },
            { "Are you sapient?", "你有智慧吗？" },
            { "Exit.", "退出。" },
            { "Activate Rodanis Y. [{{R|-100 amps}}]", "启动罗丹尼斯 Y。[{{R|-100 安培}}]" },
            { "Deactivate Rodanis Y. [{{G|+100 amps}}]", "关闭罗丹尼斯 Y。[{{G|+100 安培}}]" },
            { "Activate a chain laser emplacement. [{{R|-50 amps}}]",
              "启动一座链式激光炮台。[{{R|-50 安培}}]" },
            { "Activate a force projector. [{{R|-9 amps/square}}]",
              "启动一台力场投射器。[{{R|-9 安培/格}}]" },

            // ── 状态 / 报错行 ──
            { "[{{R|!!! ERROR: POWER SYSTEMS HAVE FAILED !!!}}]",
              "[{{R|错误：动力系统已失效！}}]" },
            { "[{{W|!!! WARNING: INSUFFICIENT POWER !!!}}]",
              "[{{W|警告：功率不足！}}]" },
            { "[{{R|!!! ERROR: RODANIS Y IS OFFLINE !!!}}]",
              "[{{R|错误：罗丹尼斯 Y 已离线！}}]" },
            { "[{{R|!!! ERROR: CHROMELING BROADCAST BEACON IS OFFLINE !!!}}]",
              "[{{R|错误：铬崽广播信标已离线！}}]" },
            { "[{{R|!!! ERROR: REMOTE MANAGEMENT OFFLINE !!!}}]\n[{{R|!!! CHAIN LASER EMPLACEMENTS MUST BE ACTIVATED MANUALLY !!!}}]",
              "[{{R|错误：远程管理已离线！}}]\n[{{R|链式激光炮台必须手动启动！}}]" },
            { "[{{R|!!! ERROR: REMOTE MANAGEMENT OFFLINE !!!}}]\n[{{R|!!! FORCE PROJECTORS MUST BE ACTIVATED MANUALLY !!!}}]",
              "[{{R|错误：远程管理已离线！}}]\n[{{R|力场投射器必须手动启动！}}]" },
        };

        /// <summary>带变量的模板（顺序敏感：具体的在前）。</summary>
        private static readonly List<KeyValuePair<Regex, string>> Rules =
            new List<KeyValuePair<Regex, string>>
        {
            new KeyValuePair<Regex, string>(
                new Regex(@"^Overclock chromelings \(\+10 QN, current level: (.*?)\)\. \[\{\{R\|-22\.5 amps\}\}\]$"),
                "超频铬崽（+10 QN，当前等级：$1）。[{{R|-22.5 安培}}]"),
            new KeyValuePair<Regex, string>(
                new Regex(@"^Reduce clock on chromelings \(-10 QN, current level: (.*?)\)\. \[\{\{G\|\+22\.5 amps\}\}\]$"),
                "降低铬崽时钟（-10 QN，当前等级：$1）。[{{G|+22.5 安培}}]"),
            new KeyValuePair<Regex, string>(
                new Regex(@"^ Available power: (.*?) amps $"),
                " 可用功率：$1 安培 "),
            new KeyValuePair<Regex, string>(
                new Regex(@"^ Available power: $"),
                " 可用功率："),
            new KeyValuePair<Regex, string>(
                new Regex(@"^ amps $"),
                " 安培 "),
            new KeyValuePair<Regex, string>(
                new Regex(@"^The location of (.+)$"),
                "$1 的位置"),
            new KeyValuePair<Regex, string>(
                new Regex(@"^Ereshkigal delivers insight from the Thin World:\s*$"),
                "埃列什基伽勒从薄世界传来洞见："),
        };

        public static void TryApply()
        {
            if (_applied || Disabled) return;
            _applied = true;
            try
            {
                Type t = FindType("XRL.UI.TerminalScreen");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.UI.TerminalScreen");
                    return;
                }
                MethodInfo m = t.GetMethod("Update", BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 TerminalScreen.Update");
                    return;
                }
                MethodInfo post = typeof(ZhsTerminalPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.terminal");
                h.Patch(m, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 TerminalScreen.Update（" + T.Count + " 条精确 + "
                    + Rules.Count + " 条模板）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e.Message);
            }
        }

        /// <summary>把一个串过表：先精确、再模板；**含汉字直接跳过**（幂等）。</summary>
        internal static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s) || Disabled) return s;
            if (HasCjk(s)) return s;
            string zh;
            if (T.TryGetValue(s, out zh)) return zh;
            // 选项在渲染时会被包成 `{{|…}}` ⇒ 拆壳再试，命中后回填
            if (s.Length > 6 && s.StartsWith("{{|", StringComparison.Ordinal)
                && s.EndsWith("}}", StringComparison.Ordinal))
            {
                string inner = s.Substring(3, s.Length - 5);
                string z2;
                if (T.TryGetValue(inner, out z2)) return "{{|" + z2 + "}}";
                z2 = TryRules(inner);
                if (z2 != null) return "{{|" + z2 + "}}";
            }
            string z3 = TryRules(s);
            return z3 ?? s;
        }

        private static string TryRules(string s)
        {
            // ★ 先按**原样**匹配（` Available power: … amps ` 这类带前后空格的模板），
            //   再按 trim 后的匹配（普通 `^…$` 模板）。
            string z = TryRulesCore(s);
            if (z != null) return z;
            string t = s.Trim();
            return t == s ? null : TryRulesCore(t);
        }

        private static string TryRulesCore(string t)
        {
            foreach (var kv in Rules)
            {
                Match m;
                try { m = kv.Key.Match(t); }
                catch { continue; }
                if (!m.Success) continue;
                string zh = kv.Value;
                for (int g = 1; g < m.Groups.Count; g++)
                    zh = zh.Replace("$" + g, m.Groups[g].Value);
                return zh;
            }
            return null;
        }

        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null || Disabled) return;
                Type ty = __instance.GetType();
                bool any = false;

                FieldInfo fm = Field(ty, "MainText");
                if (fm != null)
                {
                    string s = fm.GetValue(__instance) as string;
                    string z = Tr(s);
                    if (!ReferenceEquals(z, s) && z != s) { fm.SetValue(__instance, z); any = true; }
                }

                FieldInfo fo = Field(ty, "Options");
                if (fo != null)
                {
                    var list = fo.GetValue(__instance) as System.Collections.IList;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            string s = list[i] as string;
                            string z = Tr(s);
                            if (!ReferenceEquals(z, s) && z != s) { list[i] = z; any = true; }
                        }
                    }
                }

                FieldInfo fr = Field(ty, "RenderedText");
                if (fr != null)
                {
                    string s = fr.GetValue(__instance) as string;
                    string z = TrRendered(s);
                    if (!ReferenceEquals(z, s) && z != s) { fr.SetValue(__instance, z); any = true; }
                }

                if (any)
                {
                    _hits++;
                    if (_hits <= 3)
                        UnityEngine.Debug.Log(Tag + "首次替换（第 " + _hits + " 次 Update）类型="
                            + ty.Name);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Postfix 异常：" + e.Message);
            }
        }

        /// <summary>`RenderedText` 逐行翻（保留 `> ` 选中标记与 `a. ` 选项前缀）。</summary>
        private static string TrRendered(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string[] lines = text.Split('\n');
            bool touched = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                int lead = (line[0] == '>') ? 1 : 0;
                string rest = line.Substring(lead);
                string prefix = string.Empty;
                // 选项行形如 `a. {{|…}}`
                if (rest.Length > 3 && char.IsLetter(rest[0]) && rest[1] == '.' && rest[2] == ' ')
                {
                    prefix = rest.Substring(0, 3);
                    rest = rest.Substring(3);
                }
                string zh = Tr(rest);
                if (ReferenceEquals(zh, rest) || zh == rest) continue;
                lines[i] = line.Substring(0, lead) + prefix + zh;
                touched = true;
            }
            return touched ? string.Join("\n", lines) : text;
        }

        /// <summary>先字段、再属性（§53：`GetProperty` 取字段会恒 null）。</summary>
        private static FieldInfo Field(Type ty, string name)
        {
            for (Type t = ty; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance);
                if (f != null) return f;
            }
            return null;
        }

        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '\u4e00' && s[i] <= '\u9fff') return true;
            return false;
        }

        private static Type FindType(string full)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(full, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
