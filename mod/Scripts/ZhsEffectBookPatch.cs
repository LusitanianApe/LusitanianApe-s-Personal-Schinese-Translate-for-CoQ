// ═══════════════════════════════════════════════════════════════════════════
// 「查看效果（Active Effects）」界面汉化
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图：
//     ┌─ Active Effects -… ─────────────┐
//     │                                  │
//     │   No active effects.             │
//     │                                  │
//     │           < 1 / 1 >              │
//     │   [⇐] 上一页 [⇒] 下一页 [Esc] 合上书 │  ← 页脚**已是中文**（走字符串表）
//     └──────────────────────────────────┘
//
// 【根因（反编译）】`XRL.World.GameObject::ShowActiveEffects`：
//     foreach (Effect effect in Effects) { … stringBuilder.AppendLine(…); }
//     if (num <= 0)
//         BookUI.ShowBook(**"No active effects."**,
//                         **"&WActive Effects&Y - "** + DisplayName);   // ← 硬编码
//     else
//         BookUI.ShowBook(stringBuilder.ToString(),
//                         **"&WActive Effects&Y - "** + DisplayName);   // ← 硬编码
//
//   ⇒ 标题与「无效果」正文是**字面量**，不经 `_T` / 字符串表 ⇒ XML 改不到。
//   ⇒ 页脚翻页提示走 `[=navLeft=]-Previous Page …` 的字符串表槽 ⇒ **本来就已中文**。
//
// 【做法】给 `ShowActiveEffects` 挂 **Prefix**：
//     按「效果描述 → 中文」精确查表，翻译後**主动调原方法**（`MethodInvoker`），
//     让原方法照常弹 BookUI（**只弹一次**，不用第二个 Harmony 补丁）。
//
//   ⚠️ 未在表内的效果描述**原样保留**（只翻译白名单，绝不误伤）。
//   ⚠️ 标题里的 `DisplayName` **不动**（那是生物名，由名字系统负责）。
//
// 开关：装机目录放 `_zhs_effectbook_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsEffectBook
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>硬编码标题模板 ⇒ 中文模板（保留 `&W…&Y` 色码与原分隔符）。</summary>
        internal const string TitleEn = "&WActive Effects&Y - ";
        internal const string TitleZh = "&W生效效果&Y - ";
        internal const string EmptyEn = "No active effects.";
        internal const string EmptyZh = "没有生效中的效果。";

        /// <summary>
        /// 效果**描述**（`Effect.GetDescription()` 的返回值）⇒ 中文。
        /// 未列出的一律原样保留。
        /// </summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "{{G|in defensive stance}}",  "{{G|处于防御架势}}" },
            { "{{G|in aggressive stance}}", "{{G|处于进攻架势}}" },
            { "{{G|in dueling stance}}",    "{{G|处于决斗架势}}" },

            // ── 2026-10-18（GetDescription 分诊 · EFFECT 组）──
            //   来源：`_tools\_triage_other_gaps.tsv` 的 EFFECT 行；全是 `Effect::GetDescription`
            //   返回的**整串硬编码**（已逐个反编译核对，见 patch notes ㉒）。
            //   ★ 只收「整串」；带拼接碎片的（`{{psionic|psionically cleaved (-` /
            //     `}} Quickness)` / `dominated (` / `covered in liquid` …）**暂不收**——
            //     它们的键是半截 markup，替换会破色标。
            { "{{g|shield wall}}",          "{{g|盾墙}}" },
            { "{{M|in stasis}}",            "{{M|处于停滞}}" },
            { "{{W|safe mode}}",            "{{W|安全模式}}" },
            { "{{Y|salve}} tonic",          "{{Y|药膏}}补剂" },
            { "{{rubbergum|rubbergum}} tonic", "{{rubbergum|橡胶胶}}补剂" },
            { "{{skulk|skulk}} tonic",      "{{skulk|潜行者}}补剂" },
            { "{{ubernostrum|ubernostrum}} tonic", "{{ubernostrum|优伯诺斯特拉姆}}补剂" },
            { "{{amorous|love}} tonic",     "{{amorous|爱}}补剂" },
            { "{{c|cybernetic rejection syndrome}}", "{{c|义体排斥综合征}}" },
            { "{{g|phase spider venom}}",   "{{g|相位蜘蛛毒液}}" },
            { "+3 Str for the next three hours", "接下来三小时 +3 力量" },
            { "heal 20 hp",                 "治疗 20 点生命" },

            // 2026-10-18（F12 · EFFECT 碎片里的**整串**两条）
            //   `LiquidCovered::GetDescription`：Liquid == null ⇒ "covered in liquid"
            //   `LiquidStained::GetDescription`：Liquid == null ⇒ "stained by liquid"
            //   （有液体时返回已汉化的液体名，不走这里）
            { "covered in liquid",          "沾满液体" },
            { "stained by liquid",          "被液体玷污" },
        };

        /// <summary>
        /// 带**数值**的效果描述（碎片拼接，不能进 `L` 精确表）。2026-10-18 反编译实证：
        ///   `ShatterArmor`       → `"{{r|cleaved ({{C|-" + AVPenalty + " AV}})}}"`
        ///   `ShatterMentalArmor` → `"{{psionic|psionically cleaved (-" + MAPenalty + " MA)}}"`
        ///   `ITimeDilated`       → `"time-dilated ({{C|" + -SpeedPenalty + "}} Quickness)"`
        /// ★ 用 `Regex.Replace` 在**整页**上做（描述可能夹在多效果页里）；
        ///   输出已中文 ⇒ 中文不会再命中英文正则 ⇒ 幂等。
        /// </summary>
        private static readonly (System.Text.RegularExpressions.Regex Rx, string Zh)[] RxMap =
        {
            (new System.Text.RegularExpressions.Regex(
                @"\{\{r\|cleaved \(\{\{C\|-(\d+) AV\}\}\)\}\}"),
             "{{r|甲胄破裂（{{C|-$1 护甲值}}）}}"),
            (new System.Text.RegularExpressions.Regex(
                @"\{\{psionic\|psionically cleaved \(-(\d+) MA\)\}\}"),
             "{{psionic|精神护甲破裂（-$1 精神护甲）}}"),
            (new System.Text.RegularExpressions.Regex(
                @"time-dilated \(\{\{C\|(\d+)\}\} Quickness\)"),
             "时间迟滞（{{C|$1}} 迅捷）"),
        };

        /// <summary>翻译 BookUI 的「正文」参数（效果描述块）。</summary>
        internal static string TrPage(string page)
        {
            if (Disabled || string.IsNullOrEmpty(page)) return page;
            if (page == EmptyEn) return EmptyZh;
            string zh;
            if (L.TryGetValue(page, out zh)) return zh;
            // 多效果时正文是「{{Y|\n描述\n}}  细节」拼接 ⇒ 逐行尝试替换已知描述
            if (page.IndexOf("{{Y|", StringComparison.Ordinal) >= 0)
            {
                string outp = page;
                foreach (KeyValuePair<string, string> kv in L)
                {
                    if (outp.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                        outp = outp.Replace(kv.Key, kv.Value);
                }
                return TrRx(outp);
            }
            return TrRx(page);
        }

        /// <summary>带数值的三条（见 <see cref="RxMap"/>）。</summary>
        private static string TrRx(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string o = s;
            foreach (var (rx, zh) in RxMap)
            {
                if (rx.IsMatch(o)) o = rx.Replace(o, zh);
            }
            return o;
        }

        /// <summary>翻译 BookUI 的「标题」参数（只换前缀，`DisplayName` 部分原样保留）。</summary>
        internal static string TrTitle(string title)
        {
            if (Disabled || string.IsNullOrEmpty(title)) return title;
            if (title.StartsWith(TitleEn, StringComparison.Ordinal))
                return TitleZh + title.Substring(TitleEn.Length);
            return title;
        }
    }

    internal static class ZhsEffectBookPatch
    {
        private const string Tag = "[QudZhsCookbook] 效果书：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied || ZhsEffectBook.Disabled) return;
            _applied = true;
            try
            {
                Type tGo = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        tGo = asm.GetType("XRL.World.GameObject", false);
                        if (tGo != null) break;
                    }
                    catch { }
                }
                if (tGo == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 XRL.World.GameObject");
                    return;
                }
                MethodInfo target = tGo.GetMethod("ShowActiveEffects",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                MethodInfo pre = typeof(ZhsEffectBookPatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || pre == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 ShowActiveEffects()");
                    return;
                }
                new Harmony("Nattens.QudZhsCookbook.effectbook")
                    .Patch(target, prefix: new HarmonyMethod(pre));
                ApplyBookHook();          // 顺带挂 BookUI.ShowBook 的拦截窗口
                UnityEngine.Debug.Log(Tag + "已挂 ShowActiveEffects + ShowBook"
                    + "（" + ZhsEffectBook.L.Count + " 条效果描述）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// Prefix：翻译後主动调原方法（`MethodInvoker` 比反射快，且能拿到真实异常）。
        /// </summary>
        /// <summary>
        /// ★★ 防重入闸（2.15.67 修「查看效果秒崩」）。
        ///
        /// 【曾经的致命 bug】本 Prefix 里用 `mi.Invoke(__instance, null)` 主动调原方法，
        ///   但 Harmony 的 Prefix 返回 `false` 只是「跳过原方法体」——
        ///   而 `MethodInfo.Invoke` **走的是补丁后的入口** ⇒ 又进本 Prefix ⇒ 又 Invoke
        ///   ⇒ **无限递归 ⇒ 栈溢出 ⇒ 点「查看效果」立刻崩溃**（且常写不出堆栈）。
        ///   `_intercept` 只是「翻译开关」，并不防重入。
        ///
        /// 【修法】`_inCall` 为真时直接 `return true`，
        ///   让 Harmony 这一次**正常执行原方法体**（不再 Invoke）。
        /// </summary>
        [ThreadStatic] private static bool _inCall;

        private static bool Prefix(object __instance, MethodBase __originalMethod)
        {
            if (ZhsEffectBook.Disabled || __instance == null) return true;
            if (_inCall) return true;          // ★ 重入 ⇒ 交给 Harmony 执行原方法体
            try
            {
                MethodInfo mi = __originalMethod as MethodInfo;
                if (mi == null) return true;

                // 打开拦截窗口 ⇒ 主动调原方法（普通反射，跨版本最稳）
                _inCall = true;
                _intercept = true;
                try
                {
                    mi.Invoke(__instance, null);
                }
                finally
                {
                    _intercept = false;
                    _inCall = false;
                }
                return false;   // 已代跑
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Prefix 失败：" + e);
                _intercept = false;
                _inCall = false;
                return true;
            }
        }

        [ThreadStatic] private static bool _intercept;
        private static MethodInfo _showBook;

        /// <summary>挂 `BookUI.ShowBook(string,string,…)` 的 Prefix（由 TryApply 调用）。</summary>
        internal static void ApplyBookHook()
        {
            try
            {
                Type tb = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        tb = asm.GetType("XRL.UI.BookUI", false);
                        if (tb != null) break;
                    }
                    catch { }
                }
                if (tb == null) return;
                foreach (MethodInfo m in tb.GetMethods(BindingFlags.Public
                             | BindingFlags.Static))
                {
                    if (m.Name != "ShowBook") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(string))
                    {
                        _showBook = m;
                        break;
                    }
                }
                if (_showBook == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 BookUI.ShowBook(string,string)");
                    return;
                }
                MethodInfo pre = typeof(ZhsEffectBookPatch).GetMethod(
                    "ShowBookPrefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) return;
                new Harmony("Nattens.QudZhsCookbook.effectbook.book")
                    .Patch(_showBook, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 BookUI.ShowBook（拦截窗口）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "ShowBook 挂载失败：" + e);
            }
        }

        /// <summary>仅在拦截窗口内、且标题是 `&WActive Effects&Y - …` 时翻译参数。</summary>
        private static void ShowBookPrefix(object[] __args)
        {
            try
            {
                if (!_intercept || __args == null || __args.Length < 2) return;
                string title = __args[1] as string;
                if (title == null
                    || !title.StartsWith(ZhsEffectBook.TitleEn, StringComparison.Ordinal))
                {
                    return;
                }
                __args[0] = ZhsEffectBook.TrPage(__args[0] as string);
                __args[1] = ZhsEffectBook.TrTitle(title);
                ZhsEffectBook.Hits++;
                if (ZhsEffectBook.Hits <= 3)
                {
                    UnityEngine.Debug.Log(Tag + "书页 #" + ZhsEffectBook.Hits
                        + " 标题=[" + __args[1] + "] 正文前 40 字=["
                        + (__args[0] as string ?? "").Substring(0,
                            Math.Min(40, (__args[0] as string ?? "").Length)) + "]");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "ShowBookPrefix 失败：" + e);
            }
        }
    }
}
