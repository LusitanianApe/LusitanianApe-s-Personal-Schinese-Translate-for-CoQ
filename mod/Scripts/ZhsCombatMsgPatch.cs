// ═══════════════════════════════════════════════════════════════════════════
// 战斗日志硬编码英文的中文接管
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】
//   大量战斗消息**不走字符串表**，直接在 C# 里拼接/内插。反编译实证：
//
//     // XRL.World.Parts.Combat::HandleEvent
//     AddPlayerMessage($"You block with {arg}! (+{part.AV} AV)", 'g');
//     AddPlayerMessage("You stagger " + E.Attacker.t() + " with your shield block!", 'g');
//     AddPlayerMessage("You are staggered by " + ParentObject.poss("block") + "!", 'r');
//
//     // XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal
//     AddPlayerMessage("You don't penetrate " + Defender.poss("armor") + ".", 'r');
//     AddPlayerMessage("You don't penetrate " + Defender.poss("armor") + " with "
//                      + Attacker.its_(Weapon) + ". {{y|[" + value + "]}}", 'r');
//     AddPlayerMessage(Attacker.Does("don't") + " penetrate your armor with "
//                      + Attacker.its_(Weapon) + "! [" + value + "]");
//     AddPlayerMessage("You fail to deal damage with your attack! [" + value + "]", 'r');
//
//   ⇒ **字符串表改不到它们**，只能接管出口。
//
// 【做法】
//   挂 `MessageQueue.AddPlayerMessage` 的 Prefix，用**模式识别**改写。
//   战斗消息形式有限（十几种），正则一次覆盖；认不出的原样放行（保持英文，
//   而不是改坏）。
//
//   安全性：
//     * 只改写**能匹配**的模式；其余原样
//     * 已含中文的文本直接放行（幂等）
//     * 异常全吞，出错就走原消息
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsCombatMsgPatch
    {
        private const string Tag = "[QudZhsCookbook] 战斗消息接管：";

        private static bool _applied;
        private static int _hits, _scanned;

        // 显示名允许的字符：除了换行与 [ ] 之外都可
        private const string N = @"[^\n\[\]]+?";

        // ═══════════════════════════════════════════════════════════════════
        // ★★ 2.15.91：**名字前的可选英文冠词**（用户截图「严查」）
        //
        //   原文 `The 湿的 光鱼 doesn't penetrate your armor with 它的 咬! [10]`
        //   `The ` 是游戏给**英文对象名**加的大写定冠词；名字已汉化 ⇒ 产出
        //   `The 湿的 光鱼` ⇒ 规则若以 `^(N) …` 捕获，就会把 `The ` 带进中文句首。
        //
        //   ⇒ 在**首部捕获名字**的规则前统一插入 `ART`，让它**吞掉**冠词。
        //     捕获组编号不变（ART 全用非捕获组 `(?:…)`）。
        //
        //   ⚠ 不能只靠消息口的「英文冠词 + 中文」剥离（2.15.86）：
        //     那条要求「冠词后**紧跟中文**」，而这里冠词后跟的是**中文名**没错，
        //     但规则产出的是**已汉化的中文句**里再夹一个英文 The ——
        //     且老版本根本没有那条剥离 ⇒ 必须在生成侧吞掉。
        //
        //   ⚠ 必须**有序**：`The|the|A|a|An|an` 里 `A|a` 在 `An|an` 之前会被
        //     `An` 抢先匹配成 `A` + `n` ⇒ 把 `An` 放前面。
        // ═══════════════════════════════════════════════════════════════════
        private const string ART = @"(?:(?:The|the|An|an|A|a) )?";

        // ★★ 2026-10-17（测试窗口 #44）：物品显示名开头的**元素/特质标记**（实机 `♂ 杰作…`）。
        //   用户纠正：`♂` **不是性别符号**，是 masterwork / 元素标记；同类符号可能还有别的
        //   ⇒ **通配**剥掉 1~3 个「既不是字母数字、也不是标记语法字符」的符号。
        //   ⚠ 必须排除 `{ } | = [ ]`：那是颜色/变量标记的语法，吃进标记里会破坏外壳。
        private const string MARK = @"(?:[^\p{L}\p{N}{}|=\[\]]{1,3}\s*)?";

        // ★ 2.15.92：`poss("armor")` 的**受方名**部分。
        //   中文名 ⇒ `甲虫的armor`（无 `'s`）；英文名 ⇒ `raider's armor`
        //   ⇒ 名字主体**不含「的」与 `'`**（在它们之前停住，让下面的
        //     `的? ?(?:'s )?armor` 吃掉后缀）
        //   ⇒ 同时吞掉前导英文冠词；`(?!your)` 防止被「受方侧」规则抢走
        //   ⚠ 中文名里 `的` 是**所有格**；武器名（`with` 之后）不受此限
        //   ★★ 分支顺序：`An|an` 必须排在 `A|a` **之前** ——
        //      否则 `An` 会被 `A` 抢先匹配成 `A` + `n`，冠词吞不干净。
        //      （这条规则写在 AGENTS.md §44；我第一版在**这里**又踩了一次：
        //        实测 `the raider's armor` 的 `the ` 没被吞掉）
        private const string DEF = @"(?:(?:The|the|An|an|A|a) )?(?:(?!your)[^的'\n\[\]]+?)";

        // ★★ 2.15.92：受方侧句式（`… fails to penetrate your armor`）的**前置护栏**。
        //   攻方侧规则若也去吃「your armor」，会把 `your` 当成受方名 ⇒
        //   产出 `…的your未能穿透$4的护甲！`（实测踩到）。
        //   虽然 `DEF` 里有 `(?!your)`，但**贪婪/回溯**会让整条正则换一种切法绕过它
        //   ⇒ 用负向先行断言在**整条消息层面**排除，最稳。
        private const string NOTYOUR = @"(?![^\n]*\bfails? to penetrate your armor)";

        // ★★ F11（2026-10-16）：第三人称规则的**主语护栏** —— 排除玩家主语 `You`。
        //
        //   ⚠ 为什么**不能**照抄既有的 `(?!You )`：
        //     本族（`命中` 族）的玩家形是 **`You命中`（没有空格）**
        //     —— `%T` 与动词直接拼接（反编译 `Combat::MeleeAttackWithWeaponInternal`
        //        L559 `Append("%T")` + L564 `Append(GetVerb("hit"))`）。
        //     `(?!You )` **要求 `You` 后面跟一个空格** ⇒ 对 `You命中` **拦不住**
        //     ⇒ 实测把 `You` 当成了攻方名，产出
        //        `You用铁蔓收割者命中（x1），对你造成 1 点伤害。`（主语漏英文）✗
        //
        //   ✅ 写法 `(?!You(?![A-Za-z]))`：
        //        · `You命中` / `You hit` ⇒ `You` 后**不是**字母 ⇒ 护栏生效 ⇒ 拦下 ✓
        //        · `Yours`（真以 `You` 开头的名字，如英文宠物名）⇒ `You` 后是字母
        //          ⇒ 内层断言失败 ⇒ 护栏放行 ✓（不误伤这类名字）
        //   ★ 判据同 §44（加可选前缀要防「短词抢先」）：**断言要写全形态，不能写半截**。
        private const string NOTYOU = @"(?!You(?![A-Za-z]))";

        /// <summary>「X 喊道：{{W|'引语'}}」形态 —— 分四段：前缀/开引号/引语/闭引号。</summary>
        private static readonly Regex QuotedSpeech =
            new Regex(@"^(.*?(?:says|yells|shouts|whispers|cries|murmurs|mutters),\s*)\{\{W\|'(.+?)'\}\}$",
                      RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly List<KeyValuePair<Regex, string>> Rules =
            new List<KeyValuePair<Regex, string>>
        {
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2026-10-17：`_frag_gap.tsv` 里 29 条"玩家可见碎片缺口"的收口。
            //   分类取证见 `_tools\_frag_29_分类.md`；上下文见 `_tools\_frag_29_context.md`。
            //   规律：这些**不是碎片**，是 `"句首 " + 动态槽 + " 句尾"` 的半截 ——
            //   塞碎片字典会把后面的英文槽留在原地（`with` 那个坑），所以必须写成带捕获组的规则。
            // ═══════════════════════════════════════════════════════════════


            // ═══════════════════════════════════════════════════════════════

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2026-10-18（#68）：`Popup`/`MessageQueue` 出口的**整句字面量**批量收口。
            //   来源：`_tools\_audit_popup_forms.py` 的 94 条缺口，剔除串表已覆盖（7）
            //   与调试/内部（6）后，把**无变量整句**一次写清（77 条）。
            //   ⚠ 全部 `^…$` 锚定 + 正则转义 ⇒ 只认整句，不碰任何其它文本。
            // ═══════════════════════════════════════════════════════════════
            R(@"^You've been recalled to a resting place\.$",
              "你已被召回至一处休憩之地。"),
            R(@"^You were not recalled as you're already in a resting place\.$",
              "你已在休憩之地，无需召回。"),
            R(@"^You don't have a missile weapon equipped that uses that ammunition\.$",
              "你没有装备使用这种弹药的远程武器。"),
            R(@"^Choose an item to slot a cell into\.$",
              "选择要装入能量电池的物品。"),
            R(@"^You have no hands to beat at the flames with!$",
              "你没有手可以拍打火焰！"),
            R(@"^What would you like to name\?$",
              "你想取什么名字？"),
            R(@"^Barathrum is not in the golem\. Ascend anyway\?$",
              "巴拉瑟鲁姆不在魔像里。仍要升空吗？"),
            R(@"^Do you want to wait until the ascent is done\?$",
              "要等到升空结束吗？"),
            R(@"^No mound of scrap and clay was found to complete\.$",
              "未找到可完成任务的废料与黏土堆。"),
            R(@"^Choose a color for your maker's mark\.$",
              "为你的制造者印记选择一种颜色。"),
            R(@"^You have the feeling of waking from a dream\.$",
              "你有种从梦中醒来的感觉。"),
            R(@"^The cool swelling deflates as your organs dim\.$",
              "随着器官黯淡，那团清凉的肿胀消退了。"),
            R(@"^Your muscles deflate to their usual size\.$",
              "你的肌肉回缩到平常大小。"),
            R(@"^You recognize the area and stop being lost!$",
              "你认出了这片区域，不再迷路！"),
            R(@"^Your heart swells with a burning sensation\.$",
              "你的心脏膨胀起来，泛起灼烧感。"),
            R(@"^Your skin flattens out and stretches tautly around your body once again\.$",
              "你的皮肤重新平复，紧紧绷回身体表面。"),
            R(@"^You feel a soothing tingle in your chest as your wounds start to close\.$",
              "你感到胸口一阵舒缓的酥麻，伤口开始愈合。"),
            R(@"^Your hearts begin to beat faster and your pupils dilate\.$",
              "你的心脏开始加速跳动，瞳孔放大。"),
            R(@"^Your heart begins to beat faster and your pupils dilate\.$",
              "你的心脏开始加速跳动，瞳孔放大。"),
            R(@"^Your heart rate returns to normal and your pupils shrink\.$",
              "你的心率恢复正常，瞳孔收缩。"),
            R(@"^The clouds part in your mind and a ray of clarity strikes through\.$",
              "你脑中的迷雾散开，一道清明之光穿透而来。"),
            R(@"^Your mind clouds over once again\.$",
              "你的心神再次被阴云笼罩。"),
            R(@"^You cannot do that while submerged\.$",
              "在水下无法这么做。"),
            R(@"^A torrent of life rushes over you\.$",
              "一股生命之流涌过你全身。"),
            R(@"^The torrent of life sweeps away\.$",
              "生命之流退去了。"),
            R(@"^You are nut currently in a landmark location\.$",
              "你当前不在地标位置。"),
            R(@"^A loud buzz is emitted\. The unauthorized glyph flashes on the side of the applicator\.$",
              "响起一声刺耳的蜂鸣。未经授权的符文在施用器侧面闪烁。"),
            R(@"^You find a passageway back to your home dimension\.$",
              "你找到了一条回到你原生维度的通道。"),
            R(@"^Use which recoiler\?$",
              "使用哪个召回器？"),
            R(@"^Choose a model faction for your facial reconstruction\.$",
              "为你的面部重建选择一个样板派系。"),
            R(@"^Choose a model faction for your holographic glamour\.$",
              "为你的全息幻象选择一个样板派系。"),
            R(@"^The gates are sealed for eternity\.$",
              "大门将永远封闭。"),
            R(@"^You mind swerves from afar, and a force of repulsion from inside the sarcophagus prevents your entry\.$",
              "你的心神从远处偏转，石棺内一股排斥力阻止你进入。"),
            R(@"^A force of repulsion from inside the sarcophagus prevents your entry\.\n\nYou do not bear the Mark of Death\.$",
              "石棺内一股排斥力阻止你进入。\n\n你未带有死亡印记。"),
            R(@"^You are leaving the ambient broadcast power grid and transitioning to backup power\. Are you sure\?$",
              "你将离开环境广播电网并切换到备用电源。确定吗？"),
            R(@"^The First Council of Omonporch has begun\. Choose how to appease Asphodel\.$",
              "奥蒙波奇第一次议会开始了。选择如何安抚阿斯弗德尔。"),
            R(@"^The pact is struck\. The Barathrumites may lease control of the Spindle, and all the attending factions owe a debt to Asphodel\.$",
              "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权，所有与会派系都欠阿斯弗德尔一份人情。"),
            R(@"^The pact is struck\. The Barathrumites may lease control of the Spindle, and the chosen factions owe a debt to Asphodel\.$",
              "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权，被选中的派系欠阿斯弗德尔一份人情。"),
            R(@"^The pact is struck\. The Barathrumites may lease control the Spindle\.$",
              "契约已缔结。巴拉斯鲁米特可租借纺锤的控制权。"),
            R(@"^You must have a long blade equipped to switch stances\.$",
              "你必须装备长刃才能切换架势。"),
            R(@"^There's nothing there to lunge at\.$",
              "那里没有可突刺的目标。"),
            R(@"^There's nothing there you can lunge at\.$",
              "那里没有你能突刺的目标。"),
            R(@"^You lose your way beneath a dense canopy of spores\.$",
              "你在浓密的孢子穹顶下迷了路。"),
            R(@"^You cannot cross into Brightsheol from this place\.$",
              "你无法从此处进入光灵界。"),
            R(@"^The colossal lid slams shut\. Darkness engulfs you\.$",
              "巨大的盖子轰然合上。黑暗吞没了你。"),
            R(@"^You died\.\n\nEntombed in the burial chamber of Resheph, the Last Sultan\.$",
              "你死了。\n\n你被埋葬在末代苏丹雷谢夫的墓室中。"),
            R(@"^The moors rattle and a marble brick crumbles as a starship takes off\.$",
              "荒原震颤，一艘星舰升空时一块大理石块崩落。"),
            R(@"^You must have an axe equipped in your primary hand to go berserk\.$",
              "你必须在主手装备斧才能进入狂暴。"),
            R(@"^You can't go berserk until Dismember is off cooldown\.$",
              "肢解冷却结束前无法进入狂暴。"),
            R(@"^You must have a cudgel equipped in your primary hand to demolish things\.$",
              "你必须在主手装备棍棒才能拆毁东西。"),
            R(@"^You can't Demolish until Slam is off cooldown\.$",
              "盾击冷却结束前无法拆毁。"),
            R(@"^There's nothing there you can shank\.$",
              "那里没有你能捅刺的目标。"),
            R(@"^You must have a short blade equipped to shank\.$",
              "你必须装备短刃才能捅刺。"),
            R(@"^You have no explosives to deploy!$",
              "你没有可部署的炸药！"),
            R(@"^That wouldn't leave you with NEARLY enough junk! You can't drop that!$",
              "那样你的废料就远远不够了！你不能丢下它！"),
            // ⚠ 注意：规则跑在 `StripColorTags` **之后** ⇒ 含 `{{…}}` 的句子必须写**剥色后的形态**。
            R(@"^Unwelcome images of budding plants fill your mind\.$",
              "{{r|{{G|萌芽植株}}的不祥影像充斥你的脑海。}}"),
            R(@"^Your sphynx salt is about to run out\. Would you like to return to the start of your vision\?$",
              "你的斯芬克斯盐即将耗尽。要回到幻象的起点吗？"),
            R(@"^&CA flash of insight overcomes you!$",
              "&C一道灵光闪过你的脑海！"),
            R(@"^You can't scintillate again so soon\.$",
              "你不能这么快再次闪耀。"),
            R(@"^You're not under enough stress to scintillate\.$",
              "你的压力不足以引发闪耀。"),
            R(@"^There is no liquid here for you to spew\.$",
              "这里没有可供你喷吐的液体。"),
            R(@"^There are no creatures in range\.$",
              "范围内没有生物。"),
            R(@"^You don't have anything to use in that slot\.$",
              "那个槽位里没有可用的东西。"),
            R(@"^Are you sure you want to delete this entry\?$",
              "确定要删除这条记录吗？"),
            R(@"^You can't delete automatically recorded chronology entries\.$",
              "你不能删除自动记录的年表条目。"),
            R(@"^That name is already in use\.$",
              "这个名字已被使用。"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. You feel awfully frigid\.$",
              "你的变异生理对药剂产生了不良反应。你感到彻骨寒冷。"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. Aaaaaaaaargh!$",
              "你的变异生理对药剂产生了不良反应。啊啊啊啊啊啊！"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. Your skin starts to knot and misshape\.$",
              "你的变异生理对药剂产生了不良反应。你的皮肤开始扭结变形。"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. You flicker through spacetime uncontrollably\.$",
              "你的变异生理对药剂产生了不良反应。你在时空中不受控制地闪烁。"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. Your field of vision erupts into a plane of blinding, white light\.$",
              "你的变异生理对药剂产生了不良反应。你的视野炸成一片刺目的白光。"),
            R(@"^Your mutant physiology reacts adversely to the tonic\. The torrent of life sweeps away\.$",
              "你的变异生理对药剂产生了不良反应。生命之流退去了。"),
            R(@"^The tonics you ingested react adversely to each other\. You feel awfully frigid\.$",
              "你摄入的药剂彼此产生了不良反应。你感到彻骨寒冷。"),
            R(@"^The tonics you ingested react adversely to each other\. Aaaaaaaaargh!$",
              "你摄入的药剂彼此产生了不良反应。啊啊啊啊啊啊！"),
            R(@"^The tonics you ingested react adversely to each other\. Your skin starts to knot and misshape\.$",
              "你摄入的药剂彼此产生了不良反应。你的皮肤开始扭结变形。"),
            R(@"^The tonics you ingested react adversely to each other\. You flicker through spacetime uncontrollably\.$",
              "你摄入的药剂彼此产生了不良反应。你在时空中不受控制地闪烁。"),
            R(@"^The tonics you ingested react adversely to each other\. Your field of vision erupts into a plane of blinding, white light\.$",
              "你摄入的药剂彼此产生了不良反应。你的视野炸成一片刺目的白光。"),
            R(@"^The tonics you ingested react adversely to each other\. The torrent of life sweeps away\.$",
              "你摄入的药剂彼此产生了不良反应。生命之流退去了。"),


            // ═══════════════════════════════════════════════════════════════
            // ★★ 2026-10-18（#68 第二弹）：`GetDescription` / `GetLevelText` 的**整句默认描述**
            //   （变异/义体/缺陷的默认文案，硬编码在 C# 里；66 条）
            //   来源 `_tools\_audit_other_exits.py` → 66 条整句候选。
            // ═══════════════════════════════════════════════════════════════
            R(@"^Whenever you drink freshwater, there's a 25% chance you become immune to fear for 6 hours\.$",
              "每当你饮用淡水，有 25% 几率在 6 小时内免疫恐惧。"),
            R(@"^Defib: When powered, if the wearer enters cardiac arrest, this armor will deliver an electrical shock to the wearer every turn until their heart restarts\.$",
              "除颤：通电时，若穿戴者心脏骤停，此护甲会每回合向穿戴者释放电击，直到心脏恢复跳动。"),
            R(@"^Flare-compensating: When powered and started up, the item offers protection against visual flash effects\.$",
              "防眩补偿：通电并启动时，该物品提供对视觉闪光效果的保护。"),
            R(@"^HUD: When powered and started up, this item can be used with a smartgun to enable its bonuses\.$",
              "平视显示：通电并启动时，该物品可与智能枪配合以启用其加成。"),
            R(@"^Heartstopper: When powered, this weapon has a chance to put opponents into cardiac arrest\.$",
              "停搏：通电时，此武器有几率使对手心脏骤停。"),
            R(@"^Massively overloaded: This item has significantly increased performance but consumes a great deal of extra charge, generates considerable heat when used, and has a chance to break relative to its charge draw\.$",
              "严重超载：该物品性能大幅提升，但会消耗大量额外电量、使用时产生可观热量，并会因耗电量增加而有几率损坏。"),
            R(@"^Microserrated: This weapon has a chance to dismember opponents\.$",
              "微锯齿：此武器有几率肢解对手。"),
            R(@"^Mighty: This weapon has no strength bonus penetration cap\.$",
              "强力：此武器没有力量加成穿透上限。"),
            R(@"^You startle easily and engage your defense mechanisms\.$",
              "你容易受惊，并会立刻启动防御机制。"),
            R(@"^You beguile a nearby creature into serving you loyally\.$",
              "你魅惑附近的生物，使其忠诚地为你效劳。"),
            R(@"^You garrote an adjacent creature's mind and control its actions while your own body lies dormant\.$",
              "你绞紧邻近生物的心智，在自身躯体休眠时操控其行动。"),
            R(@"^You may fire missile weapons through the forcefield\.$",
              "你可以穿过力场发射远程武器。"),
            R(@"^You possess a towering vision of self that you project onto the minds of nearby creatures\.$",
              "你怀有宏大的自我意象，并将其投射到附近生物的心智中。"),
            R(@"^You possess extraordinary analytical prowess but you find difficulty in relating to others\.$",
              "你拥有非凡的分析能力，却难以与他人相处。"),
            R(@"^You are possessed of an indefatigable focus which every so often manifests itself as stubbornness\.$",
              "你拥有不知疲倦的专注力，这种专注有时会表现为固执。"),
            R(@"^You bond with a nearby organic creature and leech its life force\.$",
              "你与附近的有机生物建立连结，并汲取其生命力。"),
            R(@"^You tap into the aggregate mind and steal power from other espers\.$",
              "你接入聚合心智，从其他灵能者身上窃取力量。"),
            R(@"^You reflect mental attacks back at your attackers\.$",
              "你将精神攻击反射回攻击者。"),
            R(@"^You peer into your near future\.$",
              "你窥视自己的近期未来。"),
            R(@"^You may open security doors and use some secure devices by touching them\.$",
              "你可以通过触摸打开安全门并使用某些安全设备。"),
            R(@"^You sunder spacetime, sending things nearby careening through a tear in the cosmic fabric\.$",
              "你撕裂时空，使附近之物翻滚着穿过宇宙织物的裂口。"),
            R(@"^You invoke a concussive force in a nearby area, throwing enemies back and stunning them\.$",
              "你在附近区域引发震荡之力，将敌人击退并使其昏迷。"),
            R(@"^You sunder the mind of an enemy, leaving them reeling in pain\.$",
              "你撕裂敌人的心智，使其在痛苦中踉跄。"),
            R(@"^You can move things with your mind\.$",
              "你可以用意念移动物体。"),
            R(@"^You spit a puddle of corrosive acid\.$",
              "你吐出一滩腐蚀性酸液。"),
            R(@"^You live in an alternate plane of reality\.$",
              "你生活在现实的另一个位面。"),
            R(@"^You breathe confusion gas\.$",
              "你呼出混乱气体。"),
            R(@"^You breathe corrosive gas\.$",
              "你呼出腐蚀气体。"),
            R(@"^You breathe normality gas\.$",
              "你呼出常态气体。"),
            R(@"^You can travel underground by burrowing\.$",
              "你可以掘地在地下穿行。"),
            R(@"^You provoke waking dreams with your gaze\.$",
              "你的凝视会引发清醒梦。"),
            R(@"^You are a crystalline being\.$",
              "你是一个晶体生命。"),
            R(@"^Effects that make non-biological clones of you produce twice as many\.$",
              "制造你非生物克隆体的效果产量翻倍。"),
            R(@"^You extract carbon from living material\.$",
              "你从活体中提取碳。"),
            R(@"^In stressful situations, you scintillate\.$",
              "在紧张情境下，你会闪耀。"),
            R(@"^You scare creatures around you\.$",
              "你惊吓周围的生物。"),
            R(@"^Additionally, you sometimes scare creatures passively\.$",
              "此外，你有时会被动地惊吓生物。"),
            R(@"^You teleport and bring creatures along with you\.$",
              "你传送时会把生物一并带走。"),
            R(@"^You molt powerful beams across the spectrum of light and matter\.$",
              "你蜕射出横跨光与物质光谱的强大光束。"),
            R(@"^You emit powerful magnetic pulses\.$",
              "你发出强大的磁脉冲。"),
            R(@"^Several horns jut out of your head\.$",
              "你的头上突出数只角。"),
            R(@"^You bear a sphincter-choked bilge hose that you use to slurp up nearby liquids and spew them at enemies, occasionally knocking them down\.$",
              "你长着一根带括约肌的舱底软管，用来吸取附近液体并向敌人喷吐，偶尔将其击倒。"),
            R(@"^You capture prey with your sticky tongue\.$",
              "你用黏舌捕捉猎物。"),
            R(@"^You turn things to stone with your gaze\.$",
              "你的凝视能将物体化为石头。"),
            R(@"^You can move across walls, and only across walls\.$",
              "你只能沿墙壁移动。"),
            R(@"^You dash along a waveform\.$",
              "你沿着波形疾驰。"),
            R(@"^You regulate your body's release of adrenaline\.$",
              "你能调节身体的肾上腺素分泌。"),
            R(@"^You regenerate by absorbing cold\.$",
              "你通过吸收寒冷再生。"),
            R(@"^Whenever you would have taken cold damage, you heal for 0\.$",
              "每当你将受到寒冷伤害时，你改为治疗 0 点。"),
            R(@"^You accrue electrical charge that you can use and discharge to deal damage\.$",
              "你积累电荷，可将其释放以造成伤害。"),
            R(@"^You emit jets of frost from your mouth\.$",
              "你从口中喷出寒霜。"),
            R(@"^You regenerate by absorbing heat\.$",
              "你通过吸收热量再生。"),
            R(@"^Your joints stretch much further than usual\.$",
              "你的关节比常人伸展得远得多。"),
            R(@"^You are possessed of exceptionally acute smell\.$",
              "你拥有异常敏锐的嗅觉。"),
            R(@"^You are possessed of hulking strength\.$",
              "你拥有庞大的力量。"),
            R(@"^Horns jut out of your head\.$",
              "你的头上突出角。"),
            R(@"^You assume the form of any creature you touch\.$",
              "你能变成任何触碰到的生物。"),
            R(@"^You have an extra set of arms\.$",
              "你多出一双手臂。"),
            R(@"^You have an extra set of legs\.$",
              "你多出一双腿。"),
            R(@"^You may phase through solid objects for brief periods of time\.$",
              "你可以短暂穿过固体。"),
            R(@"^Your wounds heal very quickly\.$",
              "你的伤口愈合得很快。"),
            R(@"^You can walk over slime without slipping\.$",
              "你能在黏液上行走而不滑倒。"),
            R(@"^You can spin sticky silk webs\.$",
              "你能纺出黏性的丝网。"),
            R(@"^While spinning, you leave webs in your wake as you move\.$",
              "纺丝时，你移动会留下一路蛛网。"),
            R(@"^You are immune to getting stuck\.$",
              "你不会被黏住。"),
            R(@"^Creatures are pushed away from center of blast\.$",
              "生物会被冲击波从中心推开。"),

            // ── A 类：整句（原文就是一整条 Show(...)）──
            //   `Popup.Show("You ask about your location and are no longer lost.")`
            R(@"^You ask about your location and are no longer lost\.$",
              "你询问了自己的位置，不再迷路。"),
            //   `Popup.Show("You melt through the floor and descend with your meal.")`
            R(@"^You melt through the floor and descend with your meal\.$",
              "你熔穿地面，带着猎物一起沉了下去。"),

            // ── B 类形态①：`句首 + 槽 + 句尾` ──
            //   `ShowFail("You can't gain " + Category.ToLowerInvariant() + " mutations.")`
            R(@"^You can't gain (.+?) mutations\.$",
              "你无法获得$1突变。"),
            //   `ShowFail("You cannot reach " + Subject.the + Subject.ShortDisplayName + "!")`
            R(@"^You cannot reach (.+?)!$",
              "你够不到$1！"),
            //   `AddPlayerMessage("You find a weakness in " + … + " defenses.", 'g')`
            R(@"^You find a weakness in (.+?) defenses\.$",
              "你发现$1的防御有破绽。"),
            //   `Show("You've contracted " + name + " on your " + Name + ".")`
            R(@"^You've contracted (.+?) on your (.+?)\.$",
              "你的$2染上了$1。"),
            //   `PickOption("Choose a physical " + @for + " to rapidly advance.", …)`
            R(@"^Choose a physical (.+?) to rapidly advance\.$",
              "选择一项$1以快速提升。"),
            //   `JournalMapNote.GetShareText()` → `"The location of " + LowerArticles(GetShortText())`
            R(@"^The location of (.+)$",
              "$1的位置"),
            //   `textBuilder.Append(GetDescription()).Append(" has been applied")`
            //   ⚠ 前面是**已汉化**的效果描述 ⇒ 这一条只补尾巴；
            //     句点可选（原文 Append 完没句点，外层可能补一个）
            R(@"^(.+?) has been applied\.?$",
              "$1已生效。"),
            //   `Show("Your " + name + GetVerb("have") + " consumed all of " + poss + " " + liquid + ".")`
            //   ⚠ `GetLiquidName()` 可能自带 `the` 冠词 ⇒ 用 `(?:the )?` 吸掉
            R(@"^Your (.+?) (?:has|have) consumed all of (?:the )?(.+?)\.$",
              "你的$1耗尽了$2。"),
            //   `Show("The delegate for " + Faction.GetFormattedName(x) + " gives you " + heirloom.an() + "!")`
            R(@"^The delegate for (.+?) gives you (.+?)!$",
              "$1的代表给了你$2！"),
            //   `Show(ParentObject.The + ShortDisplayName + GetVerb("need") + " no supplies.")`
            //   ⇒ 动词在中间，必须连 `need/needs` 一起消费
            R(@"^(.+?) needs? no supplies\.$",
              "$1不需要补给。"),
            //   `AddAccomplishment("You became loved among " + Faction.GetFormattedName() + " and were treated as one of their own.", …)`
            R(@"^You became loved among (.+?) and were treated as one of their own\.$",
              "你在$1中深受爱戴，被当作自己人。"),

            // ── N1 收尾三条（2026-10-17 · 其余 5 条已查明是**假缺口**）──
            //   取证：`_tools\_frag_29_分类.md` §六
            //   `You have no physical…` ⇒ 规则早已存在并生效（`--trace` #300 命中）
            //   `You must wait`          ⇒ 走字符串表（`Context="ActivatedAbilityEntry cant be used - cooldown"`
            //                              / `"TeleporterPair AttemptTeleport Cooldown"`）
            //   `You contracted…to fungi across Qud.` ⇒ 走字符串表
            //                              （`Context="FungalSporeInfection Effect Accomplishment"`）

            //   `ShortBlades_Shank::Cast`：
            //     `AddPlayerMessage(ParentObject.Does("attempt") + " to take advantage of "
            //        + combatTarget.poss("misfortune") + " and shank " + combatTarget.them + ".")`
            //   ⚠ `poss("misfortune")` 会产出英文所有格 ⇒ **连它一起吃掉**，
            //     只保留"谁趁谁分神刺过去"这层意思（同一次攻击，目标相同）
            //   ⚠ 更要紧：`Does("attempt")` 产出的 `attempt/attempts` 也**没在我们的动词表里**
            //     ⇒ 必须把它一起消费，否则会留下 `咬颚兽 attempts趁它分神…`
            R(@"^(.+?) attempts? to take advantage of (.+?) and shank (.+?)\.$",
              "$1趁$3分神，猛刺了过去。"),

            //   `StatusScreen::BuyStat`：
            //     `"Your base " + ChosenStat + " is {{C|" + baseValue + "}}, modified to {{R|" + value + "}}"`
            //   ⚠ 后半截已带色标，原样透传（不能剥，否则面板配色丢失）
            R(@"^Your base (.+?) is (.+?), modified to (.+)$",
              "你的$1基础值为$2，修正后为$3"),

            //   ★★ 2026-10-17：`In the month of …` 的**通用外壳规则已移到 A1 段末尾**
            //     （本文件搜索「A1 通用外壳（兜底）」）。
            //     原因：它排在**专用规则之前**会抢先命中 ⇒ 地点 8/10 那两条
            //     `In the month of X of Y AR, =name= ascended …` 被它吃掉外壳后，
            //     正文作为 `$3` 原样透传 ⇒ **正文永远留英文**。
            //     ★ 这正是接续-i §4 记的第 ⑤ 条判据：「专用规则必须排在通配规则之前」，
            //       可用 `rulecheck --trace` 一眼看穿（#198 命中、#748 也命中，但 #198 先）。
            //     ⇒ 现在的顺序：A1 专用（含正文译文）→ 通用外壳兜底。

            // ── 生命汲取（LifeDrain）──
            //
            // ★ 原文（反编译 XRL.World.Effects.LifeDrain::HandleEvent）：
            //     else if (base.Object.IsPlayer())      // 玩家被吸、抵抗成功
            //         AddPlayerMessage("You resist " + Drainer.poss("life drain") + "!", 'g');
            //     else if (Drainer.IsPlayer())          // 玩家吸血、对方抵抗
            //         AddPlayerMessage(base.Object.Does("resist") + " your life drain!", 'r');
            //
            // ★ 两个坑：
            //   ① `poss()` 走 `Grammar.MakePossessive("水蛭") + " " + "life drain"`
            //      —— 英语加 's，对中文名无从下手 ⇒ 拼成「水蛭 life drain」（「的」丢失）
            //   ② 整句是硬编码字面量 ⇒ 字符串表改不到
            //   ⇒ 只能在这里按模式改写。存档实证：'You resist 水蛭 life drain!'
            R(@"^You resist (" + N + @") life drain!$",
              "你抵抗了$1的生命汲取！"),
            R(@"^You resist (" + N + @")'s life drain!$",
              "你抵抗了$1的生命汲取！"),
            R(@"^" + ART + @"(" + N + @") resists? your life drain!$",
              "$1抵抗了你的生命汲取！"),

            // ── 命中伤害（Combat::MeleeAttackWithWeaponInternal）──
            //
            // ★ 原文（反编译，TextBuilder 分段拼接）：
            //     textBuilder.Append("{{g|You");
            //     textBuilder.Append(" critically");        // 暴击时才加
            //     textBuilder.Append(" hit");
            //     textBuilder.Append(" {{").Append(resultColor).Append("|(x")
            //     … Append(damage.Amount).Append(" damage");
            //     textBuilder.Append(" with ");
            //     Attacker.its_(Weapon, textBuilder);       // ← 「你的 钢制长剑」
            //     textBuilder.Append("! [").Append(value).Append(']');
            //   ⇒ 全是英文字面量 ⇒ 字符串表改不到。
            //   ⇒ 实证：'You hit (x2) for 4 damage with 你的 钢制长剑 [9]'
            //
            // ★ 只用**非捕获组** `(?:…)` 做可选，**捕获组只用于要回填的内容**，
            //   这样 `$1/$2` 编号稳定。
            //   ★ 乘数「(x2)」**连括号一起捕获**（$1）——
            //     否则乘数缺失时会留下孤立的数字（实测漏网：'你命中2，造成…'）。
            //   ★ 因为已**剥色**，规则可以写得很简单（实测 9/9 命中 + 4/4 不误伤）。
            // ★★ F11（2026-10-16）：**空手**形态 —— 必须排在 138 之前
            //
            //   反编译 `Combat::MeleeAttackWithWeaponInternal` L545-548：
            //       if (Weapon == null) textBuilder.Append("your bare hands");
            //   这是 `Weapon == null` 分支唯一的**字面量**武器名 ⇒ 玩家形态是
            //       `You hit (x1) for 3 damage with your bare hands! [9]`
            //   而通用 138 条的 `(.+?)` 会把 `your bare hands` 当武器名原样搬进中文
            //   ⇒ 「用的是your bare hands！」（半译）。
            //   ★ 判据：**具体形态必须排在通用形态之前**（否则通用型先吃）。
            R(@"^You(?: critically)? hit ?\((x\d+)\) for (\d+) damage with your bare hands!? ?\[([^\]]+)\]$",
              "你空手命中（$1），造成 $2 点伤害！[$3]"),
            R(@"^You(?: critically)? hit ?\((x\d+)\) for (\d+) damage with your bare hands!?$",
              "你空手命中（$1），造成 $2 点伤害！"),
            R(@"^You(?: critically)? hit(?: (\(x\d+\)))? for (\d+) damage with (.+?)(?:!)?(?: \[([^\]]+)\])?$",
              "你命中$1，造成 $2 点伤害，用的是$3！[$4]"),
            R(@"^You(?: critically)? miss with (.+?)(?:!)?(?: \[([^\]]+)\])?$",
              "你用$1没打中！[$2]"),
            // 第三人称（同伴/敌人在你视野内命中）
            // ⚠ 2.16.24：143 行加负向断言 `(?!You )` ——
            //   实测 `You hit (x4) for 19 damage with 你的 碳化物拳! [16]`
            //   **同时命中 138/143/378/383/391 五条**；138 更靠前故胜出（输出正确）。
            //   但 143 单独跑会产出 **`You命中（x4），造成…`（主语漏出英文 You）**
            //   ⇒ 它只该管**非 You 主语** ⇒ 排除 `You ` 开头（138 已覆盖）。
            R(@"^(?!You )" + ART + @"(" + N + @")(?: critically)? hits?(?: (\(x\d+\)))? for (\d+) damage with (?:" + ART + @")?(" + N + @")(?:!)?(?: \[([^\]]+)\])?$",
              "$1命中$2，造成 $3 点伤害，用的是$4！[$5]"),

            // ═══════════════════════════════════════════════════════════════════
            // ★★★ F11（2026-10-16 · 用户截图 `猛颚战士命中 (x1) for 1 damage with 她的 咬. [18]`）
            //
            // 【为什么上面那条 143 规则命不中】—— **生产者换了动词的"形态"**
            //   反编译 `XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal`：
            //     L524 `if (Attacker.IsPlayer())`                        ← ① 玩家分支
            //            L526 `Append("{{g|You")` … L531 `Append(" hit")`  ← **字面量** ` hit`
            //     L557 `else if (Defender.IsPlayer())`                   ← ② 他人打你分支
            //            L559 `Append("%T")`                             ← 攻方名占位
            //            L564 `Append(Attacker.GetVerb("hit"))`          ← ★ **变量**！
            //            L567 `Append(" {{").Append(resultColor).Append("|(x")
            //                        .Append(Penetrations).Append(")}}")` ← `(xN)`
            //            L573 `Append(" for ").Append(damage.Amount).Append(" damage")`
            //            L583 `Append(" with ")` + `Attacker.its_(Weapon)`  ← 武器位
            //            L586-587 `Append('.')` + `Append(" [")…Append(']')`
            //     （`%T` 由 `Physics::ProcessTakeDamage` L470-472 换成攻方显示名）
            //
            //   `GetVerb("hit")` → `Grammar.ThirdPerson("hit", true)` → `" hits"`
            //     → 我方 `ZhsVerbFormPatch.Postfix`（C# 直调路唯一收口）**把它换成「命中」**
            //     （该 Postfix 用 `Trim()` 后查表、**赋的是不带前导空格的 `zhv`**）
            //   ⇒ 到达消息口时英文锚 `hits?` **已经不存在了** ⇒ 143/148 条一律不命中。
            //
            //   ★★ 判据（一眼分辨）：**玩家形好、敌人形坏**。
            //     因为玩家分支是**字面量** ` hit`（永远英文），他人分支是**变量**动词。
            //     日志实证（同局）：`{{g|You hit {{&w|(x1)}} for 1 damage with 你的 铁蔓收割者! [10]}}`
            //       ⇒ 命中 138 条 → `你命中(x1)，造成 1 点伤害，用的是你的铁蔓收割者！[10]` ✓
            //     而 `猛颚战士命中 {{&w|(x1)}} for 1 damage with 她的 咬. [18]` ⇒ **无规则命中** ✗
            //
            // 【实机原文（`Mods\QudZhsCookbook\_zhs_untranslated.log` 2026-10-16 18:53:28）】
            //     [29] 猛颚战士命中 {{&w|(x1)}} for 1 damage with 她的 咬. [18]
            //     [33] 猛颚战士命中 {{&w|(x1)}} for 3 damage with 她的 咬. [18]
            //     [22] 猛颚战士 critically命中 {{&W|(x2)}} for 2 damage with 她的 拳头. [20]
            //   去重后的种类里另有 **无 `(xN)`** 的形态（`(x1)` 只在 `!TerseMessages` 时输出）：
            //     咬颌猎手命中 for # damage with 她的 咬. [#]      （12 次）
            //     咬颚拾荒者命中 for # damage with 她的 拳头. [#]
            //   ⇒ 所以 `(xN)` **必须做成两条分支**，不能用 `(?: …)?`
            //     （合并可选项在缺失时会产出空 `（）`，本项目在 ⑤ 号未穿透规则上踩过）。
            //
            // 【写法要点】
            //   · `NOTYOU` 护栏而不是 `(?!You )`（见 `NOTYOU` 的注释：玩家形是
            //     `You命中`，**没有空格** ⇒ `(?!You )` 拦不住）。
            //   · `\s*命中` —— `%T` 与动词之间**没有**空格（Postfix 赋的是 trim 后的词），
            //     但保留 `\s*` 容忍将来动词表里带空格的形态（**零成本**）。
            //   · 句末是 `.` 不是 `!`（他人分支 L586 用的 `'.'`）—— 这也是 143/148
            //     那两条（以 `!` 结尾）**即使动词没被汉化也命不中**的第二个原因。
            //   · ★ **暴击族必须排在非暴击族之前**：`critically` 会被 `(N)` 吞进名字里
            //     （实测产出 `猛颚战士 critically用她的 拳头命中…`）。
            // ═══════════════════════════════════════════════════════════════════
            // ②-f 暴击（`L529 Append(" critically")` + `L564 GetVerb("hit")`
            //      ⇒ 实测拼成 `猛颚战士 critically命中`，**没有空格**）
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*critically\s*命中 ?\((x\d+)\) for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "$1用$4暴击命中（$2），对你造成 $3 点伤害。[$5]"),
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*critically\s*命中 for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "$1用$3暴击命中，对你造成 $2 点伤害。[$4]"),
            // ②-a 带 `(xN)` + 有武器
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*命中 ?\((x\d+)\) for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "$1用$4命中（$2），对你造成 $3 点伤害。[$5]"),
            // ②-b 无 `(xN)` + 有武器（`TerseMessages` 形态）
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*命中 for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "$1用$3命中，对你造成 $2 点伤害。[$4]"),
            // ②-c 带 `(xN)` + 空手（`Weapon == null` ⇒ `" barehanded"`）
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*命中 ?\((x\d+)\) for (\d+) damage barehanded\. ?\[([^\]]+)\]$",
              "$1空手命中（$2），对你造成 $3 点伤害。[$4]"),
            // ②-d 无 `(xN)` + 空手
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*命中 for (\d+) damage barehanded\. ?\[([^\]]+)\]$",
              "$1空手命中，对你造成 $2 点伤害。[$3]"),
            // ②-e 零伤害（`damage.Amount <= 0` ⇒ 不输出 ` for N damage`）
            //   ⚠ 判据边界（实测）：本形态整句**没有 ≥3 字母的拉丁词**时，
            //     `HasCjk && !HasEnglishFrame` 早退门禁会先放行（`(x1)` 不算词）
            //     ⇒ 只有在攻方名含拉丁词（如玩家自建英文名 / 英文宠物名）时才走得到这里。
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*命中 ?\((x\d+)\)\. ?\[([^\]]+)\]$",
              "$1命中（$2）。[$3]"),
            // ②-g ★ 玩家形**显式写 `^You`**（防御性）：
            //     本产者的玩家分支用的是字面量 ` hit`（已被 138 覆盖），
            //     但若哪天官方改成 `GetVerb`，这条立刻接管。
            //     `^You` 必须**自己写** —— `ZhPlayerPronouns` 只认**小写** `you`
            //     （`\byou(?=…)`），句首的 `You` 它**不会**动 ⇒ 不能指望总闸门兜底。
            R(@"^You\s*命中 ?\((x\d+)\) for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "你命中（$1），用的是$3，造成 $2 点伤害。[$4]"),
            R(@"^You\s*命中 for (\d+) damage with (?:" + ART + @")?(" + N + @")\. ?\[([^\]]+)\]$",
              "你命中，用的是$2，造成 $1 点伤害。[$3]"),

            // ═══════════════════════════════════════════════════════════════════
            // ★★ F11 同族（同一根因 · `Does(...)` 的**返回值被汉化**）：
            //    `Combat::MeleeAttackWithWeaponInternal` L710：
            //        AddPlayerMessage(Attacker.Does("don't") + " penetrate your armor with "
            //                         + Attacker.its_(Weapon) + "! [" + value + "]");
            //    `Does("don't")` → `GetVerb` → `Grammar.ThirdPerson` → 我方动词补丁
            //      ⇒ `Does("don't")` 变成**「不」** ⇒ 既有 249 条（要求 `doesn't`）命不中。
            //    ★ 实机原文（同 `_zhs_untranslated.log`）：
            //        [17] 猛颚战士不 penetrate 你的 armor with 她的 拳头! [13]
            //     注意 `your armor` 被渲染成「你的 armor」（`poss("armor")` 只汉化了所有格）。
            //    ★ 所有格 `她的 ` **必须被消费**（否则产出「X的她的拳头…」叠词，F5 同族）。
            // ═══════════════════════════════════════════════════════════════════
            //    ★★ 2026-10-17 修（用户实机 `火花蜱不 penetrate 你的 armor with 它的 电弧！[20]`）：
            //       下面两条**本来就在**，但要求**半角 `!`**，而实机标点是**全角「！」**
            //       （`its_(Weapon) + "! ["` 里的 `!` 已被上游收尾转全角）
            //       ⇒ **永不命中** ⇒ 用户看到整句英文。
            //       ⇒ 改成 `[!！]` 同时接受两种；并修 `[]` 空值（可选组未匹配时 `$N` 为空）。
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*不 penetrate 你的 armor with ?(?:(?:它的|他的|她的|你的|我的|我们|你们|他们|其|该) ?)?(" + N + @")[!！] ?\[([^\]]+)\]$",
              "$1的$2没能穿透你的护甲！[$3]"),
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*不 penetrate 你的 armor with ?(?:(?:它的|他的|她的|你的|我的|我们|你们|他们|其|该) ?)?(" + N + @")[!！]$",
              "$1的$2没能穿透你的护甲！"),
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*不 penetrate 你的 armor[!！] ?\[([^\]]+)\]$",
              "$1没能穿透你的护甲！[$2]"),
            R(@"^" + NOTYOU + ART + @"(" + N + @")\s*不 penetrate 你的 armor[!！]?$",
              "$1没能穿透你的护甲！"),

            // ── ★★ 2026-10-17（用户实机第五张截图）：`reels from the force of` ──
            //   实机：`无眼蟹 reels from the force of the 白化猿的 bludgeoning.`
            //   反编译/查表：`ZhsXDidYPatch.Phrases` 里**有** `("reels from the force of",
            //   "被…的力道打得踉跄")` —— 但那是**半成品**：
            //     ① 译文里是省略号占位（`被…的力道`），真输出会变成
            //        「无眼蟹被…的力道打得踉跄 the 白化猿的 bludgeoning.」—— 更怪
            //     ② 它服务的是 `XDidY` 的「X + 补语」结构，而这句是
            //        **`X + 补语 + 宾语`**（宾语还是个带所有格的复合名）⇒ 结构对不上，根本没命中
            //   ⇒ 用规则直接吃完整句。`bludgeoning` 等武器性状词**不细分**，
            //     统一用「打击」（细分要按 6 种武器类型各写一条，收益不值）。
            R(@"^(.+?) reels from the force of (?:(?:the|The) )?(.+?)的 [a-zA-Z]+\.$",
              "$1被$2的打击打得踉跄。"),

            // ── ★★ 2026-10-17（用户实机第六张截图）：**完成时助动词 `have` 被译成「有」** ──
            //   实机：`Syshwushwaun, village tinker有补满了 他的 的货物!`
            //   反编译 `GenericInventoryRestocker::PerformStock`：
            //       `Messaging.XDidY(ParentObject, "have",
            //            "restocked " + ParentObject.its + " inventory", "!", …)`
            //   ① `"have"` 是 **XDidY 的动词参数（助动词）** ⇒ `Messaging` 变位成 `has`
            //      ⇒ `ZhsVerbPatch` 的 `{ "has", "有" }` 把它译成「有」
            //      ⇒ 「X **有**补满了…」—— 中文里「补满了」本身就是完成态，**「有」必须去掉**
            //   ② 双「的」来自 `ZhsXDidYPatch.Phrases` 的**顺序错**：
            //      `" inventory"` 排在 `"his inventory"` 前面 ⇒ `his inventory` 被拆成
            //      `his` + 「的货物」⇒「他的 的货物」（已在 `Phrases` 里把长条提前修掉）
            //   ★ 本规则只管 ①（`Clean()` 只处理补语，管不到 `Messaging` 拼上去的「有」）。
            R(@"^(.+?)有补满了\s*(.+?)货物[!！]$", "$1补满了$2货物！"),

            // ★★ 2.16.79 新增：**投掷的第三人称**形态（用户截图
            //    `巨型狒狒的 small 石命中 山羊！` / `巨型狒狒用small 石命中山羊（x1），造成 2 点伤害！`）
            //
            //    反编译 `XRL.World.GameObject::PerformThrow` L374 / L378 / L382：
            //      L374 玩家：`"You hit " + <目标> + " with " + Weapon.t() + " (x" + N + ") for " + N + " damage!"`
            //      L378 无主：`Does("hit") + " with " + Weapon.an() + " (x" + N + ") for " + N + " damage!"`
            //      L382 有主：`Does("hit") + " " + <目标> + " with " + Weapon.an() + " (x" + N + ") for " + N + " damage!"`
            //
            //    ★★ 关键：`Does("hit")` **只输出动词变形**（`hits`），
            //       **主语由游戏拼在消息前面**（`MessageQueue` 会带发送者？—— 不会）
            //       ⇒ 实测英文原文就是 **`hits 山羊 with a small stone (x1) for 2 damage!`**，
            //         **没有主语**。所以旧规则 `^You hit …` 不命中，
            //         而 `^(?:ART)?(N) hits? …` 会把 **`hits` 本身当成主语** ⇒ 产出
            //         「巨型狒狒的 small 石命中 山羊！」（把武器当主语）。
            //
            //    ⇒ 判据：① **无主语**形态单独一条（动词 `hits` 开头，L378）；
            //             ② 有主语形态用**肯定的冠词+动词段**锚定（L382）。
            //       ⚠ 前瞻不能写成 `ART + "?hits?"` —— 那会拼出
            //         `(?:(?:The|…|a) )??hits?`，其中 `??` 让冠词组**可空匹配**，
            //         前瞻于是总能命中 `hits `、条件恒假 ⇒ 有主语形态**永不命中**（实测踩过）。
            //    ⇒ 中文**必须自己补动作**（英文靠动词 `hit`，中文用「投出…击中」更自然）。
            R(@"^hits\s+(?:" + ART + @")?(" + N + @") with (?:" + ART + @")?(" + N + @") \(x(\d+)\) for (\d+) damage!$",
              "投出$2，击中$1（x$3），造成 $4 点伤害！"),
            R(@"^hits\s+(?:" + ART + @")?(" + N + @") with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "投出$2，击中$1，造成 $3 点伤害！"),
            R(@"^(?:" + ART + @")?(" + N + @") hits\s+(?:" + ART + @")?(" + N + @") with (?:" + ART + @")?(" + N + @") \(x(\d+)\) for (\d+) damage!$",
              "$1投出$3，击中$2（x$4），造成 $5 点伤害！"),
            R(@"^(?:" + ART + @")?(" + N + @") hits\s+(?:" + ART + @")?(" + N + @") with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "$1投出$3，击中$2，造成 $4 点伤害！"),

            // ★★ 实证原文（反编译 `XRL.World.GameObject::PerformThrow`，投掷命中）：
            //     MessageQueue.AddPlayerMessage("You hit "
            //       + ((gameObject == this) ? itself : gameObject.t())
            //       + " with " + Weapon.t() + " (x" + num11 + ") for "
            //       + damage.Amount + " damage!", …);
            //   ⇒ 格式：`You hit <目标> with <武器> (xN) for N damage!`
            //     **乘数在末尾**（前三轮我按 `hit (xN) for…with` 猜，全错）。
            //   ⇒ 走的是 MessageQueue.AddPlayerMessage ⇒ 出口挂对了，只是规则形态错。
            R(@"^You hit (" + N + @") with (?:" + ART + @")?(" + N + @") \(x(\d+)\) for (\d+) damage!$",
              "你命中$1，用的是$2（x$3），造成 $4 点伤害！"),
            R(@"^You hit (" + N + @") with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "你命中$1，用的是$2，造成 $3 点伤害！"),
            // 第三人称（反编译同处：Does("hit") + " …"）
            R(@"^" + ART + @"(" + N + @") hits with (?:" + ART + @")?(" + N + @") \(x(\d+)\) for (\d+) damage!$",
              "$1用$2命中（x$3），造成 $4 点伤害！"),
            R(@"^" + ART + @"(" + N + @") hits (" + N + @") with (?:" + ART + @")?(" + N + @") \(x(\d+)\) for (\d+) damage!$",
              "$1用$3命中$2（x$4），造成 $5 点伤害！"),

            // ★★ 实证原文（反编译 `XRL.World.Parts.MissileWeapon::MissileHit`）：
            //     EmitMessage(Owner, "You" + text3 + " hit "
            //       + ((Defender == Owner) ? Owner.itself : Defender.t())
            //       + " (x" + num5 + ") with " + Projectile.an()
            //       + " for " + damage.Amount + " damage!", …);
            //   ⇒ 格式：`You hit <目标> (xN) with <弹丸> for N damage!`
            //   ★ 这是**截图那条**的真实来源（走 EmitMessage，不是 AddPlayerMessage）
            //   ★ text3 = " critically"（暴击）
            R(@"^You(?: critically)? hit (" + N + @") \(x(\d+)\) with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "你命中$1（x$2），用的是$3，造成 $4 点伤害！"),
            R(@"^You(?: critically)? hit (" + N + @") with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "你命中$1，用的是$2，造成 $3 点伤害！"),
            R(@"^Something hits (" + N + @") \(x(\d+)\) with (?:" + ART + @")?(" + N + @") for (\d+) damage!$",
              "有什么东西命中$1（x$2），用的是$3，造成 $4 点伤害！"),

            // ── 格挡 ──
            // You block with your steel shield! (+3 AV)
            R(@"^You block with (?:your )?(" + N + @")! \(\+(\d+) AV\)$",
              "你用$1格挡住了！（+$2 AV）"),      // 你用X格挡住了！（+N AV）
            R(@"^" + ART + @"(" + N + @") blocks? your attack with (?:" + ART + @")?(" + N + @")!?$",
              "$1用$2格挡住了你的攻击！"),      // X用Y格挡住了你的攻击！
            R(@"^You stagger (" + N + @") with your shield block!$",
              "你用盾牌格挡撞跌了$1！"),        // 你用盾牌格挡撞跌了X！
            R(@"^You are staggered by (" + N + @")'s block!$",
              "你被$1的格挡撞跌了！"),              // 你被X的格挡撞跌了！
            R(@"^You are staggered by (" + N + @") block!$",
              "你被$1的格挡撞跌了！"),

            // ── 未穿透护甲 ──
            //
            // ★★ 2.15.91 加固（用户截图「严查」）：
            //   原文 `The 湿的 光鱼 doesn't penetrate your armor with 它的 咬! [10]`
            //   · `The ` 来自 `Attacker.Does("don't")` 内部对**英文对象名**的大写定冠词
            //     （名字已汉化 ⇒ 产出 `The 湿的 光鱼`）
            //   · `它的 ` 来自 `Attacker.its_(Weapon)`（`its_` 硬编码 + 空格）
            //   ⇒ 旧规则命中后**把 `The` 原样带进译文**，产出
            //     `The 湿的 光鱼用它的 咬没能穿透你的护甲！[10]`
            //   ⇒ 加固：名字捕获允许并**吞掉**可选的前导冠词；
            //     译文里**不再复述武器所有格**（`X用它的咬…` → `X的咬…`）
            //   ⚠ 不能依赖消息口的「英文冠词+中文」剥离（那只在**新版**才有，
            //     且本规则产出的是**中文句首的 The**，剥不掉）
            // You don't penetrate the raider's armor. [9]
            // ★ 2.15.91：名字捕获允许并**吞掉**可选冠词（旧规则会把 `the raider` 带进中文）
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")'s armor\.\s*(?:\{\{y\|\[(\d+)\]\}\})?$",
              "你没能穿透$1的护甲。[$2]"),          // 你没能够穿透X的护甲。
            // ★ 2026-10-17（#5）：同一条的**裸方括号**形态（实机 `. [9]` 不是 `.{{y|[9]}}`）
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")'s armor\.\s*\[(\d+)\]$",
              "你没能穿透$1的护甲。[$2]"),
            // You don't penetrate the raider's armor with its bronze dagger. {{y|[9]}}
            // ★ 2.15.91：`its ` 属**己方武器所有格**，中文里冗余 ⇒ 吞掉不复述
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")'s armor with (?:(?:its|his|her|their|your) )?(" + N + @")\.\s*(?:\{\{y\|\[(\d+)\]\}\})?$",
              "你的$2没能穿透$1的护甲。[$3]"),
            // ★★ 2026-10-17（测试窗口 #5）：**中文所有格**版。
            //   实机：`You don't penetrate 无眼蟹的 armor with 你的 黏浆与淤泥沾污的钢制长剑. [4]`
            //   · 目标名已被名称表译成中文 ⇒ 所有格是「的」，不是 `'s`
            //   · 武器所有格 `its_`/`your` 到达时已是「你的」/「它的」
            //   · `[N]` 是**裸方括号**（不是 `{{y|[N]}}`）
            //   ⇒ 上面两条 `'s armor` 规则**结构上不可能命中**；这里按「的 armor」另开。
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")的 armor with (?:你的|它的|他的|她的|其|该|your|its|his|her|their)? ?(" + N + @")\.[ ]*\[(\d+)\]$",
              "你的$2没能穿透$1的护甲。[$3]"),
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")的 armor with (?:你的|它的|他的|她的|其|该|your|its|his|her|their)? ?(" + N + @")\.[ ]*(?:\{\{y\|\[(\d+)\]\}\})?$",
              "你的$2没能穿透$1的护甲。[$3]"),
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")的 armor\.[ ]*\[(\d+)\]$",
              "你没能穿透$1的护甲。[$2]"),
            R(@"^You don't penetrate (?:(?:The|the|A|a|An|an) )?(" + N + @")的 armor[.。]$",
              "你没能穿透$1的护甲。"),
            // The Issachari raider doesn't penetrate your armor with her bronze dagger! [20]
            // ★ 2026-10-17（#5）三处同时修：
            //   ① 句末可能是**全角** `！`（名字译成中文后标点跟着变）
            //   ② `your` 到达时已是「你的」（代词映射先跑）
            //     ⇒ 旧规则 `doesn't penetrate your armor` 结构上不可能命中
            //   ③ `[N]` 有「带 / 不带」两种形态，两条都要有（§45「门禁成组维护」）
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") doesn't penetrate (?:your|你的) armor with (?:它的|他的|她的|其|该|your|its|his|her|their) ?(" + N + @")[!！]? ?\[(\d+)\]$",
              "$1的$2没能穿透你的护甲！[$3]"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") doesn't penetrate (?:your|你的) armor[!！]? ?\[(\d+)\]$",
              "$1没能穿透你的护甲！[$2]"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") doesn't penetrate (?:your|你的) armor with (?:它的|他的|她的|其|该|your|its|his|her|their) ?(" + N + @")[!！。]?$",
              "$1的$2没能穿透你的护甲！"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") doesn't penetrate (?:your|你的) armor[!！。]?$",
              "$1没能穿透你的护甲！"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.15.92：**投射物**的「未能穿透」——`fails to` 族
            //   用户截图：`The 吐籽藤蔓的 种子 fails to penetrate your armor!`
            //
            //   反编译 `XRL.World.Parts.MissileWeapon::MissileHit`（4 处拼接）：
            //     // ① 玩家为攻方 + 武器带 Message
            //     EmitMessage(text + MessageAsFrom.GetVerb("fail") + " to penetrate "
            //       + Defender.poss("armor") + " with " + MessageAsFrom.its_(Projectile)
            //       + OutcomeMessageFragment + "!", 'r', …);
            //     // ② 玩家为攻方、武器无 Message            ← 用户截图是这一支
            //     EmitMessage(Owner.Poss(Projectile) + Projectile.GetVerb("fail")
            //       + " to penetrate " + Defender.poss("armor")
            //       + OutcomeMessageFragment + "!", 'r', …);
            //     // ③ 玩家为**受**方 + 武器带 Message（`'g'` 绿色）
            //     EmitMessage(… + " to penetrate your armor with " + … + Fragment + "!", 'g', …);
            //     // ④ 玩家为**受**方、武器无 Message           ← 用户截图是这一支
            //     EmitMessage(Owner.Poss(Projectile) + Projectile.GetVerb("fail")
            //       + " to penetrate your armor" + OutcomeMessageFragment + "!", 'g', …);
            //
            //   ★ `OutcomeMessageFragment` 是**运行时可变的**
            //     （由 `MissilePenetrateEvent` 填，值可为 `null`、` [10]`、
            //       `, but your mental attack has no effect` …）
            //     ⇒ 正则**尾部必须宽容**（捕获后丢弃，不进中文）
            //   ★ `Owner.Poss(Projectile)` ⇒ 名字可能是 `X的 Y` 形态
            //     （中文 Poss 会产出「的」）⇒ 名字捕获要允许**第二个「的」分段**
            //
            //   ⚠ 出口链已核：`EmitMessage` → `Messaging.HandleMessage`
            //     → `MessageQueue.AddPlayerMessage` ⇒ 本规则表能拦到
            // ═══════════════════════════════════════════════════════════════

            //   ★ 捕获组约定（4 条统一）：
            //       $1 = 射主名（`Owner`）
            //       $2 = 投射物名（`Owner.Poss(Projectile)` 里 `X的 Y` 的 Y，可空）
            //       $3 = 受方名（仅攻方侧两条有）
            //       $4 = 武器名（仅「武器带 Message」两条有）
            //   ★ 受方名用**贪婪** `N`，让它吃到最后一个 `'s armor` 之前
            //     （`甲方名用非贪婪` 会让 `甲虫的` 被吃进名字 ⇒ 尾部对不上，实测踩过）
            //   ★ 尾部 `[!.,][^\n]*$` —— `OutcomeMessageFragment` 实测可为
            //     ``（空）、` [10]`、`, but your mental attack has no effect`
            //     ⇒ 标点后**全部宽容丢弃**，不进中文
            //   ★ `fails?` 同时吃 `fail`（复数主语，`Projectile.GetVerb` 给原形）

            // ④ 玩家为受方、无武器 Message：`The 吐籽藤蔓的 种子 fails to penetrate your armor!`
            //    ★ 这一条是**用户截图那句**，已实测命中 ⇒ 不要动分组
            R(@"^" + ART + @"(" + N + @")(?:的[ ]?(" + N + @"))? fails? to penetrate your armor[!.,][^\n]*$",
              "$1$2未能穿透你的护甲！"),
            // ③ 玩家为受方、武器带 Message  ⇒ $3 = 武器名
            R(@"^" + ART + @"(" + N + @")(?:的[ ]?(" + N + @"))? fails? to penetrate your armor with (?:它的|他的|她的|其|该|its|his|her|their) ?("
              + N + @")[!.,][^\n]*$",
              "$1$2的$3未能穿透你的护甲！"),
            // ② 玩家为攻方、无武器 Message：`… fails to penetrate 甲虫的armor!(…)`
            //   ★★ 关键：`poss("armor")` 在**名字已汉化**时产出 `甲的armor`
            //      （**没有 `'s`、没有空格**）—— 实测 `甲虫的armor!` 里撇号根本不存在。
            //      写死 `'s armor` 会**永远匹配不上**（这是我踩过的坑）。
            //      ⇒ 用 `的? ?(?:'s )?armor` 兼容中英两种形态：
            //         中文名 ⇒ `甲虫的armor`    英文名 ⇒ `raider's armor`
            R(@"^" + NOTYOUR + ART + @"(" + N + @")(?:的[ ]?(" + N + @"))? fails? to penetrate ("
              + DEF + @")的? ?(?:'s )?armor[!.,][^\n]*$",
              "$1$2的$3未能穿透你的护甲！"),
            // ① 玩家为攻方、武器带 Message  ⇒ 武器是第 4 组
            R(@"^" + NOTYOUR + ART + @"(" + N + @")(?:的[ ]?(" + N + @"))? fails? to penetrate ("
              + DEF + @")的? ?(?:'s )?armor with (?:它的|他的|她的|其|该|its|his|her|their) ?("
              + N + @")[!.,][^\n]*$",
              "$1$2的$4未能穿透$3的护甲！"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.15.93：隐藏物「现形」（用户截图 `A 远方草丛 is revealed 此处!`）
            //
            //   反编译 `XRL.World.Parts.HiddenRender::Reveal`（**唯一**发出点）：
            //       IComponent<GameObject>._T("HiddenRender RevealedSomewhere",
            //           "=subject.A.name= =subject.verb:are= revealed =subject.to.the.direction=!")
            //         .SetSubject(ParentObject).EmitMessage(' ', FromDialog: …)
            //
            //   我方该条**已有正确译文**（`Strings.xml`，Context 与 ID 逐字一致、全仓唯一）：
            //       `=subject.A.name=在=subject.to.the.direction=现形了！`
            //   但实测渲染的是 **ID 原文**（查表 miss ⇒ 返回 ID 那套机制）
            //   ⇒ 不依赖查表结果，在**消息口**兜一条规则最稳。
            //
            //   渲染形态（截图实证）：`A 远方草丛 is revealed 此处!`
            //     · `A `   ← 名字仍带英文冠词（`=subject.A.name=`）
            //     · `此处` ← `=subject.to.the.direction=` 命中我方 `direction.expanded (.)`
            //   ⇒ $1 = 整段主语（可能带 `A `/`An `/`The `），中文里**去冠词**
            // ═══════════════════════════════════════════════════════════════
            R(@"^((?:(?:The|the|An|an|A|a) )?" + N + @") (?:is|are|was|were) revealed (" + N + @")!$",
              "$1在$2现形了！"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.15.95：`X hits you (xN) with a Y for N damage!`（用户截图）
            //
            //   ★ **语序与下面那批不同** —— 本形态是
            //       `名字 hits you (xN) with 武器 for N damage!`
            //     （`you` 在 `(xN)` **之前**，`for N damage` 在**最后**）
            //   而既有 4 条 `hits you …` 规则要求 `hits you for N damage`
            //   或 `(xN) for N damage` ⇒ **一条都不命中**
            //   （离线实测：`蛞蝓鼻 hits you (x2) with a 蛞蝓 for 9 damage!`
            //     在加入本条前 = **无规则命中**）。
            //
            //   $1 = 攻方名   $2 = 穿透次数   $3 = 伤害
            //   （武器名只吃不复述 —— 中文「用X次命中你」已表达）
            // ═══════════════════════════════════════════════════════════════
            R(@"^" + ART + @"(" + N + @") hits you \(x(\d+)\) with " + ART + N
              + @" for (\d+) damage!$",
              "$1用$2次命中你，造成 $3 点伤害！"),

            // ── ★★ 2026-10-17（测试窗口 #12）：**动词已被译、骨架还在**的投掷命中 ──
            //   实机：
            //     `种子命中你（x1）来自东北方向 for 0 damage!`
            //     `吐籽藤蔓命中你（x1） with 一把 种子 for 0 damage!`
            //   产地（反编译 `GameObject::PerformThrow`）：
            //     `Does("hit") + " with " + Weapon.an() + " (x" + N + ") for " + dmg + " damage!"`
            //     另一路 `DidXToY("hit", …)` + 方向 ⇒ `… 来自东北方向 for …`
            //   ⇒ `Does("hit")` 已被动词补丁译成「命中你」，上面的英文规则**结构上不可能命中**。
            //   ★ 括号可能是**全角**「（x1）」⇒ 两种都收；`（xN）` 只在有穿透次数时出现，
            //     所以「无 (xN)」形态也要各留一条（§45 门禁成组）。
            R(@"^" + ART + @"(" + N + @")命中你 ?[（(]x(\d+)[）)] with " + ART + "(" + N
              + @") for (\d+) damage!$",
              "$1用$3命中你（x$2），造成 $4 点伤害！"),
            R(@"^" + ART + @"(" + N + @")命中你 ?[（(]x(\d+)[）)] ?来自(.+?) for (\d+) damage!$",
              "来自$3的$1命中你（x$2），造成 $4 点伤害！"),
            R(@"^" + ART + @"(" + N + @")命中你 with " + ART + "(" + N + @") for (\d+) damage!$",
              "$1用$2命中你，造成 $3 点伤害！"),
            R(@"^" + ART + @"(" + N + @")命中你 ?来自(.+?) for (\d+) damage!$",
              "来自$2的$1命中你，造成 $3 点伤害！"),

            // ── ★★ 2026-10-17（测试窗口 #33）：眩晕 ──
            //   `Dazed` 走串表（已译「处于眩晕」），`Stun` 走 `DidX("are", "stunned", "!")`
            //   ⇒ 到达时是 `岩人脑魔 is stunned!`（或被碎片换成「昏迷」）。
            //   两条形态都收（§45 门禁成组）。
            R(@"^You are stunned!$", "你昏迷了！"),
            R(@"^" + ART + @"(" + N + @") (?:is|are|was|were) stunned!$",
              "$1昏迷了！"),
            R(@"^" + ART + @"(" + N + @") (?:is|are|was|were) 昏迷!$",
              "$1昏迷了！"),

            // ── ★★ 2026-10-17（测试窗口 #32）：棍棒猛击的「to + 动词」半译 ──
            //   实机：`机械师圣骑士试图 to slam into you!`
            //   产地：`Cudgel_Slam::Cast` 拼 `"to slam into"` + `Does("try")`
            //   ⇒ `try` 先被译成「试图」，剩余 ` to slam into you!` 没人接。
            R(@"^" + ART + @"(" + N + @")\s*(?:tries|try|试图)\s+to slam into you!$",
              "$1试图猛撞你！"),
            R(@"^" + ART + @"(" + N + @")\s*(?:tries|try|试图)\s+to slam into (" + N + ")!$",
              "$1试图猛撞$2！"),

            // ── ★★ 2026-10-17（测试窗口 #34）：被动「无法被移动」──
            //   实机（刷屏）：`Deactivated 链式激光炮台无法 be moved.`
            //   产地：`GameObject.Move` 的字面量 ` be moved.`；`cannot` 先被译成「无法」
            //   ⇒ 既有 `cannot be moved` 规则失配，留下半译。
            R(@"^" + ART + @"(" + N + @")\s*(?:cannot|can't|无法)\s*be moved[.。]$",
              "$1无法被移动。"),

            // ── 攻击未造成伤害 ──
            R(@"^You fail to deal damage with your attack! ?\[(\d+)\]$",
              "你的攻击未能造成伤害！[$1]"),  // 你的攻击未能造成伤害！
            R(@"^" + ART + @"(" + N + @") fails? to deal damage with its attack! ?\[(\d+)\]$",
              "$1的攻击未能造成伤害！[$2]"),
            // ★★ 日志实证（2.5.1）：
            //   原文 `盐跳虫 fails to deal damage with 它的 attack! [10]`
            //   旧规则 `^(N) fails? to deal damage with (N) attack! ?\[(\d+)\]$`
            //   把「它的」当成了第二个 N（名字）⇒ 产出「盐跳虫**的它的**攻击未能造成伤害！」
            //   ⇒ 修法：把 `它的/他的/她的/你的/其/该` 这些**参数词**作为**可选组**吃掉。
            //   ★★ 2.15.91 修正（离线 e2e 验证抓到的规则冲突）：
            //     本条与上一条（`… with its attack!`）**同时命中**英文原文
            //     `盐跳虫 fails to deal damage with its attack! [10]`
            //     ⇒ 上一条产出「盐跳虫的攻击未能造成伤害！」
            //       本条产出「盐跳虫的**its**攻击未能造成伤害！」（坏句）
            //     根因：本条的捕获 `[^\n\[\] ]+?` 把英文 `its` 也当成了「武器名」。
            //     ⇒ 收紧为**只吃中文参数词**：`(?:它的|他的|她的|你的|其|该)? (中文武器名)`
            //       （英文原文一律由上一条负责；中文参数词只在**参数被汉化后**出现）
            R(@"^" + ART + @"(" + N + @") fails? to deal damage with "
              + @"(?:(?:它的|他的|她的|你的|我的|其|该) ?)?([\u4e00-\u9fff][^\n\[\] ]*) attack! ?\[(\d+)\]$",
              "$1的$2攻击未能造成伤害！[$3]"),
            // ── ★★ 2026-10-17（用户实机截图）：**动词已被译**后的形态 ──
            //   实机原文（新存档，非残留）：
            //       `鳄鱼失败 to deal damage with 她的 attack! [19]`
            //   反编译 `XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal`（两条分支）：
            //       玩家：`"You fail to deal damage with your attack! [" + value + "]"`   ← **整条字面量**
            //       他人：`Attacker.Does("fail") + " to deal damage with "
            //              + Attacker.its + " attack! [" + value + "]"`                 ← **拼出来的**
            //   ⇒ 我方 `ZhsVerbPatch` 把 `fail` 译成「失败」 ⇒ 到达消息口时**英文 `fails?` 已不存在**
            //     ⇒ 上面三条（都要求英文 `fails?`）**永不命中** —— 与 F11 的 `hits` 是同一个根因。
            //   ★ 骨架 `to deal damage with` 是**字面量**（DLL 里 2 处，owner 都是 Combat），
            //     `its` 是已汉化的所有格属性，`attack` 是字面名词（`its_()` 不查表）
            //   ⇒ `$2` 是「她的 」这种（含尾空格）—— 由 `CollapsePossessiveSpace` 收尾清掉。
            //   ★ 玩家形不受影响：它是**整条字面量**，不经 `Does()` ⇒ 仍由上面第 1 条命中。
            // ★ 2026-10-18（#27）：**参数词单独成句**的形态 —— 原规则产出「用她的攻击」，
            //   中文里「用…攻击」不通（应为主语 + 的 + 攻击）。实机：
            //   `贝撒大旋涡幽灵用她的攻击未能造成伤害！`
            //   参数词 = 它的/他的/她的/你的/我的/其/该（后面**没有武器名**）。
            R(@"^(.+?)失败 to deal damage with (?:它的|他的|她的|你的|我的|其|该) ?attack! ?"
              + @"(?:\{\{y\|)?\[(\d+)\](?:\}\})?$",
              "$1的攻击未能造成伤害！[$2]"),
            R(@"^(.+?)失败 to deal damage with (?:它的|他的|她的|你的|我的|其|该) ?attack[!！]$",
              "$1的攻击未能造成伤害！"),
            R(@"^(.+?)失败 to deal damage with (.+?)attack! ?\[(\d+)\]$",
              "$1用$2攻击未能造成伤害！[$3]"),
            // ── ★ 日志/地图注记（`Qud.API.Journal*Note::Reveal` **族**，硬编码+拼接）──
            //   反编译实证这一族的**共同形态**（4 个类）：
            //     · JournalMapNote::Reveal
            //         "You note the location of " + Grammar.MakeTitleCaseWithArticle(Text)
            //       + " in the {{W|" + JournalScreen.STR_LOCATIONS + " > Historic Sites}} section…"
            //     · JournalSultanNote::Reveal
            //         "You note this piece of information in the {{W|"
            //       + JournalScreen.GetSultansDisplayName() + " > "
            //       + HistoryAPI.GetEntityName(SultanID) + "}} section of your journal."
            //     · JournalObservation::Reveal / JournalVillageNote::Reveal（同形态）
            //   ⇒ 因此写 **2 条通用规则**（按 `> ` 分段），而不是逐条枚举。
            // ★★ 2026-10-17（离线测试台 `_tools\rulecheck` 抓到的）：本条的捕获组是**超集**
            //   ——它同样能匹配 `You note the location of X in the {{W|…}} section of your journal.`
            //   与 `You note this piece of information in …`，于是把本该交给
            //   `ZhsJournalNotePatch`（要求 `{{W|…}}` **原样**）的两句先吃掉，
            //   产出 `你在日志的地点 > 商人栏记下了the location of a dromad caravan。`
            //   ⇒ 加**负向先行断言**，把这两类让给专用补丁。
            //   （前两条同类规则已删除；本条保留是给「其它 note 形态」用的。）
            // ── ★★ 2026-10-17（用户实机弹窗）：**外层骨架没人接手** ──
            //   实机：`You note this piece of information in the 村落史 › Sharshiwar section of
            //          your journal.`（`村落史` 已中文、村庄名保留、**骨架全英文**）
            //   根因：下面那条规则用**负向断言**把 `this piece of information` 排除了
            //        （`(?!the location of |this piece of information )` —— 本意是「让给
            //          `ZhsJournalNote`」），但 `ZhsJournalNote` 只负责把 `PAGE > CAT` 里的
            //        **分类名**翻成中文，**不碰外层骨架** ⇒ 这句**谁都没吃掉**。
            //   ★ 佐证（离线 4/4 复现）：带 `{{W|…}}` 的形态与裸形态**都不命中**
            //        `…in the {{W|村落史 › Sharshiwar}} section of 你的 journal.`
            //        （只做了 `your`→`你的` 与分类名翻译）
            //   producer：`Qud.API.JournalVillageNote::Reveal`
            //             `"You note this piece of information in the {{W|" + PAGE + " > "
            //              + ENTITY + "}} section of your journal."`
            //   同族：`JournalObservation::Reveal` / `JournalSultanNote::Reveal`（同形态）
            //   ★ 译文风格与下面那条一致（「你在日志的…栏记下了…」）。
            R(@"^You note this piece of information in the \{\{W\|(.+?)\}\} section of your journal\.$",
              "你在日志的$1栏记下了这条信息。"),
            R(@"^You note this piece of information in the (.+?) section of your journal\.$",
              "你在日志的$1栏记下了这条信息。"),
            // ── ★ 同族另一半 `JournalMapNote::Reveal`（`You note the location of X in the …`）**不加规则** ──
            //   离线实测（三阶段真实管线：`ZhsJournalNote.Apply` → `TranslateText` → 碎片表）：
            //     `You note the location of a dromad caravan in the {{W|地点 > Historic Sites}} section of your journal.`
            //     ⇒ `你在日志的{{W|位置 › 历史遗迹}}部分记下了 一支驼人商队。`
            //   即**已被 `ZhsJournalNote.Apply` 汉化**（它是 `JournalNoteTag` 专门负责的那一族）。
            //   ★ 若这里再加规则，**单跑 `TranslateText` 时**会命中、但在真实管线里被抢先 ⇒
            //     只会制造「单测通过、实机没用」的假象（本轮就踩了一次，③a 报「不符 1」）。
            //   ⏳ 遗留（**不是本轮范围**）：它的输出里仍带 `{{W|…}}` 原文与「下了 一支」的空格，
            //      属该函数自身的问题，另记。
            R(@"^You note (?!the location of |this piece of information )(" + N + @") in the ("
              + N + @") section of your journal\.$",
              "你在日志的$2栏记下了$1。"),
            R(@"^You discover the location of (" + N + @")\.$",
              "你发现了$1的位置。"),
            R(@"^You discovered the location of (" + N + @")\.$",
              "你已发现$1的位置。"),
            // ★★ 2026-10-17（测试窗口 #3）：兜底必须**禁止跨到 `section of`**。
            //   实机：`You note the location of Sithis，传奇蝎蚂的巢穴 in 地点 › 巢穴
            //          section of your journal.`
            //   上面 `ZhsJournalNote.Apply` 因分类名已是中文而没命中（已修），
            //   这条兜底的 `(N)` 会把**整段**（含 `in … section of your journal`）吞成 `$1`
            //   ⇒ 「你记下了…的位置。」（半译）。
            //   ⇒ 捕获组加负向断言：不允许包含 `section of your/你的 journal`（#66：`the ` 漏网后
            //     实机出现过 `你的 journal` 形态，只挡英文挡不住）。
            R(@"^You note the location of ((?:(?!section of (?:your|你的) journal).)+)\.$",
              "你记下了$1的位置。"),

            R(@"^Your mental attack does not affect (" + N + @")\.$",
              "你的精神攻击对$1无效。"),          // 你的精神攻击对X无效。

            // ── 命中 / 伤害 ──
            // You hit (x4) for 10 damage with your steel long sword! [9]
            R(@"^You hit ?\(x(\d+)\) for (\d+) damage with (?:" + ART + @")?(" + N + @")! ?\[(\d+)\]$",
              "你用$3命中（x$1），造成 $2 点伤害！[$4]"),
            R(@"^You hit ?\(x(\d+)\) for (\d+) damage! ?\[(\d+)\]$",
              "你命中（x$1），造成 $2 点伤害！[$3]"),
            // The Issachari raider hits (x2) for 4 damage with her bronze dagger. [9]
            R(@"^" + ART + @"(" + N + @") hits? ?\(x(\d+)\) for (\d+) damage with (?:" + ART + @")?(" + N + @")\.? ?\[(\d+)\]$",
              "$1用$4命中（x$2），对你造成 $3 点伤害。[$5]"),
            R(@"^" + ART + @"(" + N + @") hits? ?\(x(\d+)\) for (\d+) damage\.? ?\[(\d+)\]$",
              "$1命中（x$2），对你造成 $3 点伤害。[$4]"),
            R(@"^" + ART + @"(" + N + @") hits? you for (\d+) damage with (?:" + ART + @")?(" + N + @")\.? ?\[(\d+)\]$",
              "$1用$3命中你，造成 $2 点伤害。[$4]"),
            R(@"^" + ART + @"(" + N + @") hits? you for (\d+) damage\.? ?\[(\d+)\]$",
              "$1命中你，造成 $2 点伤害。[$3]"),
            R(@"^You hit (" + N + @") for (\d+) damage with (?:" + ART + @")?(" + N + @")\.? ?\[(\d+)\]$",
              "你用$3命中$1，造成 $2 点伤害。[$4]"),

            // ── 六日高跷狂热者（硬编码在 SixDayZealot::ZealotDeclaim）──
            R(@"^The zealot mumbles inaudibly, encased in ice\.$",
              "狂热者被冻在冰里，只能发出含混不清的声音。"),
            R(@"^The zealot yells \{\{W\|'(.+)'\}\}$", "狂热者喊道：「{{W|$1}}」"),
            R(@"^Make an offering at the Argent Well! Pay homage to your Fathers!$",
              "去银井献上供品！向你的列祖致敬！"),
            R(@"^Cast down your artifacts! You are not worthy of their make!$",
              "丢下你的器物！你不配拥有它们！"),
            R(@"^Piety compels you to deliver your sacred relics to the priests in the cathedral! Cleanse them of your filth!$",
              "虔诚迫使你把圣物交给大教堂的祭司！洗去你身上的污秽！"),
            R(@"^The Machine commands that you exorcise robots and bring their sacred husks here!$",
              "机械命令你驱除机器人身上的邪祟，把它们的圣骸带到这里来！"),

            // ── 荆棘反伤 / **环境伤害来源**（2026-10-15 重排 + 放宽 + 补长尾）──
            //
            // ★ 三个问题一起修（A4 扫描实证，见 `_tools\_from_family_scan.txt`）：
            //   ① **顺序 bug**：`'s` 变体原来排在泛规则**之后** ⇒ 对裸形态是**死规则**
            //      （泛规则先匹配，把 `X's bite` 整段当来源）⇒ `'s` 变体已提前。
            //   ② **终结符太窄**：原来只认 `\.$`，而环境伤害的来源短语常以 `!` 结尾
            //      —— 反编译实证：`WalltrapAcid::Flame` 把**裸字面量**
            //        `"from a plume of acid!"` 直传 `TakeDamage(…, Message, …)`
            //      （同族：`WalltrapFire::Flame` / `TrembleEarthquakes::RocksFall` /
            //        `PistonPressController::FireEvent` / `ModDefib::CheckDefib`）
            //      ⇒ 这批**整类漏掉**（扫描：`from `/`by ` 开头且含伤害关键词的 14 条里 5 条未覆盖）
            //      ⇒ 终结符放宽为 `[.!]`。
            //   ③ 那 5 条来源短语是**裸英文**（不经字符串表）⇒ 补 5 条具体规则（见下）。
            R(@"^\{\{r\|You take (\d+) damage from (?:(?:the )?(.+?))'s? (.+?)[.!]\}\}$",
              "{{r|你受到 $1 点来自$2的$3的伤害。}}"),
            R(@"^\{\{r\|You take (\d+) damage from (.+?)[.!]\}\}$", "{{r|你受到 $1 点来自$2的伤害。}}"),
            R(@"^You take (\d+) damage from (?:(?:the )?(.+?))'s? (.+?)[.!]$",
              "你受到 $1 点来自$2的$3的伤害。"),

            // ── 环境伤害来源短语（A4 长尾 · 每条都注明出处方法）──
            //   `ModDefib::CheckDefib`
            //   ★★ 形态说明（2026-10-15 自证实证）：匹配发生在
            //     `work = StripColorTags(s)` **之后** ⇒ 正则里**不能**写 `{{…}}`，
            //     要写剥壳后的 `an electrical shock delivered by …`。
            //     （同理：既有那四条 `\{\{r\|…` 规则在本路径上是**死规则** —— 另记待办。）
            R(@"^You take (\d+) damage from an electrical shock delivered by (.+?)[.!]$",
              "你受到$2释放的{{electrical|电击}}造成的 $1 点伤害。"),
            //   `WalltrapFire::Flame`
            R(@"^You take (\d+) damage from a jet of flames!$",
              "你受到{{fiery|烈焰喷流}}的 $1 点伤害！"),
            //   `WalltrapAcid::Flame`
            R(@"^You take (\d+) damage from a plume of acid!$", "你受到一股酸液的 $1 点伤害！"),
            //   `TrembleEarthquakes::RocksFall`
            //   ⚠ 尾随的 `{{W|(x2)}}` 在匹配前已被剥成 `(x2)` ⇒ `$2` 拿到的是剥壳后的文本
            R(@"^You take (\d+) damage from falling rocks! (.+)$", "你受到落石的 $1 点伤害！$2"),
            //   `PistonPressController::FireEvent`
            R(@"^You take (\d+) damage from being crushed by a machine press\.$",
              "你受到机器压床碾压的 $1 点伤害。"),

            R(@"^You take (\d+) damage from (.+?)[.!]$", "你受到 $1 点来自$2的伤害。"),

            // ── ★★ 2026-10-17（测试窗口 #47）：**第三人称**受伤 ──
            //   实机：`星章鱼 拿走 999 damage from 流血.`（原文 `X takes 999 damage from bleeding.`）
            //   根因：`ZhsVerbPatch` 把 take/takes 按「拿取」义统一译成「拿走」，
            //        而 `take damage` 是「受到伤害」⇒ **多义词不能靠单词表**，必须短语级规则。
            //   判据（§47）：先看搭配（takes + N/no + damage），再定义项。
            //   ⇒ 两条形态都收：① 英文原句  ② 动词已被换成「拿走」的半译句

            // ★ 2026-10-18（#53）：**先翻译来源** —— 下面 `N` 那两条会先接走「拿走」形态，
            //   如果把来源规则放在它们后面就永远轮不到（实机复现：`来自the fire`）。
            //   实机：`燃烧 Barathrumite 修补匠 拿走 1 damage from the fire!`
            R(@"^(.+?)\s*拿走\s*(\d+)\s*damage from (?:the )?(?:fire|flames)[.!]$",
              "$1受到 $2 点火焰伤害。"),
            R(@"^(.+?)\s*拿走\s*(\d+)\s*damage from (?:the )?bleeding[.!]$",
              "$1受到 $2 点流血伤害。"),
            R(@"^(.+?)\s*拿走\s*no\s*damage from (.+?)[.!]$",
              "$1未受到来自$2的伤害。"),
            R(@"^(.+?)\s*拿走\s*(\d+)\s*damage from (.+?)[.!]$",
              "$1受到 $2 点来自$3的伤害。"),

            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") takes? no damage from (.+?)[.!]$",
              "$1未受到来自$2的伤害。"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") takes? (\d+) damage from (.+?)[.!]$",
              "$1受到 $2 点来自$3的伤害。"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") 拿走 no damage from (.+?)[.!]$",
              "$1未受到来自$2的伤害。"),
            R(@"^(?:(?:The|the|A|a|An|an) )?(" + N + @") 拿走 (\d+) damage from (.+?)[.!]$",
              "$1受到 $2 点来自$3的伤害。"),

            // ★ 2026-10-18（#37）：`Chair::SitDown` 传的是**带介词的动词短语**
            //   `Actor.DidX("sit down on", Target)` ⇒ 动词表把 `sit down on` 弄成「坐下」，
            //   `on` 丢了 ⇒ `达迪坐下 轮椅。`。两条形态都收。
            R(@"^(.+?)\s*sits? down on (.+?)[.!]$", "$1坐到$2上。"),
            R(@"^(.+?)\s*坐下\s*(.+?)[.!]$", "$1坐到$2上。"),

            // ★ 2026-10-18（#9）：动态任务完成句 `You put <物品> 上 <目标> 在<方向>方向。`
            //   英文骨架 `put {item} on {target} {direction}` 直译 ⇒ 中文语序要重排。
            //   实机：`You put 指南针手链 上 湿的神性的桌子 在东方向。`
            R(@"^You put (.+?) 上 (.+?) (在.+?方向)[.!。！]$",
              "你$3的$2上放了$1。"),
            R(@"^You put (.+?) on (.+?) (to the .+?)[.!]$",
              "你在$3的$2上放了$1。"),

            // ★ 2026-10-18（#28 / #29）：`PassByVerb` 是**部件参数**（whoosh/whiz/zip…），
            //   不在动词表 ⇒ `whooshes past` / `whizzes past` 原样英文。
            //   实机：`一把 导弹 whooshes past 来自东北方向.`
            //         `You 闪身躲开, 因为 一把 铅弹 whizzes past 来自东南方向!`
            R(@"^You\s*闪身躲开[,，]\s*因为\s*(.+?)\s+(?:whizz|whoosh|zip)(?:es|s)?\s+past\s+(?:来自)?(.+?方向)[.!！]$",
              "你闪身躲开，因为$1从$2掠过！"),
            R(@"^(.+?)\s+(?:whizz|whoosh|zip)(?:es|s)?\s+past\s+(?:来自)?(.+?方向)[.!！]$",
              "$1从$2掠过。"),

            // ── ★★ 2026-10-18（#55 吟唱/历史菜单里的半译句）──
            //   `Your genome destabilized and 你 gained 腐蚀气体生成变异。`（前缀没翻）
            R(@"^Your genome destabilized and 你 gained (.+?)。?$",
              "你的基因组失稳了，你获得了$1。"),
            //   `你put something on了神性的桌子。`（动词短语没翻 + 时态「了」夹在中间）
            R(@"^你put something on了(.+?)。?$", "你在$1上放了些东西。"),
            //   `在尼月的18th, 你抵达了Narcheruk.`（序数后缀 + 半角逗号）
            R(@"^在(.+?)月的(\d+)(?:st|nd|rd|th), 你抵达了(.+?)[.!]$",
              "在$1月$2日，你抵达了$3。"),
            R(@"^你造访了(.+?)村。$", "你造访了$1村。"),

            // ★ 2026-10-18（#56）：`=included.andList=` 的槽值是**行动名键**（逻辑用），
            //   模板已中但槽值原样英文 ⇒ `使Melee行动的耗费减少15%。`
            //   常见四种单独收（数据侧键保持不变）。
            R(@"^(使|Provides )?Melee(行动的耗费减少)(.+?)(%?。?)$", "使近战行动的耗费减少$3$4"),
            R(@"^(使|Provides )?Throw(行动的耗费减少)(.+?)(%?。?)$", "使投掷行动的耗费减少$3$4"),
            R(@"^(使|Provides )?Movement(行动的耗费减少)(.+?)(%?。?)$", "使移动行动的耗费减少$3$4"),
            R(@"^(使|Provides )?Ranged(行动的耗费减少)(.+?)(%?。?)$", "使远程行动的耗费减少$3$4"),

            // ── ★ 2026-10-15（E47）：**成就文本**（`XRL.World.Effects.Proselytized::Apply`）
            //   现场是 `JournalAPI.AddAccomplishment("You convinced " + Object.an() + " to join your cause.", …)`
            //   ⇒ 拼接件，弹窗/消息栏出口都够不到 ⇒ 由 `ZhsJournalNotePatch` 新挂的
            //     `JournalAPI::AddAccomplishment` Prefix 调**本表**（走 `TranslateText`）来改。
            R(@"^You convinced (.+?) to join your cause\.$", "你说服了$1加入你的事业。"),

            // ══════════════════════════════════════════════════════════════
            // ★★ A1（2026-10-17）：**苏丹史编年史的壁画 / 福音**（arg1 / arg2）
            //
            //   现场：`XRL.World.ZoneManager::SetActiveZone` 里 15 处
            //         `JournalAPI.AddAccomplishment(arg0, arg1, arg2, …)`
            //     · arg0 = 成就行（本表已收，见上面 E47 那段）
            //     · arg1 = 壁画 mural · arg2 = 福音 gospel ← **本轮补齐**
            //   `ZhsJournalNotePatch.AccomplishmentPatch` 原来**只改 arg0**，
            //   现已扩到 arg1/arg2 ⇒ 那些长版编年史才能走到本表。
            //
            //   ★ 代词处理（关键）：福音里主语已由 `=name=` 交代，
            //     后面那个英文代词（`they`/`he`/`she`/`its`…）在中文里**冗余**
            //     ⇒ 用**非捕获组**吃掉 ⇒ **一条规则覆盖所有性别**。
            //     实证来源：`GetPronounProvider().Subjective/PossessiveAdjective`、
            //               `Grammar.RandomShePronoun()` ⇒ 由上游注入。
            // ══════════════════════════════════════════════════════════════
            R(@"^Done trekking through the root-strangled earth, =name= arrived in Kyakukya and was greeted by the village with warmth and reverence\. Upon leaving, =name= was named Friend to Oboroqoru\.$",
              "\u8D70\u5B8C\u76D8\u6839\u9519\u8282\u7684\u5927\u5730\uFF0C=name=\u62B5\u8FBE\u57FA\u4E9A\u5E93\u57FA\u4E9A\uFF0C\u53D7\u5230\u6751\u6C11\u6E29\u6696\u800C\u606D\u656C\u7684\u8FCE\u63A5\u3002\u79BB\u5F00\u65F6\uFF0C=name=\u88AB\u6388\u4E88\u300C\u5965\u535A\u7F57\u5E93\u9C81\u4E4B\u53CB\u300D\u4E4B\u540D\u3002"),
            R(@"^On an expedition down the River Svy, =name= was captured by bandits\. (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) languished in captivity for (.+?) years, eventually escaping to Kyakukya and befriending its mayor, the albino ape called Nuntu\.$",
              "\u6CBF\u65AF\u7EF4\u6CB3\u4E0B\u6E38\u8FDC\u884C\u65F6\uFF0C=name=\u88AB\u532A\u5E2E\u63B3\u53BB\uFF0C\u5728\u56DA\u7981\u4E2D\u82E6\u71AC$1\u5E74\uFF0C\u6700\u7EC8\u9003\u81F3\u57FA\u4E9A\u5E93\u57FA\u4E9A\uFF0C\u5E76\u4E0E\u90A3\u91CC\u7684\u6751\u957F\u2014\u2014\u767D\u5316\u733F\u52AA\u6069\u56FE\u7ED3\u4E3A\u53CB\u4F34\u3002"),
            R(@"^=name= journeyed to the Spindle at Omonporch, voiced a prayer for the Fossilized Saads, and observed a star fall along the planet's axis\.$",
              "=name=\u884C\u81F3\u5965\u8499\u6CE2\u5947\u7684\u7EBA\u9524\uFF0C\u4E3A\u300C\u5316\u77F3\u5316\u7684\u8428\u963F\u5FB7\u5BB6\u65CF\u300D\u8BF5\u7977\uFF0C\u5E76\u76EE\u7779\u4E00\u9897\u661F\u8FB0\u6CBF\u884C\u661F\u4E4B\u8F74\u5760\u843D\u3002"),
            R(@"^In =year=, =name= appointed the corrupt administrator Asphodel as earl and minister of Omonporch\. There (\w+) mandated the practice of (<spice\.elements\..+?\.practices\.!random>) in \S+ name\.$",
              "=year=\u5E74\uFF0C=name=\u4EFB\u547D\u8150\u8D25\u7684\u884C\u653F\u5B98\u963F\u65AF\u5F17\u5FB7\u5C14\u4E3A\u5965\u8499\u6CE2\u5947\u7684\u4F2F\u7235\u517C\u5927\u81E3\uFF0C\u5E76\u4EE5\u5176\u4E4B\u540D\u5F3A\u4EE4\u63A8\u884C$2\u3002"),
            R(@"^=name= trekked through the salt pans, north and west, to the merchant bazaar and grand cathedral of the Six Day Stilt\. There, the stiltfolk sang hymns in the sultan's honor\.$",
              "=name=\u5411\u5317\u3001\u5411\u897F\u7A7F\u8FC7\u76D0\u6CBC\uFF0C\u62B5\u8FBE\u516D\u65E5\u9AD8\u8DF7\u7684\u5546\u8D29\u96C6\u5E02\u4E0E\u5927\u6559\u5802\u3002\u9AD8\u8DF7\u4E4B\u6C11\u5728\u90A3\u91CC\u4E3A\u82CF\u4E39\u5531\u8D77\u9882\u6B4C\u3002"),
            R(@"^After trekking the vacant flats, =name= journeyed to the merchant bazaar and cathedral at the Six Day Stilt\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended the Mechanimists and threw an artifact down the Sacred Well\.$",
              "\u8D70\u8FC7\u7A7A\u5BC2\u7684\u5E73\u539F\u540E\uFF0C=name=\u6765\u5230\u516D\u65E5\u9AD8\u8DF7\u7684\u5546\u8D29\u96C6\u5E02\u4E0E\u5927\u6559\u5802\uFF0C\u5728\u90A3\u91CC\u7ED3\u4EA4\u4E86\u673A\u68B0\u6559\u6D3E\uFF0C\u5E76\u628A\u4E00\u4EF6\u53E4\u7269\u6295\u5165\u5723\u4E95\u3002"),
            R(@"^=name= hiked the salted marshes and came upon the ancient gathering place called Red Rock, where a bevy of snapjaws cooked the sultan a satisfying meal\.$",
              "=name=\u8DCB\u6D89\u76D0\u6CBC\uFF0C\u6765\u5230\u540D\u4E3A\u7EA2\u5CA9\u7684\u53E4\u8001\u805A\u5730\uFF0C\u4E00\u7FA4\u54AC\u989A\u517D\u5728\u90A3\u91CC\u4E3A\u82CF\u4E39\u70F9\u4E86\u4E00\u987F\u9971\u9910\u3002"),
            R(@"^Near the location of Red Rock, =name= was captured by baboons\. (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) murdered their leader (<spice\.elements\..+?\.murdermethods\.!random>) and from then on wore a neck ring stained with baboon blood\.$",
              "\u5728\u7EA2\u5CA9\u9644\u8FD1\uFF0C=name=\u88AB\u72D2\u72D2\u63B3\u53BB\uFF0C\u4EE5$1\u6740\u4E86\u5B83\u4EEC\u7684\u9996\u9886\uFF0C\u6B64\u540E\u4FBF\u6234\u7740\u4E00\u679A\u67D3\u6709\u72D2\u72D2\u8840\u7684\u9888\u73AF\u3002"),
            R(@"^To commemorate the imperial conquests of =name=, a triumphal arch was erected across the fruiting gorges\.$",
              "\u4E3A\u7EAA\u5FF5=name=\u7684\u5E1D\u738B\u529F\u4E1A\uFF0C\u4E00\u5EA7\u51EF\u65CB\u62F1\u95E8\u6A2A\u8DE8\u4E8E\u7ED3\u6EE1\u679C\u5B9E\u7684\u5CE1\u8C37\u4E4B\u4E0A\u3002"),
            R(@"^At (<spice\.time\.partsOfDay\.!random>) under (<spice\.commonPhrases\.strange\.!random\.article>) and rusted sky, the people of the desert canyon saw an image on the horizon that looked like a (.+?) under an archway\. It was =name=, and after (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) came and left, the people built a monument to =name= and thenceforth called (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) the Underarch (.+?)\.$",
              "$1\uFF0C\u5728$2\u4E0E\u9508\u8272\u5929\u7A7A\u4E4B\u4E0B\uFF0C\u6C99\u6F20\u5CE1\u8C37\u7684\u5C45\u6C11\u671B\u89C1\u5730\u5E73\u7EBF\u4E0A\u6709\u4E2A\u5F62\u5F71\uFF0C\u50CF\u662F\u62F1\u95E8\u4E4B\u4E0B\u7684$3\u3002\u90A3\u6B63\u662F=name=\uFF1B\u5728\u5176\u6765\u53BB\u4E4B\u540E\uFF0C\u4EBA\u4EEC\u4E3A=name=\u7ACB\u8D77\u7EAA\u5FF5\u7891\uFF0C\u81EA\u6B64\u79F0\u5176\u4E3A\u300C\u62F1\u4E0B$4\u300D\u3002"),
            R(@"^Hillfolk dug wells deep into the earth and drew spring water to honor the sultan =name= in sacred ritual\.$",
              "\u5C71\u5730\u4E4B\u6C11\u6398\u4E95\u76F4\u5165\u5730\u5E95\uFF0C\u6C72\u53D6\u6CC9\u6C34\uFF0C\u4EE5\u795E\u5723\u4EEA\u5F0F\u656C\u5949\u82CF\u4E39=name=\u3002"),
            R(@"^While journeying across the wavegraph hills, =name= discovered the rust wells\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended water barons and sipped from the red iron spring\.$",
              "\u7A7F\u884C\u4E8E\u6CE2\u56FE\u4E18\u9675\u65F6\uFF0C=name=\u53D1\u73B0\u4E86\u9508\u4E95\uFF0C\u5728\u90A3\u91CC\u4E0E\u6C34\u4E1A\u5DE8\u5934\u7ED3\u4EA4\uFF0C\u5E76\u555C\u996E\u4E86\u7EA2\u94C1\u4E4B\u6CC9\u3002"),
            R(@"^The scholar-sultan =name= visited Grit Gate and lectured the monastic order there on the subject of light refraction\.$",
              "\u5B66\u8005\u82CF\u4E39=name=\u9020\u8BBF\u7802\u783E\u95E8\uFF0C\u4E3A\u90A3\u91CC\u7684\u4FEE\u58EB\u56E2\u4F53\u8BB2\u6388\u5149\u7684\u6298\u5C04\u3002"),
            R(@"^While delving deep through moss-skirted steeples of chrome, =name= discovered the ancient Grit Gate\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended the Barathrumites and lectured them on light refraction\.$",
              "\u6DF1\u5165\u82D4\u8986\u7684\u94EC\u8272\u5C16\u5854\u4E4B\u95F4\u65F6\uFF0C=name=\u53D1\u73B0\u4E86\u53E4\u8001\u7684\u7802\u783E\u95E8\uFF0C\u5728\u90A3\u91CC\u4E0E\u5DF4\u62C9\u65AF\u9C81\u8FC8\u7279\u4EBA\u7ED3\u4EA4\uFF0C\u5E76\u5411\u4ED6\u4EEC\u8BB2\u6388\u5149\u7684\u6298\u5C04\u3002"),
            R(@"^In the month of (.+?) of (.+?) AR, =name= ascended the trash chutes of Golgotha, victorious and bathed in slime\.$",
              "AR $2 \u5E74$1\uFF0C=name=\u6500\u4E0A\u5404\u5404\u4ED6\u7684\u5783\u573E\u6ED1\u9053\uFF0C\u5F97\u80DC\u800C\u5F52\uFF0C\u6D51\u8EAB\u6D74\u6EE1\u9ECF\u6DB2\u3002"),
            R(@"^One auspicious day in the jungle, =name= descended the trash chutes of Golgotha and bathed in viscous slime\. From that day forth, (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) always kept some wet trash on (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) person\.$",
              "\u4E1B\u6797\u4E2D\u7684\u67D0\u4E2A\u5409\u65E5\uFF0C=name=\u987A\u5404\u5404\u4ED6\u7684\u5783\u573E\u6ED1\u9053\u800C\u4E0B\uFF0C\u5728\u9ECF\u7A20\u7684\u6C61\u6CE5\u4E2D\u6C90\u6D74\u3002\u81EA\u90A3\u65E5\u8D77\uFF0C\u5176\u8EAB\u4E0A\u603B\u5E26\u7740\u4E9B\u6E7F\u5783\u573E\u3002"),
            R(@"^=name= trekked to the frozen sauna at Bethesda Susa and soaked in the company of trolls\.$",
              "=name=\u8DCB\u6D89\u81F3\u8D1D\u585E\u65AF\u8FBE\u00B7\u82CF\u8428\u7684\u51B0\u51BB\u6851\u62FF\uFF0C\u4E0E\u5DE8\u9B54\u4E3A\u4F34\u5171\u6D74\u3002"),
            R(@"^In =year=, =name= appointed a corrupt administrator to the frozen sauna at Bethesda Susa\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) mandated association with trolls in \S+ name\.$",
              "=year=\u5E74\uFF0C=name=\u4E3A\u8D1D\u585E\u65AF\u8FBE\u00B7\u82CF\u8428\u7684\u51B0\u51BB\u6851\u62FF\u4EFB\u547D\u4E86\u4E00\u4F4D\u8150\u8D25\u7684\u884C\u653F\u5B98\uFF0C\u5E76\u4EE5\u5176\u4E4B\u540D\u5F3A\u4EE4\u4E0E\u5DE8\u9B54\u5F80\u6765\u3002"),
            R(@"^In the month of (.+?) of (.+?) AR, =name= trekked to the Tomb of the Eaters and traced a sigil across the Death Gate\. O wise sultan!$",
              "AR $2 \u5E74$1\uFF0C=name=\u8DCB\u6D89\u81F3\u98DF\u8005\u4E4B\u5893\uFF0C\u5728\u6B7B\u4EA1\u4E4B\u95E8\u4E0A\u63CF\u4E0B\u4E00\u9053\u5370\u8BB0\u3002\u5662\uFF0C\u777F\u667A\u7684\u82CF\u4E39\uFF01"),
            R(@"^While traveling through the banana grove, =name= trekked to the Tomb of the Eaters and dreamed they emerged at the Court of Sultans and crossed the Death Gate\. From that day forth, (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) always kept the Mark of Death on (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) person\.$",
              "\u7A7F\u8FC7\u9999\u8549\u6797\u65F6\uFF0C=name=\u8DCB\u6D89\u81F3\u98DF\u8005\u4E4B\u5893\uFF0C\u68A6\u89C1\u81EA\u5DF1\u73B0\u8EAB\u4E8E\u82CF\u4E39\u4E4B\u5EAD\u5E76\u8DE8\u8FC7\u6B7B\u4EA1\u4E4B\u95E8\u3002\u81EA\u90A3\u65E5\u8D77\uFF0C\u5176\u8EAB\u4E0A\u603B\u5E26\u7740\u6B7B\u4EA1\u5370\u8BB0\u3002"),
            R(@"^Strong =name= trekked to the asphalt mines to breathe the tarry vapor and bathe in the black blood of the earth\.$",
              "\u5F3A\u5065\u7684=name=\u8DCB\u6D89\u81F3\u6CA5\u9752\u77FF\uFF0C\u547C\u5438\u7126\u6CB9\u6C14\uFF0C\u6C90\u6D74\u4E8E\u5927\u5730\u7684\u9ED1\u8840\u4E4B\u4E2D\u3002"),
            R(@"^Sometime in =year=, =name= wandered over the high mounts and voyaged to the Asphalt Mines\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended no one and instead bathed in the black blood of the earth\.$",
              "=year=\u5E74\u67D0\u65F6\uFF0C=name=\u7FFB\u8D8A\u9AD8\u5C71\uFF0C\u8FDC\u822A\u81F3\u6CA5\u9752\u77FF\uFF1B\u5728\u90A3\u91CC\u8C01\u4E5F\u672A\u7ED3\u4EA4\uFF0C\u53EA\u5728\u9ED1\u8840\u822C\u7684\u5927\u5730\u4E4B\u6D41\u4E2D\u6C90\u6D74\u3002"),
            R(@"^Strong =name= trekked to the City of Bones to breathe the tarry vapor and bathe in the black blood of the earth\.$",
              "\u5F3A\u5065\u7684=name=\u8DCB\u6D89\u81F3\u9AB8\u9AA8\u4E4B\u57CE\uFF0C\u547C\u5438\u7126\u6CB9\u6C14\uFF0C\u6C90\u6D74\u4E8E\u5927\u5730\u7684\u9ED1\u8840\u4E4B\u4E2D\u3002"),
            R(@"^Sometime in =year=, =name= wandered over the high mounts and voyaged to the City of Bones in the Asphalt Mines\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended no one and instead bathed in the black blood of the earth\.$",
              "=year=\u5E74\u67D0\u65F6\uFF0C=name=\u7FFB\u8D8A\u9AD8\u5C71\uFF0C\u8FDC\u822A\u81F3\u6CA5\u9752\u77FF\u4E2D\u7684\u9AB8\u9AA8\u4E4B\u57CE\uFF1B\u5728\u90A3\u91CC\u8C01\u4E5F\u672A\u7ED3\u4EA4\uFF0C\u53EA\u5728\u9ED1\u8840\u822C\u7684\u5927\u5730\u4E4B\u6D41\u4E2D\u6C90\u6D74\u3002"),
            R(@"^Strong =name= trekked to the Great Asphalt Sea to breathe the tarry vapor and bathe in the black blood of the earth\.$",
              "\u5F3A\u5065\u7684=name=\u8DCB\u6D89\u81F3\u5927\u6CA5\u9752\u6D77\uFF0C\u547C\u5438\u7126\u6CB9\u6C14\uFF0C\u6C90\u6D74\u4E8E\u5927\u5730\u7684\u9ED1\u8840\u4E4B\u4E2D\u3002"),
            R(@"^Sometime in =year=, =name= wandered over the high mounts and voyaged to the Great Sea in the Asphalt Mines\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended no one and instead bathed in the black blood of the earth\.$",
              "=year=\u5E74\u67D0\u65F6\uFF0C=name=\u7FFB\u8D8A\u9AD8\u5C71\uFF0C\u8FDC\u822A\u81F3\u6CA5\u9752\u77FF\u4E2D\u7684\u5927\u6CA5\u9752\u6D77\uFF1B\u5728\u90A3\u91CC\u8C01\u4E5F\u672A\u7ED3\u4EA4\uFF0C\u53EA\u5728\u9ED1\u8840\u822C\u7684\u5927\u5730\u4E4B\u6D41\u4E2D\u6C90\u6D74\u3002"),
            R(@"^Strong =name= trekked to the Tunnels of Ur to breathe the tarry vapor and bathe in the black blood of the earth\.$",
              "\u5F3A\u5065\u7684=name=\u8DCB\u6D89\u81F3\u4E4C\u5C14\u96A7\u9053\uFF0C\u547C\u5438\u7126\u6CB9\u6C14\uFF0C\u6C90\u6D74\u4E8E\u5927\u5730\u7684\u9ED1\u8840\u4E4B\u4E2D\u3002"),
            R(@"^Sometime in =year=, =name= wandered over the high mounts and voyaged to the Tunnels of Ur in the Asphalt Mines\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended no one and instead bathed in the black blood of the earth\.$",
              "=year=\u5E74\u67D0\u65F6\uFF0C=name=\u7FFB\u8D8A\u9AD8\u5C71\uFF0C\u8FDC\u822A\u81F3\u6CA5\u9752\u77FF\u4E2D\u7684\u4E4C\u5C14\u96A7\u9053\uFF1B\u5728\u90A3\u91CC\u8C01\u4E5F\u672A\u7ED3\u4EA4\uFF0C\u53EA\u5728\u9ED1\u8840\u822C\u7684\u5927\u5730\u4E4B\u6D41\u4E2D\u6C90\u6D74\u3002"),
            R(@"^Strong =name= trekked to the Swilling Vast to breathe the tarry vapor and bathe in the black blood of the earth\.$",
              "\u5F3A\u5065\u7684=name=\u8DCB\u6D89\u81F3\u7545\u996E\u8352\u539F\uFF0C\u547C\u5438\u7126\u6CB9\u6C14\uFF0C\u6C90\u6D74\u4E8E\u5927\u5730\u7684\u9ED1\u8840\u4E4B\u4E2D\u3002"),
            R(@"^Sometime in =year=, =name= wandered over the high mounts and voyaged to the Swilling Vast in the Asphalt Mines\. There (?:[Tt]hey|[Hh]e|[Ss]he|[Xx]e|[Zz]e|[Ii]t|[Ee]y|[Tt]hey're|[Hh]e's|theirs?|[Tt]heirs?|[Hh]is|[Hh]er|[Ii]ts|[Tt]heir|them|him|us|me) befriended no one and instead bathed in the black blood of the earth\.$",
              "=year=\u5E74\u67D0\u65F6\uFF0C=name=\u7FFB\u8D8A\u9AD8\u5C71\uFF0C\u8FDC\u822A\u81F3\u6CA5\u9752\u77FF\u4E2D\u7684\u7545\u996E\u8352\u539F\uFF1B\u5728\u90A3\u91CC\u8C01\u4E5F\u672A\u7ED3\u4EA4\uFF0C\u53EA\u5728\u9ED1\u8840\u822C\u7684\u5927\u5730\u4E4B\u6D41\u4E2D\u6C90\u6D74\u3002"),


            // ── ★★ A1 通用外壳（兜底）—— 2026-10-17 从文件前部**移到这里** ──
            //
            //   `PsychicGlimmer::Update` / `Book::Hand` 的成就文本：
            //     `"In the month of " + Calendar.GetMonth() + " of " + Calendar.GetYear() + ", =name= …"`
            //   ★ 月份**走串表**且已汉化：`Calendar.GetMonth()` 内部就是
            //     `Strings._S("Calendar Month", "Iyur Ut")`（我方 13 条齐全）
            //     ⇒ `$1` 是「珥月」这类中文，**不要**再自己加「月」字
            //   ★★ 为什么放这里（而不是文件前部）：它若排在**专用规则之前**会抢先命中，
            //     把地点 8/10 的日期句正文当 `$3` 原样透传 ⇒ **正文永远留英文**。
            //     §44「通用规则抢先」的标准修法：专用在前、通用兜底。
            R(@"^In the month of (.+?) of (.+?), (.+)$",
              "$2的$1，$3"),

            // ── ★ 2026-10-15：**「字面前缀 + 变量」谓词**（反编译定的两处现场）──
            //   ① `XRL.World.Parts.Mutation.Metamorphosis::FireEvent`
            //        `DidX("assume", "the form of " + item.a + item.ShortDisplayName, …)`
            //   ② `XRL.World.Effects.Prone::Apply`
            //        `DidXToY("lie", "down on", LyingOn, …)`（另一分支 `DidX("lie","down")` 本已可匹配）
            //   ⇒ 谓词是「字面前缀 + 变量」⇒ `E` 表（精确键）**天生匹配不上** ⇒ 用前缀规则
            R(@"^You assume the form of (?:a|an|the) (.+?)\.?$", "你化作了$1的形态。"),
            R(@"^(.+?) assume the form of (?:a|an|the) (.+?)\.?$", "$1化作了$2的形态。"),
            R(@"^You lie down on (.+?)\.?$", "你躺在了$1上。"),
            R(@"^(.+?) lie down on (.+?)\.?$", "$1躺在了$2上。"),

            // ── ★ 2026-10-16：**站点 #2**（`Mutation.ElectricalGeneration::DischargeMessage`）──
            //   反编译实证：`DidX("discharge", "units of electrical charge", "!")`
            //     ⇒ 谓词是**字面量**，但运行时前面插**数量** ⇒ `E` 表（精确键）匹配不上
            //     （另有一条模板路径 `_T("Elec Gen Discharge Message", "=subject.Does:discharge= …")`，
            //       走 `GameObjectReplacers.Does` ⇒ `ZhsVerbPatch` 已挂该出口 ✓）
            //   ★ 玩家形必须**显式写**：`ZhPlayerPronouns` 只认小写 `you`，
            //     大写 `You` 开头不会被它兜住（同 assume/lie 那批的教训）
            R(@"^You discharge (\d+) units of electrical charge(!?)$", "你释放了 $1 单位电荷！"),
            R(@"^You discharge units of electrical charge(!?)$", "你释放了电荷！"),
            R(@"^(.+?) discharge (\d+) units of electrical charge(!?)$", "$1释放了 $2 单位电荷！"),
            R(@"^(.+?) discharge units of electrical charge(!?)$", "$1释放了电荷！"),

            // ── ★★ 2026-10-17（测试窗口 #58）：`PluckablePolyp::Pluck` 的**复合句** ──
            //   实机：`You pluck a coral polyp off the strut and把它扔到一边。`
            //   产地：
            //     `XDidY(Actor, "pluck", "a coral polyp off the strut" + 方向
            //            + " and" + Actor.GetVerb("toss") + " it aside")`
            //   根因：下面那条通用 `toss … aside` 把**整段前缀**当 `$1`
            //     ⇒ 前半英文原样、`and` 与「把」直接粘连。
            //   ⇒ 专用整句必须**排在通用 toss 规则之前**（具体优先）。
            R(@"^You pluck (.+?) off the strut(?:,? (.+?))? and toss(?:es)? it aside\.?$",
              "你从支柱上摘下$1$2，把它扔到一边。"),
            R(@"^" + ART + @"(" + N + @") plucks (.+?) off the strut(?:,? (.+?))? and tosses it aside\.?$",
              "$1从支柱上摘下$2$3，把它扔到一边。"),

            // ── ★ 2026-10-16：`toss` 的固定副词 **aside**（`PaxKlanqMadness::TakeAction`）──
            //   `DidXToY("toss", equipped, "aside")` ⇒ 谓词 = 物件（变量）+ 固定副词 `aside`
            //   ⇒ 动词中文由 `ZhsVerbZh.V`（toss⇒扔）补，副词 `aside` 由本条吃掉
            //   ★ 2026-10-17（#58）：`(.+?)` 收紧为**不跨 ` and `** —— 否则复合句又被整段吞
            R(@"^((?:(?! and ).)+?) toss(?:es)? (.+?) aside\.?$", "$1把$2扔到一边。"),

            // ── 死亡 ──
            R(@"^" + ART + @"(" + N + @") dies!$",
              "$1死了！"),                                                  // X死了！
            R(@"^You die!$", "你死了！"),
        
            // ★★ 2.16.7：`LeaksFluid` 的泄漏消息（实机采集 `_zhs_msgtrace.log`）
            //
            //   反编译 `XRL.World.Parts.LeaksFluid`：
            //     `.ctor()`            ⇒ `MessageVerb = "leak";`
            //     `DistributeLiquid()` ⇒
            //         _T("LeaksFluid TellLeakAmount",
            //            "=subject.Does#verb= =liquid.drams.of.liquid#volume=.")
            //           .SetArgument("verb", MessageVerb)
            //           .EmitMessage()
            //           .InlineLocalizationMatch(ParentObject.Does(MessageVerb) + " "
            //               + produced.Volume + " " + text + " of "
            //               + produced.GetLiquidName() + ".", "LeaksFluid TellLeakAmount");
            //   ⇒ **槽参数是英文动词**（`SetArgument("verb", "leak")`）
            //     ⇒ 与 §46 第 ⑦ 道门「槽里的默认值绕过译表」同族
            //   ⇒ 实机症状：`多汁的 库祖开始 leaking!`（动词原样进正文）
            // ⚠ 2.16.8 修两处（离线验证抓到）：
            //   ① 正则要**可选消费**中文的「开始/正在/正」，否则产出 `开始开始泄漏`
            //   ② `dram`/`drams` 要**进捕获组并译成「德拉姆」**，否则残留英文单位
            // ⚠ 2.16.13：按**捕获收支校验**（学 DFCN `indexed()`）修 ——
            //   单位 `dram(s)` / `德拉姆` 原本**被捕获却不消费**（译文硬写「德拉姆」）
            //   ⇒ 改为**非捕获** `(?:drams?|德拉姆)`，只留「数量」与「液体」两个真捕获组。
            //   并补**单数**分支（原文 `1 dram` 是单数形态，必须能命中）。
            R(@"^" + ART + @"(" + N + @")\s*(?:开始|正在|正)?\s*leaks? ([\d.]+) (?:drams?|德拉姆) of (" + N + @")\.$",
              "$1泄漏了 $2 德拉姆$3。"),
            R(@"^" + ART + @"(" + N + @")\s*(?:开始|正在|正)?\s*leaks? ([\d.]+) (?:drams?|德拉姆)\.$",
              "$1泄漏了 $2 德拉姆。"),
            // 兜底：`X 开始 leaking!` 形态（实机采集 `_zhs_msgtrace.log`）
            R(@"^" + ART + @"(" + N + @")\s*(?:开始|正在|正)?\s*leaking!$", "$1开始泄漏！"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.11：硬编码「片段」→ **后缀型句式规则**（第一批）
            //
            //   全量普查的 569 条片段里，**绝大多数是以空格结尾的前缀**：
            //       `You can't budge ` + 肿胀的水蛭 ⇒ `You can't budge 肿胀的水蛭.`
            //   ⇒ 正确做法不是「片段表」，而是**接住完整消息的后缀型规则**：
            //       `^You can't budge (.+)\.$` ⇒ `你推不动$1。`
            //     `(.+)` 天然吃下中英混排的专名（专名已由我方名字补丁译成中文）。
            //   ⚠ 规则由粗到细排列：**长的/更具体的在前**，避免短规则先吃掉。
            // ═══════════════════════════════════════════════════════════
            R(@"^You can\'t budge (" + N + @")\.$",
              "你推不动$1。"),
            R(@"^You can\'t budge (" + N + @")!$",
              "你推不动$1！"),
            R(@"^You can\'t auto-equip (" + N + @")\.$",
              "你无法自动装备$1。"),
            R(@"^You have no ammunition to supply (" + N + @")\.$",
              "你没有可供$1使用的弹药。"),
            R(@"^You learn to cook (" + N + @")\.$",
              "你学会了烹饪$1。"),
            R(@"^You learn the skill (" + N + @")\.$",
              "你学会了技能$1。"),
            R(@"^You gained the mutation (" + N + @")\.$",
              "你获得了突变$1。"),
            R(@"^You gained the defect (" + N + @")\.$",
              "你获得了缺陷$1。"),
            R(@"^You can\'t gain (" + N + @")\.$",
              "你无法获得$1。"),
            R(@"^You gained (" + N + @")\.$",
              "你获得了$1。"),
            R(@"^You started calling (" + N + @")\.$",
              "你开始称其为$1。"),
            R(@"^You select the name \'(" + N + @")\'\.$",
              "你选择了名字「$1」。"),
            // ★★ 2026-10-17（测试窗口 #42 / #43 / #44）：`ItemNaming` 的**两段硬编码**
            //   产地（`XRL.World.Capabilities.ItemNaming`）：
            //     `Opportunity`: `"You swell with the inspiration to name your " + 名 + ". Do you wish to?"`
            //     `NameItem`  : `"You name " + 名 + " '" + 新名 + "'."`（另有 `"You named "` 过去式）
            //   实机：`You swell with the inspiration to name 你的 ♂ 杰作带瞄准镜卡宾枪. Do you wish to?`
            //         `You name 你的 ♂ 杰作带瞄准镜卡宾枪 'Yishroyuraq'.`
            //   ① 旧规则要求物品名后**就是句号**⇒ 带 ` Do you wish to?` 的整串失配
            //   ② `your` 已被词表译成「你的」⇒ 两种都要吃
            //   ③ 名字开头的 `♂` 是 **masterwork/元素标记**（不是性别，用户 2026-10-17 纠正）
            //      ⇒ 用 MARK **通配剥掉**（不写死 `♂`，同类符号还有别的）
            R(@"^You swell with the inspiration to name (?:your |你的 )?" + MARK
              + @"(.+?)\. Do you wish to\?$",
              "你涌起为自己的$1命名的灵感。要这么做吗？"),
            R(@"^You name (?:your |你的 )?" + MARK + @"(.+?) '(.+)'\.$",
              "你为$1取名「$2」。"),
            R(@"^You named (?:your |你的 )?" + MARK + @"(.+?) '(.+)'\.$",
              "你为$1取了名「$2」。"),
            R(@"^You swell with the inspiration to name (?:your |你的 )?" + MARK + @"(" + N + @")\.$",
              "你涌起为自己的$1命名的灵感。"),
            R(@"^You gently shake (" + N + @")\.$",
              "你轻轻摇晃$1。"),
            R(@"^You became loved among (" + N + @")\.$",
              "你在$1中深受爱戴。"),
            R(@"^Your worship of (" + N + @")\.$",
              "你对$1的崇拜。"),
            R(@"^Your blasphemy against (" + N + @")\.$",
              "你对$1的亵渎。"),
            R(@"^You are promoted to the (" + N + @")\.$",
              "你被提升为$1。"),
            // ═══════════════════════════════════════════════════════════
            // ★★ 2026-10-15：水之仪式**弹窗**整句（用户截图 · 半译）
            //
            // 【截图】`你分享了自己的水 with Warden 亚拉梅 and begin the water ritual。`
            // 【反编译出处】`XRL.World.Conversations.Parts.WaterRitual::PerformRitual` ——
            //     Popup.Show("You share your " + LiquidName + " with "
            //         + The.Speaker.GetReferenceDisplayName(int.MaxValue, …, WithIndefiniteArticle: true)
            //         + " and begin the water ritual.");
            //   ⇒ 骨架两段是 **C# 硬编码字面量**（普查 owner 实证：
            //     `XRL.World.Conversations.Parts.WaterRitual PerformRitual  You share your ` /
            //      `…PerformRitual  and begin the water ritual.`）
            //   ⇒ 字符串表 / XML **改不到**；`LiquidName`（=「自己的水」）与名字来自数据侧。
            // 【本局实证】`Player.log` L5967：
            //     （弹窗/日志）You share your 水 with Warden 亚拉梅 and begin the water ritual.
            //                  =>  你分享了自己的水 with Warden 亚拉梅 and begin the water ritual。
            //   ⇒ 即本规则**未加之前**，`TranslateText` 走的是 `^You share your (.+)\.$` 那条
            //     近邻规则 ⇒ 只吃到句末英文句点，中间英文原样漏出。
            // 【写法要点】非贪婪 `(.+?)` + **显式消费** ` and begin the water ritual.`
            //   —— 与 2.16.26 的教训同源（`(.+)` 会把英文尾巴整个吞进捕获组 ⇒ 产出半译）。
            // ⚠ 名字段可能含「英文头衔 + 中文名」（`Warden 亚拉梅`）⇒ 用 `(.+?)` 原样带过，
            //   头衔汉化是**另一条线**（见 KNOWN-ISSUES 同名条目）。
            // ═══════════════════════════════════════════════════════════
            R(@"^You share your (.+?) with (.+?) and begin the water ritual\.$",
              "你与$2分享了$1，水之仪式就此开始。"),

            // ★★ 2026-10-17：**离线测试台**（`_tools\rulecheck`）整句残留审计抓到的两条真漏。
            //   取证：`_tools\_frag_worklist.tsv` 的 owners 栏
            //     · `Alarms blare across the enclave.` ← `AddPlayerMessage`
            //       （`XRL.UI.GritGateTerminalScreenMessage.<.ctor>b__2_0`）
            //     · `I don't have a target!` ← `DidXToY`
            //       （`XRL.World.AI.GoalHandlers.PlaceTurretGoal.TakeAction`）
            //   ⇒ 两条都是玩家可见、且都走强出口 ⇒ 直接在规则层收口。
            //   ★ 这正是"测试台比人肉快"的样本：没进游戏就报出来了。
            R(@"^Alarms blare across the enclave\.$",
              "警报响彻整座围场。"),
            R(@"^I don't have a target!$",
              "我没有目标！"),


            R(@"^You share your (" + N + @")\.$",
              "你分享了自己的$1。"),
            R(@"^You cannot give (" + N + @")\.$",
              "你无法给出$1。"),
            R(@"^You do not have enough (" + N + @")\.$",
              "你的$1不足。"),
            R(@"^You are stopped short by (" + N + @")\.$",
              "你被$1拦住了。"),
            R(@"^Your regenerative metabolism cures you of (" + N + @")\.$",
              "你的再生代谢治愈了你的$1。"),
            R(@"^You are fleeing from (" + N + @")\.$",
              "你正在逃离$1。"),
            R(@"^You are too far from (" + N + @")\.$",
              "你离$1太远了。"),
            R(@"^You are crippled for (" + N + @")\.$",
              "你瘫痪$1。"),
            R(@"^Your path would take you into (" + N + @")\.$",
              "你的路线会把你带入$1。"),
            R(@"^You have failed the step, (" + N + @")\.$",
              "你未能完成该步骤：$1。"),
            R(@"^You toggle (" + N + @")\.$",
              "你切换了$1。"),
            R(@"^You thirst at half rate\.$",
              "你的口渴速度减半。"),
            R(@"^You muse over the secret\.?$",
              "你思索着那个秘密。"),
            R(@"^You exploded\.$",
              "你爆炸了。"),
            R(@"^You die! \(good job\)$",
              "你死了！（干得好）"),
            R(@"^You ended the game\.$",
              "你结束了这局游戏。"),
            R(@"^You fall to the ground!$",
              "你摔倒在地！"),
            R(@"^You feel feverish\.$",
              "你感到发热。"),
            R(@"^You feel less feverish\.$",
              "你的发热感减轻了。"),
            R(@"^You can\'t be mutated\.$",
              "你无法被突变。"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.12：硬编码「片段」→ 后缀型规则（第二批 60 条）
            //
            //   与 2.16.11 同法：片段是**以空格结尾的前缀**，
            //   游戏把名字/数量拼在后面 ⇒ 用 `^<前缀>(.+)\.$` 接住完整消息。
            //   ⚠ 最后那批**通用模板**（`^You feel (.+)\.$` 之类）风险高
            //     ——可能误吃已中文的句子 —— ⇒ **不启用**，只留注释备查。
            // ═══════════════════════════════════════════════════════════
            // ═══════════════════════════════════════════════════════════
            // ★★ 2026-10-18（#67 夹具）：**必须排在通用 `^You are now (.+)\.$` 之前**
            //   否则通用规则会把 `despised by X` 当变量搬 ⇒ `你现在是liked by 约帕。`
            R(@"^You are now despised by (.+?)\.$", "你如今被$1鄙视。"),
            R(@"^You are now liked by (.+?)\.$", "你如今被$1喜爱。"),
            R(@"^You are now (.+)\.$",
              "你现在是$1。"),
            R(@"^You woke from a long, convincing dream and returned to your life as the (.+)\.$",
              "你从一场漫长而逼真的梦中醒来，回到你作为$1的生活。"),
            R(@"^You cannot do that while engulfed by (.+)\.$",
              "被$1吞没时无法这么做。"),
            R(@"^You\'re going to collapse from exhaustion in (.+)\.$",
              "你将在$1后因力竭而倒下。"),
            R(@"^You cannot fly while engulfing (.+)\.$",
              "正在吞没$1时你无法飞行。"),
            R(@"^You fail to engulf (.+)\.$",
              "你未能吞没$1。"),
            R(@"^Your larva gestated and you gained the (.+)\.$",
              "你的幼虫发育成熟，你获得了$1。"),
            R(@"^Your attack bounces harmlessly off of (.+)\.$",
              "你的攻击被$1无害地弹开了。"),
            // ★★ 2.16.123：`HologramInvulnerability`（全息影像免疫）—— 用户截图实证
            //   C# 直拼（反编译 `XRL.World.Parts.HologramInvulnerability::HandleEvent`）：
            //     AddPlayerMessage(E.Actor.Poss("attack") + " passes harmlessly through "
            //                      + E.Object.t() + ".", color);
            //   ⇒ `Poss("attack")` 走我方 `ZhsPossessivePatch` ⇒ 中文名得到「…的」，
            //     纯英文名仍是 `'s` ⇒ 两种都要认。
            R(@"^Your attack passes harmlessly through (.+)\.$",
              "你的攻击无害地穿过了$1。"),
            R(@"^(.+?)(?:的|'s) attack passes harmlessly through (.+?)\.$",
              "$1的攻击无害地穿过了$2。"),
            // 同族：`XRL.World.Effects.Stasis::HandleEvent` 的第三人称形（玩家形上面已有）
            R(@"^(.+?)(?:的|'s) attack bounces harmlessly off of (.+?)\.$",
              "$1的攻击被$2无害地弹开了。"),
            R(@"^Your mental attack does not affect (.+)\.$",
              "你的精神攻击对$1无效。"),
            R(@"^You can dimly sense (.+)\.$",
              "你能隐约感知到$1。"),
            R(@"^You convinced yourself (.+)\.$",
              "你说服了自己$1。"),
            R(@"^You were consumed whole by (.+)\.$",
              "你被$1整个吞下。"),
            R(@"^Your onboard systems are out of (.+)\.$",
              "你的舰载系统$1不足。"),
            R(@"^You cannot make contact with (.+)\.$",
              "你无法与$1建立联系。"),
            R(@"^You cannot clone (.+)\.$",
              "你无法克隆$1。"),
            R(@"^You fail to clone (.+)\.$",
              "你未能克隆$1。"),
            R(@"^You witnessed the rare formation of (.+)\.$",
              "你见证了$1的罕见形成。"),
            R(@"^You encode the psyche of (.+)\.$",
              "你编码了$1的灵能。"),
            R(@"^You pause as the psyche of (.+)\.$",
              "当$1的灵能……你停顿了。"),
            R(@"^You cannot fly while engulfing (.+)$",
              "正在吞没$1时你无法飞行。"),
            R(@"^You are hooked!$",
              "你被钩住了！"),
            R(@"^You no longer feel ill\.$",
              "你不再感到恶心。"),
            R(@"^You stop meditating\.$",
              "你停止冥想。"),
            R(@"^You start to prowl\.$",
              "你开始潜行。"),
            R(@"^You can\'t fly right now\.$",
              "你现在无法飞行。"),
            R(@"^You froze\.$",
              "你被冻住了。"),
            R(@"^You were vaporized\.$",
              "你被汽化了。"),
            R(@"^You got it!$",
              "你拿到了！"),
            R(@"^You ate it!$",
              "你吃掉了它！"),
            R(@"^You fell apart\.$",
              "你散架了。"),
            R(@"^You spot a \{\{w\|dromad\}\} caravan\.$",
              "你发现了一支{{w|驼人}}商队。"),
            R(@"^You are \{\{C\|paralyzed\}\}\.!$",
              "你{{C|瘫痪}}了！"),
            R(@"^You are \{\{C\|paralyzed\}\}\.$",
              "你{{C|瘫痪}}了。"),
            R(@"^Your genome destabilizes and you gain a new mutation:$",
              "你的基因组失稳，你获得了一项新突变："),
            R(@"^Your genome destabilizes and you gain a new defect:$",
              "你的基因组失稳，你获得了一项新缺陷："),
            R(@"^Your genome destabilizes and you gain (.+)\.$",
              "你的基因组失稳，你获得了$1。"),
            R(@"^Your psyche exploded, and its psionic bits were encoded on the holographic boundary surrounding the psyche of (.+)\.$",
              "你的灵能爆散，其碎片被编码在环绕$1灵能的全息边界上。"),
            R(@"^You do not have (\d+) drams? of (.+)\.$",
              "你没有 $1 德拉姆的$2。"),
            R(@"^You are crippled for (\d+) rounds?\.$",
              "你瘫痪 $1 回合。"),
            R(@"^You gained \{\{C\|(.+)\}\}\.$",
              "你获得了{{C|$1}}。"),
            R(@"^You learn the skill \{\{C\|(.+)\}\}\.$",
              "你学会了技能{{C|$1}}。"),
            R(@"^You gained the mutation \{\{G\|(.+)\}\}\.$",
              "你获得了突变{{G|$1}}。"),
            R(@"^You gained the defect \{\{R\|(.+)\}\}\.$",
              "你获得了缺陷{{R|$1}}。"),
            R(@"^You have passed an incorrect direction of \'(.+)\'\.$",
              "你传入了一个错误的方向：'$1'。"),
            R(@"^You gently shake (.+)$",
              "你轻轻摇晃$1"),
            R(@"^From (.+) you take (\d+) damage\.$",
              "你从$1受到 $2 点伤害。"),
            R(@"^You take (\d+) damage from (.+)\.$",
              "你受到 $1 点来自$2的伤害。"),
            R(@"^You are immune to (.+)\.$",
              "你对$1免疫。"),
            R(@"^You are encumbered by (.+)\.$",
              "你被$1拖累。"),
            R(@"^You can\'t do that while (.+)\.$",
              "$1时你无法这么做。"),
            R(@"^You can\'t (.+) while (.+)\.$",
              "$2时你无法$1。"),
            // ★★ 2026-10-17（测试窗口 #38）：`SpindleNegotiation::FireEvent` 的**两句式**
            //   实机：`你的allied factions. Come back when you're favored by 4 or more factions不足。`
            //   根因：下面那条通用规则的 `(.+)` **贪婪且允许句号** ⇒ 把两句一起吞进 `$1`
            //   ⇒ 必须先给这两句一条**精确规则**（`ZhsHardcodedMsg` 里已有同句，但没轮到它）。
            //   `{{C|4}}` 的色码做成可选（剥色/未剥色两种来源都吃）。
            R(@"^You don\'t have enough allied factions\. Come back when you\'re favored by (?:\{\{C\|)?(\d+)(?:\}\})? or more factions\.$",
              "你的盟友派系不够。当你在 $1 个或更多派系中受青睐后再来。"),
            // ★ 通用规则收紧：捕获**不许跨句**（`.` `!` `?` 与全角版、换行都排除）
            //   —— §38 的同族审计结论：凡以 `(.+)` 结尾的规则都要这样检查。
            R(@"^You don\'t have enough ([^.!?。！？\n]+)\.$",
              "你的$1不足。"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.15：片段规则第三批（50 条）
            //
            //   续 `You/Your` 族 + 扩到 `from/under/in/a/to/it/you/his/her` 等
            //   **被拼在前后**的碎片，以及 `{{色标|}}` 开头的状态标签。
            //   ⚠ 与 2.16.11/12 同法：一律用 `^…(.+)\.$` 接住**完整消息**，
            //     `(.+)` 吃下中文专名；**不做片段替换**。
            // ═══════════════════════════════════════════════════════════
            R(@"^You cannot sleep on (.+)\.$",
              "你不能睡在$1上。"),
            R(@"^You cannot sit on (.+)\.$",
              "你不能坐在$1上。"),
            R(@"^You cannot unequip (.+)\.$",
              "你无法卸下$1。"),
            R(@"^You cannot set (.+)\.$",
              "你无法设置$1。"),
            R(@"^You cannot enter (.+)\.$",
              "你无法进入$1。"),
            R(@"^You are too large to enter (.+)\.$",
              "你太大了，进不去$1。"),
            R(@"^You are already in (.+)\.$",
              "你已经在$1里了。"),
            R(@"^You fail to get (.+)\.$",
              "你没能拿到$1。"),
            R(@"^You fail to extricate (.+)\.$",
              "你没能从$1中脱身。"),
            R(@"^You step on (.+)\.$",
              "你踩到了$1。"),
            R(@"^You have no physical (.+)\.$",
              "你没有可用的肢体来$1。"),
            R(@"^You have no supplies that (.+)\.$",
              "你没有可用于$1的补给。"),
            R(@"^You have no place available to hold (.+)\.$",
              "你没有可存放$1的位置。"),
            R(@"^You gain access to every schematic of (.+)\.$",
              "你获得了$1的全部图纸。"),
            R(@"^You gain the skill (.+)\.$",
              "你获得了技能$1。"),
            R(@"^You\'re not hungry enough to bring (.+)\.$",
              "你还不够饿，无法唤出$1。"),
            R(@"^You suddenly feel ready to use (.+)\.$",
              "你忽然觉得可以使用$1了。"),
            R(@"^You don\'t have the capacity to ascend (.+)\.$",
              "你没有足够容量来上升$1。"),
            R(@"^You can\'t safely ascend (.+)\.$",
              "你无法安全地上升$1。"),
            R(@"^You don\'t have the capacity to descend (.+)\.$",
              "你没有足够容量来下降$1。"),
            R(@"^You can\'t safely descend (.+)\.$",
              "你无法安全地下降$1。"),
            R(@"^You have rapidly advanced (.+)\.$",
              "你已在$1上进展神速。"),
            R(@"^You are now \{\{\|(.+)\}\}\.$",
              "你现在是{{|$1}}。"),
            // ═══════════════════════════════════════════════════════════════════
            // ★★ 2026-10-16【A8】**删除**了原来这条**贪婪**规则：
            //     R(@"^Your genome enters an excited state! Would you like to spend
            //         \{\{rules\|(\d+)\}\} mutation points to buy (.+)\?$",
            //       "你的基因组进入激发态！要花 {{rules|$1}} 点突变点购买$2吗？")
            //
            //  【为什么要删】`(.+)` 是**贪婪**的 ⇒ 它只把 `(\d+)` 之后、**最后一个** `?`
            //     之前的内容当 $2 吃掉，于是把尾巴 `before rapidly mutating` 留在了 $2 里：
            //         产出：你的基因组进入激发态！要花 {{rules|4}} 点突变点购买
            //               变异 before rapidly mutating吗？      ← 半中半英 ✗
            //  【实测证据】`roslyntest -- mutcheck`：输入官方形态
            //       `…buy 变异 before rapidly mutating?`
            //     该贪婪规则 [HIT] 并产出上面那句污染；
            //     而**精确版**（下面 `…buy (.+?) before rapidly mutating\?$`）产出
            //       「你的基因组进入兴奋状态！要花{{rules|4}}点突变点买一个变异，
            //         然后再快速突变吗？」—— **完全正确** ✓
            //  【为什么删了不会丢覆盖】精确版**完全覆盖**同一形态，且措辞更准。
            //     `roslyntest -- mutcheck` 复跑确认：带问号形态仍 [HIT]，产出为精确版。
            // ═══════════════════════════════════════════════════════════════════
            R(@"^You were stepped on\.$",
              "你被踩到了。"),
            R(@"^You cannot do that\.$",
              "你不能那么做。"),
            R(@"^You can\'t do that here\.$",
              "在这里不能那么做。"),
            R(@"^You can\'t imprint yet\.$",
              "你还不能铭印。"),
            R(@"^You are not enclosed\.$",
              "你并未被包裹住。"),
            R(@"^You must call Init\(\) before applying this to an object\.$",
              "在向对象应用此项之前，必须先调用 Init()。"),
            R(@"^You can\'t safely ascend\.$",
              "你无法安全地上升。"),
            R(@"^You can\'t safely descend\.$",
              "你无法安全地下降。"),
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.113：受伤原因「from %t X.」族里**没有被专门规则覆盖**的那些
            //
            //   【为什么现在才补】下面那条通用 `^from (.+)\.$` 会把**英文伤害类型**
            //     直接嵌进中文 ⇒ 产出「来自attack。」这种**半译**。
            //     已有专门规则的（`from poison!` / `from acid!` / `from choking ash!` …）
            //     因为**排在通用规则之前**，所以不受影响 —— 缺的是**没有专门条目**的那几个。
            //
            //   反编译实证（`TakeDamage` 的 `Message` 形参**默认值**）：
            //       `string Message = "from %t attack."`   ← `GameObject::TakeDamage`
            //                                               `ElementalDamage::FireEvent`
            //   `%t` = 所有格限定槽（"your" / "the snapjaw's"），**游戏在传入前替换**
            //     ⇒ 实际到达本出口的是**替换后的文字**；含 `%t` 的两条是**兜底**形态。
            //
            //   ★ 这 12 条的完整字面量清单（`_asm_literals.txt` 实证）：
            //       from %t attack.      from %t explosion!     from %t spores!
            //       from %t life drain!  from %t tiny spines!  from %t adrenal stress!
            //       from %t {{W|choking ash}}!   from %t {{g|poison}}!
            //       from %t electrical discharge!
            //       from %t failed assault on the structure of spacetime.
            //       from %o slam!        from %O!
            //     —— 带 `{{…}}` 的两条剥色后即 `from choking ash!` / `from poison!`
            //        （**已有专门规则**）；其余落进通用规则 ⇒ 本批补上。
            //
            //   ⚠⚠ **必须排在通用 `^from (.+)\.$` 之前** —— 顺序错了就永远命中不了
            //      （本版第一稿就插反了，靠复查顺序发现）。
            //   ⚠ 两种形态都管：`!`（感叹）与 `.`（句点）；`%t` 替换后与未替换的都写。
            R(@"^from your attack\.$",   "来自你的攻击。"),
            R(@"^from an attack\.$",     "来自一次攻击。"),
            R(@"^from attack\.$",        "来自攻击。"),
            R(@"^from %t attack\.$",     "来自攻击。"),
            R(@"^from an explosion!$",   "来自爆炸！"),
            R(@"^from explosion!$",      "来自爆炸！"),
            R(@"^from %t explosion!$",   "来自爆炸！"),
            R(@"^from spores!$",         "来自孢子！"),
            R(@"^from %t spores!$",      "来自孢子！"),
            R(@"^from %t life drain!$",  "来自生命汲取！"),
            R(@"^from %t tiny spines!$", "来自细小尖刺！"),
            R(@"^from tiny spines!$",    "来自细小尖刺！"),
            R(@"^from %t adrenal stress!$", "来自肾上腺素应激！"),
            R(@"^from adrenal stress!$", "来自肾上腺素应激！"),
            R(@"^from %t electrical discharge!$", "来自放电！"),
            R(@"^from electrical discharge!$",    "来自放电！"),
            R(@"^from %t failed assault on the structure of spacetime\.$",
              "来自对时空结构的攻击失败。"),
            R(@"^from %o slam!$",        "被$1猛击！"),
            R(@"^from slam!$",           "来自猛击！"),
            R(@"^from %O!$",             "来自$1！"),
            R(@"^from (.+)\.$",
              "来自$1。"),
            R(@"^from a (.+)!$",
              "来自一阵$1！"),
            R(@"^from being slammed into \{\{W\|(\d+)\}\} walls!$",
              "因为被撞上{{W|$1}}面墙！"),
            R(@"^under (.+)\.$",
              "在$1之下。"),
            R(@"^under a (.+)\.$",
              "在一层$1之下。"),
            R(@"^in a (.+)\.$",
              "在一处$1中。"),
            R(@"^and (.+)\.$",
              "以及$1。"),
            R(@"^to (.+)\.$",
              "以$1。"),
            R(@"^it (.+)\.$",
              "它$1。"),
            R(@"^you (.+)\.$",
              "你$1。"),
            R(@"^his (.+)\.$",
              "他的$1。"),
            R(@"^her (.+)\.$",
              "她的$1。"),
            R(@"^\{\{K\|Base demeanor: \{\{r\|aggressive\}\}\}\}$",
              "{{K|基础性情：{{r|好斗}}}}"),
            R(@"^\{\{K\|Base demeanor: \{\{g\|docile\}\}\}\}$",
              "{{K|基础性情：{{g|温顺}}}}"),
            R(@"^\{\{K\|Engagement style: \{\{r\|aggressive\}\}\}\}$",
              "{{K|交战风格：{{r|好斗}}}}"),
            R(@"^\{\{K\|Engagement style: \{\{g\|defensive\}\}\}\}$",
              "{{K|交战风格：{{g|防守}}}}"),
            // ★★★ 2026-10-12：**专用规则必须排在通用规则前面**。
            //   `Leveler.RapidAdvancement`（反编译实证）用字面量拼了这几句：
            //       Popup.PickOption("Choose a physical " + @for + " to rapidly advance.", …)
            //       Popup.Show("You have rapidly advanced " + name + " by " + N + " ranks to rank {{C|X}}!")
            //       Popup.Show("You have no physical " + Pluralize(@for) + " to rapidly advance!")
            //       Popup.Show("Your genome enters an excited state!…")
            //   而下面的通用规则 `^Choose (.+)\.$` 会把第一句**整句**吃掉 ⇒
            //     产出 `选择 a physical 变异 to rapidly advance。`（中英乱拼）✗
            //     还顺手把 `.` 换成 `。` ⇒ `ZhsPopupPatch` 那条专门规则**从此永不匹配** ✗✗
            //   ⇒ 把专用规则放前面（数组是**按序**匹配的）。
            R(@"^Choose a physical (.+?) to rapidly advance\.$",
              "选择一项要快速突变的$1。"),
            // ★★ 2026-10-16【A8】等级部分改为**可选包装**，且**分两条**保住原形态：
            //     ① `rank {{C|4}}!`（官方源码形态）⇒ 产出也带 `{{C|}}`
            //     ② `rank 4!`（实测到达形态）    ⇒ 产出**保持裸数字**（不凭空引入颜色标记）
            //     ⚠ 为什么分两条而不合成一条：合并写法会给裸数字**凭空加上 `{{C|}}`**，
            //       而若该串已由 `UITextSkin` 渲染过，`{{C|}}` 不会被二次解析 ⇒ 露字面量
            R(@"^You have rapidly advanced (.+?) by (.+?) ranks? to rank \{\{C\|(.+?)\}\}!$",
              "你已将$1快速提升了$2级，达到{{C|$3}}级！"),
            R(@"^You have rapidly advanced (.+?) by (.+?) ranks? to rank ([^\{].*?)!$",
              "你已将$1快速提升了$2级，达到$3级！"),
            R(@"^You have no physical (.+?) to rapidly advance!$",
              "你没有可供快速突变的$1！"),
            R(@"^Your genome enters an excited state!$",
              "你的基因组进入兴奋状态！"),
            R(@"^Your genome enters an excited state! Would you like to spend \{\{rules\|(\d+)\}\} mutation points to buy (.+?) before rapidly mutating\?$",
              "你的基因组进入兴奋状态！要花{{rules|$1}}点突变点买一个$2，然后再快速突变吗？"),

            // ⚠ 通用规则**收窄**：`Choose X.` / `Share X.` 这类词标签，`X` 应当是**名词/名字**，
            //   不是一整句英文。判据：`X` 里**不得出现两个连续的 ASCII 单词**
            //   （`a physical` 这种会被挡住 ⇒ 交给上面的专用规则或原样放行）✓
            R(@"^Choose (?!.*\b[A-Za-z]{2,}\s+[A-Za-z]{2,}\b)(.+)\.$",
              "选择$1。"),
            R(@"^Share (?!.*\b[A-Za-z]{2,}\s+[A-Za-z]{2,}\b)(.+)\.$",
              "分享$1。"),
            // ⚠ 这两条**故意不加** lookahead —— 它们的模板本身就要求 `$1` 是纯数量
            //   （`伤害 $1` / `距离 $1`），而且 `Damage`/`Distance` 开头的整句英文不存在
            R(@"^Damage (.+)$",
              "伤害 $1"),
            R(@"^Distance (.+)$",
              "距离 $1"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.16：片段规则第四批（45 条）—— `{{色标|}}` 族
            //
            //   ⚠ 关键：`Prefix` 是**先剥色再匹配**的（见其第 914 行注释），
            //     所以这批规则匹配的是**剥色后**的文本（如 `hulk honey tonic`），
            //     产出时再补回中文（色标由上游保留）。
            //   ⇒ 与前三批同法：`^前缀(.+)$` 接住完整消息。
            // ═══════════════════════════════════════════════════════════
            R(@"^hulk honey tonic$",
              "巨力蜂蜜补剂"),
            R(@"^shade oil tonic$",
              "影油补剂"),
            R(@"^sphynx salt tonic$",
              "斯芬克斯盐补剂"),
            R(@"^electrical arc leaps from (.+)$",
              "电弧从$1跃出"),
            R(@"^electrical arc leaps toward (.+)$",
              "电弧跃向$1"),
            R(@"^(\d+) credits? for license tiers ([\d+\-]+)$",
              "$2 级执照需 $1 点额度"),
            R(@"^Attack Chance: (.+)$",
              "命中几率：$1"),
            R(@"^Offhand Attack Chance: (.+)$",
              "副手命中几率：$1"),
            R(@"^Chance of becoming lost (.+)$",
              "迷路几率 $1"),
            R(@"^Chance of interesting encounters while traveling (.+)$",
              "旅行时遭遇有趣事件的几率 $1"),
            R(@"^Chance of encountering (.+)$",
              "遭遇$1的几率"),
            R(@"^Speed while traveling on the world map (.+)$",
              "世界地图旅行速度 $1"),
            R(@"^Regains charge when (.+)$",
              "$1时恢复电荷"),
            R(@"^This object has a broadcast power receiver that can pick up electrical charge (.+)$",
              "此物体装有广播电力接收器，可接收电荷 $1"),
            R(@"^Satellite broadcast power is currently occluded by (.+)$",
              "卫星广播电力当前被$1遮蔽"),
            R(@"^Painted: This object is painted with a scene from the history of the village (.+)$",
              "彩绘：此物体上绘有村庄$1的历史场景"),
            R(@"^Engraved: This object is engraved with a scene from the history of the village (.+)$",
              "雕刻：此物体上刻有村庄$1的历史场景"),
            R(@"^This object is a monument to a scene from the history of the village (.+)$",
              "此物体是村庄$1历史场景的纪念碑"),
            R(@"^Holographic: This hologram depicts a scene from the history of the village (.+)$",
              "全息：此全息影像描绘了村庄$1的历史场景"),
            R(@"^enclosed in (.+)$",
              "被包在$1中"),
            R(@"^engulfed by (.+)$",
              "被$1吞没"),
            R(@"^lying on (.+)$",
              "躺在$1上"),
            R(@"^marked by (.+)$",
              "留有$1的痕迹"),
            R(@"^sitting on (.+)$",
              "坐在$1上"),
            R(@"^shared with (.+)$",
              "与$1共享"),
            R(@"^You\'ve discovered a way to conceal (.+)$",
              "你发现了藏匿$1的方法"),
            R(@"^Eureka! You may now (.+)$",
              "找到了！你现在可以$1"),
            // ★★ 2.16.97：声望增减消息（**硬编码 `Append` 链拼装**，见 `Reputation::Modify`）
            //
            //   反编译出处（`XRL.World.Reputation::Modify`）：
            //       textBuilder.Append("Your ")
            //           .Append("reputation with {{C|").Append(Faction.GetFormattedName()).Append("}} ")
            //           .Append(@for >= 0 ? "increased" : "decreased")
            //           .Append(" by {{").Append('G'/'R').Append('|').Append(Math.Abs(@for))
            //           .Append("}} to {{").Append(color).Append('|').Append(num2).Append("}}.");
            //   ⇒ **每个片段都是独立 Append** ⇒ 字符串表里没有完整句可替换 ⇒ 必须在出口改写。
            //
            //   ★★ 为什么必须**排在下面那条 `^Your reputation with (.+)$` 之前**：
            //      那条的 `(.+)$` 会把 ` decreased by 180 to -780.` 也当成**阵营名**吞掉，
            //      产出「你与X decreased by 180 to -780.**的声望**」—— 语义颠倒。
            //      实机日志铁证（`Player.log` L7389-7390）：
            //          进：Your reputation with 玛蒙之子 decreased by 180 to -780.
            //          出：你与玛蒙之子 decreased by 180 to -780.的声望     ← 用户截图
            //   ⇒ 本条精确匹配**整句**，把「的声望」放回正确位置。
            //   ★ 拆成两条（提升 / 下降）而不是用 `(increased|decreased)` 一个组：
            //     后者会把**英文单词**塞进中文句子里 ⇒ 仍然中英混排。
            R(@"^Your reputation with (.+?) increased by (\d+) to (-?\d+)\.$",
              "你与$1的声望提升了 $2 点，现为 $3。"),
            R(@"^Your reputation with (.+?) decreased by (\d+) to (-?\d+)\.$",
              "你与$1的声望下降了 $2 点，现为 $3。"),
            // ★★ F6-A3（2026-10-16 · 另一位子代理转交 · 清单 `_tools\F6-声望族全量清单.md` §二 A3）
            //   反编译 `XRL.World.Reputation::Modify`：
            //       textBuilder.Append(Because.Capitalize()).Append(", your reputation with ") …
            //   `Because` 是**调用方给的原因**（多来自串表 ⇒ 常常已是中文）
            //   ⇒ 到达出口的形态：`护送商队, your reputation with 咬颚兽 increased by 110 to 110.`
            //   ⇒ `, your reputation with … increased/decreased by N to M.` 这一段**原来无规则**
            //     （下面那条 `^Your reputation with …` 锚在 `^Your`，够不到带原因前缀的形态）
            //     ⇒ 半译。
            //   ★ 与 A1/A2 同法：拆**提升 / 下降两条**，绝不把英文单词塞进中文句。
            //   ★ 2026-10-18（F13 #2）：原因自身可能已被译成「因为…」
            //     （例：`因为他们钦佩看守 Qan, your reputation with … increased …`）
            //     模板前面还有「由于」⇒ 会叠成「由于因为…」。
            //     ⇒ 捕获组前加可选 `(?:因为)?`：有就吃掉，没有就原样。
            R(@"^(?:因为)?(.+?), your reputation with (.+?) increased by (\d+) to (-?\d+)\.$",
              "由于$1，你与$2的声望提升了 $3 点，现为 $4。"),
            R(@"^(?:因为)?(.+?), your reputation with (.+?) decreased by (\d+) to (-?\d+)\.$",
              "由于$1，你与$2的声望下降了 $3 点，现为 $4。"),
            // ★ 2026-10-18（#67 夹具）：**全角逗号 + 已汉化的 `你的`** 形态
            //   实机：`因为他们鄙视看守 Qan，你的 reputation with 海胆 decreased by 100 to -600.`
            //   （`your` 已被代词收尾换成 `你的`、逗号是全角 ⇒ 上面那条半角 `, your` 够不到）
            R(@"^(.+?)，你的 reputation with (.+?) decreased by (\d+) to (-?\d+)\.$",
              "$1，你与$2的声望下降了 $3，降至 $4。"),
            R(@"^(.+?)，你的 reputation with (.+?) increased by (\d+) to (-?\d+)\.$",
              "$1，你与$2的声望提升了 $3，升至 $4。"),
            // ★ 2026-10-18（#69）：水之仪式面板两行（`WaterRitual::HandleEvent` 硬编码）
            R(@"^Your reputation with (.+?) is (-?\d+)\.$", "你与$1的声望为 $2。"),
            R(@"^(.+?) can award an additional (\d+) reputation\.$", "$1还能再给予 $2 点声望。"),
            R(@"^Your reputation with (.+)$",
              "你与$1的声望"),
            //   ★ 数字版同步修：`(.+)$` 会把句末 `!` 也吞进捕获组
            //     ⇒ 产出「你获得 100 点XP**!**」（半角叹号，中文句里不该有）。
            //     改为把 `!` 也匹配掉、译成全角「！」。
            //   ★ 2026-10-18（#13）：**XP / SP 必须先于通用数字版接走** ——
            //     通用版会产出「你获得 250 点XP！」（量词对，但 `XP` 没译）。
            //     实机：`You gain 250 XP`（`Nectar_Tonic_Applicator` / `GameObject.GainSP` 族）。
            //     颜色标记版 `You gain {{C|250}} XP!` 也收（弹窗/消息两种形态）。
            R(@"^You gain \{\{C\|(\d+)\}\} XP!?$", "你获得了 $1 点经验！"),
            R(@"^You gain (\d+) XP!?$", "你获得了 $1 点经验！"),
            R(@"^You gained \{\{C\|(\d+)\}\} XP!?$", "你获得了 $1 点经验！"),
            R(@"^You gained (\d+) XP!?$", "你获得了 $1 点经验！"),
            R(@"^You gain \{\{C\|(\d+)\}\} SP!?$", "你获得了 $1 点技能点！"),
            R(@"^You gain (\d+) SP!?$", "你获得了 $1 点技能点！"),
            R(@"^You gain (\d+) (.+?)!?$",
              "你获得 $1 点$2！"),
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.110：盾牌猛击（`Shield_Slam`）—— 「动词+介词+宾语」型拼接
            //   反编译出处（`XRL.World.Parts.Skill.Shield_Slam::Slam`）：
            //     DidXToYWithZ("slam", "into", TargetObject, "with", ShieldObj, null, "!",
            //                  null, null, IndirectObjectPossessedBy: ParentObject, …)
            //   `Messaging.WDidXToYWithZ` 的拼装顺序（反编译实证）：
            //     `"You " + Verb + ' ' + DirectPreposition + ' ' + <直接宾语>
            //      + ' ' + IndirectPreposition + ' ' + <间接宾语>` + EndMark
            //   ⇒ 英文：`You slam <目标> with <盾牌>!`
            //   ⇒ ★ **中文必须重排语序**：「你 + 用<盾牌> + 猛击<目标>！」
            //
            //   ★ 为什么归这里而不是新挂出口（重要，避免误判）：
            //     `WDidXToYWithZ` 与已挂的 `XDidYToZ` **结构完全一样**
            //     （都 `[Obsolete]` + `TextBuilder.Get()` + `HandleMessage(...)`）
            //     ⇒ 走**同一条出口** ⇒ 覆盖早已存在，缺的只是**这条句式规则**。
            //     （2.16.84 的实证 `You 坐下 down on 地垫.` 就是同一出口来的）
            //
            //   ⚠ `with` 后可能是 `your <盾>`（`IndirectObjectPossessedBy: ParentObject`
            //      ⇒ `defaultDefiniteArticle = ParentObject.its`）也可能只是 `<盾>`
            //      ⇒ 前缀用 `(?:(?:your|the|a|an)\s+)?` **宽松匹配**
            //   ⚠ 动词形态可能是 `slam` / `slams` / `slammed`
            R(@"^You (slam|slams|slammed) (.+?) with (?:(?:your|the|a|an) )?(.+?)(!|\.)?$",
              "你用$3猛击$2！"),
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.108：「安息之钟」倒计时（**拼接件**，硬编码）
            //   反编译出处（`ildex str XRL.ITombAnchorSystem` → `OnEndTurn`）：
            //       "&MThe Bell of Rest tolls! The dead will be recalled in "
            //       + 回合数 + " rounds."
            //   ⇒ 两句拼装成一条玩家消息，**整串精确匹配永远命中不了**。
            //   ⚠ `&M` 是**第二种颜色码**（不是 `{{…}}`）——
            //      `StripColorTags` **只剥 `{{…}}`**（见本文件 L1581 的记载）
            //      ⇒ 规则里必须**原样带上 `&M`**。
            //   ★ 译名：`Bell of Rest` = **安息之钟**
            //      ⚠ 原为「安息之铃」，2.16.109 更正 ——
            //        官方 `ObjectBlueprints\Furniture.xml` 实证：
            //          `Weight="10000"`（100 公斤）· `Takeable="false"`（不可拿取）
            //          · `Hitpoints="1000"` · `<part Name="BellOfRest" />`
            //        ⇒ 这是**巨型固定装置**，不是手摇铃铛 ⇒ 用「钟」。
            //      ★ 一并统一了 `Furniture.xml`（Render.DisplayName）与
            //        `ZhsNameDisplayPatch.cs`（名字映射表 L1180）
            R(@"^&MThe Bell of Rest tolls! The dead will be recalled in (\d+) rounds\.$",
              "&M安息之钟敲响了！亡者将在 $1 轮后被召回。"),
            // ★★ 2.16.103：`You gain X!`（**无数字**形态，用户截图）
            //   实证：`Qud.API.MutationsAPI::RandomlyMutate`：
            //       Strings._T("RandomlyMutation gain mutation spentPointsReport fragment",
            //                  "You gain =mutation.name=!").SetSubject(go)…
            //   ⇒ 走**字符串表**，但 ID 是**带槽的片段**；替换槽后成 `You gain 力场墙!`
            //   ⇒ 上面那条 `^You gain (\d+) (.+)$` **要求数字** ⇒ 命中不了 ⇒ 留英文骨架。
            //   ★ 放在数字版**之后**（且靠近表尾）：`You gain 100 XP!` 必须先被数字版接走，
            //     否则会被本条吃成「你获得了 100 XP！」（丢掉量词「点」）。
            R(@"^You gain (.+)!$",
              "你获得了$1！"),
            R(@"^You miss with (.+)$",
              "你用$1没打中"),
            R(@"^You butcher (.+)$",
              "你宰割了$1"),
            R(@"^You rip (.+)$",
              "你撕开了$1"),
            // ★★ 2026-10-17（测试窗口 #4）：`Garbage::AttemptRifle` 的**四个变体**。
            //   产地（反编译）：
            //     "You rifle through " + obj + (text=="" ? "" : " " + text)
            //       + ", and find " + item.an() + "."
            //     "{{K|You rifle through " + obj + dir + ", but you find nothing.}}"
            //   `text` = `Directions.GetDirectionDescription(...)`，player 走
            //     `=dir.to.the.direction=`（我方译作「在…方向」）
            //   ⇒ 旧兜底 `^You rifle through (.+)$` 把**方向和后半句一起**吞进 `$1`
            //     ⇒ 「你翻找废墟在南方向， and find 烧毁的电容器。」
            //   ★ 方向要放在**前面**（中文语序「翻找 + 方向 + 的 + 对象」），
            //     所以模板是 `$2的$1`；`in the south` 随后由词表译成「在南方向」。
            R(@"^You rifle through (.+?) (in the .+?), and find (.+)\.$",
              "你翻找$2的$1，找到了$3。"),
            R(@"^You rifle through (.+?), and find (.+)\.$",
              "你翻找$1，找到了$2。"),
            R(@"^You rifle through (.+?) (in the .+?), but you find nothing\.$",
              "你翻找$2的$1，但什么也没找到。"),
            R(@"^You rifle through (.+?), but you find nothing\.$",
              "你翻找$1，但什么也没找到。"),
            // 兜底必须**排除**上面两种半句，否则又会整段吞掉
            R(@"^You rifle through ((?:(?!, and find |, but you find nothing).)+)$",
              "你翻找$1"),
            R(@"^You prepare (.+)$",
              "你准备了$1"),
            R(@"^You take (.+)$",
              "你承受$1"),
            R(@"^You cannot berate (.+)$",
              "你无法斥骂$1"),
            R(@"^You cannot make telepathic contact with (.+)$",
              "你无法与$1建立心灵联系"),
            R(@"^You do not have enough mutation points to increase that (.+)$",
              "你的突变点不足以提升那个$1"),
            R(@"^You may not advance this (.+)$",
              "你不能推进这个$1"),
            R(@"^You have made no map notes\. Hit (.+)$",
              "你还没有地图标注。按$1"),
            R(@"^You have made no general notes\. Hit (.+)$",
              "你还没有一般笔记。按$1"),
            R(@"^Your health has dropped below (.+)$",
              "你的生命值已降至$1以下"),
            R(@"^Your attack does not affect (.+)$",
              "你的攻击对$1无效"),
            R(@"^Your attack fails to penetrate (.+)$",
              "你的攻击未能穿透$1"),
            R(@"^Editor\'s note: (.+)$",
              "编者按：$1"),
            R(@"^Author\'s note: (.+)$",
              "作者按：$1"),
            R(@"^Schemasoft \[(.+)$",
              "图纸软件［$1"),
            R(@"^Skillsoft \[(.+)$",
              "技能软件［$1"),
            R(@"^Skillsoft Plus \[(.+)$",
              "技能软件增强版［$1"),
            R(@"^Invalid value (.+)$",
              "无效值 $1"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.17：片段规则第五批（84 条）
            //
            //   剩量最大的一批：`You/Your` 长句 + `under the effects of …`
            //   + `{{rules|…}}` 数值说明 + 引号/称呼 + 散句。
            //   ⚠ 同前：`Prefix` 先剥色 ⇒ 规则写**剥色后**文本。
            // ═══════════════════════════════════════════════════════════
            R(@"^You dissipated\.$",
              "你消散了。"),
            R(@"^Your shot goes wild!$",
              "你的射击偏得没边！"),
            R(@"^You\'ve done nothing\.$",
              "你什么也没做。"),
            R(@"^Your opponent perished!$",
              "你的对手死了！"),
            R(@"^You can\'t deploy there!$",
              "你不能部署在那里！"),
            R(@"^You are unusually large\.$",
              "你的体型异常巨大。"),
            R(@"^You are startled!$",
              "你被惊到了！"),
            R(@"^Your heart is weak\.$",
              "你的心脏很弱。"),
            R(@"^You feel drowsy\.$",
              "你感到昏昏欲睡。"),
            R(@"^You feel uneasy\.$",
              "你感到不安。"),
            R(@"^You emit a ray of flame\.$",
              "你射出一道火焰射线。"),
            R(@"^You emit a ray of frost\.$",
              "你射出一道冰霜射线。"),
            R(@"^You don\'t know how to use (.+)\.$",
              "你不知道怎么使用$1。"),
            R(@"^You hit something (.+)\.$",
              "你打中了某个东西（$1）。"),
            R(@"^You cannot seem to affect (.+)\.$",
              "你似乎影响不了$1。"),
            R(@"^You fell down (.+)\.$",
              "你掉进了$1。"),
            R(@"^You cannot slam (.+)\.$",
              "你无法猛击$1。"),
            R(@"^You aren\'t strong enough to slam through (.+)\.$",
              "你力气不够，撞不穿$1。"),
            R(@"^You were slammed by (.+)\.$",
              "你被$1撞飞了。"),
            R(@"^You were slammed into a wall by (.+)\.$",
              "你被$1撞到了墙上。"),
            R(@"^You were slammed into two walls by (.+)\.$",
              "你被$1撞上了两面墙。"),
            R(@"^You were slammed into (.+)\.$",
              "你被撞进了$1。"),
            R(@"^You were slammed into (.+) by (.+)\.$",
              "你被$2撞进了$1。"),
            R(@"^You have already proselytized (.+)\.$",
              "你已经劝诱过$1了。"),
            R(@"^You cannot shield slam (.+)\.$",
              "你无法用盾牌猛击$1。"),
            R(@"^You can\'t charge more than (.+)\.$",
              "你冲锋距离不能超过$1。"),
            R(@"^You cannot juke into (.+)\.$",
              "你无法闪避进$1。"),
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2026-10-15（N1 批次 · **E33 收紧**）
            //
            // 【原写法】`^You cannot juke (.+)\.$` / `^You cannot juke both (.+)\.$`
            //   ⇒ `(.+)` 会把中文名之后的 ` out of your way` **整段吞进捕获组**
            //   ⇒ 上屏「你无法闪避库祖 out of your way。」（半译）
            //   （报告 `_tools\E5-复核B族41与E族71.md` E33 行给出的实测症状）
            //
            // 【反编译出处】`XRL.World.Parts.Skill.Tactics_Juke::HandleEvent` L62/L66：
            //     return ParentObject.Fail("You cannot juke " + gameObject.t() + " out of your way.");
            //     return ParentObject.Fail("You cannot juke both " + gameObject.t()
            //                              + " and " + Object.t() + " out of your way.");
            //   ⇒ 固定英文只有 ` out of your way.` 一段，显式消费即可
            //
            // 【★ 顺序】`both … and …` 必须排在**单数形态之前** ——
            //   单数那条的非贪婪 `(.+?)` 也能吃下 `both A and B`（实测过同型坑）
            // ═══════════════════════════════════════════════════════════════
            R(@"^You cannot juke both (.+?) and (.+?) out of your way\.$",
              "你无法同时把$1和$2闪到一边。"),
            R(@"^You cannot juke (.+?) out of your way\.$",
              "你无法把$1闪到一边。"),
            // ★ 兜底：其余 `You cannot juke X.` 形态（防将来新增调用点）
            R(@"^You cannot juke (.+)\.$",
              "你无法闪避$1。"),
            R(@"^You cannot juke both (.+)\.$",
              "你不能同时闪避$1。"),
            R(@"^You kick at (.+)\.$",
              "你朝$1踢去。"),
            R(@"^You cannot repair (.+)\.$",
              "你无法修理$1。"),
            R(@"^You can\'t aggressively lunge through (.+)\.$",
              "你无法猛烈突刺穿过$1。"),
            R(@"^Your lunge passes through (.+)\.$",
              "你的突刺穿过了$1。"),
            R(@"^You make some progress disarming (.+)\.$",
              "你在解除$1的武装上有了些进展。"),
            R(@"^You need be near (.+)\.$",
              "你需要靠近$1。"),
            R(@"^You have increased (.+)\.$",
              "你提升了$1。"),
            R(@"^Your laser beam doesn\'t penetrate (.+)\.$",
              "你的激光束未能穿透$1。"),
            R(@"^You were turned to stone by the gaze of (.+)\.$",
              "你被$1的凝视化成了石头。"),
            R(@"^You emit a ray of flame from your (.+)\.$",
              "你从$1射出一道火焰射线。"),
            R(@"^You emit a ray of frost from your (.+)\.$",
              "你从$1射出一道冰霜射线。"),
            R(@"^Your genome destabilized and you gained the (.+)\.$",
              "你的基因组失稳，你获得了$1。"),
            R(@"^You have all available (.+)\.$",
              "你已拥有全部可用的$1。"),
            R(@"^You recovered the historic relic, (.+)\.$",
              "你取回了历史遗物：$1。"),
            R(@"^You receive tinkering bits <(.+)$",
              "你收到了改装零件 <$1"),
            R(@"^You start calling this location \'(.+)\'\.$",
              "你开始把这个地点称为「$1」。"),
            R(@"^You stop calling this location \'(.+)\'\.$",
              "你不再把这个地点称为「$1」。"),
            R(@"^You stopped calling a location \'(.+)\'\.$",
              "你不再称某地点为「$1」。"),
            R(@"^You yell, \'I cannot believe (.+)$",
              "你喊道：『我真不敢相信$1"),
            R(@"^You produce ambient light within a radius of (.+)$",
              "你在半径 $1 内产生环境光"),
            R(@"^You can gaze (.+)$",
              "你可以凝视$1"),
            R(@"^You teleport to a nearby location and bring everyone within radius (.+)$",
              "你传送到附近某处，并带走半径 $1 内的所有人"),
            R(@"^You detect the presence of creatures within a radius of (.+)$",
              "你能感知半径 $1 内的生物"),
            R(@"^You can sprint for (.+)$",
              "你可以冲刺 $1"),
            R(@"^You travel on the world map at (.+)$",
              "你在世界地图上的旅行速度为 $1"),
            R(@"^You can jump (.+)$",
              "你可以跳跃 $1"),
            R(@"^You can charge (.+)$",
              "你可以冲锋 $1"),
            R(@"^under the effects of (.+) tonic$",
              "处于$1补剂效果之下"),
            R(@"^in a war trance$",
              "处于战斗恍惚之中"),
            R(@"^scintillating in prismatic hues$",
              "闪烁着虹彩色泽"),
            R(@"^from poison!$",
              "来自毒素！"),
            R(@"^from acid!$",
              "来自强酸！"),
            R(@"^from the hulk honey!$",
              "来自巨力蜂蜜！"),
            R(@"^Base demeanor: aggressive$",
              "基础性情：好斗"),
            R(@"^Base demeanor: docile$",
              "基础性情：温顺"),
            R(@"^Engagement style: defensive$",
              "交战风格：防守"),
            R(@"^Engagement style: aggressive$",
              "交战风格：好斗"),
            R(@"^is gone! \*belch\*$",
              "没了！*打嗝*"),
            R(@"^is increased by (\d+)!$",
              "提升了 $1 点！"),
            R(@"^and gain \+(\d+) Ego!$",
              "并获得 +$1 自我！"),
            R(@"^the recipe for (.+)$",
              "$1的配方"),
            R(@"^you to improvise (.+)$",
              "由你即兴发挥$1"),
            R(@"^you to craft the item modification (.+)$",
              "由你制作物品改装件$1"),
            R(@"^can award an additional (\d+)$",
              "可额外授予 $1"),
            R(@"^in a flurry of flashing chrome and spurting liquid$",
              "在一阵闪亮的铬光与喷溅的液体中"),
            R(@"^time-dilated \((.+)$",
              "时间膨胀（$1"),
            R(@"^cleaved \((.+)$",
              "被劈开（$1"),
            R(@"^psionically cleaved \((.+)$",
              "被灵能劈开（$1"),
            R(@"^knowledge of (.+)$",
              "$1的知识"),
            R(@"^distorts into knowledge of (.+)$",
              "扭曲为$1的知识"),
            R(@"^transmutes into the mutation (.+)$",
              "转化为突变$1"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.18：片段规则第六批（96 条）—— 尾料收口
            //
            //   来源：把 379 条逐条过一遍后的「⑤ 仍待译」清单
            //   （其余四类为：已覆盖 161 · 格式碎片 14 · 开发占位 1 · UI 按钮 97）
            //   ⚠ `Prefix` 先剥色 ⇒ 规则写**剥色后**文本
            //     （`{{rules|X}}` → `X`；`{{w|antelopes}}` → `antelopes`）
            // ═══════════════════════════════════════════════════════════
            R(@"^Venom poisons opponents for (.+)$",
              "毒素使对手中毒 $1"),
            R(@"^Venom confuses opponents for (.+)$",
              "毒素使对手混乱 $1"),
            R(@"^Venom paralyzes opponents for (.+)$",
              "毒素使对手瘫痪 $1"),
            R(@"^rounds \(damage increment (.+)$",
              "回合（伤害增量 $1"),
            R(@"^Affected creatures act semi-randomly and receive a -(.+)$",
              "受影响的生物行动变得半随机，并获得 -$1"),
            R(@"^Damage to non-structural objects: (.+)$",
              "对非结构物体的伤害：$1"),
            R(@"^Damage to structural objects: (.+)$",
              "对结构物体的伤害：$1"),
            R(@"^Laser damage increment: (.+)$",
              "激光伤害增量：$1"),
            R(@"^Duration between use and reversion: (.+)$",
              "使用与回退之间的间隔：$1"),
            R(@"^Unerringly identify artifacts up to complexity tier (.+)$",
              "可万无一失地鉴定至复杂度等级 $1 的神器"),
            R(@"^Learn how to construct identified artifacts up to complexity tier (.+)$",
              "学会制作至复杂度等级 $1 的已鉴定神器"),
            R(@"^Distance (\d+): creatures receive a (.+)$",
              "距离 $1：生物获得 $2"),
            R(@"^Melee attacks heat opponents by (.+)$",
              "近战攻击使对手升温 $1"),
            R(@"^Melee attacks cool opponents by (.+)$",
              "近战攻击使对手降温 $1"),
            R(@"^Releases gas for (.+)$",
              "释放气体持续 $1"),
            R(@"^Whenever you would have taken heat damage, you heal for (.+)$",
              "每当你本应受到热伤害时，你改为治疗 $1"),
            R(@"^Whenever you would have taken cold damage, you heal for (.+)$",
              "每当你本应受到冷伤害时，你改为治疗 $1"),
            R(@"^To-hit bonus: (.+)$",
              "命中加成：$1"),
            R(@"^May only assume the form of creatures level (.+)$",
              "只能变形为等级 $1 的生物"),
            R(@"^Mental actions have (.+)$",
              "精神行动有 $1"),
            R(@"^While it\'s flowing, you gain \+(.+)$",
              "流淌期间，你获得 +$1"),
            R(@"^(\d+)% chance to remove a (.+) at random each round$",
              "每回合有 $1% 几率随机移除一个$2"),
            R(@"^(\d+)% chance on melee attack to sting your opponent \((.+)$",
              "近战攻击时有 $1% 几率蜇刺对手（$2"),
            R(@"^ at random enemies every round for (.+)$",
              "——每回合随机射向敌人，持续 $1"),
            R(@"^ class natural weapon that deal (.+)$",
              " 级天生武器，造成 $1"),
            R(@"^Vulnerable to (.+)$",
              "易受$1伤害"),
            R(@"^([+-]\d+) reputation with (.+)$",
              "与$2的声望 $1"),
            R(@"^\.\.\. and death pilgrim of the (.+)$",
              "……以及$1的死亡朝圣者"),
            R(@"^Are you sure you want to clear this binding for (.+)$",
              "确定要清除$1的绑定吗"),
            R(@"^Are you sure you want to clear the binding for (.+)$",
              "确定要清除$1的绑定吗"),
            R(@"^Press control to bind to (.+)$",
              "按控制键以绑定到$1"),
            R(@"^Press the keyboard key to bind to (.+)$",
              "按键盘按键以绑定到$1"),
            R(@"^The force bubble in front of (.+)$",
              "$1前方的力场泡"),
            R(@"^A force bubble pops into being in front of (.+)$",
              "一个力场泡在$1前方凭空出现。"),
            R(@"^The force bubble around (.+)$",
              "$1周围的力场泡"),
            R(@"^A force bubble pops into being around (.+)$",
              "一个力场泡在$1周围凭空出现。"),
            R(@"^A ribbon of electricity leaps from (.+) to (.+)\.$",
              "一道电带从$1跃向$2。"),
            R(@"^from an electrical shock delivered by (.+)$",
              "来自$1释放的电击"),
            R(@"^from choking ash!$",
              "来自呛人的灰烬！"),
            R(@"^from the cryogenic mist\.$",
              "来自低温雾气。"),
            R(@"^streaming ribbons of glitter$",
              "流淌的闪亮光带"),
            R(@"^ to gleam with an unearthly light\.$",
              " 泛起非尘世的光芒。"),
            R(@"^You started calling a location \'(.+)$",
              "你开始把某地点称为「$1"),
            R(@"^You do not need to set the display name of the skill (.+)$",
              "你无需设置技能$1的显示名。"),
            R(@"^You cannot set the display name of the skill (.+)$",
              "你无法设置技能$1的显示名。"),
            R(@"^You have no followers that can enter (.+)$",
              "你没有能进入$1的追随者。"),
            R(@"^You find a weakness in (.+)$",
              "你发现了$1的弱点。"),
            R(@"^You do not budge\.$",
              "你纹丝不动。"),
            R(@"^You cannot be seen\.$",
              "你无法被看见。"),
            // ★★ 2026-10-15（N1 批次 · **B41 收紧**）
            //   【原写法】`^You are not in cardiac arrest\. Do you want to use (.+)$`
            //     ⇒ `(.+)` 会把中文名之后的 ` on yourself anyway?` **整段吞进 $1**
            //     ⇒ 上屏「你并未心脏骤停。要使用库祖 on yourself anyway?吗」
            //   【反编译出处】`AutomatedExternalDefibrillator::AttemptDefibrillate` L46：
            //     "You are not in cardiac arrest. Do you want to use " + ParentObject.t()
            //       + " on " + Actor.itself + " anyway?"
            //   ⇒ 固定英文 ` on yourself anyway?` 必须**显式消费**
            //   ★ 同现场的**第三人称分支**（B7）见本文件 N1 段：
            //     `^ART(N) (?:is|are) not in cardiac arrest\. …`
            R(@"^You are not in cardiac arrest\. Do you want to use (.+?) on yourself anyway\?$",
              "你并未心脏骤停。仍要对自己使用$1吗？"),
            R(@"^You don\'t have the required <=bitcost=> bits! You have:$",
              "你没有所需的 <=bitcost=> 比特！你现有："),
            R(@"^The zealot yells \'(.+)$",
              "狂信徒喊道：『$1"),
            R(@"^Asphodel yells, \'(.+)\'$",
              "阿斯弗德尔喊道：『$1』"),
            R(@"^Remember, Aristocrat, your base license tier is (.+)$",
              "记住，贵族，你的基础执照等级是 $1"),
            R(@"^Overclock chromelings \(([+-]\d+) QN, current level: (.+)$",
              "超频铬兽（$1 迅捷，当前等级：$2"),
            R(@"^Reduce clock on chromelings \(([+-]\d+) QN, current level: (.+)$",
              "降低铬兽频率（$1 迅捷，当前等级：$2"),
            R(@"^Activate a chain laser emplacement\. \[(.+)\]$",
              "启动连锁激光炮台。［$1］"),
            R(@"^It will cost (\d+) mutation points? to increase (.+)$",
              "提升$2需要 $1 点突变点"),
            R(@"^ base rank to (.+)$",
              " 基础等级至 $1"),
            R(@"^ of fresh water you owe (.+)$",
              " 你欠下的淡水，$1"),
            R(@"^You don\'t have any (.+)$",
              "你没有任何$1"),
            R(@"^One or more mods enabled in this save are not available:(.+)$",
              "此存档启用了一个或多个不可用的模组：$1"),
            R(@"^These mods are disabled in the save:(.+)$",
              "这些模组在此存档中被禁用：$1"),
            R(@"^These mods are enabled in the save:(.+)$",
              "这些模组在此存档中已启用：$1"),
            R(@"^Share the burden across all allies\. \[(.+)\]$",
              "在所有盟友间分担重负。［$1］"),
            R(@"^Share the burden between two allies\. \[(.+)\]$",
              "在两位盟友间分担重负。［$1］"),
            R(@"^Choose a faction to share the burden\. \[(.+)\]$",
              "选择分担重负的派系。［$1］"),
            R(@"^Choose a faction to spare from obligation to Asphodel\. \[(.+)\]$",
              "选择免除对阿斯弗德尔义务的派系。［$1］"),
            R(@"^Spare one faction of all obligation by betraying a second faction and selling their secrets to Asphodel\. \[(.+)\]$",
              "通过背叛第二个派系并把其秘密卖给阿斯弗德尔，来免除一个派系的全部义务。［$1］"),
            R(@"^Invoke the Chaos Spiel\. \[(.+)\]$",
              "启用混沌话术。［$1］"),
            R(@"^You can\'t auto-equip (.+)$",
              "你无法自动装备$1"),
            R(@"^You\'re going to collapse from exhaustion in (.+)$",
              "你将在$1后因力竭倒下"),
            R(@"^You\'re not hungry enough to bring (.+)$",
              "你还不够饿，无法唤出$1"),
            R(@"^You don\'t have the capacity to ascend (.+)$",
              "你没有足够容量上升$1"),
            R(@"^You don\'t have the capacity to descend (.+)$",
              "你没有足够容量下降$1"),
            R(@"^You can\'t safely ascend (.+)$",
              "你无法安全上升$1"),
            R(@"^You can\'t safely descend (.+)$",
              "你无法安全下降$1"),
            R(@"^You don\'t know how to use (.+)$",
              "你不知道怎么使用$1"),
            R(@"^You can\'t aggressively lunge through (.+)$",
              "你无法猛烈突刺穿过$1"),
            R(@"^You aren\'t strong enough to slam through (.+)$",
              "你力气不够，撞不穿$1"),
            R(@"^You can\'t charge more than (.+)$",
              "你的冲锋距离不能超过$1"),
            R(@"^Your laser beam doesn\'t penetrate (.+)$",
              "你的激光束未能穿透$1"),
            R(@"^You spot a dromad caravan\.$",
              "你发现了一支驼人商队。"),
            R(@"^You are paralyzed\.$",
              "你瘫痪了。"),
            R(@"^You are paralyzed!$",
              "你瘫痪了！"),
            R(@"^Your psyche exploded, and its psionic bits were encoded on the holographic boundary surrounding the psyche of (.+)$",
              "你的灵能爆散，其碎片被编码在环绕$1灵能的全息边界上。"),
            R(@"^from slamming into (.+)$",
              "因为撞上$1"),


            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.26：`ShowFailure` 的**句子碎片** → 后缀型规则（26 条）
            //
            //   来源：`ShowFailure` 44 个调用点的实参（`_tools\_showfailure_real_gaps.txt`）
            //   这些在 DLL 里是**前缀/后缀各一条**（如 `"You are too far from "` +
            //   名字 + `" to perform the water ritual."`）⇒ 用 2.16.11-18 同款机制接住**完整消息**
            //   ⚠ 与旧规则可能重叠 ⇒ 置于**表末**，让更具体的先命中
            // ═══════════════════════════════════════════════════════════
            // ⚠ 2.16.26 修：原写的 `(.+)` 会把 `X to perform the water ritual` **整个吞掉**
            //   ⇒ 产出 `你离阿盖夫 to perform the water ritual太远了。`（半译）
            //   ⇒ 改成**非贪婪**并显式消费英文尾巴
            R(@"^You are too far from (.+?) to perform the water ritual\.$",
              "你离$1太远，无法举行水礼。"),
            R(@"^(.+) is too large for you to engulf\.$",
              "$1太大，你吞不下。"),
            R(@"^You cannot do that while (.+) in (.+) present situation\.$",
              "当$1处于$2当前状态时，你无法这么做。"),
            R(@"^There is no (.+) at the destination to receive you\.$",
              "目的地没有$1来接应你。"),
            R(@"^(.+) must be laid out before you can do that\.$",
              "$1必须先铺开，你才能这么做。"),
            R(@"^(.+) is broken\.$",
              "$1坏了。"),
            R(@"^(.+) (?:don't|doesn't) seem to be working\.$",
              "$1似乎不工作了。"),
            R(@"^(.+) must be hung up before you can do that\.$",
              "$1必须先挂起，你才能这么做。"),
            R(@"^You cannot reach (.+)\.$",
              "你够不到$1。"),
            R(@"^You are out of phase with (.+)\.$",
              "你与$1处于相位之外。"),
            R(@"^(.+) (?:are|is) sealed\.$",
              "$1封闭着。"),
            R(@"^(.+) (?:are|is) unresponsive\.$",
              "$1没有反应。"),
            R(@"^(.+)'s socket is sealed\.$",
              "$1的插槽是封死的。"),
            R(@"^You need be near (.+) in order to disassemble (.+)\.$",
              "要拆解$2，你得靠近$1。"),
            R(@"^(.+) is already in use by (.+)\.$",
              "$1已被$2占用。"),
            R(@"^(.+) (?:do|does) not have enough charge to operate\.$",
              "$1电量不足，无法运作。"),
            R(@"^(.+) merely(?:s)? clicks?\.$",
              "$1只是咔哒响了一声。"),
            R(@"^There is nowhere for the (.+) to be recalled to\.$",
              "没有可供召回$1的地方。"),
            R(@"^(.+) is obstructed\.$",
              "$1被挡住了。"),
            R(@"^You have no (.+) to recall\.$",
              "你没有可召回的$1。"),
            R(@"^(.+) (?:are|is) in no need of repairs\.$",
              "$1无需修理。"),
            R(@"^The (.+) is obstructed\.$",
              "$1被挡住了。"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.6：截图实证的** 6 条缺口**（用户截图「严查」）
            //
            //  截图里有 6 类消息仍是英文/半英文。反编译逐条定位：
            //   ① `LifeDrain` 的 `XDidYToZ(Drainer, "begin", "to drain life essence
            //      from", Object, …, "!")` ⇒ 前半被 XDidY 译成中文，**尾部英文残留**
            //   ② `LifeDrain::Apply` 的 `bonds with` 形态（`find 'bonds with'` = 0 处
            //      ⇒ 说明是 `XDidYToZ` 的**动词表**产出，冠词与动词都没译）
            //   ③ `TakeDamage(…, "from %t life drain!")` ⇒ 走 `%t` 槽
            //   ④ 回血消息（`heals for N hit point.`）
            //   ⑤ `Combat::MeleeAttackWithWeaponInternal` 受方分支
            //      `Attacker.Does("don't") + " penetrate your armor with " +
            //       Attacker.its_(Weapon) + "! [" + value + "]"`
            //      ⇒ 截图里 `[N]` 被裁掉，故 `[N]` 必须**可选**
            //   ⑥ `X misses you with Y! [a vs. b]`（受方视角）
            //
            //  ★ 门禁已实测放行这些「中文夹英文」形态（`HasLatinWordNearCjk` ±12）。
            // ═══════════════════════════════════════════════════════════════

            // ① 开始汲取（`XDidYToZ` 尾部残留）
            R(@"^" + ART + @"(" + N + @")\s*[开開]始 to drain life essence from (" + N + @")!$",
              "$1开始从$2身上汲取生命精华！"),
            R(@"^" + ART + @"(" + N + @") to drain life essence from (" + N + @")!$",
              "$1从$2身上汲取生命精华！"),

            // ② `bonds with you.`（`XDidYToZ` 产出；末尾是半角句号）
            R(@"^" + ART + @"(" + N + @") bonds with you\.$", "$1与你建立了联结。"),
            R(@"^" + ART + @"(" + N + @") bonds with (" + N + @")\.$", "$1与$2建立了联结。"),

            // ③ `You take N damage from X('s|的) life drain!`
            // ⚠ 2.16.13：`((?:的)? ?life drain)` 原本**被捕获却不消费**
            //   —— 原文的「的」/`'s` 只是**形态**，译文固定写「生命汲取」即可
            //   ⇒ 改为非捕获 `(?:的|'s|s)? ?life drain`，与「捕获全部消费」原则一致。
            R(@"^\{\{r\|You take (\d+) damage from (.*?)(?:的|[’']s?)? ?life drain!\}\}$",
              "{{r|你受到 $1 点来自$2的生命汲取伤害！}}"),
            R(@"^You take (\d+) damage from (.*?)(?:的|[’']s?)? ?life drain!$",
              "你受到 $1 点来自$2的生命汲取伤害！"),

            // ④ 回血：`The X heals for 1 hit point.`
            R(@"^" + ART + @"(" + N + @") heals? for (\d+) hit points?\.$",
              "$1恢复了 $2 点生命值。"),
            R(@"^" + ART + @"(" + N + @") heals? (\d+) hit points?\.$",
              "$1恢复了 $2 点生命值。"),

            // ⑤ 未穿透（★ `[N]` **必须拆两分支** —— 合并的 `(?:…)?` 在缺失时会
            //   产出空 `[]`，实测踩到）
            R(@"^" + ART + @"(" + N + @") doesn't penetrate your armor with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")! \[(\d+)\]$",
              "$1的$2没能穿透你的护甲！[$3]"),
            R(@"^" + ART + @"(" + N + @") doesn't penetrate your armor with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")!$",
              "$1的$2没能穿透你的护甲！"),
            R(@"^" + ART + @"(" + N + @") doesn't penetrate your armor!?(?: ?\[(\d+)\])?$",
              "$1没能穿透你的护甲！[$2]"),

            // ── ★★ 2026-10-17（用户实机第四张截图）：`don't` 已被译成「不」后的形态 ──
            //   实机：`火花蜱不 penetrate 你的 armor with 它的 电弧！[20]`
            //   反编译 `Combat::MeleeAttackWithWeaponInternal` 三条 `Does("don't")`：
            //       `Attacker.Does("don't") + " penetrate your armor."`
            //       `Attacker.Does("don't") + " penetrate your armor! [" + value + "]"`
            //       `Attacker.Does("don't") + " penetrate your armor with " + Attacker.its_(Weapon) + "! [" + value + "]"`
            //   ⇒ `don't → 不`（`ZhsVerbPatch`）⇒ 上面三条（都要求英文 `doesn't`）**永不命中**
            //   ★ 与 `hits`（F11）/ `fail`（㊺）/ `miss`（㊻）**同一个根因的又一支**。
            //   ★ `your`→「你」已汉化、`armor` 是 `Defender.poss("armor")` 的**字面参数**
            //     （`poss()` 只加所有格、不查表）⇒ 规则必须把 `你的 armor` 一起吃掉。
            //   ★ `its_(Weapon)` 产出「它的 电弧」⇒ 用可选组吃掉所有格，`$2` 只留武器名。
            //   ★ 实机标点是**全角「！」**（`its_(Weapon) + "! ["` 里的 `!` 已被别的收尾转全角）
            //     ⇒ 规则必须同时接受 `!` 与 `！`，否则「带武器」那两条永远不中（本轮踩到）。
            //   ★ 带值/不带值**分成两条**：用 `(?: ?\[(\d+)\])?` 这种可选组时，
            //     未匹配会让 `$N` 变空 ⇒ 译文里的 `[$N]` 输出成「[]」（本轮也踩到）。
            //   ★★ 2026-10-17 追记：这 4 条**已被上方 L362 附近那 4 条取代**（那边用的是
            //      `NOTYOU + ART` 前缀，更精确）⇒ 本段**留作记录、规则已删**，避免重复维护。

            // ⑥ `X misses you with Y! [a vs. b]`
            R(@"^" + ART + @"(" + N + @") misses you with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")!? ?\[([^\]]+)\]$",
              "$1的$2没打中你！[$3]"),
            R(@"^" + ART + @"(" + N + @") misses you with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")!$",
              "$1的$2没打中你！"),

            // ── ★★ 2026-10-17（**同族第三例**）：`Does("miss")` 已被 `ZhsVerbPatch` 译成「未命中」──
            //   反编译 `XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal` 三处 `Does("miss")`：
            //       `Attacker.Does("miss") + " you!"`
            //       `Attacker.Does("miss") + " you! [" + value + " vs. " + DV + "]"`
            //       `Attacker.Does("miss") + " you with " + Attacker.its_(Weapon) + "! [" + value + " vs. " + DV + "]"`
            //   ⇒ 到达消息口时英文 `misses` **已不存在** ⇒ 上面两条（要求 `misses`）**永不命中**。
            //   ★ 离线实测形态：`鳄鱼未命中 you with 她的 咬! [13 vs. 10]`
            //     （只做了 `you→你` 与空格清理，`with` 结构原样留着）
            //   ★ 与 `fail`（本文件上方「攻击未造成伤害」）、`hits`（F11）**同一个根因的第三例**。
            //   ★ 中文锚点「未命中」由 `ZhsVerbPatch.V`（`miss`/`misses`）稳定产出。
            R(@"^(.+?)未命中 (?:you|你) with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")!? ?\[([^\]]+)\]$",
              "$1的$2没打中你！[$3]"),
            R(@"^(.+?)未命中 (?:you|你) with (?:(?:its|his|her|their|你的|它的|他的|她的|其|该) )?(" + N + @")!$",
              "$1的$2没打中你！"),
            R(@"^(.+?)未命中 (?:you|你)! ?\[([^\]]+)\]$",
              "$1未命中你！[$2]"),
            R(@"^(.+?)未命中 (?:you|你)!$",
              "$1未命中你！"),

            // ── ★★ 2026-10-17（**同族第四~七例** · 全量排查所得）──
            //   每条的 owner 与**真实拼接**都反编译确认过（见 `_tools\_rule_owner_map.md`）：
            //   ① `XRL.World.Effects.LifeDrain::HandleEvent`
            //        `base.Object.Does("resist") + " your life drain!"`
            //   ② `XRL.World.Parts.Bed::AttemptSleep`
            //        `ParentObject.Does("don't") + " seem to be working."`
            //   ③ `XRL.World.Parts.Skill.ShortBlades_Hobble::FireEvent`
            //        `… + gameObjectParameter.GetVerb("find") + " a weakness in your defenses."`
            //   ④ `XRL.World.Parts.Skill.Shield_Slam::Slam`
            //        `TargetObject.Does("resist") + " your shield slam."`
            //   ★ 动词表译文：`resist`→抵抗 · `don't`→不 · `find`→**找到**（不是「发现」）
            //   ★ 实机形态（离线实测，修前全部只做了 `your`→`你的` 就放行）：
            //        `鳄鱼抵抗 your life drain!` · `鳄鱼不 seem to be working.`
            //        `鳄鱼找到 a weakness in your defenses.` · `鳄鱼抵抗 your shield slam.`
            R(@"^(.+?)抵抗 your life drain!$", "$1抵抗了你的生命汲取！"),
            R(@"^(.+?)不 seem to be working\.$", "$1似乎没在运作。"),
            R(@"^(.+?)找到 a weakness in your defenses\.$", "$1找到了你的防御破绽。"),
            R(@"^(.+?)抵抗 your shield slam\.$", "$1抵抗了你的盾击。"),

            // ═══════════════════════════════════════════════════════════════════
            // ★★★ 2026-10-15（N1 批次 · **消息栏侧**）
            //
            // 【来源】`_tools\E5-复核B族41与E族71.md`（E5 复核报告）判定的真缺口中，
            //   现场是 `AddPlayerMessage` / `MessageQueue.AddPlayerMessage` 的那些 ——
            //   共 **B 族 3 条（B19① B32 B30）· E 族 12 条（E2 E3 E6 E7 E16 E23 E29
            //   E30 E55 E57 E63 E66 E69）**，另含两条**收紧**（B41 见上 1384 · E33 见上 1170）。
            //
            // 【为什么统一排在表末】
            //   本段全部是 `^…$` **整串锚定 + 显式英文骨架** ⇒ 与前面的短规则不冲突；
            //   放表末可让更具体的既有规则先命中（`Rules` 是**首条命中即返回**）。
            //
            // 【★ 与报告草案的唯一偏差（有意）】
            //   草案里凡是「只吃尾巴、前缀原样」的写法，这里按**反编译原文**把固定英文
            //   骨架**显式消费**掉 —— 依据 2.16.26 的教训（`(.+)` 会把英文尾巴整段吞进
            //   捕获组，产出 `你无法闪避库祖 out of your way。` 这种半译）。
            //   ⇒ 先写**整句**规则，再留一条**兜底**（防变量形态变化）。
            //
            // 【★ 剥壳】`TranslateText` 与 `EmitMessage` Prefix 都在匹配**前**跑
            //   `StripColorTags` ⇒ 本段正则一律写**剥壳后形态**，**不写 `{{…}}`**。
            //
            // 【★ 冠词】首部捕获名字的规则统一走 `ART`（吞掉 `The ` / `A ` 这类
            //   游戏给英文名加的冠词）—— 同本文件 2.15.91 的口径。
            // ═══════════════════════════════════════════════════════════════════

            // ── B19①/B32：移动确认（消息栏）──
            //   出处 `XRL.World.GameObject::Move` L1122（反编译 `_tools\_e5_decomp`）：
            //     MessageQueue.AddPlayerMessage("Are you sure you want to move into "
            //       + gameObject6.t() + "? Move "
            //       + Directions.GetExpandedDirection(Direction) + " again to confirm.", 'R')
            R(@"^Are you sure you want to move into (.+?)\? Move (.+) again to confirm\.$",
              "确定要进入$1吗？再往$2移动一次以确认。"),
            // ── B30：下楼确认（消息栏）── 同文件 L1142
            R(@"^Are you sure you want to drop down a level\? Move (.+) again to confirm\.$",
              "确定要往下走一层吗？再往$1移动一次以确认。"),

            // ── B7（第三人称分支）：心脏除颤确认 ──
            //   出处 `AutomatedExternalDefibrillator::AttemptDefibrillate` L46：
            //     Object.T() + Object.Is + " not in cardiac arrest. Do you want to use "
            //       + ParentObject.t() + " on " + Object.them + " anyway?"
            //   ⚠ 第一人称分支（B41）是同一条 `?:` 的另一支，规则在本文件上方 1384（同批收紧）
            //   ★ 与报告草案的差异（有意）：草案用 `$3` 回填，而 `Object.them` 是**英文代词**
            //     （`them`/`it`/`him`/`her`）⇒ 会漏出英文。这里改为**不回填 `$3`**（仍消费掉），
            //     中文用中性「其」。判据：`$3` 是**游戏侧的英文代词**，不是可变实词。
            R(@"^" + ART + @"(" + N + @") (?:is|are) not in cardiac arrest\. Do you want to use (.+?) on (.+?) anyway\?$",
              "$1并未心脏骤停。仍要对其使用$2吗？"),

            // ── E2：第三人称「发现破绽」（`ShortBlades_Hobble::FireEvent` L23）──
            //     AddPlayerMessage(gameObjectParameter.The + ShortDisplayName
            //       + gameObjectParameter.GetVerb("find") + " a weakness in your defenses.", 'r')
            //   ★ 与既有第一人称规则（本文件 `^You find a weakness in (.+)$`）**对称补齐**
            R(@"^" + ART + @"(" + N + @") finds? a weakness in your defenses\.$",
              "$1发现了你防御中的破绽。"),

            // ── E3：赛博件灭火系统「洒你一身」（`CyberneticsFireSuppressionSystem::TurnTick` L21）──
            //     AddPlayerMessage("Your " + ParentObject.DisplayNameOnlyDirect + " discharges "
            //       + num + " " + ((num == 1) ? "dram" : "drams") + " of " + name + " all over you.")
            R(@"^Your (" + N + @") discharges (\d+) drams? of (" + N + @") all over you\.$",
              "你的$1喷出 $2 德拉姆$3，泼了你一身。"),
            // ── E16：普通灭火系统（`FireSuppressionSystem::CheckFireSuppression` L32）──
            //     AddPlayerMessage(num + " " + ((num == 1) ? "dram" : "drams")
            //       + " of " + name + " discharges all over you.")
            R(@"^(\d+) drams? of (" + N + @") discharges all over you\.$",
              "$1 德拉姆$2喷涌而出，泼了你一身。"),

            // ── E6/E7/E63：反冲踢三兄弟（`Tactics_Kickback::HandleEvent` L17 / L33 / L50）──
            //     E.Actor.Does("kick") + " at you, but the kick passes through you."
            //     E.Actor.Does("kick") + " at you, but you hold your ground."
            //     E.Actor.Does("kick") + " you backwards."
            R(@"^" + ART + @"(" + N + @") kicks? at you, but the kick passes through you\.$",
              "$1朝你踢来，但那一脚穿过了你。"),
            R(@"^" + ART + @"(" + N + @") kicks? at you, but you hold your ground\.$",
              "$1朝你踢来，但你稳住了身形。"),
            R(@"^" + ART + @"(" + N + @") kicks? you backwards\.$",
              "$1把你踢得倒退。"),

            // ── E69：盾击被抵抗（`Shield_Slam::Slam` L50）──
            //     AddPlayerMessage(TargetObject.Does("resist") + " your shield slam.", 'r')
            //   ★ 与既有 `resists? your life drain!`（本文件 116）**完全对称的一句**，
            //     当年只补了生命汲取那条 ⇒ 这条是**对称补齐**
            R(@"^" + ART + @"(" + N + @") resists? your shield slam\.$",
              "$1抵抗了你的盾击。"),

            // ── E66：拴附武器松开玩家（`LatchedOnto::Expired` L16）──
            //     AddPlayerMessage(颜色 + 名 + 颜色 + LatchedOnWeapon.GetVerb("release") + " you.")
            R(@"^" + ART + @"(" + N + @") releases? you\.$",
              "$1松开了你。"),

            // ── E55/E57：状态倒计时同族（**只有 berserk 走了 `_T`**，这两条是硬编码）──
            //   E55 `Cudgel_SmashingUp::FireEvent` L12：
            //       AddPlayerMessage(Duration.Things("turn remains", "turns remain")
            //                        + " until you stop demolishing.")
            //   E57 `LongBladesCore::FireEvent` L22：
            //       AddPlayerMessage(Ultmode.Things("turn remains", "turns remain")
            //                        + " until your guard is down.")
            //   ★ 变量段渲染成 `3 turns remain`（**英文**，`turns` 不在 `ZhsUnits` 的单位表里）
            //     ⇒ 先写**吃掉数字**的版本，再留通用兜底（防渲染形态变化）
            R(@"^(\d+) turns? remain until you stop demolishing\.$",
              "还剩 $1 回合，直到你停止拆毁。"),
            R(@"^(\d+) turns? remain until your guard is down\.$",
              "还剩 $1 回合，直到你架势被破。"),
            R(@"^(" + N + @") until you stop demolishing\.$",
              "$1，直到你停止拆毁。"),
            R(@"^(" + N + @") until your guard is down\.$",
              "$1，直到你架势被破。"),

            // ── E23：宠物二选一爆炸（`PetEitherOr::explode` L57）──
            //     AddPlayerMessage(ParentObject.DisplayNameOnly
            //       + " is vacuumed to another place and time. The void that remains is filled"
            //       + " with three important objects from one of your side lives.")
            //   ★ 尾段是**固定英文散文** ⇒ 整条进 pattern（一次译完，不留尾巴）
            R(@"^(" + N + @") is vacuumed to another place and time\. The void that remains is filled with three important objects from one of your side lives\.$",
              "$1被抽吸到另一个时空。留下的虚空里填着来自你某段侧生的三件重要物品。"),

            // ── E29/E30：追随者增减（`Wishing::HandleWish` L4250 / L4269，`wish` 路径）──
            //     MessageQueue.AddPlayerMessage(firstObjectWithPart2.Does("are")
            //                                   + " no longer your follower.")
            //     MessageQueue.AddPlayerMessage(firstObjectWithPart3.Does("are")
            //                                   + " now your follower.")
            R(@"^" + ART + @"(" + N + @") (?:is|are) no longer your follower\.$",
              "$1不再是你的追随者。"),
            R(@"^" + ART + @"(" + N + @") (?:is|are) now your follower\.$",
              "$1现在成了你的追随者。"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2026-10-16 F 系列（F1 · F8 · F4）——「状态 / 动作词族」整批
            //
            // 【为什么落在**本表**（同出口的第二道闸）】
            //   `MessageQueue.AddPlayerMessage` 上挂着**两个**我方 Prefix：
            //     · `ZhsFlightPatch.Prefix`（序号绑定 `ref string __0` · 先装）
            //     · `ZhsCombatMsgPatch.Prefix`（名字绑定 `ref string Message` · 后装）
            //   同一出口两道闸 ⇒ 任一道生效都能出中文；本批补在**这一道**上，
            //   与飞行补丁互为冗余（两条规则都幂等，重复命中无害）。
            // ═══════════════════════════════════════════════════════════════

            // ── F1 `X begins flying.` ──────────────────────────────────────
            //   反编译 `XRL.World.Capabilities.Flight::StartFlying`（逐字）：
            //       MessageQueue.AddPlayerMessage(Object.Does("begin") + " flying.");
            //   实证 `Player.log`：
            //       L4566 `飞行消息：巨蜻蜓 begins flying.  =>  巨蜻蜓开始飞行。`
            R(@"^" + ART + @"(" + N + @")\s+begins?\s+(?:flying|to fly)\.\s*$",
              "$1开始飞行。"),
            //   伴随形态：若动词侧先把 `begin` 译成「开始」，句尾 ` flying.` 会**留英文**
            //   （`Object.Does("begin")` → `GetVerb` → `Grammar.ThirdPerson`；
            //     对该路径的核查结论见 `_tools\_F8_状态词形态矩阵.md`）
            R(@"^" + ART + @"(" + N + @")\s*开始\s+(?:flying|to fly)\.\s*$",
              "$1开始飞行。"),

            // ── F1/F8 `X swoops down to attack.` ──────────────────────────
            //   反编译 `XRL.World.Parts.Combat::SwoopAttack`：
            //       AddPlayerMessage(Object.Does("swoop") + " down to attack.");
            //   （字面量 `swoop` / `down to attack` 同属该方法：
            //     `_tools\_census\asm-*\literal-owners.tsv`）
            //   存档实证（消息栏落盘条目）：`巨蜻蜓 swoops down to attack.`
            //   ★ 全仓**没有任何表**覆盖它 ⇒ 恒英文 ⇒ 本条是 F1/F8 的真缺口。
            R(@"^" + ART + @"(" + N + @")\s+swoops?\s+down\s+to\s+attack\.\s*$",
              "$1俯冲扑击。"),
            // ★ 2026-10-17（测试窗口 #26）：**动词先被译**的半译形态。
            //   实机：`贝撒大旋涡幽灵俯冲 down to attack.`
            //   `EmitPrefix` 只应用 `Rules`、**不调**碎片表 ⇒ 碎片表里的
            //   `down to attack → 俯冲攻击` 够不到；而 `Does("swoop")` 已把
            //   `swoops` 换成「俯冲」⇒ 上面那条英文规则也失配。
            R(@"^" + ART + @"(" + N + @")\s*俯冲\s+down\s+to\s+attack[.!]?\s*$",
              "$1俯冲扑击。"),

            // ★ 2026-10-17（测试窗口 #30）：`ShieldWall::Apply` 的硬编码尾巴
            //   实机：`沾血的圣所守护者举起他的碳化物盾 in wall formation!`
            //   `in wall formation` 是**字面量碎片**（无中文出口）⇒ 吃掉整段尾巴。
            R(@"^(" + N + @") in wall formation[.!]?$",
              "$1，组成盾墙！"),

            // ── F8 `You are fleeing from X!`（状态句）──────────────────────
            //   实证：装机目录 `_zhs_untranslated.log`（2026-10-16 13:07，本局）
            //         `[2次] 半译（中英混排） You are fleeing from 恐惧之根!`
            //   ★ `^You` 玩家形**必须显式写**（`ZhPlayerPronouns` 只认小写 `you`）
            R(@"^You are fleeing from (" + N + @")!$", "你正在逃离$1！"),

            // ═══════════════════════════════════════════════════════════════
            // ── F4 「停止移动的原因」从句族 ────────────────────────────────
            //
            // 【实证 · 装机目录 `_zhs_untranslated.log`（2026-10-16 13:07，本局）】
            //     `[4次] 半译（中英混排） 你停止移动，因为something is shooting at 你。`
            //     `[2次] 半译（中英混排） 你停止移动，因为there is in 你的 way。`
            //   ⇒ 到达出口的整句形如 `你停止移动，因为<英文从句>。`
            //
            // 【根因】`ZhsFlightMsg.Rules` 里那三条是**整句锚定**
            //     (`^something is shooting at you…$`) ⇒ 从句被夹在句中 ⇒ **恒不命中**
            //     （2.16.84 加规则时按「从句单独到达」的形态写，与实际形态不符）。
            //   而本表的 `Regex.Replace(work, repl)` 对**子串**同样生效
            //   ⇒ 用「因为」作锚即可命中。
            //
            // 【来源（反编译）】
            //   `XRL.World.Parts.MissileWeapon::FireEvent`
            //       AutoAct.Interrupt("something is shooting at you", …)
            //       AutoAct.Interrupt("something is shooting at you or " + …, …)
            //       AutoAct.Interrupt("you " + … + " something shooting at " + …, …)
            //   `XRL.World.GameObject::Move`
            //       AutoAct.Interrupt(… "there" + Is + " " + an() + " in your way", …)
            //   模板 `AutoAct Interrupt Message` = `You stop =autoActDescription= because =because=.`
            //   已由字符串表译成「你停止移动，因为=because=。」⇒ 从句**原样填空**。
            //
            // ⚠ 标点用**先行断言**收尾（不吞掉原文的「。」），译文里也不补标点。
            // ⚠ `or X` 变体必须排在裸句之前。
            // ═══════════════════════════════════════════════════════════════
            R(@"因为\s*something is shooting at you or ([^。.\n]+?)(?=[。.]|$)",
              "因为有人朝你射击，或者是$1"),
            R(@"因为\s*something is shooting at you(?=[。.]|$)",
              "因为有人朝你射击"),
            R(@"因为\s*(?:you|你)\s*\w+\s+something shooting at ([^。.\n]+?)(?=[。.]|$)",
              "因为你察觉到有东西朝$1射击"),
            // `there is a X in your way` ⇒ 补出**挡路者**（中文「挡住」必须有主语）
            R(@"因为\s*there\s+(?:is|are)\s+(?:a|an|the)?\s*([^。.\n]+?)\s+(?:are\s+)?in\s+your\s+way(?=[。.]|$)",
              "因为$1挡住了路"),
            R(@"因为\s*([^。.\n]+?)\s+(?:are\s+)?in\s+your\s+way(?=[。.]|$)",
              "因为$1挡住了路"),
            // ── F15（2026-10-18 · 同窗口探针残留）──────────────────────────
            // `IrisdualBeam.AlertTargetLine` → AutoAct.Interrupt("you are in the path of " + …)
            //   实机：`你停止移动，因为你 are in the path of 吉尔什卡斯的 irisdual beam。`
            R(@"因为\s*(?:you|你)\s+are\s+in\s+the\s+path\s+of\s+([^。.\n]+?)(?=[。.]|$)",
              "因为$1挡住了你的路"),
            // `IrisdualBeam.Apply/Remove` → Poss("rind") + " softens while " +
            //   it + GetVerb("recrystallize") + "!"
            //   实机：`吉尔什卡斯的 rind softens while 她 recrystallizes!`
            R(@"^(.+?)\s+rind\s+softens\s+while\s+(她|他|它|你)\s+recrystallizes(!?)$",
              "$1外壳软化，$2重新结晶了$3"),
            R(@"^(.+?)\s+rind\s+recrystallizes\s+and\s+hardens\s+once\s+more\.$",
              "$1外壳重新结晶，再次硬化。"),
            // `DidX("start","to shine over the full spectrum","!")` —— `start` 先被动词补丁
            //   译成「开始」⇒ `ZhsDidXZh.E` 的结构化入口（要求英文谓语）够不到补语。
            R(@"开始\s+to\s+shine\s+over\s+the\s+full\s+spectrum(!?)",
              "开始泛出全频谱的光$1"),
            // `DromadCaravan.Render` 的硬编码地标名：`A {{w|dromad}} caravan`
            //   （进规则前已剥色 ⇒ 实际形态 `A dromad caravan`；**不锚定整串** ——
            //    日志注记里它前面还有「备注：」等外壳）
            R(@"\bA\s+dromad\s+caravan", "一支驼人商队"),
            R(@"\ba\s+dromad\s+caravan", "一支驼人商队"),
};

        private static KeyValuePair<Regex, string> R(string pat, string rep)
        {
            return new KeyValuePair<Regex, string>(
                new Regex(pat, RegexOptions.Compiled), rep);
        }

        /// <summary>
        /// ★★ F6-B5（2026-10-16 · 转交项）：声望**状态句**的消息栏/弹窗形态。
        ///
        /// 复用 `ZhsPopupPatch.RepStateSentence`（**只读引用**，不改对方文件）：
        ///   · 词表 `ZhsPopupPatch.REP_STATE` 的 5 词来自 `Reputation::Modify`
        ///     反编译的 **8 个赋值点去重**（`loved/favored/indifferent/disliked/despised`）
        ///   · `You are now despised by X.` ⇒ `X现在憎恶你。`
        ///   · `X is/are now despised to you.` ⇒ `X现在对你憎恶。`
        /// 判据：**未命中 / 词不在表里 / 派系名为空 ⇒ 返回 null**（调用方放行，绝不半译）。
        /// 三个出口都调（`Prefix` / `EmitPrefix` / `TranslateText`）⇒ 与挂载顺序无关。
        /// </summary>
        private static string RepStateTry(string work)
        {
            try { return ZhsPopupPatch.RepStateSentence(work); }
            catch { return null; }
        }

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType("XRL.Messages.MessageQueue", false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null)
            {
                // ★ 2026-10-15：日志**必须**包 try —— 否则体外/异常时它会**吃掉已算好的结果**
                try { UnityEngine.Debug.LogError(Tag + "找不到 MessageQueue"); } catch { }
                return;
            }

            MethodInfo pre = typeof(ZhsCombatMsgPatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            int n = 0;

            // ★★ 2.4.8 重写：不再用 `asm.GetType(名字)` 找宿主。
            //   2.4.7 日志实证 `EmitMessage：见 0 个重载，挂上 0 个` ⇒ 名字查找没命中。
            //   反编译 `XRL.World.IComponent` 实证其签名（第 2 参就是消息文本）：
            //     public static void EmitMessage(GameObject Source, string Msg,
            //         char Color = ' ', …)
            //   ⇒ 改为**暴力遍历所有类型**，只按「方法名 EmitMessage + 静态 + 第2参是 string」收。
            var emitMethods = new List<MethodInfo>();
            var seenSig = new HashSet<string>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    var ok = new List<Type>();
                    foreach (Type tx in ex.Types) if (tx != null) ok.Add(tx);
                    all = ok.ToArray();
                }
                catch { continue; }

                foreach (Type ty in all)
                {
                    if (ty == null) continue;
                    MemberInfo[] mem;
                    try
                    {
                        mem = ty.GetMember("EmitMessage", MemberTypes.Method,
                                BindingFlags.Public | BindingFlags.NonPublic
                                | BindingFlags.Static);
                    }
                    catch { continue; }
                    foreach (MemberInfo m0 in mem)
                    {
                        MethodInfo mi = m0 as MethodInfo;
                        if (mi == null) continue;
                        ParameterInfo[] ps;
                        try { ps = mi.GetParameters(); } catch { continue; }
                        if (ps.Length < 2) continue;
                        if (ps[1].ParameterType != typeof(string)) continue;
                        if (ps[1].IsOut) continue;
                        string sig = (mi.DeclaringType != null
                                      ? mi.DeclaringType.FullName : "?") + "|" + ps.Length;
                        if (seenSig.Contains(sig)) continue;
                        seenSig.Add(sig);
                        emitMethods.Add(mi);
                    }
                }
            }
            // ★ 2026-10-15：日志**必须**包 try —— 否则体外/异常时它会**吃掉已算好的结果**
            try { UnityEngine.Debug.Log(Tag + "EmitMessage 反射到 " + emitMethods.Count + " 个候选"); } catch { }

            int emitSeen = 0, emitPatched = 0;
            MethodInfo emitPre2 = typeof(ZhsCombatMsgPatch).GetMethod(
                "EmitPrefix", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (MethodInfo mi in emitMethods)
            {
                emitSeen++;
                if (emitPre2 == null) break;
                try
                {
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.combatmsg.emit."
                        + (mi.DeclaringType != null ? mi.DeclaringType.Name : "?")
                        + "." + mi.GetParameters().Length);
                    h.Patch(mi, prefix: new HarmonyMethod(emitPre2));
                    emitPatched++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.Log(Tag + "EmitMessage 挂 "
                        + (mi.DeclaringType != null ? mi.DeclaringType.FullName : "?")
                        + " 失败：" + e.Message);
                }
            }
            UnityEngine.Debug.Log(Tag + "EmitMessage：候选 " + emitSeen
                + " 个，挂上 " + emitPatched + " 个");

            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static))
            {
                if (mi.Name != "AddPlayerMessage") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                if (ps[0].IsOut) continue;
                try
                {
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.combatmsg." + ps.Length);
                    h.Patch(mi, prefix: new HarmonyMethod(pre));
                    n++;
                }
                catch { }
            }
            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（"
                + Rules.Count + " 条模式）");
        }

        /// <summary>
        /// ★★ 专供 `EmitMessage` 的 Prefix（2.4.8）。
        ///
        /// 【为什么必须用序号传参】
        ///   2.4.6 日志实证：`Parameter "Message" not found in method …EmitMessage(…)`
        ///   —— Harmony 把 `ref string Message` 按**参数名**绑定，而目标方法
        ///   在反射下拿不到该名字 ⇒ 挂载失败。
        ///   反编译实证 `XRL.World.IComponent.EmitMessage` 的签名：
        ///     `public static void EmitMessage(GameObject Source, string Msg, char Color = ' ', …)`
        ///   ⇒ 消息在**第 2 个参数** ⇒ 用 Harmony 的序号名 `__1`。
        /// </summary>
        private static bool EmitPrefix(ref string __1)
        {
            try
            {
                if (string.IsNullOrEmpty(__1)) return true;
                _scanned++;
                if (_scanned <= 30)
                {
                    UnityEngine.Debug.Log(Tag + "扫描[Emit] #" + _scanned + "："
                        + __1.Substring(0, Math.Min(150, __1.Length)));
                }
                string s = __1;
                string work = StripColorTags(s);

                // ═══════════════════════════════════════════════════════════
                // ★★ 2.16.59 兜底：补回**丢失的玩家主语**
                //
                // 【用户截图】消息栏 `:: 点燃了篝火。` —— **缺主语**。
                //
                // 【已查清的部分】
                //   · 游戏原句由 `Messaging.XDidY` 生成，玩家路径是
                //         textBuilder.Append("You ").Append(Verb) …
                //     ⇒ 英文原句形如 `You kindled the campfire.`（有 "You "）
                //   · 我方 `ZhsMessagingVerbPatch` 只改**第 2 个参数 Verb**，
                //     不动 `"You "` 前缀 ⇒ 中间态是 `You 点燃了 篝火。`（中英混排）
                //   · 最终到达本出口时 `You ` 已不在（实测日志 L3208：
                //     `扫描[Emit] #12：点燃了篝火。`）
                //
                // 【未钉死的部分（如实记）】`You ` 是**在哪一步**被去掉的，未定位。
                //   已排除：`StripColorTags`（只剥 `{{…}}`）、
                //           `ZhsAdjunctPatch.Clean`（只删「该/一个/一名/那个」+ 处理 X of Y）。
                //   ⇒ 故此处做**实证兜底**，而不是去改那条未知路径。
                //
                // 【判据（保守，只认实证形态）】
                //   ① `You ` 紧跟中文        ⇒ 换成「你」（系统性修法，覆盖所有动词）
                //   ② 行首是 `点燃了` / `点燃` ⇒ 补「你」（截图实证的那一条）
                //   两条都只对**含中文**的消息生效 ⇒ 纯英文消息不受影响。
                // ═══════════════════════════════════════════════════════════
                {
                    string restored = RestorePlayerSubject(s);
                    if (!ReferenceEquals(restored, s) && restored != s)
                    {
                        _hits++;
                        // ★ 日志包 try（同 `TranslateText` 的坑：体外 ECall 抛异常会被
                        //   外层 catch 吞掉，连结果一起丢）
                        if (_hits <= 40)
                        {
                            try
                            {
                                UnityEngine.Debug.Log(Tag + "（补玩家主语）" + s
                                    + "  =>  " + restored);
                            }
                            catch { }
                        }
                        s = restored;
                        work = StripColorTags(s);
                    }
                }
                // ★★ 早退判据必须看**剥色后**是否有英文框架（2.4.5 的修复）。
                //   否则 `The 沾血的 野猪 takes no damage from 流血.` 这类
                //   「英文框架 + 已汉化名」的消息会被 HasCjk 直接放行 ⇒ 规则跑不到。
                bool hasEnglishFrame = HasEnglishFrame(work);
                if (HasCjk(work) && !hasEnglishFrame)
                {
                    string cleaned = ZhsAdjunctPatch.Clean(s);
                    if (!string.IsNullOrEmpty(cleaned) && cleaned != s)
                    {
                        _hits++;
                        if (_hits <= 40)
                            UnityEngine.Debug.Log(Tag + "（Emit 中文清理）" + s
                                + "  =>  " + cleaned);
                        __1 = DedupPossessive(cleaned);
                    }
                    return true;
                }
                // ★★ F6-B5（2026-10-16 · 转交项）：声望**状态句**先过一道专用改写。
                //   形态：`You are now {{R|despised}} by {{C|咬颚兽}}.`（剥壳后
                //         `You are now despised by 咬颚兽.`）
                //   若放任它往下走，会被通用规则 `^You are now (.+)\.$` 把**状态词当变量搬**
                //   ⇒ 产出「你现在是despised by 咬颚兽。」（骨架中文、词仍英文 = 半译）。
                //   ★ **只读复用** `ZhsPopupPatch.RepStateSentence`（词表 `REP_STATE` 5 词
                //     来自反编译 8 个赋值点去重；弹窗侧已用它跑过 12 项自证）
                //     —— 词表**只有一份**，本文件不另建，避免两处口径漂移。
                //   ★ 未命中返回 null ⇒ 原样往下走原规则链（**绝不半译**）。
                string repSt = RepStateTry(work);
                if (!string.IsNullOrEmpty(repSt))
                {
                    _hits++;
                    if (_hits <= 40)
                        try { UnityEngine.Debug.Log(Tag + "[Emit] （声望状态句）" + s + "  =>  " + repSt); } catch { }
                    __1 = repSt;
                    return true;
                }
                Match q = QuotedSpeech.Match(work);
                if (q.Success)
                {
                    string inner = q.Groups[3].Value;
                    foreach (var kv in Rules)
                    {
                        if (!kv.Key.IsMatch(inner)) continue;
                        string ri = ZhPlayerPronouns(kv.Key.Replace(inner, kv.Value));
                        if (ri == inner) continue;
                        work = q.Groups[1].Value + q.Groups[2].Value + ri + q.Groups[4].Value;
                        _hits++;
                        break;
                    }
                }
                foreach (var kv in Rules)
                {
                    if (!kv.Key.IsMatch(work)) continue;
                    string r = ZhPlayerPronouns(kv.Key.Replace(work, kv.Value));
                    if (r == work) continue;
                    _hits++;
                    if (_hits <= 40)
                        try { UnityEngine.Debug.Log(Tag + "[Emit] " + s + "  =>  " + r); } catch { }
                    __1 = DedupPossessive(r);
                    return true;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// 「剥色后是否仍含英文框架」—— 用于决定走规则表还是走中文清理。
        /// 实证需求：`The 沾血的 野猪 takes no damage from 流血.`
        /// 这类消息**既含中文又含英文框架**，不能因为 HasCjk 就放行。
        ///
        /// ★★ 2.15.94 重构：**词表 → 通用判据**
        ///
        ///   旧实现是一张 19 条**固定词表**（`"You "` / `" hits "` / `" revealed "` …），
        ///   每遇到一种新句式就得手工补一条 —— 实测**补不完**：
        ///     用 DLL 串堆里 13,426 条「像玩家可见消息」的字面量测，
        ///     旧词表判为「无英文框架」的有 **11,313 条（84.3%）**，
        ///     即词表只覆盖 15.7% ⇒ `consumes`（用户截图）只是被漏掉的几千分之一。
        ///
        ///   新判据（**穷举式、与词表解耦**）：
        ///     剥色后，**只要有一个拉丁词（≥3 字母）附近 ±12 字符内有汉字**
        ///     ⇒ 这就是「中文里夹着英文词」⇒ 判为有英文框架。
        ///   · 纯中文 ⇒ 无拉丁词 ⇒ false（保持早退，零开销）
        ///   · 纯英文 ⇒ 无汉字 ⇒ false（不归本链管；另有英文消息规则）
        ///   · `The 黏滑的 甲虫臀 consumes the 血染的咸的 黏滑的 骨骼.` ⇒ true ✅
        ///
        ///   离线验证（11 个用例含 5 个反例）：旧词表不符 5/11，新判据 **0/11**。
        ///   窗口敏感性：±8 / ±12 / ±16 / ±24 / ±32 全部 0/11 ⇒ 取 ±12 留余量。
        ///
        ///   ⚠ 保留 `marks` 词表作为**并集**（防止「拉丁词被剥色吃掉」的边角），
        ///     但**不再依赖它** —— 新增句式**无需**再改这里。
        /// </summary>
        private static bool HasEnglishFrame(string work)
        {
            if (string.IsNullOrEmpty(work)) return false;
            // ① 通用判据：中文里夹着拉丁词
            if (HasLatinWordNearCjk(work, 12)) return true;
            // ② 词表兜底（并集，保持向后兼容）
            foreach (string m in ENGLISH_FRAME_MARKS)
                if (work.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>旧词表（并集兜底）。★ 新增句式**不再需要**改这里。</summary>
        private static readonly string[] ENGLISH_FRAME_MARKS = {
            "You ", " hits ", " hit ", " damage", " with ", " fails ",
            " penetrat", " miss", " shoot", " in your way", "One of ",
            "no damage", "takes no", "starts bleeding", "wounds stop",
            "from another wound", "block with", "It burns", " revealed ",
        };

        private static readonly Regex LatinWord =
            new Regex(@"(?<![A-Za-z])([A-Za-z]{3,})(?![A-Za-z])", RegexOptions.Compiled);

        private static readonly Regex AnyCjk =
            new Regex(@"[\u3000-\u303f\u4e00-\u9fff\uff00-\uffef]", RegexOptions.Compiled);

        /// <summary>
        /// 「拉丁词（≥3 字母）附近 ±<paramref name="win"/> 字符内有汉字」。
        /// 该判据是**穷举**的 —— 不依赖任何人工词表。
        /// </summary>
        private static bool HasLatinWordNearCjk(string work, int win)
        {
            if (string.IsNullOrEmpty(work)) return false;
            if (!AnyCjk.IsMatch(work)) return false;     // 无汉字 ⇒ 不归本链管
            foreach (Match m in LatinWord.Matches(work))
            {
                int a = Math.Max(0, m.Index - win);
                int b = Math.Min(work.Length, m.Index + m.Length + win);
                if (AnyCjk.IsMatch(work.Substring(a, b - a))) return true;
            }
            return false;
        }

        /// <summary>
        /// ★ 清理冗余所有格标记：`的X的` → `的`（X ∈ 它的/他的/她的/你的/我的/其/该）。
        ///
        /// 日志实证（2.5.1）：
        ///   原文 `盐跳虫 fails to deal damage with 它的 attack! [10]`
        ///   名字捕获把「它的」也吃进去了 ⇒ 产出「盐跳虫**的它的**攻击未能造成伤害！」
        ///   ⇒ 这里统一收口：`盐跳虫的它的攻击` → `盐跳虫的攻击`。
        ///
        /// 适用于两个 Prefix（`Prefix` / `EmitPrefix`），幂等。
        /// </summary>
        private static string DedupPossessive(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // ① 的 + (它的|他的|她的|你的|我的|其|该) + 的  ⇒  的
            s = Regex.Replace(s, @"的(?:它的|他的|她的|你的|我的|其|该)的", "的");
            // ★ 2026-10-16（F5 · 用户截图）：**「的」+ 代词 + 名词**（末尾无「的」）的叠加
            //   实证：`咬颚拾荒者的他的攻击未能造成伤害！` / `鳄鱼的他 的攻击未能造成伤害！`
            //   成因：名词片给了「…者」+ 所有格「的」，代词槽又给「他的」⇒ 中文里重复
            //   （① 号规则要求末尾有「的」⇒ 这种形态**命不中** ⇒ 单列一条）
            //   ★ 变体容忍：代词与「的」之间**可能有空格**（实证 `鳄鱼的他 的攻击`）
            s = Regex.Replace(s, @"(?<=\u7684)(?:\u5b83|\u4ed6|\u5979|\u4f60|\u6211)\s*\u7684\s*(?=[\u4e00-\u9fff])", "");
            s = Regex.Replace(s, @"(?<=\u7684)(?:\u5176|\u8be5)\s*(?=[\u4e00-\u9fff])", "");
            // ★ 2026-10-16（F5）：所有格代词与后接中文之间的**英文语序空格**
            //   实证：`咬颚拾荒者用他的 咬没打中你！` ⇒ 去掉那个空格
            // ★ 2026-10-17：加上**复数所有格**（他们的/她们的/它们的/你们的/我们的）——
            //   用户实机 `鳄鱼用他们的 攻击未能造成伤害！` 就是这个空格。
            //   .NET 的 lookbehind 支持变长替代，所以直接并进交替即可。
            s = Regex.Replace(s, @"(?<=(?:\u4ed6|\u5b83|\u5979|\u4f60|\u6211|\u4ed6\u4eec|\u5979\u4eec|\u5b83\u4eec|\u4f60\u4eec|\u6211\u4eec)\u7684)\s+(?=[\u4e00-\u9fff])", "");
            // ② 「的的」⇒「的」（★ 存档实证 34 处）
            //    成因三类：
            //      A. poss() 输出「她的」+ 已含「的」的短语（她的的星之飞艇）
            //      B. poss() 重复包裹（征用的了他的的壤土十二面体 → ① 先收口）
            //      C. 整句替换拼接处（头盔的的幼芽 / 小的的星苹果园）
            //    这条规则对三类**都**有效，且幂等。
            s = s.Replace("的的", "的");
            // ②b ★★ 英文所有格残留（2.6.3 新增，**老存档的冻结文本只能靠这里清**）
            //
            //   实证（存档 `0a1f0278`，43 处）：
            //     `因因Ixerpater's的以离开他的的故土闻名的名声`
            //       ↑    ↑★`'s的`      ↑★`的的`
            //   成因：游戏把**裸 `'s`** 传给 `Grammar.MakePossessive`，
            //   而我方补丁当时给 `'s` 补了「的」⇒ 产出 `'s的`。
            //   （根因已在 `ZhsPossessivePatch` 修掉，但**已生成存档的文本是冻结的**，
            //     只能在这个出口清理。）
            // ★ 2.15.3 修正：`'s` 后**可能有空格**（实测 `它's 压印的…`）
            //   旧规则的零宽断言要求紧跟汉字 ⇒ 带空格的漏掉了。
            s = Regex.Replace(s, @"['\u2019]s\s*(?=[\u4e00-\u9fff])", "");
            // 顺带：`it`/`It` 被所有格引擎误译成「它」时，`它's` 已由上行清掉 `'s`，
            //   但会留下孤立的「它」⇒ 若「它」紧跟的正是被清掉的 `'s` 位置，去掉它。
            //    用 lookbehind **保留**前面的汉字（`它's 压印的` ⇒ `它压印的`）
            s = Regex.Replace(s, @"(?<=[\u4e00-\u9fff])['\u2019]s\s*(?=[\u4e00-\u9fff])", "");
            s = s.Replace("'s的", "").Replace("\u2019s的", "");
            // ②c 单字重复（『的』类助词才去重，保留「好好」「念念」等正常叠词）
            s = Regex.Replace(s, @"的{2,}", "的");
            s = Regex.Replace(s, @"之{2,}", "之");
            // ③ 「之之」同理（防御性，防模板拼接）
            s = s.Replace("之之", "之");
            // ④ 规则替换后可能留下「汉字 空格 汉字」的孤立空格
            //    （如捕获组没吃到「它的」时：`盐跳虫的 攻击`）
            s = Regex.Replace(s, @"([\u4e00-\u9fff]) +([\u4e00-\u9fff])", "$1$2");
            // ⑤ ★★ 悬空英文冠词（**运行时拼接**产生，静态文件里搜不到）
            //
            //   实证（截图 `Abappur的the栖所`）——反编译 `Qud.API.JournalMapNote::Reveal`：
            //     text = "You note the location of "
            //          + Grammar.MakeTitleCaseWithArticle(Text)      // ← 加了英文冠词 "the "
            //          + " in the {{W|" + JournalScreen.STR_LOCATIONS
            //          + " > Historic Sites}} section of your journal.";
            //   `MakeTitleCaseWithArticle` 对英文名加 `"the "`（带**尾空格**），
            //   而中文名（如「栖所」）**不带尾空格** ⇒ 拼出 `the栖所`（空格被吃）。
            //
            //   ⇒ 这里删掉**紧邻汉字**的悬空冠词/介词。
            //     只删「后面紧跟汉字」的形态 ⇒ 不会误伤正常的英文短语
            //     （实测反例：`the sultan of Qud` / `a sultan` / `an artifact`
            //       / `of the` **全部未被改动**）。
            //
            //   ⚠ 踩坑：**不能用 `\b`**！
            //     `\b(?:the|a|an|of)(?=[\u4e00-\u9fff])` 在 `Abappur的the栖所` 上**不匹配**
            //     —— 因为「的」和 `t` **都是 `\w`** ⇒ 两者之间**没有** `\b`。
            //     实测（_tools\cleantest，真 .NET Regex）：
            //       `\b` 版：`Abappur的the栖所` → 不变   ❌
            //       无 `\b` 版：`Abappur的the栖所` → `Abappur的栖所`  ✅
            // ③ 冠词 + 空格 + 汉字（2.7.2 新增，治实证漏网）
            //   截图：`The 湿的 watervine farmer采收 一个藤片。`
            //   成因：游戏硬编码 `"The "` 前缀 + 已部分汉化的显示名；
            //         而 `ZhsArticlePatch` 只处理**不定冠词**（`Grammar::A` /
            //         `IndefiniteArticle`），**定冠词 `the` 走另一条路径** ⇒ 漏掉。
            //   ⇒ 此处按**词边界**删「冠词（可带空格）+ 汉字」。
            //     要求 `(?<![A-Za-z])` 防止误伤 `breathe` / `loathe` 这类词尾。
            s = Regex.Replace(s, @"(?<![A-Za-z])(?:The|the|THE)\s+(?=[\u4e00-\u9fff])", "");
            s = Regex.Replace(s, @"(?<![A-Za-z])(?:A|a|An|an)\s+(?=[\u4e00-\u9fff])", "");
            // ② 原规则：冠词**紧跟**汉字（无空格）
            s = Regex.Replace(s, @"(?:the|a|an|of)(?=[\u4e00-\u9fff])", "");
            // ⑥ ★ 日志页签名（硬编码 `XRL.UI.JournalScreen.STR_*`）
            //
            //   反编译实证（`XRL.UI.JournalScreen`）：
            //     public static readonly string STR_LOCATIONS = "Locations";
            //     STR_CHRONOLOGY / STR_OBSERVATIONS / STR_SULTANS /
            //     STR_VILLAGES / STR_GENERAL / STR_RECIPES …
            //   ⇒ **静态 readonly 字段，改不了值** ⇒ 只能在出口按名替换。
            //
            //   ⚠ 踩坑（真 .NET 实测，_tools\e2etest）：
            //     最初写成按 `"in the X > Y section of your journal."` **整句式**替换，
            //     但规则表已经把那一整段**消费掉了** ⇒ 锚点不存在 ⇒ 页签名仍是英文
            //     （结果：`你在日志的Sultan Histories > Khuthrishid栏记下了这条信息。`）
            //   ⇒ 改为**按页签名单独替换**，不依赖任何句式。
            //     只替换**独立的**页签名（前后不是字母），避免误伤正文。
            for (int i = 0; i < TabNames.Length; i++)
            {
                s = Regex.Replace(s,
                    @"(?<![A-Za-z])" + Regex.Escape(TabNames[i]) + @"(?![A-Za-z])",
                    TabNamesZh[i]);
            }
            // ⑦ `X > Y` 分隔符统一成中文间隔号
            s = s.Replace(" > ", " › ");
            // ⑧ ★ 日志**类别名**（`Qud.API.JournalCategories` 的 const，同样硬编码）
            //   反编译实证 13 个：Artifacts / Baetyls / Dimensions / Gossip /
            //   Historic Sites / Kith and Kin / Lairs / Merchants /
            //   Natural Features / Oddities / Ruins /
            //   Ruins with Becoming Nooks / Settlements
            //   （译名与 `ZhsJournalNotePatch.Cats` 保持一致）
            for (int i = 0; i < CatNames.Length; i++)
            {
                s = Regex.Replace(s,
                    @"(?<![A-Za-z])" + Regex.Escape(CatNames[i]) + @"(?![A-Za-z])",
                    CatNamesZh[i]);
            }
            return s;
        }

        /// <summary>
        /// `Qud.API.JournalCategories` 的硬编码类别名（反编译实证，13 个）。
        /// 与 `ZhsJournalNotePatch.Cats` 保持一致。
        /// </summary>
        private static readonly string[] CatNames = {
            "Artifacts", "Baetyls", "Dimensions", "Gossip", "Historic Sites",
            "Kith and Kin", "Lairs", "Merchants", "Natural Features",
            "Oddities", "Ruins", "Ruins with Becoming Nooks", "Settlements",
        };
        private static readonly string[] CatNamesZh = {
            "器物", "贝提尔", "维度", "流言", "历史遗迹",
            "亲族与同胞", "巢穴", "商人", "自然地物",
            "奇物", "遗迹", "含蜕变角的遗迹", "聚落",
        };

        /// <summary>
        /// `XRL.UI.JournalScreen` 的硬编码页签名（`STR_*` 静态 readonly 字段）。
        /// 反编译实证，与 `ZhsJournalNotePatch.Tabs` 保持一致。
        /// </summary>
        private static readonly string[] TabNames = {
            "Locations", "Chronology", "Gossip and Lore", "Sultan Histories",
            "Village Histories", "General Notes", "Recipes",
        };
        private static readonly string[] TabNamesZh = {
            "位置", "年表", "流言与传说", "苏丹史",
            "村落史", "一般笔记", "食谱",
        };

        private static bool Prefix(ref string Message)
        {
            try
            {
                if (string.IsNullOrEmpty(Message)) return true;
                _scanned++;
                // ★★ 早退判据必须看**剥色后**是否有英文框架（2.4.5 的修复）。
                //   旧代码 `if (HasCjk(s)) { …; return true; }` 用的是**剥色前**的 s，
                //   而游戏在生成消息时**已把物件名译成中文**了，例如：
                //     `{{g|You hit {{&W|(x2)}} for 9 damage with 你的 钢制长剑! [12]}}`
                //   含「你的 钢制长剑」⇒ HasCjk 为真 ⇒ **直接 return** ⇒ 规则一条都跑不到。
                //   日志实证：扫描 #1 出现了 20 条，而「命中」记录 **0 条**。
                //   ⇒ 现在：只有「剥色后**纯中文**」才早退。
                string s = Message;
                string work = StripColorTags(s);
                if (HasCjk(work) && !HasEnglishFrame(work))
                {
                    string cleaned = ZhsAdjunctPatch.Clean(s);
                    if (!string.IsNullOrEmpty(cleaned) && cleaned != s)
                    {
                        _hits++;
                        if (_hits <= 40)
                            try { UnityEngine.Debug.Log(Tag + "（中文清理）" + s + "  =>  " + cleaned); } catch { }
                        Message = DedupPossessive(cleaned);
                    }
                    return true;
                }

                // ★★ F6-B5（2026-10-16 · 转交项）：声望**状态句**先过一道专用改写
                //   （与 `EmitPrefix` 同一处守卫，共用 `ZhsPopupPatch.RepStateSentence`；
                //     未命中 ⇒ null ⇒ 原样往下走原规则链，绝不半译）。
                string repSt = RepStateTry(work);
                if (!string.IsNullOrEmpty(repSt))
                {
                    _hits++;
                    if (_hits <= 40)
                        try { UnityEngine.Debug.Log(Tag + "（声望状态句）" + s + "  =>  " + repSt); } catch { }
                    Message = repSt;
                    return true;
                }

                // ★★ 关键：先**剥掉颜色标记**再匹配。
                //
                //   实测（_tools/test_strip_tags.py，9 正例 + 4 反例）：
                //     · 带颜色标记的形态里，乘数段可能是**嵌套颜色**
                //       `{{g|You hit {{G|(x2)}} for 4 damage …}}`
                //     · 这种嵌套**没有任何单条正则能稳妥处理**
                //       （穷举 6×3×6×2 = 216 组候选，全通过方案 **0 组**，上限 6/9）
                //     · 而**先迭代剥掉 `{{色|内容}}` → `内容`**，
                //       再用简单正则 ⇒ **9/9 命中 + 4/4 不误伤**
                //   ⇒ 代价是丢掉着色（命中消息本就是整句 `{{g|…}}` 包裹），可接受。
                //   （`work` 已在上面定义，此处不重复声明。）

                // ── 阶段一：`The zealot yells {{W|'...'}}` / `says` / `yells` 的引语
                //   外层包装与内层台词**各是独立的硬编码字符串**（反编译                //   `SixDayZealot::ZealotDeclaim` 实证），所以要先译内层、再包外层。
                Match q = QuotedSpeech.Match(work);
                if (q.Success)
                {
                    string inner = q.Groups[3].Value;
                    foreach (var kv in Rules)
                    {
                        if (!kv.Key.IsMatch(inner)) continue;
                        string ri = ZhPlayerPronouns(kv.Key.Replace(inner, kv.Value));
                        if (ri == inner) continue;
                        work = q.Groups[1].Value + q.Groups[2].Value + ri + q.Groups[4].Value;
                        _hits++;
                        break;
                    }
                }

                // ── 阶段二：整句模式 ──
                foreach (var kv in Rules)
                {
                    if (!kv.Key.IsMatch(work)) continue;
                    string r = ZhPlayerPronouns(kv.Key.Replace(work, kv.Value));
                    if (r == work) continue;
                    _hits++;
                    if (_hits <= 40)
                        try { UnityEngine.Debug.Log(Tag + s + "  =>  " + r); } catch { }
                    Message = DedupPossessive(r);
                    return true;
                }

                // ★★ 2026-10-15 **总闸门（本出口）**：规则一条都没命中时，**放行文本**
                //   也要串一次 `ZhPlayerPronouns` —— 否则中文里夹的英文 `you/your`
                //   会原样留在消息栏（与 `TranslateText` 的缺口 B 同源，一并堵上）。
                //   安全性：`ZhPlayerPronouns` 自带 `if (!HasCjk(s)) return s;`（纯英文不动）
                //   且对已中文的串幂等；这里 `Message` 仍是**原文**（未命中 ⇒ 未被改写）✓
                Message = ZhPlayerPronouns(Message);
            }
            catch (Exception e)
            {
                // ★★ 2.16.108：**这个 catch 原来是空的** —— 它把「命中之后的异常」
                //   完全吞掉。症状极具迷惑性：`Hits` 计数**涨了**（说明规则命中过），
                //   但 `Message` **没被回写** ⇒ 玩家看到的仍是英文，日志里**一个字都没有**。
                //   实证（`mods\_conndiag\`）：`Hits 0→1` 而输出未变。
                //
                //   ⚠⚠ 写**文件**而不是 `Debug.Log` ——
                //     本项目已在 `ZhsFileUtil` 里踩过同一课：
                //     日志调用本身可能抛异常，而它若排在赋值**之前**，
                //     就会把「正常的改动」也一起毁掉（本版亲历：加了 Debug.LogError 后，
                //     连原本能命中的消息也变成「未改写」）。
                //   ⇒ 记录必须是**旁路**的，绝不能在关键路径上。
                try
                {
                    string p = System.IO.Path.Combine(
                        ZhsFileUtil.WriteDir, "zhs_combatmsg_err.log");
                    if (!System.IO.File.Exists(p))
                        System.IO.File.WriteAllText(p,
                            "ZhsCombatMsgPatch.Prefix 异常记录\n", System.Text.Encoding.UTF8);
                    System.IO.File.AppendAllText(p, e.ToString() + "\n---\n",
                        System.Text.Encoding.UTF8);
                }
                catch { }
            }
            return true;
        }

        /// <summary>
        /// 迭代替换 `{{色|内容}}` → `内容`（支持嵌套）。
        /// 例：`{{g|You hit {{G|(x2)}} for 4 damage!}}` → `You hit (x2) for 4 damage!`
        /// </summary>
        private static readonly Regex ColorTag =
            new Regex(@"\{\{[^{}]*\|([^{}]*)\}\}", RegexOptions.Compiled);

        /// <summary>
        /// 兜底补回**丢失的玩家主语**（见 `EmitPrefix` 里的长注释）。
        ///
        /// 判据（保守，只认实证形态）：
        ///   ① `You ` 紧跟中文 ⇒ 换成「你」（系统性，覆盖所有 XDidY 动词）
        ///   ② 行首 `点燃了` / `点燃` ⇒ 补「你」（用户截图实证的那一条）
        /// 两者都要求**串里已有中文** ⇒ 纯英文消息一律不动。
        /// </summary>
        private static string RestorePlayerSubject(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (!HasCjk(s)) return s;

            // ① `You <中文>` → `你<中文>`（`You` 后必须紧跟空白再跟中文）
            if (s.StartsWith("You ", StringComparison.Ordinal))
            {
                char c = s.Length > 4 ? s[4] : '\0';
                if (c >= '一' && c <= '鿿')
                    return "你" + s.Substring(4);
            }

            // ② 行首光秃秃的「点燃了 / 点燃」⇒ 补主语（截图实证形态）
            if (s.StartsWith("点燃了", StringComparison.Ordinal) ||
                s.StartsWith("点燃", StringComparison.Ordinal))
                return "你" + s;

            return s;
        }

        /// <summary>
        /// ★★ 2.16.87：把**嵌在中文里的英文玩家代词**换成中文。
        ///
        /// 【为什么需要】`RestorePlayerSubject` 只处理**行首**的 `You `，
        ///   处理不了**句中做宾语**的 `you`。
        /// 【实证】用户截图：`吐籽藤蔓投出种子，击中you（x3），造成 6 点伤害!`
        ///   来源：我方第三人称投掷规则
        ///       `^(N) hits (N) with (N) \(x(\d+)\) for (\d+) damage!$`
        ///       → `$1投出$3，击中$2（x$4），造成 $5 点伤害！`
        ///   而 `Regex.Replace` 把捕获组 `$2`（=`you`）**原样**填进去。
        ///   游戏侧来源（反编译 `Messaging.XDidYToZ`）：
        ///       `Object.IsPlayer()` ⇒ `Append(PossessiveObject ? "your" : "you")`（硬编码英文）
        /// 【判据】只替换**紧邻中文**的 `you`/`your`，避免整句英文被误改：
        ///   · 前面是中文，后面是中文/标点/结尾  ⇒ `you`  → `你`
        ///   · 前面是中文，后面是中文/标点/结尾  ⇒ `your` → `你的`
        /// 【先例】本项目 `misses you with` / `bonds with you` 也是把 `you` 当宾语，
        ///   原规则里就在用「你」⇒ 本助手把这类处理**系统化**。
        /// </summary>
        private static string ZhPlayerPronouns(string s)
        {
            if (string.IsNullOrEmpty(s) || !HasCjk(s)) return s;
            // ★ 判据（2.16.87 首版写错，靠样本实测「全不变」才发现）：
            //   `击中you（x3）` 里 `you` 前面是**全角逗号**、后面是**全角左括号**
            //   ⇒ 首版 `(?<=[\u4e00-\u9fff])` + 前后字符类**都不匹配** ⇒ 一个也没命中。
            //   现改为：前允许「中文或全角标点」，后允许「中文/全角标点/空白/结束」。
            const string L = @"[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]";
            const string R = @"[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef\s]";
            // ` your` / ` your，` 等 ⇒ `你的`
            s = Regex.Replace(s, @"(?<=" + L + @")['\u2019]?s?\s*your(?=" + R + @"|$)", "你的");
            s = Regex.Replace(s, @"\byour(?=" + R + @")", "你的");
            // ` you` ⇒ `你`
            s = Regex.Replace(s, @"(?<=" + L + @")\s*you(?=" + R + @"|$)", "你");
            s = Regex.Replace(s, @"\byou(?=" + R + @")", "你");
            return s;
        }

        /// <summary>
        /// ★★ F11（2026-10-16）：**只做「所有格代词 + 空格 + 汉字」的收口**。
        ///
        /// 为什么不直接调 `DedupPossessive`：实测它会**改掉三条既有已验收行为**
        ///   （harness `-- combat` 直接从 105/0 掉到 3 FAIL）：
        ///     · `湿的 巨蜻蜓 begins flying.` ⇒ `湿的巨蜻蜓…`（④ 号「汉字 空格 汉字」）
        ///     · `You are now a 光鱼.`       ⇒ `你现在是光鱼。`（⑤ 号悬空冠词）
        ///   ⇒ 那两条行为是**别的批次有意定下的**，本批不许动（「不许回落」）。
        /// 而 F11 真正需要的只是「她的 咬」这一个形态
        ///   （`Attacker.its_(Weapon)` 硬编码 `她的` + 一个 ASCII 空格）
        ///   ⇒ 只把 `DedupPossessive` 的 **②d** 那一条单拎出来，零副作用。
        /// ★ 与 `DedupPossessive` 里那条**同一份正则文本** —— 改一处必须两处一起改。
        /// </summary>
        private static readonly Regex PossessiveSpace = new Regex(
            @"(?<=(?:\u4ed6|\u5b83|\u5979|\u4f60|\u6211|\u4ed6\u4eec|\u5979\u4eec|\u5b83\u4eec|\u4f60\u4eec|\u6211\u4eec)\u7684)\s+(?=[\u4e00-\u9fff])",
            RegexOptions.Compiled);

        private static string CollapsePossessiveSpace(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf(' ') < 0) return s;
            return PossessiveSpace.Replace(s, "");
        }

        /// <summary>★ F15-2：`…的 + 空格 + 汉字` ⇒ 去掉空格。
        /// 中文里「的」后面的空格恒不该存在（英文所有格 `X's name` 被译成 `X的 名字`
        /// 这类形态由碎片替换后才暴露，如 `吉尔什卡斯的 irisdual beam` → `吉尔什卡斯的 虹双光束`）。</summary>
        private static readonly Regex DeSpace = new Regex(
            @"(?<=[\u4e00-\u9fff]的)\s+(?=[\u4e00-\u9fff])",
            RegexOptions.Compiled);

        private static string CollapseDeSpace(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(' ') < 0) return s;
            return DeSpace.Replace(s, "");
        }

        /// <summary>
        /// ★★ 2026-10-18（测试窗口 #65）：削掉**尾部**的空白与经典色码（`&x` / `^x`）。
        ///
        /// 【为什么必须做】本文件所有规则都是 `^…$` **整串锚定**，而 .NET 的 `$`
        ///   只容忍尾部 `\n`，**不容忍空格、也不容忍色码**。
        ///   实机消息尾部经常挂着 `[19] ` / `[19]&n`（穿透计数后面就有一个空格或颜色复位），
        ///   于是那一整条规则**永远不命中** —— 症状是「同一句时好时坏」。
        ///
        /// 【实证】同一构建离线矩阵：
        ///     `…! [19]`        → 正常翻译
        ///     `…! [19]␠`       → **只改了所有格空格，骨架原样**
        ///     `…! [19]&n`      → 同上
        ///
        /// 【边界】只削**尾部**；不改 `StripColorTags` 本身
        ///   （宽度/折行那套逻辑依赖 `&` 码存在，见 AGENTS §56）。
        /// </summary>
        private static string TrimTrailingCodeSpace(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int end = s.Length;
            while (end > 0)
            {
                char c = s[end - 1];
                if (char.IsWhiteSpace(c)) { end--; continue; }
                if (end >= 2 && (s[end - 2] == '&' || s[end - 2] == '^'))
                {
                    end -= 2;
                    continue;
                }
                break;
            }
            return end == s.Length ? s : s.Substring(0, end);
        }

        private static string StripColorTags(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("{{", StringComparison.Ordinal) < 0)
                return s;
            string prev = null;
            int guard = 0;
            while (prev != s && guard++ < 8)
            {
                prev = s;
                s = ColorTag.Replace(s, "$1");
            }
            return s;
        }

        private static bool HasCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '一' && c <= '鿿') || (c >= '＀' && c <= '￯'))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// ★ 公开入口：把同一套**句式规则**应用到任意字符串（返回译后文本）。
        ///
        /// 【为什么需要它】反编译 `Qud.API.IBaseJournalEntry::DisplayMessage`：
        ///     if (NotedPrompt) { Popup.Show(Message, null, Sound); return; }
        ///     SoundManager.PlayUISound(Sound);
        ///     MessageQueue.AddPlayerMessage(Message);
        ///   ⇒ 日志笔记默认走 **Popup.Show**，**不经过** `MessageQueue`
        ///     ⇒ 本补丁挂在 `EmitMessage` 上的出口**够不到它**
        ///     ⇒ 实测症状：`You note this piece of information in the
        ///        Sultan Histories > 雷谢夫 section of your journal.`（整句英文）
        ///   由 `ZhsPopupPatch` 在弹窗出口调用本方法。
        /// </summary>
        internal static string TranslateText(string s)
        {
            ZhsHookTrace.Step("TranslateText.in", s);
            // ★★ 2026-10-17：**在规则收尾处串一次碎片字典**。
            //
            // 【为什么必须在这里串，而不是只靠 `ZhsFragmentDictPatch`】
            //   `ZhsFragmentDictPatch` 只挂 `MessageQueue.AddPlayerMessage`
            //   —— 而**弹窗走 `Popup.Show`，根本不经过 MessageQueue**
            //   （实证：callsites 里 `Popup` 类**没有任何** AddPlayerMessage 调用；
            //     弹窗由 `ZhsPopupPatch` 拦下后调本方法）。
            //   ⇒ 只在 AddPlayerMessage 挂，会让「规则没覆盖的零散碎片」
            //     在**弹窗里永远留着英文**，而且事后不进日志就再也补不到。
            //
            // 【为什么幂等、为什么不会双重翻译】
            //   碎片表只换**表里登记过的**英文碎片，且 `Apply` 是纯替换；
            //   消息栏那条路会再把 `ZhsFragmentDictPatch` 跑一遍 ⇒ 已换过就没得换。
            //
            // 【为什么放收尾而不是放开头】
            //   先让整句规则有机会吃掉整句（碎片表更"碎"，先跑反而可能拆坏整句）。
            string core = TranslateTextCore(s);
            ZhsHookTrace.Step("TranslateText.core", core);
            try
            {
                string fin = ZhsFragmentDictTable.Apply(core);
                ZhsHookTrace.Step("TranslateText.frag", fin);
                // ★ F15-2：碎片替换后才出现的「名字 + 的 + 空格 + 中文」（如
                //   `吉尔什卡斯的 irisdual beam` → `吉尔什卡斯的 虹双光束`）在这里收空格。
                fin = CollapseDeSpace(fin);
                // ★★ 2026-10-18（F15-1）：**碎片表之后必须再清一次孤立系词**。
                //   实测 `水藤 is 被摧毁!`：`destroyed` 不在动词表 ⇒ 系词留在句里；
                //   随后碎片表把 `destroyed` 换成「被摧毁」⇒ 得到「水藤 is 被摧毁」。
                //   而 `TranslateTextCore` 的系词清理跑在**碎片表之前** ⇒ 够不到这个形态。
                //   `ZhsCopula.Fix` 幂等（无变化原样返回），且只动「中文字 + ASCII 系词 + 中文字」。
                string fin2 = ZhsCopula.Fix(fin);
                if (fin2 != fin) ZhsHookTrace.Step("TranslateText.copula", fin2);
                return fin2;
            }
            catch { return core; }
        }

        private static string TranslateTextCore(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                // ★ 2.16.127：**单位词替换**（`rounds` ⇒ 回合）—— 用户指示「不管出口，全部翻译」。
                string sfx = ZhsUnits.Fix(s);
                if (sfx != s) s = sfx;
                // ★★ 2026-10-18（#65）：**先削尾部空白/色码**再进规则 ——
                //   实机 `[19] ` / `[19]&n` 会让所有 `^…$` 规则整条失配（离线可复现）。
                string work = TrimTrailingCodeSpace(StripColorTags(s));
                // 剥色后**纯中文**就不用管（与 Prefix 的早退判据一致）
                // ★ 2026-10-16（F5）：早退也要走**共用收尾** —— 截图那种「整行全中文」的
                //   消息（`咬颚拾荒者的他的攻击未能造成伤害！ [17]`）正落在这个早退上，
                //   原来直接 `return s` ⇒ 叠词永远清不掉。`DedupPossessive` 幂等，成本极低。
                if (HasCjk(work) && !HasEnglishFrame(work)) return DedupPossessive(FixChineseOnly(s));

                // ★★ F6-B5（2026-10-16 · 转交项）：声望状态句（弹窗/日志两条路都从这里过）
                string repSt = RepStateTry(work);
                if (!string.IsNullOrEmpty(repSt)) return repSt;

                // ★ 先译**页签名**（句式规则的捕获组 `(" + N + @")` 会把整段页签名
                //   一起吞掉 ⇒ 规则**不可能**命中它 ⇒ 必须单独预处理）
                string pre = TranslatePageNames(work);
                if (pre != work) work = pre;

                for (int i = 0; i < Rules.Count; i++)
                {
                    Regex re = Rules[i].Key;
                    if (!re.IsMatch(work)) continue;
                    string r = ZhPlayerPronouns(re.Replace(work, Rules[i].Value));
                    if (r != work)
                    {
                        _hits++;
                        // ★★ 2026-10-15：**日志必须包 try** —— 这里原来是裸调用，而
                        //   `UnityEngine.Debug.Log` 在体外（离线自证路由）会抛
                        //   `SecurityException: ECall methods must be packaged into a system module`
                        //   ⇒ 被本方法外层的 `catch { }` 吞掉 ⇒ **连已算好的译文一起丢**。
                        //   症状极具迷惑性：`Rules.Count` 正常、规则逻辑也对，却**原样返回英文**。
                        //   ★ 这是本项目**第三次**踩同一坑（`ZhsUnits.Fix` / `ZhsPopupPatch` 已修）
                        //     ⇒ 判据：**凡在 try 块里打日志，日志自己也要包 try**。
                        if (_hits <= 60)
                        {
                            try
                            {
                                UnityEngine.Debug.Log(Tag + "（弹窗/日志）" + work
                                    + "  =>  " + r);
                            }
                            catch { }
                        }
                        // ★★ F11（2026-10-16）：本出口原来裸 `return r` ⇒ 规则产出里
                        //   「所有格 + 英文语序空格」清不掉（实测 `猛颚战士用她的 咬命中…`），
                        //   而 `AddPlayerMessage` 出口（`Prefix`）是 `DedupPossessive(r)`
                        //   ⇒ **两条出口口径不一致**（同一条规则在消息栏对、在弹窗错）。
                        //
                        //   ⚠ 但**不能**直接改成 `DedupPossessive`：实测它会改掉三条
                        //     既有已验收行为，`-- combat` 从 105/0 掉到 3 FAIL：
                        //       · `湿的 巨蜻蜓 begins flying.` ⇒ `湿的巨蜻蜓…`（④ 汉字间空格）
                        //       · `You are now a 光鱼.`       ⇒ `你现在是光鱼。`（⑤ 悬空冠词）
                        //     ⇒ 那两条是**别的批次有意定下的**，本批不许动（「不许回落」）。
                        //   ⇒ 只把 `DedupPossessive` 的 ②d（所有格后的英文语序空格）
                        //     **单拎成纯函数**调一次 —— 零副作用，且与 `DedupPossessive`
                        //     里那条**同一份正则**（改一处要两处一起改）。
                        return CollapsePossessiveSpace(StripArticleBeforeLatin(r));
                    }
                }
            }
            catch { }
            // ★★ 2026-10-15 **总闸门**（`_tools\you路径审计-结论.md` §六第 1 条，堵「缺口 B」）：
            //   本方法原来只在**规则命中**的出口串 `ZhPlayerPronouns`，
            //   未命中的文本**原样返回** ⇒ 中文里夹的英文 `you/your` 就留在画面上
            //   （实测形态：`击中you（x3）` —— `you` 前是汉字、后是全角括号）。
            //   在这里再串一次即可覆盖**所有放行路径**：
            //     · `ZhPlayerPronouns` 自带 `if (!HasCjk(s)) return s;` ⇒ 纯英文句不动 ✓
            //     · 对已中文的串幂等（正则要求前后是中文/全角标点）✓
            //     · 与上面两个「命中出口」串两次也无害（幂等）✓
            // ★★ 2026-10-15：**放宽分支**也接在这里（`ZhsDidXZh.RelaxedVerb`）——
            //   覆盖那 88 处「谓词是纯变量（已是中文）」的 DidX 家族消息；
            //   动词表查不到 ⇒ 原样返回（不静默丢实义）✓
            string rv = ZhsDidXZh.RelaxedVerb(s);
            // ★ 2026-10-16（F5）：**共用收尾** —— `DedupPossessive` 原来只在 EmitMessage 路径调，
            //   弹窗路径与 harness 都测不到；两条出口共用同一收尾函数（§36，与挂载顺序无关）
            return ZhPlayerPronouns(DedupPossessive(FixChineseOnly(rv)));
        }

        /// <summary>
        /// **纯中文消息**的收尾修整（测试窗口 #45 / #37 同族）。
        ///
        /// 放在这里的原因：`HasCjk && !HasEnglishFrame` 的早退在规则表**之前**返回，
        /// 这类文本永远轮不到消息规则 ⇒ 只能在这里做「中文里还夹着英文碎片」的定点修整。
        ///
        /// #45：`ScriptCallToArms::GenerateResultConversation` 的模板里
        ///   `=books.cardinal.no=` 在 0 时展开成英文 `no` ⇒ `no 本书被焚`。
        ///   ⇒ `no` + 量词 + 名词 ⇒ `没有` + 名词（去掉量词更自然）。
        /// </summary>
        private static string FixChineseOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // ★ 2026-10-18：**只对中文消息生效** —— 否则 `You have no inventory!` 会被
            //   兜底的 `no `→`没有` 弄成半英半中（实测两条残留）。
            if (!HasCjk(s)) return s;

            // #1：`…迫害的的蛋糕…` —— 叠「的」一律合一（中文里 `的的` 恒错）
            while (s.IndexOf("的的", StringComparison.Ordinal) >= 0)
                s = s.Replace("的的", "的");
            // #37：`达迪坐到 轮椅。` → `达迪坐到轮椅上。`
            //   （动词表已把 `sit down on` 译成「坐到」；这里补回被吞掉的 `on`）
            if (s.IndexOf("坐到", StringComparison.Ordinal) >= 0)
            {
                s = Regex.Replace(s, @"^(.+?坐到)\s*([^，。！？,]{1,20})([。！])$", "$1$2上$3");
            }
            if (s.IndexOf("no ", StringComparison.Ordinal) >= 0)
            {
                // `还有 no 件家具` → `还没有家具`（否则会成「还有没有」）
                s = s.Replace("还有 no ", "还没有");
                s = Regex.Replace(s, @"还没有\s*(.)(书|书架|作物|小玩意儿|家具)", "还没有$2");
                // `no 本书被焚` → `没有书被焚` · `no 座书架被毁` → `没有书架被毁`
                s = Regex.Replace(s, @"\bno\s+(.)(书|书架|作物|小玩意儿|家具)", "没有$2");
                // 兜底：剩余的 `no ` 一律「没有」
                s = s.Replace("no ", "没有");
            }

            // F13 #2 附：`你与该 Umas的村民的声望…` —— 中文冠词「该」被拼到拉丁专名前。
            //   `ZhsAdjunctPatch.Clean` 只管显示名出口（GameObjectReplacers / FactionReplacers）；
            //   声望弹窗走 `Reputation::Modify` 直接取 faction 名 ⇒ 够不到那里。
            //   ★ 规则命中路径在 L3813 **提前 return**，所以两处都要调（见该方法注释）。
            s = StripArticleBeforeLatin(s);

            // ★ F15-3（2026-10-18）：`发光蛾倒下 倒地。`
            //   动词表把 `falls` 译成「倒下」，碎片表又把 `to the ground` 译成「倒地」
            //   ⇒ 同义叠加 + 中间空格。中文里「倒下」与「倒地」二选一即可。
            if (s.IndexOf("倒下", StringComparison.Ordinal) >= 0)
                s = Regex.Replace(s, @"倒下\s*倒地", "倒地");
            return s;
        }

        /// <summary>
        /// 删掉**紧贴拉丁字母**的中文冠词「该」（中文里 `该 Xxx` 恒为冠词残留）。
        /// ★ 只认「该」：`一个 NPC` 这类是正常中文，不能动。
        /// </summary>
        internal static string StripArticleBeforeLatin(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('该') < 0) return s;
            return Regex.Replace(s,
                @"(?:(?<=[\u3000-\u303f\u4e00-\u9fff\uff00-\uffef])|^)该\s*(?=[A-Za-z])", "");
        }

        /// <summary>日志的页签名（与 `ZhsFilterBarPatch` 的页签译文保持一致）。</summary>
        private static readonly Dictionary<string, string> PageNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Locations",         "地点" },
            { "Chronology",        "年表" },
            { "Gossip and Lore",   "流言与传说" },
            { "Sultan Histories",  "苏丹史" },
            { "Village Histories", "村落史" },
            { "General Notes",     "一般笔记" },
            { "Observations",      "见闻" },
            { "Recipes",           "食谱" },
            { "Journal",           "日志" },
        };

        /// <summary>
        /// 日志**分类名**（`Qud.API.JournalCategories` 的 13 个 const）。
        ///
        /// 【为什么必须单独一张表】`Reveal` 族的句子形态是
        ///     `in the {{W|Locations > Historic Sites}} section of your journal.`
        /// ⇒ `>` **左边**是页签名（`Locations`，在 `PageNames` 里）、
        ///   **右边**是分类名（`Historic Sites`）。
        ///   而 `PageNames` 只为页签名建表 ⇒ 分类名永远查不到 ⇒ 留英文。
        ///
        /// 【实证】2.16.93 前的 `Player.log`：
        ///     战斗消息接管：（页签名）Locations > Historic Sites  =>  地点 > Historic Sites
        ///     战斗消息接管：（弹窗/日志）… in the 地点 > Historic Sites section of your journal.
        ///       =>  你在日志的地点 > Historic Sites栏记下了Mamor的位置。
        ///   —— `Locations` 译成了「地点」（在 `PageNames`），`Historic Sites` 没译（不在表里）。
        ///
        /// ★ 官方本来就为分类名提供了本地化口：
        ///     `JournalCategories.GetCategoryDisplayName(c)`
        ///       → `Strings._S("journal category", c)`
        ///   而我方 `Strings.xml` 的 `Context="journal category"` **13 条全有译文**
        ///   ⇒ 这里只是把那张表**镜像**过来给纯字符串后处理用
        ///     （不反射调官方 API：本类是无状态字符串替换，不依赖加载时机）。
        /// ★ 维护口径：改这里 ⇒ 同步 `Strings.xml` 的 `journal category` 与
        ///   `ZhsJournalNotePatch.Cats`（同一份译名，三处必须一致）。
        ///   译名权威来源 = `Strings.xml`（用户可见的那一份）。
        /// </summary>
        private static readonly Dictionary<string, string> CategoryNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Artifacts",                 "器物" },
            { "Baetyls",                   "贝提尔" },
            { "Dimensions",                "维度" },
            { "Gossip",                    "流言" },
            { "Historic Sites",            "历史遗迹" },
            { "Kith and Kin",              "亲族与同胞" },
            { "Lairs",                     "巢穴" },
            { "Merchants",                 "商人" },
            { "Natural Features",          "自然地物" },
            { "Oddities",                  "奇物" },
            { "Ruins",                     "遗迹" },
            { "Ruins with Becoming Nooks", "含蜕变角的遗迹" },
            { "Settlements",               "聚落" },
        };

        /// <summary>
        /// 把 `A > B` 这一级的页签名逐段译成中文（`A` 可能还带英文冠词 `the `）。
        /// 例：`Sultan Histories > Resheph` ⇒ `苏丹史 > Resheph`
        ///
        /// 【为什么必须单独做】句式规则的中间捕获组是 `(" + N + @")`（`N = [^\n\[\]]+?`），
        /// 它会把**整段页签名一起吞掉**，所以句式规则**不可能**命中页签名本身。
        /// 实测症状（2.15.61 前）：
        ///     `你在日志的Sultan Histories > 雷谢夫栏记下了这条信息。`
        /// </summary>
        private static string TranslatePageNames(string s)
        {
            // ★★ 2.16.66 修（用户截图 `你在日志的Sultan Histories > 雷谢夫栏记下了这条信息。`）
            //
            // 【旧实现的两个病根，叠加才暴露】
            //   ① `if (s.IndexOf("section of your journal") < 0) return s;`
            //      —— 而 `ZhsJournalNotePatch` 的**已汉化**形态（`…栏记下了这条信息。`）
            //         **不含** `section of your journal` ⇒ 这句直接放行。
            //   ② `int a = s.IndexOf("{{W|"); if (a < 0) return s;`
            //      —— **强制要求色标**。可实证日志里那条消息到达本函数时
            //         `{{W|` **已经不存在**（上游剥色）：
            //           战斗消息接管：（弹窗/日志）You note this piece of information
            //             in the Sultan Histories > 雷谢夫 section of your journal.
            //         ⇒ `a < 0` ⇒ 再次直接返回。
            //   ⇒ 两条早退把函数变成**永不执行**，页签名于是永远英文。
            //
            // 【修法】不再依赖「色标」与「英文句式」这两个前提：
            //   把 `{{W|…}}` 与裸形态**统一**成「前缀 + 内容 + 后缀」三片，
            //   在内容里按 ` > ` 逐段查 `PageNames`；认不出就整体不动。
            //   保留早退只为省开销，但条件放宽为「认得出页签名才继续」。
            const string PREFIX = "{{W|";
            int a = s.IndexOf(PREFIX, StringComparison.Ordinal);
            int bodyStart, bodyEnd;
            if (a >= 0)
            {
                int b = s.IndexOf("}}", a + PREFIX.Length, StringComparison.Ordinal);
                if (b < 0) return s;
                bodyStart = a + PREFIX.Length;
                bodyEnd = b;
            }
            else
            {
                // ★ 2.16.93 起支持**两条**裸句式（此前只有第一条）：
                //   ① 英文原句：`in the {PAGE > CAT} section of your journal.`
                //   ② **已汉化句**：`你在日志的{PAGE > CAT}栏记下了…`
                //
                //   ②为什么必需：本函数总是**在下游**被调用，而
                //   `JournalNoteTag` 的两条句式规则（L392-L400）会先把
                //   `You note the location of X in the …` 整句换成
                //   `你在日志的$2栏记下了$1的位置。` —— `$2` 里原样保留着
                //   未译的 `地点 > Historic Sites`。此时句子**已不含**
                //   `in the … section of your journal` ⇒ ①定位不到
                //   ⇒ 直接 `return s` ⇒ 分类名永远漏。
                //
                //   ★ 实证（2.16.93 前 Player.log）：
                //       战斗消息接管：（页签名）Locations > Historic Sites  =>  地点 > Historic Sites
                //       战斗消息接管：（弹窗/日志）… in the 地点 > Historic Sites section of your journal.
                //         =>  你在日志的地点 > Historic Sites栏记下了Mamor的位置。
                //     —— 两行是**两条路径**：`TranslateText` 内部那行（英文句，走①）
                //        译出了 `地点`（`Locations` 在 `PageNames`）；
                //        而最终 popup 那条已汉化句**根本没被本函数处理过**。
                const string LEAD_EN = "in the ";
                const string TAIL_EN = " section of your journal";
                int p = s.IndexOf(LEAD_EN, StringComparison.Ordinal);
                if (p >= 0)
                {
                    bodyStart = p + LEAD_EN.Length;
                    int q = s.IndexOf(TAIL_EN, bodyStart, StringComparison.Ordinal);
                    bodyEnd = q >= 0 ? q : s.Length;
                }
                else
                {
                    // ② 已汉化句式：`的` 与 `栏` 之间就是 `PAGE > CAT`
                    int z = s.IndexOf("的", StringComparison.Ordinal);
                    int y = (z >= 0) ? s.IndexOf("栏", z + 1, StringComparison.Ordinal) : -1;
                    if (z < 0 || y < 0) return s;          // 认不出句式 ⇒ 不动
                    bodyStart = z + 1;
                    bodyEnd = y;
                }
            }
            if (bodyEnd <= bodyStart) return s;

            string inner = s.Substring(bodyStart, bodyEnd - bodyStart);
            if (!inner.Contains(" > ")) return s;

            string[] parts = inner.Split(new[] { " > " }, StringSplitOptions.None);
            bool changed = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                string bare = p;
                string art = "";
                if (bare.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                {
                    art = bare.Substring(0, 4);
                    bare = bare.Substring(4);
                }
                string zh;
                // ★ 2.16.93：两张表串联查 —— 先页签名、再分类名。
                //
                //   实证病灶（改前 Player.log）：
                //     战斗消息接管：（页签名）Locations > Historic Sites  =>  地点 > Historic Sites
                //   ⇒ `Locations` 命中 `PageNames`、`Historic Sites` **两张表都不在**
                //     （它只在 C# 硬编码的 `Reveal`.`else if (Category == …)` 里出现）
                //   ⇒ 整段 `changed` 虽为真（左半译出了），右半却留英文。
                //
                //   两张表**可以安全串联**：页签名是「Locations / Sultan Histories / …」，
                //   分类名是「Historic Sites / Lairs / …」，**无一同名**；
                //   `Ruins` 只出现在分类表（`PageNames` 里没有它）。
                //   ⇒ 顺序不影响结果，仅为确定性先查页签名。
                if (PageNames.TryGetValue(bare, out zh)
                    || CategoryNames.TryGetValue(bare, out zh))
                {
                    parts[i] = art + zh;
                    changed = true;
                }
            }
            if (!changed) return s;                // 一段都没认出 ⇒ 整体不动（护栏）
            string joined = string.Join(" > ", parts);
            _hits++;
            // ★ 2026-10-15：日志**必须**包 try —— 否则体外/异常时它会**吃掉已算好的结果**
            try { UnityEngine.Debug.Log(Tag + "（页签名）" + inner + "  =>  " + joined); } catch { }
            return s.Substring(0, bodyStart) + joined + s.Substring(bodyEnd);
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：扫描 " + _scanned
                + " 条，改写 " + _hits + " 条");
        }
    }
}
