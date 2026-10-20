// ═══════════════════════════════════════════════════════════════════════
//  ZhsFragmentDictTable —— **自动生成，请勿手改**
//  生成器：_tools\_gen_frag_table.py（源表 _tools\_zh_fragments.py）
//  条数：255 · 按长度降序（运行时顺序替换 = 贪心最长匹配）
//
//  ★ 为什么表与「编译/替换」逻辑都放这个文件：
//    `ZhsFragmentDictPatch.cs` 引用了 `XRL.Messages.MessageQueue`，
//    被本地 `mods\_compilecheck` **排除**；本文件是**纯 C#**（只用 System.*），
//    因此留在这里才能被四步收尾的 `dotnet build` 覆盖到。
// ═══════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFragmentDictTable
    {
        /// <summary>编译好的碎片规则（表按长度降序 ⇒ 顺序替换即贪心最长匹配）。</summary>
        internal static readonly List<KeyValuePair<Regex, string>> Rules =
            new List<KeyValuePair<Regex, string>>();

        /// <summary>把一条英文碎片编译成带词边界的正则。</summary>
        /// ★ 左侧恒不接字母与点；**右侧只在碎片以字母结尾时**要求不接字母
        ///   —— 以空格/标点结尾的是「前缀碎片」（如 `Because they despise `），
        ///      后面紧跟名字（`Elder 伊鲁达德`）是**正常**的，不能拦。
        internal static Regex Compile(string fragment)
        {
            // ★ 只看**字面最后一个字符**：以字母结尾才要求右边界。
            //   "Because they despise " 以空格结尾 = 前缀拼接件 ⇒ **不加**（后面紧跟名字是正常的）
            string right = "";
            if (fragment.Length > 0)
            {
                char c = fragment[fragment.Length - 1];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) right = "(?![A-Za-z])";
            }
            return new Regex("(?<![A-Za-z.])" + Regex.Escape(fragment) + right,
                RegexOptions.Compiled);
        }

        /// <summary>★ 对**定稿文本**做一次贪心最长匹配替换（幂等；含中文也照样只换登记的碎片）。</summary>
        internal static string Apply(string Text)
        {
            if (Text == null || Text.Length == 0 || Rules.Count == 0) return Text;
            string neo = Text;
            for (int i = 0; i < Rules.Count; i++)
            {
                Regex rx = Rules[i].Key;
                if (!rx.IsMatch(neo)) continue;
                neo = rx.Replace(neo, Rules[i].Value);
            }
            return neo;
        }

        /// <summary>自检：已知句 + 已知反例，返回替换前后清单。</summary>
        internal static string SelfCheck()
        {
            string[] cases =
            {
                "湿的巨蜻蜓俯冲 down to attack.",
                "may be ineffective unless stocked.",
                "into pull",
                "The 湿的 光鱼 down to attack.",
                "你已经在地上 on the ground",
            };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < cases.Length; i++)
                sb.Append("  ").Append(cases[i]).Append("  ⇒  ").Append(Apply(cases[i])).Append('\n');
            return sb.ToString();
        }

        internal static void Load()
        {
            Rules.Clear();
            KeyValuePair<Regex, string>[] rows =
            {
                new KeyValuePair<Regex, string>(Compile("and neither BaseObject nor ExcludeFromDynamicEncounters"), "BaseObject 与 ExcludeFromDynamicEncounters 都没有"),
                new KeyValuePair<Regex, string>(Compile("Something prevents you from dominating"), "有什么在阻止你支配"),
                new KeyValuePair<Regex, string>(Compile("There's no magnetic containment inside"), "里面没有磁约束"),
                new KeyValuePair<Regex, string>(Compile("Attempting to generate reserved zone."), "正在生成预留区域。"),
                new KeyValuePair<Regex, string>(Compile("You can't aggressively lunge through"), "你不能强行扑击穿过"),
                new KeyValuePair<Regex, string>(Compile("Your cloning capacity is refreshed."), "你的克隆容量已恢复。"),
                new KeyValuePair<Regex, string>(Compile("mandated association with trolls in"), "与巨魔的强制关联"),
                new KeyValuePair<Regex, string>(Compile("no object in ObjectLeavingCellEvent"), "ObjectLeavingCellEvent 里没有对象"),
                new KeyValuePair<Regex, string>(Compile("Your body flushes with adrenaline!"), "肾上腺素涌遍你全身！"),
                new KeyValuePair<Regex, string>(Compile("may be ineffective unless stocked."), "若不装填可能无效。"),
                new KeyValuePair<Regex, string>(Compile("Attempting to thaw reserved zone."), "正在解冻预留区域。"),
                new KeyValuePair<Regex, string>(Compile("sleep mode by emergency interrupt"), "因紧急中断而进入休眠模式"),
                new KeyValuePair<Regex, string>(Compile("No valid targets in that square."), "那一格没有合法目标。"),
                new KeyValuePair<Regex, string>(Compile("There's nothing there to hobble."), "那里没有可以致跛的目标。"),
                new KeyValuePair<Regex, string>(Compile("Your bilge sphincter is missing."), "你的舱底括约肌缺失。"),
                new KeyValuePair<Regex, string>(Compile("shell glimmers with unseen light"), "外壳闪着不可见的光"),
                new KeyValuePair<Regex, string>(Compile("The limb rejects the infection!"), "那条肢体排斥了感染！"),
                new KeyValuePair<Regex, string>(Compile("The polygel morphs into another"), "聚合胶变成了另一种"),
                new KeyValuePair<Regex, string>(Compile("There's nothing there to shank."), "那里没有可以捅刺的目标。"),
                new KeyValuePair<Regex, string>(Compile("You climb into the sarcophagus."), "你爬进了石棺。"),
                new KeyValuePair<Regex, string>(Compile("You feel increasingly unstable."), "你感到越来越不稳定。"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Bethesda Susa."), "你跋涉到了贝塞斯达·苏萨。"),
                new KeyValuePair<Regex, string>(Compile("under the pressure of normality"), "在常态的压力下"),
                new KeyValuePair<Regex, string>(Compile("Launch spaceship and end game?"), "发射飞船并结束游戏？"),
                new KeyValuePair<Regex, string>(Compile("befriened fungi and contracted"), "与真菌为友并缔结"),
                new KeyValuePair<Regex, string>(Compile("You crossed into Brightsheol."), "你跨入了光灵界（Brightsheol）。"),
                new KeyValuePair<Regex, string>(Compile("Your mind was stranded inside"), "你的意识被困在了"),
                new KeyValuePair<Regex, string>(Compile("Your psyche is too exhausted."), "你的精神力已经枯竭。"),
                new KeyValuePair<Regex, string>(Compile("a definitely unexpected three"), "确实是意外的三"),
                new KeyValuePair<Regex, string>(Compile("spacetime in the local region"), "局部区域的时空"),
                new KeyValuePair<Regex, string>(Compile(" and begin the water ritual."), "，并开始了水之仪式。"),
                new KeyValuePair<Regex, string>(Compile("The sprayer head won't move."), "喷头纹丝不动。"),
                new KeyValuePair<Regex, string>(Compile("You have completed the quest"), "你完成了任务"),
                new KeyValuePair<Regex, string>(Compile("Your healing is interrupted!"), "你的治疗被打断了！"),
                new KeyValuePair<Regex, string>(Compile("Your heart rate slows again."), "你的心率重新慢了下来。"),
                new KeyValuePair<Regex, string>(Compile("a weakness in your defenses."), "你防御中的一处弱点。"),
                new KeyValuePair<Regex, string>(Compile("is solid and has RenderLayer"), "是实心的且有 RenderLayer"),
                new KeyValuePair<Regex, string>(Compile("The experience is fleeting."), "这种感受转瞬即逝。"),
                new KeyValuePair<Regex, string>(Compile("You feel shaken and infirm."), "你感到身心俱损。"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Grit Gate."), "你跋涉到了砂砾门。"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Omonporch."), "你跋涉到了奥蒙波奇。"),
                new KeyValuePair<Regex, string>(Compile("and begin the water ritual."), "并开始了水之仪式。"),
                new KeyValuePair<Regex, string>(Compile("brain begins to hemorrhage."), "大脑开始出血。"),
                new KeyValuePair<Regex, string>(Compile("into another cardiac arrest"), "再次陷入心脏骤停"),
                new KeyValuePair<Regex, string>(Compile("The soothing tingle fades."), "那阵抚慰的酥麻消退了。"),
                new KeyValuePair<Regex, string>(Compile("There are hostiles nearby!"), "附近有敌人！"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Golgotha."), "你跋涉到了各各他。"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Kyakukya."), "你跋涉到了基亚库基亚。"),
                new KeyValuePair<Regex, string>(Compile("You journeyed to Red Rock."), "你跋涉到了红岩。"),
                new KeyValuePair<Regex, string>(Compile("Your lunge is interrupted."), "你的扑击被打断了。"),
                new KeyValuePair<Regex, string>(Compile("a stream of corrosive acid"), "一股腐蚀性酸液"),
                new KeyValuePair<Regex, string>(Compile("and cannot be poured from."), "且无法从中倒出。"),
                new KeyValuePair<Regex, string>(Compile("and cannot be poured into."), "且无法倒入其中。"),
                new KeyValuePair<Regex, string>(Compile("and dissolved the Klanq on"), "并溶解了上面的克兰克"),
                new KeyValuePair<Regex, string>(Compile("condition this does little"), "这种情况下这没什么用"),
                new KeyValuePair<Regex, string>(Compile("into their fold and taught"), "纳入门下并传授了"),
                new KeyValuePair<Regex, string>(Compile("no duplicate recipes found"), "未发现重复的配方"),
                new KeyValuePair<Regex, string>(Compile("units of electrical charge"), "单位电荷"),
                new KeyValuePair<Regex, string>(Compile("A sewage eel wraps itself"), "一条污水鳗缠住了自己"),
                new KeyValuePair<Regex, string>(Compile("Attack in what direction?"), "朝哪个方向攻击？"),
                new KeyValuePair<Regex, string>(Compile("Failed to rebuild body as"), "重建躯体失败，因为"),
                new KeyValuePair<Regex, string>(Compile("Select your maker's mark."), "选择你的匠人印记。"),
                new KeyValuePair<Regex, string>(Compile("The sarcophagus is inert."), "石棺毫无反应。"),
                new KeyValuePair<Regex, string>(Compile("You are already burrowed."), "你已经钻入地下了。"),
                new KeyValuePair<Regex, string>(Compile("You have failed the quest"), "你任务失败了"),
                new KeyValuePair<Regex, string>(Compile("You have no supplies that"), "你没有可用于"),
                new KeyValuePair<Regex, string>(Compile("You have rapidly advanced"), "你迅速进阶了"),
                new KeyValuePair<Regex, string>(Compile("You regain your bearings."), "你重新辨明了方向。"),
                new KeyValuePair<Regex, string>(Compile("You return to the ground."), "你回到了地面。"),
                new KeyValuePair<Regex, string>(Compile("Your lunge passes through"), "你的扑击穿过了"),
                new KeyValuePair<Regex, string>(Compile("a pool of stinking liquid"), "一滩发臭的液体"),
                new KeyValuePair<Regex, string>(Compile("that you are enclosed by."), "你所处的围合结构。"),
                new KeyValuePair<Regex, string>(Compile("until your guard is down."), "直到你放松警惕。"),
                new KeyValuePair<Regex, string>(Compile("your wound is an illusion"), "你的伤口是幻觉"),
                new KeyValuePair<Regex, string>(Compile("The svardym egg hatches."), "斯瓦德姆的卵孵化了。"),
                new KeyValuePair<Regex, string>(Compile("You don't have a tongue!"), "你没有舌头！"),
                new KeyValuePair<Regex, string>(Compile("You muse over the secret"), "你思索着那个秘密"),
                new KeyValuePair<Regex, string>(Compile("Your wish is my command!"), "你的愿望就是我的命令！"),
                new KeyValuePair<Regex, string>(Compile("a vast freezing ray from"), "一道巨大的冰冻射线自"),
                new KeyValuePair<Regex, string>(Compile("an electromagnetic pulse"), "一道电磁脉冲"),
                new KeyValuePair<Regex, string>(Compile("blown forcefully back by"), "被强力吹回，来自"),
                new KeyValuePair<Regex, string>(Compile("loosens and breaks away."), "松脱并脱落了。"),
                new KeyValuePair<Regex, string>(Compile("no longer your follower."), "不再是你的追随者。"),
                new KeyValuePair<Regex, string>(Compile("Move in what direction?"), "朝哪个方向移动？"),
                new KeyValuePair<Regex, string>(Compile("Pick a filter to change"), "选择要更改的过滤器"),
                new KeyValuePair<Regex, string>(Compile("The svardym eggs hatch."), "斯瓦德姆的卵孵化了。"),
                new KeyValuePair<Regex, string>(Compile("You abide the memory of"), "你承载着这段记忆："),
                new KeyValuePair<Regex, string>(Compile("You are able to collect"), "你能够采集"),
                new KeyValuePair<Regex, string>(Compile("You are promoted to the"), "你被提升为"),
                new KeyValuePair<Regex, string>(Compile("You can't deploy there!"), "你不能在那里展开！"),
                new KeyValuePair<Regex, string>(Compile("You feel less feverish."), "你的发热减轻了。"),
                new KeyValuePair<Regex, string>(Compile("You have increased your"), "你提升了自己的"),
                new KeyValuePair<Regex, string>(Compile("You no longer feel ill."), "你不再感到不适。"),
                new KeyValuePair<Regex, string>(Compile("change their opinion of"), "改变他们对……的看法"),
                new KeyValuePair<Regex, string>(Compile("method of construction."), "的建造方式。"),
                new KeyValuePair<Regex, string>(Compile("of your cells to supply"), "你的细胞来供给"),
                new KeyValuePair<Regex, string>(Compile("overwhelmed with terror"), "被恐惧压倒"),
                new KeyValuePair<Regex, string>(Compile("pours out all over you!"), "全部倾倒在你身上！"),
                new KeyValuePair<Regex, string>(Compile("seem to understand you."), "似乎能听懂你的话。"),
                new KeyValuePair<Regex, string>(Compile("with life and arranging"), "赋予生命，并安排"),
                new KeyValuePair<Regex, string>(Compile("It is already daytime."), "现在已经是白天了。"),
                new KeyValuePair<Regex, string>(Compile("While traveling around"), "在……一带旅行时"),
                new KeyValuePair<Regex, string>(Compile("While wandering around"), "在……一带游荡时"),
                new KeyValuePair<Regex, string>(Compile("You can't imprint yet."), "你还不能铭印。"),
                new KeyValuePair<Regex, string>(Compile("You have no inventory!"), "你的物品栏是空的！"),
                new KeyValuePair<Regex, string>(Compile("You'll have to pony up"), "你得掏钱"),
                new KeyValuePair<Regex, string>(Compile("a swatch of frost webs"), "一片霜网"),
                new KeyValuePair<Regex, string>(Compile("and gain some insight."), "并获得一些洞见。"),
                new KeyValuePair<Regex, string>(Compile("and holds you in place"), "并把你定在原地"),
                new KeyValuePair<Regex, string>(Compile("no usable base genders"), "没有可用的基础性别"),
                new KeyValuePair<Regex, string>(Compile("opponent's reaction to"), "对手对……的反应"),
                new KeyValuePair<Regex, string>(Compile("plans to return to it!"), "打算回到那里！"),
                new KeyValuePair<Regex, string>(Compile("the secret to becoming"), "成为……的秘诀"),
                new KeyValuePair<Regex, string>(Compile("who can see this item."), "能看到这件物品的人。"),
                new KeyValuePair<Regex, string>(Compile("Because they despise "), "因为他们鄙视"),
                new KeyValuePair<Regex, string>(Compile("Because they dislike "), "因为他们不喜欢"),
                new KeyValuePair<Regex, string>(Compile("The gates swing wide."), "大门敞开了。"),
                new KeyValuePair<Regex, string>(Compile("You are not enclosed."), "你没有被围住。"),
                new KeyValuePair<Regex, string>(Compile("You can't be mutated."), "你无法被变异。"),
                new KeyValuePair<Regex, string>(Compile("You can't recoil yet."), "你还不能回溯。"),
                new KeyValuePair<Regex, string>(Compile("You fail to extricate"), "你没能挣脱"),
                new KeyValuePair<Regex, string>(Compile("back into hibernation"), "重新进入休眠"),
                new KeyValuePair<Regex, string>(Compile("because they despise "), "因为他们鄙视"),
                new KeyValuePair<Regex, string>(Compile("because they dislike "), "因为他们不喜欢"),
                new KeyValuePair<Regex, string>(Compile("blown into the air by"), "被吹到空中，来自"),
                new KeyValuePair<Regex, string>(Compile("forced to the surface"), "被迫浮出表面"),
                new KeyValuePair<Regex, string>(Compile("found with no context"), "找到时缺少上下文"),
                new KeyValuePair<Regex, string>(Compile("into a block of shale"), "变成一块页岩"),
                new KeyValuePair<Regex, string>(Compile("no thermal insulation"), "没有隔热层"),
                new KeyValuePair<Regex, string>(Compile("nose begins to bleed."), "鼻子开始流血。"),
                new KeyValuePair<Regex, string>(Compile("not seem operational."), "似乎无法运转。"),
                new KeyValuePair<Regex, string>(Compile("there isn't a body in"), "里面没有尸体"),
                new KeyValuePair<Regex, string>(Compile("with understanding of"), "带着对……的理解"),
                new KeyValuePair<Regex, string>(Compile("Because they admire "), "因为他们钦佩"),
                new KeyValuePair<Regex, string>(Compile("Because they regard "), "因为他们看待"),
                new KeyValuePair<Regex, string>(Compile("Deep in the wilds of"), "在……的荒野深处"),
                new KeyValuePair<Regex, string>(Compile("Do you want to empty"), "你想倒空"),
                new KeyValuePair<Regex, string>(Compile("Error when disarming"), "卸下武器时出错"),
                new KeyValuePair<Regex, string>(Compile("No valid targets for"), "没有合法目标："),
                new KeyValuePair<Regex, string>(Compile("Pour on self message"), "倒在自己身上的消息"),
                new KeyValuePair<Regex, string>(Compile("Select a destination"), "选择目的地"),
                new KeyValuePair<Regex, string>(Compile("SunderMind no target"), "裂脑无目标"),
                new KeyValuePair<Regex, string>(Compile("You stop meditating."), "你停止了冥想。"),
                new KeyValuePair<Regex, string>(Compile("a sip of fresh water"), "一口淡水"),
                new KeyValuePair<Regex, string>(Compile("anything useful from"), "从……得到任何有用的东西"),
                new KeyValuePair<Regex, string>(Compile("away from the danger"), "远离危险"),
                new KeyValuePair<Regex, string>(Compile("because they admire "), "因为他们钦佩"),
                new KeyValuePair<Regex, string>(Compile("because they regard "), "因为他们看待"),
                new KeyValuePair<Regex, string>(Compile("core begins to leak."), "核心开始泄漏。"),
                new KeyValuePair<Regex, string>(Compile("of electrical charge"), "电荷"),
                new KeyValuePair<Regex, string>(Compile("to take advantage of"), "以利用"),
                new KeyValuePair<Regex, string>(Compile("will have to pony up"), "得掏钱"),
                new KeyValuePair<Regex, string>(Compile("It's miniature you."), "那是迷你版的你。"),
                new KeyValuePair<Regex, string>(Compile("You ended the game."), "你结束了这局游戏。"),
                new KeyValuePair<Regex, string>(Compile("You start to prowl."), "你开始潜行。"),
                new KeyValuePair<Regex, string>(Compile("You think you broke"), "你觉得自己弄坏了"),
                new KeyValuePair<Regex, string>(Compile("being blown back by"), "被吹回，来自"),
                new KeyValuePair<Regex, string>(Compile("from colliding with"), "因与……碰撞"),
                new KeyValuePair<Regex, string>(Compile("into a blood frenzy"), "陷入嗜血狂暴"),
                new KeyValuePair<Regex, string>(Compile("into cardiac arrest"), "陷入心脏骤停"),
                new KeyValuePair<Regex, string>(Compile("of anyone who reads"), "任何读到的人"),
                new KeyValuePair<Regex, string>(Compile("to anyone who reads"), "给任何读到"),
                new KeyValuePair<Regex, string>(Compile("to fungi across Qud"), "传给卡德各处的真菌"),
                new KeyValuePair<Regex, string>(Compile("to rapidly advance!"), "迅速进阶！"),
                new KeyValuePair<Regex, string>(Compile("to rapidly advance."), "迅速进阶。"),
                new KeyValuePair<Regex, string>(Compile("Because they love "), "因为他们喜爱"),
                new KeyValuePair<Regex, string>(Compile("No problems found."), "未发现问题。"),
                new KeyValuePair<Regex, string>(Compile("While traveling in"), "在……旅行时"),
                new KeyValuePair<Regex, string>(Compile("You are paralyzed!"), "你瘫痪了！"),
                new KeyValuePair<Regex, string>(Compile("You are puzzled by"), "你被……弄糊涂了："),
                new KeyValuePair<Regex, string>(Compile("You don't have any"), "你没有"),
                new KeyValuePair<Regex, string>(Compile("You feel feverish."), "你在发热。"),
                new KeyValuePair<Regex, string>(Compile("Zone has no parts."), "区域没有任何部件。"),
                new KeyValuePair<Regex, string>(Compile("because they love "), "因为他们喜爱"),
                new KeyValuePair<Regex, string>(Compile("no longer poisoned"), "不再中毒"),
                new KeyValuePair<Regex, string>(Compile("not dismemberable."), "无法被肢解。"),
                new KeyValuePair<Regex, string>(Compile("now your follower."), "现在是你的追随者。"),
                new KeyValuePair<Regex, string>(Compile("You are startled!"), "你被吓了一跳！"),
                new KeyValuePair<Regex, string>(Compile("You do not budge."), "你纹丝不动。"),
                new KeyValuePair<Regex, string>(Compile("a cloud of spores"), "一团孢子云"),
                new KeyValuePair<Regex, string>(Compile("blade in the air."), "空中的刀刃。"),
                new KeyValuePair<Regex, string>(Compile("from player body."), "从玩家身上。"),
                new KeyValuePair<Regex, string>(Compile("to distort around"), "扭曲环绕"),
                new KeyValuePair<Regex, string>(Compile("your shield slam."), "你的盾击。"),
                new KeyValuePair<Regex, string>(Compile("It's shadow you."), "那是你的影子。"),
                new KeyValuePair<Regex, string>(Compile("You feel drowsy."), "你觉得昏昏欲睡。"),
                new KeyValuePair<Regex, string>(Compile("You feel uneasy."), "你感到不安。"),
                new KeyValuePair<Regex, string>(Compile("a dromad caravan"), "一支驼人商队"),
                new KeyValuePair<Regex, string>(Compile("before your eyes"), "在你眼前"),
                new KeyValuePair<Regex, string>(Compile("have a chat with"), "聊一聊"),
                new KeyValuePair<Regex, string>(Compile("your life drain!"), "你的生命汲取！"),
                new KeyValuePair<Regex, string>(Compile("Choose a reward"), "选择奖励"),
                new KeyValuePair<Regex, string>(Compile("That was awful!"), "太糟糕了！"),
                new KeyValuePair<Regex, string>(Compile("These creatures"), "这些生物"),
                new KeyValuePair<Regex, string>(Compile("You share your "), "你分享了自己的"),
                new KeyValuePair<Regex, string>(Compile("from the ground"), "从地面"),
                new KeyValuePair<Regex, string>(Compile("is in disarray."), "陷入混乱。"),
                new KeyValuePair<Regex, string>(Compile("no longer lost."), "不再迷路。"),
                new KeyValuePair<Regex, string>(Compile("of fresh water."), "淡水。"),
                new KeyValuePair<Regex, string>(Compile("these creatures"), "这些生物"),
                new KeyValuePair<Regex, string>(Compile("a berserk fury"), "一阵狂怒"),
                new KeyValuePair<Regex, string>(Compile("down to attack"), "俯冲攻击"),
                new KeyValuePair<Regex, string>(Compile("dromad caravan"), "驼人商队"),
                new KeyValuePair<Regex, string>(Compile("their secrets."), "它们的秘密。"),
                new KeyValuePair<Regex, string>(Compile("to shield slam"), "盾击"),
                new KeyValuePair<Regex, string>(Compile("to the ground."), "倒地。"),
                new KeyValuePair<Regex, string>(Compile("you can store?"), "你能存放吗？"),
                new KeyValuePair<Regex, string>(Compile("This creature"), "此生物"),
                new KeyValuePair<Regex, string>(Compile("You are lost!"), "你迷路了！"),
                new KeyValuePair<Regex, string>(Compile("domesticated "), "家养"),
                new KeyValuePair<Regex, string>(Compile("irisdual beam"), "虹双光束"),
                new KeyValuePair<Regex, string>(Compile("on the ground"), "在地上"),
                new KeyValuePair<Regex, string>(Compile("scintillating"), "闪烁"),
                new KeyValuePair<Regex, string>(Compile("this creature"), "此生物"),
                new KeyValuePair<Regex, string>(Compile("to the ground"), "倒地"),
                new KeyValuePair<Regex, string>(Compile("Barathrumite"), "巴拉斯鲁米特"),
                new KeyValuePair<Regex, string>(Compile("to extricate"), "挣脱"),
                new KeyValuePair<Regex, string>(Compile("with a flash"), "闪光一闪"),
                new KeyValuePair<Regex, string>(Compile("deactivated"), "未激活"),
                new KeyValuePair<Regex, string>(Compile("to confirm."), "确认。"),
                new KeyValuePair<Regex, string>(Compile("Not found."), "未找到。"),
                new KeyValuePair<Regex, string>(Compile("not budge."), "纹丝不动。"),
                new KeyValuePair<Regex, string>(Compile("suppressed"), "被压制"),
                new KeyValuePair<Regex, string>(Compile("to confirm"), "确认"),
                new KeyValuePair<Regex, string>(Compile("to protect"), "保护"),
                new KeyValuePair<Regex, string>(Compile("with life."), "带着生命。"),
                new KeyValuePair<Regex, string>(Compile("Unable to"), "无法"),
                new KeyValuePair<Regex, string>(Compile("away from"), "远离"),
                new KeyValuePair<Regex, string>(Compile("conjoined"), "被接合"),
                new KeyValuePair<Regex, string>(Compile("crackling"), "噼啪作响"),
                new KeyValuePair<Regex, string>(Compile("cyberized"), "义体化"),
                new KeyValuePair<Regex, string>(Compile("destroyed"), "被摧毁"),
                new KeyValuePair<Regex, string>(Compile("paralyzed"), "瘫痪"),
                new KeyValuePair<Regex, string>(Compile("polarized"), "被极化"),
                new KeyValuePair<Regex, string>(Compile("to avenge"), "复仇"),
                new KeyValuePair<Regex, string>(Compile("to hobble"), "致跛"),
                new KeyValuePair<Regex, string>(Compile("with life"), "带着生命"),
                new KeyValuePair<Regex, string>(Compile("dazzling"), "耀眼"),
                new KeyValuePair<Regex, string>(Compile("disarmed"), "被卸械"),
                new KeyValuePair<Regex, string>(Compile("painting"), "画作"),
                new KeyValuePair<Regex, string>(Compile("revealed"), "被揭示"),
                new KeyValuePair<Regex, string>(Compile("to clean"), "清洁"),
                new KeyValuePair<Regex, string>(Compile("to stand"), "站起"),
                new KeyValuePair<Regex, string>(Compile("to touch"), "触碰"),
                new KeyValuePair<Regex, string>(Compile("chromed"), "镀铬"),
                new KeyValuePair<Regex, string>(Compile("grafted"), "被嫁接"),
                new KeyValuePair<Regex, string>(Compile("healing"), "治疗中"),
                new KeyValuePair<Regex, string>(Compile("hobbled"), "跛行"),
                new KeyValuePair<Regex, string>(Compile("stunned"), "昏迷"),
                new KeyValuePair<Regex, string>(Compile("to dust"), "扬尘"),
                new KeyValuePair<Regex, string>(Compile("to help"), "帮助"),
                new KeyValuePair<Regex, string>(Compile("to pull"), "拉拽"),
                new KeyValuePair<Regex, string>(Compile("to warm"), "取暖"),
                new KeyValuePair<Regex, string>(Compile("dazed"), "眩晕"),
                new KeyValuePair<Regex, string>(Compile("to be"), "成为"),
            };
            for (int i = 0; i < rows.Length; i++) Rules.Add(rows[i]);
        }
    }
}
