// ═══════════════════════════════════════════════════════════════════════════
// 冠词杀手 v3 —— 补上**显示名**那条链（水池的 `a`）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户提问】「为什么之前杀冠词没覆盖到水池？」
//
// 【反编译走访（完整链路）】
//   池子名渲染成 `a 池子 of咸的水`（探针实证），那个 `a` 的来路：
//       `GameObjectReplacers.WithIndefiniteArticle`（`=object.a.name=` 出口）
//         → `Object.GetDisplayName(WithIndefiniteArticle: true)`
//         → `Translator.GameObjectDisplayName(…)`            ← 门面，只转发
//         → `provider.GameObjectDisplayName(…)`              ← provider = TranslatorEnglish
//         → `TranslatorEnglish : TranslatorBase`（**空壳**）  ← 逻辑全在基类
//         → `TranslatorBase.GameObjectDisplayName`
//              if (Params.WithIndefiniteArticle) AddArticle(…)
//         → `TranslatorBase.AddArticle(AddArticleParams Params)`
//              Params.SB.Insert(0, "a ");       // ★★ 冠词在这里被插到最前面
//
// 【★ 第一次修**失败**了（2.16.74 → 2.16.75 实测）】
//   我原本挂 `TranslatorBase.AddArticle`，**编译通过**，但运行时：
//       [QudZhsCookbook] 冠词杀手v3：挂载失败：
//       System.BadImageFormatException: Cannot box IsByRefLike type '.AddArticleParams'
//   ⇒ `AddArticleParams` 是 **`ref struct`**（IsByRefLike），Harmony 的
//     `object[] __args` **无法装箱它** ⇒ 补丁等于不存在。
//   ★ 教训（§46 的活例）：**编译通过 ≠ 挂上了**；必须去日志确认。
//
// 【★★ 现在的做法：改挂 `Translator.GameObjectDisplayName`】
//   它与 `AddArticle` 的**参数类型不同**（`GameObjectDisplayNameParams` 是普通 class，
//   可以正常引用），而且就在**同一个汇合点上游**：
//       · `ZhsGrammarShortCircuit` 已证明这个点可以被挂钩
//       · 我们现有的 `ZhsNameDisplayPatch` 挂的是 `Translator.GameObjectDisplayName`
//   做法（**Postfix**，不改游戏逻辑，只清理**已经加错的**英文冠词）：
//       若 `__result` 含 CJK 且以 `a ` / `an ` / `the `（含首字母大写）开头
//       ⇒ 把那个英文冠词摘掉
//   判据说明：**只有英文 provider 才会产出这些前缀**；
//   中文名若以「一」开头（如「一个池子」）自然不会被误伤。
//
// 【安全】
//   · 只摘「结果已含 CJK」且「前缀恰是英文冠词」的场合；异常一律放行
//   · 开关沿用 `_zhs_article_off.txt`（与另两个冠词补丁一致）
//   · 加「首次触发」日志（§46：只写「已挂」不能证明执行过）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsArticleTransPatch
    {
        private const string Tag = "[QudZhsCookbook] 冠词杀手v3：";

        private static bool _applied;
        private static int _calls;
        private static int _stripped;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            try
            {
                if (ZhsArticleFix.Disabled)
                {
                    UnityEngine.Debug.Log(Tag + "已被 _zhs_article_off.txt 停用");
                    return;
                }
            }
            catch { }

            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.Language.Translator", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Translator");
                    return;
                }

                MethodInfo target = null;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Static))
                {
                    if (mi.Name != "GameObjectDisplayName") continue;
                    if (mi.ReturnType != typeof(string)) continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 2) continue;
                    if (ps[0].ParameterType.Name != "GameObject") continue;
                    if (ps[1].ParameterType.Name != "Boolean&") continue;
                    target = mi;
                    break;
                }
                if (target == null)
                {
                    UnityEngine.Debug.LogError(Tag
                        + "找不到 Translator.GameObjectDisplayName(GameObject, out bool, …)");
                    return;
                }

                MethodInfo post = typeof(ZhsArticleTransPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.articletrans")
                    .Patch(target, postfix: new HarmonyMethod(post));

                UnityEngine.Debug.Log(Tag + "已挂 Translator.GameObjectDisplayName"
                    + "（结果含中文 ⇒ 摘掉英文冠词）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>结果含 CJK 且以英文冠词开头 ⇒ 摘掉那个冠词。</summary>
        /// <param name="__result">显示名（已中文化，可能带英文冠词前缀）。</param>
        /// <param name="__0">`XRL.World.GameObject` —— ★ 唯一能拿到"这件东西是什么"的地方。</param>
        private static void Postfix(ref string __result, object __0)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                _calls++;
                ZhsHookTrace.Step("ArticleTrans.in", __result);
                if (!ZhsArticleVoid.HasCjk(__result)) return;

                // ★★ 2026-10-18（#21 / #49 / #53 附加缺口）：显示名里的**部件参数/硬编码词**
                //   · `deactivated`：`ArtificialIntelligence.HandleEvent` 直接 AddTag（不走串表）
                //   · `domesticated`：`IBondedCompanion` 的 Honorific 参数（不走串表）
                //   · `Barathrumite`：阵营形容词，`Factions.xml` 的 DisplayName 管不到
                string fixedWords = ZhsDisplayWord.Fix(__result);
                if (fixedWords != __result) __result = fixedWords;

                // ★★ 2026-10-18（#36 / #31）：`adjunctNoun` 的**语序** ——
                //   模板 `=name=的=adjunctNoun=`（`GameObject.GetDisplayName adjunct noun pre`）
                //   只适合关系名词（`X的村民`）；量词类会成 `奇异管束的套`。
                //   ⇒ 在这里把「X的量词」重新排成「一量词X」。
                string fixedOrder = ZhsAdjunctOrder.Fix(__result);
                if (fixedOrder != __result) __result = fixedOrder;
                // ★ `ZhsAdjunctOrder.Fix` 的 ⓪ 步已经收掉量词后的尾随空格（F13 #1）。

                // ═══════════════════════════════════════════════════════════════
                //  ★★【2026-10-16 夜 · 用户指示：**暂时下掉出口侧**】
                //    改走**数据侧**：`mods\QudZhsCookbook\MeasureWords.xml` 给 2527 个 object
                //    写了官方 `IndefiniteArticle` 蓝图标签（生成器 `_tools\_mw_gen_article_xml.py`）。
                //    机制：`TranslatorBase.IndefiniteArticle` 的**第一条判据**就是
                //        GetPropertyOrTag("IndefiniteArticle")；**非空即短路**，
                //        后面整套英文 a/an/some 规则都不执行 ⇒ 值「一具」直接拼成「一具 熊名」。
                //    用户原话：「先下掉我们的出口侧，如果有疏漏再把出口侧补回来」。
                //    ⇒ 所以下面这段**保留但停用**：数据侧若漏，把 `false` 改成 `true` 即可回补。
                //  ⚠ 副作用（有意）：`a `/`an ` 不再被摘掉 ⇒ 本补丁只处理 `the `。
                // ═══════════════════════════════════════════════════════════════
                const bool ExitSideMeasureWord = false;   // ← 数据侧生效期间 = false

                if (ExitSideMeasureWord)
                {
                    int artLen = IndefiniteArticleLength(__result);
                    if (artLen > 0)
                    {
                        string rest = __result.Substring(artLen);
                        if (rest.Length > 0)
                        {
                            string mw = null;
                            try
                            {
                                mw = ZhsMeasureWord.For(__0 as XRL.World.GameObject, rest);
                            }
                            catch { }
                            // `mw == null` ⇒ 类型表不表态 ⇒ 复用现有首字表（行为与改动前一致）
                            if (mw == null) mw = ZhsTextFilter.MeasureFor(rest) ?? "个";

                            _stripped++;
                            string neo = mw.Length == 0 ? rest : ("一" + mw + rest);
                            if (_stripped <= 8)
                            {
                                UnityEngine.Debug.Log(Tag + "冠词→量词：'" + Shorten(__result)
                                    + "'  =>  '" + Shorten(neo) + "'");
                            }
                            else if (_stripped == 9)
                            {
                                UnityEngine.Debug.Log(Tag + "（后续同类不再逐条打印）");
                            }
                            __result = neo;
                            return;
                        }
                    }
                }

                string stripped = StripLeadingArticle(__result);
                if (stripped == null) return;

                _stripped++;
                if (_stripped <= 5)
                {
                    UnityEngine.Debug.Log(Tag + "摘掉冠词：'" + Shorten(__result)
                        + "'  =>  '" + Shorten(stripped) + "'");
                }
                else if (_stripped == 6)
                {
                    UnityEngine.Debug.Log(Tag + "（后续同类不再逐条打印）");
                }
                __result = stripped;
            }
            catch { }
        }

        /// <summary>
        /// 开头是 `a ` / `an `（含首字母大写）时返回其长度，否则 0。
        /// ⚠ 只认**不定冠词**（要换成「一+量词」）；`the ` 不在此列。
        /// </summary>
        private static int IndefiniteArticleLength(string s)
        {
            if (s.Length > 3 && s.StartsWith("an ", StringComparison.Ordinal)) return 3;
            if (s.Length > 3 && s.StartsWith("An ", StringComparison.Ordinal)) return 3;
            if (s.Length > 2 && s.StartsWith("a ", StringComparison.Ordinal)) return 2;
            if (s.Length > 2 && s.StartsWith("A ", StringComparison.Ordinal)) return 2;
            return 0;
        }

        /// <summary>
        /// 只摘**开头**的英文冠词（`a ` / `an ` / `the `，含首字母大写）。
        /// 返回 null 表示「没摘」（调用方据此不加计数）。
        /// </summary>
        private static string StripLeadingArticle(string s)
        {
            string[] arts = { "a ", "an ", "the ", "A ", "An ", "The " };
            foreach (string a in arts)
            {
                if (!s.StartsWith(a, StringComparison.Ordinal)) continue;
                if (s.Length <= a.Length) continue;
                // ★★ 2.16.123：**删掉「冠词后面必须紧跟汉字」这条护栏**。
                //   它原来的理由是「否则可能是专名，如 `A. Smith`」——但那个例子
                //   根本不会命中：`A. Smith` 的第一个字符是 `A`，`StartsWith("A ")` 就不成立。
                //   而它误伤的却是**最常见**的形态：名字里既有英文又有中文，例如
                //       `The Barathrumite 修补匠`   ← 冠词后面紧跟的是 'B'（拉丁字母）
                //       `the chrome 守卫`            ← 冠词后面紧跟的是 'c'
                //   ⇒ 护栏一挡，`The` 就永远摘不掉（用户截图实证：
                //      `The Barathrumite 修补匠的 attack passes harmlessly through …`）。
                //   ★ 安全性由**调用方**保证：`Postfix` 里已经先判过
                //     `!ZhsArticleVoid.HasCjk(__result) ⇒ return`，
                //     即**整串含中文**才会走到这里 ⇒ 纯英文名（`the snapjaw`）根本进不来。
                //     所以这里只需保证「摘掉的是冠词本身、后面还有内容」即可。
                return s.Substring(a.Length);
            }
            return null;
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ");
            return s.Length <= 60 ? s : s.Substring(0, 60) + "…";
        }

        internal static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：调用 " + _calls + " 次，摘冠词/补量词 "
                + _stripped + " 次 ｜ " + ZhsMeasureWord.Stats());
        }
    }

    /// <summary>
    /// `adjunctNoun` 语序重排（测试窗口 #36 / #31）。
    ///
    /// 官方模板：`GameObject.GetDisplayName adjunct noun pre` = `=name=的=adjunctNoun=`
    ///   · 关系名词（`villagers of X`）⇒ `X的村民` ✅
    ///   · **量词类**（`set of strange tubes`）⇒ `奇异管束的套` ❌（中文是「一+量词+名词」）
    ///
    /// 量词表（与 `MeasureWords.xml` / 物品量词口径一致）：
    ///   套 / 双 / 副 / 丛 / 份 / 堆 / 块 / 串 / 对 / 群 / 束 / 组 / 批
    /// ★ 只认**结尾**是「的+量词」的形态 ⇒ 关系名词（`X的村民` / `X的例子`）不受影响。
    /// </summary>
    internal static class ZhsAdjunctOrder
    {
        private static readonly string[] Mw =
        {
            "套", "双", "副", "丛", "份", "堆", "块", "串", "对", "群", "束", "组", "批",
        };

        private static readonly (string En, string Zh)[] En =
        {
            ("pairs", "双"), ("pair", "双"), ("suits", "副"), ("suit", "副"),
            ("clumps", "丛"), ("clump", "丛"), ("portions", "份"), ("portion", "份"),
            ("piles", "堆"), ("pile", "堆"), ("pieces", "块"), ("piece", "块"),
            ("chunks", "块"), ("chunk", "块"), ("bunches", "串"), ("bunch", "串"),
            ("sets", "套"), ("set", "套"),
        };

        internal static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // ⓪ 量词后的尾随空格（`一株 水藤` → `一株水藤`，见 Postfix 处的长注释）
            s = FixMeasureSpace(s);

            // ① 中文量词：`X的套` → `一套X`
            foreach (string m in Mw)
            {
                string tail = "的" + m;
                if (!s.EndsWith(tail, StringComparison.Ordinal)) continue;
                if (s.Length <= tail.Length) continue;
                return "一" + m + StripLeadingMeasure(s.Substring(0, s.Length - tail.Length));
            }

            // ② 还没被字典换掉的英文量词：`X的pair` → `一双X`
            foreach (var (en, zh) in En)
            {
                string tail = "的" + en;
                if (!s.EndsWith(tail, StringComparison.Ordinal)) continue;
                if (s.Length <= tail.Length) continue;
                return "一" + zh + StripLeadingMeasure(s.Substring(0, s.Length - tail.Length));
            }
            return s;
        }

        /// <summary>
        /// `TranslatorBase.IndefiniteArticle` 的返回值**自带一个尾随空格**
        /// （反编译：`return value + " ";`）⇒ 中文量词渲染成 `一株 水藤`。
        /// 只在「串首 = 一 + 1~2 个汉字」且后面紧跟**中文**（可隔一层色标 `{{W|`）时收掉空格。
        /// ★ 不动「一株 水藤」以外的形态：后面是拉丁字母/数字时原样（`一株 V 型` 保留空格更清楚）。
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex MeasureSpace =
            new System.Text.RegularExpressions.Regex(
                @"^(一[\u4e00-\u9fff]{1,2})[ \t]+(?=(?:\{\{[^|{}]*\|)?[\u4e00-\u9fff])",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static string FixMeasureSpace(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '一') return s;
            return MeasureSpace.Replace(s, "$1");
        }

        /// <summary>
        /// 显示名出口已经带了冠词/量词（`一件沾血的钢靴子的pair`）⇒ 先剥掉再重排，
        /// 否则会成「一双一件沾血的钢靴子」。
        /// </summary>
        private static string StripLeadingMeasure(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                s, @"^一[个件双枚把颗张台口座具只条根块片副]?\s*", "");
        }
    }

    /// <summary>
    /// 显示名里的**部件参数 / 硬编码词**补丁（测试窗口 #21 / #49 / #53）。
    ///
    /// 这三个词都不走字符串表，也不经过 `Factions.xml` 的 DisplayName：
    ///   · `deactivated` —— `XRL.World.Parts.ArtificialIntelligence::HandleEvent`
    ///       直接 `E.AddTag("{{y|[{{K|deactivated}}]}}", 20)`（硬编码）
    ///   · `domesticated` —— `XRL.World.Parts.HasSlaves` 造 `DomesticatedSlave(ParentObject, "Templar", "domesticated", …)`，
    ///       由 `IBondedCompanion::HandleEvent(GetDisplayNameEvent)` 的 `E.AddHonorific(Honorific)` 拼进显示名
    ///   · `Barathrumite` —— 阵营形容词，`GetDisplayName` 直接用英文形容词
    ///
    /// ⇒ 统一在**显示名出口**（`Translator.GameObjectDisplayName` 的 Postfix）做词级替换。
    /// ★ 只在整串**含中文**时调用（调用方已判 `HasCjk`）⇒ 纯英文名不受影响。
    /// </summary>
    internal static class ZhsDisplayWord
    {
        private static readonly (System.Text.RegularExpressions.Regex Rx, string Zh)[] Map =
        {
            // 头衔在名字**前面**，吃掉尾随空白 ⇒ `家养阿戈尔祖夫`（不是「家养 阿戈尔祖夫」）
            (new System.Text.RegularExpressions.Regex(
                @"\bdomesticated\b[ \t]*", System.Text.RegularExpressions.RegexOptions.Compiled),
             "家养"),
            (new System.Text.RegularExpressions.Regex(
                @"\bdeactivated\b", System.Text.RegularExpressions.RegexOptions.Compiled),
             "未激活"),
            (new System.Text.RegularExpressions.Regex(
                @"\bBarathrumite\b", System.Text.RegularExpressions.RegexOptions.Compiled),
             "巴拉斯鲁米特"),
            // ★ 2026-10-18（#46）：`GuardSpawner` / `HasGuards` 的硬编码头衔 `hired guard`
            (new System.Text.RegularExpressions.Regex(
                @"\bhired guard\b", System.Text.RegularExpressions.RegexOptions.Compiled),
             "受雇守卫"),
            // ★ 2026-10-18（#23）：名字 tag 里的 `[{{B|lying on 一张 床}}]`（`Prone` 族硬编码）
            //   捕获到 `}}` / `]` 之前，把尾随空白吃掉，再补「上」⇒ `[{{B|躺在 一张 床 上}}]`
            (new System.Text.RegularExpressions.Regex(
                @"lying on ([^\[\]{}]+?)\s*(?=\}\}|\])"), "躺在$1上"),
            (new System.Text.RegularExpressions.Regex(
                @"sitting on ([^\[\]{}]+?)\s*(?=\}\}|\])"), "坐在$1上"),
        };

        internal static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var (rx, zh) in Map)
            {
                if (!rx.IsMatch(s)) continue;
                s = rx.Replace(s, zh);
            }
            return s;
        }
    }
}
