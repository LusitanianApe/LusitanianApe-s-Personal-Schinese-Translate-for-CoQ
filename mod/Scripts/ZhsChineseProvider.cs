// ═══════════════════════════════════════════════════════════════════════════
//  ZhsChineseProvider —— 中文语言 Provider（走**官方框架**）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么换这条路】
//   我们此前全靠 Harmony 从**外面**劫。但反编译发现官方**本来就留了口子**：
//       XRL.Language.Translator.Provider            （public abstract class，31 个可覆写点）
//       XRL.Language.Translator.SetProvider(…)      （★ public static）
//       XRL.Language.TranslatorBase                 （完整英文实现）
//       [LanguageProvider("en")] TranslatorEnglish : TranslatorBase { }   ← ★ **空壳**
//   ⇒ 只要 `class X : TranslatorBase` 覆写想改的方法，再 `SetProvider(new X())` 即可。
//
// 【★★ 这条路还顺手解决了一个老坑（见 ZhsArticleTransPatch.cs L19-25）】
//   当年挂 `TranslatorBase.AddArticle` 失败：
//       System.BadImageFormatException: Cannot box IsByRefLike type '.AddArticleParams'
//   —— `AddArticleParams` / `MakeAndListParams` 等是 **ref struct**，Harmony 的
//      `object[] __args` **无法装箱** ⇒ 补丁等于不存在。
//   **继承 + SetProvider 是直接调用，不经装箱** ⇒ 这类方法从此可改 ✓
//
// 【官方加载链（实证）】
//   LanguageLoader.LoadLanguages()          [ModSensitiveCacheInit]
//     ├─ foreach (xml in DataManager.YieldXMLStreamsWithRoot("languages"))   ← 我方 Languages.xml
//     ├─ Translator.SetProvider(null)                                        ← ★ 会清空
//     ├─ foreach (t in ModManager.ActiveTypes)                               ← 遍历模组程序集
//     │     if (t.GetCustomAttribute<LanguageProviderAttribute>()?.Lang == ActiveLanguage)
//     │         Translator.SetProvider(new t())                              ← 语言码匹配才注册
//     └─ The.FontManager?.UpdateFontFallbacks()
//
//   ⚠️ 问题：`ActiveLanguage` 来自**玩家选项**（`Options.GetOption("Language", "en")`）
//       ⇒ 玩家没切成 `zh-Hans` ⇒ 上面的循环**不会**选中我们 ⇒ Provider 静默不加载 ✗
//   ✅ 对策：本文件的 `ZhsChineseProviderPatch` 在 `LoadLanguages` **之后**再断言一次，
//       **不依赖玩家设置** ⇒ Provider 永远生效。
//
// ═══════════════════════════════════════════════════════════════════════════
// ═══════════════════════════════════════════════════════════════════════════
//  ★★★ 2026-10-15 订正：三个 `ReadOnlySpan<char>` 覆写**做得到**（旧「定论」已推翻）
// ═══════════════════════════════════════════════════════════════════════════
//  旧结论「做不到」的成因：当年 `_tools\spanprobe` 的引用集**没带 mscorlib**
//  ⇒ 编译器只剩 `0Harmony.dll` 里那个 **internal** 的 `ReadOnlySpan` ⇒ `CS0122`。
//  ★ 本轮实测（`_tools\roslyntest`，用**游戏自带的 Roslyn 3.11** + 游戏 `Managed\` 全量引用）：
//      全量 188 引用（含 0Harmony）⇒ 错误 0 ✅ · 产物里 ReadOnlySpan ← **mscorlib**（公开版）
//      最小集 4 引用（含 0Harmony）⇒ 错误 0 ✅ · 同上
//      全量 − mscorlib             ⇒ 52 错 ⛔（缺基础类型）
//  ★★ 且存在**编译器版本差异**（同一份代码、同一引用集）：
//      游戏 Roslyn **3.11** ⇒ 通过（选 mscorlib 的公开版）
//      SDK   Roslyn **4.x** ⇒ `CS0122`（选 0Harmony 的 internal 版）
//      ⇒ 游戏用的就是 3.11（`Managed\Microsoft.CodeAnalysis.dll` 即它）⇒ **游戏里能编过**；
//        本地 `_compilecheck`（net8.0 + SDK Roslyn）因此**排除**这组覆写所在文件，
//        改由 `_tools\roslyntest` 用**游戏的 Roslyn 编译真实 Scripts** 来把关。
//  ⇒ 三个覆写写在 `ZhsProviderOverrides.cs`（折行 / 字宽 / 段间空格），本文件只管**注册**。
//
//  ⚠ 仍成立的两条旧事实（保留备查）：
//    · `object[] __args` **无法装箱 ref struct** ⇒ 这类方法**不能用 Harmony 挂**
//      （`AddArticleParams` 曾报 `BadImageFormatException: Cannot box IsByRefLike type`）
//      ⇒ **继承 + SetProvider 是唯一路径**，现在这条路通了。
//    · `ModManager.GetCompilerService()` 的引用集 = 「当时已加载的、满足谓词的程序集」。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using HarmonyLib;
using XRL.Language;

namespace Nattens.QudZhsCookbook
{
    /// <summary>
    /// 中文语言 Provider。**只覆写想改的**，其余 30 个方法走 `TranslatorBase` 的英文实现。
    ///
    /// ⚠ 本版**故意不覆写任何成员** —— 见文件头【定论】：
    ///   三个最高价值的覆写被 `0Harmony` 里那个 internal 的 `ReadOnlySpan` 挡死，
    ///   连**游戏自己**都编不过。当前形态（只注册、不改行为）是**已被编译验证过**的安全形态。
    /// </summary>
    [LanguageProvider("zh-Hans")]
    public partial class ZhsChineseProvider : TranslatorBase
    {
        internal static bool Disabled;

        /// <summary>CJK 判定（范围取自游戏自己的 `GetGraphemeWidth`，实证）。</summary>
        internal static bool IsCjk(char c)
        {
            return (c >= '\u1100' && c <= '\u11FF')     // 谚文字母（游戏原范围）
                || (c >= '\u2E80' && c <= '\u9FFF')     // CJK 部首 + 统一汉字（游戏原范围）
                || (c >= '\uA960' && c <= '\uA97F')     // 谚文字母扩展-A（游戏原范围）
                || (c >= '\uFF01' && c <= '\uFF60')     // 全角形式（游戏原范围）
                || (c >= '\u3000' && c <= '\u303F')     // CJK 标点 · 顿号/句号/书名号等
                || (c >= '\u3040' && c <= '\u30FF')     // 平假名 / 片假名
                || (c >= '\uAC00' && c <= '\uD7A3');    // 谚文音节
        }
    }

    /// <summary>
    /// ★ 强制注册：`LanguageLoader.LoadLanguages()` 之后断言 Provider 是我们。
    ///
    /// 为什么需要：官方的注册条件是 `attr.Lang == ActiveLanguage`，而 `ActiveLanguage`
    /// 来自**玩家选项**（默认 `"en"`）⇒ 玩家没切语言 ⇒ 我们的 Provider **静默不加载** ✗
    /// 这里在它**之后**再设一次 ⇒ **不依赖玩家设置**。
    ///
    /// ⚠️ 挂的是 `LoadLanguages`（无参、返回 void）—— 参数都是普通类型，
    ///    没有 `ref struct`/`Span` ⇒ 不会重演 `AddArticleParams` 那次 BadImageFormatException。
    /// </summary>
    internal static class ZhsChineseProviderPatch
    {
        private const string Tag = "[QudZhsCookbook] 中文Provider：";
        private static bool _applied;
        private static bool _announced;

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            // ① 立刻设一次（若 LoadLanguages 已经跑过，这一步就够）
            ForceSet("引导时");

            // ② 再挂 Postfix，保证 LoadLanguages 每次跑完（含 ModSensitiveCacheInit 重跑）都是我们的
            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(LanguageLoader), "LoadLanguages", Type.EmptyTypes, null);
                if (target == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 LanguageLoader.LoadLanguages ⇒ 只做了一次性设置");
                    return;
                }
                Harmony h = new Harmony("Nattens.QudZhsCookbook.zhprov");
                h.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ZhsChineseProviderPatch), nameof(Postfix))));
                UnityEngine.Debug.Log(Tag + "已挂 LanguageLoader.LoadLanguages 的 Postfix（强制注册）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e.Message);
            }
        }

        private static void Postfix()
        {
            ForceSet("LoadLanguages 之后");
        }

        private static void ForceSet(string from)
        {
            try
            {
                // 开关：模组目录放 `_zhs_zhprovider_off.txt` 即不强制（回落官方的语言码匹配）
                string dir = ZhsTrace.ModDirectory;
                if (!string.IsNullOrEmpty(dir)
                    && System.IO.File.Exists(System.IO.Path.Combine(dir, "_zhs_zhprovider_off.txt")))
                {
                    if (!_announced)
                    {
                        _announced = true;
                        UnityEngine.Debug.Log(Tag + "开关文件存在 ⇒ 不强制注册（走官方语言码匹配）");
                    }
                    return;
                }

                Translator.SetProvider(new ZhsChineseProvider());
                if (!_announced)
                {
                    _announced = true;
                    UnityEngine.Debug.Log(Tag + "★ 已强制注册 ZhsChineseProvider（" + from
                        + "）· ActiveLanguage=" + LanguageLoader.ActiveLanguage);
                }
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "强制注册异常：" + e.Message); } catch { }
            }
        }
    }
}
