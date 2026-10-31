// ═══════════════════════════════════════════════════════════════════════════
//  `=day=` 的中文日期 —— 顶掉游戏自带的英文序数（`13th`）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】（用户 2026-10-25 截图）
//     `在细月的13th，你抵达了绿洲小村约帕（Joppa）…`
//   中文句子里夹英文序数。
//
// 【反编译实证】`XRL.World.Text.Delegates.CalendarReplacers`
//     public static void Day(VariableContext Context)            ⇒ Day(Context, GetTimeOfYear())
//     public static void Day(VariableContext Context, long Time)   ⇒ Day(Context, GetTimeOfYear(Time))
//     public static void Day(VariableContext Context, int TimeOfYear)
//     {
//         int dateOfMonth = Calendar.GetDateOfMonth(TimeOfYear);
//         Context.Value.Append("=daynumber.displayDate=".StartReplace()
//                              .SetArgument("daynumber", dateOfMonth).ToString());
//     }
//   ⇒ 内层 `=daynumber.displayDate=` 走的是**英文序数格式化**（`13th` / `Ides` 之类）
//   ⇒ 任何 XML / 串表都改不到它（模板里只有 `=day=` 这个槽位）
//
// 【影响面】我方 `=day=` 共 **43 处**：开场白、`PlayerStatusBar Date And Time`（状态栏）、
//   Gospel 铭文、壁画、任务记录 …… 修这一个出口全族受益。
//
// 【修法】Prefix 挂 `Day(VariableContext, int)`，自己写「十三日」，`return false` 跳过原方法。
//   `Day(Context)` / `Day(Context, long)` 都会转调这一个 ⇒ 只需挂一处。
//   ★ 与 `ZhsThisCreaturePatch` 同一个打法：**void 型 replacer + Context.Value**。
//   ★ `VariableContext.Value` 是 **public readonly 字段**（§53），用 GetField 取。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsCalendarPatch
    {
        private const string Tag = "[QudZhsCookbook] 中文日期：";

        private static bool _applied;
        private static int _calls, _errors;
        private static readonly Dictionary<Type, MethodInfo> _appendOf = new Dictionary<Type, MethodInfo>();

        /// <summary>1..31 的中文日期写法（「一日」「十五日」「三十一日」）。</summary>
        private static readonly string[] DayNames =
        {
            "", "一日", "二日", "三日", "四日", "五日", "六日", "七日", "八日", "九日", "十日",
            "十一日", "十二日", "十三日", "十四日", "十五日", "十六日", "十七日", "十八日", "十九日", "二十日",
            "二十一日", "二十二日", "二十三日", "二十四日", "二十五日", "二十六日", "二十七日", "二十八日", "二十九日", "三十日",
            "三十一日",
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = Find("XRL.World.Text.Delegates.CalendarReplacers");
            if (t == null) { LogError("找不到 CalendarReplacers"); return; }

            MethodInfo target = null;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static))
            {
                if (mi.Name != "Day") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 2 && ps[1].ParameterType == typeof(int)) { target = mi; break; }
            }
            if (target == null) { LogError("找不到 Day(VariableContext, int)"); return; }

            MethodInfo pre = typeof(ZhsCalendarPatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                new Harmony("Nattens.QudZhsCookbook.calendar").Patch(target, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 Day(VariableContext, int) —— `=day=` 输出「十三日」");
            }
            catch (Exception e) { LogError("挂载失败：" + e.Message); }
        }

        /// <summary>`Day(VariableContext Context, int TimeOfYear)`。</summary>
        private static bool Prefix(object __0, int __1)
        {
            try
            {
                if (__0 == null) return true;

                int day = XRL.World.Calendar.GetDateOfMonth(__1);
                string zh = (day >= 1 && day < DayNames.Length) ? DayNames[day] : (day + "日");

                object value = Field(__0, "Value");
                MethodInfo append = AppendOf(value);
                if (value == null || append == null) return true;   // 拿不到就放行，别吞原文

                append.Invoke(value, new object[] { zh });
                _calls++;
                if (_calls <= 5)
                    UnityEngine.Debug.Log(Tag + "#" + _calls + " 第 " + day + " 日 -> " + zh);
                return false;
            }
            catch (Exception e)
            {
                if (_errors++ < 5)
                    UnityEngine.Debug.LogWarning(Tag + "Prefix 异常（放行）：" + e.Message);
                return true;
            }
        }

        // ---------------- 反射工具 ----------------

        private static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object Field(object o, string name)
        {
            if (o == null) return null;
            FieldInfo f = o.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p == null ? null : p.GetValue(o, null);
        }

        private static MethodInfo AppendOf(object sink)
        {
            if (sink == null) return null;
            Type t = sink.GetType();
            MethodInfo mi;
            if (_appendOf.TryGetValue(t, out mi)) return mi;
            try
            {
                mi = t.GetMethod("Append", BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { mi = null; }
            _appendOf[t] = mi;
            return mi;
        }

        private static void LogError(string msg)
        {
            if (_errors++ < 20) UnityEngine.Debug.LogError(Tag + msg);
        }
    }
}
