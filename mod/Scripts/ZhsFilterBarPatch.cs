// ═══════════════════════════════════════════════════════════════════════════
// 筛选栏类目汉化（库存筛选栏 + 日志页签）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么需要它】反编译 `Qud.UI.FilterBarCategoryButton`：
//
//     public void SetCategory(string category, string tooltip = null)
//     {
//         Tooltip = tooltip ?? category;          // ★ 不给 tooltip 就直接用类目名
//         this.category = category;
//         tooltipText?.SetText(Tooltip);
//         … categoryImageMap.TryGetValue(category, out value);   // 只取**图标**
//         … categoryTextMap.TryGetValue(category, out value2);
//         if (value == null && value2 == null)
//             value2 = category;                  // ★ 显示文字兜底 = 英文类目名
//
//     static FilterBarCategoryButton()            // .cctor 里写死
//     {
//         categoryImageMap = new Dictionary<string, string> { { "Light Sources", … }, … };
//         categoryTextMap  = new Dictionary<string, string> { { "*All", "ALL" } };  // ← 只有 1 条！
//     }
//
//   ⇒ `categoryImageMap` 只提供**图标**；显示文字与提示气泡都是**英文类目名本身**。
//   ⇒ `categoryTextMap` 是唯一的显示文字覆盖点，而它**只有一个 `*All` 条目**，
//     且是 `static readonly` 风格的初始化 ⇒ 改不到。
//
// 【已验证：数据文件里没有这些名字】
//   · 库存类目（`Light Sources` 等）：官方 `Strings.example.xml` 的
//     `Context="Physics (Inventory) Category"` 里**有**（40 条，我方已全部译好），
//     但 `FilterBarCategoryButton` **不走字符串表**，所以界面上仍是英文。
//   · 日志页签（`Village Histories` 等）：**数据文件里全无** ⇒ 纯硬编码
//     （`XRL.UI.JournalScreen.STR_*` 静态字段 + `Qud.UI.FilterBarCategoryButton::.cctor`）。
//
// 【做法】挂 `SetCategory` 的 **Postfix**：
//   · `__instance.Tooltip` 与 `__instance.category` 都换成中文；
//   · **同时**把 `category` 参数保留原英文 ⇒ `categoryImageMap` 查表不受影响；
//   · 只替换白名单内的名字，其它类目原样。
//
// ⚠ 注意：`category` **字段**被下游用作**数据键**（`categoryImageMap`、
//   以及按类目过滤物品的逻辑）⇒ 本补丁**只改 `Tooltip` 与显示文本**，
//   不动 `category` 参数的语义（见下方实现：改的是 `data` 的显示字段与 Tooltip）。
//
// 开关：装机目录放 `_zhs_filterbar_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFilterBarPatch
    {
        private const string Tag = "[QudZhsCookbook] 筛选栏：";

        internal static bool Disabled;
        private static bool _applied;
        private static int _hits;
        private static int _calls;      // ★ 无条件计数：Postfix 被调用了几次

        /// <summary>
        /// 英文类目名 → 中文。
        /// 库存类目部分取自官方 `Context="Physics (Inventory) Category"`（我方 `Strings.xml` 同名条目），
        /// 日志页签部分取自 `ZhsJournalNotePatch.Tabs`（反编译 `XRL.UI.JournalScreen` 实证）。
        /// </summary>
        private static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 库存筛选栏（`Physics (Inventory) Category`，官方 40 条）──
            { "*All",                    "全部" },
            { "Light Sources",           "光源" },
            { "Melee Weapons",           "近战武器" },
            { "Thrown Weapons",          "投掷武器" },
            { "Miscellaneous",           "杂项" },
            { "Food",                    "食物" },
            { "Corpses",                 "尸体" },
            { "Plants",                  "植物" },
            { "Missile Weapons",         "远程武器" },
            { "Projectiles",             "投射物" },
            { "Ammo",                    "弹药" },
            { "Armor",                   "护甲" },
            { "Shields",                 "盾牌" },
            { "Grenades",                "手雷" },
            { "Creatures",               "生物" },
            { "Applicators",             "涂抹器" },
            { "Energy Cells",            "能量电池" },
            { "Natural Weapons",         "天然武器" },
            { "Natural Missile Weapons", "天然远程武器" },
            { "Natural Missile Weapon",  "天然远程武器" },
            { "Natural Armor",           "天然护甲" },
            { "Meds",                    "药物" },
            { "Tonics",                  "补剂" },
            { "Water Containers",        "水容器" },
            { "Books",                   "书籍" },
            { "Tools",                   "工具" },
            { "Artifacts",               "器物" },
            { "Clothes",                 "衣物" },
            { "Trade Goods",             "贸易货物" },
            { "Quest Items",             "任务物品" },
            { "Data Disks",              "数据盘" },
            { "Scrap",                   "废料" },
            { "Trinkets",                "饰品" },
            { "Cybernetic Implants",     "义体植入物" },

            // ── 日志页签（`XRL.UI.JournalScreen.STR_*` + `.cctor`，7 条硬编码）──
            { "Locations",               "位置" },
            { "Chronology",              "年表" },
            { "Gossip and Lore",         "流言与传说" },
            { "Sultan Histories",        "苏丹史" },
            { "Village Histories",       "村落史" },
            { "General Notes",           "一般笔记" },
            { "Recipes",                 "食谱" },
        };

        private static string Tr(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            string zh;
            return L.TryGetValue(en, out zh) ? zh : en;
        }

        internal static void TryApply()
        {
            if (_applied || Disabled) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("Qud.UI.FilterBarCategoryButton", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 FilterBarCategoryButton");
                    return;
                }
                MethodInfo mi = t.GetMethod("SetCategory",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 SetCategory");
                    return;
                }
                MethodInfo post = typeof(ZhsFilterBarPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.filterbar");
                h.Patch(mi, postfix: new HarmonyMethod(post));
                UnityEngine.Debug.Log(Tag + "已挂 FilterBarCategoryButton.SetCategory（"
                    + L.Count + " 条译名）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log(Tag + "挂载失败：" + e.Message);
            }
        }

        /// <summary>
        /// Postfix：把**显示用**的文字换成中文。
        ///
        /// 只改这两处：
        ///   · `Tooltip`（提示气泡）
        ///   · `tooltipText` 的已设置文本
        /// **不改** `category` 字段 —— 它被 `categoryImageMap` 与物品过滤逻辑当**数据键**用。
        /// </summary>
        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                Type t = __instance.GetType();
                _calls++;                       // ★ 无条件计数（判断补丁是否被调用）

                FieldInfo fCat = t.GetField("category",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fTip = t.GetField("Tooltip",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fCat == null) return;

                string en = fCat.GetValue(__instance) as string;

                // ★ 诊断（前 20 次无条件记录）—— 2.6.0 上线时**没有这个**，
                //   导致无法区分「补丁没被调用」与「调用了但不在白名单」。
                //   （教训来自 `ZhsCombatMsgPatch`：加诊断前追了一整天的假象。）
                if (_calls <= 20)
                {
                    UnityEngine.Debug.Log(Tag + "SetCategory 调用 #" + _calls
                        + "：category=" + (en ?? "(null)")
                        + "  white=" + (en != null && L.ContainsKey(en) ? "是" : "否"));
                }

                string zh = Tr(en);
                if (zh == en) return;                    // 不在白名单 ⇒ 不动

                if (fTip != null) fTip.SetValue(__instance, zh);

                // 提示气泡的 UI 组件也更新（`tooltipText?.SetText(Tooltip)`）
                FieldInfo fTt = t.GetField("tooltipText",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fTt != null)
                {
                    object tt = fTt.GetValue(__instance);
                    if (tt != null)
                    {
                        MethodInfo setText = tt.GetType().GetMethod("SetText",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (setText != null)
                        {
                            ParameterInfo[] ps = setText.GetParameters();
                            setText.Invoke(tt, ps.Length == 1
                                ? new object[] { zh }
                                : new object[] { zh, null });
                        }
                    }
                }

                // 显示文字：从 `data` 里找字符串字段并替换（`data` 是 struct，需装箱回写）
                FieldInfo fData = t.GetField("data",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fData != null && fData.FieldType.IsValueType)
                {
                    object boxed = fData.GetValue(__instance);
                    if (boxed != null)
                    {
                        bool changed = false;
                        foreach (FieldInfo f in fData.FieldType.GetFields(
                                     BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance))
                        {
                            if (f.FieldType != typeof(string)) continue;
                            string v = f.GetValue(boxed) as string;
                            if (v == en) { f.SetValue(boxed, zh); changed = true; }
                        }
                        if (changed) fData.SetValue(__instance, boxed);
                    }
                }

                _hits++;
                if (_hits <= 8)
                    UnityEngine.Debug.Log(Tag + en + "  =>  " + zh);
            }
            catch { }
        }
    }
}
