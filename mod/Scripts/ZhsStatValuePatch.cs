// ═══════════════════════════════════════════════════════════════════════════
//  ZhsStatValuePatch —— `StatCollector` 取值的**源头**汉化
//
//  【问题】用户截图（2026-10-17 · 「选择变异」界面）：
//      `Sunder Mind` 的等级文本里有一行 **`射程：sight`**
//
//  【链路（反编译实证）】
//      XRL.World.Parts.Mutation.SunderMind::CollectStats:
//          stats.Set("Range", "sight");
//      → `StatNode` / `StatLineNode` 模板 `=stats.value#name#unit#signed=`
//      → `XMLTemplateReplacers::Value` 直接读 `Stats.values[key]`（**不过字符串表**）
//      → 渲染成 `sight`
//
//  【为什么不用 `StatLineNode` 的 Prefix】
//    也能拼，但那条路要把整行 `ToString()` 再 `Append` ⇒ 会**丢掉 TextBuilder 的颜色段**
//    （`AppendTo` 保留 span，`Append(string)` 不保留）。
//    这里改的是**源头字符串**，官方渲染管线原样跑 ⇒ 颜色 / 单位 / 复数 / 标记全不动。
//
//  【做法】Prefix `XRL.Templates.StatCollector::Set(string, string, bool, int)`：
//    用 `ZhsUnits.MapValue`（**整串精确**）替换 arg1；未命中原样。
//    ⇒ `sight` → 视野、`once per night` → 每夜一次、`confusing` → 致幻 …
//    ⇒ 所有 `=stats.value#…=` / `=stat…#stats=` 出口同时生效（不只 statline）。
//
//  【安全】只认**整串精确**（`MapValue` 不走「中文冒号」形态）⇒ 不会改散文；
//    词表就是 `ZhsUnits.Values` —— 它原本就是按 `StatCollector::Set` 的 arg1 建的。
//
//  【自证】`--call Nattens.QudZhsCookbook.ZhsStatValue.Fix`
//    样本：`sight` → 视野 · `once per night` → 每夜一次 · `5` 原样。
//
//  【开关】装机目录放 `_zhs_statvalue_off.txt` 即停用（默认开启）。
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>`StatCollector` 取值词的整串精确替换（纯函数，便于 `--call` 自证）。</summary>
    internal static class ZhsStatValue
    {
        internal static bool Disabled;
        internal static int Hits;

        internal static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                string t = ZhsUnits.MapValue(s);
                if (!string.Equals(t, s, StringComparison.Ordinal))
                {
                    Hits++;
                    // ★ 日志必须**单独包 try**：rulecheck（无 Unity 运行时）里
                    //   `Debug.Log` 会抛 ⇒ 外层 catch 返回**原串** ⇒ 自证静默为 0。
                    try
                    {
                        if (Hits <= 12)
                            UnityEngine.Debug.Log("[QudZhsCookbook] 取值源："
                                + s + "  =>  " + t);
                    }
                    catch { }
                }
                return t;
            }
            catch
            {
                return s;
            }
        }
    }

    internal static class ZhsStatValuePatch
    {
        private const string Tag = "[QudZhsCookbook] 取值源出口：";

        private static bool _applied;
        private static int _firstLogged;

        /// <summary>`Set(string Key, string Value, …)` 的 arg1（`__1`）就地换中文。</summary>
        private static void Prefix(ref string __1)
        {
            if (ZhsStatValue.Disabled) return;
            try
            {
                string t = ZhsStatValue.Fix(__1);
                if (string.Equals(t, __1, StringComparison.Ordinal)) return;
                if (_firstLogged == 0)
                {
                    _firstLogged = 1;
                    try { UnityEngine.Debug.Log(Tag + "首次触发（" + __1 + "  =>  " + t + "）"); }
                    catch { }
                }
                __1 = t;
            }
            catch { }
        }

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                // 开关文件
                try
                {
                    string off = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                        "Mods", "QudZhsCookbook", "_zhs_statvalue_off.txt");
                    if (File.Exists(off)) ZhsStatValue.Disabled = true;
                }
                catch { }

                if (ZhsStatValue.Disabled)
                {
                    UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                    return;
                }

                Type t = typeof(XRL.World.GameObject).Assembly
                    .GetType("XRL.Templates.StatCollector", false);
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Templates.StatCollector");
                    return;
                }

                MethodInfo target = t.GetMethod("Set",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(string), typeof(bool), typeof(int) }, null);
                if (target == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Set(string,string,bool,int)");
                    return;
                }

                MethodInfo pre = typeof(ZhsStatValuePatch).GetMethod(
                    nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Prefix 方法");
                    return;
                }

                Harmony h = new Harmony("Nattens.QudZhsCookbook.statvalue");
                h.Patch(target, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 1 个出口（StatCollector.Set(string,string)）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }
    }
}
