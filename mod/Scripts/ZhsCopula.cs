// ═══════════════════════════════════════════════════════════════════════════
//  ZhsCopula —— **丢掉「孤立系词」**（中英混排残留）
//
//  【为什么需要它】2026-10-15 实证（`Player.log` 漏译探针）：
//      甲虫臀 is 被恐惧压倒!
//    主语与谓语都已是中文，中间孤零零留一个英文系词。
//
//  【系词从哪来】★ 机制链（全部实证，见 patch_notes/1.0.0.1.md ⑳）：
//    ① `XRL.World.Effects.Terrified::Apply` 调用
//         DidX("are", "overwhelmed with terror", "!", null, null, null, Object);
//       —— **动词写作 `are`**（第二人称/复数形），这是官方的书写约定：
//    ② `Assembly-CSharp.xml` 的文档注释写着：
//         `GameObjectReplacers.Verb(...)`：「will be third-person singular or
//          second-person/plural depending on Object. **Verb param should be provided in
//          2nd-person / plural form** (e.g. "are", "have"…)」
//         `Grammar.ThirdPerson(string, bool)`：「conjugate a (root/2nd-person) verb
//          for a 3rd-person subject」
//    ③ 句子由 `XRL.World.Capabilities.Messaging` 组装：
//         玩家主语：`"You " + Verb + " " + Extra + EndMark`
//         他人主语：`<名> + GetVerb(Verb) + " " + Extra + EndMark`
//       ⇒ 他人主语时 `are` **被变位成 `is`**（`GameObject.Is` 就是 `GetVerb("are")`）
//
//  【为什么我方补丁漏了它】`ZhsDidXZh` 的 `SUBJ` 正则要求「主语 + 英文动词 + **英文谓语**」，
//    可这句的谓语**已被别的表先翻成中文**（`ZhsXDidYPatch.Phrases` 里有
//    `"overwhelmed with terror" ⇒ 被恐惧压倒`）⇒ 正则匹配不上 ⇒ 系词没人处理。
//    ★ 两个补丁挂的是**同一个类型**（`XRL.World.Capabilities.Messaging`），
//      谁先跑取决于 Harmony 挂载顺序 ⇒ 不能靠「谁先谁后」来保证，必须**幂等**。
//
//  【判据为什么安全】要求「**中文字 + 空白 + 单个 ASCII 系词 + 空白 + 中文字/标记**」：
//    · 纯英文句（`You are being watched.`）⇒ 前后不是中文字 ⇒ 不碰 ✓
//    · 中文句里正常引用的英文（`他 is 什么` 这种本就该修）⇒ 修 ✓
//    · 系词后面若是 ASCII 词（`甲虫臀 is watching`）⇒ 不碰（那是没翻完，另有出口管）✓
//
//  【中文为什么要「丢」而不是「译成『是』」】这类消息的谓语本身已是动词短语
//    （被恐惧压倒 / 陷入狂暴 / 甩掉了眩晕）⇒ 加「是」反而不通：
//      ✗ 甲虫臀是被恐惧压倒!      ✓ 甲虫臀被恐惧压倒!
//    （与 `ZhsDidXZh.SUBJ` 分支的既有做法一致 —— 它本来也是丢掉动词）
//
//  【★ 2026-10-17 N7 —— 名词表语要**补「是」**，不能一律丢】
//    用户原话：「有主语和表语都是名词词性的情况」。
//    病句形态：`咬颚兽 is 一个变异体` → `咬颚兽一个变异体` ✗（应「是一个变异体」）
//    ★ 判据选择（**方案 A · 保守 · 零回归**，见 `_tools\待办总览.md` N7）：
//        只对**表语以量词/指示词/所有格开头**的句子补「是」——
//        一个/一名/一件/一种/某/这些/那些/我的… ⇒ 这类开头的表语**必定是名词性**。
//        其余情形（动词短语、无量词名词）**照旧丢** ⇒ 既有 11 条自证用例的期望**全部不变**。
//      ⚠ 已知代价：`你 is 大肥鱼`（无量词）仍会丢成「你大肥鱼」——
//        这条**本来就没有实机实例**（F9 取证：24 处命中全是动词短语），
//        要覆盖它就得判「中文名词 vs 形容词」，纯中文表面做不到 ⇒ 留给有实机证据时再定。
//      ★ `has/have/had` **永远是助动词**，任何情况都丢，不参与本判据（两条规则分开）。
// ═══════════════════════════════════════════════════════════════════════════
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsCopula
    {
        /// <summary>
        /// 孤立系词：**前是中文字、后是中文字或标记开头**的 ASCII 系词（含其两侧空白）。
        /// 替换为空串 ⇒ 顺带把多余空格一起吃掉。
        /// </summary>
        /// <summary>
        /// ★ 2026-10-15 扩：**have 家族**（`has/have/had`）同属这一类 —— 普查显示
        ///   `DidX("have", …)` / `GetVerb("have")` 共 **14 条**；谓语翻成中文后同样会剩
        ///   孤立助动词（`甲虫臀 has 吸收了部分电荷`）。中文可**直接丢助动词**，
        ///   时态交由上下文/「了」承担（与系词同理）。
        ///   ⚠ 不含情态动词 `will/would/can`：那类中文该补「将/会/能」而不是丢，另议。
        /// </summary>
        private static readonly Regex Stranded = new Regex(
            // ★ 2026-10-18（#6）：末尾改成 `\s*` —— 实机是 `你的 有刻纹匕首 was已卸下。`
            //   （`was` 与中文之间**没有空格**）⇒ 原 `\s+` 恒失配。
            @"(?<=[\u4e00-\u9fff])\s+(?:is|are|was|were|am|has|have|had)\s*(?=[\u4e00-\u9fff\{=@])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// ★ N7（2026-10-17）：**表语是名词** ⇒ 补「是」而不是丢。
        ///
        /// 判据 = 表语以**量词 / 指示词 / 所有格**开头（这类开头的表语必定是名词性）：
        ///     一+量词（一个/一名/一件/一种/一只/一条/…/一些）· 某 · 这/那 · 我的/你的/他的…
        /// ★ 只收 `is/are/was/were/am` —— `has/have/had` 是助动词，任何情况都丢。
        /// ★ 为什么不用「表语不在动词表里 ⇒ 就是名词」：动词表（431 条）只覆盖
        ///   `=verb:` 槽里的词，覆盖面远小于 `DidX` 家族的谓语短语
        ///   ⇒ 误判会把动词短语编译成「是被恐惧压倒」⇒ 违反零回归。
        /// </summary>
        private static readonly Regex NounPredicate = new Regex(
            @"(?<=[\u4e00-\u9fff])\s+(?:is|are|was|were|am)\s+"
            + @"(?=一[个名位件种只条头把张本枚座片群套副支根颗粒块滴剂份员层段堆批束捆箱袋桶罐瓶杯碗盘包盒卷缕丝股阵场幕棵株丛些]"
            + @"|某|这|那|我|你|他|她|它)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>丢弃孤立系词；无变化时**原样返回**（调用方据此判断有没有干活）。</summary>
        public static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // 早退：不含 ASCII 系词就不可能命中（省掉正则开销）
            // ★ 2026-10-18（#6）：**不能要求两侧有空格** —— 实机是 `was已卸下`（贴中文）
            if (s.IndexOf("is", System.StringComparison.Ordinal) < 0
                && s.IndexOf("are", System.StringComparison.Ordinal) < 0
                && s.IndexOf("was", System.StringComparison.Ordinal) < 0
                && s.IndexOf("were", System.StringComparison.Ordinal) < 0
                && s.IndexOf("am", System.StringComparison.Ordinal) < 0
                && s.IndexOf("has", System.StringComparison.Ordinal) < 0
                && s.IndexOf("have", System.StringComparison.Ordinal) < 0
                && s.IndexOf("had", System.StringComparison.Ordinal) < 0)
                return s;   // ★ 早退守卫必须与正则同集（§45：漏一个就静默不处理）
            try
            {
                // ① N7：表语以量词/指示词/所有格开头 ⇒ 补「是」（名词性表语）
                string r = NounPredicate.Replace(s, "是");
                // ② 其余孤立系词 / 助动词 ⇒ 丢（原判据不动 ⇒ 零回归）
                return Stranded.Replace(r, "");
            }
            catch { return s; }
        }

        /// <summary>离线自证（`roslyntest -- units` 反射调用）。返回可读报告。</summary>
        public static string SelfTest()
        {
            string[][] cases =
            {
                new[] { "甲虫臀 is 被恐惧压倒!", "甲虫臀被恐惧压倒!", "实机原样（他人主语 + 变位系词）" },
                new[] { "甲虫臀 are 被恐惧压倒!", "甲虫臀被恐惧压倒!", "未变位形（玩家/复数书面形）" },
                new[] { "甲虫臀 was 被恐惧压倒!", "甲虫臀被恐惧压倒!", "过去式" },
                new[] { "甲虫臀 is {{R|被恐惧压倒}}!", "甲虫臀{{R|被恐惧压倒}}!", "谓语带标记开头" },
                new[] { "You are being watched.", "You are being watched.", "★反例：纯英文句不碰" },
                new[] { "甲虫臀 is watching you.", "甲虫臀 is watching you.", "★反例：系词后是 ASCII ⇒ 不碰（另有出口）" },
                new[] { "甲虫臀被恐惧压倒!", "甲虫臀被恐惧压倒!", "★反例：无系词 ⇒ 原样" },
                new[] { "他说 is 好", "他说好", "中文夹系词（同族，一并修）" },
                new[] { "甲虫臀 has 吸收了部分电荷。", "甲虫臀吸收了部分电荷。", "have 家族（他人主语 ⇒ has）" },
                new[] { "甲虫臀 have 吸收了部分电荷。", "甲虫臀吸收了部分电荷。", "have 家族（书写形）" },
                new[] { "甲虫臀 has a good day.", "甲虫臀 has a good day.", "★反例：助动词后是 ASCII ⇒ 不碰" },

                // ═══════════════════════════════════════════════════════════
                // ★★ 2026-10-16 F9：**SVC（主系表）风险锁定**（用户 2026-10-16 重点关注）
                //
                //  【用户原话】「可能存在**你是大肥鱼**这种主语表语都是名词的形态，
                //    我们能不能对这种主系表句做区分和隔离？因为这种句子的 is 是**要翻译的**。」
                //
                //  【判据现状】本类**不看表语词性** ⇒ 表语是**名词**时也会丢系词。
                //
                //  【取证结论（2026-10-16）】**未发现实机实例** ——
                //    · 扫 `Player.log` / `Player-prev.log` / `_zhs_untranslated.log` /
                //      最近 3 个存档消息栏，用与本类**逐字同源**的正则
                //      （`_tools\_probe_f9_copula.py`）：命中 24 处 / 去重 3 种，
                //      **全部**是「系词 + 动词短语」（`is 被恐惧压倒`）或存档里的
                //      英文骨架（`was @@bitten to death by …`，属 F3 死亡界面线）
                //      ⇒ 「系词 + **名词**表语」**一个都没有**。
                //    · 系词来源普查（`_tools\_probe_f9_sources.py`）：15 个 callee，
                //      除已知的 `DidX`(28) / `GetVerb`(24) 外，还有
                //      `Reputation::Modify` 的 `Append("are"/"is")`、
                //      `GameObject.get_YouAre`（`Concat` 里带 `is/are`）、
                //      `XDidXToY`/`XDidYToZ`、`VillageCoda` 的 `String.Replace`。
                //
                //  【处置（按纪律「查不到实例 ⇒ 不许凭空改判据」）】判据**保持不动**，
                //    改用**风险锁定用例**钉住现状：任何人要动判据，必须先让这两条红。
                //    ⚠ 下方两条「期望」是**现状快照，不是理想译文**；
                //      真要修，须先拿到实机证据（或对 `AuxSkip` 之外的 SVC 名词族
                //      设计判据），并把这两条一并更新。
                // ═══════════════════════════════════════════════════════════
                new[] { "你 is 大肥鱼", "你大肥鱼",
                        "★★风险锁定（现状快照，非理想译文）：表语=**名词**时系词也被丢 —— "
                        + "未发现实机实例；改判据前必须先取证（见本类上方注释）" },
                new[] { "你 are 大肥鱼。", "你大肥鱼。",
                        "★★风险锁定（同上 · 句号形态）" },
                new[] { "You is a big fish.", "You is a big fish.",
                        "★反例：纯英文（前后无中文）⇒ 一律不碰（英文局行为不变）" },

                // ═══════════════════════════════════════════════════════════
                // ★★ 2026-10-17 N7：**名词表语补「是」**（方案 A · 保守 · 零回归）
                //
                //  【判据】表语以量词/指示词/所有格开头 ⇒ 名词性 ⇒ 换「是」
                //  【为什么只认这些开头】纯中文表面**分不出**名词与形容词，
                //    但「一个/一名/某/我的…」开头的表语**必定是名词性** ⇒ 这是零误判的锚
                //  【代价（明写）】`你 is 大肥鱼`（无量词）仍丢 —— 见本类上方 N7 段
                // ═══════════════════════════════════════════════════════════
                new[] { "咬颚兽 is 一个变异体", "咬颚兽是一个变异体", "N7 正例：量词「一个」开头 ⇒ 补「是」" },
                new[] { "咬颚兽 is 一名看守", "咬颚兽是一名看守", "N7 正例：量词「一名」" },
                new[] { "它 was 某种东西", "它是某种东西", "N7 正例：量词「某种」的「某」" },
                new[] { "那个 is 我的东西", "那个是我的东西", "N7 正例：所有格「我的」" },
                new[] { "咬颚兽 is 一件破烂", "咬颚兽是一件破烂", "N7 正例：量词「一件」" },
                new[] { "咬颚兽 is 一起走", "咬颚兽一起走",
                        "★N7 反例：「一」+**非量词**（起）⇒ 仍走丢分支（不补「是」）" },
                new[] { "咬颚兽 is 被恐惧压倒!", "咬颚兽被恐惧压倒!",
                        "★N7 反例（回归）：动词短语仍丢，不被补「是」" },
            };
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== `ZhsCopula.Fix` 离线自证（真实代码路径）===");
            int pass = 0, fail = 0;
            foreach (string[] c in cases)
            {
                string got = Fix(c[0]);
                bool ok = got == c[1];
                if (ok) pass++; else fail++;
                sb.AppendLine((ok ? "  [PASS] " : "  [FAIL] ") + c[2]);
                if (!ok)
                {
                    sb.AppendLine("         输入: " + c[0]);
                    sb.AppendLine("         期望: " + c[1]);
                    sb.AppendLine("         实得: " + got);
                }
            }
            sb.AppendLine("★ ZhsCopula 合计 PASS " + pass + " · FAIL " + fail);
            return sb.ToString().TrimEnd();
        }
    }
}
