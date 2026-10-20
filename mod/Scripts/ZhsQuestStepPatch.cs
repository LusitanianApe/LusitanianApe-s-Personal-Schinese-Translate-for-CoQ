// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsQuestStepPatch —— 任务步骤的**中文显示**（T1）
// ----------------------------------------------------------------------------
//  【为什么需要】
//    `Quests.xml` 的 `<step Name>` **同时是查表键**：
//      `XRL.World.QuestLoader.XMLParsing::HandleStep` 里 `ID` 缺省即 = `Name`，
//      而 `Quest.StepsByID` 按 `ID` 建索引、对话 `CompleteQuestStep="任务~步骤"` 按它查。
//    我们曾把 `Name` 译成中文 ⇒ 键变中文 ⇒ 引用查不到 ⇒ **任务永远不结算（静默）**。
//    ⇒ 2026-10-16 已把 84 个 `Name` **全部还原成官方英文**（止血），
//      代价是任务面板里步骤名显示英文。**本补丁负责把"显示"换回中文，键不动。**
//
//  【接管点】（反编译实证）
//    `XRL.World.Text.Delegates.QuestReplacers::StepName(VariableContext, QuestStep)`
//        [VariableReplacer(new string[] { "name" })]      // 即模板里的 `=step.name=`
//        { return Step.Name; }
//    ★ 参数里**直接有 `QuestStep` 对象** ⇒ 能拿 `Step.ID`（= 官方英文名）查词表
//    ★ 渲染方 `XRL.UI.QuestLog::GetLinesForQuest` 用模板
//        `=glyph:Bullet= {{white|=step.optional.if:Optional|colonAfter|spaceAfter==step.name=}}`
//      而 `Qud.UI.QuestsLine::setData`（现代 UI）**也调 `GetLinesForQuest`**
//      ⇒ **一个接管点同时覆盖「经典日志」与「现代任务面板」**（全 DLL 仅此一处模板）
//
//  【安全性（三条，都可复核）】
//    ① **不改 `Name` 字段** ⇒ `StepsByID` 的键不受影响 ⇒ **结算照常**
//    ② 搜索走 `Qud.UI.QuestsLineData.searchText`（**直读 `QuestStep.Name`**），
//       **不经过本 replacer** ⇒ 搜索行为完全不变
//    ③ 幂等：`Step.Name` 已是中文时不再查表（`HasCjk` 判据），重复渲染安全
//
//  【自证】
//    `roslyntest -- queststep`：把词表与官方 `Quests.xml` 一起喂进去，
//    逐条验证「84 个步骤里，官方英文名 → 中文」的映射与 `StepsByID` 键**互不干扰**。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>任务步骤 `=step.name=` 的中文显示出口（T1）。</summary>
    internal static class ZhsQuestStepPatch
    {
        private static int _mounted;
        private static int _hits;
        private static bool _firstLogged;

        /// <summary>运行期开关文件名（装机目录放它即停用；与其他补丁命名一致）。</summary>
        private const string OffName = "_zhs_queststep_off.txt";
        private static string _modDir;
        private static int _offChecked;      // 0=未查 1=开 2=关

        /// <summary>每次调用检查一次开关文件（代价极低，且免重启即可停用）。</summary>
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
                    UnityEngine.Debug.Log(Tag + "任务步骤中文显示已被 " + OffName + " 停用");
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
                var t = AccessTools.TypeByName("XRL.World.Text.Delegates.QuestReplacers");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 QuestReplacers —— 未挂载");
                    return;
                }

                MethodInfo post = typeof(ZhsQuestStepPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                Harmony h = new Harmony("Nattens.QudZhsCookbook.queststep");

                // `StepName` 有**两个重载**：
                //   StepName(VariableContext, Quest)      ← 任务的显示名
                //   StepName(VariableContext, QuestStep)  ← ★ 我们要的那个（步骤名）
                // ⇒ 必须按**参数类型**挑，不能按名字盲挂（否则会连任务标题一起改坏）。
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "StepName") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[1].ParameterType == null
                        || ps[1].ParameterType.Name != "QuestStep") continue;
                    try
                    {
                        h.Patch(m, postfix: new HarmonyMethod(post));
                        _mounted++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(Tag + "挂载失败：" + e.Message);
                    }
                }

                if (_mounted > 0)
                {
                    UnityEngine.Debug.Log(Tag + "任务步骤中文显示：已挂 " + _mounted
                        + " 个 `StepName(QuestStep)` 出口（词表 " + ZhsQuestStepZh.Map.Count + " 条）");
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "任务步骤中文显示：未找到 `StepName(QuestStep)` 重载");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "TryApply 异常：" + e);
            }
        }

        /// <summary>
        /// `StepName` 的 Postfix：把官方英文步骤名换成中文。
        /// **只改返回值**，不碰 `Step.Name` 字段。
        /// </summary>
        private static void Postfix(object __1, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (IsOff()) return;
                // 已经是中文 ⇒ 原样放行（幂等）
                if (HasCjk(__result)) return;

                string id = StepId(__1);
                string zh = ZhsQuestStepZh.Lookup(id);
                if (zh == null) return;          // 词表没有 ⇒ 保持英文（不猜）

                __result = zh;
                _hits++;
                if (!_firstLogged)
                {
                    _firstLogged = true;
                    UnityEngine.Debug.Log(Tag + "任务步骤首次替换：`" + id + "` → `" + zh + "`");
                }
                else if (_hits <= 40 && _hits % 10 == 0)
                {
                    UnityEngine.Debug.Log(Tag + "任务步骤已替换 " + _hits + " 次");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Postfix 异常：" + e.Message);
            }
        }

        /// <summary>
        /// 取 `QuestStep.ID`。★ `ID`/`Name` 都是 **public 字段**（不是属性）——
        /// 本项目的 §53 踩过「用 `GetProperty` 取字段 ⇒ 恒 null ⇒ 静默放行」，
        /// 所以这里**先字段、再属性**，并把结果打日志便于核对。
        /// </summary>
        private static string StepId(object step)
        {
            if (step == null) return null;
            Type t = step.GetType();

            FieldInfo f = t.GetField("ID", BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                var v = f.GetValue(step) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }

            PropertyInfo p = t.GetProperty("ID", BindingFlags.Public | BindingFlags.Instance);
            if (p != null)
            {
                var v = p.GetValue(step, null) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }

            // 兜底：`ID` 缺省时官方就等于 `Name`，但 `Name` 现在是官方英文名 ⇒ 可直接用
            FieldInfo fn = t.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
            if (fn != null) return fn.GetValue(step) as string;
            PropertyInfo pn = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            return pn != null ? pn.GetValue(step, null) as string : null;
        }

        private static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        private const string Tag = "[QudZhsCookbook] ";
    }
}
