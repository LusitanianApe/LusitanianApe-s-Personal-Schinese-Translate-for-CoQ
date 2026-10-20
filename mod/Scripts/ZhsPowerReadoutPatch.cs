// ═══════════════════════════════════════════════════════════════════════════
// 电力读数块（`Power available:` / `Total X production:` …）—— **作用域护栏版**
// ═══════════════════════════════════════════════════════════════════════════
//
// 【待办项的由来】`_tools\汉化润色待办.md` §「电力读数块」
//   2.16.0 已汉化 `GetBehaviorDescription()` 的 4 分支拼接，
//   但**同一方法后面**的读数块仍是英文。
//
// 【为什么不能加进 `ZhsShortDescPatch` 的精确表】
//   那是**分片追加**：`E.Postfix.Append("\n{{rules|").Append("Power available: ")…`
//   ⇒ `ZhsShortDescPatch` 挂的 `TextBuilder.Append(string)` **每次只看到一个片段**；
//     而 `"Power available: "` 是**通用短语**，光看分片**无法判断**是否在读数块里
//     ⇒ 直接加有误伤风险（这也是待办里标注「需作用域护栏」的原因）。
//
// 【反编译取证（本补丁的依据）】
//   `XRL.World.Parts.IPowerTransmission::HandleEvent(GetShortDescriptionEvent E)`：
//       E.Postfix.AppendRules(GetBehaviorDescription());
//       if (Readout || …)
//       {
//           string value = "\n{{rules|";
//           E.Postfix.Append(value).Append("Power available: ").Append(text)
//             .Append(' ').Append((text == "1") ? Unit : Grammar.Pluralize(Unit));
//           E.Postfix.Append(value).Append("Total ").Append(Assembly)
//             .Append(" production: ")…          // availability / draw 同形
//           E.Postfix.Append(value).Append("Local ").Append(Assembly)
//             .Append(" capacity: ")…
//           E.Postfix.Append("}}");             // ★ 整块有明确起止
//       }
//
//   `XRL.World.IShortDescriptionEvent`：
//       public readonly TextBuilder Postfix = new CompositedTextBuilder(512);
//       … string text4 = Postfix.ToString();        // ★ 可取全文
//       … E.Postfix.Clear().Append(Postfix);        // ★ 可 Clear + Append
//   ⇒ **所以能整块读取→替换→写回**，不需要状态机（比待办里的 B 方案干净）。
//
// 【护栏】只替换**读数行前缀**（`Power available: ` / `Total X production: ` 等），
//   且只在 `E.Postfix` 的文本里做**行首**匹配 ⇒ 作用域限定在描述文本的该行，
//   不会碰到别的分片。清单里的 `{{rules|` 开头分片是**逐行**出现的，故用行级替换。
//
// 【不做的事】`Assembly`（组装名）与 `Unit`（单位）是**部件字段**，
//   在官方 XML / ObjectBlueprints / 代码字面量里均未检索到取值
//   （实测：`Assembly=` / `Unit=` 0 处 · 电力读数字面量在 DLL 里也未按预期检索到）
//   ⇒ 本补丁**只译框架文字**，`Assembly` / `Unit` 的实际值原样保留。
//     ⚠ 若实机看到组装名/单位仍是英文，那是**字段值**问题，须另行取证（不在此补丁范围）。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPowerReadout
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>读数行框架 → 中文。**行级**匹配（`^` 锚定）。</summary>
        // ⚠⚠ 锚点三版教训（§45：规则「加了却没生效」先查锚点）：
        //   ① 只锚 `^`              ⇒ 行首是 `{{rules|` ⇒ **一条都匹配不到**
        //   ② `PRE` 消费 `{{rules|`  ⇒ 替换后**色标丢失**；
        //      为保色标又写 `${0}`   ⇒ `${0}` = **整个匹配**（含英文）⇒ **英文被抄回来**
        //   ③ 试 `(?=...)` 零宽断言 ⇒ 要求色标**紧贴行首** ⇒ 缓冲区拼接时又不命中；
        //   ④ ★ 正解：`(?:^|\{\{色标\|)` **交替** —— 行首走 `^`，
        //      前接其它文本时走色标分支 ⇒ 两种形态都命中，英文被真正消费。
        private const string PRE = @"(?<=\{\{rules\|)";

        /// <summary>读数行框架 → 中文。★ 用**后行断言**（不消费 `{{rules|`），保住色标。</summary>
        private static readonly (Regex Rx, string Zh)[] RULES =
        {
            // `{{rules|Power available: 3 charge`  ⇒ `{{rules|可用电力：3 电荷`
            (new Regex(PRE + @"Power available: ", RegexOptions.Compiled), "可用电力："),
            // `{{rules|Total <Assembly> production: 3 charge` ⇒ `{{rules|总<组装>产出：3 电荷`
            (new Regex(PRE + @"Total ([^:\n]{1,40}) production: ", RegexOptions.Compiled), "总$1产出："),
            (new Regex(PRE + @"Total ([^:\n]{1,40}) availability: ", RegexOptions.Compiled), "总$1可用："),
            (new Regex(PRE + @"Total ([^:\n]{1,40}) draw: ", RegexOptions.Compiled), "总$1消耗："),
            (new Regex(PRE + @"Local ([^:\n]{1,40}) capacity: ", RegexOptions.Compiled), "本地$1容量："),
        };

        /// <summary>
        /// 读数里的**单位 / 组名**（`Unit` / `Assembly` 是部件字段，取值不固定）。
        /// ★ 只在读数行里替换 ⇒ 不会误伤正文里的同形单词。
        /// </summary>
        private static readonly (Regex Rx, string Zh)[] VALUES =
        {
            // ⚠ 不能用 `\b`：中文 `总`/`本地` 也是 `\w` ⇒ `总power grid` 之间**没有词边界**
            //   （实测：加了 `\b` 时 `power grid` 一条都不替换）
            (new Regex(@"amps", RegexOptions.Compiled), "安培"),
            (new Regex(@"amp", RegexOptions.Compiled), "安培"),
            (new Regex(@"charge", RegexOptions.Compiled), "电荷"),
            (new Regex(@"power grid", RegexOptions.Compiled), "电网"),
        };

        /// <summary>
        /// 整块替换；无改动返回 null。
        /// ★ 2026-10-18（#64）两处修正：
        ///   ① `Postfix` 字段在**基类** `IShortDescriptionEvent` 上 ——
        ///      `__0.GetType().GetField(...)` 取不到继承字段（§53 同族）⇒ 从没生效过；
        ///   ② 旧 `PRE` 把 `{{rules|` **消费掉了** ⇒ 替换后色标丢失（它自己的注释里
        ///     就记着这个坑）⇒ 改后行断言，不消费。
        /// </summary>
        internal static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return null;
            string[] lines = s.Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("{{rules|", StringComparison.Ordinal) < 0) continue;
                string o = line;
                foreach (var (rx, zh) in RULES) o = rx.Replace(o, zh);
                foreach (var (rx, zh) in VALUES) o = rx.Replace(o, zh);
                if (o != line) { lines[i] = o; any = true; }
            }
            return any ? string.Join("\n", lines) : null;
        }
    }

    /// <summary>挂 `IPowerTransmission.HandleEvent(GetShortDescriptionEvent)` 的 Postfix。</summary>
    internal static class ZhsPowerReadoutPatch
    {
        private const string Tag = "[QudZhsCookbook] 电力读数：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsPowerReadout.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.World.Parts.IPowerTransmission", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Parts.IPowerTransmission");
                    return;
                }
                MethodInfo target = null;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name != "HandleEvent") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 1
                        && ps[0].ParameterType.FullName == "XRL.World.GetShortDescriptionEvent")
                    {
                        target = mi;
                        break;
                    }
                }
                if (target == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 HandleEvent(GetShortDescriptionEvent)");
                    return;
                }
                MethodInfo post = typeof(ZhsPowerReadoutPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.powerreadout")
                    .Patch(target, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 IPowerTransmission.HandleEvent"
                    + "（GetShortDescriptionEvent · 5 条读数行框架 · 行级锚定）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// Postfix：把 `E.Postfix` 的**全文**取出、替换、写回。
        /// 反射访问 `Postfix`（`TextBuilder`）的 `ToString()` / `Clear()` / `Append(string)`，
        /// 对签名零编译期依赖。
        /// </summary>
        private static void Postfix(object __0)
        {
            if (ZhsPowerReadout.Disabled || __0 == null) return;
            try
            {
                // ★ 2026-10-18（#64）：`Postfix` 声明在**基类** `IShortDescriptionEvent` 上，
                //   `Type.GetField` 默认**不返回继承字段**（§53 同族）⇒ 必须逐层往上找。
                FieldInfo fi = null;
                for (Type t = __0.GetType(); t != null && fi == null; t = t.BaseType)
                {
                    fi = t.GetField("Postfix",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                object tb = fi != null ? fi.GetValue(__0) : null;
                if (tb == null) return;
                Type tbt = tb.GetType();
                string s = tbt.GetMethod("ToString", Type.EmptyTypes)?.Invoke(tb, null)
                           as string;
                if (string.IsNullOrEmpty(s)) return;
                string fixedUp = ZhsPowerReadout.Fix(s);
                if (fixedUp == null) return;
                MethodInfo clear = tbt.GetMethod("Clear", Type.EmptyTypes);
                MethodInfo append = null;
                foreach (MethodInfo mi in tbt.GetMethods(BindingFlags.Public
                             | BindingFlags.Instance))
                {
                    if (mi.Name == "Append")
                    {
                        ParameterInfo[] ps = mi.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                        {
                            append = mi;
                            break;
                        }
                    }
                }
                if (clear == null || append == null)
                {
                    UnityEngine.Debug.LogError(Tag + "TextBuilder 缺 Clear/Append(string)");
                    return;
                }
                clear.Invoke(tb, null);
                append.Invoke(tb, new object[] { fixedUp });
                if (ZhsPowerReadout.Hits < 6)
                {
                    UnityEngine.Debug.Log(Tag + s.Replace("\n", "\\n")
                        + "  =>  " + fixedUp.Replace("\n", "\\n"));
                }
                ZhsPowerReadout.Hits++;
            }
            catch (Exception e)
            {
                if (ZhsPowerReadout.Hits < 4)
                {
                    UnityEngine.Debug.LogError(Tag + "替换失败：" + e.Message);
                }
            }
        }
    }
}
