// 冠词汉化：把英文的 "a" / "an" / "the" 换成中文量词体系。
//
// 【为什么挂这里】
//   IL 显示 `GenderedNounReplacers::WithIndefiniteArticle` 的实现是
//       IL_001B: call XRL.Language.Grammar::A       ← 冠词由它生成
//   而显示名模板是
//       Context="GameObject.GetDisplayName prepend article-like (indefinite)"
//       ID="=article|spaceAfter==name="
//   所以从 `Grammar` 的 `A` / `IndefiniteArticle` 入手是**源头**，
//   比在最终文本里用正则猜要可靠得多。
//
// 【做法】
//   枚举 `Grammar` 中所有返回 string 的 `A(...)` / `IndefiniteArticle(...)` 重载，
//   给每个挂 Postfix：把结果里的英文冠词替换掉。
//     * 若方法参数里带了**名词**（能拿到中文名）→ 用 ZhsTextFilter 的量词表选量词
//     * 拿不到名词 → 用通用量词「个」
//   结果形如「一」/「一个」/「一把」。
//
// 【安全】
//   * 只改**结果恰好是冠词**（a / an / the，可带尾随空格）的场合，绝不碰其它返回值。
//   * 全部 try/catch；[HarmonyPrepare] 守卫；`_zhs_article_off.txt` 可停用。

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>
    /// 处理「带冠词的完整名字」："a table" / "an apple" / "the X"。
    /// 同时把**原始名字**（去掉冠词后的部分）记进量词学习表，
    /// 便于以后按完整名词而不是首字来选量词。
    /// </summary>
    internal static class ZhsNameArticle
    {
        // ★ 必须含 `some`：`WithIndefiniteArticle` 对不可数/复数名词输出 `some X`
        //   （官方注解 `[VariableExample("some bark armor:Some bark armor", …)]`）。
        //   原来只列 a|an|the ⇒ `some` 原样漏出，出现「你经过some 盐沼。」。
        private static readonly System.Text.RegularExpressions.Regex Head =
            new System.Text.RegularExpressions.Regex(@"^(a|an|the|some)\s+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static int _hits;

        public static string Apply(string s, object[] args)
        {
            if (ZhsArticleFix.Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                var m = Head.Match(s);
                if (!m.Success) return s;               // 不是「冠词+名字」就原样返回

                string article = m.Groups[1].Value;
                string rest = s.Substring(m.Length);    // 冠词后的名字（可能已是中文）

                // ★ `the`（定冠词）与 `some`（复数/不可数不定冠词）在中文里**都不加**
                if (article.Equals("the", StringComparison.OrdinalIgnoreCase)
                    || article.Equals("some", StringComparison.OrdinalIgnoreCase))
                    return rest;

                // 中文名 -> 查量词。
                // ★★ 2.16.95：三种返回值的处理（用户 2026-10-10 决定「不可数/集合名词清空数词」）
                //   · `null`（未收录）  ⇒ 回退「个」
                //   · `""`（收录=不加）  ⇒ **整个「一+量词」都不写**，直接给名词
                //       实证：`a watervine` ⇒ `水藤`（而不是「一份水藤」）
                //             `a metal workbench` ⇒ `金属工作台`
                //   · 非空               ⇒ 正常「一 + 量词 + 名词」
                string mw = ZhsTextFilter.MeasureFor(rest);
                if (mw == null) mw = "个";
                if (_hits < 8)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 冠词->量词：" + article + " " + rest
                        + " => " + (mw.Length == 0 ? "（不加量词）" : "一" + mw));
                    _hits++;
                }
                return mw.Length == 0 ? rest : "一" + mw + rest;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 名字冠词处理异常：" + e.Message);
                return s;
            }
        }
    }

    internal static class ZhsArticleFix
    {
        public static bool Disabled;
        private static int _hits;
        private static bool _logged;

        /// <summary>把 "a"/"an"/"the" 结果换成中文。</summary>
        public static string Apply(string s, object[] args)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                string t = s.Trim();
                string tail = s.Substring(t.Length);   // 保留原有尾随空格
                bool isA = t.Equals("a", StringComparison.OrdinalIgnoreCase)
                        || t.Equals("an", StringComparison.OrdinalIgnoreCase);
                // ★ `some` 与 `the` 同类：中文都不加冠词 ⇒ 整段删掉
                bool isThe = t.Equals("the", StringComparison.OrdinalIgnoreCase)
                        || t.Equals("some", StringComparison.OrdinalIgnoreCase);
                if (!isA && !isThe) return s;          // 只处理纯冠词

                // 从参数里找名词（可能是 GenderedNoun 或 string）
                string noun = null;
                if (args != null)
                {
                    foreach (object a in args)
                    {
                        if (a == null) continue;
                        if (a is string ss && ss.Length > 0) { noun = ss; break; }
                        // ★ 必须遍历基类链 —— `AccessTools.Field(type, name)` 只看该类型
                        //   自己声明的字段。（同一个坑在 `ZhsMutationPatch.FieldStr` 里
                        //   让所有 `Name` 读取返回 null，见那边的注释。）
                        object nm = null;
                        for (Type bt = a.GetType(); bt != null && nm == null; bt = bt.BaseType)
                        {
                            nm = AccessTools.Property(bt, "Name")?.GetValue(a, null)
                              ?? AccessTools.Field(bt, "Name")?.GetValue(a);
                        }
                        if (nm is string s2 && s2.Length > 0) { noun = s2; break; }
                    }
                }

                string zh;
                if (isThe)
                {
                    zh = "";                           // 中文没有定冠词 -> 直接删
                }
                else
                {
                    // ★★ 2.16.95：与 `ZhsArticlePatch.Apply` 同一条规则 ——
                    //   不可数 / 集合名词（`MeasureFor` 返回 `""`）**整个「一+量词」都不写**。
                    //   ⚠ 这里**不能**写成 `"一" + (mw ?? "个")`：
                    //     `mw` 为 `""` 时它会产出**孤零零一个「一」**
                    //     （=`一水藤`），比原来的「一份水藤」更坏。
                    string mw = noun != null ? ZhsTextFilter.MeasureFor(noun) : null;
                    if (mw == null) mw = "个";
                    zh = mw.Length == 0 ? "" : "一" + mw;
                }

                if (!_logged)
                {
                    _logged = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 冠词汉化生效：'" + s + "' -> '" + zh + tail
                        + "'（名词=" + (noun ?? "(未取到)") + "）");
                }
                _hits++;
                return zh + tail;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 冠词替换异常：" + e.Message);
                return s;
            }
        }
    }

    /// <summary>
    /// ★ 真正的冠词生产函数（实测 Grammar.A 从未被调用，说明它被内联或走了别的重载）：
    ///     GameObjectReplacers::WithIndefiniteArticle  -> "a table" / "an X"
    ///     GameObjectReplacers::WithDefiniteArticle    -> "the X"
    ///   直接给这两个挂 Postfix，产出后立刻替换冠词，并按**原始名字**选量词。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsNameArticlePatch
    {
        private static int _calls;
        private static bool _first;

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
                    if (m.Name == "WithIndefiniteArticle" || m.Name == "WithDefiniteArticle")
                        list.Add(m);
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

        [HarmonyPostfix]
        private static void Postfix(MethodBase __originalMethod, object[] __args, ref string __result)
        {
            try
            {
                if (!_first)
                {
                    _first = true;
                    var ps = __originalMethod.GetParameters();
                    var nm = new string[ps.Length];
                    for (int i = 0; i < ps.Length; i++) nm[i] = ps[i].ParameterType.Name;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 冠词挂钩(" + __originalMethod.Name
                        + ") 参数=[" + string.Join(",", nm) + "] 首返='" + __result + "'");
                }
                _calls++;
            }
            catch { }
            __result = ZhsNameArticle.Apply(__result, __args);
        }
    }

    [HarmonyPatch]
    internal static class ZhsArticlePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            try
            {
                foreach (MethodInfo m in typeof(XRL.Language.Grammar).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.ReturnType != typeof(string)) continue;
                    if (m.Name != "A" && m.Name != "IndefiniteArticle") continue;
                    list.Add(m);
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
        private static bool _targetLogged;

        [HarmonyPostfix]
        private static void Postfix(MethodBase __originalMethod, object[] __args, ref string __result)
        {
            // ★ 无条件诊断：先确认这些重载**是否真的被调用**
            try
            {
                if (!_targetLogged)
                {
                    _targetLogged = true;
                    var ps = __originalMethod.GetParameters();
                    var names = new string[ps.Length];
                    for (int i = 0; i < ps.Length; i++) names[i] = ps[i].ParameterType.Name;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 冠词挂钩命中方法："
                        + __originalMethod.Name + "(" + string.Join(",", names) + ") 首返='" + __result + "'");
                }
                _calls++;
                if (_calls <= 8)
                    UnityEngine.Debug.Log("[QudZhsCookbook] Grammar.A 被调用 #" + _calls
                        + " 结果='" + __result + "'");
            }
            catch { }
            __result = ZhsArticleFix.Apply(__result, __args);
        }
    }
}
