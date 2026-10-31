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
        public static Dictionary<string, string> V => ZhsLexicon.Verbs;

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
