// ═══════════════════════════════════════════════════════════════════════════
//  量词 · 按物品类型（纯函数，可离线跑样本）
// ═══════════════════════════════════════════════════════════════════════════
//
//  【为什么需要它】现有 `ZhsTextFilterPatch.MW` 是**中文名首字表**（178 条，92 条死键），
//    在最终文本出口（只剩字符串、拿不到对象）运行 ⇒ 4821 件有中文名的物品里
//    **4164 件（86.4%）**回退成「一个」。同一件「透镜」拿到「具」（尸体量词）。
//
//  【本类的位置】在**拿得到 GameObject** 的出口调用（`Translator.GameObjectDisplayName`
//    的 Postfix）⇒ 按 `Inherits` 链 / 部件判类型，而不是猜名字首字。
//
//  【用户裁定 · 2026-10-16】三条：
//    ① 走路线 A：在出口按类型**补量词**（不是把冠词删掉了事）
//    ② 整尸 vs 尸块判据 = **看中文名结尾**（`尸体|遗骸|尸骸|残骸|尸身|尸`）
//       实测：43/43 命中、0/25 误伤 —— 12 个候选判据里**唯一**有效的
//       （★ 表 A 原写「部件 Butcherable 是整尸唯一判据」**不准确**：
//          带 Butcherable 的 63 件**全在** `Inherits Corpse` 链上，
//          而该链共 1014 件 ⇒ 两者是**大圈套小圈**，不是「整尸 vs 尸块」）
//    ③ **先做无争议的 23 类**；13 个存疑类（T08/T14/T15/T16/T16B/T17/T23/T24/T26/T27/T32…）
//       一律 return null ⇒ 回退现有首字表，行为**与改动前一致**
//
//  【求值顺序】照抄 `_tools\_mw_census.py` 的 `RULES` 列表（那份列表就是表 A 的求值序）。
//    ★ 顺序有意义：`AmmoArrow` 必须先于 `MeleeWeapon`（箭也继承近战武器）；
//      `Shield` 必须先于 `Armor`（盾也带 Armor）；整尸/尸块必须先于 `Food`/`Item`
//      （否则「熊尸」会变「一份熊尸」）。
//
//  【返回值三态】与 `ZhsTextFilterPatch.MeasureFor` 一致，调用方必须分清：
//      null  —— 本函数**不表态** ⇒ 回退现有首字表 / 「个」
//      ""    —— **明确不加**量词（不可数 / 集合名词 / 地形）
//      非空  —— 量词本体（`把` / `只` / `具` …）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsMeasureWord
    {
        // ── 日志计数（供「首次触发」自证；§46：只写"已挂"不算证据）──
        internal static int Calls;
        internal static int Typed;
        internal static int Content;

        /// <summary>整尸判据：中文名以这些词结尾（可带一个收尾括号）。43/43 · 0/25 实测。</summary>
        private static readonly Regex WholeCorpse = new Regex(
            @"(尸体|遗骸|尸骸|残骸|尸身|尸)[\]\)）》」]?$", RegexOptions.Compiled);

        /// <summary>
        /// 按类型取量词。
        /// <para>`zh` = 该对象**已中文化**的显示名（用于整尸判据）；传 null 时跳过该判据。</para>
        /// </summary>
        internal static string For(XRL.World.GameObject go, string zh)
        {
            if (go == null) return null;
            try
            {
                Calls++;
                XRL.World.GameObjectBlueprint bp = go.GetBlueprint();
                if (bp == null) return null;
                Facts f = Facts.Of(bp);
                // ★ 整尸判据要**中文名**：在这里填（`Of` 只认蓝图）
                f.WholeCorpseName = !string.IsNullOrEmpty(zh) && WholeCorpse.IsMatch(zh);
                return Classify(f);
            }
            catch
            {
                return null;   // 分类绝不能影响显示
            }
        }

        /// <summary>
        /// ★ 纯函数：判据与顺序全在这里，**不碰 GameObject**。
        /// <para>离线样本（`_tools\roslyntest -- measure`）直接用真实普查数据构造 `Facts`
        /// 调本函数 ⇒ 验的是**同一份规则**，不是抄一份逻辑（§43 / 「自证≠验证」）。</para>
        /// <para>返回三态：`null` 不表态 · `""` 明确不加 · 非空 = 量词本体。</para>
        /// </summary>
        internal static string Classify(Facts f)
        {
            if (f == null) return null;
            Typed++;

            // ── 尸体族（必须最先，否则会被 Food / Item 抢走）──
            if (f.Butcherable) return "具";                                  // 整尸（可屠宰）
            if (f.CorpseChain)
                return f.WholeCorpseName ? "具" : "块";                       // 整尸 / 尸块·器官

            // ── 弹药（★ 必须先于武器：箭也继承 MeleeWeapon）──
            if (f.AmmoArrow) return "支";
            if (f.AmmoGrenade) return "枚";
            // 注：T08 `AmmoDart`（飞镖/注射器）属存疑项，**有意未实现**

            // ── 武器 ──
            if (f.MeleeWeaponChain) return "把";
            if (f.MissileWeaponChain) return "把";

            // ── 穿戴（★ 盾必须先于护甲：盾也带 Armor）──
            if (f.Shield) return "面";
            if (f.Armor) return "件";
            if (f.Cybernetics) return "件";

            // ── 文书 ──
            if (f.Book) return "本";
            if (f.DataDisk) return "张";

            // ── 家具（★ 容器必须先于固定安装件：
            //      否则 `Argyve Multicabinet` 这种「带 Container 的柜子」会被判成"不加"）──
            if (f.Chair) return "把";
            if (f.Bed) return "张";
            if (f.Door) return "扇";
            if (f.Container) return "口";
            if (f.MountedFurniture) return NoWord();

            // ── 吃食 ──
            if (f.Food) return "份";
            // 注：T26 `PreservableItem`（独占 0）属存疑项，**有意未实现**

            // ── 场景 / 不可数 ──
            if (f.PlantChain) return "株";
            if (f.WallChain) return NoWord();
            if (f.TerrainChain) return NoWord();

            // ── 生物（`CreatureChain`）：表里判「只」但用户裁定**挂起**（细分未定）⇒ 不表态 ──
            // ── 天生武器（喙/咬/前肢，`Bite`/`Foreleg`）：T04「把」不适用 ⇒ 不表态 ──

            return null;   // 不表态 ⇒ 调用方回退现有首字表
        }

        private static string NoWord()
        {
            Content++;
            return "";
        }

        /// <summary>
        /// 一件物品的**类型事实**（只含判据用得到的布尔量）。
        /// 由 `Of(GameObjectBlueprint)` 从真实蓝图填；离线样本可直接构造。
        /// </summary>
        internal sealed class Facts
        {
            // 部件判据（HasPart）
            public bool Butcherable, AmmoArrow, AmmoGrenade, Shield, Armor,
                        Cybernetics, Book, DataDisk, Chair, Bed, Door,
                        Container, MountedFurniture, Food;
            // 链判据（DescendsFrom）
            public bool CorpseChain, MeleeWeaponChain, MissileWeaponChain,
                        PlantChain, WallChain, TerrainChain, CreatureChain;
            // 名字判据（整尸）
            public bool WholeCorpseName;

            internal static Facts Of(XRL.World.GameObjectBlueprint bp)
            {
                return new Facts
                {
                    Butcherable = bp.HasPart("Butcherable"),
                    AmmoArrow = bp.HasPart("AmmoArrow"),
                    AmmoGrenade = bp.HasPart("AmmoGrenade"),
                    Shield = bp.HasPart("Shield"),
                    Armor = bp.HasPart("Armor"),
                    Cybernetics = bp.HasPart("CyberneticsBaseItem"),
                    Book = bp.HasPart("Book"),
                    DataDisk = bp.HasPart("DataDisk"),
                    Chair = bp.HasPart("Chair"),
                    Bed = bp.HasPart("Bed"),
                    Door = bp.HasPart("Door"),
                    Container = bp.HasPart("Container"),
                    MountedFurniture = bp.HasPart("MountedFurniture"),
                    Food = bp.HasPart("Food"),
                    CorpseChain = bp.DescendsFrom("Corpse"),
                    MeleeWeaponChain = bp.DescendsFrom("MeleeWeapon"),
                    MissileWeaponChain = bp.DescendsFrom("MissileWeapon"),
                    PlantChain = bp.DescendsFrom("Plant"),
                    WallChain = bp.DescendsFrom("Wall"),
                    TerrainChain = bp.DescendsFrom("Terrain"),
                    CreatureChain = bp.DescendsFrom("Creature"),
                    WholeCorpseName = false   // 名字判据由调用方（For）覆盖
                };
            }

            /// <summary>离线样本用：从普查记录直接构造（`whole` 由中文名算）。</summary>
            internal static Facts ForTest(bool butcherable, bool corpseChain, bool wholeName,
                bool ammoArrow, bool ammoGrenade, bool melee, bool missile, bool shield,
                bool armor, bool cyber, bool book, bool disk, bool chair, bool bed,
                bool door, bool container, bool mounted, bool food, bool plant,
                bool wall, bool terrain)
            {
                return new Facts
                {
                    Butcherable = butcherable, CorpseChain = corpseChain,
                    WholeCorpseName = wholeName, AmmoArrow = ammoArrow,
                    AmmoGrenade = ammoGrenade, MeleeWeaponChain = melee,
                    MissileWeaponChain = missile, Shield = shield, Armor = armor,
                    Cybernetics = cyber, Book = book, DataDisk = disk, Chair = chair,
                    Bed = bed, Door = door, Container = container,
                    MountedFurniture = mounted, Food = food, PlantChain = plant,
                    WallChain = wall, TerrainChain = terrain
                };
            }
        }

        internal static string Stats()
        {
            return "查类型 " + Calls + " 次 · 命中类型表 " + Typed + " 次（其中判不加 " + Content + "）";
        }
    }
}
