// ═══════════════════════════════════════════════════════════════════════════
// 任务通知的中文接管（硬编码字符串的部件拦截）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么必须用部件接管】
//   反编译 `XRL.World.Quest`（工具：_tools/decomp，ICSharpCode.Decompiler）：
//
//       public void ShowStartPopup()
//           => Popup.Show("You have received a new quest, " + DisplayName + "!");
//
//       public void ShowFinishPopup()
//           => Popup.Show("You have completed the quest " + DisplayName + "!");
//
//       public void ShowFailPopup()
//           => Popup.Show("You have failed the quest " + DisplayName + "!");
//
//       public void ShowFinishStepPopup(QuestStep Step)
//       {
//           string text = "You have finished the step, {{G|" + Step.Name
//                       + "}}, of the quest " + DisplayName + "!";
//           ...
//           MessageQueue.AddPlayerMessage(text);
//       }
//
//   ⇒ 英文骨架是 **C# 里硬编码的拼接**，拼接后不存在完整字符串，
//     所以「字符串表」与「DLL 字面量搜索」两条路全部失效。
//     这解释了为什么任务通知一直是英文（不是漏译，是**源文本不可翻译**）。
//
// 【验证手段（调用栈实证）】
//   `_zhs_msgtrace.log` 抓到：
//       XRL.World.Quest::ShowFinishStepPopup   ← "You have finished the step, …"
//       XRL.UI.Popup::Show                     ← "You have completed the quest …"
//
// 【做法】
//   挂这些方法的 **Postfix**（它们返回 void，所以用 `__args` 拿不到结果）——
//   改用 **Transpiler 风险太大**，这里用更稳的办法：
//   挂 `Popup.Show(string)` 与 `MessageQueue.AddPlayerMessage(string)` 的 **Prefix**，
//   在**入口处**把英文骨架换成中文（保留 `{{G|…}}` 标记与原有的名字变量）。
//
//   好处：一处拦截覆盖所有 Popup/消息；不改游戏逻辑。
//   安全：只在**匹配到已知英文骨架**时替换；异常全吞。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsQuestTextPatch
    {
        private const string Tag = "[QudZhsCookbook] 任务通知接管：";

        private static bool _applied;
        private static int _hits;

        /// <summary>
        /// 英文骨架 → 中文骨架。
        /// 用**正则**匹配，保留名字与 {{…}} 标记。
        /// </summary>
        private static readonly List<KeyValuePair<Regex, string>> Rules =
            new List<KeyValuePair<Regex, string>>
        {
            // You have received a new quest, X!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have received a new quest,\s*(.+?)!$", RegexOptions.Singleline),
                "\u4f60\u63a5\u5230\u4e86\u65b0\u4efb\u52a1\uff1a$1\uff01"),          // 你接到了新任务：X！

            // You have completed the quest X!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have completed the quest\s+(.+?)!$", RegexOptions.Singleline),
                "\u4f60\u5df2\u5b8c\u6210\u4efb\u52a1\uff1a$1\uff01"),                // 你已完成任务：X！

            // You have failed the quest X!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have failed the quest\s+(.+?)!$", RegexOptions.Singleline),
                "\u4efb\u52a1\u5931\u8d25\uff1a$1\u3002"),                             // 任务失败：X。

            // You have finished the step, X, of the quest Y!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have finished the step,\s*(.+?),\s*of the quest\s+(.+?)!$",
                          RegexOptions.Singleline),
                "\u4efb\u52a1\u6b65\u9aa4\u5b8c\u6210\uff1a$1\uff08\u4efb\u52a1\uff1a$2\uff09\uff01"), // 任务步骤完成：X（任务：Y）！

            // You have failed the step, X, of the quest Y!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have failed the step,\s*(.+?),\s*of the quest\s+(.+?)!$",
                          RegexOptions.Singleline),
                "\u4efb\u52a1\u6b65\u9aa4\u5931\u8d25\uff1a$1\uff08\u4efb\u52a1\uff1a$2\uff09\u3002"), // 任务步骤失败：X（任务：Y）。

            // ★ 饥饿 / 枯萎 状态弹窗（硬编码在 XRL.World.Effects.Famished::Apply）
            //   Popup.Show("You are famished! You'll act more slowly until you eat again.");
            //   Popup.Show("You have wilted! You'll move and regenerate slower until you eat or bask in the sunlight again.");
            new KeyValuePair<Regex, string>(
                new Regex(@"^You are famished!\s*You'll act more slowly until you eat again\.$",
                          RegexOptions.Singleline),
                "\u4f60\u997f\u5f97\u53d1\u614c\uff01\u5728\u4e0b\u6b21\u8fdb\u98df\u524d\uff0c\u4f60\u7684\u884c\u52a8\u4f1a\u66f4\u6162\u3002"),   // 你饿得发慌！在下次进食前，你的行动会更慢。
            new KeyValuePair<Regex, string>(
                new Regex(@"^You have wilted!\s*You'll move and regenerate slower until you eat or bask in the sunlight again\.$",
                          RegexOptions.Singleline),
                "\u4f60\u67af\u840e\u4e86\uff01\u5728\u4e0b\u6b21\u8fdb\u98df\u6216\u518d\u6c90\u9633\u5149\u4e4b\u524d\uff0c\u4f60\u7684\u79fb\u52a8\u4e0e\u518d\u751f\u4f1a\u66f4\u6162\u3002"),   // 你枯萎了！在下次进食或再沐阳光之前，你的移动与再生会更慢。
            // You gain N XP!
            new KeyValuePair<Regex, string>(
                new Regex(@"^You gain\s+(.+?)\s+XP!$", RegexOptions.Singleline),
                "\u4f60\u83b7\u5f97\u4e86 $1 \u7ecf\u9a8c\uff01"),                    // 你获得了 N 经验！

            // ★ 2026-10-18（#13）：弹窗里**带颜色标记**的形态
            //   `You gain {{C|250}} XP!` / `You gained {{C|3}} SP!`（片段拼接后）
            new KeyValuePair<Regex, string>(
                new Regex(@"^You gain\s+\{\{C\|(.+?)\}\}\s+XP!$", RegexOptions.Singleline),
                "\u4f60\u83b7\u5f97\u4e86 {{C|$1}} \u70b9\u7ecf\u9a8c\uff01"),        // 你获得了 C{N} 点经验！
            new KeyValuePair<Regex, string>(
                new Regex(@"^You gained\s+\{\{C\|(.+?)\}\}\s+XP!$", RegexOptions.Singleline),
                "\u4f60\u83b7\u5f97\u4e86 {{C|$1}} \u70b9\u7ecf\u9a8c\uff01"),
            new KeyValuePair<Regex, string>(
                new Regex(@"^You gain\s+\{\{C\|(.+?)\}\}\s+SP!$", RegexOptions.Singleline),
                "\u4f60\u83b7\u5f97\u4e86 {{C|$1}} \u70b9\u6280\u80fd\u70b9\uff01"),  // 你获得了 C{N} 点技能点！
            new KeyValuePair<Regex, string>(
                new Regex(@"^You gained\s+(.+?)\s+XP!$", RegexOptions.Singleline),
                "\u4f60\u83b7\u5f97\u4e86 $1 \u7ecf\u9a8c\uff01"),
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            MethodInfo pre = typeof(ZhsQuestTextPatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (pre == null) return;

            int n = 0;
            // Popup.Show / Popup.ShowBlock
            Type popup = FindType("XRL.UI.Popup");
            if (popup != null) n += PatchFirstString(popup, pre, "Show", "ShowBlock");

            // MessageQueue.AddPlayerMessage
            Type mq = FindType("XRL.Messages.MessageQueue");
            if (mq != null) n += PatchFirstString(mq, pre, "AddPlayerMessage");

            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个入口");
        }

        private static int PatchFirstString(Type t, MethodInfo pre, params string[] names)
        {
            int n = 0;
            Harmony h = new Harmony("Nattens.QudZhsCookbook.questtext." + t.Name);
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static | BindingFlags.Instance))
            {
                if (Array.IndexOf(names, mi.Name) < 0) continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 0) continue;
                if (ps[0].ParameterType != typeof(string)) continue;
                try
                {
                    // ★ 2026-10-16（F1 真根因）：本补丁的 Prefix 声明 `object[] __args`，
                    //   而 `__args` 是**方法入口快照** ⇒ 它回写时会对所有参数槽 `starg`，
                    //   把**它之前**所有 Prefix 的 `ref` 改写**整批回滚**（实证：飞行消息算出中文后被它覆盖回英文）。
                    //   ⇒ 设 `Priority.First` 让它**最先跑**，其回滚发生在所有人写回之前。
                    h.Patch(mi, prefix: new HarmonyMethod(pre) { priority = Priority.First });
                    n++;
                }
                catch { }
            }
            return n;
        }

        private static Type FindType(string full)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(full, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        /// <summary>入口拦截：把英文骨架换成中文。</summary>
        private static void Prefix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;
                string s = __args[0] as string;
                if (string.IsNullOrEmpty(s)) return;

                // ★★ 2026-10-17（测试窗口 #8 / #11）：步骤完成弹窗**必须单独处理**。
                //   `Quest::ShowFinishStepPopup`（反编译）：
                //     text = "You have finished the step, {{G|" + Step.Name + "}}, of the quest "
                //            + DisplayName + "!"
                //     if (Step.XP > 0) Popup.ShowBlock(text + "\nYou gain {{C|" + Step.XP + "}} XP!", …)
                //   · `Step.Name` 是**键**（英文），`QuestReplacers::StepName` 不走这条路径
                //     ⇒ 上面那条通用规则只能搬变量，搬不出中文（#11）
                //   · 旧规则 `(.+?)!$` + Singleline 会把 `\nYou gain 375 XP` **整段吞进 $2**（#8）
                //   ⇒ 这里：① 显式吃掉 XP 行 ② 步骤名走 `ZhsQuestStepZh`（键=官方英文步骤名）
                string fin = FinishStep(s);
                if (fin != null)
                {
                    __args[0] = fin;
                    _hits++;
                    if (_hits <= 20)
                        UnityEngine.Debug.Log(Tag + s + "  =>  " + fin);
                    return;
                }

                foreach (var kv in Rules)
                {
                    Match m = kv.Key.Match(s.Trim());
                    if (!m.Success) continue;
                    string zh = kv.Value;
                    for (int g = 1; g < m.Groups.Count; g++)
                        zh = zh.Replace("$" + g, m.Groups[g].Value);
                    __args[0] = zh;
                    _hits++;
                    if (_hits <= 20)
                        UnityEngine.Debug.Log(Tag + s + "  =>  " + zh);
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// `Quest::ShowFinishStepPopup` 的两行形态（**步骤名走词表**）。
        /// 例：`You have finished the step, {{G|Travel to Grit Gate}}, of the quest
        ///      噪声中的信号!\nYou gain {{C|500}} XP!`
        /// ⇒ `任务步骤完成：前往砂砾门（任务：噪声中的信号）！\n你获得了 500 经验！`
        /// 步骤名查不到（且是英文）⇒ 返回 null（**整句保留英文，不半译**）。
        /// </summary>
        private static string FinishStep(string s)
        {
            Match m = RE_FINISH_STEP.Match(s.Trim());
            if (!m.Success) return null;
            bool finished = m.Groups[1].Value == "finished";
            string step = m.Groups[2].Value.Trim();
            string quest = m.Groups[3].Value.Trim();
            string xp = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;

            string zh = ZhsQuestStepZh.Lookup(step);
            if (zh == null)
            {
                if (!HasCjkText(step)) return null;   // 英文步骤名查不到 ⇒ 不半译
                zh = step;
            }

            string head = finished
                ? "任务步骤完成：" + zh + "（任务：" + quest + "）！"
                : "任务步骤失败：" + zh + "（任务：" + quest + "）。";
            if (!string.IsNullOrEmpty(xp)) head += "\n你获得了 " + xp + " 经验！";
            return head;
        }

        /// <summary>`You have (finished|failed) the step, [{{G|]X[}}], of the quest Y[!\nYou gain {{C|N}} XP]`</summary>
        private static readonly Regex RE_FINISH_STEP = new Regex(
            @"^You have (finished|failed) the step,\s*(?:\{\{G\|)?(.+?)(?:\}\})?,\s*of the quest\s+"
            + @"([^\n!]+?)(?:!\s*\n\s*You gain\s+(?:\{\{C\|)?([^\n]+?)(?:\}\})?\s+XP!|!)$",
            RegexOptions.Compiled);

        private static bool HasCjkText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '\u4e00' && s[i] <= '\u9fff') return true;
            return false;
        }

        public static void Report()
        {
            UnityEngine.Debug.Log(Tag + "统计：替换 " + _hits + " 条");
        }
    }
}
