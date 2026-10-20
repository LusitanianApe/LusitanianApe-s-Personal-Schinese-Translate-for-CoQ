// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsQuestStepZh —— 任务步骤的**中文显示词表**（T1）
//
//  背景（2026-10-16 定案）：
//    `Quests.xml` 的 `<step Name>` **同时是查表键**（官方 69/84 个步骤
//    没写 `ID=`，`XRL.World.QuestLoader.XMLParsing::HandleStep` 里 `ID` 缺省即 = `Name`）。
//    我们曾把它译成中文 ⇒ 对话 `CompleteQuestStep="任务~步骤"` 查不到 ⇒ **任务不结算**。
//    ⇒ 已还原成官方英文（键正确），本表负责**显示**时再换回中文。
//
//  接管点（反编译实证）：
//    `XRL.World.Text.Delegates.QuestReplacers::StepName(VariableContext, QuestStep)`
//       [VariableReplacer(new string[] { "name" })]   // 即模板里的 `=step.name=`
//       { return Step.Name; }
//    ★ 参数里**直接有 QuestStep 对象** ⇒ 能拿 `Step.ID`（= 官方英文名）查本表
//    ★ `QuestLog::GetLinesForQuest` 用模板
//         "=glyph:Bullet= {{white|=step.optional.if:…==step.name=}}"
//      而 `Qud.UI.QuestsLine::setData`（现代 UI）**也调它** ⇒ 一个接管点覆盖两个界面
//
//  ★ 安全性：
//    · **不改 `Name` 字段** ⇒ `StepsByID` 的键不受影响 ⇒ 结算照常
//    · 搜索走 `QuestsLineData.searchText`（直读 `QuestStep.Name`），**不经过本 replacer**
//      ⇒ 搜索行为完全不变
//
//  本文件由 `_tools\_gen_quest_step_zh.py` 自动生成，请勿手改。
// ============================================================================

using System.Collections.Generic;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsQuestStepZh
    {
        /// <summary>
        /// 键 = **官方步骤 ID**（= 官方英文步骤名），值 = 中文显示名。
        ///
        /// ★ 为什么只用 ID 就够（实测判据 `_tools\_quest_step_key_check.py`）：
        ///   56 个不同步骤 ID 里，**同一个 ID 的中文处处一致**（不一致 = 0）
        ///   ⇒ 不需要带任务名限定；而 `VariableContext` 里本来也拿不到所属任务。
        /// </summary>
        internal static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Accuse the thief", "\u6307\u8BA4\u7A83\u8D3C" },
            { "Armament", "\u9009\u5B9A\u5175\u5668" },
            { "Ascend", "\u767B\u5347" },
            { "Ascend the Tomb and Cross into Brightsheol", "\u767B\u9675\u5E76\u8DE8\u5165\u660E\u8000\u51A5\u5E9C" },
            { "Atzmus", "\u9009\u5B9A\u672C\u8D28" },
            { "Barathrum", "\u51C6\u5907\u767B\u5347" },
            { "Bid the Slynth to Choose", "\u8BA9\u65AF\u6797\u65AF\u6289\u62E9" },
            { "Body", "\u9009\u5B9A\u8EAF\u58F3" },
            { "Catalyst", "\u9009\u5B9A\u89E6\u5A92" },
            { "Consult Settlements", "\u5F81\u8BE2\u5404\u805A\u843D" },
            { "Convince Pax Klanq to Construct the Climber", "\u8BF4\u670D\u5E15\u514B\u65AF\u00B7\u514B\u5170\u514B\u9020\u6500\u722C\u5668" },
            { "Defend Grit Gate", "\u5B88\u4F4F\u7802\u783E\u95E8" },
            { "Disable the Spindle's Magnetic Field", "\u5173\u95ED\u7EBA\u9524\u78C1\u573A" },
            { "Discover all the evidence", "\u627E\u9F50\u6240\u6709\u8BC1\u636E" },
            { "Eat the God's Flesh", "\u98DF\u795E\u4E4B\u8089" },
            { "Enter the Tomb of the Eaters", "\u8FDB\u5165\u98DF\u8005\u4E4B\u5893" },
            { "Find 200 Feet of Wire", "\u627E\u5230\u4E24\u767E\u5C3A\u7EBF\u6750" },
            { "Find Another Knickknack", "\u518D\u627E\u5230\u4E00\u4EF6\u53E4\u7269" },
            { "Find Mamon Souldrinker", "\u627E\u5230\u566C\u9B42\u8005\u9A6C\u8499" },
            { "Find a Dysfunctional Waydroid", "\u627E\u5230\u5931\u80FD\u7684\u5BFB\u8DEF\u673A\u5668\u4EBA" },
            { "Find a Knickknack", "\u627E\u5230\u4E00\u4EF6\u53E4\u7269" },
            { "Find the Vermin", "\u627E\u51FA\u5BB3\u866B" },
            { "Follow the Coral Path", "\u5FAA\u73CA\u745A\u4E4B\u5F84" },
            { "Gather Tau's Chime", "\u53D6\u56DE\u9676\u7684\u949F" },
            { "Get a Corpse", "\u53D6\u5F97\u4E00\u5177\u5C38\u4F53" },
            { "Go to the Bottom of Taproot", "\u4E0B\u81F3\u4E3B\u6839\u4E4B\u5E95" },
            { "Hamsa", "\u9009\u5B9A\u62A4\u7B26" },
            { "Incantation", "\u9009\u5B9A\u5492\u6587" },
            { "Inscribe the Mark", "\u523A\u4E0A\u5370\u8BB0" },
            { "Investigate the Rumbling", "\u8C03\u67E5\u90A3\u9635\u8F70\u9E23" },
            { "Locate the Barathrumite Enclave", "\u627E\u5230\u5DF4\u62C9\u745F\u9C81\u59C6\u6D3E\u98DE\u5730" },
            { "Locate the Mechanimist Compound at Bethesda Susa", "\u627E\u5230\u8D1D\u585E\u65AF\u8FBE\u00B7\u82CF\u8428\u7684\u673A\u68B0\u6559\u6D3E\u795E\u6BBF" },
            { "Make a Pilgrimage to the Six Day Stilt", "\u671D\u5723\u516D\u65E5\u9AD8\u8DF7" },
            { "Nephal", "\u4F0F\u5367\u4E4B\u517D" },
            { "Power", "\u53D6\u5F97\u767B\u5347\u80FD\u6E90" },
            { "Prepare Defenses", "\u7B79\u5907\u9632\u5FA1" },
            { "Recover the Amaranthine Prism", "\u53D6\u56DE\u82CB\u7D2B\u8272\u68F1\u6676" },
            { "Recover the Mark of Death", "\u5BFB\u56DE\u6B7B\u4EA1\u5370\u8BB0" },
            { "Recover the Repulsive Device", "\u593A\u56DE\u53EF\u618E\u4E4B\u5668" },
            { "Repair the Waydroid", "\u4FEE\u597D\u5BFB\u8DEF\u673A\u5668\u4EBA" },
            { "Return to the Hydropon", "\u8FD4\u56DE\u6C34\u57F9\u573A" },
            { "Return with the Corpse", "\u5E26\u5C38\u4F53\u8FD4\u56DE\u7EA6\u5E15" },
            { "Sanctuary Candidates", "\u5E87\u62A4\u5019\u9009\u5730" },
            { "Seek the Heart of the Rainbow", "\u5BFB\u5F69\u8679\u4E4B\u5FC3" },
            { "Soup", "\u53D6\u5F97\u8840\u4E4B\u6D41\u4F53" },
            { "Speak to Otho", "\u4E0E\u5965\u7D22\u4EA4\u8C08" },
            { "Speak with the Watchers", "\u4E0E\u5B88\u671B\u8005\u4EA4\u8C08" },
            { "Travel to Golgotha", "\u524D\u5F80\u5404\u5404\u4ED6" },
            { "Travel to Grit Gate", "\u524D\u5F80\u7802\u783E\u95E8" },
            { "Travel to Omonporch", "\u524D\u5F80\u5965\u8499\u6CE2\u5947" },
            { "Travel to Red Rock", "\u524D\u5F80\u7EA2\u5CA9" },
            { "Use the Repulsive Device", "\u4F7F\u7528\u53EF\u618E\u4E4B\u5668" },
            { "Warleader1", "\u51FB\u8D25=name=" },
            { "Warleader2", "\u51FB\u8D25=name=" },
            { "Warleader3", "\u51FB\u8D25=name=" },
            { "Warleader4", "\u51FB\u8D25=name=" },
        };

        /// <summary>按官方步骤 ID 查中文；查不到返回 null（调用方原样放行）。</summary>
        internal static string Lookup(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return null;
            string zh;
            return Map.TryGetValue(stepId, out zh) ? zh : null;
        }
    }
}
