// DidX 家族合成消息汉化（自动生成，勿手改 —— 改 `_tools\_gen_didx_patch.py`）
//
// 【为什么必须挂在出口侧】反编译实证 `Messaging::XDidY` 的拼装：
//     玩家： "You " + Verb + " " + Extra + (EndMark ?? ".")
//     他人： <名> + GetVerb(Verb) + " " + Extra + (EndMark ?? ".")
//   ⇒ **"You " 是硬编码的**，从参数侧消不掉 ⇒ 只能改**合成后的整句**。
//   出口 = `XRL.World.Capabilities.Messaging::HandleMessage(Source, Msg, …)`
//   （它再分派到 `Popup.Show` 或 `MessageQueue.AddPlayerMessage`，两条我方都已挂）。
//
// 【做法】① 精确表：玩家形整句（有限 87 条，逐字生成 ⇒ 零误伤）
//         ② 正则：他人主语形（主语动态）—— 动词用**数据里出现过的确定集合**锚定
// 【开关】装机目录放 `_off_ZhsDidXZh.txt` 即停用（由 `ZhsBootstrap.MountOff` 统一处理，无需本类自己读文件）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsDidXZh
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>Extra（谓语碎片）→ 中文谓语。</summary>
        internal static readonly Dictionary<string, string> E =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "a berserk fury", "陷入狂暴之怒" },
            { "a bit glitchy", "感觉有点不对劲" },
            { "a menacing pose", "摆出威吓的姿态" },
            { "a sip of fresh water", "啜了一口淡水" },
            { "a trance", "陷入出神状态" },
            { "a {{B|wall of force}}", "召唤出一道{{B|力墙}}" },
            { "a {{fiery|plume of flame}}", "喷出{{fiery|一股火舌}}" },
            { "an electrical arc", "释放出一道电弧" },
            { "an electromagnetic pulse", "释放出电磁脉冲" },
            { "away from the danger", "闪现脱离了危险" },
            { "back into hibernation", "重新陷入蛰伏" },
            { "back on the ambient broadcast power grid", "重新接入了环境广播电网" },
            { "been poisoned", "中毒了" },
            { "before your eyes", "眼前发花" },
            { "being", "继续存在" },
            { "crackling", "开始噼啪作响" },
            { "dazed", "晕眩了" },
            { "down", "躺下了" },
            { "forced to the surface", "被逼到地表" },
            { "free", "挣脱了" },
            { "from the ground", "从地里钻了出来" },
            { "full corporeal form again", "重新凝成完整的实体形态" },
            { "healing", "开始愈合" },
            { "hobbled", "跛行了" },
            { "in place", "被锁死在原地" },
            { "into a block of shale", "冷却成一块页岩" },
            { "into another cardiac arrest", "再次心脏骤停" },
            { "into another space", "窥入另一片空间" },
            { "into cardiac arrest", "心脏骤停了" },
            { "into flames", "猛然起火" },
            { "knocked prone", "被击倒" },
            { "leaving the ambient broadcast power grid and transitioning to backup power", "正脱离环境广播电网并转入低功耗" },
            { "less stiff", "觉得没那么僵硬了" },
            { "less stricken", "看起来没那么痛苦了" },
            { "locked, and you don't have the key", "锁着，而你没有钥匙" },
            { "no longer choking", "不再窒息" },
            { "no longer crippled", "不再残废" },
            { "no longer ill", "不再患病" },
            { "no longer poisoned", "不再中毒" },
            { "off the stun", "甩掉了眩晕" },
            { "one of your wounds is an illusion", "意识到自己有一处伤口是幻觉" },
            { "one of your wounds is an illusion, and the pain from it suddenly stops", "意识到自己有一处伤口是幻觉，疼痛骤然消失" },
            { "overwhelmed with terror", "被恐惧淹没" },
            { "paralyzed", "瘫痪了" },
            { "polarity", "翻转了极性" },
            { "revealed", "暴露了" },
            { "scintillating", "停止闪烁" },
            { "scintillating in {{rainbow|prismatic hues}}", "开始泛出{{rainbow|虹彩斑斓}}的光" },
            { "sleep mode", "退出了睡眠模式" },
            { "sleep mode by emergency interrupt", "因紧急中断而退出睡眠模式" },
            { "spacetime in the local region", "扰动了此地的时空" },
            { "stunned", "被震晕了" },
            { "suppressed", "被压制了" },
            { "the spores off", "抖落了孢子" },
            { "thinking out loud", "在自言自语" },
            { "to dust", "化作尘土" },
            { "to flicker in and out of corporeality", "开始在虚实之间明灭" },
            { "to have absorbed some of the electrical charge", "似乎吸收了一部分电荷" },
            { "to shine over the full spectrum", "开始泛出全频谱的光" },
            { "under the pressure of normality", "在常态的压迫下崩解" },
            { "up", "起来了" },
            { "up in a daze", "恍惚中转醒" },
            { "with a flash", "轰然炸开，闪过一道光" },
            { "your wound is an illusion", "意识到自己的伤口是幻觉" },
            { "your wound is an illusion, and the pain suddenly stops", "意识到自己的伤口是幻觉，疼痛骤然消失" },
        };

        /// <summary>玩家形整句 → 中文（逐字生成，精确匹配）。</summary>
        internal static readonly Dictionary<string, string> EXACT =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "You enter a berserk fury!", "你陷入狂暴之怒！" },
            { "You 进入 a berserk fury!", "你陷入狂暴之怒！" },
            { "You feel a bit glitchy.", "你感觉有点不对劲。" },
            { "You 感到 a bit glitchy.", "你感觉有点不对劲。" },
            { "You assume a menacing pose.", "你摆出威吓的姿态。" },
            { "You take a sip of fresh water.", "你啜了一口淡水。" },
            { "You enter a trance!", "你陷入出神状态！" },
            { "You 进入 a trance!", "你陷入出神状态！" },
            { "You conjure a {{B|wall of force}}!", "你召唤出一道{{B|力墙}}！" },
            { "You emit a {{fiery|plume of flame}}!", "你喷出{{fiery|一股火舌}}！" },
            { "You discharge an electrical arc!", "你释放出一道电弧！" },
            { "You emit an electromagnetic pulse.", "你释放出电磁脉冲。" },
            { "You blink away from the danger.", "你闪现脱离了危险。" },
            { "You lapse back into hibernation.", "你重新陷入蛰伏。" },
            { "You are back on the ambient broadcast power grid.", "你重新接入了环境广播电网。" },
            { "You have been poisoned!", "你中毒了！" },
            { "You swim before your eyes!", "你眼前发花！" },
            { "You 游泳 before your eyes!", "你眼前发花！" },
            { "You continue being.", "你继续存在。" },
            { "You 继续 being.", "你继续存在。" },
            { "You start crackling.", "你开始噼啪作响。" },
            { "You 开始 crackling.", "你开始噼啪作响。" },
            { "You are dazed.", "你晕眩了。" },
            { "You lie down.", "你躺下了。" },
            { "You are forced to the surface.", "你被逼到地表。" },
            { "You break free!", "你挣脱了！" },
            { "You 打破 free!", "你挣脱了！" },
            { "You emerge from the ground.", "你从地里钻了出来。" },
            { "You assume full corporeal form again.", "你重新凝成完整的实体形态。" },
            { "You begin healing.", "你开始愈合。" },
            { "You 开始 healing.", "你开始愈合。" },
            { "You are hobbled!", "你跛行了！" },
            { "You lock in place.", "你被锁死在原地。" },
            { "You 锁上 in place.", "你被锁死在原地。" },
            { "You cool into a block of shale.", "你冷却成一块页岩。" },
            { "You go into another cardiac arrest!", "你再次心脏骤停！" },
            { "You peer into another space.", "你窥入另一片空间。" },
            { "You go into cardiac arrest!", "你心脏骤停了！" },
            { "You erupt into flames!", "你猛然起火！" },
            { "You are knocked prone!", "你被击倒！" },
            { "You are leaving the ambient broadcast power grid and transitioning to backup power.", "你正脱离环境广播电网并转入低功耗。" },
            { "You feel less stiff.", "你觉得没那么僵硬了。" },
            { "You 感到 less stiff.", "你觉得没那么僵硬了。" },
            { "You look less stricken.", "你看起来没那么痛苦了。" },
            { "You are locked, and you don't have the key.", "你锁着，而你没有钥匙。" },
            { "You are no longer choking!", "你不再窒息！" },
            { "You are no longer crippled!", "你不再残废！" },
            { "You are no longer ill.", "你不再患病。" },
            { "You are no longer poisoned!", "你不再中毒！" },
            { "You shake off the stun.", "你甩掉了眩晕。" },
            { "You 摇动 off the stun.", "你甩掉了眩晕。" },
            { "You realize one of your wounds is an illusion.", "你意识到自己有一处伤口是幻觉。" },
            { "You realize one of your wounds is an illusion, and the pain from it suddenly stops.", "你意识到自己有一处伤口是幻觉，疼痛骤然消失。" },
            { "You are overwhelmed with terror!", "你被恐惧淹没！" },
            { "You are paralyzed!", "你瘫痪了！" },
            { "You flip polarity.", "你翻转了极性。" },
            { "You are revealed!", "你暴露了！" },
            { "You stop scintillating.", "你停止闪烁。" },
            { "You 停止 scintillating.", "你停止闪烁。" },
            { "You start scintillating in {{rainbow|prismatic hues}}!", "你开始泛出{{rainbow|虹彩斑斓}}的光！" },
            { "You 开始 scintillating in {{rainbow|prismatic hues}}!", "你开始泛出{{rainbow|虹彩斑斓}}的光！" },
            { "You exit sleep mode.", "你退出了睡眠模式。" },
            { "You exit sleep mode by emergency interrupt.", "你因紧急中断而退出睡眠模式。" },
            { "You shift spacetime in the local region.", "你扰动了此地的时空。" },
            { "You remain stunned!", "你被震晕了！" },
            { "You are stunned!", "你被震晕了！" },
            { "You are suppressed!", "你被压制了！" },
            { "You shake the spores off.", "你抖落了孢子。" },
            { "You 摇动 the spores off.", "你抖落了孢子。" },
            { "You thinking out loud.", "你在自言自语。" },
            { "You crumble to dust!", "你化作尘土！" },
            { "You begin to flicker in and out of corporeality.", "你开始在虚实之间明灭。" },
            { "You 开始 to flicker in and out of corporeality.", "你开始在虚实之间明灭。" },
            { "You seem to have absorbed some of the electrical charge.", "你似乎吸收了一部分电荷。" },
            { "You start to shine over the full spectrum!", "你开始泛出全频谱的光！" },
            { "You 开始 to shine over the full spectrum!", "你开始泛出全频谱的光！" },
            { "You collapse under the pressure of normality.", "你在常态的压迫下崩解。" },
            { "You wake up.", "你起来了。" },
            { "You 唤醒 up.", "你起来了。" },
            { "You stand up.", "你起来了。" },
            { "You 站起 up.", "你起来了。" },
            { "You wake up in a daze.", "你恍惚中转醒。" },
            { "You 唤醒 up in a daze.", "你恍惚中转醒。" },
            { "You explode with a flash!", "你轰然炸开，闪过一道光！" },
            { "You 爆炸 with a flash!", "你轰然炸开，闪过一道光！" },
            { "You realize your wound is an illusion.", "你意识到自己的伤口是幻觉。" },
            { "You realize your wound is an illusion, and the pain suddenly stops.", "你意识到自己的伤口是幻觉，疼痛骤然消失。" },
        };

        /// <summary>他人主语形：`<主语> <动词> <Extra><句末>` —— 两段都必须落在确定集合里。</summary>
        internal static readonly Regex SUBJ = new Regex(
            @"^(?<s>[^\r\n]{1,40}?)\s+(?<v>are|ares|assume|assumes|begin|begins|blink|blinks|break|breaks|collapse|collapses|conjure|conjures|continue|continues|cool|cools|crumble|crumbles|discharge|discharges|emerge|emerges|emit|emits|enter|enters|erupt|erupts|exit|exits|explode|explodes|feel|feels|flip|flips|go|gos|had|has|have|haves|lapse|lapses|lie|lies|lock|locks|look|looks|peer|peers|realize|realizes|remain|remains|seem|seems|shake|shakes|shift|shifts|stand|stands|start|starts|stop|stops|swim|swims|take|takes|wake|wakes|were|[\u4e00-\u9fff]{1,8})\s+(?<e>leaving\ the\ ambient\ broadcast\ power\ grid\ and\ transitioning\ to\ backup\ power|one\ of\ your\ wounds\ is\ an\ illusion,\ and\ the\ pain\ from\ it\ suddenly\ stops|your\ wound\ is\ an\ illusion,\ and\ the\ pain\ suddenly\ stops|to\ have\ absorbed\ some\ of\ the\ electrical\ charge|scintillating\ in\ \{\{rainbow\|prismatic\ hues\}\}|back\ on\ the\ ambient\ broadcast\ power\ grid|to\ flicker\ in\ and\ out\ of\ corporeality|locked,\ and\ you\ don't\ have\ the\ key|sleep\ mode\ by\ emergency\ interrupt|one\ of\ your\ wounds\ is\ an\ illusion|under\ the\ pressure\ of\ normality|to\ shine\ over\ the\ full\ spectrum|spacetime\ in\ the\ local\ region|into\ another\ cardiac\ arrest|a\ \{\{fiery\|plume\ of\ flame\}\}|your\ wound\ is\ an\ illusion|full\ corporeal\ form\ again|an\ electromagnetic\ pulse|overwhelmed\ with\ terror|forced\ to\ the\ surface|into\ a\ block\ of\ shale|back\ into\ hibernation|a\ \{\{B\|wall\ of\ force\}\}|away\ from\ the\ danger|a\ sip\ of\ fresh\ water|into\ cardiac\ arrest|no\ longer\ crippled|no\ longer\ poisoned|into\ another\ space|no\ longer\ choking|thinking\ out\ loud|an\ electrical\ arc|before\ your\ eyes|from\ the\ ground|a\ menacing\ pose|a\ berserk\ fury|the\ spores\ off|a\ bit\ glitchy|less\ stricken|no\ longer\ ill|knocked\ prone|scintillating|been\ poisoned|up\ in\ a\ daze|with\ a\ flash|off\ the\ stun|into\ flames|sleep\ mode|less\ stiff|suppressed|paralyzed|crackling|a\ trance|revealed|in\ place|polarity|healing|stunned|hobbled|to\ dust|dazed|being|down|free|up)(?<end>[.!?])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// ★★ 2026-10-15：**放宽分支** —— 谓词**已是中文**的场合（谓词是运行时变量/中文名/中文值）。
        ///
        /// 【为什么需要】2026-10-15 普查（`_tools\_rewrite_engine_scale.py`）：
        ///   动词位是字面量的 DidX 家族调用点 **175** 处，其中
        ///     · 谓词位有字面量（`E` 表可精确匹配） 87（50%）
        ///     · 谓词位**无字面量（纯变量）**       88（50%）  ← 这一支原来必然落空
        ///   ⇒ 那 88 处的形态统一是 `<主语> <英文动词> <中文变量>`，只要给动词补上中文即可。
        ///
        /// 【安全设计（关键）】动词中文**只能来自** `ZhsVerbZh.V`（402 条）；
        ///   **查不到就完全不动**（宁可留整句英文，**绝不静默丢掉动词的实义**）。
        ///   —— 这正是本项目对「丢动词」的一贯边界：**丢的是语法位，不是实义**。
        /// 【动词槽为什么不复制 `SUBJ` 的巨表】真正的过滤器是「动词表里有没有中文」，
        ///   所以这里用宽松的 `[A-Za-z]{2,20}` + 查表 ✓（少一处需要同步维护的重复清单）。
        /// </summary>
        internal static readonly Regex SUBJ_ZH = new Regex(
            @"^(?<s>[^\r\n]{1,40}?)\s+(?<v>[A-Za-z][A-Za-z'\-]{1,19})\s+(?<e>[^\r\n]*[\u4e00-\u9fff][^\r\n]*?)(?<end>[.!?])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>系词形（`are/is/was/were` **不在** `ZhsMessagingVerbPatch` 的动词表里）。</summary>
        internal static readonly Regex COPULA = new Regex(
            @"^You\s+(?:are|is|was|were|am)\s+(?<e>leaving\ the\ ambient\ broadcast\ power\ grid\ and\ transitioning\ to\ backup\ power|one\ of\ your\ wounds\ is\ an\ illusion,\ and\ the\ pain\ from\ it\ suddenly\ stops|your\ wound\ is\ an\ illusion,\ and\ the\ pain\ suddenly\ stops|to\ have\ absorbed\ some\ of\ the\ electrical\ charge|scintillating\ in\ \{\{rainbow\|prismatic\ hues\}\}|back\ on\ the\ ambient\ broadcast\ power\ grid|to\ flicker\ in\ and\ out\ of\ corporeality|locked,\ and\ you\ don't\ have\ the\ key|sleep\ mode\ by\ emergency\ interrupt|one\ of\ your\ wounds\ is\ an\ illusion|under\ the\ pressure\ of\ normality|to\ shine\ over\ the\ full\ spectrum|spacetime\ in\ the\ local\ region|into\ another\ cardiac\ arrest|a\ \{\{fiery\|plume\ of\ flame\}\}|your\ wound\ is\ an\ illusion|full\ corporeal\ form\ again|an\ electromagnetic\ pulse|overwhelmed\ with\ terror|forced\ to\ the\ surface|into\ a\ block\ of\ shale|back\ into\ hibernation|a\ \{\{B\|wall\ of\ force\}\}|away\ from\ the\ danger|a\ sip\ of\ fresh\ water|into\ cardiac\ arrest|no\ longer\ crippled|no\ longer\ poisoned|into\ another\ space|no\ longer\ choking|thinking\ out\ loud|an\ electrical\ arc|before\ your\ eyes|from\ the\ ground|a\ menacing\ pose|a\ berserk\ fury|the\ spores\ off|a\ bit\ glitchy|less\ stricken|no\ longer\ ill|knocked\ prone|scintillating|been\ poisoned|up\ in\ a\ daze|with\ a\ flash|off\ the\ stun|into\ flames|sleep\ mode|less\ stiff|suppressed|paralyzed|crackling|a\ trance|revealed|in\ place|polarity|healing|stunned|hobbled|to\ dust|dazed|being|down|free|up)(?<end>[.!?])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// ★★ 动词**已被** `ZhsMessagingVerbPatch` 换成中文的场合（`enter`/`feel`/`stand`/
        ///   `wake`/`lock`/`break`… 那 138 条表里有）⇒ 出口文本形如
        ///   `You 进入 a berserk fury!` —— Extra 仍是英文 ⇒ 必须单独认这种形态。
        /// </summary>
        internal static readonly Regex YOU_ZH = new Regex(
            @"^You\s*(?<v>[\u4e00-\u9fff]{1,8})\s*(?<e>leaving\ the\ ambient\ broadcast\ power\ grid\ and\ transitioning\ to\ backup\ power|one\ of\ your\ wounds\ is\ an\ illusion,\ and\ the\ pain\ from\ it\ suddenly\ stops|your\ wound\ is\ an\ illusion,\ and\ the\ pain\ suddenly\ stops|to\ have\ absorbed\ some\ of\ the\ electrical\ charge|scintillating\ in\ \{\{rainbow\|prismatic\ hues\}\}|back\ on\ the\ ambient\ broadcast\ power\ grid|to\ flicker\ in\ and\ out\ of\ corporeality|locked,\ and\ you\ don't\ have\ the\ key|sleep\ mode\ by\ emergency\ interrupt|one\ of\ your\ wounds\ is\ an\ illusion|under\ the\ pressure\ of\ normality|to\ shine\ over\ the\ full\ spectrum|spacetime\ in\ the\ local\ region|into\ another\ cardiac\ arrest|a\ \{\{fiery\|plume\ of\ flame\}\}|your\ wound\ is\ an\ illusion|full\ corporeal\ form\ again|an\ electromagnetic\ pulse|overwhelmed\ with\ terror|forced\ to\ the\ surface|into\ a\ block\ of\ shale|back\ into\ hibernation|a\ \{\{B\|wall\ of\ force\}\}|away\ from\ the\ danger|a\ sip\ of\ fresh\ water|into\ cardiac\ arrest|no\ longer\ crippled|no\ longer\ poisoned|into\ another\ space|no\ longer\ choking|thinking\ out\ loud|an\ electrical\ arc|before\ your\ eyes|from\ the\ ground|a\ menacing\ pose|a\ berserk\ fury|the\ spores\ off|a\ bit\ glitchy|less\ stricken|no\ longer\ ill|knocked\ prone|scintillating|been\ poisoned|up\ in\ a\ daze|with\ a\ flash|off\ the\ stun|into\ flames|sleep\ mode|less\ stiff|suppressed|paralyzed|crackling|a\ trance|revealed|in\ place|polarity|healing|stunned|hobbled|to\ dust|dazed|being|down|free|up)(?<end>[.!?])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// ★★ 2026-10-15：**放宽分支**（共用纯函数，`Tr` 与 `ZhsCombatMsgPatch.TranslateText` 都调）。
        ///
        /// 形态：`<主语> <英文动词> <**含中文**的谓词><句末>` ⇒ 用动词表补中文动词 ⇒
        ///   `主语 + 中文动词 + 谓词`。
        /// 【为什么】2026-10-15 普查：DidX 家族 175 处调用点里 **88 处（50%）谓词位是纯变量**
        ///   （`<主语> <动词> <中文变量>`）⇒ `E` 表（精确键）必然落空。
        /// 【安全】动词中文**只能来自** `ZhsVerbZh.V`；**查不到就完全不动** ——
        ///   宁可留英文（由骨架审计追踪），**绝不静默丢动词实义**。
        /// </summary>
        public static string RelaxedVerb(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                Match mz = SUBJ_ZH.Match(s);
                if (!mz.Success) return s;
                string zhv;
                if (!ZhsVerbZh.V.TryGetValue(mz.Groups["v"].Value, out zhv)
                    || string.IsNullOrEmpty(zhv)) return s;
                string subj = mz.Groups["s"].Value;
                if (subj == "You" || subj == "you") subj = "\u4f60";
                return subj + zhv + mz.Groups["e"].Value + End(mz.Groups["end"].Value);
            }
            catch { return s; }
        }

        internal static string Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r = null;
            if (EXACT.TryGetValue(s, out string ex)) r = ex;
            if (r == null)
            {
                Match m = COPULA.Match(s);
                if (m.Success) r = "你" + E[m.Groups["e"].Value] + End(m.Groups["end"].Value);
            }
            if (r == null)
            {
                Match m = YOU_ZH.Match(s);
                if (m.Success) r = "你" + m.Groups["v"].Value + E[m.Groups["e"].Value]
                    + End(m.Groups["end"].Value);
            }
            if (r == null)
            {
                Match m = SUBJ.Match(s);
                if (m.Success)
                {
                    string subj = m.Groups["s"].Value;
                    if (subj == "You" || subj == "you") subj = "你";
                    r = subj + E[m.Groups["e"].Value] + End(m.Groups["end"].Value);
                }
            }
            if (r == null)
            {
                // ★★ 2026-10-15（放宽分支）：见 `RelaxedVerb`（抽成共用函数，两条出口都调）
                string rv = RelaxedVerb(s);
                if (rv != s) r = rv;
            }
            if (r == null)
            {
                // ★★ 2026-10-15：三条正则都要求**谓语还是英文**，但谓语可能已被
                //   `ZhsXDidYPatch` 先翻成中文（两个补丁挂同一类型 `Messaging`，
                //   谁先跑由 Harmony 挂载顺序决定）⇒ 出口会剩一个**孤立系词**：
                //     `甲虫臀 is 被恐惧压倒!`
                //   ⇒ 兜底丢系词（共用纯函数，与挂载顺序无关 · §36）
                string fx = ZhsCopula.Fix(s);
                if (fx == s) return s;
                r = fx;
            }

            // ★★ 2.16.122：**必须留痕** ——「已挂 N 个出口」只证明挂载，
            //   不证明**执行过**（§16 / §46）。前 12 条逐条记「原文 => 译文」，
            //   之后每 100 次记一条累计，便于事后从 `Player.log` 判断有没有干活。
            Hits++;
            if (Hits <= 12)
                UnityEngine.Debug.Log("[QudZhsCookbook] DidX 合成消息 #" + Hits
                    + "：'" + s + "' => '" + r + "'");
            else if (Hits % 100 == 0)
                UnityEngine.Debug.Log("[QudZhsCookbook] DidX 合成消息：累计已替换 " + Hits + " 条");
            return r;
        }

        private static string End(string e)
        {
            return e.Replace('.', '。').Replace('!', '！').Replace('?', '？');
        }

        internal static void TryApply()
        {
            if (Disabled) return;
            try
            {
                Type t = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = a.GetType("XRL.World.Capabilities.Messaging", false);
                    if (t != null) break;
                }
                if (t == null) { UnityEngine.Debug.Log("[QudZhsCookbook] DidX：找不到 Messaging"); return; }
                Harmony h = new Harmony("Nattens.QudZhsCookbook.didx");
                MethodInfo pre = typeof(ZhsDidXZh).GetMethod("HandlePrefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Static))
                {
                    if (m.Name != "HandleMessage") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2 || ps[1].ParameterType != typeof(string)) continue;
                    try { h.Patch(m, prefix: new HarmonyMethod(pre)); n++; } catch { }
                }
                UnityEngine.Debug.Log("[QudZhsCookbook] DidX 合成消息：已挂 " + n
                    + " 个出口（精确 " + EXACT.Count + " 条 + 主语形规则 " + E.Count + " 个谓语）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] DidX 挂载失败：" + e.Message);
            }
        }

        private static void HandlePrefix(object[] __args)
        {
            if (Disabled || __args == null || __args.Length < 2) return;
            if (!(__args[1] is string s) || s.Length == 0) return;
            string z = Tr(s);
            if (!ReferenceEquals(z, s) && z != s) __args[1] = z;   // ★ 就地改元素（§49）
        }
    }
}
