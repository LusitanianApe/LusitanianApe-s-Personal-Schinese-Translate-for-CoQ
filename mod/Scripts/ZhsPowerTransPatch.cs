// ═══════════════════════════════════════════════════════════════════════════
// 电力/液压/机械/生物机械**传输**描述汉化
// —— 挂 `XRL.World.Parts.IPowerTransmission::GetBehaviorDescription`
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图】
//     电力发电机 的物品描述里有一整段英文：
//     `Contains wiring enabling it to function as part of a power grid,
//       producing electrical charge.`
//
// 【反编译实证】
//   ① `IPowerTransmission::GetBehaviorDescription()` —— **4 个分支的字符串拼接**：
//        if (IsConduit)
//            return "Functions as part of " + Grammar.A(Assembly) + ", " + Activity
//                 + " " + GetPowerTransmissionType() + " " + Substance
//                 + " between locations.";
//        if (IsProducer)
//            return "Contains " + Constituent + " enabling " + ParentObject.them
//                 + " to function as part of " + Grammar.A(Assembly) + ", producing "
//                 + GetPowerTransmissionType() + " " + Substance + ".";
//        if (IsConsumer) … "consuming " …
//        return … ", " + Activity + " … between locations.";
//
//      ★ 拼接用的都是**部件字段**（数据侧定义，不是字面量）⇒ 所以
//        XML / DLL 里都搜不到整句（`Contains wiring…` 在 DLL 里 = 0 处）。
//
//   ② 字段取值（各子类 `.ctor`，decomp 全量）：
//        ElectricalPowerTransmission
//            Substance="charge"  Activity="conducting"  Constituent="wiring"
//            Assembly="power grid"                Unit="amp"
//        HydraulicPowerTransmission
//            Substance="power"   Activity="carrying"    Constituent="plumbing"
//            Assembly="hydraulic transmission system"  Unit="joule"
//        MechanicalPowerTransmission
//            Substance="power"   Activity="carrying"    Constituent="machinery"
//            Assembly="mechanical transmission system" Unit="joule"
//        BiomechanicalPowerTransmission
//            Substance="power"   Activity="carrying"    Constituent="biomachinery"
//            Assembly="biomechanical transmission system" Unit="joule"
//      `GetPowerTransmissionType()` 返回：
//        "electrical" / "hydraulic" / "mechanical" / "biomechanical"
//
//   ③ 对接方式（`IPowerTransmission::HandleEvent(GetShortDescriptionEvent)`）：
//        E.Postfix.AppendRules(GetBehaviorDescription());
//      ⇒ 描述句走**原生**的「规则块」追加机制；
//        本补丁**只改该方法的返回值**，不动 `Postfix`（改动面最小）。
//
// 【做法】Postfix：把拼接结果里的**固定片段 + 12 个部件字段值**按最长优先替换成中文。
//     · 只处理**精确的固定片段**（`Contains ` / ` enabling ` / ` to function as
//       part of ` / `, producing ` / `, consuming ` / ` between locations.` …）
//       和**枚举字段值**，**不做自由文本翻译** ⇒ 零误伤
//     · `ParentObject.them` 已被游戏渲染成中文（「它」）⇒ 我方译法直接省掉该代词
//     · 未命中 ⇒ 原样返回英文（安全退化）
//
// 【安全】`_zhs_powertrans_off.txt` 可停用；异常一律返回原值。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPowerTrans
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>
        /// 替换表 —— **按 key 长度降序**应用（避免「短键先吃掉长键的一部分」）。
        /// 键 = 英文原文（固定片段 / 枚举字段值），值 = 中文。
        /// </summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── ① 整段句式（**最长优先**，一次吃掉代词与冠词）──
            //
            //   ★ `Grammar.A(Assembly)` 会在名词前加 `a `/`an `/`the ` ⇒ 必须吞掉
            //   ★ `ParentObject.them` 会被渲染成「它们」⇒ 中文里删掉（句子更顺）
            { "Contains ",                                  "" },      // 兜底（见下方整段项）
            { "Functions as part of ",                      "可作为" },
            { " to function as part of a ",                 "，可接入" },
            { " to function as part of an ",                "，可接入" },
            { " to function as part of the ",               "，可接入" },
            { " to function as part of ",                   "，可接入" },
            { ", producing ",                               "，产生" },
            { ", consuming ",                               "，消耗" },
            { ", carrying ",                                "，输送" },
            { ", conducting ",                              "，传导" },
            { " between locations.",                        "。" },
            // ── ② 枚举字段值（`GetPowerTransmissionType()` 与部件字段）──
            { "biomechanical transmission system", "生物机械传动系统" },
            { "mechanical transmission system",    "机械传动系统" },
            { "hydraulic transmission system",     "液压传动系统" },
            { "power grid",                        "电网" },
            { "biomachinery",                      "生物机械" },
            { "machinery",                         "机械" },
            { "plumbing",                          "管路" },
            { "wiring",                            "线路" },
            { "biomechanical",                     "生物机械" },
            { "mechanical",                        "机械" },
            { "hydraulic",                         "液压" },
            { "electrical",                        "电力" },
            { "charge",                            "电荷" },
            { "power",                             "动力" },
        };

        /// <summary>按 key 长度**降序**排序后的键（惰性构建一次）。</summary>
        private static string[] _keys;

        private static string[] Keys()
        {
            if (_keys != null) return _keys;
            var ks = new List<string>(L.Keys);
            ks.Sort((a, b) => b.Length.CompareTo(a.Length));   // 长 → 短
            _keys = ks.ToArray();
            return _keys;
        }

        /// <summary>把英文结果串按替换表改写；无改动则返回 null。</summary>
        internal static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            // 只处理确实像该描述的结果（防御：万一挂了别的返回路径）
            if (s.IndexOf("Contains ", StringComparison.Ordinal) < 0
                && s.IndexOf("Functions as part of ", StringComparison.Ordinal) < 0)
            {
                return null;
            }
            string neo = s;
            foreach (string k in Keys())
            {
                if (neo.IndexOf(k, StringComparison.Ordinal) < 0) continue;
                neo = neo.Replace(k, L[k]);
            }
            // ── ③ 序贯清理（替换顺序会留下零星残迹，统一收口）──
            neo = neo.Replace(" ，", "，").Replace("。。", "。").Replace("  ", " ");
            // `Grammar.A()` 加的不定冠词（上面只吞了紧跟 `as part of` 的那个）
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"(?<![A-Za-z])(?:a|an|the) (?=[\u4e00-\u9fff])", "");
            // ── 代词：**穷举**处理，不枚举 ──
            //
            //   `ParentObject.them` = `GetPronounProvider().Objective`
            //   ⇒ 随生物的性别/单复数变化：`it` / `them` / `him` / `her` …
            //     甚至可能是**中文代词**（我方 `ZhsPronounPatch` 生效时）。
            //   ⇒ 枚举「它们/它/them」会漏（实测截图里是 `it`）
            //   ⇒ 改为**整段删掉** `~~ enabling <代词>`：
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"(?<![A-Za-z])enabling\s+(?:it|them|him|her|himself|herself|itself|"
                + @"themselves|It|They|Them|He|She|它|它们|他|他们|她|她们|其)"
                + @"(?![A-Za-z\u4e00-\u9fff])", "");
            // 兜底：残留的英文/中文代词（前有空格）
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"\s+(?:it|them|him|her|itself|themselves)(?![A-Za-z])", "");
            neo = neo.Replace("它们", "").Replace(" 它", "");
            // 「电力电荷 / 电力 电荷 / 生物机械动力」——去掉名词之间的空格
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"(?<=[\u4e00-\u9fff]) (?=[\u4e00-\u9fff])", "");
            neo = neo.Replace("。。", "。").Replace("，，", "，");
            // ★ 标点前的空格（删代词后残留：`管路 ，可接入` → `管路，可接入`）
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"\s+(?=[，。！？、；：])", "");
            // 英文句号 → 中文句号（**只在中文之后**，避免误伤小数/缩写）
            neo = System.Text.RegularExpressions.Regex.Replace(neo,
                @"(?<=[\u4e00-\u9fff])\.(?=\s|$)", "。");
            return neo == s ? null : neo.Trim();
        }
    }

    internal static class ZhsPowerTransPatch
    {
        private const string Tag = "[QudZhsCookbook] 传输描述：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsPowerTrans.Disabled)
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
                    UnityEngine.Debug.LogError(Tag + "找不到 IPowerTransmission");
                    return;
                }
                MethodInfo m = t.GetMethod("GetBehaviorDescription",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null || m.ReturnType != typeof(string))
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 GetBehaviorDescription（或返回类型非 string）");
                    return;
                }
                new Harmony("Nattens.QudZhsCookbook.powertrans").Patch(m,
                    postfix: new HarmonyMethod(typeof(ZhsPowerTransPatch).GetMethod(
                        "Postfix", BindingFlags.NonPublic | BindingFlags.Static)));
                UnityEngine.Debug.Log(Tag + "已挂 GetBehaviorDescription（"
                    + ZhsPowerTrans.L.Count + " 条替换项 · 按最长优先）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Postfix(ref string __result)
        {
            if (ZhsPowerTrans.Disabled) return;
            try
            {
                string zh = ZhsPowerTrans.Fix(__result);
                if (zh == null) return;
                if (ZhsPowerTrans.Hits < 6)
                {
                    UnityEngine.Debug.Log(Tag + __result + "  =>  " + zh);
                }
                ZhsPowerTrans.Hits++;
                __result = zh;
            }
            catch { }
        }
    }
}
