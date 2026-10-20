// ═══════════════════════════════════════════════════════════════════════════
// 现代 UI（UGUI / `Qud.UI`）三处英文 —— **数据表与字符串表都够不到**
// ═══════════════════════════════════════════════════════════════════════════
//
// 背景：用户两张截图
//   · 帮助界面（Modern）：标题 `HELP`、左侧栏 12 条 `Quickstart / Attributes / …` 英文
//   · 按键设置界面：中间一列**全大写**英文分类
//       `BASIC MOVE / ATTACK` · `MENUS` · … · `DEBUG`
//
// ─────────────────────────────────────────────────────────────────────────
// ① 标题 `HELP` / `CONTROL MAPPING` —— 预制体里的**序列化默认值**
// ─────────────────────────────────────────────────────────────────────────
// 取证 A（对照官方自己的写法）：
//   `Qud.UI.SaveManagement::Show()`：
//       savesScroller.titleText.SetText(
//           Strings._S("SaveManagement Screen Title", "{{W|LOAD GAME}}"));
//   ⇒ 有字符串表的界面能译（我方 `Strings.xml` 已有 `{{W|载入游戏}}`）
//
// 取证 B（本界面没有这一步）：
//   `Qud.UI.HelpScreen::Show()` / `KeybindsScreen` 里**没有** `titleText.SetText(…)`
//   ⇒ 标题只能是预制体 `UITextSkin.text` 的序列化值
//
// 取证 C（在 Unity 资源里找出处）：
//   `_tools\_scan_level0_titles.py` 扫 `CoQ_Data\level0`：
//       @1514621  `<color=#CFC041FF>HELP </color>`
//       @1443293  `<color=#CFC041FF>CONTROL MAPPING </color>`
//       @1919856  `{{W|HELP}}`          ← ★ `UITextSkin.text` 的**原值**
//       @2094112  `{{W|CONTROL MAPPING}}`
//   ⇒ `{{W|HELP}}` / `{{W|CONTROL MAPPING}}` **既不在官方 Base/ExampleLanguage，
//     也不在 DLL 字面量，也不在字符串表**（DLL 里搜 `{{W|HELP}}` 只命中无关的 `XRL.Help`）
//      ⇒ ★ **唯一的注入点就是运行时**：`XRL.UI.UITextSkin::Apply()`
//        （`Start()` → `Apply()`，预制体默认值一定经过这里）
//
// ─────────────────────────────────────────────────────────────────────────
// ② 帮助左侧栏 12 条英文 —— 渲染的是**键**，不是显示名
// ─────────────────────────────────────────────────────────────────────────
//   `Qud.UI.HelpScreen::HelpMenu()`：
//       new HelpDataRow { CategoryId = kv.Key,                    // ← 官方 `name`（英文键）
//                         Description = kv.Value.DisplayTopic,   // ← 我方 `<topic DisplayName>`
//                         HelpText = kv.Value.GetData() }
//   `Qud.UI.LeftSideCategory::setData()`：
//       if (data is HelpDataRow r) text.SetText("{{C|" + r.CategoryId + "}}");   // ★ 用了键
//   ⇒ 数据侧**改不了**（`CategoryId` 是 `categoryExpanded` / `SelectIndex` 的键，§18 不可动）
//   ⇒ Postfix 里改用 `Description` 重设文本
//
//   ⚠ 顺带说明：`HelpRow::setData()` 的段标题走的是
//       `helpDataRow.Description.ToUpper()` —— 即 `DisplayTopic`。
//     2.16.35 给我方 `Manual.xml` 补了 12 条 `DisplayName` 之后，
//     `[-] SAVING THROWS` 这类**应当已是中文**（见 patch_notes 的验证项）。
//
// ─────────────────────────────────────────────────────────────────────────
// ③ 按键设置那一列**全大写**英文分类 —— `CategoriesInOrder` 的英文空壳
// ─────────────────────────────────────────────────────────────────────────
// 取证 D（大写从哪来）：
//   `Qud.UI.KeybindRow::setData()`：
//       Strings._T("KeybindRow Category Description Text", "{{C|=description|upper=}}")
//              .SetArgument("description", categoryRow.CategoryDescription) …
//   ⇒ `|upper=` 就是「全大写」的来源；`=description=` 是 `CategoryDescription`
//
// 取证 E（`CategoryDescription` 从哪来）：
//   `Qud.UI.KeybindsScreen::QueryKeybinds()`：
//       foreach (string item in CommandBindingManager.CategoriesInOrder)
//           menuItems.Add(new KeybindCategoryRow { CategoryId = item,
//                                                  CategoryDescription = item });
//
// 取证 F（★ 根因：`CategoriesInOrder` 只增不减）：
//   `XRL.UI.CommandBindingManager::HandleCommandNode()`：
//       if (xml.TryParseAttribute<string>("Category", out var Value)
//           && CurrentParsingCommand.Category != Value)
//       {
//           …value.Remove(CurrentParsingCommand);     // 从旧分类里摘掉命令
//           CurrentParsingCommand.Category = Value;   // 换成新分类
//           if (!CategoriesInOrder.Contains(Value)) CategoriesInOrder.Add(Value);
//       }
//   ⇒ 官方 Base 先加载（英文分类入列），我方 `Commands.xml` 后加载（命令改挂中文分类）
//     ⇒ **15 个英文分类名全部变成「空壳」却仍留在 `CategoriesInOrder` 里**
//     ⇒ `QueryKeybinds` 给每个空壳都生成一个表头行 ⇒ 一列空的大写英文
//   （唯一会移除分类的是末尾的「能力过滤」分支，它走的是 `item3.Category`，
//     那时命令的 Category 已是中文 ⇒ 清不掉英文空壳）
//
//   ⇒ 修法：在 `LoadCommands` 之后 / `QueryKeybinds` 之前，把
//     `CommandsByCategory` 里已空的分类名从 `CategoriesInOrder` 剔除。
//     （同一处也修好了经典 `KeyMappingUI` 的索引栏 —— 它同样遍历 `CategoriesInOrder`）
//
//   ⇒ 保险网：万一某个英文分类**非空**（例如官方新增了命令而我方尚未覆盖），
//     `KeybindRow::setData` 之前把已知官方英文名换成中文，至少表头不露英文。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsModernUiPatch
    {
        private const string Tag = "[QudZhsCookbook] 现代界面：";

        internal static bool Disabled;

        private static bool _applied;
        private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>① 预制体默认标题：**精确相等**才换（不误伤任何其它文本）。</summary>
        private static readonly Dictionary<string, string> Titles =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "{{W|HELP}}",            "{{W|帮助}}" },
            { "{{W|CONTROL MAPPING}}", "{{W|按键设置}}" },
            // ★ 2.16.51：补完 `_scan_level0_titles.py` 列出的其余四个标题。
            //   它们是**预制体 `UITextSkin.text` 的序列化默认值**（§50 第四处：Unity 资源）
            //   ⇒ 三处（XML / 字符串表 / DLL）都搜不到，只能按**精确字面量**在 `UITextSkin.Apply()` 里改。
            //
            //   译名依据（§10 ①同一英文词同一译名）：
            //     · `HIGH SCORES` 是主菜单项**兼**该界面标题，与 `{{W|ACHIEVEMENTS}}`（`{{W|成就}}`）、
            //       `{{W|ENDED RUNS}}`（`{{W|已结束的历程}}`）并列 —— 后两条**走字符串表**且已译。
            //     · 同屏的 `leaderboard` 一律译**排行榜**：
            //         `HighScoresScreen Fetching Leaderboard` → 「正在获取排行榜」
            //         `HighScoresScreen FetchLeaderboard Empty Leaderboard` → 「排行榜为空」
            //       ⇒ 取「排行榜」（同一概念同一词）。若日后要更直白的「最高分」，只改这一行。
            //     · `TRADE` 与 `Cats["Trade"]`（命令分类）同译「交易」；两者作用域不同，互不影响。
            { "{{W|OPTIONS}}",          "{{W|设置}}" },
            { "{{W|HIGH SCORES}}",      "{{W|排行榜}}" },
            { "{{W|TRADE}}",            "{{W|交易}}" },
            { "{{W|SELECTED ABILITY}}", "{{W|选中的能力}}" },
            // ★ 2.16.52：角色面板的两个分组标题。
            //   判据（§50 第四处）：
            //     · 官方 `Strings.example.xml` 里 `SECONDARY ATTRIBUTES` / `RESISTANCES` **各 0 条**
            //       —— 对照 `MAIN ATTRIBUTES` 有 2 条（所以「主要属性」能译）
            //     · `level0`（Unity 预制体）里两者**各有 2 处** utf8 字面量
            //     · 反编译：只有 `primaryAttributesHeaderText` 有
            //       `SetText(Strings._S(…))`，另两个标题的文字在预制体里
            //   ⇒ 与 `HELP` / `CONTROL MAPPING` 同一类，走本白名单。
            //
            // ★★★ 2.16.125 修正：**键的形态写错了，这两个标题从来没被替换过**。
            //   用户截图 04 实证：同屏「主要属性」已是中文，而
            //   `SECONDARY ATTRIBUTES` / `RESISTANCES` **仍是英文**。
            //   离线取证（`_tools\_probe_level0_attrs.py` 扫 `level0` 的 utf8 字面量，
            //   打印命中点**前后 70 字节**）：
            //       CONTROL MAPPING  @1443293  `<color=#CFC041FF>CONTROL MAPPING </color>`
            //                        @2094112  `{{W|CONTROL MAPPING}}`      ← ✅ 有 `{{W|}}` 形态
            //       HELP             @1514621  `<color=#CFC041FF>HELP </color>`
            //                        @1919856  `{{W|HELP}}`                 ← ✅ 同上
            //       SECONDARY ATTRIBUTES @1324572 `SECONDARY ATTRIBUTES `  ← ★ **裸文本**
            //                            @1958172 `SECONDARY ATTRIBUTES`   ← ★ 也是裸的
            //       RESISTANCES      @1517340  `RESISTANCES `              ← ★ 裸文本
            //                            @1990588  `RESISTANCES`            ← ★ 裸的
            //   ⇒ **`{{W|SECONDARY ATTRIBUTES}}` 这个串在预制体里根本不存在** ⇒
            //     字典精确匹配永远不命中 ⇒ 英文照旧。
            //   ⇒ 改成**裸文本**键；替换值也**不带 `{{W|}}`**（原文本本就没有 markup，
            //     加了会凭空多出金色样式，与相邻的 `主要属性` 视觉不一致）。
            //   ⚠ 顺带处理**带尾随空格**的形态（@1324572 / @1517340 都是 `… ` 带空格）。
            { "SECONDARY ATTRIBUTES",  "次要属性" },
            { "SECONDARY ATTRIBUTES ", "次要属性" },
            { "RESISTANCES",           "抗性" },
            { "RESISTANCES ",          "抗性" },
            // 保留旧键：万一别的预制体/版本用 `{{W|}}` 形态，命中即用（无害）
            { "{{W|SECONDARY ATTRIBUTES}}", "{{W|次要属性}}" },
            { "{{W|RESISTANCES}}",          "{{W|抗性}}" },
            // ★ 2.16.126：`Psychic Glimmer` —— `CharacterStatusScreen` 里 **C# 硬编码**的变异名。
            //   离线定位：调用点表里两条
            //       `CharacterStatusScreen → IPart::SetName("Psychic Glimmer")`         ← ★ **这是查表键，不能动**
            //       `CharacterStatusScreen → BaseMutation::SetDisplayName("Psychic Glimmer")` ← 纯显示 setter
            //   面板大标题 = `mutationNameText.SetText("{{B|" + mutation.GetDisplayName() + "}}")`
            //   ⇒ 走到 `UITextSkin` 时是 `{{B|Psychic Glimmer}}` ⇒ 精确键即可命中。
            //   列表行是 `Psychic Glimmer (40)` 这种**拼接**形态 ⇒ 由下面的**片段替换**兜住。
            { "{{B|Psychic Glimmer}}", "{{B|心灵微光}}" },
        };

        /// <summary>③ 官方 `Commands.xml` 英文分类名 → 我方中文分类（保险网）。</summary>
        private static readonly Dictionary<string, string> Cats =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Basic Move / Attack",          "移动" },
            { "Menus",                        "菜单" },
            { "Adventuring",                  "冒险" },
            { "Advanced Adventuring",         "高级冒险" },
            { "Targeting",                    "目标" },
            { "Ability Bar",                  "能力栏" },
            { "Shortcuts to Character Sheet", "状态" },
            { "Character Sheet",              "状态" },
            { "Trade",                        "交易" },
            { "Character Creation",           "人物创建" },
            { "UI",                           "显示" },
            { "System",                       "系统" },
            { "Mouse-specific",               "鼠标操作" },
            { "Debug",                        "调试" },
            // `InternalUI` 我方也是英文名，保持一致不动
        };

        /// <summary>
        /// ★★ 2.16.111：**运行时拼装的步骤名**前缀表（`SpreadPax` 任务）。
        ///
        /// 【为什么不能用精确表】步骤名是**运行时 Concat 拼的**
        ///   （`XRL.World.QuestManagers.SpreadPax::Init()`，`decomp member` 实证）：
        ///     "Spread Klanq Deep in the Earth"            ← 整句（地下）
        ///     "Spread Klanq to "  + 阵营名.ToTitleCase()   ← 拼阵营
        ///     "Spread Klanq onto " + 蓝图名                ← 拼物品
        ///     "Spread Klanq " + "in " + 地点名             ← 拼地点
        ///   ⚠ **数据侧管不了**：`PaxQuestStep` 不在 `Quests.xml` 里
        ///     （实测 `grep "Spread Klanq" Quests.xml` → 0 条）
        ///   ⇒ 只能按**前缀**译，拼接部分原样保留。
        ///
        /// 译名依据（§10 ① 同一英文词同一译名）：
        ///   · `Klanq`  = **克兰克**（全模组统一：帕克斯·克兰克）
        ///   · `puff`   = **喷**（`Klanq puff at you.` → 「克兰克朝你喷。」）
        ///   · `spores` = **孢子**（`Immune to fungal spores.` → 「免疫真菌孢子。」）
        ///   · `Deep in the Earth` = **地底深处**（同任务描述
        ///     `Puff Klanq spores at a depth of at least 20 levels.` 可印证）
        ///
        /// ⚠⚠ **顺序即优先级**：查找取**首个**命中 ⇒ 必须**长前缀在前**，
        ///    否则 `"Spread Klanq "` 会先命中、把 `to`/`onto`/`Deep…` 一起吞进拼接部分。
        /// </summary>
        internal static readonly KeyValuePair<string, string>[] STEP_PREFIX =
        {
            new KeyValuePair<string, string>("Spread Klanq Deep in the Earth", "在地底深处散播克兰克"),
            // ⚠⚠ 下面这几条**必须**逐一带上介词 —— 实证游戏拼的是
            //    `"Spread Klanq " + "in " + 地点名`（介词是**拼进去的一段**）
            //    ⇒ 若只写通用前缀 `"Spread Klanq "`，会产出「在**in** 约帕」（残渣）
            new KeyValuePair<string, string>("Spread Klanq onto ", "散播克兰克孢子到"),
            new KeyValuePair<string, string>("Spread Klanq into ", "向"),
            new KeyValuePair<string, string>("Spread Klanq in ",   "在"),
            new KeyValuePair<string, string>("Spread Klanq to ",   "向"),
            new KeyValuePair<string, string>("Spread Klanq on ",   "在"),
            new KeyValuePair<string, string>("Spread Klanq ",      "散播克兰克："),
            new KeyValuePair<string, string>("Puff Klanq spores at a depth of at least 20 levels.",
                                             "在至少 20 层深处喷洒克兰克孢子。"),
            new KeyValuePair<string, string>("Puff Klanq spores on a sentient member of the ",
                                             "向以下阵营的智慧成员喷洒克兰克孢子："),
            new KeyValuePair<string, string>("Puff Klanq spores in the vicinity of ",
                                             "在以下地点附近喷洒克兰克孢子："),
            new KeyValuePair<string, string>("Puff Klanq spores onto ", "向"),
            new KeyValuePair<string, string>("Puff Klanq spores on ",   "向"),
            new KeyValuePair<string, string>("Puff Klanq spores in ",   "在"),
        };

        private static FieldInfo _skinTextField;   // XRL.UI.UITextSkin.text
        private static MethodInfo _skinApply;      // XRL.UI.UITextSkin.Apply()
        private static MethodInfo _skinSetText;    // XRL.UI.UITextSkin.SetText(string)
        private static FieldInfo _sideTextField;   // Qud.UI.LeftSideCategory.text
        private static Type _cmdType;              // XRL.UI.CommandBindingManager
        private static FieldInfo _fOrder;          //   .CategoriesInOrder
        private static FieldInfo _fByCat;          //   .CommandsByCategory
        private static readonly Dictionary<Type, FieldInfo> _fieldCache = new Dictionary<Type, FieldInfo>();

        // ═══════════════════════════════════════════════════════════════════
        // ★★ 2.16.52 —— 物品栏 / 交易界面的**分类页签**（`FilterBarCategoryButton`）
        // ═══════════════════════════════════════════════════════════════════
        //
        // 【为什么之前译不到】
        //   反编译 `Qud.UI.FilterBarCategoryButton`：
        //       public static Dictionary<string, string> categoryTextMap =
        //           new Dictionary<string, string> { { "*All", "ALL" } };   // ★ 官方只填了一个
        //       public void SetCategory(string category, string tooltip = null) {
        //           string value2;
        //           if (!categoryTextMap.TryGetValue(category, out value2))  // ← 查不到就回退
        //               value2 = category;
        //           text.SetText(value2);                                    // ← 直接 set，不查字符串表
        //       }
        //   ⇒ `text.SetText(...)` **不经字符串表**，所以那 40 条
        //      `Context="Physics (Inventory) Category"` 对它**完全无效**
        //      （判据：`Player.log` 里这些分类名的 `String table miss` = **0 条**，
        //        对照：`*All` 有 1 条 miss —— 说明查表确实发生过，只是键带 `*`，
        //        而我们那 40 条都没有 `*` 前缀）。
        //
        // 【修法】官方**自带扩展点**：那张 `categoryTextMap` 就是给人查的。
        //   给静态构造函数挂 Postfix，把我们已有的中文**灌进去**即可
        //   （§12「优先用数据侧扩展点」的同族：官方留了表，我们填表）。
        //
        // 【译名来源】全部取自**我方既有的** `Physics (Inventory) Category` 条目
        //   （`Strings.xml` 40 条），不另拟名（§10 ①）。其中 9 个键没有对应条目，
        //   按既有口径补：`Natural Missile Weapons` 沿 `Natural Missile Weapon`＝天然远程武器；
        //   `*Mods` 系列按游戏内既有译法「改造」（`物品改造` / `改造图纸` / `可改造的物品`）。
        private static readonly Dictionary<string, string> CatText =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
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
            { "Clothes",                 "衣物" },
            { "Trade Goods",             "贸易货物" },
            { "Quest Items",             "任务物品" },
            { "Data Disks",              "数据盘" },
            { "Scrap",                   "废料" },
            { "Trinkets",                "饰品" },
            { "Artifacts",               "器物" },
            { "Cybernetic Implants",     "义体植入物" },
            { "CommonMods",              "通用改造" },
            { "WeaponMods",              "武器改造" },
            { "BladeMods",               "刃类改造" },
            { "LongBladeMods",           "长刃改造" },
            { "CudgelMods",              "棍棒改造" },
            { "AxeMods",                 "斧类改造" },
            { "ThrownWeaponMods",        "投掷武器改造" },
            { "GrenadeMods",             "手雷改造" },
            { "MissileWeaponMods",       "远程武器改造" },
        };

        internal static int CatHits;

        /// <summary>
        /// ★★ 2.16.58：**按用户要求关闭**——分类页签保持英文。
        ///
        /// 用户原话：「分类页签上面不要显示中文」（2026-10-09）。
        ///
        /// 判据保留：官方 `categoryTextMap` 只填了 `{ "*All", "ALL" }`；
        /// 2.16.52 曾把 43 条中文灌进去 ⇒ 页签显示中文。
        /// 现改为**整段不挂载**（而不是进去就 return，免得编译器报 CS0162）。
        /// ⚠ 译表 `CatText` 与 `CatMapPost()` 都保留在源码里 —— 日后要开回来，
        ///   只需把这里的 `false` 改成 `true`。
        /// </summary>
        internal static readonly bool EnableCategoryTextMap = false;

        /// <summary>静态构造函数 Postfix：把中文灌进官方 `categoryTextMap`。</summary>
        private static void CatMapPost()
        {
            try
            {
                Type t = FindType("Qud.UI.FilterBarCategoryButton");
                if (t == null) return;
                FieldInfo f = t.GetField("categoryTextMap",
                    BindingFlags.Public | BindingFlags.Static);
                if (f == null) return;
                var map = f.GetValue(null) as Dictionary<string, string>;
                if (map == null) return;
                int n = 0;
                foreach (var kv in CatText)
                {
                    if (!map.ContainsKey(kv.Key)) { map[kv.Key] = kv.Value; n++; }
                }
                CatHits++;
                if (CatHits <= 2)
                {
                    LogOnce("catmap", Tag + "分类页签：已补 " + n + " 条中文（表内共 "
                        + map.Count + " 条）");
                }
            }
            catch (Exception e)
            {
                LogOnce("catmaperr", Tag + "分类页签补录失败：" + e.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────── 挂载

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type skin = FindType("XRL.UI.UITextSkin");
                Type left = FindType("Qud.UI.LeftSideCategory");
                Type row = FindType("Qud.UI.KeybindRow");
                Type scr = FindType("Qud.UI.KeybindsScreen");
                _cmdType = FindType("XRL.UI.CommandBindingManager");

                int n = 0;
                var names = new List<string>();

                // ① 标题：Prefix 改 `text` 字段，原方法体随后照中文渲染（不递归）
                if (skin != null)
                {
                    _skinTextField = skin.GetField("text", BindingFlags.Public | BindingFlags.Instance);
                    _skinApply = FindMethod(skin, "Apply", 0, false);
                    _skinSetText = FindMethod(skin, "SetText", 1, false);
                }
                if (_skinApply != null && _skinTextField != null)
                {
                    Patch(_skinApply, "modernui.title", "TitlePrefix", null);
                    n++; names.Add("UITextSkin.Apply");
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 UITextSkin.Apply/text ⇒ 标题补丁跳过");
                }

                // ② 帮助左侧栏
                if (left != null)
                {
                    _sideTextField = left.GetField("text", BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo m = FindMethod(left, "setData", 1, false);
                    if (m != null && _sideTextField != null && _skinSetText != null)
                    {
                        Patch(m, "modernui.sidebar", null, "SidePost");
                        n++; names.Add("LeftSideCategory.setData");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(Tag + "LeftSideCategory 缺 setData/text ⇒ 左侧栏跳过");
                    }
                }

                // ③-a 表头保险网
                if (row != null)
                {
                    MethodInfo m = FindMethod(row, "setData", 1, false);
                    if (m != null)
                    {
                        Patch(m, "modernui.catrow", "RowPre", null);
                        n++; names.Add("KeybindRow.setData");
                    }
                }

                // ③-b 空壳分类清理（根因）：`LoadCommands` 之后 + 每次打开前
                if (_cmdType != null)
                {
                    _fOrder = _cmdType.GetField("CategoriesInOrder",
                        BindingFlags.Public | BindingFlags.Static);
                    _fByCat = _cmdType.GetField("CommandsByCategory",
                        BindingFlags.Public | BindingFlags.Static);
                    MethodInfo load = FindMethod(_cmdType, "LoadCommands", 0, true);
                    if (load != null && _fOrder != null && _fByCat != null)
                    {
                        Patch(load, "modernui.cleancat", null, "CleanPost");
                        n++; names.Add("CommandBindingManager.LoadCommands");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(Tag + "CommandBindingManager 缺 LoadCommands/"
                            + "CategoriesInOrder/CommandsByCategory ⇒ 分类清理跳过");
                    }
                }
                if (scr != null)
                {
                    MethodInfo q = FindMethod(scr, "QueryKeybinds", 0, false);
                    if (q != null)
                    {
                        Patch(q, "modernui.cleanpre", "CleanPre", null);
                        n++; names.Add("KeybindsScreen.QueryKeybinds");
                    }
                }

                // ④ 物品栏/交易 分类页签：官方 `categoryTextMap` 只填了 `*All` ⇒ 补全中文
                //    挂静态构造函数（`.cctor`）的 Postfix，并**主动触发一次** `cctor`
                //    （该类型可能还没被碰过；用 RunClassConstructor 定向触发，
                //      避免为了碰它而 `GetMethods()` 把它整个初始化掉）。
                {
                    Type fb = FindType("Qud.UI.FilterBarCategoryButton");
                    if (!EnableCategoryTextMap)
                    {
                        // ★ 2.16.58：按用户要求 —— 分类页签保持英文，整段跳过
                        UnityEngine.Debug.Log(Tag
                            + "分类页签中文：**已按用户要求关闭**（页签保持英文）");
                    }
                    else if (fb != null)
                    {
                        MethodInfo post = typeof(ZhsModernUiPatch).GetMethod("CatMapPost",
                            BindingFlags.NonPublic | BindingFlags.Static);
                        ConstructorInfo cc = fb.TypeInitializer;
                        if (cc != null && post != null)
                        {
                            // 注意：`Patch(...)` 只接受 MethodInfo；构造函数要直接调 Harmony
                            try
                            {
                                Harmony hc = new Harmony("Nattens.QudZhsCookbook.modernui.catmap");
                                hc.Patch(cc, postfix: new HarmonyMethod(post));
                                n++; names.Add("FilterBarCategoryButton.cctor");
                            }
                            catch (Exception ex)
                            {
                                UnityEngine.Debug.LogError(Tag
                                    + "分类页签挂载失败：" + ex.Message);
                            }
                            try
                            {
                                // 先触发一次，把中文灌进去；顺序无妨（Patch 已就位）
                                System.Runtime.CompilerServices.RuntimeHelpers
                                    .RunClassConstructor(fb.TypeHandle);
                            }
                            catch { }
                            // 兜底：若上面触发时补丁尚未生效，这里再直接补一次
                            CatMapPost();
                        }
                        else
                        {
                            UnityEngine.Debug.LogError(Tag
                                + "FilterBarCategoryButton 缺 .cctor 或 Postfix ⇒ 分类页签跳过");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(Tag + "找不到 Qud.UI.FilterBarCategoryButton");
                    }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口："
                                      + string.Join(" / ", names.ToArray())
                                      + "（标题 " + Titles.Count + " 条 · 分类名 "
                                      + Cats.Count + " 条 · 页签 " + CatText.Count + " 条）");

                // 界面若在挂载前就已实例化（`Start()` 已跑过），补一次全量扫描
                Sweep();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void Patch(MethodInfo target, string id, string prefix, string postfix)
        {
            Harmony h = new Harmony("Nattens.QudZhsCookbook." + id);
            h.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(
                    typeof(ZhsModernUiPatch).GetMethod(prefix,
                        BindingFlags.NonPublic | BindingFlags.Static)),
                postfix: postfix == null ? null : new HarmonyMethod(
                    typeof(ZhsModernUiPatch).GetMethod(postfix,
                        BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // ─────────────────────────────────────────────────────── ① 标题替换

        private static void TitlePrefix(object __instance)
        {
            if (Disabled || __instance == null) return;
            try
            {
                string s = _skinTextField.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(s)) return;

                // ★★ 2.16.127：**单位词替换**（`rounds` ⇒ 回合）—— 用户指示「不管出口，全部翻译」。
                //   变异详情面板的「持续 3rounds」「冷却：80rounds」走的就是这条
                //   （`Mutation::GetLevelText` 的 `String.Concat` + `StatCollector::Set`，
                //   不经过字符串表，见 `patch_notes/2.16.124.md` §五）。
                {
                    string su = ZhsUnits.Fix(s);
                    if (su != s)
                    {
                        s = su;
                        _skinTextField.SetValue(__instance, s);
                    }
                }

                // ① 标题精确表（预制体默认值）
                //   ★★ 2026-10-15 拆门禁（子代理 C 取证）：原来这里是
                //        `if (s.Length >= 6 && s[0] == '{')`
                //      —— 那道门禁假定「标题键都是 `{{W|…}}` 形态」。但
                //      `SECONDARY ATTRIBUTES` / `RESISTANCES` 在预制体里是**裸文本**
                //      （字节级实证：`level0` 里就是 `SECONDARY ATTRIBUTES`(20) /
                //        `RESISTANCES`(11)，无 `{{W|}}`、无尾随空格），首字符 'S'/'R'
                //      ⇒ **连查表都不会发生** ⇒ 2.16.125 补的 4 条裸键在主路径上是**死代码**
                //      （用户截图 04 一直英文的真正原因）。
                //   ⇒ 去掉门禁：`Titles` 是 `StringComparer.Ordinal` 的**整串精确**匹配、
                //      键又都是长串（最短 11 字符）⇒ 不存在「短词抢先」式误伤；
                //      命中即 `return`，与原行为一致（只是把裸键也纳入）。
                //   ⇒ 两条 `{{W|SECONDARY ATTRIBUTES}}` / `{{W|RESISTANCES}}` 是**死键**
                //      （level0 里 0 命中），**保留**无害、且历史记录用得上。
                if (s.Length >= 6)
                {
                    string zh;
                    if (!Titles.TryGetValue(s, out zh))
                    {
                        // 容错：预制体值可能带首尾空白
                        string t = s.Trim();
                        if (t != s) Titles.TryGetValue(t, out zh);
                    }
                    if (!string.IsNullOrEmpty(zh))
                    {
                        _skinTextField.SetValue(__instance, zh);
                        if (_logged.Add("T:" + s))
                            UnityEngine.Debug.Log(Tag + "标题替换 [" + s + "] ⇒ [" + zh + "]");
                        return;
                    }
                }

                // ★★ 2.16.125：变异详情弹窗的两个**段落标签**（C# 直拼，三处都搜不到）
                //   反编译 `Qud.UI.CharacterStatusScreen::HandleHighlightMutation`：
                //       textBuilder.Compound("{{w|This rank}}:\n", "\n\n")
                //                  .Append(mutation.GetLevelText(Level));
                //       textBuilder.Append("\n\n{{w|Next rank}}:\n")
                //                  .Append(mutation.GetLevelText(Level + 1));
                //   ⇒ `{{w|This rank}}:` / `{{w|Next rank}}:` 是**字面量片段**，
                //     拼在等级文本**前面** ⇒ 整串随等级文本变化 ⇒ **不能用精确表**。
                //   离线实证（`_tools\_offline_triage_shots.py`）：
                //     整串 `This rank:` **我方表 / 官方 XML / DLL heap 三处都没有**，
                //     只有 `{{w|This rank}}` 这个片段在 DLL 里（用户截图 05 显示的正是英文）。
                //   ⇒ 用**片段替换**，且只在文本确实含该片段时动（零误伤）。
                if (s.IndexOf("{{w|This rank}}", StringComparison.Ordinal) >= 0
                    || s.IndexOf("{{w|Next rank}}", StringComparison.Ordinal) >= 0)
                {
                    // ★★ 2.16.126：用户实测「`下一级：` 前面**掉了换行**」。
                    //   游戏侧原文（`CharacterStatusScreen::HandleHighlightMutation`）：
                    //       textBuilder.Compound("{{w|This rank}}:\n", "\n\n").Append(levelText);
                    //       textBuilder.Append("\n\n{{w|Next rank}}:\n").Append(nextLevelText);
                    //   ⇒ 两处**都**带 `\n\n`，但实测只有 `本级` 那处渲染出空行、
                    //     `下一级` 那处与上一句连在一起（「…反射基于光的伤害。下一级： 你可以在…」）。
                    //   ⚠ 已排除的原因：不是本方法里 `return` 提前返回造成的
                    //     —— `ZhsPopupPatch.Reassemble` 对这段文本**返回 null**
                    //        （`REASM` 里没有匹配规则），跳过它等于什么都没做。
                    //   ⇒ 根因**未定**（无法离线看运行时字符串）⇒ 两手都做：
                    //     ① **保险修复**：`{{w|Next rank}}:` 前若**没有**换行就补 `\n\n`
                    //        （用 `(?<![\r\n])` 负向回顾 ⇒ 已经有换行的**不会**被补成三连）
                    //     ② **原文日志**：首次把原始串**转义后**打出来（`\n` 可见），
                    //        下次开局就能一眼确认「到底有没有 `\n\n`」⇒ 再决定要不要动根因
                    string rz2 = System.Text.RegularExpressions.Regex.Replace(
                        s, @"(?<![\r\n])\{\{w\|Next rank\}\}:", "\n\n{{w|Next rank}}:")
                        .Replace("{{w|This rank}}:", "{{w|本级}}：")
                        .Replace("{{w|Next rank}}:", "{{w|下一级}}：");
                    if (rz2 != s)
                    {
                        _skinTextField.SetValue(__instance, rz2);
                        if (_logged.Add("RANKRAW"))
                        {
                            UnityEngine.Debug.Log(Tag + "变异详情原文（转义，前 260 字）："
                                + rz2.Substring(0, Math.Min(260, rz2.Length))
                                    .Replace("\r", "\\r").Replace("\n", "\\n"));
                        }
                        if (_logged.Add("R:" + s.Length))
                            UnityEngine.Debug.Log(Tag + "变异详情段落标签：This/Next rank ⇒ 本级/下一级");
                        // ★★ 2026-10-15 修（子代理 B 取证）：原来是 `return;`
                        //   —— 它把**同一串**后面的 `ZhsPopupPatch.Reassemble` 兜底整个跳过，
                        //      而「有 `{{w|This rank}}` 标签的串」正是「有 rankBoost 行与
                        //      cost 行的串」⇒ 两件事被同一个 `return` 绑死 ✗
                        //   ⇒ 改为**落穿**：更新局部变量 `s`（下面几段都读写 `s`，
                        //      不更新的话它们会拿原文覆盖字段、且 `Reassemble` 也拿不到新串）。
                        s = rz2;
                    }
                }

                // ★★ 2.16.126：**专名片段替换**（C# 硬编码、且被拼进别的文本里的）
                //   `Psychic Glimmer` 在 `CharacterStatusScreen` 里是硬编码字面量：
                //       `mutation.SetDisplayName("Psychic Glimmer")`
                //   大标题（`{{B|Psychic Glimmer}}`）走上面的精确键；
                //   **变异列表那一行**是 `Psychic Glimmer（40）` 这种拼接形态 ⇒ 精确键够不到。
                //   判据：这是个**唯一的专名**（全游戏只有它），整串替换零误伤。
                if (s.IndexOf("Psychic Glimmer", StringComparison.Ordinal) >= 0)
                {
                    string rz3 = s.Replace("Psychic Glimmer", "心灵微光");
                    if (rz3 != s)
                    {
                        s = rz3;
                        _skinTextField.SetValue(__instance, s);
                        if (_logged.Add("PG"))
                            UnityEngine.Debug.Log(Tag + "Psychic Glimmer ⇒ 心灵微光（片段替换）");
                    }
                }

                // ★★ 2.16.111：**运行时动态构造的步骤名**（前缀式匹配）
                //   来源：`XRL.World.QuestManagers.SpreadPax::Init()`
                //     （`decomp member` 实证），步骤名是**运行时 Concat 拼的**：
                //       "Spread Klanq Deep in the Earth"          ← 整句（地下）
                //       "Spread Klanq to " + 阵营名.ToTitleCase()  ← 拼阵营
                //       "Spread Klanq onto " + 蓝图名              ← 拼物品
                //       "Spread Klanq " + "in " + 地点名           ← 拼地点
                //       （`PaxQuestStep.Name`，同类还有 `Puff Klanq spores …`）
                //   ⇒ ⚠ **数据侧管不了**：`PaxQuestStep` 不在 `Quests.xml` 里
                //     （实测 `grep "Spread Klanq" Quests.xml` → 0 条）
                //     所以只能在这个出口按**前缀**译，拼接部分原样保留。
                //   ⚠ 必须**长前缀优先**，否则 `Spread Klanq ` 会先命中、把后面的也吃掉
                {
                    string t2 = s.Trim();
                    string zh2 = null;
                    foreach (var kv in STEP_PREFIX)
                    {
                        if (t2.StartsWith(kv.Key, StringComparison.Ordinal))
                        {
                            zh2 = kv.Value + t2.Substring(kv.Key.Length);
                            break;
                        }
                    }
                    if (zh2 != null)
                    {
                        _skinTextField.SetValue(__instance, zh2);
                        if (_logged.Add("S:" + s))
                            UnityEngine.Debug.Log(Tag + "步骤名替换 [" + s + "] ⇒ [" + zh2 + "]");
                        return;
                    }
                }

                // ★★ 2.16.99：**拼装文本兜底** —— 让 `UITextSkin` 这条出口也能用
                //   `ZhsPopupPatch.Reassemble` 的规则表。
                //
                //   【为什么必需】变异详情面板的「等级变化原因」行来自
                //   `XRL.World.Parts.Mutation.BaseMutation::CalcLevel` 的硬编码拼接：
                //       "+ This " + Grammar.MakePossessive(text) + " rank is increased by "
                //       + value.Modifier + " due to your high " + value.Name + "."
                //   它**不经过消息队列**（`Player.log` 搜不到）⇒ 只走本出口渲染。
                //   而原实现有 `s[0] != '{' ⇒ return` 的早退，把这类行**全部挡掉**。
                //
                //   ★ `Reassemble` 自带判据（**必须同时含中文和英文**）⇒ 纯英文/纯中文
                //     一律放行 ⇒ 在这里调用对 UI 其它文本**零影响**。
                if (s.Length < 8) return;
                string rz = ZhsPopupPatch.Reassemble(s);
                if (string.IsNullOrEmpty(rz) || rz == s) return;
                _skinTextField.SetValue(__instance, rz);
                if (_logged.Add("R:" + s))
                    UnityEngine.Debug.Log(Tag + "界面拼装改写 [" + s + "] ⇒ [" + rz + "]");
            }
            catch (Exception e)
            {
                LogOnce("title-err", Tag + "标题替换出错：" + e.Message);
            }
        }

        /// <summary>界面在补丁挂载前已 `Start()` 过 ⇒ 对已实例化的 `UITextSkin` 补一次。</summary>
        private static void Sweep()
        {
            try
            {
                if (_skinApply == null || _skinTextField == null) return;
                Type t = FindType("XRL.UI.UITextSkin");
                if (t == null) return;
                UnityEngine.Object[] all = UnityEngine.Resources.FindObjectsOfTypeAll(t);
                if (all == null) return;
                int hit = 0;
                foreach (UnityEngine.Object o in all)
                {
                    if (o == null) continue;
                    string s = _skinTextField.GetValue(o) as string;
                    string zh;
                    if (s == null || !Titles.TryGetValue(s, out zh)) continue;
                    _skinTextField.SetValue(o, zh);
                    try { _skinApply.Invoke(o, null); } catch { }
                    // ★ 2026-10-15：只标记**真正改写过**的这个键。
                    //   原来在下面 `foreach (string k in Titles.Keys) _logged.Add("T:" + k);`
                    //   把 13 条键**全部**预标成「已记录」⇒ 主路径（Apply Prefix）即使替换成功，
                    //   `_logged.Add("T:" + s)` 也返回 false ⇒ 不再打日志
                    //   ⇒ 本局 `Player.log` 里「标题替换」0 条，**既不能证明生效也不能证明没生效**。
                    _logged.Add("T:" + s);
                    hit++;
                }
                if (hit > 0)
                    UnityEngine.Debug.Log(Tag + "挂载时扫描：改写 " + hit + " 个已实例化标题");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载时扫描出错：" + e.Message);
            }
        }

        // ─────────────────────────────────────────────────── ② 帮助左侧栏

        private static void SidePost(object __instance, object __0)
        {
            if (Disabled || __instance == null || __0 == null) return;
            try
            {
                string tn = __0.GetType().Name;
                string shown = null;

                if (tn == "HelpDataRow")
                {
                    // `CategoryId` 是键（逻辑用）不可动 ⇒ 显示改用 `Description`
                    // （= `XRLManualPage.DisplayTopic` = 我方 `<topic DisplayName>`）
                    string disp = GetStr(__0, "Description");
                    if (!string.IsNullOrEmpty(disp)) shown = "{{C|" + disp + "}}";
                }
                else if (tn == "KeybindCategoryRow")
                {
                    string cat = GetStr(__0, "CategoryDescription");
                    string zh;
                    if (cat != null && Cats.TryGetValue(cat, out zh)) shown = "{{C|" + zh + "}}";
                }

                if (shown == null) return;
                object skin = _sideTextField.GetValue(__instance);
                if (skin == null) return;
                _skinSetText.Invoke(skin, new object[] { shown });

                if (_logged.Add("S:" + shown))
                    UnityEngine.Debug.Log(Tag + "左侧栏替换 ⇒ " + shown);
            }
            catch (Exception e)
            {
                LogOnce("side-err", Tag + "左侧栏替换出错：" + e.Message);
            }
        }

        // ─────────────────────────────────────────────── ③ 分类表头 / 空壳清理

        private static void RowPre(object __0)
        {
            if (Disabled || __0 == null) return;
            try
            {
                if (__0.GetType().Name != "KeybindCategoryRow") return;
                FieldInfo f = FieldOf(__0.GetType(), "CategoryDescription");
                if (f == null) return;
                string cat = f.GetValue(__0) as string;
                string zh;
                if (cat == null || !Cats.TryGetValue(cat, out zh)) return;
                f.SetValue(__0, zh);
                if (_logged.Add("R:" + cat))
                    UnityEngine.Debug.Log(Tag + "分类表头替换 [" + cat + "] ⇒ [" + zh + "]");
            }
            catch (Exception e)
            {
                LogOnce("row-err", Tag + "分类表头替换出错：" + e.Message);
            }
        }

        private static void CleanPost()
        {
            if (Disabled) return;
            CleanCategories();
        }

        private static void CleanPre()
        {
            if (Disabled) return;
            if (_logged.Add("q-open"))
                UnityEngine.Debug.Log(Tag + "按键设置：QueryKeybinds 首次触发（打开前清理空壳分类）");
            CleanCategories();
        }

        /// <summary>
        /// 把 `CategoriesInOrder` 里「`CommandsByCategory` 已空」的分类名剔除。
        /// 返回剔除条数。**这是那一列大写英文分类的根因修复。**
        /// </summary>
        internal static int CleanCategories()
        {
            if (_fOrder == null || _fByCat == null) return 0;
            try
            {
                IList order = _fOrder.GetValue(null) as IList;
                IDictionary byCat = _fByCat.GetValue(null) as IDictionary;
                if (order == null || byCat == null) return 0;

                List<object> dead = null;
                foreach (object k in order)
                {
                    ICollection list = byCat[k] as ICollection;
                    if (list != null && list.Count > 0) continue;
                    if (dead == null) dead = new List<object>();
                    dead.Add(k);
                }
                if (dead == null) return 0;

                var names = new List<string>();
                foreach (object k in dead)
                {
                    order.Remove(k);
                    names.Add(Convert.ToString(k));
                }
                if (_logged.Add("C:" + names.Count))
                    UnityEngine.Debug.Log(Tag + "剔除 " + names.Count + " 个空壳分类："
                                          + string.Join(" / ", names.ToArray()));
                return names.Count;
            }
            catch (Exception e)
            {
                LogOnce("clean-err", Tag + "清理分类出错：" + e.Message);
                return 0;
            }
        }

        // ────────────────────────────────────────────────────────── 反射小工具

        private static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type t, string name, int argCount, bool isStatic)
        {
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                if (m.IsStatic != isStatic) continue;
                if (m.GetParameters().Length != argCount) continue;
                return m;
            }
            return null;
        }

        private static FieldInfo FieldOf(Type t, string name)
        {
            FieldInfo f;
            if (_fieldCache.TryGetValue(t, out f) && f != null && f.Name == name) return f;
            f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            _fieldCache[t] = f;
            return f;
        }

        private static string GetStr(object o, string field)
        {
            FieldInfo f = FieldOf(o.GetType(), field);
            return f == null ? null : f.GetValue(o) as string;
        }

        private static void LogOnce(string key, string msg)
        {
            if (_logged.Add(key)) UnityEngine.Debug.LogError(msg);
        }
    }
}
