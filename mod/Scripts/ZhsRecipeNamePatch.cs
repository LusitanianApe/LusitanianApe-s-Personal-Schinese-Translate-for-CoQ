// 食谱正文的部件接管：给 CookingRecipe 的命名方法打 Harmony Postfix，把菜名换成中文。
//
// 全部走运行时反射（AccessTools.TypeByName + AccessTools.Method 遍历），
// 因此本文件对食谱类型/方法名**没有编译期依赖**：类型或方法名在新版本里变了，
// 也只是日志提示找不到，不会导致整个模组编译失败。
//
// 补丁目标（按优先级）：
//   1. CookingRecipe.GenerateRecipeName()  → 直接产出菜名，最干净
//   2. CookingRecipe.GetDisplayName()      → 兜底（会把 =chefName|'s= =displayName= 框架也算进来）
// 两者都被打上也没关系：ZhsRecipeNames.Translate 是幂等的（已含中文就直接返回）。

using System;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class RecipeNamePatch
    {
        private static readonly string[] TypeCandidates =
        {
            "XRL.World.Skills.Cooking.CookingRecipe",
            "XRL.World.CookingRecipe",
            "CookingRecipe"
        };

        private static readonly string[] MethodCandidates =
        {
            "GenerateRecipeName", "GetDisplayName"
        };

        public static void TryApply()
        {
            MethodInfo postfixMethod = typeof(RecipeNamePatch).GetMethod(
                "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            if (postfixMethod == null) return;
            HarmonyMethod postfix = new HarmonyMethod(postfixMethod);

            foreach (string typeName in TypeCandidates)
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t == null) continue;

                int applied = 0;
                Harmony harmony = new Harmony("Nattens.QudZhsCookbook.recipe." + t.Name);

                foreach (string methodName in MethodCandidates)
                {
                    foreach (MethodInfo mi in t.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (mi.Name != methodName) continue;
                        if (mi.ReturnType != typeof(string)) continue;   // 只补返回字符串的
                        if (mi.GetParameters().Length != 0) continue;    // 只补无参重载，避免签名不匹配
                        if (mi.IsAbstract) continue;
                        try
                        {
                            harmony.Patch(mi, postfix: postfix);
                            applied++;
                            UnityEngine.Debug.Log("[QudZhsCookbook] 食谱正文补丁已应用："
                                + t.FullName + "." + methodName + "()");
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogError("[QudZhsCookbook] 补丁 "
                                + t.FullName + "." + methodName + " 失败：" + e.Message);
                        }
                    }
                }

                if (applied > 0)
                {
                    PatchFixedOverrides(t);
                    return;
                }
                UnityEngine.Debug.Log("[QudZhsCookbook] 类型 " + t.FullName
                    + " 上没找到可补丁的命名方法（需返回 string 且无参），尝试下一个候选。");
            }

            UnityEngine.Debug.LogError("[QudZhsCookbook] 未找到食谱类型，正文菜名将保持英文。");
        }

        /// <summary>
        /// ★ 2026-10-18：固定菜谱（`Skills.Cooking.AppleMatz` 等 10 个类）**各自 override**
        /// `GetDisplayName()/GetDescription()` ⇒ 基类的 Postfix 拦不到虚方法重写。
        /// 这里把它们逐个挂上「精确表」出口（`ZhsRecipeNames.Fixed`，查不到就原样）。
        /// </summary>
        private static void PatchFixedOverrides(Type recipeType)
        {
            try
            {
                MethodInfo post = typeof(RecipeNamePatch).GetMethod(
                    "PostfixFixed", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;
                HarmonyMethod hm = new HarmonyMethod(post);
                Harmony harmony = new Harmony("Nattens.QudZhsCookbook.recipe.fixed");

                int n = 0;
                Type[] types;
                try
                {
                    types = recipeType.Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                foreach (Type sub in types)
                {
                    if (sub == null || sub == recipeType || !sub.IsSubclassOf(recipeType)) continue;
                    foreach (MethodInfo mi in sub.GetMethods(BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (mi.ReturnType != typeof(string)) continue;
                        if (mi.GetParameters().Length != 0) continue;
                        if (mi.IsAbstract) continue;
                        if (mi.Name != "GetDisplayName" && mi.Name != "GetDescription") continue;
                        try
                        {
                            harmony.Patch(mi, postfix: hm);
                            n++;
                        }
                        catch { }
                    }
                }
                UnityEngine.Debug.Log("[QudZhsCookbook] 固定菜谱补丁：已挂 " + n
                    + " 个重写（" + ZhsRecipeNames.FixedNames.Count + " 个菜名 + "
                    + ZhsRecipeNames.FixedDescs.Count + " 条说明）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 固定菜谱补丁失败：" + e);
            }
        }

        private static void PostfixFixed(ref string __result)
        {
            try
            {
                string zh = ZhsRecipeNames.Fixed(__result);
                if (zh != null) __result = zh;
            }
            catch { }
        }

        private static void Postfix(ref string __result)
        {
            try
            {
                __result = ZhsRecipeNames.Translate(__result);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 食谱正文替换异常：" + e);
            }
        }
    }
}
