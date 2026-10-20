// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsConversationTagPatch —— 对话选项的**方括号注解**汉化（测试窗口 #19）
//
//  实机（Otho 选项）：
//      `[begin water ritual; 1 dram of 水]` / `[Complete Quest Step]`
//  产地（反编译）：
//      `XRL.World.Conversations.Parts.WaterRitualBegin::HandleEvent(GetChoiceTagEvent)`
//          `E.Tag = "{{<Lowlight>|[begin water ritual; {{<Numeric>|1}} dram of " + LiquidName + "]}}";`
//      `XRL.World.Conversations.Parts.QuestHandler::HandleEvent(GetChoiceTagEvent)`
//          `E.Tag = "{{W|[Complete Quest Step]}}";`
//          `E.Tag = "{{W|[Complete Quest - level-based reward]}}";`
//          `E.Tag = "{{W|[Complete Quest]}}";`
//    ⇒ 全是**硬编码**，不经过串表；选项正文（`<choice>`）已随 `Conversations.xml` 汉化，
//      只有这些注解还是英文。
//
//  做法：挂 `XRL.World.Conversations.Choice::GetDisplayText(bool)` 的 **Postfix**
//    （它把正文和 tag 拼成最终显示文本，见反编译 `text = text + " " + tag`）。
//    只替换**方括号注解**里的固定骨架，正文不受影响。
//
//  `_zhs_convtag_off.txt` 可停用。
// ============================================================================

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsConversationTag
    {
        public static bool Disabled;
        internal static int Hits;

        // `{{Numeric|1}} dram of 水` ⇒ `{{Numeric|1}} 德拉姆水`
        private static readonly Regex DramOf = new Regex(
            @"\}\}\s*dram of\s*", RegexOptions.Compiled);

        internal static string Fix(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('[') < 0) return s;

            string o = s;
            o = o.Replace("[Complete Quest Step]", "[完成任务步骤]");
            o = o.Replace("[Complete Quest - level-based reward]", "[完成任务·等级奖励]");
            o = o.Replace("[Complete Quest]", "[完成任务]");
            o = o.Replace("[begin water ritual; ", "[开始水之仪式；");
            o = o.Replace("[Begin Water Ritual; ", "[开始水之仪式；");
            o = DramOf.Replace(o, "}} 德拉姆");
            if (o != s) Hits++;
            return o;
        }
    }

    internal static class ZhsConversationTagPatch
    {
        private const string Tag = "[QudZhsCookbook] 对话注解：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsConversationTag.Disabled) return;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.World.Conversations.Choice", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Choice");
                    return;
                }
                MethodInfo m = null;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name != "GetDisplayText") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
                    {
                        m = mi;
                        break;
                    }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Choice.GetDisplayText(bool)");
                    return;
                }
                MethodInfo post = typeof(ZhsConversationTagPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.convtag")
                    .Patch(m, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 Choice.GetDisplayText(bool)");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                __result = ZhsConversationTag.Fix(__result);
            }
            catch { }
        }
    }
}
