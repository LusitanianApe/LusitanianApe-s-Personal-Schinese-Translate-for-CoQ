// 动词变形片段汉化：把 `=subject.Does:verb=` / `=subject.Does=verb` 的英文动词换成中文。
//
// 【为什么这么做】
//   官方大量消息用 `=subject.Does:<动词>=` 生成「主谓」片段，例如
//       Context="XRLUITradeUI nothing to trade popup"
//       ID="=subject.Does:have|strip= nothing to trade."
//   我们把后半句译成了中文，但 `=subject.Does:have=` 仍解析成英文 "has"，
//   于是出现「Warden Neelahind has没有可交易的东西。」这种中英夹杂。
//   IL 显示处理函数是
//       XRL.World.Text.Delegates.GameObjectReplacers::Does(VariableContext ctx, object obj)
//   动词从 `ctx.Parameters[0]` 取（见 IL_0016 ldarg.0 / ldfld Parameters）。
//
// 【能达到的效果与极限（诚实说明）】
//   把动词换成中文后，句子结构**仍是英文语序**，所以像
//       "Warden Neelahind 有 没有可交易的东西。"
//   依然别扭（「有」与「没有」语义打架）。
//   要彻底解决必须接管整句生成 —— 那超出碎片层的能力范围。
//   本补丁的目标是**消除裸露英文**，把夹杂度降到最低。
//
// 【安全】只改**结果恰好是已知英文动词变形**的场合；异常全吞；可开关停用。

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsVerbZh
    {
        public static bool Disabled;

        /// <summary>英文动词（含第三人称单数形式）-> 中文。</summary>
        public static readonly Dictionary<string, string> V =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "be", "是" }, { "is", "是" }, { "are", "是" },
            { "was", "曾是" }, { "were", "曾是" },
            { "have", "有" }, { "has", "有" }, { "had", "曾有" },
            { "do", "做" }, { "does", "做" }, { "did", "做了" },
            { "don't", "不" }, { "doesn't", "不" }, { "didn't", "没有" },
            { "take", "拿走" }, { "takes", "拿走" }, { "took", "拿走了" },
            // ★ 2026-10-17（测试窗口 #30）：`swipe` 一直不在表里 ⇒
            //   `沾血的圣所守护者 swipes 他的 blade at you.` 半译。
            { "swipe", "挥击" }, { "swipes", "挥击" }, { "swiped", "挥击了" },
            { "fall", "倒下" }, { "falls", "倒下" }, { "fell", "倒下了" },
            { "explode", "爆炸" }, { "explodes", "爆炸" },
            { "try", "尝试" }, { "tries", "尝试" },
            { "emit", "发出" }, { "emits", "发出" },
            { "transfer", "转移" }, { "transfers", "转移" },
            { "read", "读作" }, { "reads", "读作" },
            { "begin", "开始" }, { "begins", "开始" },
            { "resist", "抵抗" }, { "resists", "抵抗" },
            { "miss", "未命中" }, { "misses", "未命中" },
            { "start", "开始" }, { "starts", "开始" },
            { "shake", "震动" }, { "shakes", "震动" },
            // ★ 2.15.89：`XRL.World.Parts.Pettable::Pet` 里的
            //   `Actor.Fail(ParentObject.Does("shy") + " away from you.")`
            //   —— 动词表里没有 shy ⇒ `Does("shy")` 只能给英文 ⇒ 漏出
            //   `光猫 shies away from you.`（用户截图「严查」）。
            { "shy", "躲开" }, { "shies", "躲开" }, { "shied", "躲开了" },
            { "seem", "似乎" }, { "seems", "似乎" },
            { "feel", "感到" }, { "feels", "感到" },
            { "reflect", "反射" }, { "reflects", "反射" },
            { "pour", "倾倒" }, { "pours", "倾倒" },
            { "vibrate", "振动" }, { "vibrates", "振动" },
            { "need", "需要" }, { "needs", "需要" },
            { "activate", "激活" }, { "activates", "激活" },
            { "intercept", "拦截" }, { "intercepts", "拦截" },
            { "clean", "清洗" }, { "cleans", "清洗" },
            { "beat", "扑打" }, { "beats", "扑打" },
            { "leak", "泄漏" }, { "leaks", "泄漏" },
            { "gain", "获得" }, { "gains", "获得" },
            { "exit", "退出" }, { "exits", "退出" },
            { "wake", "醒来" }, { "wakes", "醒来" },
            { "clasp", "紧握" }, { "clasps", "紧握" },
            { "phase", "相位化" }, { "phases", "相位化" },
            { "wander", "游荡" }, { "wanders", "游荡" },
            { "stop", "停止" }, { "stops", "停止" },
            { "collapse", "坍缩" }, { "collapses", "坍缩" },
            { "detonate", "引爆" }, { "detonates", "引爆" },
            { "make", "制造" }, { "makes", "制造" },
            { "fail", "失败" }, { "fails", "失败" },
            { "flip", "翻转" }, { "flips", "翻转" },
            { "disappear", "消失" }, { "disappears", "消失" },
            { "appear", "出现" }, { "appears", "出现" },
            { "impale", "刺穿" }, { "impales", "刺穿" },
            { "sense", "感知" }, { "senses", "感知" },
            { "identify", "鉴定" }, { "identifies", "鉴定" },
            { "disarm", "卸除" }, { "disarms", "卸除" },
            { "enter", "进入" }, { "enters", "进入" },
            { "slip", "滑倒" }, { "slips", "滑倒" },
            { "grab", "抓住" }, { "grabs", "抓住" },
            { "move", "移动" }, { "moves", "移动" },
            { "attack", "攻击" }, { "attacks", "攻击" },
            { "die", "死亡" }, { "dies", "死亡" },
            { "hit", "命中" }, { "hits", "命中" },
            { "fire", "开火" }, { "fires", "开火" },
            { "open", "打开" }, { "opens", "打开" },
            { "close", "关闭" }, { "closes", "关闭" },
            { "drop", "掉落" }, { "drops", "掉落" },
            { "break", "断裂" }, { "breaks", "断裂" },
            { "burn", "燃烧" }, { "burns", "燃烧" },
            { "melt", "熔化" }, { "melts", "熔化" },
            { "freeze", "冻结" }, { "freezes", "冻结" },
            { "heal", "治愈" }, { "heals", "治愈" },
            { "regrow", "重生" }, { "regrows", "重生" },
            { "shatter", "碎裂" }, { "shatters", "碎裂" },
            { "crack", "开裂" }, { "cracks", "开裂" },
            { "wilt", "枯萎" }, { "wilts", "枯萎" },
            { "bloom", "开花" }, { "blooms", "开花" },
            { "glow", "发光" }, { "glows", "发光" },
            { "roar", "咆哮" }, { "roars", "咆哮" },
            { "hiss", "嘶鸣" }, { "hisses", "嘶鸣" },
            { "wail", "哀号" }, { "wails", "哀号" },
            { "laugh", "大笑" }, { "laughs", "大笑" },
            { "speak", "说话" }, { "speaks", "说话" },
            { "whisper", "低语" }, { "whispers", "低语" },
            { "shout", "喊叫" }, { "shouts", "喊叫" },
            { "sing", "歌唱" }, { "sings", "歌唱" },
            { "dance", "起舞" }, { "dances", "起舞" },
            { "eat", "进食" }, { "eats", "进食" },
            { "drink", "饮水" }, { "drinks", "饮水" },
            { "sleep", "睡眠" }, { "sleeps", "睡眠" },
            { "rest", "休息" }, { "rests", "休息" },
            { "wait", "等待" }, { "waits", "等待" },
            { "dig", "挖掘" }, { "digs", "挖掘" },
            { "climb", "攀爬" }, { "climbs", "攀爬" },
            { "swim", "游泳" }, { "swims", "游泳" },
            { "fly", "飞行" }, { "flies", "飞行" },
            { "land", "着陆" }, { "lands", "着陆" },
            { "leave", "离开" }, { "leaves", "离开" },
            { "return", "返回" }, { "returns", "返回" },
            { "arrive", "抵达" }, { "arrives", "抵达" },
            { "follow", "跟随" }, { "follows", "跟随" },
            { "flee", "逃跑" }, { "flees", "逃跑" },
            { "chase", "追逐" }, { "chases", "追逐" },
            { "hunt", "狩猎" }, { "hunts", "狩猎" },
            { "trade", "交易" }, { "trades", "交易" },
            { "buy", "购买" }, { "buys", "购买" },
            { "sell", "出售" }, { "sells", "出售" },
            { "give", "给予" }, { "gives", "给予" },
            { "lose", "失去" }, { "loses", "失去" },
            { "find", "找到" }, { "finds", "找到" },
            { "search", "搜寻" }, { "searches", "搜寻" },
            { "examine", "检视" }, { "examines", "检视" },
            { "wield", "持握" }, { "wields", "持握" },
            { "equip", "装备" }, { "equips", "装备" },
            { "wear", "穿戴" }, { "wears", "穿戴" },
            { "repair", "修理" }, { "repairs", "修理" },
            { "destroy", "摧毁" }, { "destroys", "摧毁" },
            { "kill", "杀死" }, { "kills", "杀死" },
            { "dismember", "肢解" }, { "dismembers", "肢解" },
            { "decapitate", "斩首" }, { "decapitates", "斩首" },
            { "knock", "击倒" }, { "knocks", "击倒" },
            { "push", "推动" }, { "pushes", "推动" },
            { "pull", "拉扯" }, { "pulls", "拉扯" },
            { "throw", "投掷" }, { "throws", "投掷" },
            { "catch", "接住" }, { "catches", "接住" },
            { "block", "格挡" }, { "blocks", "格挡" },
            { "dodge", "闪避" }, { "dodges", "闪避" },
            { "parry", "招架" }, { "parries", "招架" },
        
            // ── 以下 43 条借自 `ZhsMessagingVerbPatch`（原先只挂 
            //    `Messaging.XDidY*`，而模板走的是 `GameObjectReplacers.Does`，
            //    两边表不共享 ⇒ 这些动词明明有译文却查不到）。
            //    实测：官方模板里 `=X.Does:verb=` 共 176 种动词，
            //    原先只命中 64 种；借入后覆盖率显著提升。
            { "awaken", "\u82cf\u9192" },
            { "awakens", "\u82cf\u9192" },
            { "become", "\u53d8\u5f97" },
            { "becomes", "\u53d8\u5f97" },
            { "continue", "\u7ee7\u7eed" },
            { "continues", "\u7ee7\u7eed" },
            { "cook", "\u70f9\u996a" },
            { "cooks", "\u70f9\u996a" },
            { "craft", "\u5236\u4f5c" },
            { "crafts", "\u5236\u4f5c" },
            { "detect", "\u5bdf\u89c9" },
            { "detects", "\u5bdf\u89c9" },
            { "jump", "\u8df3\u8dc3" },
            { "jumps", "\u8df3\u8dc3" },
            { "lock", "\u9501\u4e0a" },
            { "locks", "\u9501\u4e0a" },
            { "notice", "\u6ce8\u610f\u5230" },
            { "notices", "\u6ce8\u610f\u5230" },
            { "penetrate", "\u7a7f\u900f" },
            { "penetrated", "\u7a7f\u900f\u4e86" },
            { "penetrates", "\u7a7f\u900f" },
            { "pick up", "\u62fe\u8d77" },
            { "picks up", "\u62fe\u8d77" },
            { "put on", "\u6234\u4e0a" },
            { "puts on", "\u6234\u4e0a" },
            { "receive", "\u6536\u5230" },
            { "receives", "\u6536\u5230" },
            { "say", "\u8bf4" },
            { "says", "\u8bf4" },
            { "share", "\u5206\u4eab" },
            { "shares", "\u5206\u4eab" },
            { "sit down", "\u5750\u4e0b" },
            // ★ 2026-10-18（#37）：`sit down on` / `sits down on` 原来也译「坐下」⇒ 介词 on 丢了
            //   （`Chair::SitDown` → `Actor.DidX("sit down on", Target)`）
            //   ⇒ 改成「坐到」，由 `ZhsCombatMsgPatch.FixChineseOnly` 补「上」。
            { "sit down on", "\u5750\u5230" },
            { "sit on", "\u5750\u4e0a" },
            { "sits down on", "\u5750\u5230" },
            { "stand up", "\u7ad9\u8d77" },
            { "stands up", "\u7ad9\u8d77" },
            { "take off", "\u8131\u4e0b" },
            { "takes off", "\u8131\u4e0b" },
            { "unlock", "\u89e3\u9501" },
            { "unlocks", "\u89e3\u9501" },
            { "yell", "\u558a\u9053" },
            { "yells", "\u558a\u9053" },
        
            // ── 补缺口（主语恢复后暴露的未译动词）
            { "carry", "\u643a\u5e26" },
            { "chop", "\u780d" },
            { "unequip", "\u5378\u4e0b" },
            { "regenerate", "\u518d\u751f" },
            { "loosen", "\u677e\u5f00" },
            { "tighten", "\u6536\u7d27" },
            { "destabilize", "\u5931\u7a33" },
            { "stiffen", "\u50f5\u76f4" },
            { "plop", "\u6251\u901a" },
            { "implode", "\u5185\u7206" },
            { "dust", "\u63b8" },
            { "raise", "\u4e3e\u8d77" },
            { "ignore", "\u65e0\u89c6" },
            { "blow", "\u5439" },
            { "lop", "\u780d\u4e0b" },
            { "strike", "\u523a" },
            { "cause", "\u5f15\u53d1" },
            { "revert", "\u590d\u539f" },
            { "shame", "\u7f9e\u8fb1" },
            { "flicker", "\u95ea\u70c1" },
            { "starve", "\u6328\u997f" },
            { "evade", "\u95ea\u907f" },
            { "intimidate", "\u9707\u6151" },
            { "invoke", "\u5524\u8d77" },
            { "chop off", "\u780d\u4e0b" },
            { "take a sip of", "\u555c\u996e" },
        
            // ── 补 B1 主语恢复所需的动词
            { "cannot", "\u65e0\u6cd5" },
            { "fling", "\u629b\u63b7" },
            { "focus", "\u805a\u7126" },
            { "hook", "\u52fe\u4f4f" },
            { "see", "\u770b\u89c1" },
            { "stand", "\u7ad9\u7acb" },

            // ═══════════════════════════════════════════════════════════
            // ★★ 2.16.21：补齐 `=verb:…=` 用的 **40 个动词**
            //
            //   来源：穷举 DLL + 官方 XML 里全部 `=verb:X=` 的**不同动词** = 52 个，
            //   对照我方动词表 ⇒ **缺 40 个**（`rust` 即用户截图 `Your 化学电池 rusts.`）。
            //
            //   ⚠ 为什么必须补：`ZhsVerbFormPatch` 是**中文直通闸**
            //     （挂 `Grammar.ThirdPerson`，输入已是中文就原样返回）
            //     ⇒ 但 `Grammar.ThirdPerson("rust")` → `"rusts"`
            //       而表里**没有** `rust` ⇒ 变形后查表落空 ⇒ 漏出英文。
            //   ⇒ 表里只要**有原形**，变形后也能命中（直通闸保护）。
            //   义项按 `_tools\缺失动词.txt` 的上下文逐一确认
            //     （`bat`=拍打 非「蝙蝠」· `cut`=劈开 · `run`=跑开 …）。
            // ═══════════════════════════════════════════════════════════
            { "bark", "吠叫" },
            { "barks", "吠叫" },
            { "bat", "拍打" },
            { "bats", "拍打" },
            { "beep", "哔哔叫" },
            { "beeps", "哔哔叫" },
            { "boop", "嘟嘟叫" },
            { "boops", "嘟嘟叫" },
            { "buzz", "嗡嗡叫" },
            { "buzzes", "嗡嗡叫" },
            { "chirp", "啁啾" },
            { "chirps", "啁啾" },
            { "chirr", "唧唧叫" },
            { "chirrs", "唧唧叫" },
            { "creep", "匍匐" },
            { "creeps", "匍匐" },
            { "croak", "呱呱叫" },
            { "croaks", "呱呱叫" },
            { "crumble", "崩解" },
            { "crumbles", "崩解" },
            { "cut", "劈开" },
            { "cuts", "劈开" },
            { "dehydrate", "脱水" },
            { "dehydrates", "脱水" },
            { "dissipate", "消散" },
            { "dissipates", "消散" },
            { "erupt", "喷发" },
            { "erupts", "喷发" },
            { "evaporate", "蒸发" },
            { "evaporates", "蒸发" },
            { "exhibit", "展示" },
            { "exhibits", "展示" },
            { "extrude", "挤出" },
            { "extrudes", "挤出" },
            { "fizzle", "嘶嘶作响" },
            { "fizzles", "嘶嘶作响" },
            { "flex", "屈伸" },
            { "flexes", "屈伸" },
            { "gibber", "叽里咕噜叫" },
            { "gibbers", "叽里咕噜叫" },
            { "glitch", "故障闪烁" },
            { "glitches", "故障闪烁" },
            { "growl", "低吼" },
            { "growls", "低吼" },
            { "gurgle", "咕噜作响" },
            { "gurgles", "咕噜作响" },
            { "meow", "喵喵叫" },
            { "meows", "喵喵叫" },
            { "run", "跑开" },
            { "runs", "跑开" },
            { "saunter", "踱步" },
            { "saunters", "踱步" },
            { "seek", "寻求" },
            { "seeks", "寻求" },
            { "shloop", "噗哧作响" },
            { "shloops", "噗哧作响" },
            { "slither", "蜿蜒爬行" },
            { "slithers", "蜿蜒爬行" },
            { "sniff", "嗅闻" },
            { "sniffs", "嗅闻" },
            { "splinter", "碎裂" },
            { "splinters", "碎裂" },
            { "split", "分裂" },
            { "splits", "分裂" },
            { "spurt", "喷涌" },
            { "spurts", "喷涌" },
            { "squish", "发出黏响" },
            { "squishes", "发出黏响" },
            { "stare", "凝视" },
            { "stares", "凝视" },
            { "stumble", "跌撞" },
            { "stumbles", "跌撞" },
            { "swallow", "吞没" },
            { "swallows", "吞没" },
            { "sway", "轻摇" },
            { "sways", "轻摇" },
            { "tug", "拖拽" },
            { "tugs", "拖拽" },
            { "undulate", "波状起伏" },
            { "undulates", "波状起伏" },
            { "rust", "锈蚀" },
            { "rusts", "锈蚀" },
                    { "place", "\u653e\u7f6e" },   // ★ 2026-10-16：`RelaxedVerb` 用它成句
            { "toss", "\u6254" },   // ★ 2026-10-16：`RelaxedVerb` 用它成句

            // ══════════════════════════════════════════════════════════════
            // ★★ 2026-10-16 F8 ——「状态 / 动作词 × 形态」缺口补齐
            //
            // 【用户原话（照录）】「状态类文本，例如 stuck 和 flying，疑似被漏掉了，
            //   我建议**严查这类词的各个变形形态**，会不会是**只做了原形没做变形**的补丁。」
            //
            // 【核查结论（证据见 `_tools\_F8_状态词形态矩阵.md`）】
            //   本表原来对绝大多数动词只有**原形 + 三单**两列；而
            //   `ZhsVerbZh.MatchAndReplace` / `WordEq` 只认 `-s/-es/-ies/-ed` 变形
            //   ⇒ **分词（-ing）/ 不规则过去式 / 过去分词**（`flying` / `flew` /
            //     `stuck` / `swam` / `fled` …）**一个也查不到**。
            //
            // 【本表被谁查】`ZhsVerbZh.Apply` ← `GameObjectReplacers.Does` 的 Postfix
            //   （即 XML 模板槽 `=object.Does:verb=`）与 `SweepEnglishVerbs`（逐词扫）。
            //   ⚠ **不覆盖** C# 直调路径 `Object.Does("begin")` → `GameObject.GetVerb`
            //     → `Grammar.ThirdPerson`（该路径的中文由 `ZhsVerbFormPatch` 的
            //     英文动词表决定，现仅 2 条；本轮**未改**该文件，已写进报告待办）。
            //
            // 【来源】`_tools\_census\asm-*\literal-owners.tsv` 中
            //   `Capabilities.Flight` · `Parts.Combat::SwoopAttack` · `Effects.Stuck` ·
            //   `Effects.Flying` · `Effects.Grounded` · `Effects.Overburdened` ·
            //   `Effects.Prone` 等 owner 的实参字面量形态（原形/分词/过去式）。
            // ══════════════════════════════════════════════════════════════
            // ── fly 家族（`Flight::StartFlying` / `Effects.Flying` / `Wings`）──
            { "flying", "飞行" }, { "flew", "飞行" }, { "flown", "飞行" },
            // ── begin 家族（`Flight::StartFlying` 的 `Does("begin")`）──
            { "began", "开始" }, { "begun", "开始" }, { "beginning", "开始" },
            // ── swoop 家族（`Combat::SwoopAttack`：`Does("swoop") + " down to attack."`）──
            { "swoop", "俯冲" }, { "swoops", "俯冲" }, { "swooping", "俯冲" },
            // ── swim / wade 家族（`Swimming` / `Wading` / `LiquidVolume`）──
            { "swimming", "游泳" }, { "swam", "游泳" }, { "swum", "游泳" },
            { "wade", "涉水" }, { "wades", "涉水" }, { "wading", "涉水" }, { "waded", "涉水" },
            // ── stuck 家族（`Effects.Stuck::.ctor` 的字面量就是 `stuck` / `in`）──
            { "stick", "卡住" }, { "sticks", "卡住" }, { "sticking", "卡住" },
            { "stuck", "卡住" },
            // ── 同源状态词（`Effects.*::Apply` 的 ParticleText 词根 / 状态句）──
            { "bleeding", "流血" }, { "bled", "流血" },
            { "stunning", "眩晕" },
            { "fleeing", "逃跑" }, { "fled", "逃跑" },
            { "landing", "着陆" }, { "landed", "着陆" },
};

        private const string Tag = "[QudZhsCookbook] ";
        private static int _hits;
        internal static int Count { get { return V.Count; } }
        private static bool _logged;

        public static string Apply(string s, string verb)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                string zh;
                string core = (verb ?? "").Trim();

                // ══════════════════════════════════════════════════════════
                // ★★ 0) 首选：`s` 是「主语 + 动词（+ 副词/尾部）」形态
                //     例："You miss" / "Barathrum misses" / "You already have"
                //     目标：保留主语与尾部，只把动词换成中文。
                //     —— 原实现把这一步放在最后，被第 ① 步截胡 ⇒ 主语丢失。
                // ══════════════════════════════════════════════════════════
                if (!string.IsNullOrEmpty(core))
                {
                    string hit = TrySubjectVerb(s, core);
                    if (hit != null) return Finish(hit, core);
                }

                // ══════════════════════════════════════════════════════════
                // 1) `s` 整体就是动词变形（裸动词，无主语）
                //    对应 `=subject.Does#verb=`（无空白写法）
                // ══════════════════════════════════════════════════════════
                if (!string.IsNullOrEmpty(core) && V.TryGetValue(core, out zh))
                    return Finish(zh, core);

                // 2) 前导代词回退（旧分支 ②，保留以免回归）
                string t = s.Trim();
                string[] heads = { "You ", "It ", "They ", "He ", "She ", "We ",
                                   "you ", "it ", "they ", "he ", "she ", "we " };
                foreach (string hd in heads)
                {
                    if (!t.StartsWith(hd, StringComparison.Ordinal)) continue;
                    string rest = t.Substring(hd.Length).Trim();
                    if (!V.TryGetValue(rest, out zh)) continue;
                    return Finish(SubjOf(hd.Trim()) + zh, rest);
                }

                // ══════════════════════════════════════════════════════════
                // 3) ★ 英文词兜底：`Object.does()` 的输出可能被别的 Postfix
                //    先改过（如代词替换把 "You" 换成「你」），导致上面的形态匹配
                //    对不上。这里逐词扫一遍，把**已知英文动词**换成中文。
                //    实测症状：Player.log `原返='你 pour'` —— 主语已汉化、动词没译。
                // ══════════════════════════════════════════════════════════
                string swept = SweepEnglishVerbs(s);
                if (!ReferenceEquals(swept, s) && swept != s) return Finish(swept, "(扫词)");
            }
            catch { }
            return s;
        }

        /// <summary>
        /// 逐词扫描，把独立的已知英文动词（含 -s/-es/-ies/-ed 变形）替换为中文。
        /// 中文之间不留空格；未命中的词原样保留。
        /// </summary>
        private static string SweepEnglishVerbs(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string[] toks = s.Split(' ');
            bool any = false;
            for (int i = 0; i < toks.Length; i++)
            {
                string w = toks[i];
                if (w.Length == 0 || (w[0] >= '0' && w[0] <= '9')) continue;
                string zh;
                if (V.TryGetValue(w, out zh)) { toks[i] = zh; any = true; continue; }
                // 去变形再试
                string b = null;
                if (w.EndsWith("ies", StringComparison.Ordinal) && w.Length > 3)
                    b = w.Substring(0, w.Length - 3) + "y";
                else if (w.EndsWith("es", StringComparison.Ordinal) && w.Length > 2)
                    b = w.Substring(0, w.Length - 2);
                else if (w.EndsWith("s", StringComparison.Ordinal) && w.Length > 1)
                    b = w.Substring(0, w.Length - 1);
                else if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 2)
                    b = w.Substring(0, w.Length - 2);
                if (b != null && V.TryGetValue(b, out zh)) { toks[i] = zh; any = true; }
            }
            if (!any) return s;
            string joined = string.Join(" ", toks);
            joined = System.Text.RegularExpressions.Regex.Replace(
                joined, @"(?<=[\u3000-\u9fff\uff00-\uffef])\s+(?=[\u3000-\u9fff\uff00-\uffef])", "");
            return joined;
        }

        /// <summary>代词 → 中文主语。</summary>
        private static string SubjOf(string en)
        {
            switch (en.ToLowerInvariant())
            {
                case "you": return "\u4f60";       // 你
                case "it": return "\u5b83";        // 它
                case "they": return "\u5b83\u4eec"; // 它们
                case "he": return "\u4ed6";        // 他
                case "she": return "\u5979";       // 她
                default: return "\u6211\u4eec";   // 我们
            }
        }

        /// <summary>动词的第三人称单数形式（含 -ies / -es / -s）。</summary>
        private static string ThirdOf(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            if (v.EndsWith("y", StringComparison.Ordinal) && v.Length > 1)
                return v.Substring(0, v.Length - 1) + "ies";
            if (v.EndsWith("s", StringComparison.Ordinal)
                || v.EndsWith("x", StringComparison.Ordinal)
                || v.EndsWith("ch", StringComparison.Ordinal)
                || v.EndsWith("sh", StringComparison.Ordinal))
                return v + "es";
            return v + "s";
        }

        /// <summary>
        /// 若 `s` 形如「主语 + 动词（+ 尾部）」，返回重建后的中文串；否则 null。
        ///
        /// 重建规则：主语原样保留（它已被游戏本地化/翻译）、动词换中文、尾部原样保留。
        ///   "You miss"           → "你" + "没打中"
        ///   "Barathrum misses"   → "Barathrum" + "没打中"
        ///   "You already have"   → "You already" + "有"   （副词留在主语侧）
        /// </summary>
        /// <summary>英语副词 → 中文（`=X.Does:have:adverb:already=` 之类会夹在中间）。</summary>
        private static readonly Dictionary<string, string> ADV =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "already", "\u5df2\u7ecf" }, { "just", "\u521a\u521a" },
            { "still", "\u4ecd\u7136" }, { "now", "\u73b0\u5728" },
            { "only", "\u53ea" }, { "never", "\u4ece\u4e0d" },
            { "again", "\u518d\u6b21" }, { "always", "\u603b\u662f" },
            { "loudly", "\u5927\u58f0" }, { "quietly", "\u8f7b\u58f0" },
            { "slowly", "\u7f13\u6162\u5730" }, { "quickly", "\u8fc5\u901f\u5730" },
            { "suddenly", "\u7a81\u7136" }, { "finally", "\u7ec8\u4e8e" },
        };

        /// <summary>
        /// 若 `s` 形如「主语 + 动词（+ 尾部）」，返回重建后的中文串；否则 null。
        ///
        /// ★ 匹配方式：**按空格切词、逐段与动词表比对**（而非猜 -s/-ies 规则）——
        ///    · "Barathrum misses"  → 逐词试 "misses"→未中、"miss"→中 ⇒ 命中
        ///    · "farmer sits down on" → 试 "sits down on"→未中、"sit down on"→中 ⇒ 命中
        ///   这样多词动词与不规则变形都能覆盖。
        /// </summary>
        private static string TrySubjectVerb(string s, string verb)
        {
            string zh0;
            if (!string.IsNullOrEmpty(verb) && V.TryGetValue(verb.Trim(), out zh0))
            {
                // 参数里的动词有译文 → 用「逐词比对」定位它在 s 中的位置
                string r = MatchAndReplace(s, verb.Trim(), zh0);
                if (r != null) return r;
            }

            // 参数没译文或没定位到 → 对 s 的所有「连续词窗」做一次查表，取最长命中
            string[] toks = s.Split(' ');
            for (int i = toks.Length - 1; i >= 1; i--)
            {
                for (int j = i; j >= 1 && i - j < 5; j--)
                {
                    var sbw = new System.Text.StringBuilder();
                    for (int k = j; k <= i; k++)
                    {
                        if (sbw.Length > 0) sbw.Append(' ');
                        sbw.Append(toks[k]);
                    }
                    string phrase = sbw.ToString();
                    string z;
                    if (V.TryGetValue(phrase, out z))
                    {
                        string head = string.Join(" ", toks, 0, j);
                        var tailSb = new System.Text.StringBuilder();
                        for (int k = i + 1; k < toks.Length; k++)
                        {
                            if (tailSb.Length > 0) tailSb.Append(' ');
                            tailSb.Append(toks[k]);
                        }
                        return Join(TranslateSubject(head), z, tailSb.ToString());
                    }
                }
            }
            return null;
        }

        /// <summary>用「逐词比对（含去 -s/-es/-ies）」定位 verb 并替换。</summary>
        private static string MatchAndReplace(string s, string verb, string zh)
        {
            string[] toks = s.Split(' ');
            int vlen = verb.Split(' ').Length;
            for (int j = 1; j + vlen <= toks.Length; j++)
            {
                bool all = true;
                for (int k = 0; k < vlen; k++)
                {
                    if (!WordEq(toks[j + k], verb.Split(' ')[k])) { all = false; break; }
                }
                if (!all) continue;
                string head = string.Join(" ", toks, 0, j);
                var tailSb = new System.Text.StringBuilder();
                for (int k = j + vlen; k < toks.Length; k++)
                {
                    if (tailSb.Length > 0) tailSb.Append(' ');
                    tailSb.Append(toks[k]);
                }
                return Join(TranslateSubject(head), zh, tailSb.ToString());
            }
            return null;
        }

        /// <summary>词形比较：相同，或英文第三人称单数（去 s / es / ies）。</summary>
        private static bool WordEq(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string x = a.ToLowerInvariant(), y = b.ToLowerInvariant();
            if (x == y) return true;
            if (x == y + "s" || x == y + "es") return true;
            if (y.EndsWith("y", StringComparison.Ordinal) && x == y.Substring(0, y.Length - 1) + "ies")
                return true;
            if (x == y + "ed" || x == y + "d") return true;
            return false;
        }

        /// <summary>拼装：中文之间不留空格；尾部英文保留（必要时补空格）。</summary>
        private static string Join(string head, string zh, string tail)
        {
            head = TranslateAdverbs(head).Trim();
            tail = TranslateAdverbs(tail).Trim();
            var sb = new System.Text.StringBuilder();
            sb.Append(head);
            // ★ 中文之间不留空格：仅当两侧都是 ASCII 时才补空格
            if (sb.Length > 0 && zh.Length > 0
                && IsAscii(sb[sb.Length - 1]) && IsAscii(zh[0])) sb.Append(' ');
            sb.Append(zh);
            if (tail.Length > 0)
            {
                if (sb.Length > 0 && IsAscii(sb[sb.Length - 1]) && IsAscii(tail[0]))
                    sb.Append(' ');
                sb.Append(tail);
            }
            return sb.ToString();
        }

        private static bool IsAscii(char c) { return c < 0x2E80; }

        /// <summary>
        /// 把整串里的独立英语副词换成中文，并**收紧中文之间的空格**
        /// （逐词替换会留下被换词两侧的空格：`你 already ` → `你 已经 ` → `你已经`）。
        /// </summary>
        private static string TranslateAdverbs(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (ADV.Count > 0)
            {
                string[] toks = s.Split(' ');
                for (int i = 0; i < toks.Length; i++)
                {
                    string z;
                    if (ADV.TryGetValue(toks[i].ToLowerInvariant(), out z)) toks[i] = z;
                }
                s = string.Join(" ", toks);
            }
            // 中文之间的空格收紧（CJK 区间）
            // 用逐字字符串 + 完全限定名，避免 using 与转义问题
            s = System.Text.RegularExpressions.Regex.Replace(
                s, @"(?<=[\u3000-\u9fff\uff00-\uffef])\s+(?=[\u3000-\u9fff\uff00-\uffef])", "");
            return s;
        }

        /// <summary>
        /// 主语部分的前导英语代词 → 中文。
        /// `Object.does` 对玩家输出 "You"，对 NPC 输出其专名（已是中文）。
        /// 只在**句首**替换，避免误伤专名里的同形词。
        /// </summary>
        private static string TranslateSubject(string head)
        {
            if (string.IsNullOrEmpty(head)) return head;
            string[] EN = { "You", "you", "They", "they", "It", "it",
                            "He", "he", "She", "she", "We", "we" };
            string[] ZH = { "\u4f60", "\u4f60", "\u4ed6\u4eec", "\u4ed6\u4eec",
                            "\u5b83", "\u5b83", "\u4ed6", "\u4ed6",
                            "\u5979", "\u5979", "\u6211\u4eec", "\u6211\u4eec" };
            for (int i = 0; i < EN.Length; i++)
            {
                if (!head.StartsWith(EN[i], StringComparison.Ordinal)) continue;
                if (head.Length > EN[i].Length)
                {
                    char c = head[EN[i].Length];
                    if (c != ' ' && c != '\t') continue;   // 必须是独立词
                }
                return ZH[i] + head.Substring(EN[i].Length);
            }
            return head;
        }

        /// <summary>记录命中（只打一次日志）并返回结果。</summary>
        private static string Finish(string result, string verb)
        {
            if (!_logged)
            {
                _logged = true;
                UnityEngine.Debug.Log("[QudZhsCookbook] 动词汉化生效：" + verb + " -> " + result);
            }
            _hits++;
            return result;
        }
    }

    /// <summary>挂 GameObjectReplacers.Does —— 动词变形的生产函数。</summary>
    [HarmonyPatch]
    internal static class ZhsVerbPatch
    {
        private const string Tag = "[QudZhsCookbook] ";
        private static bool _applied;

        /// <summary>
        /// 显式挂载（替代属性式 `[HarmonyPatch]` —— 后者在本项目里从未生效）。
        /// 目标：`GameObjectReplacers.Does` / `DoesAdverbially`（返回 string 的那些重载）。
        /// </summary>
        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            int n = 0;
            try
            {
                Type t = typeof(XRL.World.Text.Delegates.GameObjectReplacers);
                MethodInfo post = typeof(ZhsVerbPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null)
                {
                    UnityEngine.Debug.LogError(Tag + "动词接管：找不到 Postfix");
                    return;
                }
                // ★ 唯一 Harmony ID：原来多个重载共用 "verb.Does.2" 这类 ID，
                //   Harmony 视为「同一 ID 重复 Patch」而互相覆盖 ⇒ 只有最后一个生效。
                //   实测症状：`pour` 始终未被翻译（Player.log `原返='你 pour'`）。
                int idx = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                                                      | BindingFlags.NonPublic
                                                      | BindingFlags.Static
                                                      | BindingFlags.Instance))
                {
                    if (m.ReturnType != typeof(string)) continue;
                    if (m.Name != "Does" && m.Name != "DoesAdverbially") continue;
                    idx++;
                    try
                    {
                        // 参数类型串进 ID，保证每个重载唯一
                        var sig = new System.Text.StringBuilder();
                        foreach (ParameterInfo pi in m.GetParameters())
                            sig.Append('_').Append(pi.ParameterType.Name);
                        Harmony h = new Harmony("Nattens.QudZhsCookbook.verb."
                            + m.Name + sig + "." + idx);
                        var hm = new HarmonyMethod(post);
                        // 优先级高于 ZhsTakePatch 的代词替换，保证先跑
                        hm.priority = Priority.First;
                        h.Patch(m, postfix: hm);
                        n++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(Tag + "动词接管：挂 " + m.Name
                            + " 失败：" + e.Message);
                    }
                }
                UnityEngine.Debug.Log(Tag + "动词接管：已显式挂 " + n
                    + " 个 Does 出口（动词表 " + ZhsVerbZh.Count + " 条）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "动词接管挂载失败：" + e);
            }
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            try
            {
                Type t = typeof(XRL.World.Text.Delegates.GameObjectReplacers);
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                      | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (m.ReturnType != typeof(string)) continue;
                    if (m.Name == "Does" || m.Name == "DoesAdverbially") list.Add(m);
                }
            }
            catch { }
            return list;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return true; } catch { return false; }
        }

        private static int _calls;

        [HarmonyPostfix]
        private static void Postfix(object __0, ref string __result)
        {
            try
            {
                // 从 VariableContext.Parameters[0] 取动词
                if (__0 == null) return;
                object pars = AccessTools.Property(__0.GetType(), "Parameters")?.GetValue(__0, null);
                var list = pars as System.Collections.IList;
                if (list == null || list.Count == 0) return;
                string verb = list[0] as string;
                if (string.IsNullOrEmpty(verb)) return;

                if (_calls < 5)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] Does 挂钩：动词='" + verb
                        + "' 原返='" + __result + "'");
                    _calls++;
                }
                __result = ZhsVerbZh.Apply(__result, verb);
            }
            catch { }
        }
    }
}
