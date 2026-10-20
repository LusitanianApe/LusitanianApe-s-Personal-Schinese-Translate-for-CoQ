// ═══════════════════════════════════════════════════════════════════════════
//  ZhsLateralityPatch —— 身体部位的**方位前缀**汉化（修「leftfore脚」）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图实证】「选择超验印记（atzmus）」界面：
//      蝙蝠的 leftfore脚 · 高级虚无者的 righthind腿 · 咬颚兽的 left手
//      · 四伏滑翔者的 right鳍 · 达加沙的 lefthind脚 · 蝎鸡的 leftmid-hind腿
//      · 桑塔拉洛兹的 right小羽片
//   ⇒ **方位前缀是英文，词干已是中文** ✗
//
// 【根因（反编译实证，完整链路）】
//   调用方：`XRL.World.Anatomy.BodyPart::ChangeLaterality`
//               Description = Laterality.WithLateralityAdjective(bodyPartType.Description, Laterality, Capitalized: true)
//               Name        = Laterality.WithLateralityAdjective(bodyPartType.Name, Laterality)
//           （`BodyPart::IsLateralityConsistent` 也用它做比较）
//       ↓
//   `XRL.World.Capabilities.Laterality.WithLateralityAdjective(string Base, int Laterality, out bool Conjoin, bool Capitalized)`
//       ↓
//   `Laterality.BuildLateralityAdjective(TextBuilder SB, int Laterality, out bool Conjoin, bool Capitalized)`
//       里面是**硬编码英文**：
//           INNER(512)   → "inner" / "Inner"
//           OUTER(1024)  → "outer" / "Outer"
//           UPPER(4)     → "upper"      LOWER(8)  → "lower"
//           RIGHT(2)     → "right"      LEFT(1)   → "left"
//           INSIDE(128)  → "inside"     OUTSIDE(256) → "outside"
//           MID(32)      → "mid-fore" / "mid-hind" / "mid"     ← Conjoin = true
//           FORE(16)     → "fore"       HIND(64)  → "hind"      ← Conjoin = true
//   `WithLateralityAdjective` 末尾：
//           if (Conjoin) textBuilder.Append(Base.ToLower());   // ★★ 连写 + ToLower
//           else         textBuilder.Compound(Base);
//   ⇒ `left` + `fore` + `脚` = **「leftfore脚」** ✗ —— 完全解释了截图
//
// 【修法：只挂一个汇合点】
//   `BuildLateralityAdjective` 只被 `WithLateralityAdjective` 与 `StripLateralityAdjective` 调用
//   ⇒ 挂 `WithLateralityAdjective` 即可覆盖**所有**部位名/描述 ✓
//   另须挂 `StripLateralityAdjective`：换方位时它负责**剥掉旧前缀**，
//   若只汉化「加」不汉化「剥」⇒ 名字会**累积前缀** ✗
//
// 【中文方位顺序】
//   英文顺序：Inner/Outer → Upper/Lower → Right/Left → Inside/Outside → Mid/Fore/Hind
//   中文习惯：**右左 → 上下 → 前后 → 内外**（「左上臂」「右前腿」「左中后腿」）
//   ⇒ 覆写时**重排**，不是照抄英文顺序 ✓
//   拼接：**前缀 + 词干，不加空格、不做 ToLower** ✓
//
// 【安全】
//   · 全程**纯字符串**，不引用 `TextBuilder`（项目已知：直接引用它会拉进 `ReadOnlySpan<>` ⇒ CS7069）
//   · 任一步异常 ⇒ 返回 `true` 交还原实现（英文行为不变）
//   · 开关：模组目录放 `_zhs_laterality_off.txt`
//   · 加「首次触发」日志（§46：只写「已挂」不能证明执行过）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsLateralityPatch
    {
        private const string Tag = "[QudZhsCookbook] 方位前缀：";
        private static bool _applied;
        private static int _hits;
        private static int _strips;
        private static bool _logged;
        private static bool _firstNonEmpty;      // ★ 首个**非空**方位前缀（关键证据）
        private static bool _fallbackLogged;     // 回落基类的首次记录
        private static int _fallbacks;

        // ── 位标志（照 `Laterality` 的 const，实证）──
        private const int LEFT = 1, RIGHT = 2, UPPER = 4, LOWER = 8;
        private const int FORE = 16, MID = 32, HIND = 64;
        private const int INSIDE = 128, OUTSIDE = 256, INNER = 512, OUTER = 1024;

        /// <summary>中文方位前缀（顺序：右左 → 上下 → 前后 → 内外）。</summary>
        internal static string Prefix(int lat)
        {
            var sb = new StringBuilder(8);
            if ((lat & RIGHT) != 0) sb.Append('右');
            else if ((lat & LEFT) != 0) sb.Append('左');
            if ((lat & UPPER) != 0) sb.Append('上');
            else if ((lat & LOWER) != 0) sb.Append('下');
            if ((lat & MID) != 0)
            {
                if ((lat & FORE) != 0) sb.Append("中前");
                else if ((lat & HIND) != 0) sb.Append("中后");
                else sb.Append('中');
            }
            else if ((lat & FORE) != 0) sb.Append('前');
            else if ((lat & HIND) != 0) sb.Append('后');
            if ((lat & INNER) != 0) sb.Append("内侧");
            else if ((lat & OUTER) != 0) sb.Append("外侧");
            if ((lat & INSIDE) != 0) sb.Append('内');
            else if ((lat & OUTSIDE) != 0) sb.Append('外');
            return sb.ToString();
        }

        /// <summary>英文的 `Conjoin` 语义：仅 MID/FORE/HIND 为 true（照 `BuildLateralityAdjective`）。</summary>
        private static bool ConjoinOf(int lat)
        {
            return (lat & (MID | FORE | HIND)) != 0;
        }

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            // 开关由 `ZhsBootstrap.MountOff("ZhsLateralityPatch")` 统一处理（`_off_ZhsLateralityPatch.txt`）

            try
            {
                MethodInfo withLat = null;
                foreach (var m in typeof(XRL.World.Capabilities.Laterality)
                             .GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "WithLateralityAdjective") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 4 && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(int) && ps[2].IsOut)
                    {
                        withLat = m; break;
                    }
                }
                if (withLat == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 WithLateralityAdjective(4 参) 重载");
                    return;
                }

                var h = new Harmony("Nattens.QudZhsCookbook.laterality");
                h.Patch(withLat, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ZhsLateralityPatch), nameof(WithPrefix))));
                UnityEngine.Debug.Log(Tag + "已挂 WithLateralityAdjective（方位汉化）");

                MethodInfo strip = AccessTools.Method(
                    typeof(XRL.World.Capabilities.Laterality), "StripLateralityAdjective",
                    new[] { typeof(string), typeof(int), typeof(bool) }, null);
                if (strip != null)
                {
                    h.Patch(strip, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(ZhsLateralityPatch), nameof(StripPrefix))));
                    UnityEngine.Debug.Log(Tag + "已挂 StripLateralityAdjective（剥前缀也走中文）");
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 StripLateralityAdjective ⇒ 换方位时前缀可能累积");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// `WithLateralityAdjective(string Base, int Laterality, out bool Conjoin, bool Capitalized)`
        /// 的 Prefix。用 `__args` 写 out 参数（§49：别用 `ref`，对值类型/接口不可靠）。
        /// </summary>
        private static bool WithPrefix(object[] __args, ref string __result)
        {
            try
            {
                string Base = __args[0] as string;
                int lat = (int)__args[1];
                string p = Prefix(lat);
                __args[2] = ConjoinOf(lat);          // out bool Conjoin

                if (p.Length == 0) __result = Base;                    // 无方位 ⇒ 原样
                else if (Base == null) __result = p;                   // 只要方位
                else __result = p + Base;                              // ★ 不加空格、不 ToLower

                if (!_logged)
                {
                    _logged = true;
                    UnityEngine.Debug.Log(Tag + "★ 方位前缀已汉化（首次触发）"
                        + " Base=" + (Base ?? "null") + " lat=" + lat + " ⇒ " + __result);
                }
                // ★★ 首个**非空**方位前缀 —— 这才是「补丁真的改到了部位名」的证据
                //    （首次触发常是 lat=0 的无方位调用，证明不了什么）
                if (!_firstNonEmpty && p.Length > 0 && Base != null)
                {
                    _firstNonEmpty = true;
                    UnityEngine.Debug.Log(Tag + "★★ 首个**非空**方位前缀：lat=" + lat
                        + " Base=" + Base + " ⇒ " + __result);
                }
                // 每 200 次打一条汇总（能反映「到底被调用了多少次」）
                if (_hits % 200 == 0)
                {
                    UnityEngine.Debug.Log(Tag + "汇总 #" + _hits + "：" + Stats());
                }
                _hits++;
                return false;                                          // 跳过原实现
            }
            catch (Exception e)
            {
                _fallbacks++;
                if (!_fallbackLogged)
                {
                    _fallbackLogged = true;
                    try
                    {
                        UnityEngine.Debug.LogError(Tag + "WithPrefix 异常（交还原实现）"
                            + " 第 " + _fallbacks + " 次：" + e);
                    }
                    catch { }
                }
                return true;
            }
        }

        /// <summary>
        /// `StripLateralityAdjective(string Text, int Laterality, bool Capitalized)` 的 Prefix。
        /// 若只汉化「加」而不汉化「剥」⇒ 换方位时英文前缀剥不掉、名字会累积 ✗
        /// </summary>
        private static bool StripPrefix(object[] __args, ref string __result)
        {
            try
            {
                string Text = __args[0] as string;
                int lat = (int)__args[1];
                if (Text == null) { __result = null; return false; }

                string p = Prefix(lat);
                if (p.Length == 0 || !Text.StartsWith(p, StringComparison.Ordinal)
                    || Text.Length <= p.Length)
                {
                    __result = Text;
                    return false;
                }
                __result = Text.Substring(p.Length);
                _strips++;
                return false;
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "StripPrefix 异常（交还原实现）：" + e.Message); }
                catch { }
                return true;
            }
        }

        /// <summary>诊断用计数。</summary>
        internal static string Stats()
        {
            return "改前缀 " + _hits + " 次 · 剥前缀 " + _strips + " 次 · 回落 " + _fallbacks + " 次";
        }
    }
}
