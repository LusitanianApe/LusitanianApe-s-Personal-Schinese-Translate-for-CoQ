// ═══════════════════════════════════════════════════════════════════════════
// 飞行消息汉化 —— 清洗 `Flight.StartFlying` 硬编码的英文半句
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户报告】消息栏 `:: 蝙蝠 begins flying.`（中文名 + 英文动词短语）
//
// 【反编译实证：元凶是**硬编码字符串拼接**】
//     // XRL.World.Capabilities.Flight::StartFlying
//     if (Object.IsPlayer())
//         MessageQueue.AddPlayerMessage("You begin flying!");                 // 整句（可被字符串表覆盖）
//     else if (Object.IsVisible())
//         MessageQueue.AddPlayerMessage(Object.Does("begin") + " flying.");   // ★★ 元凶
//   · `Object.Does("begin")` 由我方动词表渲染 → 「蝙蝠 begins」
//     （实测 `Player.log`：`拿取接管：Does 挂钩 #6 原返='冢虫（Knollworm） begins'`）
//   · **`" flying."` 是 C# 字面量** ⇒ 字符串表**覆盖不到** ⇒ 中英混排。
//
// 【★ 为什么不用别的三种写法（都试过，各有致命问题）】
//   ① Prefix 抢整个 `StartFlying` ⇒ **会破坏飞行状态逻辑**（它还要加 Swoop 能力、
//      发 MovementModeChanged、置布尔状态），照抄一遍风险极大 ✗
//   ② 给动词表加 `begin` ⇒ 只解决「begins」，**`" flying."` 仍是英文** ✗
//   ③ 事后用正则清洗**整条消息** ⇒ 得靠猜「哪半句是英文动词」，
//      还会误伤玩家自建名（§5），脆弱 ✗
//   ✅ **本方案**：挂**消息入队口** `MessageQueue.AddPlayerMessage(string,string,bool)`，
//      对**形态明确**的少量模板做定点替换 —— 不碰游戏逻辑，规则窄到不会误伤。
//
// 【规则表（窄而明确）】
//     `X begins flying.`        → `X开始飞行。`
//     `X begins to fly.`        → `X开始飞行。`
//     `X returns to the ground.`→ `X回到地面。`
//     `X falls to the ground!`  → `X摔到地上！`
//   其中 `X` = 主语（可能是中文名，也可能是英文——**原样保留**，符合 §5）。
//
// 【安全】`_zhs_flight_off.txt` 可停用；异常一律放行原文。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFlightMsg
    {
        public static bool Disabled;
        internal static int Hits;

        // ── 规则表：`(?<s>主语)` + 英文尾串 ⇒ 中文尾串 ──
        //   主语用 `.+?` 且**要求非空**；尾串全是**固定英文**，误伤风险极低。
        private static readonly (Regex Rx, string Zh)[] Rules =
        {
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.85：`XDidY` / `XDidYToZ` 的**硬编码主语 `"You "`**
            //    用户截图：`You 坐下 down on 地垫.` —— 动词已中文化，`You` 仍是英文。
            //    来源（反编译）：
            //      `XRL.World.Capabilities.Messaging::XDidY`
            //          textBuilder.Append("You ").Append(Verb);   ← ★ 字面量 "You "
            //      `XDidYToZ` 同理（`"You " + Verb + " " + Preposition + " " + Object…`）
            //    ⚠ 必须放在**通用 `^(?<s>.+?)` 规则之前**，否则被抢走。
            // ═══════════════════════════════════════════════════════════════
            (new Regex(@"^You (?<s>[\u4e00-\u9fff][^\n]*)$",
                RegexOptions.Compiled), "你{s}"),
            // 「X begins flying.」/「X begins to fly.」
            (new Regex(@"^(?<s>.+?)\s+begins?\s+(?:flying|to fly)\.\s*$",
                RegexOptions.Compiled), "{s}开始飞行。"),
            // 「X returns to the ground.」
            (new Regex(@"^(?<s>.+?)\s+returns?\s+to the ground\.\s*$",
                RegexOptions.Compiled), "{s}回到地面。"),
            // 「X falls to the ground!」
            (new Regex(@"^(?<s>.+?)\s+falls?\s+to the ground!\s*$",
                RegexOptions.Compiled), "{s}摔到地上！"),
            // 「X ceases using one of its flight capabilities.」
            (new Regex(@"^(?<s>.+?)\s+ceases?\s+using\s+(?:one of )?(?:its|their)\s+flight\s+capabilit(?:y|ies)\.\s*$",
                RegexOptions.Compiled), "{s}停用了其中一项飞行能力。"),
            // 「X begins using an additional flight capability.」
            (new Regex(@"^(?<s>.+?)\s+begins?\s+using\s+an?\s+additional\s+flight\s+capability\.\s*$",
                RegexOptions.Compiled), "{s}启用了另一项飞行能力。"),
            // 「One of X's flight capabilities fails.」
            (new Regex(@"^One of (?<s>.+?)'s flight capabilities fails?\.\s*$",
                RegexOptions.Compiled), "{s}的一项飞行能力失灵了。"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.84 新增：`AutoAct` 打断消息的 `because` 值（用户截图实证）
            //    截图：`你停止了移动，因为something is shooting at you。`
            //    来源（反编译定位）：
            //      `XRL.World.Capabilities.AutoAct::Interrupt(...)`
            //          Strings._T("AutoAct Interrupt Message",
            //                      "You stop =autoActDescription= because =because=.")
            //              .SetArgument("because", Because)     ← ★ 参数，不走查表
            //      `=because=` 的供给者只有两个：
            //        ① `AutoAct::TryToMove` 等 —— 用 `Strings._T("AutoAct … 'because' (clause)", …)`
            //           ⇒ **走查表**，我方 Strings.xml 已覆盖 15 条 ✅
            //        ② `XRL.World.Parts.MissileWeapon::FireEvent` L302/962/966
            //           ⇒ **裸字面量**，绕过 `Strings._T` ⇒ 查表够不到 ⇒ ★ 本规则要补的就是它
            //            `AutoAct.Interrupt("something is shooting at you", …)`
            //            `AutoAct.Interrupt("you " + …+ " something shooting at " + …)`
            //            `AutoAct.Interrupt("something is shooting at you or " + …)`
            //    ⚠ 这三条是**从句**（拼在「因为」之后）⇒ 后半句译文照从句写，不写成整句。
            // ═══════════════════════════════════════════════════════════════
            (new Regex(@"^something is shooting at you or (?<s>.+?)\.?\s*$",
                RegexOptions.Compiled), "有东西在朝你射击，或者是{s}"),
            (new Regex(@"^something is shooting at you\.?\s*$",
                RegexOptions.Compiled), "有东西在朝你射击"),
            // ⚠ `=perceptionVerb=` 是**英文动词**（see / sense / smell…）⇒
            //    整段译文退回英文更自然；此处保守处理为「你察觉到」，
            //    宁可略去感知方式，也不出中英混排。
            (new Regex(@"^you \w+ something shooting at (?<s>.+?)\.?\s*$",
                RegexOptions.Compiled), "你察觉到有东西朝{s}射击"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.84 新增：DLC 宠物「非此/即彼」的形态切换（用户截图实证）
            //    截图：`:: 非此 starts to flicker.` / `:: or starts to flicker.`
            //    来源（反编译 + 字面量定位）：
            //      XRL.World.Parts.PetEitherOr::trigger()
            //          AddPlayerMessage(ParentObject.DisplayNameOnly + " starts to flicker.");
            //      ★ `" starts to flicker."` 是 **C# 拼接**（前导空格）⇒ 字符串表覆盖不到
            //        （`_asm_literals.txt` L27455 有这条；`decomp find` 命中唯一：
            //          `XRL.World.Parts.PetEitherOr::trigger`）
            //      ★ 调用链 `IComponent<GameObject>.AddPlayerMessage`
            //          → `MessageQueue.AddPlayerMessage` ⇒ **正是本补丁挂的那一层**
            //    主语 = 宠物名（`DisplayNameOnly`，不带颜色标记）⇒ 中文名「非此」/「即彼」
            //    ⇒ 译作 `{s}开始闪烁。`，与 `begins flying` 系列同构。
            // ═══════════════════════════════════════════════════════════════
            (new Regex(@"^(?<s>.+?)\s+starts?\s+to\s+flicker\.\s*$",
                RegexOptions.Compiled), "{s}开始闪烁。"),

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.52 新增：**切换架势**（用户截图实证）
            //    来源：旧消息栏审计 G 类「英文游戏术语」——
            //      `The Templar squire switches to aggressive stance.`
            //      `The Templar squire switches to defensive stance.`
            //    反编译佐证：DLL 里有 `AggressiveStanceID` / `DefensiveStanceID` /
            //      `ChangeStance`；而 `GetCommandForStance` / `GetAbilityForStance`
            //      说明这句是**运行时拼**的（整句在 XML / DLL / 字符串表三处都搜不到）。
            //    ⚠ 只收**实证出现过的形态**（aggressive / defensive），
            //      不猜其它架势词；句式固定、尾串是英文 ⇒ 误伤风险低。
            // ═══════════════════════════════════════════════════════════════
            (new Regex(@"^(?<s>.+?)\s+switches?\s+to\s+aggressive\s+stance\.\s*$",
                RegexOptions.Compiled), "{s}切换到进攻姿态。"),
            (new Regex(@"^(?<s>.+?)\s+switches?\s+to\s+defensive\s+stance\.\s*$",
                RegexOptions.Compiled), "{s}切换到防御姿态。"),
            (new Regex(@"^(?<s>.+?)\s+switches?\s+to\s+dueling\s+stance\.\s*$",
                RegexOptions.Compiled), "{s}切换到决斗姿态。"),
        };

        // ═══════════════════════════════════════════════════════════════════
        // ★★ 移动受阻消息（2.15.77）—— 全部来自 `XRL.World.GameObject::Move`
        //    的**裸字面量**，既不进字符串表、也没有 `_T(Context, ID)`：
        //        MessageQueue.AddPlayerMessage("You cannot go that way.");
        //        Fail("You are paralyzed!");               // `Fail` 内部同样走消息口
        //    ⇒ 用户在消息栏看到整句英文 `You cannot go that way.`
        //    判据：**整句完全相等**才替换 ⇒ 零误伤。
        // ═══════════════════════════════════════════════════════════════════
        private static readonly Dictionary<string, string> EXACT =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "You cannot go that way.", "那边过不去。" },
            { "You are not a mobile creature.", "你不是能自己移动的生物。" },
            { "You are paralyzed!", "你被麻痹了！" },
            { "You are carrying too much to move!", "你负重过多，无法移动！" },
            { "You are an aquatic creature and may not move onto land!",
              "你是水生生物，不能移到陆地上！" },
            { "You are a wall-dwelling creature and may only move onto walls.",
              "你是壁居生物，只能移到墙上。" },

            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.53 —— 硬编码普查「仅 DLL」+「三处皆无」两档的补齐
            //
            // 来源：`_tools\hardcoded_A_完整句.txt` 的 358 条，
            //   经 `_tools\_classify_hardcoded_gap.py` 按「我方 XML / 官方 XML / DLL / 三处皆无」
            //   四层归属后，剩下**只在 DLL** 的 31 条与**三处皆无**的 17 条。
            //
            // 判据：这些串在官方 XML 与字符串表里都搜不到 ⇒ 是**硬编码整句**
            //   ⇒ 整串精确匹配（`TryGetValue`）是零误伤的做法。
            // ⚠ 纯内部键不在此列（如 `Replacer =number.bytes= bytes`、`DelegatesMutations … replacer`）。
            // ═══════════════════════════════════════════════════════════════
            { "You ended the game.", "你结束了这局游戏。" },
            { "You fall to the ground!", "你摔到地上！" },
            { "You thirst at half rate.", "你的口渴速度减半。" },
            { "You can't be mutated.", "你无法被突变。" },
            { "You feel less feverish.", "你的发热减轻了。" },
            { "You can't fly right now.", "你现在不能飞。" },
            { "from the {{G|hulk}} {{w|honey}}!", "来自{{G|巨兽}}{{w|蜂蜜}}！" },
            { "You no longer feel ill.", "你不再感到不适。" },
            { "Your genome destabilizes and you gain a new mutation:",
              "你的基因组失稳，获得了一项新突变：" },
            { "Your genome destabilizes and you gain a new defect:",
              "你的基因组失稳，获得了一项新缺陷：" },
            { "You start to prowl.", "你开始潜行。" },
            { "You spot a {{w|dromad}} caravan.", "你发现了一支{{w|驼人}}商队。" },
            { "You were stepped on.", "你被踩了。" },
            { "You cannot do that.", "你无法这样做。" },
            { "You can't do that here.", "你在这里不能这样做。" },
            { "You can't imprint yet.", "你现在还不能铭印。" },
            { "You are not enclosed.", "你没有被包裹。" },
            { "from the {{icy|cryogenic mist}}.", "来自{{icy|低温雾气}}。" },
            { "A {{W|ribbon of electricity}} leaps from %o to %w.",
              "一道{{W|电弧}}从 %o 窜向 %w。" },
            { "Your shot goes wild!", "你这一发射偏了！" },
            { "Your Hobble cooldown is reduced by 5 rounds.",
              "你的「跛行」冷却缩短 5 回合。" },
            { "to gleam with an {{K|unearthly light}}.",
              "泛起{{K|非世间之光}}。" },
            { "You've done nothing.", "你什么都没做。" },
            { "You can't deploy there!", "你不能在那里展开！" },
            { "from a {{fiery|jet of flames}}!", "来自{{fiery|烈焰喷流}}！" },
            { "You are unusually large.", "你的体型异常巨大。" },
            { "Your heart is weak.", "你的心脏很弱。" },
            { "You do not budge.", "你纹丝不动。" },
            { "You don't have a tongue!", "你没有舌头！" },
            { "You emit a ray of flame.", "你射出一道火焰射线。" },
            { "You emit a ray of frost.", "你射出一道寒霜射线。" },
            { "The reacting liquids congeal into =object.a.name:withTitles=.",
              "反应后的液体凝结成了=object.a.name:withTitles=。" },
            { "Your Quickness is permanently increased by {{rules|=boost=}}.",
              "你的迅捷永久提高 {{rules|=boost=}}。" },
            { "You read one of the few legible excerpts from =title|color:Y=:",
              "你读了=title|color:Y=中少数尚可辨认的段落之一：" },
            { "Painted: This object is painted with a scene from the history of the village {{M|=villageName=}}:",
              "彩绘：这件物品上绘有{{M|=villageName=}}村史中的一幕：" },
            { "Engraved: This object is engraved with a scene from the history of the village {{M|=villageName=}}:",
              "镌刻：这件物品上刻有{{M|=villageName=}}村史中的一幕：" },
            { "This object is a monument to a scene from the history of the village {{M|=villageName=}}:",
              "这件物品是{{M|=villageName=}}村史中一幕的纪念碑：" },
            { "Holographic: This hologram depicts a scene from the history of the village {{M|=villageName=}}:",
              "全息：这幅全息图描绘了{{M|=villageName=}}村史中的一幕：" },
            { "You don't have the required <=bitcost=> bits! You have:",
              "你没有所需的 <=bitcost=> 比特！你拥有：" },
            { "You can change this option again later in =options[OptionAllowCSMods].path=.",
              "你稍后可以在=options[OptionAllowCSMods].path=中再次更改此选项。" },
        };

        // ⚠ 生物卡的 `\nBase demeanor: …` / `\nEngagement style: …` **不在这里**：
        //   它们**不走 `MessageQueue`**（是 `GetShortDescriptionEvent.Postfix` 的后置串）
        //   ⇒ 由 `ZhsShortDescPatch`（挂 `TextBuilder.Append(string)`）负责。

        // ── 带主语/名字的句式（`{s}` = 主语，`{n}` = 名字）──
        //
        // ★★ 为什么同一条文案要在**两个补丁**里各写一份：
        //   反编译 `Popup.Show(string Message, …, bool LogMessage = true, …)`：
        //       if (Suppress || _TextConsole == null) { if (LogMessage) MessageQueue.AddPlayerMessage(Message); }
        //       else { ShowBlock(Message, …, LogMessage, …); }      // 弹窗，且也会记进消息栏
        //   ⇒ **弹窗文本同时进消息栏** ⇒ 只挂 `Popup.Show` 不够：
        //     消息栏那条是**另一次** `AddPlayerMessage` 调用（实测用户截图：
        //     `:: You start calling 你自己 by the name '⑨智锤哥'.` 出现在消息栏）。
        //   ⇒ `ZhsRenamePatch` 管弹窗路，这里管消息栏路。
        private static readonly (Regex Rx, string Zh)[] NAME_RULES =
        {
            // `You start calling 你自己 by the name '⑨智锤哥'.`
            (new Regex(@"^You start calling (?<s>.+?) by the name '(?<n>.*)'\.$",
                RegexOptions.Compiled), "你开始用「{n}」这个名字称呼{s}。"),
            // `Enter a name for 你自己.` / `Enter a new name for X.`
            (new Regex(@"^Enter a (?:new )?name for (?<s>.+?)\.$", RegexOptions.Compiled),
             "为{s}输入名称。"),
            // `Choose a random name from 你的 culture.`
            (new Regex(@"^Choose a random name from (?<s>.+?) culture\.$", RegexOptions.Compiled),
             "从{s}的文化中随机取一个名字。"),
            // ═══════════════════════════════════════════════════════════════
            // ★★ 摸宠物被拒（2.15.89）：用户截图 `光猫 shies away from you.`
            //   反编译 `XRL.World.Parts.Pettable::Pet`：
            //       if (!WillingToBePettedBy(Actor))
            //       {
            //           Actor.Fail(ParentObject.Does("shy") + " away from you.");   // ★ 元凶
            //           return false;
            //       }
            //   `Does("shy")` 产出「光猫 shies」（动词表里有 shy 才算中文），
            //   而 **`" away from you."` 是硬编码** ⇒ 中英混排。
            //   实测 DLL 串堆：`'shy'` 与 `' away from you.'` **紧邻**（同一处拼接）。
            //
            //   ★ 主语**限定为中文**（`[\u4e00-\u9fff]` 起头）—— 官方那句
            //     `The apple farmer's daughter shies away from you.` 走**字符串表**
            //     （我方已有译文「苹果农的女儿见了你就躲开了。」）⇒ 不该在消息口被抢。
            //   ★ 兼容 `shies`（三单）与 `shy`（原形，`Does` 偶尔给原形）。
            // ═══════════════════════════════════════════════════════════════
            (new Regex(@"^(?<s>[\u4e00-\u9fff][^\n]*?)\s+sh(?:ies|y)\s+away\s+from\s+you\.$",
                RegexOptions.Compiled), "{s}躲开了你。"),
        };

        /// <summary>命中规则则返回中文，否则返回 null（调用方保留原文）。</summary>
        internal static string Fix(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            // ① 先查**整句精确表**（零误伤）
            string exact;
            if (EXACT.TryGetValue(msg, out exact)) return exact;
            if (EXACT.TryGetValue(msg.Trim(), out exact)) return exact;
            // ② 带主语/名字的句式
            foreach (var (rx, zh) in NAME_RULES)
            {
                Match m = rx.Match(msg);
                if (!m.Success) continue;
                string subj = m.Groups["s"].Value.Trim();
                if (subj.Length == 0) continue;
                string outZh = zh.Replace("{s}", subj);
                if (m.Groups["n"].Success) outZh = outZh.Replace("{n}", m.Groups["n"].Value);
                // 主语多以「的」结尾而模板又接「的」⇒ 压掉重复（单次替换后再净化）
                outZh = outZh.Replace("\u7684\u7684", "\u7684");
                return outZh;
            }
            // ③ 纯主语句式（`=subject` 占位，无固定主语）
            //   ★ 2.16.84：改为**通用命名组替换** —— 原来只认 `{s}`/`{n}`，
            //     新增的 `{v}`（感知动词）会**字面输出**成「你{v}有东西…」。
            //     现在按正则的**全部命名组**逐个替换 `{组名}`，以后加组不必再改这里。
            foreach (var (rx, zh) in Rules)
            {
                Match m = rx.Match(msg);
                if (!m.Success) continue;
                if (m.Groups["s"].Success && m.Groups["s"].Value.Trim().Length == 0) continue;
                string outZh = zh;
                foreach (string gn in rx.GetGroupNames())
                {
                    if (int.TryParse(gn, out _)) continue;      // 跳过编号组
                    if (!m.Groups[gn].Success) continue;
                    outZh = outZh.Replace("{" + gn + "}", m.Groups[gn].Value.Trim());
                }
                return outZh;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // ★★ 兜底：剥掉「英文冠词 + 中文」（2.15.86）
        //    用户截图：`The 湿的 光鱼死亡!` —— `The` 是**槽运行时产出**的
        //    （`=subject.The.name=` 形态，全仓 60 处；大写 T = 大写定冠词），
        //    而中文不需要冠词。
        //
        //    【为什么可以通用剥】
        //      · 审计过：我方 **10475 条**译文里「英文冠词 + 中文」的形态 = **0 处**
        //        ⇒ 不存在「本来就该有冠词」的中文译文，剥掉不会误伤我方数据
        //      · 只匹配 `the/a/an` **后紧跟中文**的场合，且只在**行首或空白后**
        //        ⇒ 不会碰英文句子里的正常冠词（如 `a dram of …` 后面是英文）
        //      · 玩家自建名若以英文冠词开头（罕见），只在后接中文时才动 —— 可接受
        //    ⚠ 保留「The 」之外的空格结构：`The 湿的` → `湿的`（连同其后一个空格）
        // ═══════════════════════════════════════════════════════════════════
        // ★★ 2.16.7 修正：**不能再消费前导空白**。
        //
        //   旧写法 `(?:^|(?<=[\s\(\[\{>]))…` 会把那个空白**一起匹配掉**，
        //   而替换是 `Replace(msg, "")` ⇒ 空白被一并删除：
        //       `The 无眼蟹 滑倒在黏液上！` → `无眼蟹滑倒在黏液上！`（丢了空格）
        //   实测来源（`_zhs_msgtrace.log`）：
        //       `{{slimy|The {{&Y|无眼蟹}} 滑倒在黏液上！}}`
        //   ⇒ 改为**零宽**边界 `(?<![A-Za-z])`：
        //       既保证「前面不是字母」（不会切进英文单词），又**不消费**任何字符。
        private static readonly Regex BARE_ARTICLE = new Regex(
            @"(?<![A-Za-z])(?:[Tt]he|[Aa]n?)\s+(?=[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef])",
            RegexOptions.Compiled);

        // ★★ 2.16.40 补：`XRL.World.GameObject::Move` 里这句是**硬编码拼接**：
        //     AutoAct.Interrupt(gameObject.HasProperName
        //         ? (gameObject.does("are") + " in your way")
        //         : ("there" + gameObject.Is + " " + gameObject.an() + " in your way"), …)
        //   ⇒ 英文 `there is a 洞穴蜘蛛 in your way`；`an()` 的 `a ` 被 BARE_ARTICLE 吃掉，
        //     而 `there is ` 与 ` in your way` **没有任何规则管**（字符串表覆盖不到拼接件）。
        //   ⇒ 两处一起补：
        //     ① 冠词紧跟 `there is ` 时，把 `there is a ` **整段**折成「有一」
        //     ② 段规则清掉 ` in your way` / ` are in your way`
        private static readonly Regex THERE_IS_ARTICLE = new Regex(
            @"(?<![A-Za-z])there\s+(?:is|are)\s+(?:[Tt]he|[Aa]n?)\s+"
            + @"(?=[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef])",
            RegexOptions.Compiled);

        /// <summary>` in your way` / ` are in your way` ⇒ 中文。硬编码拼接件，只能在此改。</summary>
        private static readonly Regex IN_YOUR_WAY = new Regex(
            @"\s+(?:are\s+)?in your way",
            RegexOptions.Compiled);

        /// <summary>
        /// ★ 2.16.59：`there is a X in your way` 的**整段**（X 必须已是中文）。
        /// ⇒ `路被X挡住了`（**被字句**：给「挡住」补出主语，且绕开量词问题
        ///    —— 「有一只门」量词会错，而「路被门挡住了」不需要量词）。
        /// `X` 用非贪婪匹配到 ` in your way` 之前，允许内部空格（名字常含空格）。
        /// </summary>
        private static readonly Regex THERE_IS_IN_WAY = new Regex(
            @"there\s+(?:is|are)\s+(?:[Tt]he|[Aa]n?)\s+"
            + @"([\u4e00-\u9fff\u3000-\u303f\uff00-\uffef][^.!?]*?)\s+"
            + @"(?:are\s+)?in your way",
            RegexOptions.Compiled);

        /// <summary>`there is a X in your way` ⇒ 往整串里写入 `路被X挡住了`。</summary>
        private const string WAY = "路被$1挡住了";

        // ★★ 2.16.40 补：`XRL.World.Parts.Chair` 的**坐下/起身**全是 C# 字符串拼接
        //   （`Actor.Fail("You cannot sit on " + Actor.itself + ".")`、
        //     `Actor.Fail("You are already sitting down.")` 等）⇒ **不在 DLL 字面量池**，
        //   任何字面量普查都扫不到 ⇒ 只能在此按句式改。对象名保持原样（已由映射表译）。
        private static readonly (Regex Re, string Zh)[] CHAIR_RULES = new[]
        {
            (new Regex(@"^You sit down on (?:the )?(.+?)\.$", RegexOptions.Compiled),
             "你坐在$1上。"),
            (new Regex(@"^You stand up\.$", RegexOptions.Compiled),
             "你站了起来。"),
            (new Regex(@"^You are already sitting down\.$", RegexOptions.Compiled),
             "你已经坐着了。"),
            (new Regex(@"^You cannot sit on (.+?)\.$", RegexOptions.Compiled),
             "你不能坐在$1上。"),
            (new Regex(@"^You cannot reach (.+?)\.$", RegexOptions.Compiled),
             "你够不到$1。"),
            (new Regex(@"^You are out of phase with (.+?)\.$", RegexOptions.Compiled),
             "你与$1不在同一相位。"),
            (new Regex(@"^You cannot do that while enclosed\.$", RegexOptions.Compiled),
             "被包裹时无法这么做。"),
            // `XRL.World.GameObject::Move` 的其它硬编码句
            (new Regex(@"^You cannot go that way\.$", RegexOptions.Compiled),
             "你不能往那边走。"),
            (new Regex(@"^You are stopped short by (.+?)\.$", RegexOptions.Compiled),
             "$1把你拦住了。"),
            (new Regex(@"^You can't budge (.+?)\.$", RegexOptions.Compiled),
             "你推不动$1。"),
            (new Regex(@"^(.+?) cannot be moved\.$", RegexOptions.Compiled),
             "$1无法被移动。"),
            (new Regex(@"^(.+?) are stuck\.$", RegexOptions.Compiled),
             "$1卡住了。"),
        };

        /// <summary>硬编码拼接句 ⇒ 中文。整串匹配（`^…$`）以免误伤。</summary>
        internal static bool TryChairRule(string msg, out string zh)
        {
            zh = null;
            if (string.IsNullOrEmpty(msg)) return false;
            foreach (var (re, tpl) in CHAIR_RULES)
            {
                Match m = re.Match(msg);
                if (!m.Success) continue;
                zh = m.Result(tpl);
                return true;
            }
            return false;
        }

        /// <summary>剥离中文前的英文冠词；有改动才返回新串，否则 null。</summary>
        internal static string StripBareArticle(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            // ★★ 2.16.19 修（真 bug，由用户截图「严查喵」暴露）：
            //
            //   旧代码在跑正则**之前**先做了一次「快速筛」：
            //       if (msg.IndexOf("he ") < 0 && msg.IndexOf("he\t") < 0
            //           && msg.IndexOf("a ") < 0 && msg.IndexOf("n ") < 0) return null;
            //   本意是省掉不可能有冠词的串，但它**先于**正则判断 ⇒
            //   `The 多汁的 沾血的 蝙蝠死亡!` 里既没有 `he ` 也没有 `a `/`n `
            //   ⇒ **门禁直接挡掉** ⇒ `BARE_ARTICLE`（2.16.14 刚校准过）**根本没机会跑**。
            //   实测症状：`The 多汁的 沾血的 蝙蝠死亡!` 的 `The ` 不掉。
            //
            //   ⇒ 删掉门禁：`BARE_ARTICLE` 本身已足够严格
            //     （要求「冠词 + 空白 + **紧跟汉字**」，且前置零宽 `(?<![A-Za-z])`），
            //     不命中时直接返回 null，代价可忽略。
            // ★★ 2.16.40：① 先试**整句**硬编码规则（坐下/起身/走不动…）
            if (TryChairRule(msg, out string zhWhole)) return zhWhole;
            string work = msg;
            // ★★ 2.16.59：**先**处理 `there is a X in your way` 的整段
            // ★★ 2.16.59：**先**处理 `there is a X in your way` 的整段
            //
            // 【用户截图】消息栏 `:: 你停止了移动，因为挡住了路。`
            //   —— 「因为」后**缺成分**：中文「挡住」必须有主语。
            //
            // 【根因（实证）】游戏把该消息拼成
            //       `You stop moving because ` + 字符串表条目
            //   而那条是 `Context="AutoAct interrupt 'because' (clause)"`
            //             `ID="=subject.does:are= in the way"`  （主语是**挡路那个东西**）
            //   我方把它译成「挡住了路」—— 英文靠 `=subject.` 槽填主语，
            //   中文这条**丢了主语** ⇒ 读起来就是残句。
            //
            //   更糟的是本方法的处理顺序：`there is a X` 先被折成「有一」，
            //   再被 `BARE_ARTICLE` 删掉中间空格 ⇒ `有一 咬颚拾荒者` → `有一咬颚拾荒者`
            //   （**量词也没了**）。实测日志：
            //       `你停止了移动，因为there is a 咬颚拾荒者 in your way。`
            //         => `你停止了移动，因为有一咬颚拾荒者挡住了路。`
            //
            // 【修法（按用户指示）】① 补出**挡路者** ② 后半句改成**被字句**
            //       `…因为there is a 咬颚拾荒者 in your way。`
            //         => `…因为有一只咬颚拾荒者挡住了路。`
            //   即 `有一只X挡住了路`（X 已是中文）⇒ 主语与量词都齐了。
            //   ⚠ `there is a X` 若 X 还是英文（名字映射没有），则**不处理**，
            //     交给下面的原逻辑（宁可保持现状，也不产出中英混排）。
            work = THERE_IS_IN_WAY.Replace(work, WAY);
            //   ② 再处理 `there is a X` 的**整段**（折成「有一」），
            //   ③ 再走原冠词剥离；④ 最后清 ` in your way`。
            work = THERE_IS_ARTICLE.Replace(work, "有一");
            work = BARE_ARTICLE.Replace(work, "");
            work = IN_YOUR_WAY.Replace(work, "被挡住了路");
            return work == msg ? null : work;
        }

        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u4e00' && c <= '\u9fff')
                    || (c >= '\u3400' && c <= '\u4dbf')) return true;
            }
            return false;
        }
    }

    /// <summary>挂 `MessageQueue.AddPlayerMessage(string, string, bool)` 的 Prefix。</summary>
    internal static class ZhsFlightPatch
    {
        private const string Tag = "[QudZhsCookbook] 飞行消息：";
        private static bool _applied;

        /// <summary>回写失败的次数（§49 回读校验；>0 就说明 `ref string __0` 没写回去）。</summary>
        internal static int WriteBackMisses;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsFlightMsg.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.Messages.MessageQueue", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Messages.MessageQueue");
                    return;
                }
                MethodInfo m = null;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (mi.Name != "AddPlayerMessage") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 3 && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(string))
                    {
                        m = mi;
                        break;
                    }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 AddPlayerMessage(string,string,bool)");
                    return;
                }
                MethodInfo pre = typeof(ZhsFlightPatch).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.flightmsg")
                    .Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 MessageQueue.AddPlayerMessage"
                    + "（6 条飞行消息模板 · 只动形态明确的英文尾串）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ★★★ 2026-10-16（F1 严查）**真根因与修法** ——「算出中文但没送达」
        //
        // 【症状】本 Prefix 的日志写着 `巨蜻蜓 begins flying.  =>  巨蜻蜓开始飞行。`，
        //   而**同一局**里：探针 Postfix 读到英文、存档消息栏落盘也是英文、屏幕上英文。
        //   （用户 2026-10-16 实证：**新存档**依旧 ⇒ 不是旧存档残留）
        //
        // 【根因（生成 IL 实证 · `harmony.log.txt` 2026-10-16 12.37.22 那一块）】
        //   `MessageQueue.AddPlayerMessage(string,string,bool)` 上还挂着
        //       `ZhsQuestTextPatch::Prefix(object[] __args)` —— **排在 Flight 之后**。
        //   Harmony 对「声明了 `object[] __args` 的 Prefix」会在调用它之后**发射整批回写**：
        //       IL_0046: call ZhsFlightPatch::Prefix(String& __0)        ← 中文写进 arg0
        //       IL_0059: call ZhsQuestTextPatch::Prefix(Object[] __args)
        //       IL_0067: ldelem.ref / IL_0068: castclass String / IL_006D: starg 0
        //                                                              ★ 把 arg0 覆盖回英文
        //       IL_00BC: call ZhsCombatMsgPatch::Prefix(String&)         ← 排在覆盖之后 ⇒ 它写的能生效
        //       IL_00CF: // start original                               ← 方法体读到的是英文
        //   而 `__args` 是 Harmony 在**方法入口**建好的**快照**（`newarr` 在所有 Prefix 之前）
        //   ⇒ **任何一个声明 `__args` 的 Prefix，都会把它之前所有 Prefix 的 ref 回写整批回滚。**
        //   （`_tools\_f1fix` 用同版本 2.4.2 复现：只写 `__0` ⇒ 入库英文；
        //     连 `__args[0]` 一起写 ⇒ 入库中文，且**与挂载顺序无关**。）
        //
        // 【修法】本 Prefix **同时声明 `object[] __args`**，并把中文**也写进 `__args[0]`**
        //   ⇒ 之后那一次 `__args` 回写写的是中文（幂等），本补丁不再被任何 `__args` 补丁吃掉。
        //   ⚠ 只写 `__0` 是不够的；只写 `__args[0]` 也不够（本 Prefix 自己那次回写顺序不保证）。
        //   ⚠ 同族地雷：`ZhsMsgTrace` 也是「Prefix + `object[] __args`」，已一并改为 `ref string __0`。
        //═══════════════════════════════════════════════════════════════════════
        private static void Prefix(ref string __0, object[] __args)
        {
            if (ZhsFlightMsg.Disabled) return;
            try
            {
                // ★ 诊断（§49：从**参数数组**里读，而不是复读自己的局部引用）：
                //   `__args[0]` 若与 `__0` 不同 ⇒ 说明本 Prefix 之前已有别的 Prefix 改过实参。
                //   只打前 5 次。
                if (_argsDiag < 5 && __args != null && __args.Length > 0)
                {
                    string entry = __args[0] as string;
                    if (entry != null && entry != __0)
                    {
                        _argsDiag++;
                        UnityEngine.Debug.Log(Tag + "入口快照 ≠ 当前实参（前面已有 Prefix 改过）：'"
                            + entry + "' vs '" + __0 + "'");
                    }
                }

                // ★★ 2.16.9：**先查硬编码完整句的精确表**（全量普查第 1 步）
                //   整串精确匹配 ⇒ 零误伤；未命中再走原有的形态修复链。
                if (!ZhsHardcodedMsg.Disabled)
                {
                    string zh = ZhsHardcodedMsg.Tr(__0);
                    // ★★ 2.16.78 修正**日志判据**：
                    //   原来写 `if (!ReferenceEquals(zh, __0) && zh != __0)` ——
                    //   `Tr()` 对**未命中**的句子也返回**新字符串** ⇒ ReferenceEquals 恒假
                    //   ⇒ 条件**恒为真** ⇒ 日志对**每一条消息**都照打
                    //   ⇒ 「日志说改了」完全不能证明改了（这就是「巨蜻蜓 begins flying.」
                    //     日志显示已改、屏幕仍是英文的原因之一）。
                    //   改为**按值比较**：只有真的不同才算命中。
                    if (!string.IsNullOrEmpty(zh) && zh != __0)
                    {
                        if (ZhsHardcodedMsg.Hits < 10)
                            UnityEngine.Debug.Log("[QudZhsCookbook] 硬编码整句："
                                + __0 + "  =>  " + zh);
                        ZhsHardcodedMsg.Hits++;
                        WriteBack(ref __0, __args, zh);
                        return;
                    }
                }
                string fixedUp = ZhsFlightMsg.Fix(__0);
                if (fixedUp == null)
                {
                    // ★ 兜底：剥掉「英文冠词 + 中文」（`The 湿的 光鱼死亡!` → `湿的 光鱼死亡!`）
                    fixedUp = ZhsFlightMsg.StripBareArticle(__0);
                    if (fixedUp == null) return;
                }
                if (ZhsFlightMsg.Hits < 8)
                {
                    UnityEngine.Debug.Log(Tag + __0 + "  =>  " + fixedUp);
                }
                ZhsFlightMsg.Hits++;
                WriteBack(ref __0, __args, fixedUp);
            }
            catch { }
        }

        /// <summary>入口快照诊断计数（只打前 5 次）。</summary>
        private static int _argsDiag;

        /// <summary>
        /// ★★★ 双通道回写（2026-10-16 F1 严查的修法核心）。
        ///
        /// `__0` 是 Harmony **直接传进来的 arg0 地址**（写它就改了实参）；
        /// `__args[0]` 是 Harmony 在**方法入口**建好的快照 ——
        /// **任何一个声明了 `object[] __args` 的 Prefix（本方法上就有 `ZhsQuestTextPatch`）
        /// 都会在它跑完之后把整个快照 `starg` 回去**（生成 IL 实证）。
        /// ⇒ 只写 `__0`，等于把刚写好的中文**交给下一次 `__args` 回写去覆盖**。
        /// ⇒ 必须两边都写。
        /// </summary>
        private static void WriteBack(ref string __0, object[] __args, string value)
        {
            __0 = value;
            if (__args != null && __args.Length > 0) __args[0] = value;
            // §49：改完**从参数数组里再读一遍**（只复读自己的局部引用 = 自证，等于没证）
            string readBack = (__args != null && __args.Length > 0)
                ? (__args[0] as string) : __0;
            if (readBack != value && ZhsFlightPatch.WriteBackMisses < 5)
            {
                ZhsFlightPatch.WriteBackMisses++;
                UnityEngine.Debug.LogError(Tag + "★ 回写失败！写入='" + value
                    + "' 但从参数数组读到='" + readBack + "'");
            }
        }
    }
}
