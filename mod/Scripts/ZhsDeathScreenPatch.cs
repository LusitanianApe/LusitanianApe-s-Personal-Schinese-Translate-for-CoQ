// ═══════════════════════════════════════════════════════════════════════════
//  ZhsDeathScreenPatch —— F3：死亡界面那行 `You were killed by ____.`
//
//  【用户截图（453×368，2026-10-16）逐字抄录】
//      你死了。                                  ✅ 已中文（`CheckpointingSystem YouDied` 走串表）
//      ────────────────────────────────
//      You were killed by 步枪炮塔.              ✗ **英文骨架未汉化**（名词「步枪炮塔」已中文）
//      ────────────────────────────────
//      > 查看遗言  ✅ · 从检查点重新载入 ✅ · 让角色退隐 ✅ · 退回主菜单 ✅
//
//  【★ 出口定位（反编译实证，**不是猜**）】
//    死亡界面正文 = `Popup.PickOption("", textBuilder.ToString(), …)` 的 **arg1**，
//    而 `textBuilder` 由 `XRL.CheckpointingSystem::ShowDeathMessage(message, …, SayYouDied)` 拼：
//        ShowDeathMessage（反编译）：
//            `text   = SayYouDied ? Strings._S("CheckpointingSystem YouDied", "You died.") : ""`
//            `textBuilder.Append(text); textBuilder.Compound(message, "\n\n");`
//        `GameObject::Die`（反编译）：
//            `CheckpointingSystem.ShowDeathMessage(Reason ?? XRLCore.Core.Game.DeathReason, …)`
//      ⇒ `message` 就是那行死亡原因句 ✓
//
//    ★★ 那 `message` 从哪来？**两条路，我们原来覆盖的是死的那条**：
//
//      路 A（**死的**）：`XRL.World.Parts.Physics::CheckHP`
//          LastDeathReason = Strings.AssertLocalizationMatch(
//              "You were " + … + "killed by " + LastDamagedBy.an() + ".",
//              _T("DeathReason Killed By", "You were =accidental.if:accidentally :=killed by =object.an=.")…)
//        ⇒ 它**只**在 `LastDeathReason.IsNullOrEmpty()` 时执行 ⇒ 而从伤害死亡时
//          `LastDeathReason` **早已被路 B 填满** ⇒ `_T("DeathReason Killed By")` **永远不执行**。
//        ⇒ 铁证：我方 `Strings.xml:885` **本来就有**中文译文
//              `You were =accidental.if:accidentally :=killed by =object.an=.`
//              ⇒ 「你=accidental.if:失手：被==死于=object.an=之手。」
//          却从不出现在画面上（用户截图是英文）—— 说明它从来不是这条出口。
//
//      路 B（**活的**）：`XRL.World.Parts.Physics::ProcessTakeDamage`
//          `if (E.HasParameter("DeathReason")) { … } else {`
//              `using TextBuilder textBuilder = TextBuilder.Get();`
//              `textBuilder.Append(text2 != null ? "Your " : "You ");`
//              `if (text2 != null) textBuilder.Append(text2).Append(' ');`
//              `else if (!flag2) textBuilder.Append("were ");`
//              `if (flag3 && text2 == null) textBuilder.Append("accidentally ");`
//              `textBuilder.Append(text);`                       // ★ `text` = 伤害类型动词
//              `if (gameObject2 != null && gameObject2 != ParentObject) {`
//                  `if (!value.IsNullOrEmpty()) textBuilder.Append(' ').Append(value);`
//                  `textBuilder.Append(' ').Append(gameObject2.an(…));`   // ← 这里出「步枪炮塔」
//              `}`
//              `textBuilder.Append('.');`
//              `LastDeathReason = textBuilder.ToString();`
//          }
//          `if (IsPlayer()) { The.Game.DeathReason = LastDeathReason; … }`
//      ⇒ 全句 = **纯 C# `Append` 链拼装**（`"You "` / `"were "` / `"accidentally "` / 动词 /
//        `" by "` / `an()` / `"."`）⇒ **字符串表里没有完整句可替换**（§38 同族：
//        不是「没打上」，是「**根本没有可查的键**」）⇒ 必须在出口按句式改写。
//
//    ★ 动词/介词/被动态全部来自 `ProcessTakeDamage` 开头那段 damage-type switch
//      （反编译 **全量枚举**，见下表）：
//        text  = "killed"(默认) / "cooked" / "immolated" / "vaporized" / "frozen to death"
//              / "electrocuted" / "dissolved" / "disintegrated" / "plasma-burned to death"
//              / "lased to death" / "illuminated to death" / "died of poison" / "bled to death"
//              / "died of asphyxiation" / "failed" / "drained to extinction"
//              / "psychically extinguished" / "mentally obliterated" / "pricked to death"
//              / "bitten to death"
//        value = "by"(默认) / "from" / "because of" / "by colliding with"
//        text2 = null(默认) / "metabolism" / "vital essence was"
//        flag2 = true ⇒ **不输出 "were "**（`died of…` / `bled to death` / `failed` /
//                        `drained to extinction` 本身就是过去式谓语）
//        flag3 = true ⇒ 插 "accidentally "
//        flag4 = true ⇒ 换 "You convinced yourself you …"（Illusion 伤害，自伤）
//        flag5 = true ⇒ 末尾加 " via reflected damage"
//
//  【★ 为什么挂 `ProcessTakeDamage` 的 Postfix 而不是 `GameObject::Die`】
//    · `GameObject::Die` 里 `XRLCore.Core.RenderBase()` **先于** `ShowDeathMessage`
//      执行 ⇒ Prefix 太晚（会闪英文），Postfix 更晚 ⇒ 都不行。
//    · `ProcessTakeDamage` 是**唯一**写入 `LastDeathReason` / `The.Game.DeathReason`
//      的活路径 ⇒ 在这里改完，后面**所有**消费者（死屏 / 成就 / 编年史）都拿到中文，
//      且**不需要第二个出口**。
//    · 签名 `public bool ProcessTakeDamage(Event E)`（反编译核实）；
//      `LastDeathReason` / `LastThirdPersonDeathReason` 都是 **public 字段**
//      （`decomp type XRL.World.Parts.Physics`：`public string LastDeathReason = "";`）。
//
//  【★ 幂等】中文串不含汉字以外的英文骨架 ⇒ 第二次进 Postfix 被 `LooksEnglish` 挡下。
//
// ═══════════════════════════════════════════════════════════════════════════
//  ★★★ 2.16.129（F3 追加 · **用户第二次实机反馈：死亡屏仍英文**）
//
//  【现象与自相矛盾的证据】
//     · 用户截图：`You were killed by 咬颚拾荒者.`（主语中文、骨架英文）
//     · `Player.log` 却明明有成功记录：
//         `死亡界面接管：You were killed by 咬颚拾荒者.  =>  你被咬颚拾荒者杀死。`
//    ⇒ 「算得对，但**没送达**」—— 与 F1 的 `__args` 回滚同族，但**根因完全不同**：
//      这次是**时序**：我们改字段时，死亡屏**已经画完了**。
//
//  【★★★ 铁证（同一局 Player.log 的行号顺序，行号即真相）】
//      4549  死亡界面接管：You were bitten to death by 咬颚拾荒者.  =>  你被咬颚拾荒者咬死。
//      4550  死亡界面接管：You were bitten to death by 咬颚拾荒者.  =>  你被咬颚拾荒者咬死。
//      4551  deathCategory: killed          ← ★ 游戏自己打的（`ShowDeathMessage` 第一行）
//      4552  弹窗接管：顺序探针 #1（改写前读 __args[4]）：[查看遗言][从检查点重新载入]…
//      4553  弹窗接管：选项列表 #1 类型 = System.String[]
//      4554  死亡界面接管：You were killed by 咬颚拾荒者.  =>  你被咬颚拾荒者杀死。
//                                            ↑ **我们的 Postfix 晚了 3 行**
//
//  【机制链（全部反编译核实，一条不多）】
//      `Physics::ProcessTakeDamage`
//        L294-367  `TextBuilder` 拼出 `LastDeathReason`（**此时是英文**）
//        L369-373  `if (IsPlayer()) The.Game.DeathReason = LastDeathReason;`   ← 也是英文
//        L580      `stat.Penalty += damage.Amount;`        ← ★ 触发同步 `StatChangeEvent`
//                     → `Physics::HandleEvent(StatChangeEvent)`
//                     → `CheckHP(E.NewValue, E.OldValue, …)`   （`if (!WillCheckHP())` 分支）
//                     → `ParentObject.Die(…, Reason: LastDeathReason, …)`
//                     → `CheckpointingSystem::ShowDeathMessage(Reason ?? Game.DeathReason, …)`
//                     → `Debug.Log("deathCategory: …")` ── 上面 4551 那一行
//                     → `Popup.PickOption("", textBuilder.ToString(), …)` ── 4552/4553
//        （方法返回）→ **我们的 Postfix 才跑** ── 4554
//   ⇒ **死亡屏是在 `ProcessTakeDamage` 内部画出来的**，Postfix 天然晚一步。
//
//  【★ 修法（两条新出口 + 一条保留）】
//    ① `Physics::CheckHP` 的 **Prefix** —— 在 CheckHP 读字段**之前**把
//       `LastDeathReason`/`LastThirdPersonDeathReason` 中文化
//       ⇒ `Die` 拿到的 `Reason` 就是中文 ⇒ 死亡屏 / `The.Game.DeathReason` /
//         `JournalAPI.AddAccomplishment`（编年史那条）**一起**对。
//    ② `CheckpointingSystem::ShowDeathMessage` 的 **Prefix** —— 这是死亡屏的
//       **唯一显示收口**（反编译 + `native-calls.tsv` 实证：全 DLL 只有 **3** 个调用点
//        —— `GameObject::Die` 一处、`GameObject::Destroy` 两处）
//       ⇒ 无论上游是谁产的 message（`ProcessTakeDamage` / 6 个直接
//         `Die(Reason: 字面量)` / `Consumer` / `StoneGaze` / `Cudgel_Slam` …），
//         只要上屏就一定从这里过 ⇒ 覆盖矩阵见 `_tools\F3-死亡消息覆盖矩阵.md`。
//    ③ 原 `ProcessTakeDamage` 的 Postfix **保留**（生产者侧兜底 + 幂等）。
//
//  【★ 为什么以前"覆盖了 20 个 damage-type 分支"却还是漏】
//    上一位只审了 **damage-type switch**（`text`/`value`/`flag2~6` 那 20 个分支），
//    而漏的是**出口时序**：分支覆盖得再全，Postfix 也是在屏之后跑的。
//    同一份审计还漏了**非 damage 路径的死亡句**（`Consumer`/`StoneGaze`/`Cudgel_Slam`
//    等 6+ 处 `Die(Reason: 字面量)`）—— 它们**根本不经过** `ProcessTakeDamage`，
//    规则表再全也覆盖不到 ⇒ 必须挂显示收口。
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsDeathScreenPatch
    {
        private const string Tag = "[QudZhsCookbook] 死亡界面接管：";

        private static bool _applied;
        private static int _hits;

        /// <summary>
        /// 门禁：这条串**还有英文骨架**吗？（true = 要改；false = 已中文化 / 别碰）
        ///
        /// ★★ 这一条**踩过坑，记下来**（第一版写错，自证直接抓到 27 条 FAIL）：
        ///   第一版写的是「**不含任何汉字** ⇒ 才动手」，理由是「幂等」。
        ///   **错**：真实串里的**生物名已经是中文**（用户截图就是 `You were killed by 步枪炮塔.`）
        ///   ⇒ 含汉字 ⇒ 门禁直接放行 ⇒ **整条规则永远不执行**。
        ///   ⇒ 判据改成**看英文骨架特征**，与有没有汉字无关：
        ///       `You ` 开头 / 含 ` by ` / 含 ` from ` / 含 ` because of ` / 含 `ere `
        ///       （`were` —— 中文串里**不可能**出现这个字节序列）
        ///   ⇒ 中文结果「你被步枪炮塔杀死。」不含任何上述特征 ⇒ 第二次进来判 false ⇒ **幂等成立** ✓
        ///   ⇒ 反例「你被步枪炮塔杀死。」也走这条放行 ✓
        /// </summary>
        internal static bool HasEnglishFrame(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("You ", StringComparison.Ordinal)) return true;
            // ★ `Your …`（`text2 != null` 的两支：`Your metabolism failed…` /
            //   `Your vital essence was…`）—— 以及第三人称归一化后的 `Its `/`Their `
            //   （虽然后者会先被换成 `Your `，两条都留着更稳）
            if (s.StartsWith("Your ", StringComparison.Ordinal)) return true;
            if (s.StartsWith("Its ", StringComparison.Ordinal)) return true;
            if (s.StartsWith("Their ", StringComparison.Ordinal)) return true;
            if (s.IndexOf(" by ", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf(" from ", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf(" because of ", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("ere ", StringComparison.Ordinal) >= 0) return true;   // `were`
            return false;
        }

        /// <summary>
        /// 死亡原因句改写规则：**整句替换**。
        ///
        /// 三元组：`Rx` · `Zh`（中文模板，`$n` 对应捕获组） ·
        ///        `VerbSlot1`（**`$1` 是不是动词槽** ⇒ 要不要过 `VERB` 表）。
        ///
        /// ★★ 第三个元素**必须显式写**：不能用「模板里有 `$1`」推断 ——
        ///   ③⑤⑥ 的 `$1` 是**施动者**不是动词，按位置推断会把它们一并要求过动词表
        ///   ⇒ 永远查不到 ⇒ 全部 `continue` ⇒ 落到兜底型产出「你were killed by X。」
        ///   （实测踩过：自证 7 条 FAIL 全出自这一个判据。）
        ///
        /// 写法说明：
        ///   · `You` / `Your` 两态都接（`Your ` 用于 `text2 != null` 那两个变体）；
        ///   · `(?:\s+were)?` 可选 —— `flag2` 族**不输出** `were `；
        ///   · `\s+` 而非单空格：拼接残留证明游戏会留双空格（F5 那批 `用他的 咬`）；
        ///   · 第一人称句（`Your metabolism failed…` / `Your vital essence was…`）
        ///     **不含** `you`，`an()` 出的是**玩家自己的名字** ⇒ 中文省掉施动者。
        /// </summary>
        private static readonly (Regex Rx, string Zh, bool VerbSlot1)[] RULES =
        {
            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2.16.129（F3 追加）：**非 damage-type 路径**的死亡句（定点族）
            //
            //  【为什么必须排在最前】它们比下面 ⑪`^You … by X\.$` / ⑫`^You …\.$`
            //    **更具体**；排在后面会被 ⑪⑫ 抢先吞成夹生句
            //    （实测形态：`你被falling rockscrushed。`）。
            //
            //  【证据】每条 / 每族的反编译出处写在它自己的注释里；
            //    总表与"是否覆盖"判据见 `_tools\F3-死亡消息覆盖矩阵.md`。
            //
            //  ★★ **本族只放"表里没有"的**：无变量的死亡句一律走上面 ⓪ 的
            //    `ZhsHardcodedMsg.L`（整句精确表），**绝不两处各写一份**
            //    —— 本项目刚吃过这个亏：`You were resorbed into the Mass Mind.`
            //      在两处各有一份中文（「你被心智之海吸收了。」vs 表里的
            //      「你被群体心智重新吸收了。」）⇒ 谁先命中就看代码顺序，读者无从判断。
            //    已在**表**里（本处**故意不再写规则**）的 8 条：
            //      `You were cancelled from existence.` · `You were reduced to dust.`
            //      `You were resorbed into the Mass Mind.`
            //      `You experienced a lack of existential support.`
            //      `You were crushed by falling rocks.`
            //      `You were entombed in the burial chamber of Resheph, the Last Sultan.`
            //      `You cooled into a block of shale.` · `You had a dream, …`
            //      `Your mind winks/winked out of existence.`
            // ═══════════════════════════════════════════════════════════════════
            // ── `Physics::CheckThresholds`：`LastDeathReason = "You were vaporized."` ──
            (new Regex(@"^You were vaporized\.$"), "你被蒸发殆尽。", false),
            // ── `FallsApart::TurnTick`：`Die(…, "You fell apart.")` ──
            (new Regex(@"^You fell apart\.$"), "你四分五裂了。", false),
            // ── `Gas::Dissipate`：`Die(…, "You dissipated.")` ──
            (new Regex(@"^You dissipated\.$"), "你消散了。", false),
            // ── `LiquidVolume::HandleEvent`（Frozen）：`Die(…, "You froze.")` ──
            (new Regex(@"^You froze\.$"), "你冻僵了。", false),
            // ── `LootOnStep::SteppedOn`：`Die(…, "You were stepped on.")` ──
            (new Regex(@"^You were stepped on\.$"), "你被踩死了。", false),
            // ── `GameObject::Explode`（反编译 L33 / L37 两个 `Die(Owner, "laws of physics", …)`）：
            //      L33 `"You were crushed under the weight of a thousand suns."`（**在表里**）
            //      L37 `"You exploded."`（**表里没有** ⇒ 本族补上）
            (new Regex(@"^You exploded\.$"), "你爆炸了。", false),
            // ── `Effects.CardiacArrest`：`"Your " + (heart|hearts) + " stopped."`（两态）──
            (new Regex(@"^Your hearts? stopped\.$"), "你的心脏停止了跳动。", false),
            // ── `GameObject::Destroy`（玩家 + Dominated / 普通 Destroy 两处**直接**
            //    调 `ShowDeathMessage` 的字面量）──
            (new Regex(@"^You die! \(good job\)$"), "你死了！（干得漂亮）", false),
            // ── `GameObject::Destroy`：`The.Game.DeathReason = Reason ?? ("You were " +
            //    (Obliterate ? "obliterated" : "destroyed"))` —— **无句号**（只进日志字段）──
            (new Regex(@"^You were (?:destroyed|obliterated)\.$"), "你被摧毁了。", false),
            (new Regex(@"^You were (?:destroyed|obliterated)$"), "你被摧毁了", false),
            // ── `Consumer::Consume`：`"You were consumed whole by " + ParentObject.an() + "."` ──
            (new Regex(@"^You were consumed whole by (.+?)\.$"), "你被$1整个吞下。", false),
            // ── `Mutation.StoneGaze::FireLine`：
            //    `"You were turned to stone by the gaze of " + ParentObject.an() + "."` ──
            (new Regex(@"^You were turned to stone by the gaze of (.+?)\.$"),
             "你被$1的凝视变成了石头。", false),
            // ── `Skill.Axe_Decapitate::Decapitate`：
            //    `"You were " + (Head ? "relieved of your vital anatomy" : "decapitated") + " by " + X + "."` ──
            (new Regex(@"^You were (?:relieved of your vital anatomy|decapitated) by (.+?)\.$"),
             "你被$1斩首。", false),
            // ── `Skill.Cudgel_Slam::Cast`：4 个拼接分支（**顺序：具体在前**，
            //    否则 `slammed into …` 会被 `slammed by` 吞掉）──
            (new Regex(@"^You were slammed into two walls by (.+?)\.$"),
             "你被$1撞得在两堵墙之间来回弹。", false),
            (new Regex(@"^You were slammed into a wall by (.+?)\.$"),
             "你被$1撞到墙上。", false),
            (new Regex(@"^You were slammed into (\d+) walls by (.+?)\.$"),
             "你被$2撞得在$1堵墙之间来回弹。", false),
            (new Regex(@"^You were slammed by (.+?)\.$"),
             "你被$1撞飞。", false),

            // ── flag2 族：**不输出 "were "** 的过去式谓语 ──
            //    "You died of poison from a 步枪炮塔."
            (new Regex(@"^You died of poison(?:\s+from\s+(.+?))?\.$"),
             "你中毒而死。", false),
            (new Regex(@"^You died of asphyxiation(?:\s+from\s+(.+?))?\.$"),
             "你窒息而死。", false),
            (new Regex(@"^You bled to death(?: because of (.+?))?\.$"),
             "你失血而死。", false),
            (new Regex(@"^Your metabolism failed(?:\s+from\s+(.+?))?\.$"),
             "你的新陈代谢衰竭了。", false),
            (new Regex(@"^Your vital essence was drained to extinction(?:\s+by\s+(.+?))?\.$"),
             "你的生命精粹被吸干了。", false),

            // ── flag4：幻象伤害，自伤（`text2 == null ? "you" : "your"`）──
            //   ★ 只留 `your …` 与「无 `by` 子句」两支的规则；
            //     **`you … by X` 那支由 `Translate` 开头的归一化处理**（见 `NORM4`）：
            //     第一版让它一条正则吃到底 ⇒ `$1`=`killed by 幻影` ⇒ 「你killed by 幻影。」
            //     （中英夹生，自证 FAIL 抓到）；改成递归又出「你killed幻影。」
            //     ⇒ 最后选**归一化成内层第二人称句**再套同一批规则，最省心也最好验证。
            (new Regex(@"^You convinced yourself your\s+(.+?)\.$"),
             "你说服了你自己，你的$1。", false),

            // ═══════════════════════════════════════════════════════════════════
            //  ── 默认 / 环境伤害族（输出 "were "）──
            //
            //  ★★【规则顺序 = 语义】`killed` 这一族**必须**按「先具体、后通用」排。
            //     实测踩到三次（全靠 `-- ftext` / `-- probe1` 自证点数出来）：
            //       ① 通用型 ⑪ 先跑 ⇒ 把 `killed by X` 劈成 `$1`=`killed`（不在动词表）
            //          ⇒ 落到兜底型 ⇒ 「你were killed by X。」
            //       ② 通用意外型（⑨）先跑 ⇒ 把 `killed by X` 整段当动词
            //          ⇒ `killed by colliding with 墙` 被吞成「你被colliding with 墙杀死。」✗
            //       ③ **带空白捕获组的规则若标了 `VerbSlot1` 会「整条报废」**：
            //          `^…killed by colliding with (.+?)\.$` 在别的串上 `$1` 为空
            //          ⇒ 动词表查空串 ⇒ 报错放行 ⇒ 连它**本来能管的**串也一起丢了
            //     ⇒ 所以：**专用规则排在通用规则之前**，且**专用规则的捕获组一律不标
            //        `VerbSlot1`**（它们的 `$1` 是值不是动词槽）。
            // ═══════════════════════════════════════════════════════════════════
            //  ⑤ `killed by colliding with X`（`value` 被 Collision 分支改过）
            //     ★ 必须排在 ⑥ 之前 —— 否则 ⑥ 的 `(.+?)` 会吞掉 `colliding with 墙`
            (new Regex(@"^You(?:\s+were)?\s+killed by colliding with (.+?)\.$"),
             "你撞上$1而死。", false),
            //  ⑥ `You were killed by X.` —— **用户截图那一句**（`$1` = 施动者）
            (new Regex(@"^You(?:\s+were)?\s+killed by (.+?)\.$"),
             "你被$1杀死。", false),
            //  ⑦ 意外 + killed（`flag3` 与默认动词的组合）
            (new Regex(@"^You(?:\s+were)?\s+accidentally\s+killed by (.+?)\.$"),
             "你意外被$1杀死。", false),
            //  ⑧ 光杆 `You were killed.`（无施动者）
            (new Regex(@"^You(?:\s+were)?\s+killed\.$"),
             "你死了。", false),
            //  ⑨ 意外（非 killed）：「你意外<动词>。」；`$1` **是**动词槽
            //     ★ 它必须**排在 ⑤⑥⑦ 之后** —— 否则会把 `killed by X` 整段当动词吞掉。
            (new Regex(@"^You(?:\s+were)?\s+accidentally\s+(.+?)\.$"),
             "你意外$1。", true),
            //  ⑩ `via reflected damage` 尾缀（`flag5`）—— 必须排在 ⑪ 之前，
            //     否则尾缀会被 ⑪ 当**人名**吞进 `$2`
            //     ⚠ 实测这条**从未命中**（⑪ 抢先把尾缀吞进 `$2`，而 ⑩ 要求尾缀紧跟
            //       `by X` 之后 —— 两者形态无法用一条正则统一）。**真正的处理在
            //       `Translate` 开头的 `TAIL5` 归一化**：先剥尾缀 → 套规则 → 再接回中文括注。
            //       这条规则**保留**作双保险（万一哪天 ⑪ 的表里出现了带尾缀的动词）。
            (new Regex(@"^You(?:\s+were)?\s+(.+?)\s+by\s+(.+?)\s+via reflected damage\.$"),
             "你被$2$1（反弹伤害）。", true),
            //  ⑪ 通用被动态：`$1` **是**动词槽（必须在动词表里），`$2` = 施动者
            //     `You were cooked by 岩浆.` ⇒ `你被岩浆烤死。`
            (new Regex(@"^You(?:\s+were)?\s+(.+?)\s+by\s+(.+?)\.$"),
             "你被$2$1。", true),
            //  ⑫ 兜底：`You were vaporized.` 之类无施动者形态；`$1` **是**动词槽
            (new Regex(@"^You(?:\s+were)?\s+(.+?)\.$"),
             "你$1。", true),
        };

        /// <summary>
        /// 动词 → 中文。**必须与英文原词逐一对应**；未命中就 `continue` 给下一条（不做半译）。
        ///
        /// ★ `killed` 这一族**刻意不收**：`killed by X` / `killed.` 由上面 ③④⑤⑥ 四条
        ///   **专用规则**处理（它们能正确产出「你被X杀死。」「你死了。」）。
        ///   若把 `killed` 放进表里，通用型 ⑧ 就会抢先生成丢动词的形态。
        /// </summary>
        private static readonly Dictionary<string, string> VERB =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "cooked",                   "烤死" },
            { "immolated",                "烧死" },
            { "vaporized",                "蒸发" },
            { "frozen to death",          "冻死" },
            { "electrocuted",             "电死" },
            { "dissolved",                "溶解" },
            { "disintegrated",            "分解" },
            { "plasma-burned to death",   "等离子烧死" },
            { "lased to death",           "激光烧死" },
            { "illuminated to death",     "强光烧死" },
            { "pricked to death",         "刺死" },
            { "bitten to death",          "咬死" },
            { "psychically extinguished", "精神抹杀" },
            { "mentally obliterated",     "精神摧毁" },
        };

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type tp = AccessTools.TypeByName("XRL.World.Parts.Physics");
                if (tp == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Parts.Physics");
                    return;
                }
                MethodInfo m = null;
                foreach (MethodInfo mi in tp.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name != "ProcessTakeDamage") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 1) { m = mi; break; }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 ProcessTakeDamage(Event)");
                    return;
                }
                MethodInfo post = typeof(ZhsDeathScreenPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.deathscreen");
                h.Patch(m, postfix: new HarmonyMethod(post));

                UnityEngine.Debug.Log(Tag + "已挂出口 ①（Physics::ProcessTakeDamage 的 Postfix，"
                    + RULES.Length + " 条句式 + " + VERB.Count + " 个动词）");

                // ═══════════════════════════════════════════════════════════════
                //  ★★★ 2.16.129（F3 追加）：出口 ②③ —— 见文件头「死亡屏为什么晚了一步」
                //
                //  ② `Physics::CheckHP` 的 **Prefix**
                //     判据：`CheckHP` 是本方法**唯一**被 `Die` 读字段的入口
                //     （反编译 `CheckHP`：`ParentObject.Die(LastDamagedBy, null,
                //       LastDeathReason, …)`）；Prefix 早于方法体 ⇒ 早于 `Die` ⇒ 早于死亡屏。
                //  ③ `CheckpointingSystem::ShowDeathMessage` 的 **Prefix**
                //     判据：**死亡屏唯一显示收口**。`native-calls.tsv` 实证全 DLL 只有
                //     3 个调用点（`GameObject::Die` 1 个 + `GameObject::Destroy` 2 个）
                //     ⇒ 覆盖"不经 `ProcessTakeDamage`"的那 6+ 条死亡路径。
                // ═══════════════════════════════════════════════════════════════
                MountCheckHp(h);
                MountShowDeathMessage(h);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// 出口 ②：`Physics::CheckHP` 的 Prefix —— 在 CheckHP **读字段之前**改中文。
        /// ★ 这一步是「死亡屏时序」的正解：`Die` 拿到的 `Reason` 就已经是中文，
        ///   于是死亡屏正文 / `The.Game.DeathReason` / 编年史 `AddAccomplishment` 一起对。
        /// </summary>
        private static void MountCheckHp(Harmony h)
        {
            try
            {
                Type tp = AccessTools.TypeByName("XRL.World.Parts.Physics");
                MethodInfo m = null;
                if (tp != null)
                {
                    foreach (MethodInfo mi in tp.GetMethods(BindingFlags.Public
                                 | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (mi.Name != "CheckHP") continue;
                        m = mi; break;
                    }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Physics::CheckHP（出口 ② 未挂）");
                    return;
                }
                MethodInfo pre = typeof(ZhsDeathScreenPatch).GetMethod(
                    "PrefixCheckHP", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) { UnityEngine.Debug.LogError(Tag + "找不到 PrefixCheckHP"); return; }
                h.Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂出口 ②（Physics::CheckHP 的 Prefix，"
                    + "在 Die 之前把两个字段中文化）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "出口 ② 挂载失败：" + e);
            }
        }

        /// <summary>
        /// 出口 ③：`CheckpointingSystem::ShowDeathMessage` 的 Prefix。
        /// ★ 死亡屏的**唯一显示收口**（全 DLL 3 个调用点）⇒ 这一条把所有产者一次盖住。
        /// </summary>
        private static void MountShowDeathMessage(Harmony h)
        {
            try
            {
                Type tp = AccessTools.TypeByName("XRL.CheckpointingSystem");
                MethodInfo m = null;
                if (tp != null)
                {
                    foreach (MethodInfo mi in tp.GetMethods(BindingFlags.Public
                                 | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (mi.Name != "ShowDeathMessage") continue;
                        m = mi; break;
                    }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 CheckpointingSystem::ShowDeathMessage（出口 ③ 未挂）");
                    return;
                }
                MethodInfo pre = typeof(ZhsDeathScreenPatch).GetMethod(
                    "PrefixShowDeathMessage", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) { UnityEngine.Debug.LogError(Tag + "找不到 PrefixShowDeathMessage"); return; }
                h.Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂出口 ③（CheckpointingSystem::ShowDeathMessage 的 Prefix，"
                    + "死亡屏显示收口 · arg0 改写）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "出口 ③ 挂载失败：" + e);
            }
        }

        /// <summary>
        /// 出口 ② 的 Prefix：把两条死亡原因字段就地中文化。
        /// ★ 与 Postfix 共用 `Fix` / `FixThird` —— **同一份规则、同一个收尾函数**
        ///   （§36：不许两处口径漂移）；两条路径都**幂等**（中文串过不了 `HasEnglishFrame`）。
        /// </summary>
        private static void PrefixCheckHP(object __instance)
        {
            try
            {
                if (__instance == null) return;
                Type tp = __instance.GetType();
                string zh = Fix(tp, __instance, "LastDeathReason");
                FixThird(tp, __instance, zh);
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "CheckHP Prefix 失败：" + e); } catch { }
            }
        }

        /// <summary>出口 ③ 的日志计数（只打前几条，避免刷屏）。</summary>
        private static int _screenHits;

        /// <summary>
        /// 出口 ③ 的 Prefix：改写 `ShowDeathMessage` 的 **第 1 个参数**（屏正文）。
        ///
        /// ★ 用 `object[] __args` 而不是 `ref string __0` —— 本项目 §49 的铁律
        ///   （`ref` 型前缀出过「算了但没送达」的大坑）。
        /// ★ **就地读写同一个槽**（不做入口快照），且改完**从参数数组里再读一遍**打日志
        ///   —— 只复读自己的局部引用 = 自证 = 没证。
        /// </summary>
        private static void PrefixShowDeathMessage(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;
                string msg = __args[0] as string;
                if (string.IsNullOrEmpty(msg)) return;
                string zh = Translate(msg);
                if (zh == null || zh == msg) return;
                __args[0] = zh;
                string back = __args[0] as string;          // ★ 从参数数组回读
                if (back == zh && _screenHits < 8)
                {
                    _screenHits++;
                    try
                    {
                        UnityEngine.Debug.Log(Tag + "（死亡屏收口）" + msg + "  =>  " + back);
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "ShowDeathMessage Prefix 失败：" + e); } catch { }
            }
        }

        /// <summary>
        /// `ProcessTakeDamage` 的 Postfix：把两条死亡原因字段里的英文骨架换成中文。
        /// ★ 两条都改 —— `LastThirdPersonDeathReason` 会进**成就 / 编年史**（第三人称叙述）。
        /// </summary>
        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                Type tp = __instance.GetType();
                // ★ 第二人称（死屏正文 / `The.Game.DeathReason`）
                string zh = Fix(tp, __instance, "LastDeathReason");
                // ★ 第三人称（成就 / 编年史叙述）。`GameText.RoughConvertSecondPersonToThirdPerson`
                //   产的是 `It were killed by a 咬颚兽.`（主语 `It`/`They`，谓词仍是 2nd-person 形）
                //   ⇒ 先**归一化**回 `You …` 再套同一批规则，然后把中文主语换成「它」。
                //   这样两条字段共用**一套句式规则**，不会各自漂移。
                FixThird(tp, __instance, zh);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Postfix 失败：" + e);
            }
        }

        /// <summary>第三人称字段：归一化主语 → 套同一批规则 → 中文主语换「它」。</summary>
        private static void FixThird(Type tp, object inst, string secondPersonZh)
        {
            string en = ReadMember(tp, inst, "LastThirdPersonDeathReason") as string;
            if (string.IsNullOrEmpty(en)) return;
            string zh = TranslateThird(en);
            // 归一化没命中 ⇒ 退一步：若第二人称译文已在别处算好，直接复用（避免两套规则漂移）
            if (zh == null && !string.IsNullOrEmpty(secondPersonZh))
                zh = TranslateThird(secondPersonZh.StartsWith("你", StringComparison.Ordinal)
                    ? "It" + secondPersonZh.Substring(1) : secondPersonZh);
            if (zh == null || zh == en) return;
            WriteMember(tp, inst, "LastThirdPersonDeathReason", zh);
        }

        /// <summary>
        /// 第三人称形态 → 中文（**供自证直接调用**）。
        /// `GameText.RoughConvertSecondPersonToThirdPerson` 产的是
        ///   `It were killed by a 咬颚兽.`（**主语换成 It/They，谓词仍是 2nd-person 形**）
        /// ⇒ 归一化回 `You were …` 套同一批规则，再把中文主语换成「它」。
        /// </summary>
        internal static string TranslateThird(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            // ① 主语 `It` / `They` ⇒ `You`（`GameText` 只换主语，谓词仍是 2nd-person 形）
            //   `They were cooked by 岩浆.` ⇒ `You were cooked by 岩浆.`
            string t = s;
            if (t.StartsWith("They ", StringComparison.Ordinal)) t = "You" + t.Substring(4);
            else if (t.StartsWith("It ", StringComparison.Ordinal)) t = "You" + t.Substring(2);
            string zh = Translate(t);
            // ② `Its metabolism failed.` / `Their vital essence was …` —— **没有 `by` 子句**
            //    的变体，`Translate` 不认（它只收 `You…` / `Your…` 开头）
            //    ⇒ 归一化 `Its `/`Their ` ⇒ `Your ` 再试一次。
            if (zh == null)
            {
                string u = t;
                if (u.StartsWith("Its ", StringComparison.Ordinal)) u = "Your" + u.Substring(3);
                else if (u.StartsWith("Their ", StringComparison.Ordinal)) u = "Your" + u.Substring(5);
                if (u != t) zh = Translate(u);
            }
            // ③ 第二人称译文若已在别处算好，直接复用（避免两套规则漂移）
            if (zh == null && !string.IsNullOrEmpty(_lastSecondPersonZh))
                zh = _lastSecondPersonZh;
            if (zh == null) return null;
            // ④ 中文主语：「你…」⇒「它…」（与旁白口径一致）
            if (zh.StartsWith("你", StringComparison.Ordinal)) zh = "它" + zh.Substring(1);
            return zh;
        }

        /// <summary>
        /// 最近一次算出的**第二人称**译文（由 `Fix` 在改字段时记下）。
        /// 用途：第三人称字段归一化失败时**复用同一份中文**，保证两条字段口径一致。
        /// </summary>
        private static string _lastSecondPersonZh;

        /// <summary>改一个字段；返回**算出来的中文**（未命中返回 null）。</summary>
        private static string Fix(Type tp, object inst, string member)
        {
            string en = ReadMember(tp, inst, member) as string;
            if (string.IsNullOrEmpty(en)) return null;
            string zh = Translate(en);
            if (zh == null || zh == en) return null;
            WriteMember(tp, inst, member, zh);
            if (member != "LastDeathReason") return zh;
            // ★★ 2.16.129：把第二人称译文**记下来** —— `TranslateThird` 的 ③ 号兜底
            //   （`_lastSecondPersonZh`）原来**从来没人赋值** ⇒ 那条分支恒假（死代码）。
            //   本行让它变成活的；副作用为零（只是缓存最近一次的译文）。
            _lastSecondPersonZh = zh;
            // ★ 日志自己也要包 try（本项目第三次踩坑：`Debug.Log` 在体外自证时抛
            //   SecurityException，被外层 catch 吞掉 ⇒ 连算好的译文一起丢）
            if (_hits > 40) return zh;
            _hits++;
            try { UnityEngine.Debug.Log(Tag + en + "  =>  " + zh); } catch { }
            return zh;
        }

        /// <summary>英文死亡原因句 → 中文；**未命中返回 null**（调用方保留原文，绝不半译）。</summary>
        internal static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (!HasEnglishFrame(s)) return null;         // 幂等门禁（见 HasEnglishFrame 注释）
            string t = s.Trim();

            // ═══════════════════════════════════════════════════════════════════
            //  ⓪ **整句精确表**兜底（复用 `ZhsHardcodedMsg.L`，**只读**）
            //
            //  【为什么复用别人那张表】那些**无变量**的死亡句中文早就有：
            //      `You were reduced to dust.` ⇒ 你化作了尘埃。
            //      `You cooled into a block of shale.` ⇒ 你冷却成了一块页岩。
            //      `You had a dream, …` ⇒ …
            //    但它们原来只挂在 `MessageQueue.AddPlayerMessage` 出口
            //    （`ZhsHardcodedMsg` 的落点，见该文件头），而**死亡屏**走
            //    `CheckpointingSystem.ShowDeathMessage` ⇒ **表在、出口不通** ⇒ 屏上仍英文。
            //
            //  【口径】只做**整串精确匹配**（`Dictionary.TryGetValue`），不做任何模糊
            //    ⇒ 零误伤；未命中原样往下走规则链。表里带 `=槽=` 的条目永远不可能
            //      与本处的整句相等 ⇒ 天然安全。
            // ═══════════════════════════════════════════════════════════════════
            if (ZhsHardcodedMsg.L.TryGetValue(t, out string lit)
                && !string.IsNullOrEmpty(lit) && lit != t)
                return lit;

            // ═══════════════════════════════════════════════════════════════════
            //  ① `flag4`（幻象自伤）归一化
            // ═══════════════════════════════════════════════════════════════════
            //   原句：`You convinced yourself you were killed by 幻影.`
            //   中文要的是「你说服了你自己，你**被幻影杀死**。」—— 内层是一个**完整句**。
            //   ★ 为什么要归一化而不是在规则里塞两个捕获组：
            //     第一版一条正则吃到底 ⇒ `$1`=`killed by 幻影` ⇒「你killed by 幻影。」
            //     第二版切开 `$1`=`killed`/`$2`=`幻影` + 递归 ⇒「你killed幻影。」（内层句
            //       被削主语时与动词表打架）
            //     ⇒ 第三版：**先把外层壳剥掉，把内层当独立句翻**，再把结果拼进外层中文。
            //       规则数不变、无递归、可单测。
            Match m4 = NORM4.Match(t);
            if (m4.Success)
            {
                string inner = Translate("You " + m4.Groups[1].Value.Trim() + ".");
                if (inner == null) return null;           // 内层不认识 ⇒ 整句放行（不半译）
                inner = StripLeadSubject(inner).TrimStart('，', ' ');
                return "你说服了你自己，你" + inner + "。";
            }

            // ═══════════════════════════════════════════════════════════════════
            //  ② `flag5`（反弹伤害）尾缀归一化
            // ═══════════════════════════════════════════════════════════════════
            //   原句：`You were killed by your 你自己的攻击 via reflected damage.`
            //   ★ 尾缀必须在**套规则之前**剥掉 —— 否则会被通用被动态的 `(.+?)` 当**人名**吞进 `$2`
            //     （实测：`你被你的你自己的攻击 via reflected damage杀死。` ✗）
            //   ⇒ 剥掉 → 翻主句 → 句末接中文括注。
            string tailZh = null;
            if (t.EndsWith(" via reflected damage.", StringComparison.Ordinal))
            {
                tailZh = "（反弹伤害）";
                t = t.Substring(0, t.Length - " via reflected damage.".Length) + ".";
            }

            for (int i = 0; i < RULES.Length; i++)
            {
                Match m = RULES[i].Rx.Match(t);
                if (!m.Success) continue;
                string[] g = new string[m.Groups.Count];
                for (int k = 0; k < m.Groups.Count; k++) g[k] = m.Groups[k].Value.Trim();
                // ★★ 动词槽校验 ——**只对显式标了 `VerbSlot1 = true` 的规则做**。
                //
                //   【为什么必须显式标，不能靠「模板里有 `$1`」推断】
                //     第一版判据是 `RULES[i].Zh.IndexOf("$1") >= 0` ⇒ 于是
                //     **`^…killed by (.+?)\.$`（`$1` 是施动者，不是动词）也被要求过动词表**
                //     ⇒ 永远查不到 ⇒ 落到兜底型 ⇒ 「你were killed by X。」
                //     （实测：自证 7 条 FAIL，全是这一个判据造成的）
                //   ⇒ 判据改成**规则自己声明**：只有 `$1` **真的是动词槽**的规则才查表。
                if (RULES[i].VerbSlot1)
                {
                    if (g.Length < 2 || g[1].Length == 0) return null;
                    // ★★ `continue` 还是 `return null`？—— 判据是「**通用型排在专用型之前了吗**」：
                    //     · 全部通用型都**排在专用型之后** ⇒ 走到它们时已无更具体的候选
                    //       ⇒ 必须 `return null`，否则会 fall through 到**语义更宽**的
                    //         兜底型，把「只认识一部分的句子」硬翻出来：
                    //           实测 `killed by colliding with 墙` 曾被兜底型吞成
                    //           「你被colliding with 墙杀死。」（中英夹生）✗
                    //     · 这正是本项目 §45 的同族：**放行路径必须是「门禁」，不是「漏斗」**。
                    if (!VERB.TryGetValue(g[1], out string hit)) return null;
                    g[1] = hit;
                }
                string zh = RULES[i].Zh;
                for (int k = 1; k < g.Length; k++)
                {
                    string v = g[k];
                    // ★ 施动者槽里**残留的英文所有格**清掉
                    //   （`You were killed by your 你自己的攻击 …` ⇒ `你的你自己的攻击`）。
                    //   为什么不写在正则里：玩家/生物名是**变量**，位置不定；
                    //   在**捕获结果**上做一次小写 `your ` 替换最稳（英文名侧不会出现小写 your）。
                    if (k == 2 || !RULES[i].VerbSlot1)
                        v = v.Replace("your ", "你的").Replace("Your ", "你的");
                    zh = zh.Replace("$" + k, v);
                }
                if (zh.IndexOf('$') >= 0) return null;   // 有未替换占位 ⇒ 形态不符 ⇒ 放行
                if (tailZh != null)
                {
                    // 句末括注插在句号**之前**
                    int dot = zh.LastIndexOf('。');
                    zh = dot >= 0 ? zh.Substring(0, dot) + tailZh + zh.Substring(dot)
                                  : zh + tailZh;
                }
                return zh;
            }
            return null;
        }

        /// <summary>
        /// `flag4` 外壳：`You convinced yourself you[( were)] &lt;内层句&gt;.`
        /// 捕获组 1 = **内层句**（`killed by 幻影` / `pricked to death`）。
        /// </summary>
        private static readonly Regex NORM4 =
            new Regex(@"^You convinced yourself you(?:\s+were)?\s+(.+?)\.$");

        /// <summary>
        /// 把内层整句削成**谓语片段**，供外层句拼接（外层自己会补主语与句号）。
        ///   `你被幻影杀死。` ⇒ `被幻影杀死`
        ///   `你杀死了幻影。` ⇒ `杀死了幻影`（**只有** 被/把/让 才跟着削主语 ——
        ///      「你杀死幻影」本就以「你」开头，那是**主语**不是被动态标记，不能重复削）
        /// </summary>
        private static string StripLeadSubject(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            if (v.EndsWith("。", StringComparison.Ordinal) || v.EndsWith(".", StringComparison.Ordinal))
                v = v.Substring(0, v.Length - 1);
            if (v.Length >= 2 && v[0] == '你'
                && (v[1] == '被' || v[1] == '把' || v[1] == '让'))
                v = v.Substring(1);
            return v;
        }

        private static object ReadMember(Type t, object inst, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(inst);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p == null ? null : p.GetValue(inst, null);
        }

        private static void WriteMember(Type t, object inst, string name, object val)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) { f.SetValue(inst, val); return; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(inst, val, null);
        }
    }
}
