// ═══════════════════════════════════════════════════════════════════════════
// 效果说明汉化 —— 兜住**绕过模板**的硬编码
// ═══════════════════════════════════════════════════════════════════════════
//
// 【背景（2026-10-09 用户截图：状态面板里 `flying` 的说明是英文）】
//
//   `XRL.World.Effect` 的基类实现是**数据驱动**的：
//       public virtual string GetDetails()
//       {
//           if (Templates.TemplateByID.TryGetValue("Effect." + GetType().Name + ".Details", out var value))
//               return value.Build(CollectStats, "effect details").TrimEnd('\r', '\n');
//           return "[effect details]";
//       }
//   ⇒ 官方把 49 条说明放在 `Base\EffectsDetails.xml`（模板 ID = `Effect.<类名>.Details`）
//     **本模组已提供中文版同名文件**覆盖那 49 条。
//
//   ⚠ 但有些效果类**override 了 GetDetails 并直接返回字面量**，绕过模板：
//       public override string GetDetails()      // XRL.World.Effects.Flying
//       {
//           return "Can't be attacked in melee by non-flying creatures.\nIsn't affected by terrain.";
//       }
//   ⇒ 数据侧**够不着**，只能挂出口。
//
// 【做法】运行时枚举 `XRL.World.Effect` 的**所有子类**，给「声明了 GetDetails 的类型」
//   逐个挂 Postfix（基类也挂一次，兜住「覆盖了但没改文本」的情况）。
//   Postfix 用一张**整串精确匹配**的译表替换；命不中一律原样放行 ⇒ 零误伤。
//
// 【为什么不用 `decomp` 先列出全部硬编码】工具给不出全量类型清单；
//   运行时枚举是**等价且更可靠**的（它枚举的就是真实加载的类型）。
//
// 【安全】全部 try/catch；`_zhs_effectdetails_off.txt` 可停用；幂等（只挂一次）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsEffectDetails
    {
        internal const string Tag = "[QudZhsCookbook] 效果说明：";
        internal static int Hits;

        /// <summary>
        /// 硬编码说明的**整串精确匹配**译表。
        /// 键 = DLL 里 `GetDetails()` 返回的原样（含 `\n`）。
        ///
        /// ★★ 2.16.61：从「只 1 条（Flying）」扩到**部件级**覆盖。
        ///
        /// 【为什么要部件级】`XRL.World.Effects.Sitting::GetDetails` 有 **5 个分支**，
        /// 返回 5 种不同的多行组合（级别不同，行数与内容都变）：
        ///     Level ≥ 5    ：Improves / Aids / -6 DV / Must spend a turn
        ///     Level &gt; -2   ：**Slightly** improves / Aids / -6 DV / Must spend a turn
        ///     Level == -2  ：Slightly improves / -6 DV / Must spend a turn
        ///     Level &gt; -10  ：Slightly improves / **Distracts** / -6 DV / Must spend a turn
        ///     否则          ：**Inflicts ongoing damage** / Distracts / -6 DV / Must spend a turn
        /// ⇒ 逐条整串写要写 5 条且以后还会漏。
        /// ⇒ 改为：**先整串查表，未命中再把每一行单独查表**（`LookupLinewise`）。
        ///   这样每个「行级」译文只需写一次，覆盖全部分支组合。
        ///
        /// 【实测】用户截图 `sitting` 那 4 行 = `Level &gt; -2` 分支（第 2 种）。
        /// </summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── Flying（2.16.58 起）──
            { "Can't be attacked in melee by non-flying creatures.\nIsn't affected by terrain.",
              "不会被非飞行生物近战攻击到。\n不受地形影响。" },

            // ── Sitting（2.16.61）—— 行级，覆盖 5 个分支的全部组合 ──
            { "Improves natural healing rate.", "提高自然愈合速度。" },
            { "Slightly improves natural healing rate.", "略微提高自然愈合速度。" },
            { "Aids in examining and disassembling artifacts.", "有助于检视和拆解器物。" },
            { "Distracts from examining and disassembling artifacts.", "会干扰检视和拆解器物。" },
            { "Inflicts ongoing damage.", "造成持续伤害。" },
            { "-6 DV.", "-6 DV。" },
            { "Must spend a turn to stand up before moving.", "移动前必须花一回合站起来。" },

            // ══════════════════════════════════════════════════════════════
            // ★★ 2.16.64：**硬编码普查的效果说明盲区整批补完**
            //
            // 【盲区是怎么来的】老普查（2.16.56 结项）扫的是
            //   「DLL 里的**整句字面量**」；而效果说明是**每个效果类各 override
            //   一个 `GetDetails()` 返回字面量** —— 字符串确实在 DLL 里，
            //   却没进那批候选（`hardcoded_C_碎片.txt` 里这 88 条**一条都没有**）。
            //
            // 【范围（本次用反编译精确数出来，不再靠猜）】
            //   `GetDetails` 覆盖 = **142** 个类型
            //     ├─ **49** 个走模板 `Effect.<类名>.Details`
            //     │    ⇒ 已由我方 `EffectsDetails.xml` 覆盖 ✅
            //     └─ **68** 个**绕过模板**、直接返回字面量
            //          ⇒ 去重后 **88** 条 ← 就是下表
            //   工具：`_tools\_census_effects_literals.py`
            //        产出 `_tools\_effects_getdetails_literals.txt`
            //
            // 【术语口径】`DV` / `AV` / `MA` / `Quickness` / `Move Speed` 等
            //   按既有约定：**DV / AV / MA 保留英文**（用户明确要求），
            //   `Quickness`→迅捷 · `Move Speed`→移动速度 · `Resist`→抗性。
            // ══════════════════════════════════════════════════════════════
            // —— 数值 / 属性 ——
            { "+5 Quickness", "+5 迅捷" },
            { "-5 Quickness", "-5 迅捷" },
            { "-10 Quickness", "-10 迅捷" },
            { "-50 Quickness", "-50 迅捷" },
            { "-60 Quickness", "-60 迅捷" },
            { "-70 Quickness", "-70 迅捷" },
            { "+6 Agility", "+6 敏捷" },
            { "+6 Strength", "+6 力量" },
            { "+9 Strength", "+9 力量" },
            { "+5 Quickness\n+6 Willpower", "+5 迅捷\n+6 意志" },
            { "+6 Willpower", "+6 意志" },
            { "+8 DV", "+8 DV" },
            { "+10 Move Speed", "+10 移动速度" },
            { "-25 Move Speed", "-25 移动速度" },
            { "-10 Move Speed", "-10 移动速度" },
            { "-50% Move Speed", "-50% 移动速度" },
            { "+5% XP gained", "获得经验 +5%" },
            { "+20 Cold Resist", "+20 耐寒" },
            { "-10% to natural healing rate", "自然愈合速度 -10%" },
            { "-60% Quickness", "-60% 迅捷" },
            { "-30% Quickness", "-30% 迅捷" },
            { "+2 temporary hit points per level.", "每级 +2 点临时生命值。" },
            { "+3 temporary hit points per level.", "每级 +3 点临时生命值。" },

            // —— 长刃架势 / 长刃效果 ——
            { "+2 DV while wielding a long blade in the primary hand.",
              "主手持长刃时 +2 DV。" },
            { "+3 DV while wielding a long blade in the primary hand.",
              "主手持长刃时 +3 DV。" },
            { "+2 to hit while wielding a long blade in the primary hand.",
              "主手持长刃时命中 +2。" },
            { "+3 to hit while wielding a long blade in the primary hand.",
              "主手持长刃时命中 +3。" },
            { "+1 to your penetration roll and -2 to hit while wielding a long blade in the primary hand.",
              "主手持长刃时穿透掷骰 +1、命中 -2。" },
            { "+2 to your penetration roll and -3 to hit while wielding a long blade in the primary hand.",
              "主手持长刃时穿透掷骰 +2、命中 -3。" },

            // —— 失能 / 控制 ——
            { "Can't move or attack.", "无法移动或攻击。" },
            { "Can't move or stand up.", "无法移动或站起。" },
            { "Can't move.", "无法移动。" },
            { "Can't take actions.", "无法行动。" },
            { "Can't take physical actions.", "无法进行肢体动作。" },
            { "Can't be attacked in melee by non-flying creatures.", "不会被非飞行生物近战攻击到。" },
            { "Can't be poisoned.", "不会中毒。" },
            { "Can't fast travel.", "无法快速旅行。" },
            { "Can't feel pain.", "感觉不到疼痛。" },
            { "Can't physically interact with creatures and objects.", "无法与生物和物件进行物理互动。" },
            { "Can't move without breaking free first.", "必须先行挣脱才能移动。" },
            { "Cannot take actions.", "无法行动。" },
            { "Cannot fly.", "无法飞行。" },
            { "Cannot fall asleep.", "无法入睡。" },
            { "Cannot be put in sleep mode.", "无法进入睡眠模式。" },
            { "Unable to move.", "无法移动。" },
            { "DV set to 0.", "DV 归零。" },
            { "Blocks all incoming melee attacks.", "格挡所有来袭的近战攻击。" },
            { "Immunity" , "免疫" },

            // —— 免疫 / 抗性 ——
            { "Immune to fear.", "免疫恐惧。" },
            { "Immune to confusing attacks.", "免疫困惑攻击。" },
            { "Suffers double damage from light-based attacks.", "受到的光系攻击伤害加倍。" },
            { "Suffers 1% of max hit points in damage each turn (can't be reduced below one hit point by this effect).",
              "每回合受到最大生命值 1% 的伤害（此效果不会使生命值降到 1 点以下）。" },

            // —— 治疗 / 恢复 ——
            { "5% HP healed per turn.", "每回合恢复 5% 生命值。" },
            { "Stops healing if another action is taken or damage is taken.",
              "若采取其它行动或受到伤害，则停止治疗。" },
            { "Doesn't heal hit points naturally.", "不会自然恢复生命值。" },
            { "External healing is only half as effective.", "外部治疗的效果只有一半。" },
            { "Regenerates at thrice the normal rate.", "以三倍速度再生。" },
            { "Negative status effects wear off at thrice the normal rate.",
              "负面状态以三倍速度消退。" },
            { "Recovers 0.6 hit points per level (minimum 3) each turn.",
              "每回合恢复每级 0.6 点生命值（最少 3 点）。" },
            { "Recovers 0.9 hit points per level (minimum 5) each turn.",
              "每回合恢复每级 0.9 点生命值（最少 5 点）。" },
            { "For the first 5 rounds after imbibing, gains 0.6 hit points per level (minimum 3) each turn.",
              "饮下后的前 5 回合，每回合获得每级 0.6 点生命值（最少 3 点）。" },
            { "Purged of all short-term, biological debuffs and up to one severed appendage is regrown at the end of the tenth turn.",
              "清除所有短期生理减益，并在第十回合结束时再生至多一处断肢。" },
            { "Stops meditating if another action is taken or ", "若采取其它行动或" },

            // —— 相位 / 空间 / 维度 ——
            { "(very dense) Can't take actions.", "（极密）无法行动。" },
            { "+40% Move Speed at night and underground.\n+4 Agility at night and underground.\n-20% Move Speed in the daylight.\n-3 Agility in the daylight.\nCan see in the dark (radius 8).\nSuffers double damage from light-based attacks.",
              "夜间与地下移动速度 +40%。\n夜间与地下敏捷 +4。\n日光下移动速度 -20%。\n日光下敏捷 -3。\n可在黑暗中视物（半径 8）。\n受到的光系攻击伤害加倍。" },
            { "+25% Move Speed at night and underground.\n+3 Agility at night and underground.\n-20% Move Speed in the daylight.\n-3 Agility in the daylight.\nCan see in the dark (radius 8).\nSuffers double damage from light-based attacks.",
              "夜间与地下移动速度 +25%。\n夜间与地下敏捷 +3。\n日光下移动速度 -20%。\n日光下敏捷 -3。\n可在黑暗中视物（半径 8）。\n受到的光系攻击伤害加倍。" },
            { "25% chance per turn to phase out of the spatial dimension for 1-3 turns.",
              "每回合 25% 概率脱离空间维度 1–3 回合。" },
            { "Can phase out of the spatial dimension for 7-9 turns.",
              "可脱离空间维度 7–9 回合。" },
            { "Physically interactive with both in-phase and out-of-phase creatures and objects.",
              "可与同相与异相的生物和物件进行物理互动。" },
            { "Can see into dimensions half a step over.", "能看见半步之遥的其它维度。" },
            { "Stable from implosion.", "稳定，不会内爆。" },
            { "Nothing can emerge.", "无物能够浮现。" },
            { "Suspended in spacetime. Cannot act or be interacted with.",
              "被悬停在时空之中。无法行动，也无法被互动。" },

            // —— 视觉 / 光照 ——
            { "Can see in the dark (radius 8).", "可在黑暗中视物（半径 8）。" },
            { "Can see things at normal distances.", "能以正常距离视物。" },
            { "Casts light in radius 2.", "在半径 2 内发出光照。" },
            { "Radiates light in radius 3.", "在半径 3 内散发光照。" },
            { "Can peer into the near future.", "能窥见近未来。" },
            { "Coruscating with the dim light of a trillion shattered suns.",
              "以万亿颗破碎太阳的微光闪耀。" },
            { "Molting beams of prismatic light.", "正在蜕出棱彩光束。" },

            // —— 移动 / 地形 ——
            { "Isn't affected by terrain.", "不受地形影响。" },
            { "Can walk over webs without getting stuck.", "可以走过蛛网而不被粘住。" },
            { "Moving at full speed.", "正以全速移动。" },
            { "Sprints or power skates at thrice move speed.", "以三倍移动速度冲刺或动力滑行。" },
            { "35% chance to fall prone when moving.", "移动时有 35% 概率跌倒。" },
            { "Getting ready to act.", "正准备行动。" },
            { "Fleeing in panic.", "惊慌逃窜。" },
            { "Is being dragged.", "正被拖行。" },
            { "Mobility impaired due to missing or broken limbs.", "因肢体缺失或折断而行动不便。" },
            { "Staying underwater.", "停留在水下。" },

            // —— 附着 / 束缚 ——
            { "Has been latched onto.", "已被附着。" },
            { "Has been latched onto by ", "已被附着于" },
            { "Recruited by another creature via proselytization.", "被另一生物通过劝诱招募。" },
            { "Incommunicado", "无法通讯" },

            // —— 机器 / 载具 / 锈蚀 ——
            { "Doesn't function properly.", "无法正常运作。" },
            { "Doesn't function properly and can't be equipped.", "无法正常运作，且无法装备。" },
            { "Will erode into dust if rusted again.", "若再次锈蚀将化为尘埃。" },
            { "Doesn't know how to rejoin leader.", "不知道如何回到首领身边。" },
            { "In the process of mutating.", "正在突变。" },
            { "Has grown burrowing claws.", "已长出掘地爪。" },

            // —— 突变 / 能力 / 冥想 ——
            { "Activated mental mutations cool down twice as quickly.", "已激活的精神突变冷却速度加倍。" },
            { "Lunge and Swipe have no cooldown.", "突刺与横扫无冷却。" },
            { "Slam has no cooldown.", "猛击无冷却。" },
            { "100% chance to daze with cudgels.", "用棍棒时有 100% 概率造成眩晕。" },
            { "Confuses nearby hostile creatures per Confusion 7.", "按困惑 7 的效果困惑附近的敌对生物。" },
            { "Will fall asleep soon.", "很快会睡着。" },
            { "Will fall in love with the first thing examined.", "会爱上检视的第一件东西。" },
            { "Easier to hit with bows and rifles.", "更容易被弓与步枪命中。" },
            { "Easier to hit with bows and rifles wielded by ", "更容易被持弓与步枪者命中：" },
            { "This creature is unresponsive.", "该生物没有反应。" },
            { "This creature performs non-movement actions ", "该生物会执行非移动动作" },

            // ══════════════════════════════════════════════════════════════
            // 2.16.64 补完：覆盖率自证后剩下的 **9 种行**
            //   工具 `_tools\_verify_effects_coverage.py`：
            //     88 条字面量 → ① 整串直接命中 54 · ② 逐行自动拼 **28** · ③ 还缺 **9 种行**
            //   ⇒ 本段补的就是那 9 种。
            //   ★ 其中「昼夜」那组必须**逐行**补（整串带 `\n`，逐行查对不上）
            //     且**昼与夜都要**，否则 `LookupLinewise` 的「有一行没译文就整体不动」
            //     会让我们连能翻的那几行也一起放弃。
            // ══════════════════════════════════════════════════════════════
            { "(dense) -60% Quickness", "（极密）-60% 迅捷" },
            { "(light) -30% Quickness", "（稀薄）-30% 迅捷" },
            { "+25% Move Speed at night and underground.", "夜间与地下移动速度 +25%。" },
            { "+40% Move Speed at night and underground.", "夜间与地下移动速度 +40%。" },
            { "+3 Agility at night and underground.", "夜间与地下敏捷 +3。" },
            { "+4 Agility at night and underground.", "夜间与地下敏捷 +4。" },
            { "-20% Move Speed in the daylight.", "日光下移动速度 -20%。" },
            { "-3 Agility in the daylight.", "日光下敏捷 -3。" },
        };

        /// <summary>
        /// 先整串查；未命中则**逐行**查，全部行都有译文才拼回去（否则返回 null）。
        /// ★ 只在「每一行都命中」时改写 ⇒ 不会产出半中半英的残句。
        /// </summary>
        internal static string LookupLinewise(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length < 2) return null;          // 单行交给整串查表
            var sb = new StringBuilder();
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) { if (i > 0) sb.Append('\n'); continue; }
                // ★ 2.16.64：`ln.Trim()` 也试 —— 有些字面量行**带尾随空白**
                //   （实测 `"Can't move or attack.\n"` / `"Can't take actions.\n"`），
                //   原实现只试 `ln` 与 `ln.TrimEnd()` ⇒ 这类行永远落空。
                string t = ln.TrimEnd();
                string zh;
                if (L.TryGetValue(ln, out zh)) { any = true; }
                else if (L.TryGetValue(t, out zh)) { any = true; }
                else if (L.TryGetValue(ln.Trim(), out zh)) { any = true; }
                else return null;                        // 有一行没译文 ⇒ 整体不动
                if (i > 0) sb.Append('\n');
                sb.Append(zh);
            }
            return any ? sb.ToString() : null;
        }

        /// <summary>整串查表；命中返回值，未命中返回 null（调用方原样放行）。</summary>
        internal static string Lookup(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string v;
            if (L.TryGetValue(s, out v)) return v;
            // 容错：CRLF / 行尾空白差异
            string t = s.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
            if (L.TryGetValue(t, out v)) return v;
            return null;
        }
    }

    /// <summary>给 `Effect.GetDetails` 及其全部覆盖挂 Postfix。</summary>
    internal static class ZhsEffectDetailsPatch
    {
        private static bool _applied;
        private const string OffFile = "_zhs_effectdetails_off.txt";

        /// <summary>
        /// Postfix：`__result` 命中译表就替换。
        /// ⚠ 签名用 `ref string __result` —— 目标返回 `string`，合法。
        /// </summary>
        private static void Postfix(ref string __result)
        {
            try
            {
                // ① 整串精确匹配
                string zh = ZhsEffectDetails.Lookup(__result);
                // ② 未命中 ⇒ 逐行匹配（覆盖 Sitting 那种多变体多行组合）
                if (zh == null) zh = ZhsEffectDetails.LookupLinewise(__result);
                if (zh == null) return;
                if (zh == __result) return;
                __result = zh;
                ZhsEffectDetails.Hits++;
                if (ZhsEffectDetails.Hits <= 8)
                {
                    UnityEngine.Debug.Log(ZhsEffectDetails.Tag
                        + "替换 #" + ZhsEffectDetails.Hits + "："
                        + zh.Replace("\n", "⏎"));
                }
            }
            catch { }
        }

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                // 开关文件（在任一候选目录里存在即停用）
                try
                {
                    foreach (string d in ZhsFileUtil.Dirs())
                    {
                        if (System.IO.File.Exists(System.IO.Path.Combine(d, OffFile)))
                        {
                            UnityEngine.Debug.Log(ZhsEffectDetails.Tag + "开关文件存在，已停用");
                            return;
                        }
                    }
                }
                catch { }

                Type baseT = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        baseT = asm.GetType("XRL.World.Effect", false);
                        if (baseT != null) break;
                    }
                    catch { }
                }
                if (baseT == null)
                {
                    UnityEngine.Debug.LogError(ZhsEffectDetails.Tag + "找不到 XRL.World.Effect");
                    return;
                }

                MethodInfo post = typeof(ZhsEffectDetailsPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.effectdetails");
                int n = 0;
                var asmOfBase = baseT.Assembly;
                Type[] all;
                try { all = asmOfBase.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    var list = new List<Type>();
                    foreach (Type t in e.Types) if (t != null) list.Add(t);
                    all = list.ToArray();
                }

                foreach (Type t in all)
                {
                    try
                    {
                        if (t == null || t.IsAbstract && t != baseT) { }
                        bool derived = baseT.IsAssignableFrom(t);
                        if (!derived) continue;

                        // 只给「自己声明了 GetDetails」的类型挂（基类也挂一次）
                        MethodInfo m = t.GetMethod("GetDetails",
                            BindingFlags.Public | BindingFlags.Instance,
                            null, Type.EmptyTypes, null);
                        if (m == null) continue;
                        if (m.DeclaringType != t) continue;      // 不是本类声明 ⇒ 跳过（避免重复挂）
                        if (m.ReturnType != typeof(string)) continue;

                        h.Patch(m, postfix: new HarmonyMethod(post));
                        n++;
                    }
                    catch { }
                }

                UnityEngine.Debug.Log(ZhsEffectDetails.Tag + "已挂 " + n
                    + " 个 GetDetails 出口（译表 " + ZhsEffectDetails.L.Count + " 条）");

                // ★★ 2.16.61：**机械枚举**全部硬编码字面量（不靠猜）
                //   做法：直接读 `GetDetails` 的 **IL 字节码**，取 `ldstr` 操作数。
                //   好处：**不执行任何游戏代码** ⇒ 不需要 Object、不会抛异常、零副作用。
                //   产出：`_zhs_effectdetails_dump.txt` —— 每个未收录的英文串一行。
                DumpLiterals(all, baseT);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(ZhsEffectDetails.Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// 扫全部 `GetDetails` 覆盖的 IL，抽出 `ldstr` 字面量，写成 dump 文件。
        /// 仅诊断用；`_zhs_effectdetails_nodump.txt` 可停用。
        /// </summary>
        private static void DumpLiterals(Type[] all, Type baseT)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int total = 0, missing = 0;
                sb.Append("# GetDetails IL 字面量 dump（每行：<类型> | <是否已收录> | <串>）\n");
                foreach (Type t in all)
                {
                    if (t == null || !baseT.IsAssignableFrom(t)) continue;
                    MethodInfo m = t.GetMethod("GetDetails",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (m == null || m.DeclaringType != t) continue;
                    MethodBody body;
                    try { body = m.GetMethodBody(); }
                    catch { continue; }
                    if (body == null) continue;
                    byte[] il;
                    try { il = body.GetILAsByteArray(); }
                    catch { continue; }
                    if (il == null) continue;

                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        // 0x72 = ldstr <token:4>
                        if (il[i] != 0x72) continue;
                        int tok = BitConverter.ToInt32(il, i + 1);
                        string s;
                        try { s = m.Module.ResolveString(tok); }
                        catch { continue; }
                        if (string.IsNullOrEmpty(s)) continue;
                        // 只看「像说明文本」的：含字母、有空格、不是纯键名
                        bool looksText = s.Length >= 8
                            && s.IndexOf(' ') >= 0
                            && System.Text.RegularExpressions.Regex.IsMatch(s, "[A-Za-z]{3}");
                        if (!looksText) continue;
                        total++;
                        bool have = ZhsEffectDetails.Lookup(s) != null
                                    || ZhsEffectDetails.LookupLinewise(s) != null;
                        if (!have) missing++;
                        sb.Append(t.Name).Append(" | ")
                          .Append(have ? "有" : "缺").Append(" | ")
                          .Append(s.Replace("\r", "").Replace("\n", "\\n"))
                          .Append('\n');
                    }
                }

                // ★★ 2.16.62：**先打日志**（不再依赖找得到目录）。
                //   旧版 `if (modDir == null) return;` 把整件事静默吞掉 ——
                //   实测 `FindModDir()` 恒返回 null（见下方注释），于是 dump 从来没跑。
                UnityEngine.Debug.Log(ZhsEffectDetails.Tag + "IL 字面量 dump：共 "
                    + total + " 条，其中**未收录 " + missing + " 条**");

                // ★ 逐条打**未收录**的（`Debug.Log` 会进 Player.log，我们一定能读到）。
                //   限 400 条防刷屏。
                int shown = 0;
                foreach (string line in sb.ToString().Split('\n'))
                {
                    if (line.IndexOf(" | 缺 | ") < 0) continue;
                    UnityEngine.Debug.Log(ZhsEffectDetails.Tag + "MISS " + line);
                    if (++shown >= 400) break;
                }
                UnityEngine.Debug.Log(ZhsEffectDetails.Tag + "MISS 已打印 " + shown + " 条");

                // ★ 再尝试写文件（共用工具：多候选目录 + 成败都留痕）
                ZhsFileUtil.Write(ZhsEffectDetails.Tag, "_zhs_effectdetails_dump.txt",
                                  sb.ToString());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(ZhsEffectDetails.Tag + "dump 失败：" + e);
            }
        }

        /// <summary>
        /// ★★ 2.16.62 踩坑记录（保留为教训）：
        ///
        /// 原实现用 `Assembly.GetExecutingAssembly().Location` 往上找 `manifest.json`
        /// —— **本项目根本没有 DLL**（游戏运行时从 `Scripts\*.cs` 现场编译，
        /// 产物在 Unity 临时目录）⇒ 父级链上永远没有 `manifest.json` ⇒ **恒返回 null**。
        /// 后果：`DumpLiterals` 在**打日志之前**就 `if (modDir == null) return;`
        /// ⇒ 整个功能**静默失效**（既无成功日志也无失败日志）。
        ///
        /// 同族问题：`ZhsTombstoneLayout._zhs_tomb_raw.log` 也一直没产出（同一原因）。
        ///
        /// ⇒ 已抽出共用工具 `ZhsFileUtil`（多候选目录 + 成败都留痕）。
        /// </summary>
        // （原 `CandidateDirs()` / `FindModDir()` 已删除，改由 `ZhsFileUtil` 统一提供）
    }
}
