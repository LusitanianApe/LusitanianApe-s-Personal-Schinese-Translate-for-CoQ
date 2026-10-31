// ═══════════════════════════════════════════════════════════════════════
//  ZhsLexicon —— 词表中心（2026-10-25 · 补丁收拢 阶段 1）
// ═══════════════════════════════════════════════════════════════════════
//
//  【为什么】4 张表原先分散在各自的补丁类里，导致：
//    · 同一个英文词可能被两处用不同译法处理（互相不知道）
//    · 新增文本出口时，不知道去哪张表补
//  【做法】表集中在这里，原类保留**同名转发属性** ⇒ 调用方零改动。
//  【约束】只搬「词 → 词」的纯映射；带逻辑的（碎片字典的正则表）暂不搬。
//
//  Verbs   动词（`ZhsVerbZh.V` 转发）
//  Extras  补语/短语（`ZhsDidXZh.E` 转发）
//  Effects 效果名（`ZhsEffectNameCollect.L` 转发）
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsLexicon
    {
        // ── Verbs ──────────────────────────────────────────
            internal static readonly Dictionary<string, string> Verbs =
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
                { "pet", "抚摸" },     // 2026-10-25：`水藤农夫 pets 克特西普斯.`
                { "pets", "抚摸" },
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

        // ── Extras ──────────────────────────────────────────
            internal static readonly Dictionary<string, string> Extras =
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
                { "to glow", "发光" },
            // 2026-10-25: glow dims message (light manipulation)
            { "dims until it's extinguished.", "渐渐变暗，直至熄灭" },
                { "under the pressure of normality", "在常态的压迫下崩解" },
                { "up", "起来了" },
                { "up in a daze", "恍惚中转醒" },
                { "with a flash", "轰然炸开，闪过一道光" },
                { "your wound is an illusion", "意识到自己的伤口是幻觉" },
                { "your wound is an illusion, and the pain suddenly stops", "意识到自己的伤口是幻觉，疼痛骤然消失" },
            };

        // ── Effects ──────────────────────────────────────────
            internal static readonly Dictionary<string, string> Effects =
                new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ── 长刃架势（原 `ZhsEffectNamePatch` 的 6 条，统一收到这里）──
                // 2026-10-28：`XRL.World.Effects.Stuck::.ctor` 里 `DisplayName = Adjective`（默认 "stuck"）
                //   ⇒ 效果列表里的名字；**带主体**的 `stuck in 霜冻蛛网` 在 `Tr()` 里按前缀规则处理
                { "stuck",                      "被困住" },
                { "{{G|defensive stance}}",     "{{G|防御架势}}" },
                // 2026-10-25：效果书里的 `phosphorescent`（字面量 `{{m|phosphorescent}}`，DLL @10785453）
                { "{{m|phosphorescent}}",       "{{m|磷光}}" },
                { "{{R|aggressive stance}}",    "{{R|进攻架势}}" },
                { "{{W|dueling stance}}",       "{{W|决斗架势}}" },
                { "{{G|in defensive stance}}",  "{{G|处于防御架势}}" },
                { "{{R|in aggressive stance}}", "{{R|处于进攻架势}}" },
                { "{{W|in dueling stance}}",    "{{W|处于决斗架势}}" },

                // ══════════════════════════════════════════════════════════════
                // ★★ 2.16.65：**效果名整批补完**（用户截图 `sitting` 仍是英文）
                //
                // 【盲区】效果名**不走字符串表**，是**构造函数里直接赋 `DisplayName`**
                //   ⇒ 任何 XML 都改不到，只能挂构造函数替换。
                //   本表此前**只有 8 条**（液体 2 + 长刃 6）⇒ 其余全英文。
                //
                // 【范围（机械枚举，不靠猜）】
                //   工具 `_tools\_census_effect_names.py`：
                //     从 `GetDetails` 覆盖（142）+ `DisplayName = "` 赋值点（100）
                //     合并 152 个类型，扫**全部构造函数**的 IL/反编译体
                //     ⇒ 去重 **103 条**效果名字面量（产出 `_tools\_effect_displaynames.txt`）
                //   ⚠ 其中若干条是**前缀**（如 `{{C|lying on `、`{{B|engulfed by `），
                //     代码会再拼上对象名 ⇒ 译文只译到标记内文本、**保留标记与前缀形态**。
                // ══════════════════════════════════════════════════════════════
                // —— 无标记 ——
                // ★★ 2026-10-17 补：**色标被剥掉后的同名形态**
                //   【起因】用户截图：查看面板里状态名显示成**光秃秃的 `sitting`**
                //     而本表当时只有带色标的 `{{C|sitting}}` ⇒ 精确匹配落空 ⇒ 留英文。
                //   【取证】`XRL.World.Effects.Sitting::.ctor` 赋的确实是 `"{{C|sitting}}"`
                //     （`Swimming::.ctor` 同样是 `"{{B|swimming}}"`），
                //     但**查看/检查面板会把色标剥掉后再渲染** ⇒ 命中不到。
                //   【做法】给最高频的状态名**成对登记**（带色标 + 无标记），
                //     精确匹配 ⇒ 不会误伤；两条都是白名单内的原文，安全。
                //   ⚠ 与上面「带色标」那组是**不同的键**，不构成重复键。
                { "sitting", "坐着" },
                { "swimming", "游泳" },
                { "wading", "涉水" },
                { "submerged", "淹没" },
                { "prone", "倒地" },
                { "stunned", "眩晕" },
                { "paralyzed", "麻痹" },
                { "hobbled", "跛行" },
                { "piloting", "驾驶中" },
                { "warming up", "预热中" },
                { "camouflaged", "伪装" },
                { "corrected vision", "视力矫正" },
                { "covered in liquid", "被液体覆盖" },
                { "cracked", "破裂" },
                { "demolishing", "正在摧毁" },
                { "flying", "飞行" },
                { "greased", "涂满油脂" },
                { "healing", "治疗中" },
                { "hemorrhaging", "大出血" },
                { "hooked", "被钩住" },
                { "immobilized", "被定身" },
                { "incommunicado", "无法通讯" },
                { "lost", "迷失" },
                { "meditating", "冥想中" },
                { "shaken", "动摇" },
                { "shield wall", "盾墙" },
                { "springing", "蓄势待发" },
                { "sprinting", "疾奔" },
                { "stained by liquid", "被液体染色" },
                { "suppressed", "被压制" },
                { "synapse snap", "突触崩断" },
                { "syphoned", "被汲取" },
                { "time cubed", "时间立方" },
                { "time-dilated", "时间膨胀" },
                { "trance", "出神" },
                { "waking dream", "醒着的梦" },
                { "&Wwell fed (+", "&W饱食（+" },
                { "&minhabited", "&m被寄生" },
                // —— 带色标 ——
                { "{{B|engulfed by ", "{{B|被吞没于 " },
                { "{{B|frenzied}}", "{{B|狂乱}}" },
                { "{{B|mobility impaired}}", "{{B|行动不便}}" },
                { "{{B|stressed}}", "{{B|超载}}" },
                { "{{B|submerged}}", "{{B|淹没}}" },
                { "{{B|swimming}}", "{{B|游泳}}" },
                { "{{B|wading}}", "{{B|涉水}}" },
                { "{{C|enclosed in ", "{{C|被封闭于 " },
                { "{{C|hobbled}}", "{{C|跛行}}" },
                { "{{C|interdicted}}", "{{C|被禁制}}" },
                { "{{C|lying on ", "{{C|躺在 " },
                { "{{C|paralyzed}}", "{{C|麻痹}}" },
                { "{{C|piloting ", "{{C|驾驶 " },
                { "{{C|piloting}}", "{{C|驾驶中}}" },
                { "{{C|prone}}", "{{C|倒地}}" },
                { "{{C|sitting on ", "{{C|坐在 " },
                { "{{C|sitting}}", "{{C|坐着}}" },
                { "{{C|stunned}}", "{{C|眩晕}}" },
                { "{{C|unpiloted}}", "{{C|无人驾驶}}" },
                { "{{C|warming up}}", "{{C|预热中}}" },
                { "{{G|En garde!}}", "{{G|架势已起！}}" },
                { "{{G|poisoned by gas}}", "{{G|气体中毒}}" },
                { "{{G|poisoned}}", "{{G|中毒}}" },
                { "{{G|refreshed}}", "{{G|神清气爽}}" },
                { "{{K|gleaming}}", "{{K|闪光}}" },
                { "{{K|overburdened}}", "{{K|负重过度}}" },
                { "{{K|unpowered}}", "{{K|无动力}}" },
                { "{{M|mutating}}", "{{M|突变中}}" },
                { "{{M|stasis}}", "{{M|停滞}}" },
                { "{{O|invulnerable}}", "{{O|无敌}}" },
                { "{{O|shimmering}}", "{{O|闪耀}}" },
                { "{{R|FURIOUS}}", "{{R|暴怒}}" },
                { "{{R|confused}}", "{{R|困惑}}" },
                { "{{R|crippled}}", "{{R|残废}}" },
                { "{{R|enraged}}", "{{R|激怒}}" },
                { "{{R|marked}}", "{{R|被标记}}" },
                { "{{W|cardiac arrest}}", "{{W|心搏停止}}" },
                { "{{W|covered in spores}}", "{{W|覆满孢子}}" },
                { "{{W|floppy}}", "{{W|松软}}" },
                { "{{W|queasy}}", "{{W|恶心}}" },
                { "{{W|terrified}}", "{{W|恐惧}}" },
                { "{{W|wakeful}}", "{{W|清醒}}" },
                { "{{Y|omniphase}}", "{{Y|全相位}}" },
                { "{{Y|proselytized}}", "{{Y|被劝诱}}" },
                { "{{amorous|love}} tonic", "{{amorous|爱之}}补剂" },
                { "{{b|deep dreaming}}", "{{b|深度入梦}}" },
                { "{{camouflage|camouflaged}}", "{{camouflage|伪装}}" },
                { "{{c|cybernetic rejection syndrome}}", "{{c|义体排斥综合征}}" },
                { "{{freezing|freezing}}", "{{freezing|冰冻中}}" },
                { "{{freezing|frozen}}", "{{freezing|冻结}}" },
                { "{{g|illness}}", "{{g|患病}}" },
                { "{{g|nullphased}}", "{{g|零相位}}" },
                { "{{g|phase spider venom}}", "{{g|相位蛛毒}}" },
                { "{{g|vitalized}}", "{{g|活力充盈}}" },
                { "{{m|quantum-locked}}", "{{m|量子锁定}}" },
                { "{{psionic|psionically cleaved}}", "{{psionic|灵能劈裂}}" },
                { "{{rainbow|irisdual molting}}", "{{rainbow|虹彩蜕皮}}" },
                { "{{rainbow|scintillating}}", "{{rainbow|闪烁}}" },
                { "{{r|bleeding}}", "{{r|流血}}" },
                { "{{r|callow}}", "{{r|稚嫩}}" },
                { "{{r|cleaved}}", "{{r|被劈裂}}" },
                { "{{r|drained}}", "{{r|被汲取}}" },
                { "{{r|flagging}}", "{{r|衰退}}" },
                { "{{r|latched onto}}", "{{r|被附着}}" },
                { "{{r|nosebleed}}", "{{r|鼻血}}" },
                { "{{r|prowling}}", "{{r|夜行强化}}" },
                { "{{r|rusted}}", "{{r|锈蚀}}" },
                { "{{r|shamed}}", "{{r|蒙羞}}" },
                { "{{r|war trance}}", "{{r|战意出神}}" },
                { "{{urban camouflage|urban camouflage}}", "{{urban camouflage|都市伪装}}" },
                { "{{w|grounded}}", "{{w|接地}}" },
                { "{{w|hampered}}", "{{w|受阻}}" },

                // ── 架势的**其它色标变体**（`{{R|` / `{{W|` 版，2.16.65 对账时补出）──
                //   对账工具 `_tools\_reconcile_effect_names3.py`：
                //   103 条枚举名 → 缺这 2 条（`{{G|` 版早已有，红/白标版漏了）。
            };

    }
}
