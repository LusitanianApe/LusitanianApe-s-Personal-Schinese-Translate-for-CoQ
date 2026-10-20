// ═══════════════════════════════════════════════════════════════════════════
// 传送目标选择弹窗汉化（`Select a destination` 硬编码）· **安全重写版**
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图：
//     ┌─ Select a destination ──┐
//     │ [a] Current location (C) │
//     │ [b] Center (C)           │
//     └──────────────────────────┘
//   全英文，且**官方数据文件里完全没有这些字符串**（实测 0 命中）。
//
// 【根因（反编译 `XRL.World.GameObject::PullDown(bool)`）】
//     string item = "Current location";                        // ← 硬编码
//     GetDirectionFromCellXY(ZoneX, ZoneY, showCenter: true);  // ← switch 返回 "NW"/"N"/…
//     list.Add((num == 1 && num2 == 1) ? "Center" : "Arrival location");
//     int num3 = Popup.PickOption("Select a destination", null, "", …,
//                                 list.ToArray(), list2.ToArray(), …);
//   ⇒ 数据侧（XML）无法覆盖 ⇒ 只能挂补丁。
//
// ═══════════════════════════════════════════════════════════════════════════
// ★★★ 2026-10-10 重大修正：**旧版会卡死游戏**（已隔离实验坐实）
// ═══════════════════════════════════════════════════════════════════════════
//
//   旧版做法（**已废弃**）：
//     ① 挂 `PullDown(bool)` 的 **Prefix**，在里面 `__originalMethod.Invoke(...)`
//        主动代跑原方法，再 `return false` 跳过原方法；
//     ② 用一个 `[ThreadStatic] _intercept` 窗口，让 `PickOption` 的 Prefix
//        只在"自己代跑的这段"里改参数。
//
//   崩溃/卡死实证：
//     · 现象：**大地图 → 小地图（已加载区块，如 Joppa）时无征兆卡死**
//     · 形态：CPU **完全静止**（15 秒 0.0s 增量）⇒ 死锁/等待，不是死循环
//     · 隔离实验（`_off_<补丁名>.txt` 逐个禁/启）：
//         4 个候选里，**只有本补丁单独启用时卡死** ⇒ 元凶唯一
//     · 同一签名崩溃 11 次：`ntdll.dll · 0xc0000005 · 偏移 0x123ca7`
//
//   病灶（**为什么 Prefix 里代跑原方法必然危险**）：
//     `PullDown` 内部会调用 `Popup.PickOption(...)` —— 那是**阻塞式弹窗**
//     （等玩家按键才返回）。在 Prefix 里**重入**这个"弹窗 + 等待"流程，
//     等于在调用栈中途再开一次模态交互 ⇒ 与游戏的世界地图/区域切换状态机冲突
//     ⇒ 卡死。
//   ⚠ 注意：`_intercept` **本来就是 `[ThreadStatic]`**，复位也用 try/finally ——
//     旧注释里把这当成"已防护"是**误判**：问题不在线程安全，而在**重入模态流程**。
//
//   ⇒ **正确做法：根本不碰 `PullDown`。**
//     只挂 `Popup.PickOption` 的 Prefix，用**标题逐字匹配**识别这个弹窗，
//     就地改 `__args` 的参数。全程：
//       · 不调用任何原方法（零重入）
//       · 不依赖任何全局/线程状态（不需要 `_intercept`）
//       · 不干预返回值/流程（Prefix 返回 void）
//     ⇒ 影响面 = 标题恰好为 `Select a destination` 的那**一个**弹窗。
//
// 开关：装机目录放 `_zhs_pulldown_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>该弹窗的硬编码字符串 ⇒ 中文（精确匹配）。</summary>
    internal static class ZhsPullDown
    {
        public static bool Disabled;

        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Select a destination", "选择目的地" },   // 标题
            { "Current location",     "当前位置" },
            { "Center",               "中心" },
            { "Arrival location",     "抵达地点" },
            // `GetDirectionFromCellXY` 的 switch 返回字母
            { "NW", "西北" }, { "N", "北" },  { "NE", "东北" },
            { "W",  "西"  },                  { "E",  "东"  },
            { "SW", "西南" }, { "S", "南" },  { "SE", "东南" },
        };

        public static int Hits;

        /// <summary>精确查表；未命中原样返回。
        /// 兼容游戏**拼接出来**的两种形态：
        ///   ① `"N, 某地"`            —— 选项 + 备注（游戏拼 `", " + note.GetShortText()`）
        ///   ② `"Current location (C)"` —— 选项 + 快捷键后缀（游戏拼 `" (C)"`）
        /// 实测（`弹窗 #1 选项 2 条：[Current location (C) | Center (C)]`）：
        ///   不带 ② 的处理时**查不到** ⇒ 选项仍是英文，所以这条不可省。</summary>
        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (L.TryGetValue(en, out zh)) return zh;

            // ① `"X, 备注"` ⇒ 译 X，备注原样保留
            int i = en.IndexOf(", ", StringComparison.Ordinal);
            if (i > 0)
            {
                string head = en.Substring(0, i);
                if (L.TryGetValue(head, out zh)) return zh + en.Substring(i);
            }

            // ② `"X (C)"` ⇒ 译 X，后缀原样保留
            //    ⚠ 必须**从尾部**找 `" ("`：选项正文本身可能含括号
            int j = en.LastIndexOf(" (", StringComparison.Ordinal);
            if (j > 0 && en.EndsWith(")", StringComparison.Ordinal))
            {
                string head2 = en.Substring(0, j);
                if (L.TryGetValue(head2, out zh)) return zh + en.Substring(j);
            }
            return en;
        }
    }

    internal static class ZhsPullDownPatch
    {
        private const string Tag = "[QudZhsCookbook] 传送选单：";
        private const string TITLE = "Select a destination";

        private static bool _applied;
        private static MethodInfo _pick;

        internal static void TryApply()
        {
            if (_applied || ZhsPullDown.Disabled) return;
            _applied = true;
            try
            {
                _pick = FindPickOption();
                if (_pick == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 Popup.PickOption(带选项表的重载)");
                    return;
                }

                MethodInfo pre = typeof(ZhsPullDownPatch).GetMethod(
                    "PickOptionPrefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 PickOptionPrefix");
                    return;
                }

                new Harmony("Nattens.QudZhsCookbook.pulldown.pick")
                    .Patch(_pick, prefix: new HarmonyMethod(pre));

                UnityEngine.Debug.Log(Tag + "已挂 Popup.PickOption（仅标题匹配时改写 · "
                    + ZhsPullDown.L.Count + " 条译名）"
                    + " —— ★ 不再挂 PullDown（旧版那样会卡死，见文件头）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
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

        /// <summary>找带 `IReadOnlyList&lt;string&gt;` 选项参数的 `PickOption` 重载。</summary>
        private static MethodInfo FindPickOption()
        {
            Type tp = FindType("XRL.UI.Popup");
            if (tp == null) return null;
            foreach (MethodInfo m in tp.GetMethods(BindingFlags.Public
                         | BindingFlags.Static))
            {
                if (m.Name != "PickOption") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < 5) continue;
                Type p4 = ps[4].ParameterType;
                if (p4.IsArray) continue;                 // 只认 IReadOnlyList<string>
                if (!p4.IsGenericType) continue;
                if (p4.GetGenericArguments()[0] != typeof(string)) continue;
                return m;
            }
            return null;
        }

        /// <summary>
        /// `PickOption` 的 Prefix：**仅在标题逐字等于 `Select a destination` 时**改写参数。
        ///
        /// ★★ 与旧版的根本区别：**不调用任何原方法、不依赖任何状态**。
        ///    旧版靠 `[ThreadStatic] _intercept` 窗口来判断"这次是不是我代跑的"，
        ///    而"代跑"本身（Prefix 里 Invoke 原方法）就是卡死的根源。
        ///    现在改用**标题精确匹配**做识别 —— 无状态、无重入、零流程干预。
        /// </summary>
        private static void PickOptionPrefix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 5) return;

                string title = __args[0] as string;
                if (!string.Equals(title, TITLE, StringComparison.Ordinal)) return;

                // ① 标题
                __args[0] = ZhsPullDown.Tr(title);

                // ② 选项表（就地改元素；`__args[4]` 是引用类型的数组/IList）
                var opts = __args[4] as IReadOnlyList<string>;
                if (opts == null) return;

                string[] zh = new string[opts.Count];
                for (int i = 0; i < opts.Count; i++) zh[i] = ZhsPullDown.Tr(opts[i]);
                __args[4] = zh;

                ZhsPullDown.Hits++;
                if (ZhsPullDown.Hits <= 3)
                {
                    UnityEngine.Debug.Log(Tag + "弹窗 #" + ZhsPullDown.Hits
                        + " 选项 " + opts.Count + " 条：["
                        + string.Join(" | ", zh) + "]");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "PickOptionPrefix 失败：" + e);
            }
        }
    }
}
