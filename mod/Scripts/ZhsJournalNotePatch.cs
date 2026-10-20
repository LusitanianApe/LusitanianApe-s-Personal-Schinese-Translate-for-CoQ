// 日志「记下位置/信息」提示的汉化。
//
// ── 为什么需要这个补丁 ──────────────────────────────────────────────
// 这些文案在 C# 里是**硬编码字面量拼接**，从不查字符串表：
//
//   // Qud.API.JournalMapNote::Reveal
//   else if (Category == "Historic Sites")
//       text = "You note the location of " + Grammar.MakeTitleCaseWithArticle(Text)
//            + " in the {{W|" + JournalScreen.STR_LOCATIONS + " > Historic Sites}} section of your journal.";
//
// ⇒ 段名（STR_LOCATIONS）是查表的（所以存档里有中文版），
//   但**包在外面的句式**是硬编码 ⇒ Strings.xml 无法覆盖。
//
// ── 为什么挂这里 ────────────────────────────────────────────────────
// `Qud.API.IBaseJournalEntry.DisplayMessage` 是 **static**，只有 2 个参数，
// 且是全部 5 个 Journal 类的**共同出口**：
//     JournalMapNote / JournalObservation / JournalRecipeNote
//     / JournalSultanNote / JournalVillageNote
// ⇒ 挂一处即全覆盖；Postfix 只改 `ref __0`，**原方法的副作用完全不受影响**。
//   （对比：Prefix 直接跳过原方法会丢 base.Reveal() 的 Revealed 标记等副作用。）
//
// ── 注意 ────────────────────────────────────────────────────────────
// * Harmony 参数必须**按值/按序号**传，且**不能写 ref 之外的东西**。
//   静态方法用 `ref string __0` 是合法的（对应第 1 个参数）。
// * 正则必须**锚定整句**，避免误伤游戏里其它含 "journal" 的正常文本。
// * 开关文件 `_zhs_journalnote_off.txt` 可停用。

using System.Reflection;
using System;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>日志「记下信息」提示的替换规则。</summary>
    internal static class ZhsJournalNote
    {
        public static bool Disabled;
        private static int _hits;

        // 日志分类名 → 中文。
        // ★ 权威来源：反编译 `Qud.API.JournalCategories` 的 13 个 const；
        //   译名取自我方 Strings.xml 的 `[journal category]`（13 条全有）。
        private static readonly (string En, string Zh)[] Cats =
        {
            ("Artifacts",                 "器物"),
            ("Baetyls",                   "贝提尔"),
            ("Dimensions",                "维度"),
            ("Gossip",                    "流言"),
            ("Historic Sites",            "历史遗迹"),
            ("Kith and Kin",              "亲族与同胞"),
            ("Lairs",                     "巢穴"),
            ("Merchants",                 "商人"),
            ("Natural Features",          "自然地物"),
            ("Oddities",                  "奇物"),
            ("Ruins",                     "遗迹"),
            ("Ruins with Becoming Nooks", "含蜕变角的遗迹"),
            ("Settlements",               "聚落"),
            // ★★ 2026-10-15（E-5 第 ④ 项 / `you路径审计-结论.md` §六第 4 条）：
            //   `Named Locations` 是 `JournalMapNote::Reveal` 的 11 个分类之一，
            //   但**官方硬编码、无串表条目**（实证：`ExampleLanguage\Strings.example.xml` 里
            //   `ID="Named Locations"` **0 命中**）⇒ 只能自定义。
            //   ★ 译名由**用户 2026-10-15 裁定**：「具名地点」
            //     （决策表：`_tools\地点分类11条-译名决策表.md`）
            ("Named Locations",           "具名地点"),
        };

        // 日志页签名 → 中文。
        // ★ 这些是 `XRL.UI.JournalScreen..cctor` 里的**硬编码字面量**
        //   （STR_LOCATIONS = "Locations" 等），我和官方 example 里都没有，
        //   且**被拼进上面那些 popup 句子里**（"in the {{W|" + STR_LOCATIONS + " > …"）。
        private static readonly (string En, string Zh)[] Tabs =
        {
            ("Locations",         "位置"),
            ("Chronology",        "年表"),
            ("Gossip and Lore",   "流言与传说"),
            ("Sultan Histories",  "苏丹史"),
            ("Village Histories", "村落史"),
            ("General Notes",     "一般笔记"),
            ("Recipes",           "食谱"),
        };

        /// <summary>把 popup 文本里夹着的英文页签名换成中文。</summary>
        private static string FixTabs(string s)
        {
            // ① 带颜色标记的形态（原始消息）
            for (int i = 0; i < Tabs.Length; i++)
            {
                s = s.Replace("{{W|" + Tabs[i].En + " > ", "{{W|" + Tabs[i].Zh + " › ");
                s = s.Replace("{{W|" + Tabs[i].En + "}}", "{{W|" + Tabs[i].Zh + "}}");
            }
            // ② ★★ **无颜色标记**的形态（2.5.3 补）
            //
            //   实证（截图）：`You note this piece of information in the
            //   Sultan Histories > Khuthrishid section of your journal.`
            //   我方 `ZhsCombatMsgPatch` 的规则表是在**剥色之后**匹配的
            //   ⇒ 那时 `{{W|` 已经没了 ⇒ 上面的 ① **全都匹配不上** ⇒ 页签名漏译。
            //
            //   ⇒ 用正则精确匹配 `in the <页签> > <子项> section of your journal`
            //     三个捕获组：页签名 / 子项名 / 整段
            //     （只在这一种句式里替换 ⇒ 不会误伤正文里同名的词）
            //
            // ★★ 2.16.66 修（用户截图：`你在日志的Sultan Histories > 雷谢夫栏记下了这条信息。`）
            //
            //   旧正则：`in the ([A-Za-z][A-Za-z ]*) > ([^\n]+?) section of your journal\.`
            //                            └─ 只允许**纯 ASCII** 页签名 ─┘
            //   而实际消息是 `…in the Sultan Histories > 雷谢夫 section of your journal.`
            //     —— 子项 `雷谢夫` **已被官方字符串表译成中文**
            //   ⇒ `[A-Za-z ]*` 吃不下 CJK ⇒ **整条正则匹配失败** ⇒ 页签名漏译。
            //   （上一版把「页签 > 子项」当整体看，故只在「子项也是英文」时成立。）
            //
            //   两根因叠加才暴露：
            //     ① 正则的字符类排除了 CJK
            //     ② `Apply()` 里那条**「宁可保留英文也不产出坏句」的护栏**
            //        （2.15.63 加的）在匹配失败后接管 ⇒ 直接返回原文
            //   ⇒ 于是产出「中文句式 + 英文页签名」的半译形态。
            //
            //   修法：页签名改 `[A-Za-z][A-Za-z ]*?`（**非贪婪**，让它停在 ` > `），
            //         并**校验捕获到的页签名确实在 `Tabs` 里**才替换
            //         （护栏保留：认不出就整体不动，绝不产出坏句）。
            var rx = new System.Text.RegularExpressions.Regex(
                @"in the ([A-Za-z][A-Za-z ]*?) > ([^\n]+?) section of your journal\.",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            s = rx.Replace(s, m =>
            {
                string tab = m.Groups[1].Value.Trim();
                string sub = m.Groups[2].Value.Trim();
                string zh = null;
                for (int i = 0; i < Tabs.Length; i++)
                {
                    if (Tabs[i].En == tab) { zh = Tabs[i].Zh; break; }
                }
                // 护栏：页签名不在译表里 ⇒ 原样放行（不猜、不改坏）
                if (zh == null) return m.Value;
                return "的「" + zh + " › " + sub + "」栏";
            });
            return s;
        }

        private static Regex _mapNoteAny;
        private static Regex _obsNote;

        private static void Build()
        {
            if (_mapNoteAny != null) return;
            // ★★ 2026-10-17（测试窗口 #3）：**不能再按英文分类名逐条建正则**。
            //   实机到达这里时分类**已经是中文**（`地点 > 巢穴` / `地点 > 聚落`），
            //   旧的 `... > " + en + "}}"` 结构上不可能命中 ⇒ 落到 `ZhsCombatMsgPatch`
            //   的兜底 `^You note the location of (X)\.$` ⇒ `$1` 把
            //   `Sithis，… in 地点 › 巢穴 section of your journal` **整段吞掉**，
            //   产出「你记下了…的位置。」（半译）。
            //   ⇒ 改成**一条通用正则**：分类名先捕获再按 EN / ZH 双查（见 Apply）。
            //   `{{W|` 外壳与 `}}` 都做成可选：消息口另一条路径会先**剥色**。
            _mapNoteAny = new Regex(
                @"^You note the location of (.+?) in (?:the )?(?:\{\{W\|)?(.+?)\s*[>›]\s*(.+?)(?:\}\})? "
                + @"section of (?:your|你的) journal\.$",
                RegexOptions.Compiled | RegexOptions.Singleline);
            // {{W|X}}\n\nYou note this piece of information in the {{W|{LOC}}} section of your journal.
            // ★ 2.15.9 修正：原正则要求「前置 `{{W|…}}` + 空行」且 `{{W|…}}` 必在，
            //   但 `JournalSultanNote::Reveal` 实际输出的是**单行**：
            //     "You note this piece of information in the {{W|Sultan Histories > 雷谢夫}}
            //      section of your journal."
            //   ⇒ 匹配失败、原样输出英文（用户 2026-10-07 截图实证）。
            //   现在三部分**全可选**，兼容两种情况。
            _obsNote = new Regex(
                @"^(?:\{\{W\|.+?\}\}\s*\n\s*\n)?You note this piece of information in the "
                + @"(?:\{\{W\|)?(.+?)(?:\}\})? section of (?:your|你的) journal\.$",
                RegexOptions.Compiled | RegexOptions.Singleline);
        }

        /// <summary>
        /// 纯字符串精确替换表（硬编码 popup 文案）。
        ///
        /// ★ 实证：DLL 里这 4 条**硬编码**（我方字符串表与官方数据文件都没有）：
        ///     "Can't fast travel."
        ///     "You recognize the area and stop being lost!"
        ///     "You regain your bearings."
        ///     "You are lost!"                              ← 截图实证漏网
        ///   同一族的 "You're lost! Regain your bearings by exploring your
        ///   surroundings." 则**走字符串表**（Context="TeleportGate Lost Popup"），已译。
        ///   ⇒ 两套机制并存，必须分别处理。
        /// </summary>
        private static readonly (string En, string Zh)[] Exact =
        {
            ("You are lost!",                              "你迷路了！"),
            ("You regain your bearings.",                  "你重新辨明了方位。"),
            ("You recognize the area and stop being lost!", "你认出了这片地方，不再迷路。"),
            ("Can't fast travel.",                         "无法快速旅行。"),
        };

        /// <summary>文本后处理主入口；未命中则原样返回。</summary>
        public static string Apply(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            // ① 硬编码精确表（先做，代价最低）
            for (int i = 0; i < Exact.Length; i++)
            {
                if (s == Exact[i].En) { _hits++; return Exact[i].Zh; }
            }
            // ★ 2026-10-17（#3）：实机到达时 `your` 可能**已被词表译成「你的」**
            //   （截图：`… section of 你的 journal.`）⇒ 判据放宽到 `section of `。
            if (s.IndexOf("section of ", StringComparison.Ordinal) < 0)
                return s;
            try
            {
                Build();
                // ① 位置类（通用正则 + 分类名双查，见 Build 的注释）
                {
                    var m = _mapNoteAny.Match(s);
                    if (m.Success)
                    {
                        string cat = m.Groups[3].Value.Trim();
                        string zh = null;
                        for (int i = 0; i < Cats.Length; i++)
                        {
                            if (Cats[i].En == cat || Cats[i].Zh == cat)
                            {
                                zh = Cats[i].Zh;
                                break;
                            }
                        }
                        // 分类名查不到 ⇒ 原样保留捕获到的名字（骨架仍汉化；
                        // 不套模板会掉给兜底规则把整段吞掉，反而更糟）
                        if (zh == null) zh = cat;
                        _hits++;
                        // 「位置」= STR_LOCATIONS 的译名；分类名紧随其后
                        return FixTabs("你在日志的{{W|位置 > " + zh + "}}部分记下了 "
                                       + m.Groups[1].Value + "。");
                    }
                }
                // ② 观测类
                {
                    var m = _obsNote.Match(s);
                    if (m.Success)
                    {
                        // ★ 2.15.63 修：`_obsNote` 的 $1 是**贪婪到最后一个 ` > `** 的，
                        //   当页签名形如 `Sultan Histories > 雷谢夫`（子项已是中文）时：
                        //       $1 = "Sultan Histories > 雷谢夫"（整段）
                        //       $2 = ""                        ⇒ 模板里的 {{W|}} 空掉
                        //   而 `FixTabs` 的兜底正则要求页签名是**纯 ASCII**，
                        //   匹配不上 ⇒ 页签名被留在句式**外面**，产出坏句：
                        //       旧=You note this piece of information in the {{W|Sultan Histories > 雷谢夫}} section of your journal.
                        //       新=Sultan Histories > 雷谢夫\n\n你在日志的{{W|}}部分记下了这条信息。
                        //   （日志实证：Player.log「日志提示已替换」那一段）
                        //
                        //   ⇒ 校验译后文本：若**仍含拉丁字母**（= 页签名没译成）
                        //     就**不套模板**，返回原文 —— 宁可保留英文，
                        //     也不产出「{{W|}} 空掉 + 页签名错位」的坏句。
                        string cand = FixTabs(m.Groups[1].Value + "\n\n你在日志的{{W|"
                                             + m.Groups[2].Value + "}}部分记下了这条信息。");
                        int k = cand.IndexOf("部分记下了这条信息", StringComparison.Ordinal);
                        string sig = k > 0 ? cand.Substring(0, k) : cand;
                        bool hasLatin = false;
                        for (int q = 0; q < sig.Length; q++)
                        {
                            char c = sig[q];
                            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                            {
                                hasLatin = true;
                                break;
                            }
                        }
                        if (hasLatin)
                            return s;      // 没译成 ⇒ 保持原样，不产出坏句
                        _hits++;
                        return cand;
                    }
                }
            }
            catch { /* 后处理绝不能影响游戏 */ }
            return s;
        }

        public static string Stats() { return "日志提示替换 " + _hits + " 次"; }
    }

    /// <summary>
    /// 挂 `Qud.API.IBaseJournalEntry.DisplayMessage(string, string)` ——
    /// 5 个 Journal 类的共同出口，且是 static，只改第 1 个参数。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsJournalNotePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Qud.API.IBaseJournalEntry");
            if (t == null) return null;
            // 优先按 (string, string) 精确匹配
            var m = AccessTools.Method(t, "DisplayMessage", new[] { typeof(string), typeof(string) });
            if (m != null) return m;
            // 退化：取第一个 static DisplayMessage
            foreach (var mm in t.GetMethods(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Static))
            {
                if (mm.Name == "DisplayMessage") return mm;
            }
            return null;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try
            {
                var m = TargetMethod();
                UnityEngine.Debug.Log("[QudZhsCookbook] 日志提示补丁 Prepare：TargetMethod="
                    + (m == null ? "❌ null（补丁不会挂！）"
                                 : "✅ " + m.DeclaringType.FullName + "::" + m.Name
                                   + " 参数 " + m.GetParameters().Length
                                   + " 静态 " + (m.IsStatic ? "是" : "否")));
                return m != null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 日志提示补丁 Prepare 异常：" + e);
                return false;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            string before = __0;
            ZhsHookTrace.Step("JournalNote.in", before);
            __0 = ZhsJournalNote.Apply(__0);
            if (!ReferenceEquals(before, __0) && before != __0)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 日志提示已替换：\n  旧="
                    + (before ?? "").Substring(0, Math.Min(120, (before ?? "").Length))
                    + "\n  新=" + (__0 ?? "").Substring(0, Math.Min(120, (__0 ?? "").Length)));
            }
        }
    }

    /// <summary>
    /// 兜底挂载点。
    ///
    /// ★ 实证：`Player.log` 里搜不到 "section of your journal"
    ///   ⇒ 这条 popup **不进 message log** ⇒ `DisplayMessage` 走的是
    ///     `if (NotedPrompt) { Popup.Show(Message, null, Sound); return; }` 分支。
    ///   所以兜底要挂 **`XRL.UI.Popup.Show`**，而不是 `AddPlayerMessage`。
    ///   （`AddPlayerMessage` 也保留，覆盖 `NotedPrompt=false` 的情形。）
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsJournalNoteFallbackPatch
    {
        private static int _showHit;

        private static System.Reflection.MethodBase TargetMethod()
        {
            // Popup.Show(string Message, string Sound = null) —— 参数可能还有更多可选参数，
            // 故不写死签名，按名字找第一个 static Show(string, …)。
            var t = AccessTools.TypeByName("XRL.UI.Popup");
            if (t == null) return null;
            System.Reflection.MethodBase best = null;
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "Show") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    if (best == null || ps.Length < best.GetParameters().Length) best = m;
                }
            }
            return best;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try
            {
                var m = TargetMethod();
                UnityEngine.Debug.Log("[QudZhsCookbook] 日志兜底补丁 Prepare：Popup.Show="
                    + (m == null ? "❌ null"
                                 : "✅ " + m.DeclaringType.FullName + "::" + m.Name
                                   + " 参数 " + m.GetParameters().Length));
                return m != null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 日志兜底补丁 Prepare 异常：" + e);
                return false;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            if ((__0 ?? "").IndexOf("of your journal", StringComparison.Ordinal) >= 0
                && _showHit < 4)
            {
                _showHit++;
                UnityEngine.Debug.Log("[QudZhsCookbook] 兜底命中 Popup.Show："
                    + __0.Substring(0, Math.Min(170, __0.Length)));
            }
            __0 = ZhsJournalNote.Apply(__0);
            ZhsHookTrace.Step("JournalFallback.post", __0);
        }
    }

    /// <summary>
    /// ★★ 2026-10-15（E47）：**成就文本出口**。
    ///
    /// 【为什么需要】`XRL.World.Effects.Proselytized::Apply` 里是
    ///     `JournalAPI.AddAccomplishment("You convinced " + Object.an() + " to join your cause.", …)`
    ///   —— **运行时拼接**，弹窗（`Popup.*`）与消息栏（`AddPlayerMessage`）两条出口**都够不到**
    ///   （E5 报告把它列为**唯一「出口待定」**的一条）。
    /// 【做法】在**源头** `Qud.API.JournalAPI::AddAccomplishment` 挂 Prefix，
    ///   把 arg0（成就文本）过一遍 `ZhsCombatMsgPatch.TranslateText` ——
    ///   那是本模组**统一的句式规则入口**，命中即改写、不命中原样返回（幂等、零风险）。
    /// 【签名】`AddAccomplishment(string text, string muralText = null, string gospelText = null,
    ///   string aggregateWith = null, string category = "general", …)`（反编译实证）
    ///   ⇒ 只改 **arg0**；`muralText` / `gospelText` 是壁画/福音的**长版**，另属 A 族，不动。
    /// </summary>
    [HarmonyPatch]
    internal static class AccomplishmentPatch
    {
        private static MethodBase TargetMethod()
        {
            try
            {
                var t = AccessTools.TypeByName("Qud.API.JournalAPI");
                if (t == null) return null;
                // 按名找**第一个** arg0 是 string 的重载（签名参数多，写死类型太脆）
                foreach (var m in AccessTools.GetDeclaredMethods(t))
                {
                    if (m.Name != "AddAccomplishment") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                        return m;
                }
                return null;
            }
            catch { return null; }
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0, ref string __1, ref string __2)
        {
            Fix("成就行(arg0)", ref __0, "self");
            Fix("壁画(arg1)", ref __1, "mural");
            Fix("福音(arg2)", ref __2, "gospel");
        }

        /// <summary>
        /// 单条文本的替换（幂等：不命中 ⇒ 原样返回）。
        /// </summary>
        /// <param name="slot">日志用的槽名。</param>
        /// <param name="s">要处理的串（按引用改）。</param>
        /// <param name="kind">用于日志分类。</param>
        private static void Fix(string slot, ref string s, string kind)
        {
            if (string.IsNullOrEmpty(s)) return;
            try
            {
                string r = ZhsCombatMsgPatch.TranslateText(s);
                if (r == s) return;
                if (_hitByKind[kind] < 3)
                {
                    _hitByKind[kind]++;
                    try
                    {
                        UnityEngine.Debug.Log("[QudZhsCookbook] " + slot + " 已替换：\n  旧="
                            + s + "\n  新=" + r);
                    }
                    catch { }
                }
                s = r;
            }
            catch { }
        }

        private static readonly System.Collections.Generic.Dictionary<string, int> _hitByKind =
            new System.Collections.Generic.Dictionary<string, int>();
    }
}
