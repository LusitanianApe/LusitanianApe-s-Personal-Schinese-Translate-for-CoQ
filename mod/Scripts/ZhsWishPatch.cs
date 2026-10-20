// ═══════════════════════════════════════════════════════════════════════════
// wish（愿望）界面汉化
// ═══════════════════════════════════════════════════════════════════════════
//
// 【取证：反编译 XRL.Wish.WishManager】
//
//   [WishCommand(null, null, Command = "wish")]
//   public static void AskWish()
//   {
//       string text = Popup.AskString("Your wish is my command!", "", null, null,
//                                     null, 999, 0, ReturnNullForEscape: false,
//                                     EscapeNonMarkupFormatting: true, false);
//       …
//   }
//
//   ⇒ **硬编码字面量**，直接传给 `Popup.AskString`。
//     字符串表（Strings.xml）改不到它 —— 我方此前**完全没有覆盖**。
//     （截图实证：wish 弹窗提示词为英文；但按钮「提交 / [Esc] 取消」已是中文，
//       说明按钮走的是别的层。）
//
//   WishManager 的全部英文字面量（decomp 全量）：
//       'Your wish is my command!'      ← ★ 唯一的用户可见文案
//       'Wish:'                         ← MetricsManager 埋点，非 UI
//       'Loading wish commands'         ← 日志
//       'WishCommand ' / 'WishManager.Command: …'  ← 调试
//
//   ※ 我方的 `ZhsJournalNotePatch` 兜底补丁挂了 `Popup.Show`，
//     但 `AskString` **是另一个方法** ⇒ 碰不到，所以必须单独挂。
//
// 【做法】
//   挂 `XRL.UI.Popup.AskString` 的 Postfix，改**第 1 个 string 参数**（`ref __0`）。
//   与 `ZhsJournalNotePatch` 的兜底同风格：静态方法、按序号传参、
//   只在精确命中时才改（`You're lost` 那次的教训：宁可漏，不可错）。
//
//   开关文件 `_zhs_wish_off.txt` 可停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsWishText
    {
        public static bool Disabled;
        private static int _calls, _fixed;

        /// <summary>
        /// 精确匹配表：英文 → 中文。
        ///
        /// ★ 来源：decomp 全量扫描 `Popup.AskString` / `AskNumber` 的调用点，
        ///   抽出 13 条硬编码提示词。**这些全部不经字符串表**，
        ///   所以只能在这里按精确串替换。
        ///
        /// ★ 带 `Type 'XXX' to confirm` 的确认框：**必须保留那个英文关键词**，
        ///   因为玩家要照着敲（区分大小写）⇒ 中文提示里原样保留 `'ASCEND'` 等。
        /// </summary>
        private static readonly (string En, string Zh)[] Exact =
        {
            // ── 调试 / 开发 ──
            ("Your wish is my command!", "你的愿望就是我的命令！"),
            ("Enter text to filter inventory by item name.",
             "输入要用来筛选物品栏的文本。"),
            ("Entry text", "输入文本"),
            ("Wish:", "愿望："),

            // ── 重命名 / 兑换码 ──
            ("Enter a new name for ", "为以下对象输入新名字："),
            ("Redeem a Code", "兑换代码"),

            // ── 结局确认（★ 保留英文确认词，玩家要照着敲）──
            ("End game?", "结束游戏？"),
            ("Launch spaceship and end game?", "发射飞船并结束游戏？"),
            ("Are you sure you want to return to Qud? \n\nType 'RETURN' to confirm.",
             "你确定要回到卡德（Qud）吗？\n\n输入 'RETURN' 以确认。"),
            ("Are you sure you want to return to Qud? \\n\\nType 'RETURN' to confirm.",
             "你确定要回到卡德（Qud）吗？\\n\\n输入 'RETURN' 以确认。"),
            ("Are you sure you want to ascend the Spindle? You know of no means to descend again.\\n\\nType 'ASCEND' to confirm.",
             "你确定要攀上纺锤（the Spindle）吗？你知道没有任何办法能再下来。\\n\\n输入 'ASCEND' 以确认。"),
            ("Are you sure you want to ascend the Spindle? You know of no means to descend again.\n\nType 'ASCEND' to confirm.",
             "你确定要攀上纺锤（the Spindle）吗？你知道没有任何办法能再下来。\n\n输入 'ASCEND' 以确认。"),
            ("Crossing into Brightsheol will retire your character. Are you sure you want to do it? Type 'CROSS' to confirm.",
             "踏入明暗界（Brightsheol）将使你的角色退役。你确定要这么做吗？输入 'CROSS' 以确认。"),

            // ── ★★ 2026-10-17 补：另两条防呆确认框（此前**整句原样放行**，提示一直是英文）──
            //   取值来源（`literal-owners.tsv` + 反编译）：
            //     XRL.Core.XRLCore::PlayerTurn                "Core Abandon Prompt" | "ABANDON"
            //     XRL.CheckpointingSystem::ShowDeathMessage   "CheckpointingSystem RETIRE" | "RETIRE"
            //   两条都是 `Popup.AskString(整句, …, ExpectedInput: "ABANDON"/"RETIRE", …)`
            //   ⇒ 提示是**拼接/字面量**、不经字符串表 ⇒ 只能在这里按精确串换。
            //   ★ 关键词照旧原样保留（玩家要照着敲，区分大小写）。
            ("If you quit without saving, you will lose all your unsaved progress. Are you sure you want to QUIT and LOSE YOUR PROGRESS?\\n\\nType 'ABANDON' to confirm.",
             "若你不存档就退出，将失去所有未保存的进度。确定要退出并放弃进度吗？\\n\\n输入 'ABANDON' 以确认。"),
            ("If you quit without saving, you will lose all your unsaved progress. Are you sure you want to QUIT and LOSE YOUR PROGRESS?\n\nType 'ABANDON' to confirm.",
             "若你不存档就退出，将失去所有未保存的进度。确定要退出并放弃进度吗？\n\n输入 'ABANDON' 以确认。"),
            ("If you quit without saving, you will lose all your progress and your character will be lost. Are you sure you want to QUIT and LOSE YOUR PROGRESS?\\n\\nType 'ABANDON' to confirm.",
             "若你不存档就退出，将失去全部进度，角色也会丢失。确定要退出并放弃进度吗？\\n\\n输入 'ABANDON' 以确认。"),
            ("If you quit without saving, you will lose all your progress and your character will be lost. Are you sure you want to QUIT and LOSE YOUR PROGRESS?\n\nType 'ABANDON' to confirm.",
             "若你不存档就退出，将失去全部进度，角色也会丢失。确定要退出并放弃进度吗？\n\n输入 'ABANDON' 以确认。"),
            ("Type 'RETIRE' to confirm.",
             "输入 'RETIRE' 以确认。"),

            // ── 其它确认 ──
            ("For how many rounds would you like to set the timer? (max=10)",
             "计时器设多少回合？（上限 10）"),
            ("The sealing mechanisms inside this sarcophagus will certainly kill you if you close ",
             "如果你合上这具石棺，里面的封闭机关一定会杀死"),

            // ══════════════════════════════════════════════════════════════
            // ★★ 以下 10 条来自 `XRLCore::PlayerTurn`（Esc 系统菜单流程）
            //
            //   取证（反编译，逐个 `Popup.*` 调用）：
            //     Popup.Show("You can only set your checkpoint in settlements.");
            //     Popup.Show("You can only restore your checkpoint outside settlements.");
            //     Popup.ShowYesNoCancel("Are you sure you want to restore your checkpoint?")
            //     Popup.ShowYesNoCancel("Are you sure you want to go to the world map?")
            //     Popup.ShowYesNoCancel("Would you like to walk to the nearest stairway up?")
            //     Popup.ShowYesNoCancel("Would you like to walk to the nearest stairway down?")
            //     Popup.ShowYesNoCancel("Are you sure you want to save and quit?")
            //     Popup.ShowYesNoCancel("Do you want to save first?")
            //     Popup.PickOption("Go to which point of interest?", …)
            //     Popup.PickOption("Select a destination", …)
            //
            //   ⇒ **全部是字面量，没有 `_T()`/`_S()` Context** ⇒ 表里改不到，只能在这里换。
            //   ※ 对照：同文件里走 `_T()` 的那些（Core Saved Notify / Core Quick Save /
            //     Core Load Quick Save / Core Move Command Popup Title / Core Quit Text /
            //     Core Abandon Text）**我方 Strings.xml 里都已收录**，无需在此重复。
            // ══════════════════════════════════════════════════════════════
            ("You can only set your checkpoint in settlements.",
             "你只能在聚落里设置检查点。"),
            ("You can only restore your checkpoint outside settlements.",
             "你只能在聚落外恢复检查点。"),
            ("Are you sure you want to restore your checkpoint?",
             "确定要恢复检查点吗？"),
            ("Are you sure you want to go to the world map?",
             "确定要前往世界地图吗？"),
            ("Would you like to walk to the nearest stairway up?",
             "要自动走向最近的向上阶梯吗？"),
            ("Would you like to walk to the nearest stairway down?",
             "要自动走向最近的向下阶梯吗？"),
            ("Are you sure you want to save and quit?",
             "确定要保存并退出吗？"),
            ("Do you want to save first?",
             "要先保存吗？"),
            ("Go to which point of interest?",
             "前往哪个兴趣点？"),
            ("Select a destination",
             "选择目的地"),
        };

        public static string Apply(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            _calls++;
            try
            {
                for (int i = 0; i < Exact.Length; i++)
                {
                    if (s == Exact[i].En)
                    {
                        _fixed++;
                        return Exact[i].Zh;
                    }
                }
                // 兜底：前缀式（"Wish: xxx"）
                if (s.StartsWith("Wish: ", StringComparison.Ordinal))
                {
                    _fixed++;
                    return "愿望：" + s.Substring(6);
                }
            }
            catch { }
            return s;
        }

        public static string Stats()
        {
            return "wish 文案：" + _calls + " 次调用 / " + _fixed + " 次替换";
        }
    }

    /// <summary>
    /// 挂 `XRL.UI.Popup.AskString(…)` —— wish 弹窗的提示词出口。
    /// 静态方法，只改第 1 个参数。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsWishPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("XRL.UI.Popup");
            if (t == null) return null;
            System.Reflection.MethodBase best = null;
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "AskString") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    // 取参数最少的那一个（最可能是主重载）
                    if (best == null || ps.Length < best.GetParameters().Length)
                        best = m;
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
                UnityEngine.Debug.Log("[QudZhsCookbook] wish 补丁 Prepare：AskString="
                    + (m == null ? "❌ null"
                                 : "✅ " + m.DeclaringType.FullName + "::" + m.Name
                                   + " 参数 " + m.GetParameters().Length
                                   + " 静态 " + (m.IsStatic ? "是" : "否")));
                return m != null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] wish 补丁 Prepare 异常：" + e);
                return false;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(ref string __0)
        {
            string before = __0;
            ZhsHookTrace.Step("Wish.in", before);
            __0 = ZhsWishText.Apply(__0);
            ZhsHookTrace.Step("Wish.post", __0);
            if (!ReferenceEquals(before, __0) && before != __0)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] wish 文案替换：" + before
                    + " => " + __0);
            }
        }
    }
}
