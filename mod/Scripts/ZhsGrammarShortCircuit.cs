// ═══════════════════════════════════════════════════════════════════════════
// 英语语法引擎的中文短路（性数格 / 变位）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】
//   截图实证：`The faundren villager 跳s跨过压实的泥土地面。`
//                                         ↑ 中文动词被加了英语第三人称单数 -s
//
//   游戏本体内置一套英文语法引擎（反编译 `XRL.Language.Grammar` 实证），
//   配置来自 `Base\Genders.xml` + `Base\PronounSets.xml`（无第三方插件）。
//   它按**英语规则**做词形变化，遇到中文词就会产出垃圾：
//
//       public static string ThirdPerson(string word, bool PrependSpace = false)   // 加 -s
//       public static string Pluralize(string word)                               // 加 -s/-es
//       public static string PastTenseOf(string verb)                             // 加 -ed
//       public static string SingularFromPlural(string plural)
//       public static string MakePossessive(string word)                          // 加 's
//       public static string MakeTitleCase(string Phrase)                         // 首字母大写
//       public static string IndefiniteArticle(string Word, ...) / A(...)         // 加 a/an
//       public static string Ordinal(...) / Cardinal(...) / Multiplicative(...)
//
// 【做法】给这些函数挂 **Prefix**：输入含 CJK 时**直接短路返回原文**。
//   Prefix 返回 false 即跳过原方法，`__result` 已被我们赋值。
//
//   * 输入是英文 → 原样走游戏逻辑（不影响英文路径 / AI / 存档）
//   * 输入含中文 → 不做任何英文词形变化
//
//   安全性：只在「参数含 CJK」时短路；异常全吞；可开关停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsGrammarShortCircuit
    {
        private const string Tag = "[QudZhsCookbook] 语法短路：";

        public static bool Disabled;          // 出现异常时可由外部置 true
        private static bool _applied;
        private static int _hits;

        /// <summary>需要短路的 Grammar 函数（都吃字符串、返回字符串）。</summary>
        private static readonly string[] Targets =
        {
            "ThirdPerson",          // 第三人称单数（加 -s）      ← 截图那个
            "Pluralize",            // 复数（加 -s/-es）
            "PastTenseOf",          // 过去式（加 -ed）
            "SingularFromPlural",
            "MakePossessive",       // 所有格（加 's）
            "MakeTitleCase",        // 首字母大写
            "MakeTitleCaseWithArticle",
            "IndefiniteArticle",    // 加 a/an
            "A",                    // 加 a/an
            "ConvertAtoAn",
            "InitCap",
            "InitCapWithFormatting",
            "InitLower",
            "GetWordRoot",
            "RemoveBadTitleEndingWords",
            "RemoveBadTitleStartingWords",
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
                    t = asm.GetType("XRL.Language.Grammar", false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Grammar");
                return;
            }

            MethodInfo pre = typeof(ZhsGrammarShortCircuit).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (pre == null) return;

            int n = 0;
            var done = new HashSet<string>();
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static))
            {
                if (Array.IndexOf(Targets, mi.Name) < 0) continue;
                if (mi.ReturnType != typeof(string)) continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 0) continue;
                if (ps[0].ParameterType != typeof(string)) continue;
                string key = mi.Name + "/" + ps.Length;
                if (!done.Add(key)) continue;
                try
                {
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.grammarsc." + key);
                    h.Patch(mi, prefix: new HarmonyMethod(pre));
                    n++;
                }
                catch { }
            }
            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个词形变化出口"
                + "（输入含中文时直接返回原文）");
        }

        /// <summary>
        /// Prefix：第一个参数含 CJK 时，直接把 `__result` 设为原文并返回 false（跳过原方法）。
        /// </summary>
        private static bool Prefix(MethodBase __originalMethod, object[] __args, ref string __result)
        {
            try
            {
                if (Disabled) return true;
                if (__args == null || __args.Length == 0) return true;
                string w = __args[0] as string;
                if (string.IsNullOrEmpty(w)) return true;

                // ── 中文后紧跟拉丁小写词尾 → 剥离（马尔科夫名字生成器的残留）──
                //   实证：`布线ocus` / `逻辑门ycus`
                if (HasCjk(w))
                {
                    string stripped = SuffixRx.Replace(w, "$1");
                    if (stripped != w)
                    {
                        _suffixHits++;
                        if (_suffixHits <= 20)
                            UnityEngine.Debug.Log(Tag + "剥离拉丁词尾：'" + w
                                + "'  =>  '" + stripped + "'");
                        w = stripped;
                        __result = w;
                        return false;
                    }
                }

                if (!HasCjk(w)) return true;

                // 含中文 → 不做英文词形变化
                __result = w;
                _hits++;
                if (_hits <= 30)
                {
                    UnityEngine.Debug.Log(Tag + (__originalMethod?.Name ?? "?")
                        + "('" + w + "') 短路 → 原文");
                }
                return false;
            }
            catch
            {
                return true;   // 出错就走原逻辑
            }
        }

        /// <summary>中文后紧跟的拉丁小写词尾（马尔科夫名字生成器残留）。</summary>
        private static readonly Regex SuffixRx = new Regex(
            @"([一-鿿])[a-z]{2,6}$", RegexOptions.Compiled);

        private static int _suffixHits;

        private static bool HasCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '　' && c <= '〿') || (c >= '㐀' && c <= '䶿')
                    || (c >= '一' && c <= '鿿') || (c >= '豈' && c <= '﫿')
                    || (c >= '＀' && c <= '￯'))
                    return true;
            }
            return false;
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：短路 " + _hits + " 次");
        }
    }
}
