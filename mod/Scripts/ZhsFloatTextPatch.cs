// ═══════════════════════════════════════════════════════════════════════════
//  ZhsFloatTextPatch —— F7：**场景内浮动文字**（in-world float text）
//
//  【用户截图 1（220×166，2026-10-16）】
//      场景里、角色附近浮着一行**绿色大字** `*stuck*`（**不是消息栏**！）
//      用户原话：「就是这种战斗中出现在角色附近的文字……没打上补丁。
//                  包括这种状态类的和 critical hit 都没打上。」
//
//  【★ 出口定位（反编译实证）——一条出口，两个入口都汇到它】
//
//    ★ 入口 ①「状态浮字」：`XRL.World.Effects.Stuck::Apply`
//          `Object.ParticleText("*" + Adjective + "*", ConsequentialColorChar(null, Object));`
//        （`Stuck::.ctor` 里 `Adjective = "stuck"; DisplayName = Adjective;`）
//      ⇒ 画面上的 `*stuck*` 就是这个字符串拼接的产物
//        （这解释了为什么 `literals-heap` 里搜不到 `*stuck*` 整串）。
//
//    ★ 入口 ②「critical hit 浮字」：`XRL.World.Parts.Combat::MeleeAttackWithWeaponInternal`
//          `if (flag2) Attacker.ParticleText("*critical hit*", ConsequentialColorChar(Attacker));`
//
//    两条都调 `XRL.World.GameObject::ParticleText`（`decomp member` 全量 4 个重载）：
//        public void ParticleText(string Text, float Velocity, int Life)
//        public void ParticleText(string Text, bool IgnoreVisibility = false)
//        public void ParticleText(string Text, float xVel, float yVel, char Color = ' ', bool IgnoreVisibility = false)
//        public void ParticleText(string Text, char Color, bool IgnoreVisibility = false,
//                                 float juiceDuration = 1.5f, float floatLength = -8f)   ← ★ 本族走这个
//      ★★ 它们的**正文串就是 arg0**（`Text`），并且**没有任何查表步骤** ⇒
//         **完全不经过字符串表**（这就是为什么「没打上补丁」：根本没有键可查）。
//
//    ★ 下游（`XRL.World.Cell::ParticleText`，反编译）：
//        if (juiceEnabled) { … CombatJuice.floatingText(this, Text, ColorMap[Color], …); return; }
//        … The.ParticleManager.Add(Text, X, Y, …);
//      ⇒ 两条渲染路径（`CombatJuice.floatingText` 与 `ParticleManager`）**都在 arg0 之后**，
//        所以在 `GameObject::ParticleText` 改 arg0 ⇒ 两条一起好（§36「出口不止一条」的反面：
//        这里是**同一条出口的上游**）。
//
//  【★ 与消息栏是否共用出口】**不共用**：
//    · 消息栏 `MessageQueue.AddPlayerMessage` → `EmitMessage`（我方 `ZhsCombatMsgPatch` 挂这里）
//    · 浮动文字 `GameObject.ParticleText` → `CombatJuice.floatingText` / `ParticleManager`
//    ⇒ 两条完全不同的路（skill `coq-text-exits` §三 的「粒子横幅」那一行）
//    ⇒ 这就是 F7 从未被覆盖的机制级原因。
//
//  【★ 已枚举的浮动文字字面量（`_census\callsites.tsv` 搜 `ParticleText` + `decomp` 补漏）】
//      *crippled* · *broke free* · *mobility impaired* · *overburdened* · *paralyzed*
//      *knocked prone* · *scintillating* · *stunned* · *made save vs. stun*
//      *remains stunned* · *suppressed* · *synapse snap* · *synapse snap wore off*
//      *hobbled* · *resisted* · &W! · &R!!! · &W? · *critical hit*
//      ★ 另有**纯色码**（`&W!` / `&R!!!` / `&W?`）与**火焰粒子**（`&B`+CP437 块字符）
//        —— 前者只翻非色码部分，后者**不动**（那是图形不是文字）。
//      ⚠ `ParticleText` 还有**数值型**用途：`Physics::ProcessTakeDamage` 里
//        `CombatJuice.floatingText(ParentObject, "-" + damage.Amount, …)`（伤害数字）
//        ⇒ 不含字母的串一律放行，绝不碰。
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFloatTextPatch
    {
        private const string Tag = "[QudZhsCookbook] 浮动文字接管：";

        private static bool _applied;
        private static int _hits;

        /// <summary>
        /// 精确表：**整串**（剥掉 `{{…}}` 与 `&X` 色码后）→ 中文。
        /// ★ 只收「**有且只有这一种含义**」的整串 —— 短词如 `stuck` 单用会与
        ///   `stuck in 水洼` 之类的句子碎片撞车，故**只翻带 `*…*` 星号壳**的形态
        ///   （星号是游戏给「状态浮字」的固定外壳，见 `Stuck::Apply`）。
        /// </summary>
        private static readonly Dictionary<string, string> T =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 状态类（`Effects.*::Apply` / `HandleEvent` 的 `ParticleText`）──
            { "*stuck*",              "*卡住*" },
            { "*crippled*",           "*残废*" },
            { "*broke free*",         "*挣脱*" },
            { "*mobility impaired*",  "*行动不便*" },
            { "*overburdened*",       "*负重过度*" },
            { "*paralyzed*",          "*瘫痪*" },
            { "*knocked prone*",      "*被击倒*" },
            { "*scintillating*",      "*闪烁*" },
            { "*stunned*",            "*晕眩*" },
            { "*made save vs. stun*", "*抵抗晕眩成功*" },
            { "*remains stunned*",    "*仍然晕眩*" },
            { "*suppressed*",         "*被压制*" },
            { "*synapse snap*",       "*突触断裂*" },
            { "*synapse snap wore off*", "*突触断裂消退*" },
            { "*hobbled*",            "*腿脚受伤*" },
            { "*resisted*",           "*抵抗成功*" },
            // ── 战斗浮字 ──
            { "*critical hit*",       "*重击*" },
            // ── 已走**字符串表**的同族（列在这里 = **已覆盖**，不是漏项；见下「覆盖矩阵」）──
            //    `_S("… Particle Text", …)` 型（我方 Strings.xml 已有译文）：
            //      *dazed* · *exhausted* · *shook off stun* · *shook off daze*
            //      *shook off confusion* · *shook off fear* · Resisted stun! / Resisted daze!
            //      Tomb-tethered! · *=subject.name:withTitles|strip= broken*
            //    `_T("… ParticleText", …)` 型（同上）：
            //      *decapitated!* · *dismembered!* · KLANQ! · {{c|'=CRYPTICWORD='}}
            //    ⇒ 这些**不必**进本表；但**放进来也无害**（更早命中、结果相同）。
            // ── 纯色码 + 标点（`Axe_Berserk` 的 `&R!!!` / `Cudgel_SmashUp` 的 `&R!!!`
            //    / `Confusion` 的 `&W?` / `Terrified` 的 `&W!`）──
            //    只有色码与标点，**没有可译的词** ⇒ 原样保留（枚举出来是为了说明
            //    「已枚举但无需翻译」，不是漏项）。
        };

        /// <summary>
        /// ★ 顺序模板：**带变量**的浮字（`*<名字> broken*` 之类），精确表接不住。
        /// `{0}` = 捕获组内容（**不翻译**：那是玩家/物种自定义名，§五）。
        /// </summary>
        private static readonly (System.Text.RegularExpressions.Regex Rx, string Zh)[] PAT =
        {
            // `XRL.World.Effects.Broken::Apply`（该条**走字符串表**且我方已有译文；
            // 这里只是兜底，防止别的语言/别的调用方传英文名进来时留下 `broken`）
            (new System.Text.RegularExpressions.Regex(@"^\*(.+?) broken\*$"), "*{0}被击碎*"),
        };

        /// <summary>
        /// 是否值得过表：**必须含至少一个拉丁字母**。
        /// ⇒ 伤害数字（`-17` / `+3`）、火焰块字符（`█`）、纯色码（`&R`）一律直接放行。
        ///
        /// ★ 这一条是**必须的**，不是保险：`GameObject::ParticleText` 还被用来浮
        ///   **伤害数值**（`Physics::ProcessTakeDamage` 的
        ///   `CombatJuice.floatingText(ParentObject, "-" + damage.Amount, …)`）
        ///   —— 用户推测的「critical hit 与伤害数值同一出口」**成立**，
        ///   但数值**不该翻** ⇒ 用「不含字母」把它们挡在表外。
        /// </summary>
        private static bool HasLetter(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
            }
            return false;
        }

        /// <summary>
        /// `*…*` 星号壳浮字（游戏给「状态浮字」的固定外壳，见 `Stuck::Apply` 的
        /// `"*" + Adjective + "*"`）。**只用于诊断留痕，不用于产出译文**
        /// —— 精确表才是产出源（宁缺勿滥）。
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex STAR =
            new System.Text.RegularExpressions.Regex(@"^\*[^*]+?\*$");

        /// <summary>剥掉 `{{色|内容}}` 外壳（取内容）与 `&X` 色码，得到查表形态。</summary>
        internal static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string t = s.Trim();
            // `&X` 两字符色码（老通道）—— 只剥**开头**的连续色码，不动句中
            while (t.Length >= 2 && t[0] == '&' && !char.IsDigit(t[1]) && t[1] != ' ')
                t = t.Substring(2);
            // `{{色|内容}}`：取内容（可能嵌套一层）
            if (t.StartsWith("{{", StringComparison.Ordinal) && t.EndsWith("}}", StringComparison.Ordinal))
            {
                int bar = t.IndexOf('|');
                if (bar > 0) t = t.Substring(bar + 1, t.Length - bar - 3).Trim();
            }
            return t;
        }

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type tg = AccessTools.TypeByName("XRL.World.GameObject");
                if (tg == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.GameObject");
                    return;
                }
                MethodInfo pre = typeof(ZhsFloatTextPatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.floattext");
                int n = 0;
                foreach (MethodInfo m in tg.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name != "ParticleText") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    // ① 第一个参数必须是 string（正文）
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h.Patch(m, prefix: new HarmonyMethod(pre));
                        n++;
                    }
                    catch { }
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 ParticleText 重载（"
                    + T.Count + " 条精确浮字 + " + WORD.Count + " 个拼装词）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// 四条 `ParticleText` 重载的 Prefix：把 arg0 的浮字换成中文。
        /// ★ 用 `object[] __args` **就地改元素** 而不是 `ref string __0` —— 本项目 §49 的
        ///   教训是「接口类型的 `ref` 不回写」；这里是 `string`（值类型引用语义 OK），
        ///   但 `__args` 写法对**四个重载都能用同一份**，且改完可从数组里**复读**自证。
        /// </summary>
        private static void Prefix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 1) return;
                string s = __args[0] as string;
                if (s == null || !HasLetter(s)) return;
                string zh = Look(s);
                if (zh == null)
                {
                    // ★★ 未命中留痕（**只记一次**，不刷屏）——这是本补丁的**唯一验收凭据**：
                    //    浮字走 `CombatJuice.floatingText`（图形层），**没有**像消息栏那样的
                    //    「进 X => 出 Y」日志可读 ⇒ 必须自己记。
                    //    ⚠ `*stuck*` 在 DLL 里**没有整串字面量**（是 `"*"+Adjective+"*"` 拼的）
                    //      ⇒ 这种「拼装浮字」只能靠这条留痕回填精确条目（见类头「枚举」段）。
                    NoteUnmatched(s);
                    return;
                }
                // ★ 保留原有的**前后空白**，只换正文。
                //   ★★ **不要**在这里自己加 `{{色|…}}` —— 色码是 `Cell::ParticleText` 在下游
                //      按 `Color` 参数加的（反编译实证：`Text = "{{" + Color + "|" + Text + "}}"`）
                //      ⇒ 这里再加会**双重包裹**。
                int lead = s.Length - s.TrimStart().Length;
                int tailLen = s.Length - s.TrimEnd().Length;
                string outer = lead > 0 ? s.Substring(0, lead) : "";
                string tail = tailLen > 0 ? s.Substring(s.Length - tailLen) : "";
                __args[0] = outer + zh + tail;
                // ★ 复读自证（§49：只复读自己的局部引用 = 自证 = 等于没证）
                if (_hits <= 30)
                {
                    _hits++;
                    try
                    {
                        UnityEngine.Debug.Log(Tag + s + "  =>  " + (__args[0] as string));
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Prefix 失败：" + e);
            }
        }

        /// <summary>
        /// ★★ 通配兜底：`*&lt;词&gt;*`（**单个词，不是整句**）。
        ///
        /// 【为什么要它】用户截图那个绿色 `*stuck*` 用的是 `"*" + Adjective + "*"`
        ///   **现场拼装**（`Effects.Stuck::Apply`）⇒ DLL 里**搜不到 `*stuck*` 整串**
        ///   ⇒ 只做整串查表会漏掉**所有**这种拼装形态。
        ///
        /// 【为什么只收单词】判据必须是「**有且只有一种含义**」。
        ///   `^*([A-Za-z][A-Za-z .'\-]{0,24})*$` 那种宽兜底会把
        ///   `*made save vs. stun*` 之类的**整句**也拆开乱翻 ⇒ 反而制造中英夹生。
        ///   ⇒ **单词**（无空格）才走这张表，且词必须在表里。
        /// 【与精确表的关系】精确表命中优先；这里只补精确表**收不到的拼装形态**。
        /// </summary>
        private static readonly Dictionary<string, string> WORD =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 状态形容词（`Effects.*` 的 `Adjective` 字段值 —— 就是 `*X*` 里的那个 X）
            { "stuck",       "卡住" },
            { "crippled",    "残废" },
            { "overburdened", "负重过度" },
            { "paralyzed",   "瘫痪" },
            { "stunned",     "晕眩" },
            { "suppressed",  "被压制" },
            { "hobbled",     "腿脚受伤" },
            { "resisted",    "抵抗成功" },
            { "scintillating", "闪烁" },
            { "dazed",       "眩晕" },
            { "exhausted",   "力竭" },
            // ⚠ 刻意**不收** `flying` / `prone` / `stunned` 以外的多词形态：
            //   多词一律走精确表（`*knocked prone*` 等），避免误拆。
        };

        /// <summary>`*&lt;单词&gt;*` 形态。</summary>
        private static readonly System.Text.RegularExpressions.Regex STAR_SINGLE =
            new System.Text.RegularExpressions.Regex(@"^\*([A-Za-z][A-Za-z'\-]*)\*$");

        /// <summary>浮字串 → 中文；**未命中返回 null**（调用方保留原文）。</summary>
        private static string Look(string s)
        {
            string core = Strip(s);
            // ① 精确表
            if (T.TryGetValue(core, out string zh)) return zh;
            // ② 顺序模板（带变量，如 `*<名> broken*`）
            for (int i = 0; i < PAT.Length; i++)
            {
                System.Text.RegularExpressions.Match m = PAT[i].Rx.Match(core);
                if (m.Success) return PAT[i].Zh.Replace("{0}", m.Groups[1].Value);
            }
            // ③ 逐词兜底（只认 `*单词*`；词必须在 `WORD` 表里）
            System.Text.RegularExpressions.Match sm = STAR_SINGLE.Match(core);
            if (sm.Success && WORD.TryGetValue(sm.Groups[1].Value, out string wz))
                return "*" + wz + "*";
            return null;
        }

        /// <summary>
        /// 未命中的 `*…*` 星号壳浮字 ⇒ 记一次日志（**上限 60 条**）。
        /// 为什么只记星号壳：那才是「状态/战斗浮字」的形态（游戏自己的约定）；
        /// 伤害数字、色码、块字符都不是。
        /// </summary>
        private static void NoteUnmatched(string s)
        {
            string core = Strip(s);
            if (!STAR.IsMatch(core)) return;
            if (_unmatched >= 60) return;
            _unmatched++;
            try
            {
                UnityEngine.Debug.Log(Tag + "★未收录浮字（请回填精确条目）：[" + core + "]");
            }
            catch { }
        }

        private static int _unmatched;

        /// <summary>供离线自证用：浮字串 → 中文；未命中返回 null。</summary>
        internal static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s) || !HasLetter(s)) return null;
            return Look(s);
        }
    }
}
