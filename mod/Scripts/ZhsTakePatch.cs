// ═══════════════════════════════════════════════════════════════════════════
// 物品拿取 message log 的部件接管
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】（截图实证）
//     Inventory take from direction
//       EN = '=subject.Does:take= =object.the.name= =subject.from.its.direction#direction=.'
//       ZH = '=subject.Does:take=从=subject.from.its.direction#direction=拿出了=object.the.name=。'
//     实际输出：`You take来自西北方向拿出了沥青与阴影。`
//
//   ⇒ `=subject.Does:take=` **没有**被替换成中文。
//
// 【已查明】
//   * 生产函数确实存在：
//       XRL.World.Text.Delegates.GameObjectReplacers::Does(VariableContext, GameObject)
//       XRL.World.Text.Delegates.GameObjectReplacers::Does(VariableContext, GameObject, String Verb)
//       XRL.World.Text.Delegates.GameObjectReplacers::DoesAdverbially(VariableContext, GameObject)
//   * 已有的 `ZhsVerbPatch` 用 `[HarmonyPatch]` 属性式挂载这三个方法，
//     但 **Player.log 里没有任何挂钩记录**（它本应在首次调用时打日志），
//     怀疑属性式挂载在 0.40 上没生效。
//   * 本类改走 `ZhsBootstrap` 里的**显式 TryApply()** 路径（该路径已有成功先例：
//     ZhsCookingPatch 打印「已挂 4 个补丁」），并把结果写进日志便于核对。
//
// 【顺带修正措辞】
//   用户要求：take 可从地上 / 桌上 / 箱子里拿，中文统一用「拿走」，
//   不用「拿出」（后者限定为「从容器内取出」，语义过窄）。
//     * 模板译文：`…里拿出了…` → `…里拿到了…`
//     * 动词词条：`take` → 「拿走」
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsTakePatch
    {
        private const string Tag = "[QudZhsCookbook] 拿取接管：";

        private static bool _applied;
        private static int _calls;
        private static int _hits;

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType("XRL.World.Text.Delegates.GameObjectReplacers", false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 GameObjectReplacers");
                return;
            }

            MethodInfo post = typeof(ZhsTakePatch).GetMethod("Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (post == null) return;

            int n = 0;
            Harmony h = new Harmony("Nattens.QudZhsCookbook.take");
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static | BindingFlags.Instance))
            {
                if (mi.ReturnType != typeof(string)) continue;
                // 只挂动词变形出口（Does / DoesAdverbially / ItDoes）
                if (mi.Name != "Does" && mi.Name != "DoesAdverbially"
                    && mi.Name != "ItDoes") continue;
                if (mi.ContainsGenericParameters) continue;
                try
                {
                    h.Patch(mi, postfix: new HarmonyMethod(post));
                    n++;
                    UnityEngine.Debug.Log(Tag + "已挂 " + mi.Name + "("
                        + mi.GetParameters().Length + " 参)");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(Tag + "挂 " + mi.Name + " 失败：" + e.Message);
                }
            }
            UnityEngine.Debug.Log(Tag + "共挂 " + n + " 个动词出口");
            TryApplyPronouns();
        }

        /// <summary>
        /// `Does` 的返回值形如 "take" / "takes" / "You take" / "You takes"。
        /// 我们把它整体换成中文动词；若整体不认得，再尝试拆出动词部分。
        /// </summary>
        /// <summary>
        /// `Does` 的返回值形如 "take" / "takes" / "You take" / "You takes"。
        /// 有两类问题（截图实证）：
        ///   ① 截图 `你拿走从your waterskin那里…`：模板是
        ///        `Stomach sip message with source` → `=subject.Does:take=从=object…=那里啜了一口清水。`
        ///      方向版（`take from direction`）替换成功、来源版失败 ⇒ 重载没全覆盖。
        ///   ② 截图 `Youare现在是 6 级！`：模板是
        ///        `LevelGainDetailed` → `=subject.Does:have=升了一级！=subject.They==subject.is=现在是…`
        ///      `=subject.They=` 返回 "You"、`=subject.is=` 返回 "are"，
        ///      直接拼接成 "Youare"（**缺空格**）。
        ///
        ///  所以这里做两件事：
        ///    * 用 ZhsVerbZh.Apply 把动词/代词性动词换成中文
        ///    * 兜底：把「英文字母紧跟英文字母」的可疑拼接**补一个空格**，
        ///      至少不再粘连（"Youare" → "You are"）
        /// </summary>
        private static void Postfix(object __0, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                _calls++;
                if (_calls <= 8)
                {
                    UnityEngine.Debug.Log(Tag + "Does 挂钩 #" + _calls
                        + " 原返='" + __result + "'");
                }

                string verb = null;
                try
                {
                    object pars = AccessTools.Property(__0?.GetType(), "Parameters")
                        ?.GetValue(__0, null);
                    if (pars is IList list && list.Count > 0) verb = list[0] as string;
                }
                catch { }

                string zh = ZhsVerbZh.Apply(__result, verb);
                if (!ReferenceEquals(zh, __result) && zh != __result)
                {
                    _hits++;
                    __result = zh;
                    return;
                }

                // 代词性动词兜底：=subject.is= / =subject.They= 之类会各自返回
                // "are" / "You"，拼接后没有分隔。这里只做「补空格」，不改语义。
                __result = SpaceOut(__result);
            }
            catch { }
        }

        /// <summary>在「小写字母紧跟大写字母」处补空格（Youare → You are）。</summary>
        private static string SpaceOut(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return s;
            // 只处理**纯英文短串**（代词/动词），避免误伤中文与标记
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) return s;
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && char.IsLower(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：调用 " + _calls + " 次，替换 " + _hits + " 次");
        }

        // ────────────────────────────────────────────────────────────────
        // 代词性动词 / 主语代词的接管
        //
        // 截图实证（`LevelGainDetailed` 模板）：
        //   `=subject.Does:have=升了一级！=subject.They==subject.is=现在是 {{C|=level=}} 级！`
        //   游戏输出：`你有升了一级！Youare现在是 6 级！`
        //   ⇒ `=subject.They=` 返回 "You"、`=subject.is=` 返回 "are"，
        //     且**两者之间没有空格**，拼成 "Youare"。
        //
        // 这里给 `They` / `It` 这类主语代词挂 Postfix，把**玩家视角**的英文代词换中文。
        // 只处理「返回值恰是已知英文代词」的场合，异常全吞。
        // ────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> Pronouns =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "You", "你" },       // 你
            { "you", "你" },
            { "They", "他们" },// 他们
            { "they", "他们" },
            { "It", "它" },        // 它
            { "it", "它" },
            { "He", "他" },        // 他
            { "he", "他" },
            { "She", "她" },       // 她
            { "she", "她" },
            { "We", "我们" },  // 我们
            { "we", "我们" },
        };

        private static int _pronHits;

        public static void TryApplyPronouns()
        {
            Type t = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType("XRL.World.Text.Delegates.GameObjectReplacers", false);
                    if (t != null) break;
                }
                catch { }
            }
            if (t == null) return;

            MethodInfo post = typeof(ZhsTakePatch).GetMethod("PronounPostfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (post == null) return;

            string[] want = { "They", "It", "He", "She", "We", "Subjective" };
            int n = 0;
            Harmony h = new Harmony("Nattens.QudZhsCookbook.pronoun");
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static | BindingFlags.Instance))
            {
                if (mi.ReturnType != typeof(string)) continue;
                if (Array.IndexOf(want, mi.Name) < 0) continue;
                if (mi.ContainsGenericParameters) continue;
                try { h.Patch(mi, postfix: new HarmonyMethod(post)); n++; }
                catch { }
            }
            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个主语代词出口");
        }

        private static void PronounPostfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                string zh;
                if (Pronouns.TryGetValue(__result.Trim(), out zh))
                {
                    _pronHits++;
                    if (_pronHits <= 20)
                    {
                        UnityEngine.Debug.Log(Tag + "代词替换：" + __result + " => " + zh);
                    }
                    __result = zh;
                }
            }
            catch { }
        }
    }
}
