// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsLevelTextPatch —— 变异「等级文本」的硬编码英文（测试窗口 #39）
//
//  背景（2026-10-17 实机）：
//    变异「混乱」    → `Cone angle: 30degrees` / `Cone length: 4`
//    变异「火焰射线」→ `2d8degrees`
//    我方 `Mutations.xml` 的 `<leveltext>` 写得再对也被**类方法覆盖**。
//
//  产地（反编译，12 个类）：`GetLevelText(int)` 直接 `string.Concat` 硬编码：
//    Confusion:
//      "Affected creatures act semi-randomly and receive a -{{rules|N}} penalty …\n"
//      + "Cone angle: {{rules|30}} degrees\n" + "Cone length: {{rules|4}}\n"
//      + "Duration: {{rules|X}} rounds\n" + "Cooldown: X rounds"
//    FireBreather 族（10 个）:
//      "Breathes … in a cone.\n" + "Damage: X\n" + "Cone length: X tiles\n"
//      + "Cone angle: X degrees\n" + "Cooldown: 15 rounds\n"
//    FlamingRay / FreezingRay:
//      "Emits a N-square ray of flame …\n" + "Damage: {{rules|X}}\n"
//      + "Cooldown: 10 rounds\n" + "Melee attacks heat opponents by {{rules|X}} degrees"
//
//  ★ 做法：**不给 12 个类各写一条整句**，只挂 `GetLevelText` 的 Postfix，
//    用一张**片段替换表**把英文骨架换成中文（数字/色码原样保留）。
//  ★ 幂等：结果含汉字就不动（避免二次替换）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsLevelTextPatch
    {
        public static bool Disabled;
        private static bool _applied;
        private static int _hits;
        private const string Tag = "[QudZhsCookbook] 等级文本：";

        /// <summary>12 个硬编码 `GetLevelText` 的类（反编译 + census 交叉确认）。</summary>
        private static readonly string[] Classes =
        {
            "XRL.World.Parts.Mutation.Confusion",
            "XRL.World.Parts.Mutation.ConfusionBreather",
            "XRL.World.Parts.Mutation.CorrosiveBreather",
            "XRL.World.Parts.Mutation.FireBreather",
            "XRL.World.Parts.Mutation.IceBreather",
            "XRL.World.Parts.Mutation.NormalityBreather",
            "XRL.World.Parts.Mutation.PoisonBreather",
            "XRL.World.Parts.Mutation.ShameBreather",
            "XRL.World.Parts.Mutation.SleepBreather",
            "XRL.World.Parts.Mutation.StunBreather",
            "XRL.World.Parts.Mutation.FlamingRay",
            "XRL.World.Parts.Mutation.FreezingRay",
        };

        /// <summary>片段替换表（**长片段在前**，避免被短片段抢先）。</summary>
        private static readonly (string En, string Zh)[] Frags =
        {
            ("Affected creatures act semi-randomly and receive a -",
             "受影响的生物行动半随机，其心智能力受到 -"),
            (" penalty to their mental abilities.", " 的惩罚。"),
            ("{{rules|Increased temperature}}", "{{rules|温度提升}}"),
            ("Cooldown: ", "冷却时间："),
            ("Duration: ", "持续时间："),
            ("Cone length: ", "锥形长度："),
            ("Cone angle: ", "锥形角度："),
            ("Damage: ", "伤害："),
            ("Melee attacks heat opponents by ", "近战攻击使对手升温 "),
            (" tiles", " 格"),
            (" tile", " 格"),
            (" degrees", " 度"),
            ("degrees", "度"),
            (" rounds", " 回合"),
            (" round", " 回合"),
        };

        /// <summary>整句/句式（先跑）。</summary>
        private static readonly (Regex Rx, string Zh)[] Rules =
        {
            (new Regex(@"^Emits a (\d+)-square ray of flame in the direction of your choice\.$"),
             "朝你选择的方向发射一道 $1 格长的火焰射线。"),
            (new Regex(@"^Emits a (\d+)-square ray of frost in the direction of your choice\.$"),
             "朝你选择的方向发射一道 $1 格长的霜冻射线。"),
        };

        /// <summary>`Breathes <元素> in a cone.` 里的**元素词**（探针类名去重所得）。</summary>
        private static readonly Dictionary<string, string> Elements =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fire", "火焰" },
            { "flame", "火焰" },
            { "cold", "寒冷" },
            { "frost", "霜冻" },
            { "ice", "冰霜" },
            { "acid", "酸液" },
            { "corrosive gas", "腐蚀性气体" },
            { "poison", "毒素" },
            { "poison gas", "毒气" },
            { "normality gas", "常态气体" },
            { "sleep gas", "催眠气体" },
            { "shame gas", "羞耻气体" },
            { "stunning gas", "眩晕气体" },
            { "confusing gas", "混乱气体" },
            { "steam", "蒸汽" },
            { "lightning", "闪电" },
        };

        private static readonly Regex RE_BREATH = new Regex(@"^Breathes (.+?) in a cone\.$");

        public static void TryApply()
        {
            if (_applied || Disabled) return;
            _applied = true;
            int n = 0;
            try
            {
                MethodInfo post = typeof(ZhsLevelTextPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.leveltext");
                foreach (string name in Classes)
                {
                    Type t = FindType(name);
                    if (t == null) continue;
                    MethodInfo m = null;
                    foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (mi.Name != "GetLevelText") continue;
                        ParameterInfo[] ps = mi.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != typeof(int)) continue;
                        if (mi.ReturnType != typeof(string)) continue;
                        m = mi;
                        break;
                    }
                    if (m == null) continue;
                    try
                    {
                        h.Patch(m, postfix: new HarmonyMethod(post));
                        n++;
                    }
                    catch { }
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + "/" + Classes.Length + " 个 GetLevelText");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e.Message);
            }
        }

        internal static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s) || Disabled) return s;
            if (HasCjk(s)) return s;      // 幂等：已经翻过
            string[] lines = s.Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                string zh = Line(line);
                if (zh != line) { lines[i] = zh; any = true; }
            }
            return any ? string.Join("\n", lines) : s;
        }

        private static string Line(string line)
        {
            string t = line.Trim();
            // ① `Breathes <元素> in a cone.` —— 元素词查表（查不到原样留英文，不半译）
            Match bm;
            try { bm = RE_BREATH.Match(t); }
            catch { bm = null; }
            if (bm != null && bm.Success)
            {
                string el = bm.Groups[1].Value;
                string zel;
                if (Elements.TryGetValue(el, out zel)) return "以锥形喷吐" + zel + "。";
                return "以锥形喷吐" + el + "。";
            }
            foreach (var kv in Rules)
            {
                Match m;
                try { m = kv.Rx.Match(t); }
                catch { continue; }
                if (!m.Success) continue;
                string zh = kv.Zh;
                for (int g = 1; g < m.Groups.Count; g++)
                    zh = zh.Replace("$" + g, m.Groups[g].Value);
                return zh;
            }
            string o = line;
            foreach (var f in Frags)
            {
                if (o.IndexOf(f.En, StringComparison.Ordinal) < 0) continue;
                o = o.Replace(f.En, f.Zh);
            }
            return o;
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                string zh = Tr(__result);
                if (ReferenceEquals(zh, __result) || zh == __result) return;
                __result = zh;
                _hits++;
                if (_hits <= 3)
                    UnityEngine.Debug.Log(Tag + "首次替换 #" + _hits);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Postfix 异常：" + e.Message);
            }
        }

        private static bool HasCjk(string s)
        {
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
