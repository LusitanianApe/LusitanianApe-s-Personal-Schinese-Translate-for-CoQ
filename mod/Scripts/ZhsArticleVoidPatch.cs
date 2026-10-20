// ═══════════════════════════════════════════════════════════════════════════
// 冠词杀手 v2 —— 接管 `Grammar.A` 的 **void / StringBuilder** 重载（用户要求「把所有冠词杀掉」）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么需要本补丁（2.15.70）】
//   `ZhsArticlePatch` 只枚举**返回 string** 的 `Grammar.A(...)` 重载：
//       foreach (MethodInfo m in …) { if (m.ReturnType != typeof(string)) continue; … }
//   但真正被 `PostProcessors.Article` 调用的是 **void** 重载：
//       // XRL.World.Text.Delegates.PostProcessors::Article
//       public static void Article(VariableContext Context) {
//           string word = Context.Value.ToString();
//           Context.Value.Clear();
//           Grammar.A(word, Context.Value, Context.Capitalize);   // ← 就是它
//       }
//   ⇒ 那个重载**从来没被接管过** ⇒ 中文也照样被硬加英文冠词。
//
// 【反编译实证 `Grammar.A(string, TextBuilder, bool)`】
//       public static void A(string Word, TextBuilder Result, bool Capitalize = false) {
//           if (Word[0] == '=')                                  // ★ Word 为空 ⇒ IndexOutOfRange（崩溃）
//               Result.Append("=article=").Append(Word);          // ★ 产出又含 `=article=` ⇒ 自我循环
//           else
//               Result.Append(IndefiniteArticleShouldBeAn(Word) ? "An " : "A ").Append(Word);
//       }
//   ⇒ 两个症状同一根因：**中文没有冠词，但这个槽硬要造一个**。
//     实测漏出：`其形制令人想见a 莹润玛瑙` / `=item|article=`。
//
// 【做法】给三个重载各挂 Prefix：
//     · `A(string, TextBuilder, bool)`   —— `PostProcessors.Article` 走这条
//     · `A(string, StringBuilder, bool)` —— 另一条
//     · `A(string, bool)`（返回 string）  —— `ZhsArticlePatch` 已管，但这里补「空串」防崩
//   判据：`Word` 含 CJK ⇒ **不加任何冠词，直接落词**；`Word` 为空 ⇒ **不落词**（防越界）。
//
// 【安全】
//   * 只对**含 CJK** 的 Word 生效；纯英文走原逻辑（英文冠词照旧正确）。
//   * 全部 try/catch；`Prefix` 返回 false 只表示「已代跑」，异常一律 return true 交还原方法。
//   * 开关文件：`_zhs_article_off.txt`（沿用原开关）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsArticleVoid
    {
        private const string Tag = "[QudZhsCookbook] 冠词杀手：";
        internal static int Hits;

        /// <summary>是否含 CJK（中文/日文汉字与中文标点）。</summary>
        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u4e00' && c <= '\u9fff')      // CJK 基本区
                    || (c >= '\u3400' && c <= '\u4dbf')    // 扩展 A
                    || (c >= '\u3000' && c <= '\u303f')    // 中文标点
                    || (c >= '\uff00' && c <= '\uffef'))   // 全角
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Prefix 主体：`Word` 含 CJK 或为空时，自己写 `Result` 并跳过原方法。
        /// </summary>
        internal static bool PrefixImpl(string Word, Action<string> emit)
        {
            try
            {
                if (string.IsNullOrEmpty(Word))
                {
                    return false;          // ★ 空串 ⇒ 什么都不写（原方法会 Word[0] 越界崩溃）
                }
                if (!HasCjk(Word))
                {
                    return true;           // 纯英文 ⇒ 交还原方法（英文冠词正确）
                }
                emit(Word);                // 中文 ⇒ 只落词，不加冠词
                Hits++;
                if (Hits <= 5)
                {
                    UnityEngine.Debug.Log(Tag + "去冠词：" + Word);
                }
                return false;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Prefix 异常（交还原方法）：" + e.Message);
                return true;
            }
        }

        // ── 三个重载各自的 Prefix ──
        //   ⚠ `TextBuilder` **不能直接引用**（它会拉进 `ReadOnlySpan<>`，
        //     与 CompileCheck 的引用集冲突 ⇒ `CS7069`）⇒ 一律走反射调 `Append`。
        private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo>
            _appendCache = new System.Collections.Generic.Dictionary<Type, MethodInfo>();

        private static MethodInfo AppendOf(object sink)
        {
            if (sink == null) return null;
            Type t = sink.GetType();
            MethodInfo mi;
            if (_appendCache.TryGetValue(t, out mi)) return mi;
            try
            {
                mi = t.GetMethod("Append",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { mi = null; }
            _appendCache[t] = mi;
            return mi;
        }

        private static bool PrefixTB(string __0, object __1)
        {
            if (ZhsArticleFix.Disabled) return true;
            if (__1 == null) return true;
            MethodInfo ap = AppendOf(__1);
            if (ap == null) return true;                 // 拿不到 Append ⇒ 交还原方法
            return PrefixImpl(__0, w =>
            {
                try { ap.Invoke(__1, new object[] { w }); } catch { }
            });
        }

        private static bool PrefixSB(string __0, object __1)
        {
            if (ZhsArticleFix.Disabled) return true;
            var sb = __1 as StringBuilder;
            if (sb == null) return true;
            return PrefixImpl(__0, w => sb.Append(w));
        }

        private static bool PrefixStr(string __0, ref string __result)
        {
            if (ZhsArticleFix.Disabled) return true;
            if (string.IsNullOrEmpty(__0)) { __result = ""; return false; }
            if (!HasCjk(__0)) return true;
            __result = __0;
            Hits++;
            return false;
        }
    }

    /// <summary>把上面三个 Prefix 挂到 `Grammar` 的对应重载上。</summary>
    internal static class ZhsArticleVoidPatch
    {
        private const string Tag = "[QudZhsCookbook] 冠词杀手：";

        internal static void TryApply()
        {
            if (ZhsArticleFix.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type g = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { g = asm.GetType("XRL.Language.Grammar", false); }
                    catch { g = null; }
                    if (g != null) break;
                }
                if (g == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Grammar");
                    return;
                }

                MethodInfo pTB = typeof(ZhsArticleVoid).GetMethod("PrefixTB",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo pSB = typeof(ZhsArticleVoid).GetMethod("PrefixSB",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo pStr = typeof(ZhsArticleVoid).GetMethod("PrefixStr",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Harmony h = new Harmony("Nattens.QudZhsCookbook.articlevoid");
                int n = 0;
                foreach (MethodInfo m in g.GetMethods(BindingFlags.Public
                             | BindingFlags.Static))
                {
                    if (m.Name != "A") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2 || ps[0].ParameterType != typeof(string)) continue;
                    Type p1 = ps[1].ParameterType;
                    MethodInfo pre = null;
                    if (p1.Name == "TextBuilder") pre = pTB;
                    else if (p1 == typeof(StringBuilder)) pre = pSB;
                    if (pre == null) continue;
                    try
                    {
                        h.Patch(m, prefix: new HarmonyMethod(pre));
                        n++;
                        UnityEngine.Debug.Log(Tag + "已挂 Grammar.A(" + ps[0].ParameterType.Name
                            + ", " + p1.Name + ")");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log(Tag + "跳过 " + p1.Name + "：" + e.Message);
                    }
                }
                // 返回 string 的单参重载（`ZhsArticlePatch` 也管它，这里补「空串不崩」）
                try
                {
                    MethodInfo one = g.GetMethod("A",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(string), typeof(bool) }, null);
                    if (one != null && one.ReturnType == typeof(string) && pStr != null)
                    {
                        h.Patch(one, prefix: new HarmonyMethod(pStr));
                        n++;
                        UnityEngine.Debug.Log(Tag + "已挂 Grammar.A(string, bool):string");
                    }
                }
                catch { }

                UnityEngine.Debug.Log(Tag + "共挂 " + n + " 个出口（中文不加冠词 · 空串防崩）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }
    }
}
