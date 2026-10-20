// ═══════════════════════════════════════════════════════════════════════════
// 物品显示名（adjunct noun / 冠词 / 方向）的中文语序接管
// ═══════════════════════════════════════════════════════════════════════════
//
// 【截图实证的三类缺陷】
//   ① `一份水洼 of 藻类的 brackish 水`
//        根因：`GameObject conj` 模板
//              EN = '=adjunctNoun= of =name='
//              ZH = '=name=的=adjunctNoun='        ← 我们译了，但游戏用了英文那个
//   ② `该 villagers of Terchemas`
//        根因：中文冠词（`the`→「该」）被拼到**英文名词组**前面
//   ③ `brackish` 未译
//        根因：它来自 `ObjectBlueprints` 的 `AltAdjectiveWhenPurityBelow Alt="{{w|brackish}}"`
//              —— **不经字符串表**，直接当参数传给显示名生成器
//              ⇒ 只能做参数级映射
//
// 【做法】挂 `XRL.World.Text.Delegates.GameObjectReplacers` 的
//   `Adj Noun` / `WithIndefiniteArticle` / `WithDefiniteArticle` 出口，
//   对结果做**针对性的中文清理**：
//     * 把剩余的英文参数（brackish 等）映射为中文
//     * 把「中文冠词 + 英文字母开头」的组合去掉冠词（中文不需要冠词）
//
//   安全：只在「结果里混有英文」时动手；纯中文原样放行；异常全吞。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsAdjunctPatch
    {
        private const string Tag = "[QudZhsCookbook] 物品名接管：";

        private static bool _applied;
        private static int _calls, _fixed;

        /// <summary>
        /// 参数级英文词 → 中文。
        /// 这些词来自 ObjectBlueprints / Liquids.xml 的**属性值**，
        /// 不经字符串表，所以静态汉化覆盖不到。
        /// </summary>
        private static readonly Dictionary<string, string> ParamWords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "brackish",   "咸涩的" },   // 咸涩的
            { "salty",      "咸的" },         // 咸的
            { "salt-encrusted", "结盐的" }, // 结盐的
            { "slimy",      "黏滑的" },   // 黏滑的
            { "algiferous", "富藻的" },   // 富藻的
            { "algae",      "藻" },               // 藻
            { "pure",       "纯净的" },   // 纯净的
            { "dirty",      "污浊的" },   // 污浊的
            { "fresh",      "淡的" },         // 淡的
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            // ★★ 2026-10-17：**阵营名**也要清「该」（用户截图 `你与该 Maralil的村民的声望下降了…`）
            //   根本表：`Context="VillageBase CreateVillageFaction faction display name"`
            //           ID="villagers of =spice.entity:name=" ⇒ 中文 `=spice.entity:name=的村民` ✅
            //   但**冠词 the 被译成「该」**（别处 Context 的 ID="the" → 「该」），而本补丁此前
            //   **只挂了 `GameObjectReplacers`** 的显示名出口 ⇒ 阵营名走 `FactionReplacers`，
            //   完全没被清理 ⇒ 产出 `该 Maralil的村民`。
            //   ⇒ 把同一套 Postfix 也挂到 `FactionReplacers` 的**返回 string** 的取名入口上。
            string[] replacerTypes =
            {
                "XRL.World.Text.Delegates.GameObjectReplacers",
                "XRL.World.Text.Delegates.FactionReplacers",
            };
            int typesPatched = 0;

            MethodInfo post = typeof(ZhsAdjunctPatch).GetMethod("Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (post == null) return;

            int n = 0;
            Harmony h = new Harmony("Nattens.QudZhsCookbook.adjunct");
            foreach (string typeName in replacerTypes)
            {
                Type tr = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        tr = asm.GetType(typeName, false);
                        if (tr != null) break;
                    }
                    catch { }
                }
                if (tr == null) continue;
                typesPatched++;
                foreach (MethodInfo mi in tr.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    string nm = mi.Name;
                    bool want = nm == "AdjNoun" || nm == "AdjunctNoun"
                             || nm == "WithIndefiniteArticle" || nm == "WithDefiniteArticle"
                             || nm == "Name" || nm == "ShortName"
                             // ★ 阵营侧：带/不带冠词的取名入口（`the villagers of X` 走这里）
                             || nm == "GetFactionName" || nm == "GetTheFactionName"
                             || nm == "MostHatedFactionName" || nm == "TheMostHatedFactionName"
                             || nm == "MostSecondHatedFactionName" || nm == "TheSecondMostHatedFactionName";
                    if (!want) continue;
                    if (mi.ContainsGenericParameters) continue;
                    try
                    {
                        h.Patch(mi, postfix: new HarmonyMethod(post));
                        n++;
                    }
                    catch { }
                }
            }
            if (typesPatched == 0)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 GameObjectReplacers / FactionReplacers");
                return;
            }
            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个显示名出口（replacer 类型 "
                + typesPatched + " 个）");
        }

        private static void Postfix(MethodBase __originalMethod, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                _calls++;
                ZhsHookTrace.Step("Adjunct.in", __result);
                string fixedUp = Clean(__result);
                if (fixedUp != __result)
                {
                    _fixed++;
                    if (_fixed <= 20)
                    {
                        UnityEngine.Debug.Log(Tag + (__originalMethod?.Name ?? "?")
                            + " 修正：" + __result + "  =>  " + fixedUp);
                    }
                    __result = fixedUp;
                }
            }
            catch { }
        }

        /// <summary>把结果里的英文参数词换成中文，并去掉中文前的冗余冠词。</summary>
        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // ① 逐词替换参数级英文（保留 {{标记|…}} 结构）
            foreach (var kv in ParamWords)
            {
                if (s.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // 词边界替换，避免把 "salt" 打进 "salt-encrusted"
                s = Regex.Replace(s, @"(?<![A-Za-z-])" + Regex.Escape(kv.Key) + @"(?![A-Za-z-])",
                                  kv.Value, RegexOptions.IgnoreCase);
            }

            // ② 中文冠词 + 英文字母开头 → 去掉冠词（中文无冠词）
            //    例：「该 villagers of Terchemas」→「villagers of Terchemas」
            //
            // ★★ 2.16.52 修正：原正则带**前置断言** `(?<=[…])`，
            //    要求「该」前面**必须**是汉字 ⇒ **行首的「该」永远匹配不到**。
            //    实测漏网（用户截图 · 声望面板）：`该 Alagawar的村民`
            //      —— 「该」在行首 ⇒ 前置断言失败 ⇒ 规则静默不生效。
            //    修法：放宽为「行首 **或** 汉字/中文标点之后」；
            //    后置断言 `(?=[A-Za-z])` 保留 ⇒ 只删「后面紧跟英文」的，不误伤正常中文。
            s = Regex.Replace(s,
                @"(?:(?<=[\u3000-\u303f\u4e00-\u9fff\uff00-\uffef])|^)"
                + @"(?:该|一个|一名|那个)\s*(?=[A-Za-z])", "");

            // ③ 「X of Y」的中文语序：X、Y 都是中文时改成「Y的X」
            //
            // ★ 踩坑记录：原正则写成 `^([^\s=]{1,20}) of ([^\s=]{1,30})$`
            //   —— `[^\s=]` 排除了空格，导致 **X 或 Y 含空格时匹配失败**。
            //   截图实证漏网：'你经过一份水洼 of咸涩的 血。'
            //                              └ X ┘   └─ Y ─┘（Y 含空格）
            //   ⇒ 改为允许内部空格（仍禁 `=`，避免吃掉占位符）。
            Match m = Regex.Match(s, @"^([^=]{1,24}?)\s+of\s*([^=]{1,40})$");
            if (m.Success && HasCjk(m.Groups[1].Value) && HasCjk(m.Groups[2].Value))
            {
                string x = m.Groups[1].Value.Trim();
                string y = m.Groups[2].Value.Trim();
                s = DropInnerSpaces(y) + "的" + DropInnerSpaces(x);
            }

            return s;
        }

        /// <summary>去掉中文串里英文式的词间空格（保留 {{标记|…}} 结构）。</summary>
        private static string DropInnerSpaces(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            // 只在「汉字 空格 汉字」时删空格
            return Regex.Replace(v, @"(?<=[\u3000-\u303f\u4e00-\u9fff])[ ]+(?=[\u3000-\u303f\u4e00-\u9fff])", "");
        }

        private static bool HasCjk(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if ((c >= '　' && c <= '〿') || (c >= '㐀' && c <= '䶿')
                    || (c >= '一' && c <= '鿿') || (c >= '豈' && c <= '﫿')
                    || (c >= '＀' && c <= '￯'))
                    return true;
            }
            return false;
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：调用 " + _calls + " 次，修正 " + _fixed + " 次");
        }
    }
}
