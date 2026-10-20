// 苏丹史（HistoricEvent）文本的**部件接管**。
//
// 系统结构（已实证）：
//   * 叙述模板 = Base\ExampleLanguage\Strings.example.xml 里 256 条 HistoricEvent*/HistoricEntity*
//     （内含 =spice:xxx.!random= 之类的随机引用）→ 走字符串表覆盖；
//   * 词池取值 = Base\HistorySpice.jsonc（{"spice":{池名:[短语…]}}）；
//   * **展开器 = HistoryKit.HistoricStringExpander**（Expand / ExpandString / DelegateExpandQuery），
//     它才是把 =xxx= 占位符换成实际文字的地方（0.21.0 通过程序集反射实证）。
//     ⚠ 历史错误：旧注释写的是 `XRL.World.Text.SpiceContext` —— 那个类型名**不存在**，
//       真正相关的是 `XRL.World.Text.Delegates.SpiceContext`（只有 Random 属性与 ToString，
//       并不做词池展开）。按错误的类型名去找，就会挂到无关方法上（见下面 TryApply 的说明）。
//
// 本接管做的是"**整句精确匹配替换**"：hook SpiceContext 上返回 string 的解析方法，
// 若返回值正好是我们词表里收录的短语，就换成中文；否则原样放行（绝不动查询串！）。
// 未收录的短语写入 <模组目录>\ZhsSpiceUncovered.log —— 覆盖率因此可量化。
//
// 为什么用接管而不是只放汉化 jsonc：模组能否覆盖 .jsonc 数据文件没有先例（与 Quests.xml 同样的疑点），
// 而接管不依赖数据文件加载；两条路一起上，互为保险。

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsHistorySpicePatch
    {
    // ═══════════════════════════════════════════════════════════════════════
    // 【★ 路线分工（用户 2026-10-08 裁定：保持现状）】
    //
    //   苏丹史英文替换有**两条路**，本补丁这条路已被**自己的护栏**封死：
    //
    //     ① **数据路线**（★ 实际生效的主力）
    //          `mods\QudZhsCookbook\HistorySpice.jsonc` —— 直接改**词池文件本身**，
    //          游戏 `HistoricSpice.Init` + `MergeModJson` 时合并
    //          现况：**已译 5,339 处中文，真缺口 0**（2026-10-08 实测）
    //
    //     ② **C# 路线**（本补丁，下文称「冗余备份」）
    //          挂 `HistoricStringExpander` 的 Postfix，**展开后**逐条查 `ZhsHistorySpice.Map`
    //          现况：`Map` 428 条里**只有 2 条**（`zirconium` / `electrum`）能过下面的护栏
    //          ⇒ 扩 `Map` 到 1600+ 条，**游戏里一个字都不会变**
    //
    //   【为什么被自己封死】
    //     下面护栏② `IsPoolPhrase(__result)` 的本意是**防误伤**
    //     （词池短语是「中间产物」，若在出口处被替换，可能让 `=spice:…=` 查不到节点
    //       ⇒ 无限重试，见 AGENTS §42 血的教训）。
    //     而词池短语**几乎全在** `Known` 集合里 ⇒ 一律放行 ⇒ `Map` 没有机会。
    //
    //   【为什么不动它】（用户裁定 + 我的建议一致）
    //     · 数据路线已覆盖到位（真缺口 0）
    //     · 动护栏 = 改一个**已工作**的补丁，收益只是「让一条冗余路径也能生效」
    //     · 风险 > 收益（AGENTS §8：不重构已工作的部分）
    //   ⇒ **保持现状**：本路线保留为**冗余备份**，不扩 `Map`、不改护栏。
    //     若某天数据路线失效（例如官方改了词池加载机制），这条路可作应急回退 ——
    //     届时再评估是否放开护栏。
    // ═══════════════════════════════════════════════════════════════════════
        public static void TryApply()
        {
            MethodInfo post = typeof(ZhsHistorySpicePatch).GetMethod("Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (post == null) return;

            int types = 0, patched = 0;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    List<Type> ok = new List<Type>();
                    foreach (Type t in ex.Types) if (t != null) ok.Add(t);
                    all = ok.ToArray();
                }
                catch { continue; }

                foreach (Type t in all)
                {
                    if (t.FullName == null || t.FullName.StartsWith("Nattens.", StringComparison.Ordinal)) continue;

                    // ===== 白名单：只挂"真正负责把 =spice:...= 展开成文字"的类型 =====
                    //
                    // 演进史（每一条都是实测换来的）：
                    //   0.22 只认"类型名含 Spice" → 把真正干活的 HistoricStringExpander 整个跳过，
                    //        只挂上 HistoricSpice.CheckDeprecatedSpiceFormats() 这个无关方法 → 全军覆没。
                    //   0.23 放宽成"Spice 或 Historic" → 挂上 35 个方法，词池替换生效了，
                    //        但**顺带挂上了一大堆只读属性的 getter**（HistoricEntitySnapshot.GetProperty、
                    //        get_sacredThing、get_profaneThing、HistoricEntity.GetEntityProperty …）。
                    //        这些既不能帮我们汉化代词（代词早已存进事件属性），
                    //        又在**世界生成**路径上平白增加被改写行为的风险
                    //        —— 实测新建世界时崩在 InitializeSultan.Generate → GetRandomElement。
                    //   0.40 收紧为下面三个类型：只保留"展开文本"这条真正有用的链路。
                    bool allowed =
                        t.FullName == "HistoryKit.HistoricStringExpander"   // 展开器：把 =spice:= 换成文字（核心）
                        || t.FullName == "HistoryKit.HistoricSpice"          // 词池根
                        || t.FullName == "HistoryKit.HistoricEvent";         // 事件的 Expand / ExpandString
                    if (!allowed) continue;
                    types++;

                    Harmony h = new Harmony("Nattens.QudZhsCookbook.spice." + t.Name);
                    foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                            | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (mi.ReturnType != typeof(string)) continue;
                        // 只挑"解析/展开"类方法；查询串构造方法碰都不碰。
                        // 另外：**不再匹配宽泛的 "Get"**（那会把 GetProperty 之类的读取器卷进来）。
                        // ★★ 2.15.69 修：**排除查询解析方法**。
                        //
                        //   `ExpandQuery` / `DelegateExpandQuery` / `InternalExpandQuery`
                        //   返回的是「`$profession` 这类子类型解析出的**单个词池值**」
                        //   （如 `astrologist`），**不是最终整句**。
                        //   把它们也翻译掉 ⇒ 该中文值被塞进 `vars` ⇒
                        //   后续 `=spice:professions.$profession.guildhall=` 变成
                        //   `=spice:professions.占星家.guildhall=` ⇒ 查表 miss
                        //   ⇒ 生成器**无限重试**（实测日志 ×93,660 条 spice 引用失败）。
                        if (mi.Name.IndexOf("Query", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        bool want = mi.Name.IndexOf("Expand", StringComparison.OrdinalIgnoreCase) >= 0
                                 || mi.Name.IndexOf("Spice", StringComparison.OrdinalIgnoreCase) >= 0
                                 || mi.Name.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!want) continue;
                        if (mi.IsAbstract || mi.ContainsGenericParameters) continue;
                        if (mi.DeclaringType != null && mi.DeclaringType.IsInterface) continue;
                        try
                        {
                            h.Patch(mi, postfix: new HarmonyMethod(post));
                            patched++;
                            UnityEngine.Debug.Log("[QudZhsCookbook] 苏丹史词池接管已挂："
                                + t.FullName + "." + mi.Name + "()");
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.Log("[QudZhsCookbook] 苏丹史词池接管跳过 " + t.FullName + "."
                                + mi.Name + "：" + e.Message);
                        }
                    }
                }
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] 苏丹史词池接管：扫描 " + types
                + " 个白名单类型（HistoricStringExpander / HistoricSpice / HistoricEvent），挂上 "
                + patched + " 个方法");
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (Markup.HasCjk(__result)) return;          // 已是中文 → 幂等

                // ★★ 2.15.69 护栏①：**未展开完的中间产物**一律放行。
                //   只要还有 `=` 槽或 `=spice:` 引用，就说明这不是最终整句 ——
                //   翻译它只会破坏后续展开所需的查询串。
                if (__result.IndexOf('=') >= 0) return;

                // ★★ 2.15.69 护栏②：**正好是单条词池值**的，不译。
                //   `Known` 是词池短语全集（3749 条）⇒ 命中即「这是个中间单值」，
                //   它会被当成查询的一部分拼进 `spice.<池>.<值>.<子键>`，
                //   译成中文 ⇒ 查表 miss ⇒ 无限重试。只译**整句**。
                if (ZhsHistorySpice.IsPoolPhrase(__result)) return;

                string zh = ZhsHistorySpice.Translate(__result);
                if (zh != null)
                {
                    __result = zh;
                    return;
                }
                // 只记录"像散文"的返回值，避免把查询串/标识符写满日志
                if (__result.Length >= 6 && __result.IndexOf(' ') >= 0)
                    ZhsHistorySpice.NoteUncovered(__result);
            }
            catch { }
        }
    }
}
