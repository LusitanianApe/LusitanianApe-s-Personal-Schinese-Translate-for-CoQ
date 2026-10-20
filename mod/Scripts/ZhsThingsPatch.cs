// ═══════════════════════════════════════════════════════════════════════════
// `=N.things:名词:复数=` 汉化 —— 把英文量词换成中文
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户报告】地区名里出现 `1 stratum`（截图「严查」）：
//     `Subterranean salt marsh, Innus , legendary 光鱼的巢穴, 1 stratum深层, 甲虫月渐盈`
//
// 【反编译实证】
//   `ZoneManager::StrataText`：
//       Strings._T("ZoneManager StrataText", "=level.things:stratum= =underground.if:deep:high=")
//   我方该条**已有正确译文**：`=level.things:stratum==underground.if:深层:浅层=`
//   ⇒ 问题不在译文，而在**槽 `=level.things:stratum=` 本身产出英文名词**：
//       // XRL.World.Text.Delegates.NumberReplacers::Things
//       [VariableReplacer(Default = "thing")]
//       public static void Things(VariableContext Context, int Number)
//       {
//           string parameter = Context.GetParameter(0, Context.Default);
//           string value = ((Number == 1) ? parameter : Context.GetParameter(1, Grammar.Pluralize(parameter)));
//           Context.Value.Append(Number).Compound(value, " ");      // ★ "1" + " " + "stratum"
//       }
//   ⇒ 凡「数量 == 1」的场合都会漏出英文单数名词。
//
// 【波及面】官方模板共 **43 条**含 `.things:`，涉及 12 个名词：
//     unit · dram · turn · square · round · stratum · time · tile · error · point · charge · item
//   ⇒ 本补丁挂 `Things` 的**两个标量重载**，把名词换成中文量词。
//
// 【做法】Prefix：
//     · 查**中文量词表**（键 = 英文单数名词）
//     · 命中 ⇒ 自己把 `数字 + 量词` 写进 `Context.Value` 并返回 false（跳过原实现）
//     · 未命中 ⇒ 交还原实现（英文行为不变，零风险）
//     · 量词表里**没有**的词不猜，宁可保留英文也不乱译（§19）
//
// 【安全】`TextBuilder.Append` 走反射（直接引用会拉进 `ReadOnlySpan<>` ⇒ CS7069）；
//         `_zhs_things_off.txt` 可停用；异常一律交还原实现。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsThingsZh
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>英文单数名词 → 中文量词（与数字直接相接，不加空格）。</summary>
        internal static readonly Dictionary<string, string> UNITS =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "stratum", "层" },       // ← 用户报告的那条
            { "turn", "回合" },
            { "round", "轮" },
            { "square", "格" },
            { "tile", "格" },
            { "time", "次" },
            { "point", "点" },
            { "charge", "发" },
            { "unit", "单位" },
            { "item", "件" },
            { "error", "处" },
            { "thing", "项" },
            // ⚠ `dram`（德拉姆）**故意不收**：它是卡德的液体体积单位，
            //    项目惯例是保留「N 德拉姆」，而 `Grammar.Pluralize` 的复数形态
            //    走另一条分支，本表只管单数 ⇒ 不动更安全。
        };

        private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo>
            _cache = new System.Collections.Generic.Dictionary<Type, MethodInfo>();

        private static MethodInfo AppendOf(object sink)
        {
            if (sink == null) return null;
            Type t = sink.GetType();
            MethodInfo mi;
            if (_cache.TryGetValue(t, out mi)) return mi;
            try
            {
                mi = t.GetMethod("Append",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { mi = null; }
            _cache[t] = mi;
            return mi;
        }

        /// <summary>命中则写中文并返回 true；未命中原样（调用方交还游戏实现）。</summary>
        internal static bool TryWrite(object ctx, long number, string singular, string plural)
        {
            if (Disabled || ctx == null) return false;
            try
            {
                string noun = (number == 1 || string.IsNullOrEmpty(plural)) ? singular : plural;
                if (string.IsNullOrEmpty(noun)) return false;
                string unit;
                if (!UNITS.TryGetValue(noun.Trim(), out unit)) return false;

                object tb = ctx.GetType().GetProperty("Value")?.GetValue(ctx, null)
                         ?? ctx.GetType().GetField("Value")?.GetValue(ctx);
                if (tb == null) return false;
                MethodInfo clear = null, ap = AppendOf(tb);
                if (ap == null) return false;
                foreach (MethodInfo mi in tb.GetType().GetMethods(
                             BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mi.Name == "Clear" && mi.GetParameters().Length == 0) { clear = mi; break; }
                }
                if (clear == null) return false;
                clear.Invoke(tb, null);
                // 中文：数字直接接量词，不加空格
                ap.Invoke(tb, new object[] { number.ToString() + unit });
                Hits++;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class ZhsThingsPatch
    {
        private const string Tag = "[QudZhsCookbook] 量词汉化：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsThingsZh.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.World.Text.Delegates.NumberReplacers", false);
                    }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 NumberReplacers");
                    return;
                }
                MethodInfo pre = typeof(ZhsThingsPatch).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.thingszh");
                int n = 0;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.Static))
                {
                    if (mi.Name != "Things") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    // 只要「无 Singular 参数」的那个标量重载（`Things(Context, int)` 与 `(long)`）
                    if (ps.Length != 2) continue;
                    if (ps[1].ParameterType != typeof(int)
                        && ps[1].ParameterType != typeof(long)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(pre));
                        n++;
                    }
                    catch { }
                }
                UnityEngine.Debug.Log(Tag + "已挂 NumberReplacers.Things " + n
                    + " 个标量重载（中文量词表 " + ZhsThingsZh.UNITS.Count + " 条）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>`Things(VariableContext Context, int/long Number)` 的 Prefix。</summary>
        private static bool Prefix(object __0, object __1)
        {
            if (ZhsThingsZh.Disabled) return true;
            try
            {
                if (__0 == null || __1 == null) return true;
                long num = Convert.ToInt64(__1);
                // 参数 0 = 单数名词（缺省用 Context.Default）
                string singular = null, plural = null;
                MethodInfo gp = __0.GetType().GetMethod("GetParameter",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new Type[] { typeof(int), typeof(string) }, null);
                if (gp != null)
                {
                    singular = gp.Invoke(__0, new object[] { 0, null }) as string;
                    plural = gp.Invoke(__0, new object[] { 1, null }) as string;
                }
                if (string.IsNullOrEmpty(singular))
                {
                    FieldInfo df = __0.GetType().GetField("Default");
                    if (df != null) singular = df.GetValue(__0) as string;
                }
                if (ZhsThingsZh.TryWrite(__0, num, singular, plural))
                {
                    if (ZhsThingsZh.Hits <= 8)
                    {
                        UnityEngine.Debug.Log(Tag + num + " " + (singular ?? "?")
                            + "  =>  " + num + ZhsThingsZh.UNITS[
                                (num == 1 || string.IsNullOrEmpty(plural) ? singular : plural).Trim()]);
                    }
                    return false;
                }
                return true;
            }
            catch { return true; }
        }
    }
}
