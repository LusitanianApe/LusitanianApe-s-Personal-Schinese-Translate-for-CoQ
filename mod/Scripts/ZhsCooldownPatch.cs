// ═══════════════════════════════════════════════════════════════════════════
// 技能冷却提示汉化 —— 挂 `XRL.Templates.StatCollector::CollectCooldownTurns`
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图「严查」（技能面板 Slam）】
//     冷却：**53 rounds**
//     **Cooldown increased by 2 due to low 意志.**
//   （更早那张截图里这条是半中文 `… due to low 意志.` ⇒ 现在整条英文，
//     因为 `due to low ` 是硬编码、而**原因词**走另一条路。）
//
// 【反编译实证 —— 同一个方法同时产出这两处】
//     // XRL.Templates.StatCollector::CollectCooldownTurns
//     foreach (GetCooldownEvent.CooldownCalculation calculation in getCooldownEvent.Calculations)
//     {
//         …
//         if (num > 0)
//             postfix += $"\nCooldown increased by {num} due to {calculation.Reason}.";
//         else if (num < 0)
//             postfix += $"\nCooldown reduced by {-num} due to {calculation.Reason}.";
//     }
//     if (getCooldownEvent.ResultUncapped != getCooldownEvent.Result)
//     {
//         string text = ((getCooldownEvent.Result == 60) ? "5" : (getCooldownEvent.Result / 10).ToString());
//         postfix = postfix + "\nCooldown cannot be reduced below " + text + " rounds.";   // ★ `rounds`
//     }
//   ⇒ **C# 字符串插值**，字符串表一个字都够不着。
//   （同族还有 `{0} increased by {1} due to {2}.` 等一批，宿主是同一个类的别的 Collect* 方法，
//     本版先把**用户可见的冷却**这条收掉。）
//
// 【做法】Postfix 挂 `CollectCooldownTurns`，对 `postfix` 字段做**锚定正则**重写：
//     · `Cooldown increased by {n} due to {reason}.`  →  `冷却因{reason}延长 {n} 回合。`
//     · `Cooldown reduced by {n} due to {reason}.`   →  `冷却因{reason}缩短 {n} 回合。`
//     · `Cooldown cannot be reduced below {n} rounds.` → `冷却不能缩短到 {n} 回合以下。`
//     · `{n} rounds`（冷却行本体）→ `{n} 回合`
//   `{reason}` 是运行期文本（可能是中文「意志」或英文）⇒ **原样保留**，只换骨架。
//
// 【安全】`_zhs_cooldown_off.txt` 可停用；异常一律不动（保留英文）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsCooldownZh
    {
        public static bool Disabled;
        internal static int Hits;

        // `\nCooldown increased by 2 due to low 意志.`
        private static readonly Regex INC = new Regex(
            @"Cooldown increased by (?<n>\d+) due to (?<r>.+?)\.(?=\s|$)",
            RegexOptions.Compiled);
        private static readonly Regex RED = new Regex(
            @"Cooldown reduced by (?<n>\d+) due to (?<r>.+?)\.(?=\s|$)",
            RegexOptions.Compiled);
        private static readonly Regex FLOOR = new Regex(
            @"Cooldown cannot be reduced below (?<n>[\d.]+) rounds\.",
            RegexOptions.Compiled);
        // `53 rounds` / `53 rounds.` —— 只认「数字 + 空格 + rounds」
        private static readonly Regex ROUNDS = new Regex(
            @"(?<n>\d+(?:\.\d+)?) rounds\b",
            RegexOptions.Compiled);

        // ── ★★ 2026-10-15 新增：`AddChangePostfix` / `AddPercentChangePostfix` 家族 ──
        //   模板（`StatCollector` 反编译原文）：
        //     `"\n{0} increased by {1} due to {2}."`
        //     `"\n{0} increased by {1}% due to {2}."`
        //     `"\n{0} increased by {1}-{2} due to {3}."`
        //     以及 `reduced` 对偶（同族，成组维护 · §45）
        //   ★ 顺序：**区间先于 `%`、`%` 先于单值** —— 否则 `1-2` 会被单值规则吃掉前半
        //   ★ 中文语序：`{标签}因{原因}提升 {N}。`（与 2.16.115 的「因力量」口径一致）
        private const string CHG_W = @"(?m)^(?<w>[^\n]+?) ";
        private const string CHG_R = @" due to (?<r>[^\n]+?)\.$";

        private static readonly Regex CHG_INC_RANGE = new Regex(
            CHG_W + @"increased by (?<a>\d+)-(?<b>\d+)" + CHG_R, RegexOptions.Compiled);
        private static readonly Regex CHG_RED_RANGE = new Regex(
            CHG_W + @"reduced by (?<a>\d+)-(?<b>\d+)" + CHG_R, RegexOptions.Compiled);
        private static readonly Regex CHG_INC_PCT = new Regex(
            CHG_W + @"increased by (?<a>\d+)%" + CHG_R, RegexOptions.Compiled);
        private static readonly Regex CHG_RED_PCT = new Regex(
            CHG_W + @"reduced by (?<a>\d+)%" + CHG_R, RegexOptions.Compiled);
        private static readonly Regex CHG_INC = new Regex(
            CHG_W + @"increased by (?<a>\d+)" + CHG_R, RegexOptions.Compiled);
        private static readonly Regex CHG_RED = new Regex(
            CHG_W + @"reduced by (?<a>\d+)" + CHG_R, RegexOptions.Compiled);

        /// <summary>`what` 段（统计项显示名）⇒ 中文：复用 `ZhsUnits` 的取值词表（单一事实源）。</summary>
        private static string What(string en)
        {
            return ZhsUnits.MapValue(en == null ? null : en.Trim());
        }

        /// <summary>返回改写后的串；无改动返回 null。</summary>
        internal static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string neo = s;
            neo = INC.Replace(neo, m =>
                "冷却因" + Reason(m.Groups["r"].Value) + "延长 " + m.Groups["n"].Value + " 回合。");
            neo = RED.Replace(neo, m =>
                "冷却因" + Reason(m.Groups["r"].Value) + "缩短 " + m.Groups["n"].Value + " 回合。");
            neo = FLOOR.Replace(neo, m =>
                "冷却不能缩短到 " + m.Groups["n"].Value + " 回合以下。");
            neo = ROUNDS.Replace(neo, m => m.Groups["n"].Value + " 回合");

            // ★★ 2026-10-15：`{0} increased by {1} due to {2}` 家族（**区间 → % → 单值**）
            neo = CHG_INC_RANGE.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "提升 " + m.Groups["a"].Value + "–"
                + m.Groups["b"].Value + "。");
            neo = CHG_RED_RANGE.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "降低 " + m.Groups["a"].Value + "–"
                + m.Groups["b"].Value + "。");
            neo = CHG_INC_PCT.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "提升 " + m.Groups["a"].Value + "%。");
            neo = CHG_RED_PCT.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "降低 " + m.Groups["a"].Value + "%。");
            neo = CHG_INC.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "提升 " + m.Groups["a"].Value + "。");
            neo = CHG_RED.Replace(neo, m => What(m.Groups["w"].Value) + "因"
                + Reason(m.Groups["r"].Value) + "降低 " + m.Groups["a"].Value + "。");

            return neo == s ? null : neo;
        }

        /// <summary>
        /// 原因短语里的**英文程度词**换中文（截图实测：`due to low 意志`）。
        /// 只换 `low `/`high ` 这类**固定前缀**，其余原样保留（原因词可能已是中文）。
        /// </summary>
        private static readonly (string En, string Zh)[] REASON_PREFIX =
        {
            ("low ", "低"),
            ("high ", "高"),
            ("very low ", "极低"),
            ("very high ", "极高"),
        };

        private static string Reason(string r)
        {
            if (string.IsNullOrEmpty(r)) return "";
            string s = r.Trim();
            foreach (var (en, zh) in REASON_PREFIX)
            {
                if (s.StartsWith(en, StringComparison.OrdinalIgnoreCase))
                    return zh + MapStat(s.Substring(en.Length).Trim());
            }
            return MapStat(s);
        }

        /// <summary>
        /// 原因里的**人名/属性名/固定短语** ⇒ 中文（★ 单一事实源，§36）：
        ///   ① 已含中文 ⇒ 原样（`low 意志` 这种半中半英很常见）
        ///   ② 剥掉 `a ` / `an ` / `your ` 前缀
        ///   ③ 固定短语 ⇒ `ZhsPopupPatch.ReasonPhrase`
        ///   ④ 属性名 ⇒ `ZhsStatPopup.St`（`Ego ⇒ 自我` 等）
        /// </summary>
        private static string MapStat(string x)
        {
            if (string.IsNullOrEmpty(x)) return x;
            foreach (char c in x)
                if (c >= '\u4e00' && c <= '\u9fff') return x;
            string y = x;
            if (y.StartsWith("a ", StringComparison.OrdinalIgnoreCase)) y = y.Substring(2);
            else if (y.StartsWith("an ", StringComparison.OrdinalIgnoreCase)) y = y.Substring(3);
            else if (y.StartsWith("your ", StringComparison.OrdinalIgnoreCase)) y = y.Substring(5);
            string fixedZh = ZhsPopupPatch.ReasonPhrase(y.Trim());
            if (fixedZh != null) return fixedZh;
            return ZhsStatPopup.St(y.Trim());
        }
    }

    internal static class ZhsCooldownPatch
    {
        private const string Tag = "[QudZhsCookbook] 冷却提示：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsCooldownZh.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                // ★★ 2026-10-15 修（子代理 B 取证）：`StatCollector` 是 **`XRL.Templates` 的嵌套类型**
                //   （反射名 `XRL.Templates+StatCollector`）⇒ 点号写法
                //   `asm.GetType("XRL.Templates.StatCollector")` **恒返回 null**
                //   ⇒ 本补丁**从未挂载过**（Player.log 实证：`冷却提示：找不到 XRL.Templates.StatCollector`）
                //   ⇒ 用户截图里的 `Cooldown increased by 2 due to low 意志.` 至今英文，**这就是直接原因**。
                //   （§46 同族：挂载失败 = 补丁等于不存在；而这条失败还是**静默**的）
                //   判据：装机后 `Player.log` 必须由「找不到」变成「已挂」，并出现「冷却因…延长 N 回合。」
                Type t = FindNested("XRL.Templates", "StatCollector");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Templates+StatCollector（嵌套类型）");
                    return;
                }
                // ★★ 2026-10-15 扩挂载点（子代理 B 取证）：
                //   四个 `{N}` 模板的生产者都在 `StatCollector` 上，且都写同一个 **public** 字段 `postfix`：
                //     `AddChangePostfix(what, change, dueTo)`      → `"\n{0} increased by {1} due to {2}."`
                //     `AddChangePostfix(what, min, max, dueTo)`    → `"…{1}-{2}…"`（**重载**）
                //     `AddPercentChangePostfix(what, change, dueTo)` → `"…{1}%…"`
                //     `CollectCooldownTurns(…)`                    → `"\nCooldown increased by {0} due to {1}."`
                //   ⇒ 同一个 Postfix 一起挂（读/写 `postfix` 字段，纯文本改写，幂等）
                //   ★ 顺序无关：Postfix 在方法体之后跑 ⇒ 字段里已是**拼好的成品串**
                MethodInfo post = typeof(ZhsCooldownPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                string[] targets = { "CollectCooldownTurns", "AddChangePostfix",
                                     "AddPercentChangePostfix" };
                int mounted = 0;
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (System.Array.IndexOf(targets, mi.Name) < 0) continue;
                    // 同名重载要**都挂**（`AddChangePostfix` 有两个）⇒ 用签名去重
                    string sig = mi.Name + "(" + mi.GetParameters().Length + ")";
                    if (!seen.Add(sig)) continue;
                    try
                    {
                        new Harmony("Nattens.QudZhsCookbook.cooldown." + sig)
                            .Patch(mi, postfix: new HarmonyMethod(post));
                        mounted++;
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError(Tag + "挂 " + sig + " 失败：" + ex.Message);
                    }
                }
                if (mounted == 0)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到任何 StatCollector 模板方法");
                    return;
                }
                UnityEngine.Debug.Log(Tag + "已挂 StatCollector 模板方法 " + mounted
                    + " 个（冷却增减原因 · 「N 回合」单位 · `{0} increased by {1} due to {2}` 家族）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>直接改 `postfix` 字段（它是 public string）。</summary>
        private static void Postfix(object __instance)
        {
            if (ZhsCooldownZh.Disabled || __instance == null) return;
            try
            {
                FieldInfo f = __instance.GetType().GetField("postfix",
                    BindingFlags.Public | BindingFlags.Instance);
                if (f == null) return;
                string cur = f.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(cur)) return;
                string neo = ZhsCooldownZh.Fix(cur);
                if (neo == null) return;
                f.SetValue(__instance, neo);
                if (ZhsCooldownZh.Hits < 6)
                {
                    UnityEngine.Debug.Log(Tag + cur.Replace("\n", "⏎")
                        + "  =>  " + neo.Replace("\n", "⏎"));
                }
                ZhsCooldownZh.Hits++;
            }
            catch { }
        }
            /// <summary>
        /// 取**嵌套类型**（`outer` 下的 `nested`）。
        /// ★ 点号写法 `GetType("A.B")` 取不到嵌套类型 —— 嵌套类型的反射名是 `A+B`。
        /// </summary>
        private static Type FindNested(string outerName, string nestedName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type outer = asm.GetType(outerName, false);
                    if (outer == null) continue;
                    Type nested = outer.GetNestedType(nestedName,
                        BindingFlags.Public | BindingFlags.NonPublic);
                    if (nested != null) return nested;
                }
                catch { }
            }
            return null;
        }
}

}
