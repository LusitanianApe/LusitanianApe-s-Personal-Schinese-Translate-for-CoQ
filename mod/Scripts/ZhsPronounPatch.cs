// 代词/称谓接管 —— 第 2 版（诊断优先）。
//
// 第 1 版（0.26.0）失败复盘：
//   给 XRL.World.BasePronounProvider 挂了 22 个 getter 的 Postfix，日志显示挂钩成功，
//   但实测英文代词残留数百处、中文代词 0 处 → **Postfix 从未真正执行**。
//   最可能原因：Postfix 签名里用了 `MethodBase __originalMethod` 同时又带 `object __instance`，
//   Harmony 注入失败会抛异常，而我的 `catch { }` 把它**静默吞掉**了（自己挖的坑）。
//
// 本版改动：
//   1. **去掉 `__originalMethod`**，拆成两个独立的 Postfix（代词 / 称谓），参数最小化
//   2. **不再静默吞异常**：出错写日志（带次数上限），便于诊断
//   3. 额外挂 `XRL.Language.Grammar` 的 ObjectPronoun / PossessivePronoun / ReflexivePronoun /
//      RandomShePronoun —— 这些是另一条可能的代词出口
//   4. 加"调用计数"日志：`_pronounHits` 每命中一次 +1，句末/够数时打印，
//      用来**确定性判断**这条路到底走不走得通

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPronounPatch
    {
        private static readonly string[] PronounGetters =
        {
            "get_Subjective", "get_CapitalizedSubjective",
            "get_Objective", "get_CapitalizedObjective",
            "get_PossessiveAdjective", "get_CapitalizedPossessiveAdjective",
            "get_SubstantivePossessive", "get_CapitalizedSubstantivePossessive",
            "get_Reflexive", "get_CapitalizedReflexive",
        };

        private static readonly string[] TermGetters =
        {
            "get_PersonTerm", "get_CapitalizedPersonTerm",
            "get_ImmaturePersonTerm", "get_CapitalizedImmaturePersonTerm",
            "get_FormalAddressTerm", "get_CapitalizedFormalAddressTerm",
            "get_OffspringTerm", "get_CapitalizedOffspringTerm",
            "get_SiblingTerm", "get_CapitalizedSiblingTerm",
            "get_ParentTerm", "get_CapitalizedParentTerm",
        };

        // Grammar 上的代词出口（另一条可能路径）
        private static readonly string[] GrammarMethods =
        {
            "ObjectPronoun", "PossessivePronoun", "ReflexivePronoun", "RandomShePronoun",
        };

        // ★ 真正的出口（0.29.0 实测定位）：
        //   `=spice.entity:subjectPronoun=` 由 XRL.World.Text.Delegates.GameObjectReplacers
        //   里名为 Subjective / Objective / Possessive / … 的**静态替换器**渲染。
        //   源码：Assets\XRL Application\World\Text\Delegates\Delegates.GameObjectReplacers.cs
        //   前两版挂的 BasePronounProvider 只是"数据源"，实测**零次调用**；这里才是入口。
        private static readonly string[] GameObjectReplacerNames =
        {
            // 代词
            "Subjective", "Objective", "Possessive", "SubstantivePossessive", "Reflexive",
            // 称谓
            "PersonTerm", "ImmaturePersonTerm", "FormalAddressTerm",
            "OffspringTerm", "SiblingTerm", "ParentTerm",
        };

        // ---------------- 代词对照 ----------------
        private static readonly Dictionary<string, string> PronounMap = new Dictionary<string, string>
        {
            { "he", "他" }, { "him", "他" }, { "his", "他的" }, { "himself", "他自己" },
            { "she", "她" }, { "her", "她" }, { "hers", "她的" }, { "herself", "她自己" },
            { "it", "它" }, { "its", "它的" }, { "itself", "它自己" },
            { "they", "他们" }, { "them", "他们" }, { "their", "他们的" },
            { "theirs", "他们的" }, { "themselves", "他们自己" }, { "themself", "ta自己" },
            { "xe", "杝" }, { "xem", "杝" }, { "xyr", "杝的" }, { "xyrs", "杝的" }, { "xemself", "杝自己" },
            { "ey", "牠" }, { "em", "牠" }, { "eir", "牠的" }, { "eirs", "牠的" }, { "emself", "牠自己" },
            { "sie", "牠" }, { "hir", "牠" }, { "hirs", "牠的" }, { "hirself", "牠自己" },
            { "ze", "牠" }, { "zir", "牠" }, { "zirs", "牠的" }, { "zirself", "牠自己" },
            { "you", "你" }, { "your", "你的" }, { "yours", "你的" },
            { "yourself", "你自己" }, { "yourselves", "你们自己" },
        };

        private static readonly Dictionary<string, string> PseudoPluralMap = new Dictionary<string, string>
        {
            { "they", "ta" }, { "them", "ta" }, { "their", "ta的" },
            { "theirs", "ta的" }, { "themselves", "ta自己" }, { "themself", "ta自己" },
        };

        // ---------------- 称谓对照（Genders.xml 的 34 个值全集）----------------
        private static readonly Dictionary<string, string> TermMap = new Dictionary<string, string>
        {
            { "man", "男人" }, { "men", "男人们" },
            { "woman", "女人" }, { "women", "女人们" },
            { "person", "人" }, { "people", "人们" }, { "humans", "人类" },
            { "boy", "男孩" }, { "boys", "男孩们" },
            { "girl", "女孩" }, { "girls", "女孩们" },
            { "child", "孩子" }, { "children", "孩子们" }, { "faun", "幼鹿" },
            { "friend", "朋友" }, { "friends", "朋友们" },
            { "buck", "公鹿" }, { "my doe", "我的母鹿" },
            { "son", "儿子" }, { "sons", "儿子们" },
            { "daughter", "女儿" }, { "daughters", "女儿们" },
            { "brother", "兄弟" }, { "brothers", "兄弟们" },
            { "sister", "姐妹" }, { "sisters", "姐妹们" },
            { "sib", "同胞" }, { "sibs", "同胞们" }, { "sibling", "同胞" },
            { "father", "父亲" }, { "fathers", "父亲们" },
            { "mother", "母亲" }, { "mothers", "母亲们" },
            { "parent", "双亲之一" }, { "progenitors", "先祖" },
        };

        // 诊断计数
        private static int _pronounCalls;
        private static int _termCalls;
        private static int _grammarCalls;
        private static int _errors;
        // ★ 无条件进入计数：不管有没有替换都 +1，用来区分
        //   "挂钩没被调用" vs "被调用了但值不在对照表里"
        private static int _postfixEntries;
        private static readonly Dictionary<string, int> RawSamples = new Dictionary<string, int>();
        private static readonly object Gate = new object();

        /// <summary>供 ZhsEntityPropPatch 复用：把英文代词译成中文。pseudoPlural=true 用 ta 系。</summary>
        public static bool TryTranslatePronoun(string lower, bool pseudoPlural, out string zh)
        {
            zh = null;
            if (string.IsNullOrEmpty(lower)) return false;
            if (pseudoPlural && PseudoPluralMap.TryGetValue(lower, out zh)) return true;
            return PronounMap.TryGetValue(lower, out zh);
        }

        /// <summary>
        /// 供 ZhsEntityPropPatch 复用：**按"格"**取中文。
        ///
        /// 为什么需要单独一个方法：英文里 her 既是宾格("him/her")又是所有格("his/her")，
        /// 中文却是"她"与"她的"两个词。而 PronounMap 是扁平表（同名只能留一个），
        /// 实测就出现了 possessivePronoun = her -> 她 这种错误（应为"她的"）。
        /// 所以写入侧按属性键走这个**分格**表。
        /// </summary>
        public static bool TryTranslatePronounByCase(string key, string lower, out string zh)
        {
            zh = null;
            if (string.IsNullOrEmpty(lower)) return false;
            switch (key)
            {
                case "subjectPronoun":
                    return SubjectMap.TryGetValue(lower, out zh);
                case "objectPronoun":
                    return ObjectMap.TryGetValue(lower, out zh);
                case "possessivePronoun":
                    return PossessiveMap.TryGetValue(lower, out zh);
                case "reflexivePronoun":
                    return ReflexiveMap.TryGetValue(lower, out zh);
                default:
                    return PronounMap.TryGetValue(lower, out zh);
            }
        }

        // ---- 分格对照（写入侧用；避免 her/his 这类同名不同格被扁平表合并）----
        private static readonly Dictionary<string, string> SubjectMap = new Dictionary<string, string>
        {
            { "he", "他" }, { "she", "她" }, { "it", "它" },
            { "they", "他们" }, { "xe", "杝" }, { "ey", "牠" }, { "sie", "牠" }, { "ze", "牠" },
            { "you", "你" },
        };

        private static readonly Dictionary<string, string> ObjectMap = new Dictionary<string, string>
        {
            { "him", "他" }, { "her", "她" }, { "it", "它" },
            { "them", "他们" }, { "xem", "杝" }, { "em", "牠" }, { "hir", "牠" }, { "zir", "牠" },
            { "you", "你" },
        };

        private static readonly Dictionary<string, string> PossessiveMap = new Dictionary<string, string>
        {
            { "his", "他的" }, { "her", "她的" }, { "hers", "她的" }, { "its", "它的" },
            { "their", "他们的" }, { "theirs", "他们的" },
            { "xyr", "杝的" }, { "xyrs", "杝的" },
            { "eir", "牠的" }, { "eirs", "牠的" }, { "hir", "牠的" }, { "hirs", "牠的" },
            { "zir", "牠的" }, { "zirs", "牠的" },
            { "your", "你的" }, { "yours", "你的" },
        };

        private static readonly Dictionary<string, string> ReflexiveMap = new Dictionary<string, string>
        {
            { "himself", "他自己" }, { "herself", "她自己" }, { "itself", "它自己" },
            { "themselves", "他们自己" }, { "themself", "ta自己" },
            { "xemself", "杝自己" }, { "emself", "牠自己" }, { "hirself", "牠自己" }, { "zirself", "牠自己" },
            { "yourself", "你自己" }, { "yourselves", "你们自己" },
        };

        /// <summary>供 ZhsEntityPropPatch 复用：把英文称谓译成中文。</summary>
        public static bool TryTranslateTerm(string lower, out string zh)
        {
            zh = null;
            if (string.IsNullOrEmpty(lower)) return false;
            return TermMap.TryGetValue(lower, out zh);
        }

        public static void TryApply()
        {
            MethodInfo postPronoun = typeof(ZhsPronounPatch).GetMethod("PostfixPronoun",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo postTerm = typeof(ZhsPronounPatch).GetMethod("PostfixTerm",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo postGrammar = typeof(ZhsPronounPatch).GetMethod("PostfixGrammar",
                BindingFlags.NonPublic | BindingFlags.Static);

            int nProvider = 0, nGrammar = 0, nGameObject = 0;
            Type provider = Find("XRL.World.BasePronounProvider");
            Type grammar = Find("XRL.Language.Grammar");
            Type goRepl = Find("XRL.World.Text.Delegates.GameObjectReplacers");

            // ---- ★ 主目标：GameObjectReplacers 的代词/称谓替换器 ----
            if (goRepl != null)
            {
                Harmony h3 = new Harmony("Nattens.QudZhsCookbook.gorepl");
                foreach (MethodInfo mi in goRepl.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                             | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    if (Array.IndexOf(GameObjectReplacerNames, mi.Name) < 0) continue;
                    if (mi.ContainsGenericParameters) continue;
                    try
                    {
                        bool isTerm = mi.Name.IndexOf("Term", StringComparison.Ordinal) >= 0;
                        h3.Patch(mi, postfix: new HarmonyMethod(isTerm ? postTerm : postPronoun));
                        nGameObject++;
                    }
                    catch (Exception e) { LogError("挂 GameObjectReplacers." + mi.Name + " 失败：" + e.Message); }
                }
            }

            if (provider != null)
            {
                Harmony h = new Harmony("Nattens.QudZhsCookbook.pronoun");
                foreach (MethodInfo mi in provider.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                              | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    bool isPronoun = Array.IndexOf(PronounGetters, mi.Name) >= 0;
                    bool isTerm = Array.IndexOf(TermGetters, mi.Name) >= 0;
                    if (!isPronoun && !isTerm) continue;
                    if (mi.IsAbstract || mi.ContainsGenericParameters) continue;
                    try
                    {
                        h.Patch(mi, postfix: new HarmonyMethod(isPronoun ? postPronoun : postTerm));
                        nProvider++;
                    }
                    catch (Exception e) { LogError("挂 " + mi.Name + " 失败：" + e.Message); }
                }
            }

            if (grammar != null)
            {
                Harmony h2 = new Harmony("Nattens.QudZhsCookbook.grammar");
                foreach (MethodInfo mi in grammar.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                             | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    if (Array.IndexOf(GrammarMethods, mi.Name) < 0) continue;
                    if (mi.ContainsGenericParameters) continue;
                    try { h2.Patch(mi, postfix: new HarmonyMethod(postGrammar)); nGrammar++; }
                    catch (Exception e) { LogError("挂 Grammar." + mi.Name + " 失败：" + e.Message); }
                }
            }

            UnityEngine.Debug.Log("[QudZhsCookbook] 代词/称谓接管 v3：GameObjectReplacers 挂 "
                + nGameObject + " 个（★主目标）；BasePronounProvider 挂 " + nProvider
                + " 个；Grammar 挂 " + nGrammar + " 个（goRepl=" + (goRepl != null)
                + " provider=" + (provider != null) + " grammar=" + (grammar != null) + "）");
        }

        private static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    List<Type> ok = new List<Type>();
                    foreach (Type t in ex.Types) if (t != null) ok.Add(t);
                    all = ok.ToArray();
                }
                catch { continue; }
                foreach (Type t in all) if (t.FullName == fullName) return t;
            }
            return null;
        }

        private static void LogError(string msg)
        {
            lock (Gate)
            {
                if (_errors++ < 20)
                    UnityEngine.Debug.Log("[QudZhsCookbook] 代词接管错误：" + msg);
            }
        }

        private static void Bump(ref int counter, string kind, string from, string to)
        {
            lock (Gate)
            {
                counter++;
                // 前 60 次逐条打印，并带上实例类型 —— 用来区分"哪一类 provider 走了这条路"
                if (counter <= 60)
                    UnityEngine.Debug.Log("[QudZhsCookbook] " + kind + " #" + counter
                        + "：" + from + " -> " + to + "  [" + _lastType + "]");
            }
        }

        /// <summary>
        /// 无条件记录：每次 Postfix 进入都 +1，并抽样记下"看到过的原始字符串"。
        /// 前 40 次逐条打印（含实例类型）；之后每 200 次打印一次汇总。
        /// 这样日志能明确区分两种情况：
        ///   有 "Postfix 进入 N 次" 但 N 很小甚至 0  → 挂钩没被调用（内联/没走到）
        ///   有 "Postfix 进入 N 次" 且样本里出现 She → 挂钩有效，是映射/判定问题
        /// </summary>
        private static void Note(string result)
        {
            lock (Gate)
            {
                _postfixEntries++;
                string key = result == null ? "<null>"
                    : (result.Length > 24 ? result.Substring(0, 24) + "…" : result);
                int n;
                RawSamples.TryGetValue(key, out n);
                RawSamples[key] = n + 1;

                if (_postfixEntries <= 40)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] Postfix 进入 #" + _postfixEntries
                        + "：原始=" + key + "  [" + _lastType + "]");
                }
                else if (_postfixEntries % 200 == 0)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] Postfix 累计进入 " + _postfixEntries
                        + " 次；不同原始值 " + RawSamples.Count + " 种");
                }
            }
        }

        /// <summary>把累计样本写到日志文件（退出前由 Bootstrap 调用；也可在游戏里靠汇总行观察）。</summary>
        public static void DumpSamples(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                string p = System.IO.Path.Combine(dir, "ZhsPronounSamples.txt");
                var sb = new System.Text.StringBuilder();
                lock (Gate)
                {
                    sb.AppendLine("Postfix 总进入次数: " + _postfixEntries);
                    sb.AppendLine("代词替换次数: " + _pronounCalls);
                    sb.AppendLine("称谓替换次数: " + _termCalls);
                    sb.AppendLine("Grammar 替换次数: " + _grammarCalls);
                    sb.AppendLine("错误次数: " + _errors);
                    sb.AppendLine();
                    sb.AppendLine("=== 见到过的原始返回值（按出现次数降序，最多 200 种）===");
                    foreach (var kv in SortedSamples(200))
                        sb.AppendLine(kv.Value.ToString().PadLeft(6) + "  " + kv.Key);
                }
                System.IO.File.WriteAllText(p, sb.ToString(), new System.Text.UTF8Encoding(false));
                UnityEngine.Debug.Log("[QudZhsCookbook] 代词样本已写出 -> " + p);
            }
            catch (Exception e) { UnityEngine.Debug.Log("[QudZhsCookbook] 写样本失败：" + e.Message); }
        }

        private static List<KeyValuePair<string, int>> SortedSamples(int max)
        {
            var list = new List<KeyValuePair<string, int>>(RawSamples);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        private static string _lastType = "?";

        private static void PostfixPronoun(ref string __result, object __instance)
        {
            try
            {
                // 先定实例类型，再无条件计数（这样样本能带上"是谁在取值"）
                _lastType = __instance != null ? __instance.GetType().Name : "null";
                // ★ 无条件计数（在任何过滤之前）：证明这个 Postfix 到底有没有被执行
                Note(__result);
                if (string.IsNullOrEmpty(__result)) return;
                if (Markup.HasCjk(__result)) return;
                if (__result.Length > 20) return;

                string key = __result.Trim().ToLowerInvariant();
                string zh = null;
                if (key == "they" || key == "them" || key == "their"
                    || key == "theirs" || key == "themselves" || key == "themself")
                {
                    // 只有**确实**标了 PseudoPlural 才用 ta；否则按真复数「他们」
                    if (IsPseudoPlural(__instance))
                    {
                        PseudoPluralMap.TryGetValue(key, out zh);
                        _lastType += "+Pseudo";
                    }
                    if (zh == null) PronounMap.TryGetValue(key, out zh);
                }
                else PronounMap.TryGetValue(key, out zh);

                if (zh != null)
                {
                    Bump(ref _pronounCalls, "代词", __result, zh);
                    __result = zh;
                }
            }
            catch (Exception e) { LogError("PostfixPronoun: " + e.Message); }
        }

        private static void PostfixTerm(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (Markup.HasCjk(__result)) return;
                if (__result.Length > 20) return;
                string key = __result.Trim().ToLowerInvariant();
                string zh;
                if (TermMap.TryGetValue(key, out zh))
                {
                    Bump(ref _termCalls, "称谓", __result, zh);
                    __result = zh;
                }
            }
            catch (Exception e) { LogError("PostfixTerm: " + e.Message); }
        }

        private static void PostfixGrammar(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (Markup.HasCjk(__result)) return;
                if (__result.Length > 20) return;
                string key = __result.Trim().ToLowerInvariant();
                string zh;
                if (PronounMap.TryGetValue(key, out zh))
                {
                    Bump(ref _grammarCalls, "Grammar代词", __result, zh);
                    __result = zh;
                }
            }
            catch (Exception e) { LogError("PostfixGrammar: " + e.Message); }
        }

        private static bool IsPseudoPlural(object instance)
        {
            if (instance == null) return false;
            try
            {
                PropertyInfo pi = instance.GetType().GetProperty("PseudoPlural",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi == null || pi.PropertyType != typeof(bool)) return false;
                return (bool)pi.GetValue(instance, null);
            }
            catch { return false; }
        }
    }
}
