// 烹饪文本的中文语序接管（v1）。
//
// ══════════════════════════════════════════════════════════════════════════
// 逆向结论（来自 Assembly-CSharp.dll 的 IL，工具：_tools/ildex）
// ══════════════════════════════════════════════════════════════════════════
//
//   ① Campfire.CookFromIngredients()            3 处调用点
//        └─► Campfire.DescribeMeal(ingredients)          ★ 唯一出口
//               ├─ _T("Campfire DescribeMeal",
//               │      "=spice:cooking.cookTemplate.!random=")   取模板（4 个）
//               ├─ SetArgument(0, RollIngredients(...))          合成假食材
//               └─ Object.ToString()                             触发全链替换
//        └─► Popup.Show(描述, …)                       结果进弹窗/消息日志
//
//   ② RollIngredients 用**英语语法**造合成假食材名：
//        HistoricVariables["creaturePossessive"]
//            = Grammar.MakePossessive(creature.DisplayNameOnlyStripped)
//                                      ↑ 产出 "X's"
//        HistoricVariables["creatureBodyPart"]
//            = BodyPart.GetOrdinalName()
//              └─ Grammar.Ordinal(pos) + " " + partName      ↑ 产出 "first leg"
//
//   ③ 食材列表由 ReplaceBuilder 的 andList/commaList 连接（英语 ", " / " and "）。
//
//   ④ 液体食材走 _T("Cooking .cookingIngredientName - liquid",
//                   "a dram of =item.liquid.name=") —— 量词 dram。
//
//   ⑤ 菜名框架是 _T("CookingRecipe GetDisplayName", "=chefName|'s= =displayName=")。
//
// ══════════════════════════════════════════════════════════════════════════
// 本文件做的四件事
// ══════════════════════════════════════════════════════════════════════════
//   A. Grammar.MakePossessive  → 中文词后不再追加 "'s"
//      （中文用「的」靠词序，加了 "'s" 一定是错的）
//   B. Grammar.Ordinal         → "first" 转「第一」（供合成假食材的部件名用）
//   C. 一条总清理 ReplaceResult：兜底剔掉中文里残留的 "'s"
//   D. Campfire.DescribeMeal Postfix：模板改序 + 重写英语连接符
//
// 设计原则：**幂等**（已处理过就原样返回）、**只读不改游戏对象**、
//           全部走反射挂载（类型/方法名若在新版本变化只会日志提示，不会编译失败）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsCookingPatch
    {
        private const string Tag = "[QudZhsCookbook] 烹饪接管：";

        private static bool _applied;

        // ────────────────────────────────────────────────────────────────
        // 挂载
        // ────────────────────────────────────────────────────────────────
        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            int n = 0;

            // A / B：Grammar 上的两个语法出口
            Type grammar = Find("XRL.Language.Grammar");
            if (grammar != null)
            {
                n += Patch(grammar, "MakePossessive", "PostfixPossessive");
                n += Patch(grammar, "Ordinal", "PostfixOrdinal");
            }
            else UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Grammar");

            // D：餐食描述的唯一出口
            Type campfire = Find("XRL.World.Parts.Campfire");
            if (campfire != null)
            {
                n += Patch(campfire, "DescribeMeal", "PostfixDescribeMeal");
            }
            else UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Parts.Campfire");

            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个补丁");
        }

        private static int Patch(Type t, string method, string postfixName)
        {
            int applied = 0;
            MethodInfo postfix = typeof(ZhsCookingPatch).GetMethod(
                postfixName, BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null) return 0;

            Harmony h = new Harmony("Nattens.QudZhsCookbook.cooking." + t.Name + "." + method);
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static | BindingFlags.Instance))
            {
                if (mi.Name != method) continue;
                if (mi.ContainsGenericParameters) continue;
                try
                {
                    h.Patch(mi, postfix: new HarmonyMethod(postfix));
                    applied++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(Tag + "挂 " + t.Name + "." + method + " 失败：" + e.Message);
                }
            }
            return applied;
        }

        private static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        // ────────────────────────────────────────────────────────────────
        // A. 所有格：中文后面不要 "'s"
        // ────────────────────────────────────────────────────────────────
        private static void PostfixPossessive(ref string __result)
        {
            try { __result = CleanPossessive(__result); }
            catch (Exception e) { UnityEngine.Debug.LogError(Tag + "MakePossessive 异常：" + e.Message); }
        }

        /// <summary>剥掉紧跟在汉字/中文标点后面的 "'s" / "’s" / "'"。</summary>
        internal static string CleanPossessive(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('\'') < 0 && s.IndexOf('\u2019') < 0) return s;
            // 中文（含全角标点）后面紧跟的英语所有格标记 → 删除
            string r = Regex.Replace(s, @"(?<=[\u3000-\u303f\u3400-\u4dbf\u4e00-\u9fff"
                                        + @"\uf900-\ufaff\uff00-\uffef])['\u2019]s\b", "");
            r = Regex.Replace(r, @"(?<=[\u3000-\u303f\u3400-\u4dbf\u4e00-\u9fff"
                                + @"\uf900-\ufaff\uff00-\uffef])['\u2019]", "");
            return r;
        }

        // ────────────────────────────────────────────────────────────────
        // B. 序数词：first → 第一（供「第N条腿」这类合成部件名使用）
        // ────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> Ordinals =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "first", "\u7b2c\u4e00" },      // 第一
            { "second", "\u7b2c\u4e8c" },     // 第二
            { "third", "\u7b2c\u4e09" },      // 第三
            { "fourth", "\u7b2c\u56db" },     // 第四
            { "fifth", "\u7b2c\u4e94" },      // 第五
            { "sixth", "\u7b2c\u516d" },      // 第六
            { "seventh", "\u7b2c\u4e03" },    // 第七
            { "eighth", "\u7b2c\u516b" },     // 第八
            { "ninth", "\u7b2c\u4e5d" },      // 第九
            { "tenth", "\u7b2c\u5341" },      // 第十
            { "eleventh", "\u7b2c\u5341\u4e00" },
            { "twelfth", "\u7b2c\u5341\u4e8c" },
        };

        private static void PostfixOrdinal(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                string zh;
                if (Ordinals.TryGetValue(__result.Trim(), out zh)) __result = zh;
            }
            catch (Exception e) { UnityEngine.Debug.LogError(Tag + "Ordinal 异常：" + e.Message); }
        }

        // ────────────────────────────────────────────────────────────────
        // C. 总清理：英文列表连接符 → 中文
        // ────────────────────────────────────────────────────────────────
        internal static string CleanConnectives(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // "A, B, and C" / "A, B and C" → "A、B与C"
            s = Regex.Replace(s, @",\s+and\s+", "\u4e0e");                    // ，and → 与
            s = Regex.Replace(s, @"\s+and\s+", "\u4e0e");                     // and → 与
            s = Regex.Replace(s, @"\s*,\s*", "\u3001");                       // , → 、
            // 兜底：残留所有格
            s = CleanPossessive(s);
            return s;
        }

        // ────────────────────────────────────────────────────────────────
        // D. 餐食描述：模板改序
        // ────────────────────────────────────────────────────────────────
        private static void PostfixDescribeMeal(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                __result = RearrangeMeal(__result);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "DescribeMeal 异常：" + e.Message);
            }
        }

        /// <summary>
        /// 把 `DescribeMeal` 产出的整句改成中文语序。
        ///
        /// 中文模板（见 HistorySpice.jsonc 的 cooking.cookTemplate）已经写成：
        ///     你把{食材}扔进锅里，搅了搅。
        ///     你为这顿饭收集了手边能找到的一切：{食材}。
        /// 所以这里只需修**拼接残留**：
        ///   1. 英语列表连接符 ", " / " and " → "、" / "与"
        ///   2. 残留的 "'s"
        ///   3. "。\n\n你把" 这类模板边界 → 中文标点更顺（保留段落，但去掉重复动词）
        /// </summary>
        internal static string RearrangeMeal(string s)
        {
            s = CleanConnectives(s);

            // 模板②③④ 形如「…：{食材}。\n\n你把它们扔进锅里，搅了搅。」
            // 中文里「它们」指代前句清单，通顺；但模板① 是单句，不涉及。
            // 这里只做一件事：如果出现连续两个「你把」，删掉后一个的主语「你」。
            s = Regex.Replace(s, @"\u4f60\u628a(?=[^\n]*\n\n\u4f60\u628a)", "\u4f60\u628a");
            s = s.Replace("\n\n\u4f60\u628a", "\n\n\u628a");

            return s;
        }
    }
}
