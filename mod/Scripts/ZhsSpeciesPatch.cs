// ═══════════════════════════════════════════════════════════════════════════
// 物种名（`=player.apparentSpecies=`）汉化
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图（Tam 对话第一句）：
//     `human？我们被问候了！你想要什么？`
//   占位符保留正确，但**输出英文**。
//
// 【根因（反编译）】
//     `XRL.GenotypeEntry`（Genotypes.xml）：
//         DisplayName="变异人" / "真正子嗣"     ← 已译
//         **Species="human"**                  ← 两者共用，**未本地化**
//     `GameObject::GetApparentSpecies(Viewer)`
//         => GetApparentSpeciesEvent.GetFor(this, Viewer, GetSpecies())
//     `GameObject::GetSpecies()`
//         => GetPropertyOrTag("Species") ?? GetDisplayName(…)
//
// 【★ 为什么不能直接改 `Species`】它有**机制用途**：
//     `XRL.Names.NameStyles::Generate`
//         Species = For.GetSpecies();
//         nameStyle.CheckApply(Genotype, Subtype, **Species**, Culture, Faction, …)
//     ⇒ `Species` 是**名字风格的查表键**（取值 `human`/`dromad`/`snapjaw`/… 全小写）
//     ⇒ 改成「人类」会破坏**名字生成**与 `Village::generateMerchant`
//
// 【做法】只改**渲染路径** —— 给 `GameObject::GetApparentSpecies` 挂 **Postfix**：
//     查表翻译返回值；未命中原样返回 ⇒ 绝不误伤。
//   ⚠️ 只挂这一个方法（它是 `=player.apparentSpecies=` 的唯一出口；
//      `GetApparentSpeciesEvent.GetFor` 被 `ModDisguise` 等复用，**不该动**）。
//
// 开关：装机目录放 `_zhs_species_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsSpecies
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>
        /// 官方 `Species`（全小写标识符）⇒ 中文显示名。
        /// 取值来源：官方 Base 全库 `Species="…"`（共 26 种）。
        /// </summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "human", "人类" },
            { "dromad", "德拉玛德" },
            { "snapjaw", "咬颚兽" },
            { "baboon", "狒狒" },
            { "goatfolk", "羊民" },
            { "clam", "蛤蜊" },
            { "crab", "蟹" },
            { "star", "星" },
            { "troll", "巨魔" },
            { "bear", "熊" },
            { "ape", "猿" },
            { "ooze", "软泥" },
            { "chameleon", "变色蜥" },
            { "croc", "鳄" },
            { "dawnglider", "晨滑兽" },
            { "frog", "蛙" },
            { "iguana", "鬣蜥" },
            { "salamander", "蝾螈" },
            { "tortoise", "龟" },
            { "fish", "鱼" },
            { "spider", "蜘蛛" },
            { "scorpion", "蝎" },
            { "worm", "蠕虫" },
            { "antelope", "羚羊" },
            { "mopango", "莫潘戈" },
        };

        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (L.TryGetValue(en, out zh))
            {
                Hits++;
                if (Hits <= 10)
                    UnityEngine.Debug.Log("[QudZhsCookbook] 物种名：" + en + "  =>  " + zh);
                return zh;
            }
            return en;
        }
    }

    internal static class ZhsSpeciesPatch
    {
        private const string Tag = "[QudZhsCookbook] 物种名接管：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied || ZhsSpecies.Disabled) return;
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
                MethodInfo target = tGo.GetMethod("GetApparentSpecies",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo post = typeof(ZhsSpeciesPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || post == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 GetApparentSpecies");
                    return;
                }
                new Harmony("Nattens.QudZhsCookbook.species")
                    .Patch(target, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 GetApparentSpecies（"
                    + ZhsSpecies.L.Count + " 条物种名）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Postfix(ref string __result)
        {
            try { __result = ZhsSpecies.Tr(__result); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Postfix 失败：" + e);
            }
        }
    }
}
