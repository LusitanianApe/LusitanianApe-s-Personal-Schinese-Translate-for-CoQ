// ═══════════════════════════════════════════════════════════════════════════
// Esc 主菜单汉化（系统菜单）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么需要它】反编译 `XRL.Core.XRLCore::PlayerTurn` 的 `case "CmdSystemMenu"`：
//     string text11 = flag5 ? Strings._S("Core Quit Text", "Quit Without Saving")
//                           : Strings._S("Core Abandon Text", "Abandon Character");
//     if (transient) options = new string[4] { "Control Mapping", "Options", "Game Info", "End Test" };
//     else if (flag5) {
//         if (CheckpointingSystem.IsPlayerInCheckpoint())
//             options = new string[7] { "Set Checkpoint", "&KRestore Checkpoint",
//                 "Control Mapping", "Options", "Game Info", "Save and Quit", text11 };
//         else
//             options = new string[7] { "&KSet Checkpoint", "Restore Checkpoint", … };
//     } else
//         options = new string[5] { "Control Mapping", "Options", "Game Info", "Save and Quit", text11 };
//     num9 = Popup.PickOption("", null, "", "Sounds/UI/ui_notification", options, hotkeys, …);
//
//   ⇒ **只有 `text11` 走字符串表**（故只有「不存档退出」显示中文），
//     前 6 项是**字面量硬编码**，任何 XML 都改不到它。
//
// 【做法】挂 `Popup.PickOption` 的 Prefix，把 Options 列表里**精确等于**
//   我们列举的英文标签的项换成中文；保留 `&X` 热键标记前缀。
//   只替换白名单内的字面量 ⇒ 不影响 AutoAct 或其它 Popup。
//
// 开关：装机目录放 `_zhs_sysmenu_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsSystemMenuPatch
    {
        private const string Tag = "[QudZhsCookbook] 系统菜单：";

        internal static bool Disabled;
        private static bool _applied;
        private static int _hits;
        private static int _warned;

        /// <summary>硬编码标签 → 中文（键为**已剥掉 `&X` 标记**的形式）。</summary>
        private static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Set Checkpoint",     "\u8bbe\u7f6e\u68c0\u67e5\u70b9" },       // 设置检查点
            { "Restore Checkpoint", "\u6062\u590d\u68c0\u67e5\u70b9" },       // 恢复检查点
            { "Control Mapping",    "\u6309\u952e\u6620\u5c04" },             // 按键映射
            { "Options",            "\u9009\u9879" },                         // 选项
            { "Game Info",          "\u6e38\u620f\u4fe1\u606f" },             // 游戏信息
            { "Save and Quit",      "\u4fdd\u5b58\u5e76\u9000\u51fa" },       // 保存并退出
            { "End Test",           "\u7ed3\u675f\u6d4b\u8bd5" },             // 结束测试
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type t = typeof(XRL.UI.Popup);
                MethodInfo pre = typeof(ZhsSystemMenuPatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                                                      | BindingFlags.NonPublic
                                                      | BindingFlags.Static))
                {
                    if (m.Name != "PickOption") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    // 找那个带 IReadOnlyList<string> Options 的重载
                    bool has = false;
                    foreach (ParameterInfo pi in ps)
                        if (pi.Name == "Options"
                            && pi.ParameterType.Name.StartsWith("IReadOnlyList"))
                        { has = true; break; }
                    if (!has) continue;
                    var sig = new System.Text.StringBuilder();
                    foreach (ParameterInfo pi in ps)
                        sig.Append('_').Append(pi.ParameterType.Name);
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.sysmenu" + sig);
                    h.Patch(m, prefix: new HarmonyMethod(pre));
                    n++;
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 PickOption 出口（硬编码标签 " + L.Count + " 条）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// Prefix：把 Options 里的硬编码英文标签换成中文。
        ///
        /// ★★ 2.16.33 修掉的真 bug（2.16.32 探针实证）：
        ///   原来签名是 `Prefix(ref IReadOnlyList<string> __4)` + `__4 = neo;`
        ///   ⇒ **`ref` 对接口类型参数未回写**：替换后复读 `__4` 是中文，
        ///     但同一调用的 `__args[4]` 在另一个 Prefix 里读到的**仍是原文**
        ///     （实测 `[&KSet Checkpoint][Restore Checkpoint]…`）。
        ///   ⇒ 改为 `object[] __args` + **就地改元素**（`string[]` / `IList<string>`
        ///     都能直接写），与 `ZhsPopupPatch.RewriteList` 同款、已被证明有效的路子。
        /// </summary>
        private static void Prefix(object[] __args)
        {
            if (Disabled || __args == null || __args.Length < 5) return;
            try
            {
                object list = __args[4];
                if (list == null) return;
                int changed = 0;

                string[] arr = list as string[];
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string zh = MapOne(arr[i]);
                        if (zh == null) continue;
                        arr[i] = zh;                    // ★ 就地写
                        changed++;
                    }
                }
                else
                {
                    System.Collections.Generic.IList<string> il =
                        list as System.Collections.Generic.IList<string>;
                    if (il == null)
                    {
                        if (_warned < 3)
                        {
                            _warned++;
                            UnityEngine.Debug.LogWarning(Tag + "选项列表既非 string[] 也非 IList<string>（"
                                + list.GetType().FullName + "）⇒ 跳过");
                        }
                        return;
                    }
                    for (int i = 0; i < il.Count; i++)
                    {
                        string zh = MapOne(il[i]);
                        if (zh == null) continue;
                        il[i] = zh;                     // ★ 就地写
                        changed++;
                    }
                }

                if (changed == 0) return;
                _hits++;
                if (_hits <= 2)
                {
                    // ★ 运行期实证：**从参数数组里再读一遍**，证明这次真的写进去了
                    var sb = new System.Text.StringBuilder(Tag + "就地写入后复读 __args[4]（第 " + _hits + " 次）：");
                    var back = __args[4] as System.Collections.Generic.IList<string>;
                    if (back != null)
                        for (int i = 0; i < back.Count && i < 8; i++)
                            sb.Append('[').Append(back[i]).Append(']');
                    UnityEngine.Debug.Log(sb.ToString());
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "替换出错（忽略）：" + e.Message);
            }
        }

        /// <summary>
        /// 单个标签映射：保留 `&X` 热键标记，只换后面的文本。
        /// 返回 null 表示「不在白名单里，别动」。
        /// </summary>
        private static string MapOne(string raw)
        {
            string marks = "";
            string body = raw;
            // 剥掉开头的连续 `&X` 标记（如 `&K`）
            int i = 0;
            while (i + 1 < body.Length && body[i] == '&')
            {
                marks += body.Substring(i, 2);
                i += 2;
            }
            body = body.Substring(i);
            string zh;
            if (!L.TryGetValue(body.Trim(), out zh)) return null;
            // ★ **丢掉 `marks`**：反编译 `Popup.GetPopupOption`：
            //      result.text = "{{W|[" + c + "]}} {{y|" + Options[Index] + "}}";
            //   热键已由 `[k]` 显示，`&K` 是冗余的且会被字面拼进文本
            //   （实测截图显示 `&K还原检查点`）。
            return zh;
        }
    }
}
