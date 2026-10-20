// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsSkillReqPatch —— 技能界面「前置要求」里的**技能名**（N6）
// ----------------------------------------------------------------------------
//  【现象】（用户截图）
//      左栏 power 行：`19点 Strength` + `Cleave, Charge, Dismember`
//      —— 属性名已是中文（A9 修的），**前置技能名仍是英文**。
//
//  【根因】（反编译 `XRL.UI.SPNode::ModernUIText`，同一循环里两类取法不同）
//      前置 Requires  →  `argument = Entry.Name;`                        ← 英文 ✗
//      排斥 Exclusion →  `argument = Entry2.GetDisplayName();`           ← 中文 ✓
//      （随后两者都走 `Strings._T("Skill UI Prereq Ok","{{G|=name=}}")
//        .SetArgument("name", argument)`）
//    ⇒ **官方自己不一致**：`Name` 是查表键、`GetDisplayName()` 才返回 `DisplayName`。
//
//  【为什么数据侧修不了】
//      `argument` 是**直接赋值**的字符串，`=name=` 不是 replacer 槽
//      ⇒ 改 `Skills.xml` 的 `DisplayName`、改 `Strings.xml` 的模板都够不着它。
//
//  【接管点】
//      `XRL.UI.SPNode::ModernUIText(GameObject GO) : string`
//      Postfix **只改返回值**，不碰任何字段 ⇒ 与逻辑/查表完全无关。
//
//  【为什么只替换颜色标记内的整段】
//      技能名里有 `Jump` / `Charge` / `Cleave` 这类常用英文词
//      ⇒ 裸词替换会改坏别的句子（AGENTS §44「短词抢先」同族）。
//      本补丁只认 `{{G|X}}` / `{{g|X}}` / `{{R|X}}` / `{{r|X}}` 且 **X 必须与表键全等**。
//      幂等：已中文的片段查不到键 ⇒ 原样返回（Exclusion 那一支本来就是中文）。
//
//  【回滚判据】
//      · 日志有「已挂 1 个出口（词表 143 条）」= 挂上
//      · 日志有「首次替换：Cleave → 劈砍」= 真在干活
//      · 只有前者没有后者 ⇒ 按 §53 先数早退分支 / 检查表是否为空
//      · 一键停用：装机目录放 `_zhs_skillreq_off.txt`
//
//  【已知未覆盖】
//      经典界面 `XRL.UI.SkillsAndPowersScreen::Show` 有**同构的一行**（`argument = Entry.Name;`），
//      但它把结果 `scrapBuffer.Write(...)` 直接写进 `ScreenBuffer`，
//      **拿不到字符串**（返回类型是 `ScreenReturn`）⇒ 本补丁管不到。
//      证据：反编译 `type XRL.UI.SkillsAndPowersScreen` 的 `Show` 里
//            `LineElements.Add(Strings._T("Skill UI Console Prereq Ok", "Ex: {{G|=name=}}")...)`
//      ⇒ 记为已知限制，不为此改缓冲（改缓冲要自己处理全角宽度，风险远大于收益）。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>技能界面前置要求里的技能名中文化（N6）。</summary>
    internal static class ZhsSkillReqPatch
    {
        private static int _mounted;
        private static int _hits;
        private static bool _firstLogged;

        private const string OffName = "_zhs_skillreq_off.txt";
        private static string _modDir;
        private static int _offChecked;      // 0=未查 1=开 2=关

        private static bool IsOff()
        {
            if (_offChecked != 0) return _offChecked == 2;
            _offChecked = 1;
            try
            {
                if (_modDir == null)
                {
                    _modDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                        "Mods", "QudZhsCookbook");
                }
                if (System.IO.File.Exists(System.IO.Path.Combine(_modDir, OffName)))
                {
                    _offChecked = 2;
                    UnityEngine.Debug.Log(Tag + "技能前置名中文化已被 " + OffName + " 停用");
                }
            }
            catch { }
            return _offChecked == 2;
        }

        public static void TryApply()
        {
            try
            {
                if (IsOff()) return;

                if (ZhsSkillReqTable.Map.Count == 0)
                {
                    UnityEngine.Debug.LogError(Tag + "技能前置词表为空 —— 未挂载（检查 Skills.xml 的 Name/DisplayName）");
                    return;
                }

                var t = AccessTools.TypeByName("XRL.UI.SPNode");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.UI.SPNode —— 未挂载");
                    return;
                }

                MethodInfo post = typeof(ZhsSkillReqPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.skillreq");

                // `ModernUIText` 只有一个重载 `(GameObject GO)`，但仍按签名挑，
                // 避免将来官方加重载时盲挂（§46：挂不上 = 补丁等于不存在）。
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "ModernUIText") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    if (ps[0].ParameterType == null || ps[0].ParameterType.Name != "GameObject") continue;
                    if (m.ReturnType != typeof(string)) continue;
                    try
                    {
                        h.Patch(m, postfix: new HarmonyMethod(post));
                        _mounted++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(Tag + "技能前置名 挂载失败：" + e.Message);
                    }
                }

                if (_mounted > 0)
                {
                    UnityEngine.Debug.Log(Tag + "技能前置名中文化：已挂 " + _mounted
                        + " 个出口（词表 " + ZhsSkillReqTable.Map.Count + " 条）");
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "技能前置名中文化：未找到 `ModernUIText(GameObject)`");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "ZhsSkillReqPatch.TryApply 异常：" + e);
            }
        }

        /// <summary>只改返回值；表里查不到的键原样保留。</summary>
        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (IsOff()) return;

                int before = ZhsSkillReqTable.Hits;
                string r = ZhsSkillReqTable.Apply(__result);
                if (string.IsNullOrEmpty(r) || r == __result) return;

                __result = r;
                _hits++;
                if (!_firstLogged)
                {
                    _firstLogged = true;
                    UnityEngine.Debug.Log(Tag + "技能前置首次替换：" + Diff(__result, r));
                }
                else if (_hits <= 40 && _hits % 10 == 0)
                {
                    UnityEngine.Debug.Log(Tag + "技能前置已替换 " + _hits + " 行");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "ZhsSkillReqPatch.Postfix 异常：" + e.Message);
            }
        }

        /// <summary>日志用：把「改前 → 改后」压成一行（太长就截断）。</summary>
        private static string Diff(string from, string to)
        {
            try
            {
                string a = from == null ? "" : from.Replace("\n", " ");
                string b = to == null ? "" : to.Replace("\n", " ");
                if (a.Length > 120) a = a.Substring(0, 120) + "…";
                if (b.Length > 120) b = b.Substring(0, 120) + "…";
                return "`" + a + "` → `" + b + "`";
            }
            catch { return "(diff 失败)"; }
        }

        private const string Tag = "[QudZhsCookbook] ";
    }
}
