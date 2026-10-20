// ═══════════════════════════════════════════════════════════════════════════
// 状态效果显示名汉化（Effect DisplayName / GetStateDescription）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图：HUD 底部「生效效果： defensive stance」
//
// 【根因（反编译）】`XRL.World.Effect` 的显示名是**公开字段**，由各效果类在
//   构造函数里**直接赋硬编码英文**：
//
//     // XRL.World.Effects.LongbladeStance_Defensive::.ctor
//     public LongbladeStance_Defensive()
//     {
//         DisplayName = "{{G|defensive stance}}";     // ← ★ 硬编码
//         Duration = 9999;
//     }
//     // XRL.World.Effects.LongbladeStance_Defensive::GetStateDescription
//     public override string GetStateDescription()
//     {
//         return "{{G|in defensive stance}}";         // ← ★ 硬编码
//     }
//
//   ⇒ 这两个**都不走字符串表**（`_T` / `Strings._S`），任何 XML 都改不到。
//
// 【全族（DLL 里恰好 6 个字面量）】长刃的三个架势：
//     aggressive stance / in aggressive stance
//     defensive stance  / in defensive stance
//     dueling stance    / in dueling stance
//
// 【做法】两处 Postfix：
//   ① `XRL.World.Effect` 的**全部子类的构造函数**里，字段已赋值 ⇒ 读出来查表替换
//      （用 `AccessTools` 拿基类字段，Postfix 参数 `__instance`）
//   ② `GetStateDescription()` 的返回值查表替换（覆盖「in X stance」形态）
//
//   查表是**精确匹配（含 `{{G|…}}` 包装）** ⇒ 只动白名单内 6 个值，绝不误伤。
//
// 开关：装机目录放 `_zhs_effectname_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsEffectName
    {
        /// <summary>硬编码英文 → 中文（键含 `{{G|…}}` 包装，精确匹配）。</summary>
        private static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 长刃架势（`LongbladeStance_*`，XRL.World.Effects）──
            { "{{G|defensive stance}}",     "{{G|防御架势}}" },
            { "{{G|aggressive stance}}",    "{{G|进攻架势}}" },
            { "{{G|dueling stance}}",       "{{G|决斗架势}}" },
            { "{{G|in defensive stance}}",  "{{G|处于防御架势}}" },
            { "{{G|in aggressive stance}}", "{{G|处于进攻架势}}" },
            { "{{G|in dueling stance}}",    "{{G|处于决斗架势}}" },
        };

        public static bool Disabled;
        private static int _hits;

        /// <summary>译名表条数（挂载日志用）。</summary>
        internal static int Count { get { return L.Count; } }

        /// <summary>精确查表；未命中原样返回。</summary>
        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (!L.TryGetValue(en, out zh)) return en;
            _hits++;
            if (_hits <= 8)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 效果名："
                    + en + "  =>  " + zh);
            }
            return zh;
        }
    }

    internal static class ZhsEffectNamePatch
    {
        private const string Tag = "[QudZhsCookbook] 效果名接管：";

        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied || ZhsEffectName.Disabled) return;
            _applied = true;
            try
            {
                Type eff = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        eff = asm.GetType("XRL.World.Effect", false);
                        if (eff != null) break;
                    }
                    catch { }
                }
                if (eff == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 XRL.World.Effect");
                    return;
                }

                Harmony h = new Harmony("Nattens.QudZhsCookbook.effectname");
                int n = 0;

                MethodInfo namePost = typeof(ZhsEffectNamePatch).GetMethod(
                    "NamePostfix", BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo statePost = typeof(ZhsEffectNamePatch).GetMethod(
                    "StatePostfix", BindingFlags.NonPublic | BindingFlags.Static);

                // ── ① `Effect.DisplayName` 的**取值路径** ──
                //   ⚠ Harmony 挂**基类**的虚方法**不覆盖子类 override**
                //     （子类 override 是不同方法体）⇒ 必须逐个类型挂。
                //   但**只挂 `Effect` 自己 + 真正 override 了的子类**，
                //   不做「所有子类的所有构造函数」那种全量挂载（几百个，浪费）。
                if (namePost != null)
                {
                    foreach (Type t in eff.Assembly.GetTypes())
                    {
                        if (t == null || !eff.IsAssignableFrom(t)) continue;
                        // 只认**声明在自己身上**的 getter（即 override 或基类定义）
                        foreach (string prop in new string[]
                                 { "DisplayNameStripped", "DisplayName" })
                        {
                            MethodInfo m = null;
                            try
                            {
                                m = t.GetProperty(prop,
                                        BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance
                                        | BindingFlags.DeclaredOnly)?.GetGetMethod(true);
                            }
                            catch { }
                            if (m == null) continue;
                            try
                            {
                                h.Patch(m, postfix: new HarmonyMethod(namePost));
                                n++;
                            }
                            catch { }
                        }
                        // 构造函数里给 DisplayName 赋值的类型（如三个架势）
                        //   ⇒ 挂它们的 `.ctor`，读字段后查表
                        MethodInfo ctorPost = typeof(ZhsEffectNamePatch).GetMethod(
                            "CtorPostfix", BindingFlags.NonPublic | BindingFlags.Static);
                        if (ctorPost == null) continue;
                        foreach (ConstructorInfo ci in t.GetConstructors(
                                     BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            try
                            {
                                h.Patch(ci, postfix: new HarmonyMethod(ctorPost));
                                n++;
                            }
                            catch { }
                        }
                    }
                }

                // ── ② `GetStateDescription`（含子类 override）──
                if (statePost != null)
                {
                    foreach (Type t in eff.Assembly.GetTypes())
                    {
                        if (t == null || !eff.IsAssignableFrom(t)) continue;
                        MethodInfo m = null;
                        try
                        {
                            m = t.GetMethod("GetStateDescription",
                                BindingFlags.Public | BindingFlags.NonPublic
                                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        }
                        catch { }
                        if (m == null) continue;
                        try
                        {
                            h.Patch(m, postfix: new HarmonyMethod(statePost));
                            n++;
                        }
                        catch { }
                    }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（"
                    + ZhsEffectName.Count + " 条译名）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>属性 getter 后置（`DisplayNameStripped` 等）：查表替换。</summary>
        private static void NamePostfix(ref string __result)
        {
            try { __result = ZhsEffectName.Tr(__result); }
            catch { }
        }

        /// <summary>构造函数后置：若 `DisplayName` 是白名单英文 ⇒ 换中文。</summary>
        private static void CtorPostfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                FieldInfo f = __instance.GetType().GetField("DisplayName",
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance);
                if (f == null) return;
                string cur = f.GetValue(__instance) as string;
                string zh = ZhsEffectName.Tr(cur);
                if (!ReferenceEquals(cur, zh) && cur != zh) f.SetValue(__instance, zh);
            }
            catch { }
        }

        /// <summary>`GetStateDescription` 后置：精确查表替换。</summary>
        private static void StatePostfix(ref string __result)
        {
            try { __result = ZhsEffectName.Tr(__result); }
            catch { }
        }
    }
}
