// ═══════════════════════════════════════════════════════════════════════════
//  ZhsRayVariantPatch —— 射线变体的**部位名**中文化（`Hands`/`Face`/`Feet` ⇒ 手/脸/脚）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户 2026-10-11 截图 03】`Choose variant` 小弹窗里三个选项是英文：
//      > Hands      Face      Feet          （底部 `[Esc] 取消` 已是中文）
//
// 【取证结论（2026-10-15 · 子代理 A · 全部实证）】
//   `XRL.World.Parts.Mutation.FlamingRay::GetVariantName(GameObjectBlueprint)`
//   （`FreezingRay` 逐字相同）：
//       return Blueprint?.GetPartParameter("Armor", "WornOn", "Hands");
//   ⇒ 变体名**直接取蓝图的 `Armor.WornOn`**（= 部位类型 **ID**，不是可译字段）
//   该返回值被**两条显示路径**共用：
//     · 游戏内：`BaseMutation::SelectVariant` → `Popup.PickOption(…, arg4 = Options)`
//     · 角色创建：`QudMutationsModuleWindow.<SelectVariant>d__30::MoveNext` → `PickOptionAsync(…, arg3)`
//   ⇒ 挂这一处**同时**覆盖两个弹窗（`native-calls.tsv` 实测该方法的全部调用者都是显示路径：
//      `GetDisplayName`×2 · `SelectVariant` · chargen `MoveNext` · `Beak::CollectStats`，无一处当键用）
//
// 【★ 为什么必须走 C#（数据侧三条路全堵死，均实证）】
//   ① 改 `Items.xml` 的 `WornOn` ✗ —— 它**同时是逻辑值**：`SetVariant` 里赋 `BodyPartType`、
//      `TryGetVariantValidity` 里做 `Object.Body.HasPart(text, …)` 判定
//      ⇒ 改成中文会让变异**长错部位 / 有效性判定失败**
//   ② 加官方 `VariantName` tag ✗ —— `FlamingRay`/`FreezingRay` 的重写**没有 `base.` 调用**，
//      整个绕过该 tag（官方 Base 里 `VariantName` 只用在 Stinger 的 3 个蓝图）
//   ③ 官方本地化模板 ✗ —— `Base\ExampleLanguage\Items.example.xml` 对
//      `Ghostly Flames` / `Icy Vapor` **只标了 `Render.DisplayName` 与 `Description.Short`**
//      ⇒ 这是**游戏自身的漏译点**，官方也没留数据出口
//   （另：往 `Strings.xml` 加条目对此**完全无效** —— 字面量直传 `PickOption`，不经 `_S`/`_T`）
//
// 【★★★ 只认这三个值 —— 这是本补丁最重要的护栏】
//   `Hands`/`Face`/`Feet` 是**部位类型键**，全程序集 **119 处**字面量，绝大多数是**判定用**：
//     `Body::Dismember`（`if (Part.Type == "Face") { … armor.WornOn = "Face"; }`）
//     `ArmsOnEquip::AddArms/ResolveTypes` · `HelpingHands::AddArms` · `Waldopack::AddArm`
//     `MultipleArms/MultipleLegs/Wings/TwoHeaded/FreezeBreath/MultiHorns`
//     `CyberneticsEquipmentRack/GunRack` · `ModSpiked/ModCleated/ModSpringLoaded/ModOrthopedic/…`
//     `FungalSporeInfection` · `Firefighting` · `RelicGenerator::GetType/GetSubtype`
//   ⇒ **任何全局/整文件替换都会破坏游戏逻辑**。
//     本补丁只在**显示出口**、只对**这两个类的这一个方法**做精确匹配，别的值一律放行 ✓
//
// 【用词】与 2.16.126 已落地的 `Bodies.xml Description="手/脸/脚"` 对齐（§10 同词同译）。
//
// 【自证（§53/§49 同族）】`ref string __result` 的回写**必须实测**：
//   改完从 `__result` **再读一遍**打日志（只复读自己的局部变量 = 自证，等于没证）。
//   若日志显示没回写 ⇒ 退化为 Prefix 直算 + `return false`。
//   开关：`_zhs_rayvariant_off.txt`（走 `MountOff` 机制）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsRayVariantPatch
    {
        private const string Tag = "[QudZhsCookbook] ";
        private static bool _applied;
        private static int _hits;

        /// <summary>只映射这三个**部位类型 ID** ⇒ 中文（比较器 Ordinal = 精确匹配）。</summary>
        private static readonly Dictionary<string, string> BodyPart =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Hands", "手" },
            { "Face", "脸" },
            { "Feet", "脚" },
        };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            PatchOne("XRL.World.Parts.Mutation.FlamingRay");
            PatchOne("XRL.World.Parts.Mutation.FreezingRay");
        }

        private static void PatchOne(string typeName)
        {
            try
            {
                Type t = FindType(typeName);
                if (t == null)
                {
                    Debug.LogError(Tag + "射线变体名：找不到类型 " + typeName);
                    return;
                }
                MethodInfo target = null;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                                                       | BindingFlags.NonPublic
                                                       | BindingFlags.Instance))
                {
                    if (mi.Name == "GetVariantName") { target = mi; break; }
                }
                if (target == null)
                {
                    Debug.LogError(Tag + "射线变体名：找不到 GetVariantName（" + typeName + "）");
                    return;
                }

                MethodInfo post = typeof(ZhsRayVariantPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.rayvariant."
                                        + t.Name.ToLowerInvariant());
                h.Patch(target, postfix: new HarmonyMethod(post));
                Debug.Log(Tag + "射线变体名：已挂 " + typeName + ".GetVariantName");
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "射线变体名挂载失败（" + typeName + "）：" + e.Message);
            }
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                string zh;
                if (!BodyPart.TryGetValue(__result, out zh)) return;   // ★ 只认这三个，别的一律放行

                string before = __result;
                __result = zh;                                          // 写回

                // ★ 自证：从 __result **再读一遍**（不是复读局部变量）
                string after = __result;
                if (_hits++ < 3)
                    UnityEngine.Debug.Log(Tag + "射线变体名：" + before + " => " + after
                        + (after == zh ? "" : "  ⚠ 回写未生效！"));
            }
            catch { }
        }

        private static Type FindType(string full)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { Type t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }
    }
}
