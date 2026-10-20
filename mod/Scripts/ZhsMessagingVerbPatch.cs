// ═══════════════════════════════════════════════════════════════════════════
// `Messaging.XDidYToZ` / `XDidY` 的动词中文化
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】日志实证：`You sit down on 地垫.` / `You stand up.` 等**整句英文**。
//
// 【开发者意图】（反编译 `XRL.World.Capabilities.Messaging`）
//     public static void XDidYToZ(GameObject Actor, string Verb,
//                                 string Preposition = null, GameObject Object = null, ...)
//     {
//         ...
//         textBuilder.Append("You ").Append(Verb).Append(' ');        // 玩家路径
//         ...
//         textBuilder2.Append(Actor.GetVerb(Verb));                    // NPC 路径
//         textBuilder2.Append(' ').Append(Verb);
//     }
//
//   ⇒ **动词是 C# 调用方以英文实参传进来的**（`Chair.SitDown` 传 `"sit down on"`），
//     所以字符串表**改不到** —— 只能挂 `XDidYToZ`，把 `Verb` 形参换成中文。
//
//   ⇒ 这是**最省事且最贴合设计**的接管点：一处挂上，所有走 `XDidYToZ` 的消息
//     （日志里 `Messaging::HandleMessage` 占 21/38 条）全部中文化。
//
// 【安全性】
//   * 只替换**表中确切命中**的动词；未收录的原样放行（保持英文而非改坏）
//   * 参数以 `ref` 改写，不影响其它参数
//   * 异常全吞
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsMessagingVerbPatch
    {
        private const string Tag = "[QudZhsCookbook] XDidY 动词接管：";

        private static bool _applied;
        private static int _hits, _scanned;

        /// <summary>
        /// 动词表（英文实参 → 中文）。条目按**日志实测缺失**补齐。
        /// 注意：这里的键是调用方传的**原样实参**，可能含介词（如 "sit down on"）。
        /// </summary>
        private static readonly Dictionary<string, string> Verbs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── 日志实测缺失（`_zhs_msgtrace.log`）──
            // ★★ 2.16.72：**裸动词**必须也在表里！
            //   调用方传的是 `Verb="sit"` + `Preposition="down on"`（分开两参），
            //   拼起来的键只在「介词非空」时才查得到 ⇒ 裸动词是**兜底**。
            { "sit", "坐下" },
            { "sits", "坐下" },
            { "sit down on", "坐下" },
            { "sit down", "坐下" },
            { "sits down on", "坐下" },
            { "sit on", "坐上" },
            { "stand", "站起" },
            { "stands", "站起" },
            { "stand up", "站起" },
            { "stands up", "站起" },
            // ═══════════════════════════════════════════════════════════════
            // ★★ 2.16.86 新增：`MissileWeapon` / 摧毁消息用到的动词
            //    用户截图：
            //      `水藤 is destroyed!`                    ← Verb="destroy"
            //      `水藤农夫 flinches out of the way of …`   ← Verb="flinch"
            //    取证（`_tools\_audit_missile_verbs.py` 从 `FireEvent` 反编译抽取）：
            //      XDidYToZ(gameObject7, "flinch",  "out of the way of", Object3, Extra, "!", …)
            //      XDidYToZ(gameObject7, "attempt", "to flinch out of the way of", …)
            //      XDidYToZ(gameObject7, "flinch",  "away as", …)
            //      XDidYToZ(Object3,     passByVerb, "past",   gameObject7, …)
            //    这三个词原来**不在**本表 ⇒ 第三人称分支
            //    `textBuilder2.Append(Actor.GetVerb(Verb))` 原样输出英文。
            //    ⚠ 同时收三单形态（别处可能直接传三单）。
            //    ⚠ 组合条目（`flinch out of the way of`）依赖本次的「组合优先」修正。
            // ═══════════════════════════════════════════════════════════════
            { "destroy", "摧毁" },
            { "destroys", "摧毁" },
            { "flinch", "闪避" },
            { "flinches", "闪避" },
            { "flinch out of the way of", "闪身避开" },
            { "flinches out of the way of", "闪身避开" },
            { "flinch away as", "闪身躲开，因为" },
            { "attempt", "试图" },
            { "attempts", "试图" },
            { "attempt to flinch away, but", "试图闪开，但" },
            { "attempt to flinch out of the way of", "试图闪避" },
            { "say", "说" },
            { "says", "说" },
            { "yell", "喊道" },
            { "yells", "喊道" },
            { "share", "分享" },
            { "shares", "分享" },
            { "penetrate", "穿透" },
            { "penetrates", "穿透" },
            { "penetrated", "穿透了" },
            // ── 常见补充 ──
            { "wake", "唤醒" },
            { "wakes", "醒来" },
            { "shake", "摇动" },
            { "shakes", "摇动" },
            { "gain", "获得" },
            { "gains", "获得" },
            { "lose", "失去" },
            { "loses", "失去" },
            { "receive", "收到" },
            { "receives", "收到" },
            { "drop", "丢下" },
            { "drops", "丢下" },
            { "pick up", "拾起" },
            { "picks up", "拾起" },
            { "put on", "戴上" },
            { "puts on", "戴上" },
            { "take off", "脱下" },
            { "takes off", "脱下" },
            { "eat", "吃" },
            { "eats", "吃" },
            { "drink", "喝" },
            { "drinks", "喝" },
            { "enter", "进入" },
            { "enters", "进入" },
            { "leave", "离开" },
            { "leaves", "离开" },
            { "open", "打开" },
            { "opens", "打开" },
            { "close", "关上" },
            { "closes", "关上" },
            { "lock", "锁上" },
            { "locks", "锁上" },
            { "unlock", "解锁" },
            { "unlocks", "解锁" },
            { "climb", "攀爬" },
            { "climbs", "攀爬" },
            { "jump", "跳跃" },
            { "jumps", "跳跃" },
            { "swim", "游泳" },
            { "swims", "游泳" },
            { "fall", "坠落" },
            { "falls", "坠落" },
            { "die", "死亡" },
            { "dies", "死亡" },
            { "kill", "杀死" },
            { "kills", "杀死" },
            { "attack", "攻击" },
            { "attacks", "攻击" },
            { "hit", "命中" },
            { "hits", "命中" },
            { "miss", "未命中" },
            { "misses", "未命中" },
            { "block", "格挡" },
            { "blocks", "格挡" },
            { "dodge", "闪避" },
            { "dodges", "闪避" },
            { "parry", "招架" },
            { "parries", "招架" },
            { "detect", "察觉" },
            { "detects", "察觉" },
            { "notice", "注意到" },
            { "notices", "注意到" },
            { "sense", "感知" },
            { "senses", "感知" },
            { "feel", "感到" },
            { "feels", "感到" },
            { "become", "变得" },
            { "becomes", "变得" },
            { "begin", "开始" },
            { "begins", "开始" },
            { "start", "开始" },
            { "starts", "开始" },
            { "stop", "停止" },
            { "stops", "停止" },
            { "continue", "继续" },
            { "continues", "继续" },
            { "wait", "等待" },
            { "waits", "等待" },
            { "rest", "休息" },
            { "rests", "休息" },
            { "sleep", "入睡" },
            { "sleeps", "入睡" },
            { "awaken", "苏醒" },
            { "awakens", "苏醒" },
            { "burn", "燃烧" },
            { "burns", "燃烧" },
            { "freeze", "冻结" },
            { "freezes", "冻结" },
            { "melt", "融化" },
            { "melts", "融化" },
            { "explode", "爆炸" },
            { "explodes", "爆炸" },
            { "shatter", "碎裂" },
            { "shatters", "碎裂" },
            { "break", "打破" },
            { "breaks", "打破" },
            { "repair", "修理" },
            { "repairs", "修理" },
            { "craft", "制作" },
            { "crafts", "制作" },
            { "cook", "烹饪" },
            { "cooks", "烹饪" },
            { "trade", "交易" },
            { "trades", "交易" },
            { "buy", "购买" },
            { "buys", "购买" },
            { "sell", "出售" },
            { "sells", "出售" },
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType("XRL.World.Capabilities.Messaging", false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 Messaging");
                return;
            }

            MethodInfo pre = typeof(ZhsMessagingVerbPatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            int n = 0;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static))
            {
                if (mi.Name != "XDidYToZ" && mi.Name != "XDidY"
                    && mi.Name != "XDidYToZWithArticle") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length < 2) continue;
                if (ps[1].ParameterType != typeof(string)) continue;   // 第 2 个参数是 Verb
                if (ps[1].IsOut) continue;
                try
                {
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.msgverb."
                        + mi.Name + "." + ps.Length);
                    h.Patch(mi, prefix: new HarmonyMethod(pre));
                    n++;
                }
                catch { }
            }
            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（动词表 "
                + Verbs.Count + " 条）");
        }

        /// <summary>
        /// 改写动词实参。
        ///
        /// ★★ 2.16.72 修正（用户截图 `You sit down on 地垫.` / `You stand up.` 仍是英文）：
        ///   病根 = **查表键少拼了介词**。
        ///   实测调用方（反编译 `XRL.World.Parts.Chair`）：
        ///       SitDown : `XDidYToZ(Actor, "sit", "down on", ParentObject)`
        ///       StandUp : `XDidY(Actor, "stand", "up")`
        ///   而 `Messaging` 的签名是
        ///       `XDidYToZ(Actor, Verb, Preposition, Object, Extra, …)`
        ///       `XDidY  (Actor, Verb, Extra, …)`
        ///   ⇒ `Verb` 只拿到 `"sit"` / `"stand"`，表里的 `"sit down on"` / `"stand up"`
        ///     **永远命中不了**（这是「表里有、却原文漏出」的静默失配，§53 同族）。
        ///   ⇒ 现在**把介词拼进查表键**再查一次，并以裸动词兜底。
        /// </summary>
        private static bool Prefix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 2) return true;
                string v = __args[1] as string;
                if (string.IsNullOrEmpty(v)) return true;
                _scanned++;
                ZhsHookTrace.Step("MsgVerb.in", v);

                // ═══════════════════════════════════════════════════════════════
                // ★★ 2.16.85 修：**组合优先**
                //   旧码顺序反了 —— 先查裸动词 `"sit"`（表里有）⇒ 立刻 `return true`
                //   ⇒ 后面的「动词+介词」分支**永远走不到** ⇒ `__args[2]="down on"`
                //     没被清空 ⇒ 实测 `You 坐下 down on 地垫.`（用户截图 2.16.84）
                //   现改为：**先试组合**（能把介词并进中文、并清空介词参数），
                //          组合不命中再退回裸动词。
                // ═══════════════════════════════════════════════════════════════
                string prep = __args.Length > 2 ? __args[2] as string : null;
                string zh;

                // ① 先按「动词 + 介词」查（`XDidYToZ` 的介词在 `__args[2]`）
                //    `SitDown : XDidYToZ(Actor, "sit", "down on", ParentObject)`
                if (!string.IsNullOrEmpty(prep))
                {
                    string combo = (v.Trim() + " " + prep.Trim()).Trim();
                    if (Verbs.TryGetValue(combo, out zh))
                    {
                        __args[1] = zh;
                        __args[2] = null;      // 介词已并入中文动词，置空避免重复
                        _hits++;
                        if (_hits <= 40)
                            UnityEngine.Debug.Log(Tag + "'" + combo + "'  =>  " + zh);
                        return true;
                    }
                }

                // ② 退回**裸动词**
                if (Verbs.TryGetValue(v.Trim(), out zh))
                {
                    if (zh != v)
                    {
                        __args[1] = zh;
                        // ★ 裸动词若由「动词+介词」表项覆盖同一批介词，仍需清掉介词；
                        //   但裸动词条目（如 `sit`）与介词条目（`sit on`/`sit down on`）
                        //   是**不同语义**，此处**不动 `__args[2]`**（保持原行为）。
                        _hits++;
                        if (_hits <= 40)
                            UnityEngine.Debug.Log(Tag + "'" + v + "'  =>  " + zh);
                    }
                    return true;
                }
            }
            catch { }
            return true;
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：扫描 " + _scanned
                + " 次，改写 " + _hits + " 次");
        }
    }
}
