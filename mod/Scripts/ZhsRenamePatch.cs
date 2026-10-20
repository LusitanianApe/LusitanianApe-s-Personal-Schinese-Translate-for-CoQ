// ═══════════════════════════════════════════════════════════════════════════
// 改名弹窗汉化 —— 挂 `Popup.PickOption` / `Popup.Show` / `Popup.AskString`
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图「严查」】
//     ┌─ Rename yourself ─────────────────────────┐
//     │ > [a] Enter a name for 你自己.              │
//     │   [b] Choose a random name from 你的 culture.│
//     └─ [Esc] 取消 ───────────────────────────────┘
//   ⇒ 只有 `你自己` / `你的` 是中文 —— 那来自 `E.Item.t(…, SecondPerson: flag)`，
//     而**外层骨架是硬编码英文**。
//
// 【反编译实证 `XRL.World.GameObject::HandleRename`】
//     bool flag = E.Item.IsPlayer();
//     string text = E.Item.t(int.MaxValue, …, flag, flag);          // 第二人称 ⇒ 「你自己」
//     list.Add("Enter a name for " + text + ".");
//     list.Add("Choose a random name from " + Grammar.MakePossessive(E.Item.t(…)) + " culture.");
//     list.Add("Choose a random name from your own culture.");
//     Popup.PickOption(flag ? "Rename yourself" : "Rename your companion", …)
//     text2 = Popup.AskString("Enter a new name for " + text + ".", …);
//     Popup.Show("You start calling " + text + " by the name '" + text2 + "'.");
//   ⇒ **6 条运行时拼接骨架**，字符串表一个字都够不着。
//   （`XRL.World.Capabilities.ItemNaming::NameItem` 是物品侧的同类拼接）
//
// 【做法】Prefix 挂 `Popup.PickOption` 的 `IReadOnlyList<string> Options` 参数：
//     · 对每个选项跑「形态明确」的锚定正则，命中就重写成中文语序
//     · 主语（`你自己` / `你的` / 生物名）**原样保留**（§5：玩家自建名不动）
//     · 同时挂 `Popup.Show` / `Popup.AskString` 的第一个字符串参数（同族文案）
//     · 未命中的选项**原样放行** ⇒ 零误伤
//
// 【与 `ZhsSystemMenuPatch` 共存】那个补丁也是 `PickOption` 的 Prefix，
//   但它做的是**整表白名单替换**（Esc 菜单 7 条），与本补丁的**模式重写**互不干扰。
//
// 【安全】`_zhs_rename_off.txt` 可停用；异常一律放行原文。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsRenameZh
    {
        public static bool Disabled;
        internal static int Hits;

        // ── 整句精确（无动态内容）──
        private static readonly Dictionary<string, string> EXACT =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Rename yourself", "为自己改名" },
            { "Rename your companion", "为同伴改名" },
            { "Choose a random name from your own culture.", "从你自己的文化中随机取一个名字。" },
        };

        // ── 带动态内容的骨架（`(?<s>…)` 为主语，原样保留）──
        //
        // ★ `{s}家` 形态：主语（`你的` / `你自己的` / `某人的`）**多以「的」结尾**，
        //   而模板又要接一个「的」⇒ 会产出「你的的」。
        //   ⇒ 统一用 `TrimEnd('的')` 去重，「的」由模板提供。
        //   （离线验证抓到过：`Choose a random name from 你的 culture.`
        //     → 「从你的的文化中…」= 错的）
        private static readonly (Regex Rx, string Zh)[] RULES =
        {
            // `Enter a name for 你自己.` / `Enter a new name for X.`
            (new Regex(@"^Enter a (?:new )?name for (?<s>.+?)\.$", RegexOptions.Compiled),
             "为{s}输入名称。"),
            // `Choose a random name from 你的 culture.`
            (new Regex(@"^Choose a random name from (?<s>.+?) culture\.$", RegexOptions.Compiled),
             "从{s}的文化中随机取一个名字。"),
            // `You start calling 你自己 by the name 'X'.`
            // ⚠ 名字用 `.*`（**贪婪**）：名字里可能含 `'`（实测量子名 `⑨智锤哥`）。
            (new Regex(@"^You start calling (?<s>.+?) by the name '(?<n>.*)'\.$",
                RegexOptions.Compiled), "你开始用「{n}」这个名字称呼{s}。"),
        };

        /// <summary>命中则返回中文；否则 null（调用方保留原文）。</summary>
        internal static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string e;
            if (EXACT.TryGetValue(s, out e)) return e;
            foreach (var (rx, zh) in RULES)
            {
                Match m = rx.Match(s);
                if (!m.Success) continue;
                string subj = m.Groups["s"].Value.Trim();
                if (subj.Length == 0) continue;
                string outZh = zh.Replace("{s}", subj);
                if (m.Groups["n"].Success) outZh = outZh.Replace("{n}", m.Groups["n"].Value);
                // ★ 去重：主语（`你的` / `你自己的`）多以「的」结尾，而模板又接了一个「的」
                //   ⇒ 产出「你的的」。
                //   ⚠ 必须在**全部替换完成之后**再清理：`string.Replace` 是顺序执行的，
                //     若先做 `{s}的`→`X的` 再做 `{s}`→`X`，第二次会在「X的{s}」里
                //     再命中一次 `{s}` ⇒ 结果错乱（离线验证抓到过这个 bug）。
                outZh = outZh.Replace("\u7684\u7684", "\u7684");   // 的的 → 的
                return outZh;
            }
            return null;
        }
    }

    internal static class ZhsRenamePatch
    {
        private const string Tag = "[QudZhsCookbook] 改名弹窗：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsRenameZh.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.UI.Popup", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.UI.Popup");
                    return;
                }
                Harmony h = new Harmony("Nattens.QudZhsCookbook.rename");
                int n = 0;

                // ① PickOption 的 Options 列表（`IReadOnlyList<string>` 参数）
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (mi.Name != "PickOption" && mi.Name != "PickOptionAsync") continue;
                    bool has = false;
                    foreach (ParameterInfo pi in mi.GetParameters())
                    {
                        if (pi.Name == "Options"
                            && pi.ParameterType.Name.StartsWith("IReadOnlyList"))
                        { has = true; break; }
                    }
                    if (!has) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(
                            typeof(ZhsRenamePatch).GetMethod("PrefixOptions",
                                BindingFlags.NonPublic | BindingFlags.Static)));
                        n++;
                    }
                    catch { }
                }

                // ② `Show` / `AskString` 的第一个 string 参数
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (mi.Name != "Show" && mi.Name != "AskString"
                        && mi.Name != "ShowYesNo") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(
                            typeof(ZhsRenamePatch).GetMethod("PrefixText",
                                BindingFlags.NonPublic | BindingFlags.Static)));
                        n++;
                    }
                    catch { }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口"
                    + "（PickOption 选项表 + Show/AskString 文案 · 3 条模式 + 3 条整句）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// 重写 `Options` 列表里命中的项（**就地改元素**，其余原样保留）。
        ///
        /// 【为什么必须是 `object[] __args` 而不是 `ref IReadOnlyList&lt;string&gt;`】
        ///   §49（`_tools\AGENTS-49-ref不回写.md`）：**Harmony 对接口类型的 `ref` 赋值不回写**
        ///   —— 复读自己的引用是新的，而 `__args[4]` 仍是原文 ⇒ 画面照旧英文。
        ///   活例（2026-10-15 用户截图 `Rename yourself`）：
        ///     本方法旧版即 `ref IReadOnlyList&lt;string&gt; __4` ⇒ 日志写「已替换」，
        ///     但弹窗两项**仍是英文**；同局 `Player.log` L5951-5954 的译文其实来自
        ///     `PrefixText`（`Popup.Show` 那条路），不是这里。
        ///   ⇒ 同项目 `ZhsSystemMenuPatch`(`object[] __args`) 与
        ///     `ZhsPopupPatch.PrefixOptions`(`__args[4]` + `RewriteList`) 都是**修好的样式**，
        ///     本补丁是唯一漏改的那个。
        ///
        /// 【判据要求（§49）】改完**从 `__args` 里复读**一条打日志 —— 证明真的写进去了，
        ///   而不是只复读自己的局部变量（那是自证）。
        /// </summary>
        private static void PrefixOptions(object[] __args)
        {
            if (ZhsRenameZh.Disabled || __args == null) return;
            if (__args.Length < 5) return;
            try
            {
                object list = __args[4];
                if (list == null) return;

                // ── 三档兜底：`string[]` → `IList<string>` → 反射找可写索引器 ──
                int fixedCount = 0;
                string firstOld = null, firstNew = null;

                string[] arr = list as string[];
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string zh = ZhsRenameZh.Fix(arr[i]);
                        if (zh == null) continue;
                        if (firstOld == null) { firstOld = arr[i]; firstNew = zh; }
                        arr[i] = zh;                       // ★ 就地写元素
                        fixedCount++;
                    }
                }
                else
                {
                    System.Collections.Generic.IList<string> il =
                        list as System.Collections.Generic.IList<string>;
                    if (il != null)
                    {
                        for (int i = 0; i < il.Count; i++)
                        {
                            string zh = ZhsRenameZh.Fix(il[i]);
                            if (zh == null) continue;
                            if (firstOld == null) { firstOld = il[i]; firstNew = zh; }
                            il[i] = zh;                    // ★ 就地写元素
                            fixedCount++;
                        }
                    }
                    else
                    {
                        // 兜底：反射找**有 setter 的索引器**（`T[]`/`IList<T>` 都失败时才走）
                        System.Reflection.PropertyInfo idx = null;
                        foreach (System.Reflection.PropertyInfo pi in
                                 list.GetType().GetProperties())
                        {
                            System.Reflection.ParameterInfo[] ps = pi.GetIndexParameters();
                            if (ps.Length != 1) continue;
                            if (pi.GetSetMethod() == null) continue;
                            idx = pi;
                            break;
                        }
                        if (idx == null) return;
                        int count = 0;
                        try { count = System.Convert.ToInt32(
                            list.GetType().GetProperty("Count").GetValue(list, null)); }
                        catch { return; }
                        for (int i = 0; i < count; i++)
                        {
                            object cur = idx.GetValue(list, new object[] { i });
                            string cur2 = cur as string;
                            if (cur2 == null) continue;
                            string zh = ZhsRenameZh.Fix(cur2);
                            if (zh == null) continue;
                            if (firstOld == null) { firstOld = cur2; firstNew = zh; }
                            idx.SetValue(list, zh, new object[] { i });
                            fixedCount++;
                        }
                    }
                }

                if (fixedCount == 0) return;
                ZhsRenameZh.Hits += fixedCount;

                // ★★ 复读：**从参数数组里再读一遍**（§49 的硬要求）
                if (ZhsRenameZh.Hits <= 8)
                {
                    string back = ReadBack(__args[4]);
                    UnityEngine.Debug.Log(Tag + "改 Options（" + fixedCount + " 条）"
                        + firstOld + "  =>  " + firstNew
                        + " ｜ ★复读 __args[4] 首项=" + back);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "PrefixOptions 失败：" + e.Message);
            }
        }

        /// <summary>§49 要求的「复读」：从**参数数组**里读回首项，证明真的写进去了。</summary>
        private static string ReadBack(object list)
        {
            try
            {
                string[] arr = list as string[];
                if (arr != null) return arr.Length > 0 ? arr[0] : "(空)";
                System.Collections.Generic.IList<string> il =
                    list as System.Collections.Generic.IList<string>;
                if (il != null) return il.Count > 0 ? il[0] : "(空)";
            }
            catch { }
            return "(读不回)";
        }

        /// <summary>重写弹窗正文。</summary>
        private static void PrefixText(ref string __0)
        {
            if (ZhsRenameZh.Disabled) return;
            try
            {
                string zh = ZhsRenameZh.Fix(__0);
                if (zh == null) return;
                if (ZhsRenameZh.Hits < 8)
                {
                    UnityEngine.Debug.Log(Tag + __0 + "  =>  " + zh);
                }
                ZhsRenameZh.Hits++;
                __0 = zh;
            }
            catch { }
        }
    }
}
