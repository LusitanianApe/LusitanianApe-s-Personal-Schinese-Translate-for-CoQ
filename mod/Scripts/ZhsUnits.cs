// ═══════════════════════════════════════════════════════════════════════════
// 单位 / 取值词替换 —— `rounds` ⇒ **回合** · `sight` ⇒ **视野** …
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户 2026-10-11 指示（rounds）】
//     「rounds 破案了，**不要管什么出口，全部翻译成回合就行**」
//   ⇒ 不再逐个出口去追（它是 `String.Concat` + `StatCollector::Set` 直拼的，
//     见 `patch_notes/2.16.124.md` §五），改为**统一的词级替换**。
//
// 【用户 2026-10-15 指示（sight 与同族）】
//     「sight ⇒ 「视野」改，**同族的没覆盖的一起改掉**」
//   ⇒ 族的定义 = 送进 `XRL.Templates.StatCollector::Set(key, value)` 的**字面量取值**。
//     全量枚举工具（本轮新建）：
//       `_tools\_audit_statcollector_pairs.py`  同 (owner,方法,IL偏移) 归组 ⇒ 还原 `Set(键, 值)` 对
//         ⇒ arg1 未覆盖 **39 条**
//       `_tools\_audit_unit_candidates.py`      对候选做**全语料反例检查**（34,800 条 #US 字面量）
//         ⇒ 例如 `sight` 命中 13 条，其中
//              `a sight older than sight` / `lost sight of …` / `at the sight of …`
//            都是**散文** ⇒ ★ **裸词替换会改坏散文** ⇒ 必须按形态收窄（见下）
//     39 条里的**非文本**（骰子记法 `1d8`/`1d2`、区域 `3x3`/`7x7`、数值 `16-20`、
//       布尔 `true`、内部 ID `quillBP`/`maxDecoys`/`chargeuse`）**一律不动** ✓
//
// 【安全】
//   · `rounds` 族：只在**含数字**或**独立成词**时替换；`surround` 这类字母内部的 `round` **不动**
//     （`\b` 词边界 + 前置 `(\d)`）
//     ★ 2.16.128 修正：**只换复数 `rounds`** —— `round` 还作**形容词**（圆的），
//       实测 `two round shapes` / `perfectly round` / `perversely round and bumpy`
//     · 槽内一律不替换（`=subject.Does:need= =left.things:more round:more rounds=` 里
//       `rounds` 是**复数化参数**，不是显示文本）
//   · 取值词表：**只认两种形态** —— ① 整串（可含首尾空白）② 紧跟**中文冒号** `：`
//     ⇒ 散文里出现的同名普通词**不会**被碰到（这是本轮最重要的护栏）
//   · 开关：`_zhs_units_off.txt`
//   · 异常一律返回原串
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsUnits
    {
        public static bool Disabled;
        internal static int Hits;
        internal static int ValueHits;

        // ────────────────────────────────────────────────────────────────
        // ① 取值词表（`StatCollector::Set` 的 arg1 字面量族）
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// ★ 顺序有意义：**长的在前**（`Scavenger,TrashDivining` 必须先于 `Scavenger` 匹配）。
        /// 译名来源：`Skills.xml` 的 `DisplayName`（Scavenger=拾荒者 · Trash Divining=垃圾占卜）。
        /// </summary>
        private static readonly string[][] Values =
        {
            new[] { "sight", "视野" },
            new[] { "once per night", "每夜一次" },
            new[] { "Quickness bonus", "迅捷加成" },
            new[] { "Agility bonus", "敏捷加成" },
            new[] { "Move speed bonus", "移动速度加成" },
            new[] { "Squares of stasis field", "静滞场格数" },
            new[] { "Strength / Agility vs. character level", "力量／敏捷 对抗角色等级" },
            new[] { "-50% move speed", "移动速度 −50%" },
            new[] { "Toasts affected area over 3 rounds, dealing damage and setting things on fire\n",
                    "炙烤受影响区域 3 回合，造成伤害并点燃目标\n" },
            new[] { "Scavenger,TrashDivining", "拾荒者、垃圾占卜" },
            new[] { "Scavenger", "拾荒者" },
            new[] { "TrashDivining", "垃圾占卜" },
            new[] { "decoy", "诱饵" },
            new[] { "decoys", "诱饵" },
            new[] { "Radius", "半径" },
            new[] { "Tethered", "已系连" },
            new[] { "Pistol", "手枪" },
            new[] { "{{R|Uninitialized}}", "{{R|未初始化}}" },
            // ★ 2026-10-15：`StatCollector::AddChangePostfix` 的 `what` 段常见标签
            //   （反编译其调用点实证：`Penetration bonus` / `To-hit penalty` / `DV bonus` /
            //     `To-hit bonus` / `Move speed multiplier` / `Damage` / `Cooldown`）
            //   ⇒ 与上面的取值词表**同一张表**（§36：别建第二张同义表）
            new[] { "Penetration bonus", "穿透加成" },
            new[] { "To-hit penalty", "命中惩罚" },
            new[] { "To-hit bonus", "命中加成" },
            new[] { "DV bonus", "闪避加成" },
            new[] { "Move speed multiplier", "移动速度倍率" },
            new[] { "Damage", "伤害" },
        new[] { "damage", "伤害" },
            new[] { "Cooldown", "冷却" },
            // ★ 2026-10-15：毒液类型（`StatCollector::Set("VenomType", …)` 的取值）
            //   译名与 `ZhsMutationZh` 的 DisplayName 一致：致幻／麻痹／剧毒（§10 同词同译）
            new[] { "confusing", "致幻" },
            new[] { "paralyzing", "麻痹" },
            new[] { "poisoning", "剧毒" },
        };

        /// <summary>
        /// 形态 ①：整串（允许首尾空白，替换后原样保留空白）
        /// 形态 ②：紧跟**中文冒号**（`射程：sight` / `冷却：once per night`）
        /// </summary>
        private static string ApplyValueTable(string s)
        {
            foreach (string[] kv in Values)
            {
                if (s.Length == kv[0].Length)
                {
                    if (s == kv[0]) { ValueHits++; return kv[1]; }
                    continue;
                }
                string trim = s.Trim();
                if (trim == kv[0])                       // 仅首尾空白不同 ⇒ 保留空白
                {
                    ValueHits++;
                    return s.Replace(kv[0], kv[1]);
                }
            }
            if (s.IndexOf('：') >= 0)
            {
                foreach (string[] kv in Values)
                {
                    string from = "：" + kv[0];
                    if (s.IndexOf(from, StringComparison.Ordinal) >= 0)
                    {
                        s = s.Replace(from, "：" + kv[1]);
                        ValueHits++;
                    }
                }
            }
            return s;
        }

        // ────────────────────────────────────────────────────────────────
        // ② `rounds` 族（2.16.127 建 · 2.16.128 修）
        // ────────────────────────────────────────────────────────────────
        /// <summary>`15rounds` / `80 rounds` / `7-9 rounds` ⇒ `15 回合` / `80 回合` / `7-9 回合`</summary>
        private static readonly Regex WithNumber =
            new Regex(@"(\d)\s*rounds?\b", RegexOptions.Compiled);

        /// <summary>
        /// ★★ 2.16.128 修正：**只换复数 `rounds`**。
        ///
        /// 【为什么必须去掉单数】（2.16.127 发出去后自查抓到的**真 bug**）
        ///   `round` 在游戏文本里**还作形容词**（「圆的」）：
        ///     `A cylinder is a three dimensional shape with two round shapes at either end…`
        ///     `A sphere is a geometrical figure that is perfectly round, 3-dimensional…`
        ///     `Your limbs suddenly feel perversely round and bumpy before they shrink…`
        ///   ⇒ 原先的 `rounds?` 会产出「two 回合 shapes」「perfectly 回合」。
        ///   ★ 判据：**形容词只用单数 `round`；作单位的复数必然是 `rounds`**
        ///     （实测 heap 里 34,800 条字面量，`rounds` 无一处作形容词）。
        /// </summary>
        private static readonly Regex BarePlural =
            new Regex(@"(?<![A-Za-z])rounds(?![A-Za-z])", RegexOptions.Compiled);

        /// <summary>紧跟槽位收尾 `}}` 的**单数** `round`（如 `}} round cooldown`）—— 这种一定是单位。</summary>
        private static readonly Regex AfterSlot =
            new Regex(@"\}\}\s*round(?![A-Za-z])", RegexOptions.Compiled);

        /// <summary>被 `per` / `each` / `every` 修饰的**单数** `round` —— 一定是单位。</summary>
        private static readonly Regex AfterAdverb =
            new Regex(@"(?<![A-Za-z])(per|each|every)\s+round(?![A-Za-z])", RegexOptions.Compiled);

        /// <summary>
        /// 按**整串精确**查取值词表（未命中返回原串）。
        /// ★ 供其它出口复用（如 `StatCollector::AddChangePostfix` 的 `what` 段）—— §36「共用纯函数」，
        ///   避免再建第二张同义表（本项目在「同一把尺子」上吃过亏，见 §56）。
        /// </summary>
        internal static string MapValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (string[] kv in Values)
                if (s == kv[0]) return kv[1];
            return s;
        }

        public static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                // ① 取值词表（整串 / 冒号形态）—— 先跑，命中即返回
                string v = ApplyValueTable(s);
                if (v != s)
                {
                    // ★ 日志**单独包 try**：否则日志一抛异常就会掉进外层 catch 返回**原串**
                    //   （= 静默失效，正是本项目最怕的那种 bug）
                    try
                    {
                        if (ValueHits <= 8)
                            UnityEngine.Debug.Log("[QudZhsCookbook] 单位/取值词替换（累计 " + ValueHits + "）");
                    }
                    catch { }
                    return v;
                }

                // ② 单位词族（rounds / move actions / squares / day）
                if (s.IndexOf("round", StringComparison.OrdinalIgnoreCase) < 0
                    && s.IndexOf("{{rules|", StringComparison.Ordinal) < 0) return s;
                string t = FixOutsideSlots(s);
                if (t != s)
                {
                    Hits++;
                    try
                    {
                        if (Hits <= 8)
                            UnityEngine.Debug.Log("[QudZhsCookbook] 单位替换：round(s) ⇒ 回合（累计 "
                                + Hits + "）");
                    }
                    catch { }
                }
                return t;
            }
            catch
            {
                return s;
            }
        }

        // ── ★★ 2026-10-15 新增：`{{rules|N}}` 后面的**单位词** ──
        //   实证（本局 Player.log 的「等级文本调用」原文）：
        //     `吐丝持续 吐丝持续: {{rules|6}}move actions\n`
        //     `你能察觉 {{rules|9}}squares 范围内的生物…`
        //     `获得 {{rules|1}}day 的特殊代谢效果…`
        //   ⇒ 三者都以 **`{{rules|N}}` 紧跟单位词**的形态出现（数字在标记里，不是裸数字）
        //   ★★ 为什么**必须绑 `rules` 标记**（语料反例检查）：
        //     `squares` 裸词在 34,800 条字面量里命中 **55 条**，绝大多数是散文：
        //       `…bounded by six square faces…` / `…by moving into its square…` /
        //       `Sometimes squares have multiple items in them.`
        //     ⇒ 裸词替换会改坏这些 ✗；绑 `{{rules|N}}` 后**零误伤** ✓
        //     （`day` 裸词 70 条同理：`Previous Day` / `Next Day` / `=time.day=` 等）
        /// <summary>`{{rules|N}}` + 单位词 ⇒ `{{rules|N}} 中文单位`。</summary>
        private static readonly Regex AfterRules = new Regex(
            @"(\{\{rules\|[^}]*\}\})\s*(move actions?|squares?|days?)\b",
            RegexOptions.Compiled);
        private static readonly Dictionary<string, string> RuleUnits =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "move action", "移动动作" }, { "move actions", "移动动作" },
            { "square", "格" }, { "squares", "格" },
            { "day", "天" }, { "days", "天" },
        };

        /// <summary>`=…=` 槽区间（槽的**参数里也可能含 round**，见下）。</summary>
        private static readonly Regex SlotSpan = new Regex(@"=[^=\n]{0,90}?=");

        /// <summary>
        /// ★★ 2.16.128：**槽内一律不替换**。
        ///
        /// 【为什么】全语料验证抓到一条：
        ///     `=subject.Does:need= =left.things:more round:more rounds= before …`
        ///   —— `rounds` 是那个槽的**复数化参数**（`more round:more rounds`），
        ///   不是显示文本。改了会把槽的语义搞坏。
        ///   ⇒ 先按 `=…=` 切成「槽 / 非槽」两段，**只对非槽段**跑替换规则。
        ///   （其它 16 条预期改动全部落在非槽段 ⇒ 不受影响）
        /// </summary>
        private static string FixOutsideSlots(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            int pos = 0;
            foreach (Match m in SlotSpan.Matches(s))
            {
                if (m.Index > pos)
                    sb.Append(ApplyRules(s.Substring(pos, m.Index - pos)));
                sb.Append(m.Value);              // 槽原样保留
                pos = m.Index + m.Length;
            }
            if (pos < s.Length) sb.Append(ApplyRules(s.Substring(pos)));
            return sb.ToString();
        }

        private static string ApplyRules(string s)
        {
            // ★ 早退守卫要覆盖全部单位词（漏一个就会静默不处理）
            if (s.IndexOf("round", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("move action", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("square", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("day", StringComparison.OrdinalIgnoreCase) < 0)
                return s;
            string t = WithNumber.Replace(s, "$1 回合");
            t = BarePlural.Replace(t, "回合");
            t = AfterSlot.Replace(t, "}} 回合");
            t = AfterAdverb.Replace(t, "$1 回合");
            // ★ 2026-10-15：`{{rules|N}}` 后的单位词（move actions / squares / day）
            t = AfterRules.Replace(t, m =>
            {
                string zh;
                return RuleUnits.TryGetValue(m.Groups[2].Value, out zh)
                    ? m.Groups[1].Value + " " + zh : m.Value;
            });
            return t;
        }
    }
}
