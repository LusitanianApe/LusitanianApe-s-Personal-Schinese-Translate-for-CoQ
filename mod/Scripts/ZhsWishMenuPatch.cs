// ═══════════════════════════════════════════════════════════════════════════
//  ZhsWishMenuPatch —— wish（许愿）菜单的**显示名**汉化
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么单独一个补丁】
//   `wishcommands.xml` 走 `DataManager.YieldXMLStreamsWithRoot("wishcommands")`
//   —— 是**普通数据加载器**，模组能追加；但 `<wish>` 节点**没有 Name 属性**
//   ⇒ 只会**追加**、不会覆盖 ⇒ 我方若整份加进去，菜单会出现「英文一条 + 中文一条」**重复**
//   ⇒ 因此不在数据侧做，改在**运行时翻 DisplayName**。
//
// 【为什么改 DisplayName 是安全的】（反编译 `WishMenu::OpenWishMenu` 实证）
//     CacheInit();
//     Popup.PickOption("Wish Menu", …, MenuItems.Select(a => a.DisplayText).ToArray(), …);
//     foreach (string command in MenuItems[num].Commands) Wishing.HandleWish(The.Player, command);
//   ⇒ 选择是**按下标**取 `Commands`，`DisplayName` 只参与显示
//   ⇒ 改它不影响任何许愿逻辑
//   ★ 而且每条都会印出 `- wish: <命令>`（`DisplayText` 里带），
//     所以中文显示名旁边永远有英文命令名可对照。
//
// 【本补丁做什么】
//   挂 `XRL.World.Parts.WishMenu::CacheInit` 的 **Postfix**
//   ⇒ 把 `MenuItems` 里每条 `DisplayName` 按**精确表**换成中文（幂等）
//   `CacheInit` 在 `MenuItems.Count != 0` 时会提前 return，但 Postfix 照样跑
//   ⇒ 重复进入也只是一次无匹配遍历。
//
// 【停用】装机目录放 `_zhs_wishmenu_off.txt`
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsWishMenuPatch
    {
        internal const string Tag = "[QudZhsCookbook] 许愿菜单：";

        internal static bool Disabled;
        private static bool _applied;
        private static int _hits;

        /// <summary>官方 `Base\wishcommands.xml` 的 13 条 DisplayName（逐字对照）。</summary>
        private static readonly (string En, string Zh)[] Exact =
        {
            ("Wish for something specific",
             "许一个具体的愿望"),
            ("Switch to Roleplay mode (checkpointing at settlements)",
             "切换到角色扮演模式（在聚落存档）"),
            ("Toggle godmode",
             "切换无敌模式"),
            ("Calm creatures",
             "安抚生物"),
            ("Spawn a floating glowsphere",
             "生成浮空光球"),
            ("Gain 25,000 XP",
             "获得 25,000 经验"),
            ("Spawn an ubernostrum tonic (regrows limbs)",
             "生成万灵药注射剂（再生肢体）"),
            ("Cure fungal infection",
             "治愈真菌感染"),
            ("Swap bodies (choose direction after wish)",
             "交换躯体（许愿后选择方向）"),
            ("Spawn rare liquids",
             "生成稀有液体"),
            ("Fast-forward to Tomb of the Eaters",
             "快进到食者之墓"),
            ("Spawn a dreamcrungle",
             "生成梦魇兽"),
            ("Spawn a can of Spray-a-Brain",
             "生成一罐喷脑剂"),
            // 标题（`Popup.PickOption("Wish Menu", …)`），走同一条替换链更省事
            ("Wish Menu",
             "许愿菜单"),
        };

        /// <summary>
        /// 动态生成的显示名（`HandleWishNode` 里有
        /// `"Teleport to " + …` 与 `"Spawn " + result.an(…)`）——
        /// 名字部分通常已是中文，但**前缀仍是英文** ⇒ 按前缀换。
        /// </summary>
        private static readonly (string En, string Zh)[] Prefix =
        {
            // ★ 中文前缀**不带尾空格**：`Teleport to ` 已经含空格，
            //   `Substring` 之后直接接名字 ⇒ 产出「传送到约帕」（不留空格）
            ("Teleport to ", "传送到"),
            ("Spawn ", "生成"),
        };

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.World.Parts.WishMenu", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Parts.WishMenu");
                    return;
                }

                MethodInfo target = t.GetMethod("CacheInit",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 CacheInit");
                    return;
                }

                MethodInfo post = typeof(ZhsWishMenuPatch).GetMethod(nameof(Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.wishmenu");
                h.Patch(target, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 1 个出口（WishMenu.CacheInit）· 精确 "
                    + Exact.Length + " 条 + 前缀 " + Prefix.Length + " 条");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "TryApply 异常：" + e);
            }
        }

        private static void Postfix()
        {
            if (Disabled) return;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.World.Parts.WishMenu", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null) return;
                FieldInfo f = t.GetField("MenuItems",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) return;
                var list = f.GetValue(null) as IList;
                if (list == null) return;

                for (int i = 0; i < list.Count; i++)
                {
                    object item = list[i];
                    if (item == null) continue;
                    FieldInfo dn = item.GetType().GetField("DisplayName",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (dn == null) continue;
                    string cur = dn.GetValue(item) as string;
                    string neo = Translate(cur);
                    if (neo != null && neo != cur)
                    {
                        dn.SetValue(item, neo);
                        _hits++;
                    }
                }
                if (_hits > 0 && _hits <= Exact.Length + 2)
                {
                    try { UnityEngine.Debug.Log(Tag + "已汉化 " + _hits + " 条显示名"); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "Postfix 异常：" + e); } catch { }
            }
        }

        /// <summary>精确表 → 前缀表；都不命中返回 null（调用方据此跳过）。</summary>
        internal static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < Exact.Length; i++)
            {
                if (s == Exact[i].En) return Exact[i].Zh;
            }
            for (int i = 0; i < Prefix.Length; i++)
            {
                if (s.StartsWith(Prefix[i].En, StringComparison.Ordinal))
                {
                    return Prefix[i].Zh + s.Substring(Prefix[i].En.Length);
                }
            }
            return null;
        }
    }
}
