// ═══════════════════════════════════════════════════════════════════════════
// 硬编码弹窗汉化（Popup.* / PickDirection.ShowPicker 的字面量）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图：
//     ┌─ Would you like to walk to the nearest stairway down? ─┐
//     │        [y] 是    [n] 否    [Esc] 取消                 │  ← 按钮**已是中文**
//     └────────────────────────────────────────────────────────┘
//
// 【根因（反编译）】正文是 `XRLCore::PlayerTurn` 里的**字符串字面量**：
//     Popup.ShowYesNoCancel(**"Would you like to walk to the nearest stairway down?"**)
//   ⇒ 不经 `Strings._S/_T` ⇒ XML 改不到。
//   ⇒ 而按钮文字（是/否/取消）走游戏自己的字符串表 ⇒ **本来就已中文**。
//
// 【同一函数里的 22 条硬编码弹窗】
//     You can only set your checkpoint in settlements.
//     You can only restore your checkpoint outside settlements.
//     Are you sure you want to restore your checkpoint?
//     This saved game predates world seed info.
//     Move in what direction? / Attack in what direction?
//     You haven't found any points of interest nearby. / Go to which point of interest?
//     There are hostiles nearby! / Select a destination
//     Are you sure you want to go to the world map?
//     Would you like to walk to the nearest stairway up? / …down?
//     It is already daytime.
//     You cannot examine things while you are enraged. / …confused.
//     Are you sure you want to save and quit? / Are you sure you want to quit?
//     Do you want to save first?
//     Walk / Open / Talk
//
// 【做法】**精确匹配**翻译弹窗正文，不改任何非白名单字符串：
//   `Popup.ShowYesNoCancel` / `ShowYesNo` / `AskString` / `AskYesNo` /
//   `PickOption` / `PickDirection.ShowPicker` 的**第 0 参**挂 Prefix。
//   ⚠️ 只翻白名单里的原句 ⇒ 动态内容（物品名、生物名）一律不动。
//
// 开关：装机目录放 `_zhs_popup_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPopupText
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>硬编码弹窗原文 ⇒ 中文（**精确匹配**）。</summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ★ 2026-10-15：游戏内「射线变体」弹窗标题
            //   （`BaseMutation::SelectVariant` 把字面量直传 `Popup.PickOption` 的 arg0，
            //    不经 `_S`/`_T`；译名与 `Strings.xml` L2078 的 chargen 那条对齐 —— §10 同词同译）
            { "Choose variant", "\u9009\u62e9\u53d8\u4f53" },

            // ★ 2026-10-17：`AscensionCable::TryAscend` 的 AskString 提示（硬编码）
            //   `ASCEND` 必须保留 ASCII —— 游戏用 `.EqualsNoCase("ASCEND")` 比较输入。
            { "Are you sure you want to ascend the Spindle? You know of no means to descend again.\n\nType 'ASCEND' to confirm.",
              "你确定要攀上纺锤吗？你根本不知道下来的方法。\n\n输入「ASCEND」确认。" },

            // ★ 2026-10-17（测试窗口 #57）：`XRL.World.Parts.Vehicle::HandleEvent`
            //   → `Popup.PickGameObject("Choose a follower", …)`。白名单已补该出口。
            { "Choose a follower", "选择一名追随者" },

            // ★ 2026-10-17（测试窗口 #18）：`XRL.UI.TradeUI::ShowVendorActions` 的
            //   **标题 + 7 个选项**，全是硬编码字面量（反编译实证）。
            //   出口早就挂了（`PickOption` 的 PrefixOptions），只是表里没这些词。
            //   `Read` 在别处也可能作为选项出现，译成「阅读」无副作用。
            { "select an action", "选择一项操作" },
            { "Look", "查看" },
            { "Add to trade", "加入交易" },
            { "Identify", "鉴定" },
            { "Repair", "修理" },
            { "Recharge", "充能" },
            { "Read", "阅读" },

            // ── 2.16.120：全量硬编码普查 · 调用点分析新发现的 47 条 ──
            { "Cannot proceed, no usable base genders", "无法继续：没有可用的基础性别。" },
            { "Are you sure you want to delete this entry?", "确定要删除这条记录吗？" },
            { "There was a duplication glitch involving your player character. It'd be helpful to send the save folder and Player.log to support@freeholdgames.com along with what you were currently doing.", "你的玩家角色出现了一处复制故障。若能把这个存档文件夹与 Player.log 发到 support@freeholdgames.com，并说明你当时在做什么，会很有帮助。" },
            { "The limb rejects the infection!", "那条肢体抗拒了感染！" },
            { "The tonics you ingested react adversely to each other. You feel awfully frigid.", "你服下的几种药剂相互冲突。你感到彻骨冰寒。" },
            { "The cool swelling deflates as your organs dim.", "凉意消退，你的脏腑黯淡下去。" },
            { "The tonics you ingested react adversely to each other. Aaaaaaaaargh!", "你服下的几种药剂相互冲突。啊啊啊啊啊啊啊！" },
            { "You are {{C|paralyzed}}!", "你{{C|瘫痪}}了！" },
            { "The tonics you ingested react adversely to each other. Your skin starts to knot and misshape.", "你服下的几种药剂相互冲突。你的皮肤开始打结、变形。" },
            { "The tonics you ingested react adversely to each other. The soothing tingle fades.", "你服下的几种药剂相互冲突。抚慰的酥麻感消退了。" },
            { "The soothing tingle fades.", "抚慰的酥麻感消退了。" },
            { "The tonics you ingested react adversely to each other. You flicker through spacetime uncontrollably.", "你服下的几种药剂相互冲突。你不受控制地在时空之间闪烁。" },
            { "The tonics you ingested react adversely to each other. Your field of vision erupts into a plane of blinding, white light.", "你服下的几种药剂相互冲突。你的视野炸裂成一片刺目的白光。" },
            { "The clouds part in your mind and a ray of clarity strikes through.", "你脑中的阴云散开，一道清明之光穿透而入。" },
            { "The tonics you ingested react adversely to each other. You cannot see to see -- your mind cracks as a bell struck by a hammer.", "你服下的几种药剂相互冲突。你眼前一片混沌——你的心智像被锤子敲击的钟一样裂开。" },
            { "A torrent of life rushes over you.", "一股生命之流涌过你全身。" },
            { "The tonics you ingested react adversely to each other. The torrent of life sweeps away.", "你服下的几种药剂相互冲突。生命之流席卷而去。" },
            { "The torrent of life sweeps away.", "生命之流席卷而去。" },
            { "The gates swing wide.", "大门豁然敞开。" },
            { "This zone didn't build properly, do you wish to rebuild it?", "这个区域没有正确生成，要重建它吗？" },
            { "There's nothing there to lunge at.", "那里没有可扑击的目标。" },
            { "There's nothing there you can lunge at.", "那里没有你能扑击的目标。" },
            { "That is out of range! (8 squares)", "目标超出范围！（8 格）" },
            { "That is out of range! (12 squares)", "目标超出范围！（12 格）" },
            { "That is out of range! (10 squares)", "目标超出范围！（10 格）" },
            { "You feel drowsy.", "你感到昏昏欲睡。" },
            { "That wouldn't leave you with NEARLY enough junk! You can't drop that!", "那样你剩下的破烂就远远不够了！你不能丢掉那个！" },
            { "You are startled!", "你被惊到了！" },
            { "There is no liquid here for you to spew.", "这里没有液体可供你喷吐。" },
            { "There are no creatures in range.", "范围内没有生物。" },
            { "{{r|Unwelcome images of {{G|budding plants}} fill your mind.}}", "{{r|{{G|抽芽的植物}}的不祥影像充斥你的脑海。}}" },
            { "A loud buzz is emitted. The unauthorized glyph flashes on the side of the applicator.", "一声响亮的蜂鸣响起。未授权的符文在施用器侧面闪烁。" },
            { "A force of repulsion from inside the sarcophagus prevents your entry.\n\nYou do not bear the Mark of Death.", "石棺内部的排斥力阻止你进入。\n\n你身上没有死亡之印。" },
            { "The sarcophagus is inert.", "石棺毫无反应。" },
            { "The moors rattle and a marble brick crumbles as a starship takes off.", "荒原震颤，一块大理石砖崩碎——一艘星际飞船升空了。" },
            { "There's nothing there to hobble.", "那里没有可致跛的目标。" },
            { "There's nothing there to shank.", "那里没有可捅刺的目标。" },
            { "There's nothing there you can shank.", "那里没有你能捅刺的目标。" },
            { "No skill by that name could be found.", "找不到叫这个名字的技能。" },
            { "The pact is struck. The Barathrumites may lease control of the Spindle, and all the attending factions owe a debt to Asphodel.", "契约已立。巴拉斯鲁米特可租借纺锤（the Spindle）的控制权，所有参与的派系都欠阿斯福德勒一份人情。" },
            { "The pact is struck. The Barathrumites may lease control of the Spindle, and the chosen factions owe a debt to Asphodel.", "契约已立。巴拉斯鲁米特可租借纺锤（the Spindle）的控制权，由选定的派系欠阿斯福德勒一份人情。" },
            { "The pact is struck. The Barathrumites may lease control the Spindle.", "契约已立。巴拉斯鲁米特可租借纺锤（the Spindle）的控制权。" },
            { "The colossal lid slams shut. Darkness engulfs you.", "巨大的盖子轰然闭合。黑暗将你吞没。" },
            { "Barathrum is not in the golem. Ascend anyway?", "巴拉瑟鲁姆（Barathrum）不在魔像里。仍要飞升吗？" },
            { "Do you want to wait until the ascent is done?", "要等到飞升完成吗？" },
            { "The control pit dissolves to silence.\n\nDo you want to wait until the ascent is done?", "控制坑陷入沉寂。\n\n要等到飞升完成吗？" },
            { "No mound of scrap and clay was found to complete.", "没有找到可供完成的废料与黏土堆。" },
            // ── 自动下楼（截图那条）──
            { "Would you like to walk to the nearest stairway down?", "要自动走向最近的向下的楼梯吗？" },
            { "Would you like to walk to the nearest stairway up?",   "要自动走向最近的向上的楼梯吗？" },
            // ── 世界地图 / 检查点 ──
            { "Are you sure you want to go to the world map?",   "确定要前往世界地图吗？" },
            { "Are you sure you want to restore your checkpoint?", "确定要恢复检查点吗？" },
            { "You can only set your checkpoint in settlements.",   "只能在聚落里设置检查点。" },
            { "You can only restore your checkpoint outside settlements.", "只能在聚落外恢复检查点。" },
            { "This saved game predates world seed info.",          "此存档早于世界种子信息。" },
            // ── 存档 / 退出 ──
            { "Are you sure you want to save and quit?", "确定要保存并退出吗？" },
            { "Are you sure you want to quit?",          "确定要退出吗？" },
            { "Do you want to save first?",              "要先保存吗？" },
            // ── 方向 / 目标 ──
            { "Move in what direction?",   "朝哪个方向移动？" },
            { "Attack in what direction?", "朝哪个方向攻击？" },
            { "Go to which point of interest?", "前往哪个兴趣点？" },
            { "You haven't found any points of interest nearby.", "附近没有已发现的兴趣点。" },
            { "There are hostiles nearby!", "附近有敌人！" },
            { "Select a destination", "选择目的地" },   // 与 ZhsPullDownPatch 一致
            // ★★ 2.15.97：`Popup.ShowSpace` 的**硬编码**文案（用户截图那句在此）
            //
            //   用户截图：`You're lost! Regain your bearings by exploring your surroundings.`
            //   反编译 `TeleportGate::CheckPossibleSubject`：
            //       Popup.ShowSpace(IComponent<GameObject>._S("TeleportGate Lost Popup", …))
            //   ⇒ 出口是 **`ShowSpace`**（不在本补丁原白名单里，已补上）。
            //
            //   ★ 一次扫全 `decomp find ShowSpace` 的 9 个调用点，硬编码共 **5 条**
            //     （其余 4 处走 `_T()`/`_S()`，由字符串表覆盖）⇒ 一次补全，
            //     **不要以后再来补第二次**。
            { "You're lost! Regain your bearings by exploring your surroundings.",
              "你迷路了！探索一下四周，重新辨明方位。" },
            { "You recognize the area and stop being lost!",
              "你认出了这片区域，不再迷路了！" },
            { "You regain your bearings.",
              "你重新辨明了方位。" },
            { "You stumble upon some flattened remains.",
              "你偶然发现了一些被压扁的遗骸。" },
            { "You died.\n\nEntombed in the burial chamber of Resheph, the Last Sultan.",
              "你死了。\n\n你长眠于末代苏丹雷谢夫的墓室之中。" },
            // ★★ 2.16.1：装备槽相关（`EquipmentScreen::ShowBodypartEquipUI`）
            //   用户截图：`You don't have anything to use in that slot.`
            //   反编译该方法的**全部字符串**（逐个判断可见性）：
            //     "You don't have anything to use in that slot."   ← 用户可见 ✅
            //     "You have no inventory!"                        ← 用户可见 ✅
            //     "Hand" / "Thrown Weapon"                        ← 部件类型（键，非文本）
            //     "Melee Weapons,Shields,Light Sources,Tools"     ← categoryPriority（内部）
            //     "CommandEquipObject" / "Object" / "BodyPart"    ← 事件/参数名（内部）
            { "You don't have anything to use in that slot.",
              "那个槽位里没有可用的东西。" },
            { "You have no inventory!", "你没有物品栏！" },
            // ★★ 2.16.2：`ShowFailure` 的**完整句**（截图：`You are lost!`）
            //
            //   出口链（反编译实证）：
            //     `Lost::HandleEvent(CanTravelEvent)` ⇒ `E.Object.ShowFailure("You are lost!")`
            //     ⇒ `XRL.World.Extensions::ShowFailure(this GameObject, string Message)`
            //        { if (Object != null && Object.IsPlayer()) Popup.ShowFail(Message); }
            //     ⇒ `Popup.ShowFail` **在本补丁白名单内** ✅
            //   ⇒ 只差精确表里没有这条。
            //
            //   ⚠ 为什么**不能**指望 `ZhsCombatMsgPatch.TranslateText` 兜底：
            //     它的早退判据是 `if (HasCjk(work) && !HasEnglishFrame(work)) return s;`
            //     而**规则全是「中英混排」句式**（`^ART(N) hits you …`）
            //     ⇒ **纯英文句一条都不命中** ⇒ 原样返回。
            //   ⚠ `ZhsJournalNotePatch` 里那条 `("You are lost!", "你迷路了！")`
            //     挂在 `EmitMessage` 出口，**够不到 `Popup.ShowFail`** ⇒ 死条目。
            //
            //   ★ 本表由 `decomp find ShowFailure` 的 **80 个调用点**全量扫描得出：
            //     A 纯字面量 25 条 → 进本表（`That is out of range! (N squares)`
            //       带变量 ⇒ 见下方 `Arg0Prefix` 的范围正则）
            //     B 走 `_S()/_T()` 12 条 → 数据侧 `Strings.xml` 覆盖
            //     C 拼接 43 条 → 需句式规则，另批处理
            { "You are lost!",
              "你迷路了！" },
            { "You can't leave the golem while it is ascending.",
              "魔像上升时你无法离开它。" },
            { "You can't pilot the golem while it is ascending.",
              "魔像上升时你无法驾驶它。" },
            { "It doesn't seem to do anything.",
              "它似乎没有任何作用。" },
            { "The sprayer head won't move.",
              "喷头转不动。" },
            { "You can't repair that.",
              "那个修不了。" },
            { "You are out of turrets to place.",
              "你已经没有可部署的炮塔了。" },
            { "You lack the means to do that.",
              "你没有做这件事的手段。" },
            { "You can't recoil yet.",
              "你还不能使用召回器。" },
            { "You cannot do that while sitting.",
              "坐着时无法这么做。" },
            { "You cannot do that while flying.",
              "飞行时无法这么做。" },
            { "You can't go to sleep right now.",
              "现在不能睡觉。" },
            { "The gates are sealed for eternity.",
              "大门已被永久封闭。" },
            { "The gates are secured shut until the threat to Omonporch is eliminated.",
              "在奥蒙波奇的威胁被消除之前，大门将一直紧闭。" },
            { "You must have a long blade equipped in your primary hand to lunge.",
              "要使用突刺，你的主手必须装备长刃。" },
            { "The spaceship's launch sequence has already begun.",
              "飞船的发射程序已经开始。" },
            { "The spaceship is already traversing the void.",
              "飞船已在虚空之中航行。" },
            { "The spaceship can't launch from here.",
              "飞船无法从这里发射。" },
            { "There's nothing there to slam.",
              "那里没有可供猛击的东西。" },
            { "You have no weapon!",
              "你没有武器！" },
            { "You must have a cudgel equipped in order to use slam.",
              "要使用猛击，你必须装备棍棒。" },
            { "You're too confused to do that.",
              "你太混乱了，做不到。" },
            { "You can't repair with hostile creatures nearby.",
              "附近有敌对生物时无法修理。" },
            { "You lack a liquid to spit!",
              "你没有可吐出的液体！" },
            // `That is out of range! (N squares)` —— 格数会变 ⇒ 走正则
            //   （在 `Arg0Prefix` 末尾处理，见 `RANGE_RE`）
            // ★★ 2.16.3：睡觉时长弹窗（用户截图：`How long would you like to sleep?`）
            //
            //   反编译 `XRL.World.Parts.Bed::AttemptSleep`：
            //       List<string> list = new List<string>();
            //       list.Add("Until " + Calendar.GetTime((TotalTimeTicks + 150) % 1200));
            //       list.Add("Until " + Calendar.GetTime((TotalTimeTicks + 375) % 1200));
            //       list.Add("Until " + Calendar.GetTime((TotalTimeTicks + 600) % 1200));
            //       num = Popup.PickOption("How long would you like to sleep?", …,
            //                              list.ToArray(), …);
            //   ⇒ 时间名（浅滩时分 / 盐日渐盈 / 盐日渐亏）**已是中文**（字符串表覆盖），
            //     只有 **`Until ` 前缀**与**标题**是英文。
            //   ⇒ `Until 浅滩时分` 形态 ⇒ 用 `Until ` **前缀规则**（见 `OPTION_PREFIX`）。
            { "How long would you like to sleep?", "你想睡多久？" },
            // 召回器 / 传送选单的选项（原先只在 `ZhsPullDownPatch` 里）
            // ⇒ 并入本表，使本补丁成为**弹窗选项的唯一权威处**。
            //   ⚠ 两条补丁都会改同一批选项（`PickOption`），但都是**精确匹配**
            //     ⇒ 二次进入自然幂等（中文不会再被改一次）。
            { "Current location", "当前位置" },
            { "Center", "中心" },
            { "Arrival location", "抵达地点" },
            { "NW", "西北" }, { "N", "北" },  { "NE", "东北" },
            { "W",  "西"  },                  { "E",  "东"  },
            { "SW", "西南" }, { "S", "南" },  { "SE", "东南" },
            // ── 杂项 ──
            { "It is already daytime.", "现在已经是白天了。" },
            { "You cannot examine things while you are enraged.", "暴怒时无法查看物品。" },
            { "You cannot examine things while you are confused.", "混乱时无法查看物品。" },
            // ── 动词菜单项 ──
            { "Walk", "行走" },
            { "Open", "打开" },
            { "Talk", "交谈" },

            // ═══════════════════════════════════════════════════════════════════
            // ★★ 2.16.104：魔像「选择催化剂」界面（用户截图报大片漏译）
            //
            // 【界面】`XRL.World.Quests.GolemQuest.GolemCatalystSelection`
            //   26 个静态 `Unit*` 方法里 yield 出 `GameObjectUnit`，效果文本**硬编码**。
            // 【出口】`GolemMaterialSelection`2 :: Pick()` 最后调 **`Popup::PickOption`**
            //   （IL 实证：`IL_019E: call Popup::PickOption`）
            //   ⇒ 本补丁的 `PickOption` 出口**已覆盖** ⇒ 走本精确表即可，**无需改源码**。
            // 【为什么漏】`GameObjectPartUnit.GetDescription()`：
            //       if (Description == null) { …从 Part 算… }  return Description;
            //   `Unit*` 里写了 `Description = "…"` ⇒ 非 null ⇒ **原样输出英文**。
            // ═══════════════════════════════════════════════════════════════════
            // —— 效果描述（5 条，`Unit*` 里硬编码）——
            { "Melee attacks cause enemies to get stuck",
              "近战攻击会使敌人被粘住" },
            { "Melee attacks cause 2-4 equipped enemy items to rust, starting with held items",
              "近战攻击会使敌人装备的 2-4 件物品锈蚀，从手持物品开始" },
            { "Melee attacks drain 1 Toughness",
              "近战攻击会吸取 1 点坚韧" },
            { "Immune to confusion", "免疫混乱" },
            { "Immune to poison", "免疫中毒" },
            // ── 2.16.124：`PsychicGlimmer::Update` 的 4 段 + `CardiacArrest::Remove` 的 2 条 ──
            //   ★ 前 4 条的 `who.itself` 在 `IsPlayer()` 守卫之后 ⇒ **恒为 "yourself"**
            //     ⇒ 拼接后的整串是**确定**的，可按整串精确匹配（§49 口径）
            //   ★ 其余 2 条是 `Popup.Show` 直传字面量（洞① 救回）
            { "{{K|What you understood to be the psychic sea was only a pond. There are other watchers now, countless in number, beyond the gulf of materiality. Points of light glimmer in all directions, but what are directions on a space that cannot be ordered? All you know now is of an aether vaster than the very mathematics that describe it. And you are not nor will you ever be again alone.}}", "{{K|你原以为的心灵之海，不过是一方池塘。如今还有别的注视者，多得不可胜数，就在物质性的深渊之外。光点向四面八方闪烁——可在一个无法排序的空间里，方向又意味着什么？你如今所知的，只有一片以太，它比描述它的数学本身更为浩瀚。而你并不孤独，也再不会孤独。}}" },
            { "{{G|Your heart restarts!}}", "{{G|你的心脏重新跳动了！}}" },
            { "{{G|Your hearts restart!}}", "{{G|你的心脏全都重新跳动了！}}" },
            // —— 突变/效果名里**兼作查表键**的，只能在这里译（`Name` 字面量不可改）——
            //   ★ `GameObjectMutationUnit::Apply` 实证：`GetMutationByName(Entry?.Name ?? Name)`
            //     ⇒ `Name` 是**查表键**，改成中文会破坏突变查找（§18）。
            { "Regeneration", "再生" },
            { "Corrosive Gas Generation", "腐蚀气体生成" },
        };

        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (L.TryGetValue(en, out zh))
            {
                Hits++;
                if (Hits <= 10)
                    UnityEngine.Debug.Log("[QudZhsCookbook] 弹窗：" + en + "  =>  " + zh);
                return zh;
            }
            // ★★ 2.16.104：精确表未命中 ⇒ 试「**已中文化的名字 + 英文级别**」这一族。
            //   【为什么需要它】魔像催化剂界面的突变条目是 `Name + " at level " + Level`：
            //     上游 `L` 表先命中「Regeneration」⇒ 整串变成 `"再生 at level 5"`
            //     ⇒ 到 `REASM` 时**只剩 `at level` 一个英文词**，而 `REASM` 的判据要求
            //       「同时含中文和英文」虽满足，但那条 `^(.+?) at level (\d+)$` 是放在
            //       `Reassemble` 里、由**别的出口**（`UITextSkin`）调用的；
            //       `PickOption` 这条链走的是本函数 ⇒ 必须在这里接一次。
            //   【判据】只认 ` at level N` 结尾（**极窄**），纯英文整句不会被误伤
            //     （那种情况表里没命中就原样返回）。
            {
                var m = AT_LEVEL.Match(en);
                if (m.Success && CJK.IsMatch(m.Groups[1].Value))
                {
                    Hits++;
                    if (Hits <= 10)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 弹窗(级)："
                            + en + "  =>  " + m.Groups[1].Value + " " + m.Groups[2].Value + " 级");
                    return m.Groups[1].Value + " " + m.Groups[2].Value + " 级";
                }
            }
            // ★★★ 2.16.x：**英文方位词 + 汉字** 兜底（部位名「leftfore脚」⇒「左前脚」）。
            //
            // 【为什么必须放在**输出侧**】
            //   `Laterality.BuildLateralityAdjective` 里是硬编码英文
            //   （inner/outer/upper/lower/right/left/inside/outside/mid-fore/mid-hind/fore/hind），
            //   而 `BodyPart.Name`（来自 `Bodies.xml` 的 `Name="脚"`）已是中文
            //   ⇒ 拼出「leftfore脚」✗
            //
            //   我们已挂 `ZhsLateralityPatch`（Postfix `WithLateralityAdjective`），**确实生效**
            //   （实测日志：`★★ 首个**非空**方位前缀：lat=18 Base=脚 ⇒ 右前脚`，累计 4200+ 次）；
            //   但**那一屏仍旧是英文**，因为：
            //     · `BodyPart.Name` 是**序列化字段**（`Name = Reader.ReadOptimizedString()`）
            //       ⇒ 旧存档里存的就是「当年拼好的」那个串，补丁改不到已存的值 ✗
            //     · 蓝本期的 `Body` 也可能在本补丁挂载前就构造好了 ✗
            //   ⇒ 所以**输出侧再兜一次**，与挂载时机、存档新旧都无关 ✓
            //
            // 【判据】**只认「英文方位词 + 紧邻汉字」这个极窄形态**
            //   ⇒ 纯英文文本、英文词中间的巧合都不会被误伤 ✓
            {
                string lat = FixLaterality(en);
                if (!ReferenceEquals(lat, en) && lat != en)
                {
                    Hits++;
                    if (Hits <= 10)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 弹窗(方位)："
                            + en + "  =>  " + lat);
                    return lat;
                }
            }
            return en;
        }

        /// <summary>
        /// 英文方位词 → 中文（**顺序按中文习惯**：右左 → 上下 → 前后 → 内外）。
        /// 长词必须排在短词前面（正则交替是**从左到右**试的）。
        /// </summary>
        internal static readonly Dictionary<string, string> LAT_WORDS =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "leftmid-hind", "左中后" }, { "rightmid-hind", "右中后" },
            { "leftmid-fore", "左中前" }, { "rightmid-fore", "右中前" },
            { "mid-hind", "中后" }, { "mid-fore", "中前" },
            { "leftmid", "左中" }, { "rightmid", "右中" },
            { "leftfore", "左前" }, { "rightfore", "右前" },
            { "lefthind", "左后" }, { "righthind", "右后" },
            { "leftupper", "左上" }, { "rightupper", "右上" },
            { "leftlower", "左下" }, { "rightlower", "右下" },
            { "leftinside", "左内" }, { "rightinside", "右内" },
            { "leftoutside", "左外" }, { "rightoutside", "右外" },
            { "leftinner", "左内侧" }, { "rightinner", "右内侧" },
            { "leftouter", "左外侧" }, { "rightouter", "右外侧" },
            { "left", "左" }, { "right", "右" },
            { "fore", "前" }, { "hind", "后" },
            { "upper", "上" }, { "lower", "下" },
            { "inner", "内侧" }, { "outer", "外侧" },
            { "inside", "内" }, { "outside", "外" },
            { "mid", "中" },
        };

        /// <summary>`(?&lt;![A-Za-z-])(方位词)(?=[汉字])` —— 交替按**长度降序**拼，长词优先。</summary>
        private static readonly System.Text.RegularExpressions.Regex LAT_RE =
            new System.Text.RegularExpressions.Regex(
                @"(?<![A-Za-z\-])("
                + string.Join("|", SortedLatKeys())
                + @")(?=[\u4e00-\u9fff])");

        private static string[] SortedLatKeys()
        {
            var ks = new List<string>(LAT_WORDS.Keys);
            ks.Sort(delegate (string a, string b)
            {
                if (a.Length != b.Length) return b.Length - a.Length;
                return string.CompareOrdinal(a, b);
            });
            var outp = new List<string>(ks.Count);
            foreach (string k in ks) outp.Add(System.Text.RegularExpressions.Regex.Escape(k));
            return outp.ToArray();
        }

        /// <summary>把「英文方位词 + 汉字」换成中文方位；没有该形态则原样返回。</summary>
        internal static string FixLaterality(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                if (!LAT_RE.IsMatch(s)) return s;
                return LAT_RE.Replace(s, delegate (
                    System.Text.RegularExpressions.Match m)
                {
                    string zh;
                    return LAT_WORDS.TryGetValue(m.Groups[1].Value, out zh)
                        ? zh : m.Groups[1].Value;
                });
            }
            catch { return s; }
        }

        /// <summary>`X at level N` —— 魔像催化剂界面的突变条目形态。</summary>
        private static readonly System.Text.RegularExpressions.Regex AT_LEVEL =
            new System.Text.RegularExpressions.Regex(@"^(.+?) at level (\d+)$");

        private static readonly System.Text.RegularExpressions.Regex CJK =
            new System.Text.RegularExpressions.Regex(@"[\u4e00-\u9fff]");
    }

    internal static class ZhsPopupPatch
    {
        /// <summary>选项的**前缀式**译法（`Until 浅滩时分` ⇒ `睡到 浅滩时分`）。
        /// 键 = 英文前缀（含尾随空格）。</summary>
        private static readonly Dictionary<string, string> OPTION_PREFIX =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Until ", "睡到 " },
        };

        /// <summary>`That is out of range! (8 squares)` —— 格数可变 ⇒ 正则。</summary>
        private static readonly System.Text.RegularExpressions.Regex RANGE_RE =
            new System.Text.RegularExpressions.Regex(
                @"^That is out of range! \((\d+) squares?\)$");

        // ═══════════════════════════════════════════════════════════════════
        // ★★★ 2.16.88【修法乙】拼装弹窗的**重组规则**
        //
        // 【为什么需要】游戏很多弹窗是**运行时拼接**的：
        //     `"英文片段A" + 变量(已中文化) + "英文片段B"`
        //   ⇒ 精确表（`L`）永远对不上（含变量）⇒ 显示成中英夹生。
        //
        // 【实证】
        //   ① `XRL.World.Reputation::SetFactionRank`
        //        Popup.Show("You are promoted to the " + Faction.GetRankTerm(FactionName)
        //                   + " of " + Rank + ".");
        //        用户截图：`你被提升为rank of Journeyfriend。`
        //        （「你被提升为」是我方规则译的骨架；`rank of X` 是硬编码 + 变量）
        //   ② `XRL.World.Parts.Leveler::RapidAdvancement`（**一个方法里 5 个弹窗全是这个套路**）
        //        ShowYesNo("Your genome enters an excited state! Would you like to spend "
        //                  + "{{rules|4}} mutation points to buy " + Grammar.A(@for)
        //                  + " before rapidly mutating?", …)
        //        用户截图：`…to buy 变异 before rapidly mutating?`
        //        （`@for` = `GetMutationTermEvent.GetFor(...)` = 「变异」，我方已译）
        //
        // 【判据】只认**「英文骨架 + 中文变量 + 英文骨架」**且**至少含两个英文词**的形态
        //   ⇒ 纯中文、纯英文整句都不会命中（不会误伤）。
        //   ⚠ 不要用「英文有、中文没有」这种一刀切判据（§19 的教训）。
        //
        // 【维护】新增拼装弹窗时**加一行**即可 —— 中文模板里用 `{0}` `{1}` 占位。
        // ═══════════════════════════════════════════════════════════════════
        internal const string EN = @"[A-Za-z][A-Za-z'\-]*";

        /// <summary>
        /// ★★ 2026-10-15（N1 批次）：**名字前的可选英文冠词**（与 `ZhsCombatMsgPatch.ART` 同款）。
        ///
        /// 【为什么弹窗侧也需要它】`Does(...)` / `T()` / `t()` / `The + DisplayNameOnly`
        ///   对**英文名**会带上 `The `/`the `/`a ` 冠词，而中文名不带
        ///   ⇒ 规则若以 `^(.+?) …` 捕获，英文名那支会把 `The ` 带进中文句首。
        /// 【★ 分支顺序】`An|an` 必须在 `A|a` **之前**（否则 `An` 被 `A` 抢先匹配成 `A`+`n`）
        ///   —— 这条是本项目 §44 的教训，`ZhsCombatMsgPatch.ART` 的注释里也记着。
        /// 【全用非捕获组】⇒ 不影响规则的捕获组编号。
        /// </summary>
        internal const string ART = @"(?:(?:The|the|An|an|A|a) )?";
        internal static readonly (System.Text.RegularExpressions.Regex Rx, string Zh)[]
            REASM =
        {
            // ── 爵阶提升（`Reputation::SetFactionRank`）──
            //    `You are promoted to the rank of 旅友.` ⇒ `你被提升为旅友。`
            //    `You are promoted to the meyvn of 明者（meyvn）.` ⇒ `你被提升为明者（meyvn）。`
            //    （`rank` 那个词本身也是变量：Barathrumites 传 `rank`，别的派系可能传别的）
            (new System.Text.RegularExpressions.Regex(
                @"^You are promoted to the " + EN + @" of (.+?)\.?$"),
             "你被提升为{0}。"),

            // ── 基因组兴奋 / 快速突变（`Leveler::RapidAdvancement`）──
            //    ① 花点数买变异
            (new System.Text.RegularExpressions.Regex(
                @"^Your genome enters an excited state! Would you like to spend "
                + @"\{\{rules\|(\d+)\}\} mutation points to buy (.+?) before rapidly mutating\?$"),
             "你的基因组进入兴奋状态！你愿意花费 {{rules|{0}}} 点突变点数，先购买{1}，再进行快速突变吗？"),
            //    ② 纯宣告
            (new System.Text.RegularExpressions.Regex(
                @"^Your genome enters an excited state!$"),
             "你的基因组进入兴奋状态！"),
            //    ③ 选择要快速突变的变异
            (new System.Text.RegularExpressions.Regex(
                @"^Choose a physical (.+?) to rapidly advance\.$"),
             "选择一项要快速突变的{0}。"),
            //    ④ 已提升等级
            (new System.Text.RegularExpressions.Regex(
                @"^You have rapidly advanced (.+?) by (.+?) ranks? to rank \{\{C\|(.+?)\}\}!$"),
             "你已将{0}快速提升了{1}级，达到{{C|{2}}}级！"),
            //    ⑤ 没有可提升的
            (new System.Text.RegularExpressions.Regex(
                @"^You have no physical (.+?) to rapidly advance!$"),
             "你没有可供快速突变的{0}！"),

            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2026-10-12：**买随机变异**界面（`XRL.UI.StatusScreen::BuyRandomMutation`）
            // ═══════════════════════════════════════════════════════════════════
            //  【用户实证】新角色新存档，标题显示 `Choose 变异.` ✗
            //
            //  【反编译全文（关键那几行）】
            //      string for2 = GetMutationTermEvent.GetFor(GO);          // → 「变异」
            //      array[j] = "{{W|" + name + "}}" + (extraLimb.Contains(j)
            //                     ? "{{G| + grow a new body part}}" : "")
            //                 + " {{y|- " + desc + "}}\n" + levelText;
            //      num = Popup.PickOption("", "Choose " + Grammar.A(for2) + ".", "", …, array, …);
            //      Popup.Show("You gain " + baseMutation.GetDisplayName() + "!", …);
            //      JournalAPI.AddAccomplishment("Your genome destabilized and you gained the "
            //                     + name + " " + for2 + ".", …);
            //  ⇒ 标题 = `"Choose " + 冠词 + "."`，中文冠词被我们剥成空 ⇒ **`Choose 变异.`** ✗
            //
            //  【★ 为什么之前修了却没生效】
            //      `ZhsCombatMsgPatch` 的 `R()` 规则挂在
            //          `XRL.Messages.MessageQueue::AddPlayerMessage`（反编译实证 L1561/L1654）
            //      ⇒ 它**只管消息栏**，管不到 `Popup.PickOption` ✗
            //      而本文件当时只有两条**专用** `Choose` 规则（physical advance / one of …）
            //      ⇒ **通用 `Choose X.` 在弹窗侧从来就没有** ✗✗
            //  ⇒ 所以在这个文件里补上（弹窗侧才是它的归属）。
            //  ⇒ 通用 `Choose X.` **不再在这里重复加** ——
            //    `ZhsCombatMsgPatch.TranslateText` 已经提供（本文件 L990 会调它）✓
            //    再加一条会**绕过**它那句「不得含两个连续 ASCII 单词」的收窄判据，
            //    把整句英文又吃一遍 ✗
            //  ⚠ 真正缺的是**标题在第 1 参**的那条调用约定 —— 见 `PrefixOptions` 的注释 ✓
            (new System.Text.RegularExpressions.Regex(
                @"^You gain (.+?)!$"),
             "你获得了{0}！"),
            (new System.Text.RegularExpressions.Regex(
                @"\{\{G\| \+ grow a new body part\}\}"),
             "{{G| ＋ 长出一条新肢体}}"),
            (new System.Text.RegularExpressions.Regex(
                @"^Your genome destabilized and you gained the (.+?) (.+?)\.$"),
             "你的基因组失稳了，你获得了{0}{1}。"),

            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2026-10-12：**效果 / 单位描述模板**族（用户三张截图）
            // ═══════════════════════════════════════════════════════════════════
            //  【实证出处】
            //    · `XRL.World.Units.GameObjectSkillUnit::GetDescription`       → `Has every`（+ ` skill`）
            //    · `XRL.World.Units.GameObjectMetachromeUnit::GetDescription`  → `Equipped with`
            //    · `XRL.World.Effects.ProceduralCookingEffectUnitMutation`1`   → `Can use =mutationName= at rank =tier=.`
            //    · Player.log MISS：`ItemEffectBonusMutation`1` | `Can use =mutation= at level =tier=.`
            //    · Player.log MISS：`ItemEffectBonusMutation`1` | ` at level`   ← 就是那个「at level 拼接段」
            //
            //  【为什么以前全掉了】
            //    `ZhsEffectDetailsPatch` 是**整串精确匹配**表 ✗ ⇒ 这类**带变量**的模板
            //    永远进不了表 ⇒ 一律 MISS ✗；而弹窗侧也没有对应规则 ✗
            //  ★★ 2026-10-17：这里的 `Has every` 规则**已移除** —— 它只能换骨架、
            //     换不了技能名（`value.Name` 是英文键），实测产出
            //     「精通所有Cudgel技能」/「--精通所有…技能」（前缀空格还会被 `.Trim()` 吃掉）。
            //     ⇒ 改由 `SkillUnitZh()` 接管（**连技能名一起**用 `ZhsSkillReqTable.Map` 换），
            //        查不到名字就整行保留英文（与 `AT_LEVEL2` / `MutName` 同一判据）。
            //     ⚠ 计数基线：REASM 少 1 条（已 `--write`，见 patch notes）。
            (new System.Text.RegularExpressions.Regex(
                @"^Equipped with (.+)$"),
             "装备：{0}"),

            // ── ★★ 2026-10-18（测试窗口 #55）：`XRL.World.Units.*::GetDescription` 的硬编码句式 ──
            //   `GameObjectBaetylUnit`  → `"Spawns with " + Amount + " random baetyl reward(s)"`
            //   `GameObjectCloneUnit`   → `"Spawns with a copy in a nearby cell"`
            //   `GameObjectRelicUnit`   → `"Spawns with a " + text + "-tier relic"`
            //   `GameObjectTieredArmorUnit` → `"Spawns with"+Amount+"random pieces of"+size+" tier armor"`
            //   ⚠ TieredArmor 是 `TextBuilder.Compound` 拼的 ⇒ **片段之间没有空格**
            //     （实机 `Spawns with4random pieces ofgigantic,mid-to-high tier armor`）
            (new System.Text.RegularExpressions.Regex(
                @"^(?:--\s*)?Spawns with\s*(\d+)\s*random baetyl rewards?$"),
             "附带 {0} 个随机的贝提尔奖励"),
            (new System.Text.RegularExpressions.Regex(
                @"^(?:--\s*)?Spawns with a copy in a nearby cell$"),
             "附带一个附近格子的复制体"),
            (new System.Text.RegularExpressions.Regex(
                @"^(?:--\s*)?Spawns with a (.+?)-tier relic$"),
             "附带一件 {0} 级遗物"),
            (new System.Text.RegularExpressions.Regex(
                @"^(?:--\s*)?Spawns with\s*(\d+)\s*random pieces of\s*gigantic,mid-to-high tier armor$"),
             "附带 {0} 件巨型、中高档护甲"),
            (new System.Text.RegularExpressions.Regex(
                @"^(?:--\s*)?Spawns with\s*(\d+)\s*random pieces of\s*(.+?)\s*tier armor$"),
             "附带 {0} 件{1}级护甲"),

            // ── ★★ 2026-10-18（#67 / #69）：声望与水之仪式面板 ──
            //   `WaterRitual::HandleEvent` 硬编码：`Your reputation with X is -50.` /
            //   `X can award an additional 100 reputation.`
            //   `Reputation` 消息族：`You are now despised by X.`
            (new System.Text.RegularExpressions.Regex(
                @"^Your reputation with (.+?) is (-?\d+)\.$"),
             "你与{0}的声望为 {1}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?) can award an additional (\d+) reputation\.$"),
             "{0}还能再给予 {1} 点声望。"),
            (new System.Text.RegularExpressions.Regex(
                @"^You are now despised by (.+?)\.$"),
             "你如今被{0}鄙视。"),
            (new System.Text.RegularExpressions.Regex(
                @"^You are now liked by (.+?)\.$"),
             "你如今被{0}喜爱。"),

            // ── ★★ 2026-10-17（测试窗口 #52）：`NephalProperties` 的**硬编码 chord 弹窗** ──
            //   产地（反编译 `XRL.World.Parts.NephalProperties::HandleEvent`）：
            //     `Popup.Show("A sphere of light in the chord of " + BaseDisplayNameStripped
            //        + " radiates away.\n\nYou feel it absorbed elsewhere.")`
            //   实机：`A sphere of light in the chord of 雷尔马顿 radiates away.`
            //         `You feel it absorbed elsewhere.`
            //   ⇒ 两段一整串；单句规则锚 `^…$` 都够不到，必须整段匹配（`\s*` 吃 `\n\n`）。
            //   同族（`INephalChord::HandleEvent` / `TryPacify`）一并收。
            (new System.Text.RegularExpressions.Regex(
                @"^A sphere of light in the chord of (.+?) radiates away\.\s*You feel it absorbed elsewhere\.$"),
             "{0}之弦中的一团光消散了。\n\n你感到它在别处被吸收。"),
            (new System.Text.RegularExpressions.Regex(
                @"^This creature burns bright in the chord of (.+?)\.$"),
             "这个生物在{0}之弦中灼亮燃烧。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?) in pacification and radiates a chord of light\.$"),
             "{0}在安抚中散发出一道光之弦。"),
            (new System.Text.RegularExpressions.Regex(
                @"^Can use (.+?) at (?:level|rank) (.+?)\.$"),
             "可使用{0}（等级{1}）"),
            (new System.Text.RegularExpressions.Regex(
                @"^Can use (.+?)\.$"),
             "可使用{0}。"),
            // ★ 「X at level N」**不放这里** —— 它的变量要先查变异名表，
            //   需要代码而非纯模板 ⇒ 见 `Reassemble` 里的 `AT_LEVEL2` 前置处理 ✓

            // ── ★★ 2.16.94 同伴「能力开关」弹窗（`GameObject::ChangeCompanionAbilityUse`）──
            //   实证（用户截图）：
            //     `Choose one of 人类魔像's abilities to forbid or allow.`
            //   ⇒ 英文骨架 + **已中文化的**同伴名（`人类魔像`）+ 英文所有格 `'s`
            //   【反编译】`Popup.PickOption("", "Choose one of "
            //                + Grammar.MakePossessive(Subject.t())
            //                + " abilities to forbid or allow.", …)`
            //   ⇒ `Grammar.MakePossessive` 对中文名加了英文 `'s`（§10 的英文语法后缀问题）
            //   ★ 两种形态都要接：
            //       `人类魔像's`（`t()` 给中文名）· `人类魔像的`（将来若改对了所有格）
            (new System.Text.RegularExpressions.Regex(
                @"^Choose one of (.+?)(?:'s|’s|的) abilities to forbid or allow\.$"),
             "选择要禁止或允许{0}的一项能力。"),

            // ── 同上：切换后的三条回执（同一方法的 `Popup.Show` 分支）──
            //   `Popup.Show(Subject.Poss(displayName + " ability")
            //               + " is now toggled " + (on|off) + ".")`
            //   `… + " is now " + (forbidden|allowed) + ".")`
            //   `… + " cannot be toggled at this time.")`
            //   ⇒ 骨架英文 + 中文化的 displayName + **英文所有格 `'s`**
            //   ★ 与上面标题那条同源（同一方法、同一 `Poss` 写法）
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?)(?:'s|’s|的) ability is now toggled (on|off)\.$"),
             "{0}的能力现在是{1}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?)(?:'s|’s|的) ability is now (forbidden|allowed)\.$"),
             "{0}的能力现已{1}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?)(?:'s|’s|的) ability cannot be toggled at this time\.$"),
             "{0}的能力此刻无法切换。"),

            // ── ★★ 2.16.99：变异「等级变化原因」行（`BaseMutation::CalcLevel`）──
            //   【为什么必须做】该方法的 `reason` 有 **13 个赋值点**，全是硬编码拼接
            //   （`_tools\_calclevel.txt` L63/L73/L98/L120/…）：
            //       reason = "+ This " + Grammar.MakePossessive(text) + " rank is increased by "
            //              + value.Modifier + " due to your high " + value.Name + "."
            //       reason = "- All your " + Grammar.Pluralize(text) + "' ranks are decreased by "
            //              + -intProperty + "."
            //   ⚠ 它**不经过消息队列**（`Player.log` 里搜不到）⇒ 走 `UITextSkin` 渲染
            //     （变异详情面板）⇒ 由 `ZhsModernUiPatch` 的 `UITextSkin.Apply` 出口接管。
            //   ★ 4 条覆盖全部 13 个点：`[+-]` × `rank is / ranks are` × `increased/decreased`
            //   ★ 必须带 `{1}`（数值）—— 模板漏了它就会丢掉「by N」
            //   ★ `due to` 分支单独一条：语序要重排（中文把原因后置）
            //   ★★ `due to` 后面有**多种前缀**，必须逐个剥离（实测漏了 `your equipped item, `）：
            //        `due to your high X.`            （L63/L144）
            //        `due to your low X.`             （L73）
            //        `due to being rapidly advanced N times.`（L157）
            //        `due to a metabolizing effect.`  （L188）
            //        `due to a tonic effect.`         （L197）
            //        `due to your equipped item, X.`  （L206）★ 带逗号
            //        `due to X.`                      （L215/L224）
            //      ⇒ 用 `(?:your (?:high|low) |your equipped item, |a |being )?` 一次剥净
            //   ★★ `+ This ` / `- This ` 的 `This` **没有中文对应物** ⇒ 整体删掉。
            //     英文原句是 `"+ This " + Grammar.MakePossessive(text) + " rank is increased by …"`，
            //     中文里说「利爪的等级提升了 3 级」就够，加「这个」很生硬。
            //   ⚠ 不能把 `This` 译成「该」：`text` 可能是「全」等前缀，
            //       产出「该全力量…」不通（用户 2026-10-10 提问「这里的 this 能杀吗」⇒ 能）。
            //   ⚠ **符号 `+` / `-` 必须保留**（它不是装饰，是「加值 / 减值」的语义标记）
            //       ⇒ 用 `([+-]\s*)` 单独捕获，替换时回填 `{0}`。
            (new System.Text.RegularExpressions.Regex(
                @"^([+-]\s*)This\s+(.*?)rank is increased by (\d+) due to (?:your (?:high|low) |your equipped item, |a |being )?(.+)\.$"),
             "{0} {1}等级提升了 {2} 级，因{3}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+-]\s*)This\s+(.*?)rank is decreased by (\d+) due to (?:your (?:high|low) |your equipped item, |a |being )?(.+)\.$"),
             "{0} {1}等级下降了 {2} 级，因{3}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+-]\s*)This\s+(.*?)rank(?:s are| is) increased by (\d+)\.$"),
             "{0}{1}等级提升了 {2} 级。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+-]\s*)This\s+(.*?)rank(?:s are| is) decreased by (\d+)\.$"),
             "{0}{1}等级下降了 {2} 级。"),
            (new System.Text.RegularExpressions.Regex(
                @"^Stops meditating if another action is taken or damage is taken in a single round\.$"),
             "\u82e5\u91c7\u53d6\u5176\u5b83\u884c\u52a8\uff0c\u6216\u5728\u5355\u56de\u5408\u5185\u53d7\u5230\u4f24\u5bb3\uff0c\u5219\u505c\u6b62\u51a5\u60f3\u3002"),
            (new System.Text.RegularExpressions.Regex(
                @"^You detect the presence of psychic enemies within a radius of (\d+)\.$"),
             "\u4f60\u80fd\u63a2\u6d4b\u5230\u534a\u5f84 {0} \u5185\u7684\u7075\u80fd\u654c\u4eba\u3002"),
            (new System.Text.RegularExpressions.Regex(
                @"^After a one-round warmup, you emit a pulse with radius (\d+)\.$"),
             "\u9884\u70ed\u4e00\u8f6e\u540e\uff0c\u4f60\u653e\u51fa\u534a\u5f84 {0} \u7684\u8109\u51b2\u3002"),
            (new System.Text.RegularExpressions.Regex(
                @"^(\d+)-square ray of frost in the direction of your choice$"),
             "{0} \u683c\u5bd2\u971c\u5c04\u7ebf\uff0c\u65b9\u5411\u7531\u4f60\u9009\u62e9"),
            (new System.Text.RegularExpressions.Regex(
                @"^-1 to every attribute per turn \(current: (.+?)\)$"),
             "\u6bcf\u56de\u5408\u6240\u6709\u5c5e\u6027 -1\uff08\u5f53\u524d\uff1a{0}\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^You bear a tail with a stinger that delivers (.+?) venom to your enemies\.$"),
             "\u4f60\u957f\u7740\u4e00\u6761\u5c3e\u523a\uff0c\u80fd\u5411\u654c\u4eba\u6ce8\u5165{0}\u6bd2\u6db2\u3002"),
            (new System.Text.RegularExpressions.Regex(
                @"^Confuses nearby hostile creatures per Confusion rank (.+)$"),
             "\u4f9d\u300c\u81f4\u5e7b\u300d\u7b49\u7ea7\u4f7f\u9644\u8fd1\u654c\u5bf9\u751f\u7269\u9677\u5165\u6df7\u4e71\uff08{0}\uff09"),
            // ── ★★ 2026-10-15 B 族：代词 / 性别定制提示（Async 出口，② 已补白名单）──
            (new System.Text.RegularExpressions.Regex(
                @"^Should your (.+?) be treated as fully plural, with you being addressed as a multiple subject in all circumstances\?$"),
             "\u4f60\u7684{0}\u662f\u5426\u5e94\u89c6\u4e3a\u5168\u590d\u6570\u2014\u2014\u5373\u5728\u4e00\u5207\u573a\u5408\u90fd\u4ee5\u590d\u6570\u4e3b\u8bed\u79f0\u547c\u4f60\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^Should your (.+?) be treated as conditionally plural, with you being addressed as a multiple subject only following a pronoun, as with with singular ""they""\?$"),
             "\u4f60\u7684{0}\u662f\u5426\u5e94\u89c6\u4e3a\u6761\u4ef6\u590d\u6570\u2014\u2014\u5373\u4ec5\u5728\u4ee3\u8bcd\u4e4b\u540e\u4ee5\u590d\u6570\u4e3b\u8bed\u79f0\u547c\u4f60\uff0c\u5982\u540c\u5355\u6570 they \u7684\u7528\u6cd5\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What subjective pronoun \(he, she, they, etc\.\) should be used for this (.+?)\?$"),
             "\u8fd9\u4e2a{0}\u8be5\u7528\u54ea\u4e2a\u4e3b\u683c\u4ee3\u8bcd\uff08he\u3001she\u3001they \u7b49\uff09\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What objective pronoun \(him, her, them, etc\.\) should be used for this (.+?)\?$"),
             "\u8fd9\u4e2a{0}\u8be5\u7528\u54ea\u4e2a\u5bbe\u683c\u4ee3\u8bcd\uff08him\u3001her\u3001them \u7b49\uff09\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What possessive adjective \(his, her, their, etc\.\) should be used for this (.+?)\?$"),
             "\u8fd9\u4e2a{0}\u8be5\u7528\u54ea\u4e2a\u6240\u6709\u683c\u5f62\u5bb9\u8bcd\uff08his\u3001her\u3001their \u7b49\uff09\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What substantive possessive \(his, hers, theirs, etc\.\) should be used for this (.+?)\?$"),
             "\u8fd9\u4e2a{0}\u8be5\u7528\u54ea\u4e2a\u6240\u6709\u683c\u4ee3\u8bcd\uff08his\u3001hers\u3001theirs \u7b49\uff09\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What reflexive pronoun \(himself, herself, themself, themselves, etc\.\) should be used for this (.+?)\?$"),
             "\u8fd9\u4e2a{0}\u8be5\u7528\u54ea\u4e2a\u53cd\u8eab\u4ee3\u8bcd\uff08himself\u3001herself\u3001themself\u3001themselves \u7b49\uff09\uff1f"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used for a mature person of this (.+?)\? \(Man, woman, person, etc\.\)$"),
             "\u8fd9\u4e2a{0}\u4e2d\uff0c\u6210\u5e74\u4eba\u7684\u79f0\u547c\u8be5\u7528\u4ec0\u4e48\uff1f\uff08man\u3001woman\u3001person \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used for an immature person of this (.+?)\? \(Boy, girl, child, etc\.\)$"),
             "\u8fd9\u4e2a{0}\u4e2d\uff0c\u672a\u6210\u5e74\u4eba\u7684\u79f0\u547c\u8be5\u7528\u4ec0\u4e48\uff1f\uff08boy\u3001girl\u3001child \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used to formally address a person of this (.+?)\? \(Sir, madam, friend, etc\.\)$"),
             "\u6b63\u5f0f\u79f0\u547c\u8fd9\u4e2a{0}\u4e2d\u7684\u4eba\uff0c\u8be5\u7528\u4ec0\u4e48\uff1f\uff08sir\u3001madam\u3001friend \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used to address a person of this (.+?) as an offspring\? \(Son, daughter, child, etc\.\)$"),
             "\u79f0\u547c\u8fd9\u4e2a{0}\u4e2d\u7684\u5b50\u55e3\u8be5\u7528\u4ec0\u4e48\uff1f\uff08son\u3001daughter\u3001child \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used to address a person of this (.+?) as a sibling\? \(Brother, sister, sib, etc\.\)$"),
             "\u79f0\u547c\u8fd9\u4e2a{0}\u4e2d\u7684\u5144\u5f1f\u59ca\u59b9\u8be5\u7528\u4ec0\u4e48\uff1f\uff08brother\u3001sister\u3001sib \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What term should be used to address a person of this (.+?) as a parent\? \(Father, mother, progenitor, etc\.\)$"),
             "\u79f0\u547c\u8fd9\u4e2a{0}\u4e2d\u7684\u7236\u6bcd\u8be5\u7528\u4ec0\u4e48\uff1f\uff08father\u3001mother\u3001progenitor \u7b49\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^Is an entity with this (.+?) treated grammatically as a person, such that it would be improper to say ""look at (.+?)"" in reference to (.+?) -- one would say ""look at (.+?)"" or ""look at (.+?)""\?$"),
             "\u4ee5\u8fd9\u4e2a{0}\u770b\u5f85\u7684\u5b9e\u4f53\uff0c\u5728\u8bed\u6cd5\u4e0a\u662f\u5426\u7b97\u4f5c\u300c\u4eba\u300d\u2014\u2014\u5373\u7528\u300clook at {1}\u300d\u6307\u4ee3{2}\u662f\u5426\u4e0d\u59a5\uff1f\uff08\u800c\u5e94\u8bf4\u300clook at {3}\u300d\u6216\u300clook at {4}\u300d\uff09"),
            (new System.Text.RegularExpressions.Regex(
                @"^What name should be used for your (.+?)\? \(Male, female, etc\.\)$"),
             "\u4f60\u7684{0}\u8be5\u7528\u4ec0\u4e48\u540d\u79f0\uff1f\uff08male\u3001female \u7b49\uff09"),
            // ── ★★ 2026-10-15 新增：**变异等级弹窗家族**（`BaseMutation::CalcLevel`）──
            //   用户截图 05（心灵撕裂详情/升级确认）里那几行英文/中英混排的来源与病根：
            //     ① 这些行**原本就没有规则**（`base rank is` / cost 两行 / `This rank:` 标签）
            //     ② 已有的 rank 规则是 `^…$` **整串锚定**，而游戏喂进来的是**一个多行串**
            //        （描述 + This rank + 等级文本 + Next rank + 等级文本 + rankBoost 行 + cost 行）
            //        ⇒ `^`/`$` 只匹配串首/串尾 ⇒ 那 17 条规则**全是死代码**
            //        （`Player.log` 实证：`（拼装重组）` **0 条**，而「已挂 9 个出口」有）
            //     ③ `AppendRankBoost` 用 `XRL.Extensions.AppendColored` 把每行裹成
            //        `{{C|…}}` / `{{G|…}}` / `{{R|…}}`（`RankBoostSigilColorCodes`：
            //        `'\0'→C` · `'-'→R` · `'+'→G`）⇒ 行首不是 `+`/`-`
            //   ⇒ 本轮加了**逐行通道**（`ReassembleLines`）解决 ②，并在逐行通道里
            //     **剥壳跑规则、命中再回填**解决 ③ ⇒ 下面规则一律写**裸文本**形态 ✓
            (new System.Text.RegularExpressions.Regex(
                @"^([+\-*]\s*)This\s+(.+?)\s*base rank is (\d+)\.$"),
             "{0} {1}基础等级为 {2}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+\-*]\s*)You do not possess this (.+?) inherently, and so you cannot advance its rank\.$"),
             "{0} 你并非天生拥有这个{1}，因此无法提升其等级。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+\-*]\s*)(.+?) ranks cannot be reduced below 1\.$"),
             "{0} {1}的等级不能降到 1 以下。"),
            (new System.Text.RegularExpressions.Regex(
                @"^([+\-*]\s*)This\s+(.+?)rank is capped at (\d+) due to your level\.$"),
             "{0} {1}等级因你的等级上限封顶于 {2}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^It will cost \{\{C\|(\d+)\}\} mutation point to increase (.+?)'s rank by 1\.$"),
             "提升{1}的等级需要 {{C|{0}}} 点突变点。"),
            (new System.Text.RegularExpressions.Regex(
                @"^Do you wish to increase this (.+?) rank\?$"),
             "要提升这个{0}等级吗？"),
            // 段落标签（`StatusScreen::ShowMutationPopup` 里 `Compound("{{w|This rank}}:\n", "\n\n")`）
            (new System.Text.RegularExpressions.Regex(
                @"^\{\{w\|This rank\}\}:$"),
             "{{w|本级}}："),
            (new System.Text.RegularExpressions.Regex(
                @"^\{\{w\|Next rank\}\}:$"),
             "{{w|下一级}}："),

            // 兜底：其它前缀形态（`All your …` 等）保留原前缀
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)rank is increased by (\d+) due to (?:your (?:high|low) |your equipped item, |a |being )?(.+)\.$"),
             "{0}等级提升了 {1} 级，因{2}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)rank is decreased by (\d+) due to (?:your (?:high|low) |your equipped item, |a |being )?(.+)\.$"),
             "{0}等级下降了 {1} 级，因{2}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)rank(?:s are| is) increased by (\d+)\.$"),
             "{0}等级提升了 {1} 级。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)rank(?:s are| is) decreased by (\d+)\.$"),
             "{0}等级下降了 {1} 级。"),

            // ── ★★ 2.16.100：`TrainingBook` / `StatusScreen::BuyStat` 的硬编码拼接 ──
            //   反编译出处（各方法体，全部是 `"骨架" + 变量` 形态）：
            //     `TrainingBook::HandleEvent`
            //        E.Postfix.AppendRules("Increases the " + Attribute + " of anyone who reads "
            //                              + ParentObject.them + ".");
            //        Popup.Show("Your " + Attribute + " is increased by {{G|1}}!");
            //     `StatusScreen::BuyStat`
            //        Popup.Show(text + "You may not raise an attribute above 100.");
            //        Popup.ShowYesNo(text + "It will cost {{C|1}} attribute point to increase "
            //                        + ChosenStat + " by 1.\nDo you wish to increase this attribute?")
            //        Popup.Show("You have increased your " + ChosenStat + " to {{C|" + Value + "}}!");
            //        Popup.Show(text + "You have no attribute points to raise this attribute.");
            //   ★ 全部走 `Popup.Show` ⇒ 本补丁的出口**够得到**（不需要额外挂载）
            (new System.Text.RegularExpressions.Regex(
                @"^Increases the (.+?) of anyone who reads (.+?)\.$"),
             "提升阅读者{1}的{0}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^Your (.+?) is increased by \{\{G\|1\}\}!$"),
             "你的{0}提升了 {{G|1}}！"),
            //   ★ 这两条**不锚 `^`/`$`**：`text` 前缀是运行时拼的（含当前属性值），
            //     且整串含**真换行**。实测「锚 `^`+`$` + 逐段都对」仍不匹配
            //     ⇒ 改用「不锚定 + `[\s\S]*?` 吞掉换行」的写法（稳定）。
            (new System.Text.RegularExpressions.Regex(
                @"It will cost \{\{C\|1\}\} attribute point to increase ([\s\S]*?) by 1\.[\s\S]*?Do you wish to increase this attribute\?",
                System.Text.RegularExpressions.RegexOptions.Compiled),
             "提升{0}需要花费 {{C|1}} 点属性点。\n要提升该属性吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"You have increased your (.+?) to \{\{C\|(\d+)\}\}!$"),
             "你把{0}提升到了 {{C|{1}}}！"),
            (new System.Text.RegularExpressions.Regex(
                @"You have no attribute points to raise this attribute\."),
             "你没有属性点可用来提升该属性。"),

            // ── ★★ 2.16.101：`NavigationBonus::HandleEvent` 的**裸词**（方案第 3 步）──
            //   【为什么「裸词」不能单独翻，只能连句式一起处理】
            //   反编译 `XRL.World.Parts.NavigationBonus::HandleEvent`（`_tools\_navbonus.txt`）：
            //        E.Postfix.Append("\n{{rules|Chance of becoming lost ");
            //        … E.Postfix.Append("in certain terrain "); …
            //        E.Postfix.Append((num >= 0) ? "reduced" : "increased").Append(" by ");   // L214
            //        … E.Postfix.Append("%.");  … E.Postfix.Append(" adjusted by ") …        // L231
            //   ⇒ `increased` / `reduced` / `adjusted by` **都是被 `Append` 拼进去的词**，
            //     单独看无从判断语境（同一个词在这里指「迷路几率」，别处指「速度」…）。
            //     ★ 该句式在**四个组里重复 24 处**（迷路几率 / 遭遇几率 / 遭遇某类 / 世界地图速度）
            //     ⇒ **不给裸词逐条写规则**，而是**连前缀一起**用通用规则覆盖整个家族。
            //   ★ 前缀一律用 `^(.*?)`（通用）—— 正则默认 `.` 不匹配换行，
            //     故 `\n{{rules|` 会被留在捕获组里或由 `.*?` 自然处理。
            //   ★★ **`increased` 与 `reduced` 必须分成两条**：
            //     若用 `(?:increased|reduced)` 一条，替换模板无法区分方向
            //     ⇒ 会把「降低」译成「增加」（**语义反转**，比不译更坏）。
            //   ★ 三组分值区间形态：`N%` / `N-M%`（L217-227 的两种分支）
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)increased by (\d+)(?:-(\d+))?%\.$"),
             "{0}增加了 {1}%。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)reduced by (\d+)(?:-(\d+))?%\.$"),
             "{0}降低了 {1}%。"),
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)adjusted by (-?\d+)% to ([+-]?\d+)%\.$"),
             "{0}调整了 {1}%，至 {2}%。"),

            // ── ★★ 2.16.102：`PsychicGlimmer::Update` 的长叙述段（用户截图）──
            //   反编译出处（`XRL.World.Capabilities.PsychicGlimmer::Update` L17）：
            //     Popup.Show("{{K|You are being watched.\n\nIt's a familiar feeling. … But those
            //                 who watch you now watch in spite of such simple obstructions. …
            //                 If there are ways to conceal " + who.itself + " from these seeing
            //                 eyes, if there are new kinds of darknesses to befriend, you know
            //                 nothing of them.}}");
            //   ★★ **`who.itself` 是运行时拼接的变量** ⇒ DLL 里根本没有完整整串
            //      ⇒ `ZhsHardcodedMsg` 的「整串精确匹配」**永远命中不了**（我第一版就栽在这）
            //      ⇒ 必须走本表（带 `[\s\S]*?` 通配）。
            //   ★ 用 `([\s\S]*?)` 吃掉那个变量槽（它会含中文，如「你自己」）
            //     —— 中文句里承接「藏起…不被这些眼睛看见」，代词直接落在框内即可。
            //   ★★ 2.16.102 修正：**不用 `\n\n` 字面量做正则**（转义与空格都不确定，
            //      实测匹配不上）⇒ 改用「**首尾锚 + 中间通配**」，避开换行与变量：
            //        首锚 = `{{K|You are being watched.`
            //        尾锚 = `from these seeing eyes, … you know nothing of them.}}`
            (new System.Text.RegularExpressions.Regex(
                @"^\{\{K\|You are being watched\.[\s\S]*?(from these seeing eyes, if there are new kinds of darknesses to befriend, you know nothing of them\.)\}\}$"),
             "{{K|你正被注视着。\n\n这感觉似曾相识。从前有人注视你时，是光亮出卖了你的行踪，于是你与黑暗交上了朋友。你把帽檐压低遮住双眼。你躲到一堵茅草墙后面。但如今注视你的那些目光，无视这些简单的遮蔽。它们的「看」不是借由闪烁的星辰或火炬之光，而是借由某种远为古老的东西。若真有办法在这些眼睛底下藏起自己，若真有新的黑暗可以结交，你对此一无所知。}}"),
            // 同族的短段（`Popup.Show` 亦为拼接，`who.itself` 在中段）
            // ★★ **顺序要紧**：更具体的（带 `from extradimensional watchers`）必须排在
            //    通用形态**之前** —— 否则通用形态的 `conceal ([\s\S]*?)\. For now\.`
            //    会把「…from extradimensional watchers」整段吃进捕获组，
            //    产出「藏起你自己 from extradimensional watchers。」（实测踩到）。
            (new System.Text.RegularExpressions.Regex(
                @"^(\{\{K\|)You've discovered a way to conceal ([\s\S]*?) from extradimensional watchers\. For now\.(\}\})$"),
             "{0}你已找到办法藏起{1}，不被来自异维度的注视者看见。目前如此。{2}"),
            (new System.Text.RegularExpressions.Regex(
                @"^(\{\{K\|)You've discovered a way to conceal ([\s\S]*?)\. For now\.(\}\})$"),
             "{0}你已找到办法藏起{1}。目前如此。{2}"),

            // ── ★★ 2.16.112：**死亡原因**（`RunOver` 撞碾）──
            //   反编译出处：`XRL.World.Parts.RunOver::PerformCharge`
            //       item.TakeDamage(num3, "being run over by %O.", …)
            //     ⇒ `%O` 是**旧式占位符**（宾语名，句首大写形态）——
            //       在同一批字面量里还能看到 `"from %O!"` / `"from %o slam!"`，
            //       说明这族占位符**由游戏在传入前替换掉** ⇒ 规则用 `(.+?)` 接。
            //
            //   显示出口：`XRL.CheckpointingSystem::ShowDeathMessage`
            //       → `Popup.ShowSpace(message, …, LogMessage: true)`
            //     ⇒ 本补丁的 `ShowSpace` 出口**已挂**（2.16.94 加的白名单）
            //
            //   ⚠ 不用加 `^…$` 首尾锚 —— 死亡消息是「`You died.` + 空行 + 本句」
            //     的**拼接体**（`textBuilder.Compound(message, "\n\n")`）
            //     ⇒ 用**不带锚**的形态规则，只替换这一句。
            (new System.Text.RegularExpressions.Regex(
                @"being run over by (.+?)\.?(?:$|\n)"),
             "被$1撞碾而过。"),

            // ── ★★ 2.16.104：魔像催化剂界面 · 突变条目的「 at level N」──
            //   `GameObjectMutationUnit::GetDescription()`（反编译实证）：
            //       if (Name == null) Name = Entry?.GetDisplayName();   // ← 只有 Name==null 才本地化
            //       return Name + " at level " + Level;                 // ⇒ "Regeneration at level 5"
            //   `Unit*` 里写的是**硬编码英文字面量** `Name = "Regeneration"`（非 null）
            //   ⇒ 上面那条中文化分支**从未执行**。
            //   ⚠ `Name` **不能改**（`Apply()` 用它查表：`GetMutationByName(Entry?.Name ?? Name)`）
            //   ⇒ 只在本出口译「名 + 级别」的拼接形态。
            //   ★ 突变名本身由 `ZhsPopupText.L` 的精确表译（上面已加 `Regeneration` 等）
            (new System.Text.RegularExpressions.Regex(
                @"^(.+?) at level (\d+)$"),
             "{0} {1} 级"),

            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2026-10-15（N1 批次 · **弹窗 / 确认框侧**）
            //
            //  【来源】`_tools\E5-复核B族41与E族71.md` 判定的真缺口中，现场是
            //    `Popup.Show / ShowYesNo / ShowYesNoCancel / ShowFail / WarnYesNo`
            //    （含 `Actor.Fail` → `ShowFailure` → `Popup.ShowFail` 那条链）的那些 ——
            //    **B 族 12 条（B2 B5 B6 B10 B19② B20 B27 B28 B31 B33 B34 B36 B40 B41同现场）
            //     + E 族 16 条（E1 E13 E14 E19 E22 E26 E35 E37 E39 E40 E41 E43 E44 E45 E46 E65）**。
            //    ⚠ `WarnYesNo` **内部转调 `ShowYesNo`**（报告 §0.2② 反编译实证）⇒ 本出口拦得到，
            //      「WarnYesNo 不在白名单」**不是**缺陷。
            //
            //  【为什么落这里而不是 `ZhsCombatMsgPatch.Rules`】
            //    · 这些句子的**归属出口**就是弹窗；`L`（精确表）对不上是因为它们都含运行时变量；
            //    · 本表同时被 `ZhsModernUiPatch`（`UITextSkin.Apply`）复用
            //      ⇒ 技能／变异说明面板那一类（E39）也够得到。
            //
            //  【★ 单行 / 多行都会跑到】
            //    `Reassemble` 对**含 `\n` 的串**先走 `ReassembleLines` 的**逐行通道**
            //    （整行被 `{{X|…}}` 包裹时会剥壳 → 跑规则 → 回填）
            //    ⇒ 多行弹窗（比特修理清单 B40 / 流言正文 E43）因此也能命中。
            //    **⇒ 这两条的模板故意不写 `$`**，好把后半段原样留住。
            //
            //  【★ 顺序】`REASM` 同样是**首条命中即返回** ⇒
            //    「`both … and …`」「内层 `inside`」这类**更具体的形态写在前面**。
            // ═══════════════════════════════════════════════════════════════════

            // ── B2：停止旅行确认（`NeutronFluxContainment::HandleEvent` L74）──
            //     Popup.ShowYesNo(GetWarningMessage() + " Do you want to stop travelling?")
            //   `GetWarningMessage()` = ParentObject.Does("beep") + " loudly and "
            //                         + ParentObject.GetVerb("flash") + " a warning glyph."
            //   （★ 反编译实证 —— 正是它让整句变成「中文名 + 英文动词」的丑混排）
            //   ⇒ 先接**整句**（把那段固定英文一并消费），再留**兜底**防变形
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:beeps|beep) loudly and (?:flashes|flash) a warning glyph\. Do you want to stop travelling\?$"),
             "{0}大声蜂鸣，闪出一个警告符号。要停止旅行吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) Do you want to stop travelling\?$"),
             "{0} 要停止旅行吗？"),
            //   ★ 附带（同一字面量的**另一条出口**，同文件 L82 的 `Popup.Show(GetWarningMessage())`；
            //     不在这 46 条清单里，但同一句英文，顺手补齐）
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:beeps|beep) loudly and (?:flashes|flash) a warning glyph\.$"),
             "{0}大声蜂鸣，闪出一个警告符号。"),

            // ── B5/E13：排雷成功率确认（`Tinkering_Mine::AttemptDisarm` L105）──
            //     textBuilder.Append(chanceColor).Append(" chance of success. Do you want to make the attempt?")
            //   ⚠ 前缀里有**两个 `&X` RTF 色码**（`Stat.GetChanceColor` 返回 `"&R"`/`"&W"` …
            //     —— 反编译实证）且含物品名 ⇒ 按报告草案**只吃尾巴**，前缀原样保留
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)chance of success\. Do you want to make the attempt\?$"),
             "{0}成功率。要尝试吗？"),
            // ── B6/E14：现实稳定对抗（`RealityStabilized::OptionToContest` L95）──
            //     textBuilder.Append(chanceColor).Append(" chance of success. Do you want to try?")
            //   ⚠ 前缀是**整段英文散文**（`A normality lattice prevents you from altering
            //     spacetime in …`）⇒ 同 B5，只吃尾巴
            (new System.Text.RegularExpressions.Regex(
                @"^(.*?)chance of success\. Do you want to try\?$"),
             "{0}成功率。要试试吗？"),

            // ── B19②/B32：危险物体移动确认（`GameObject::Move` L1126 · `Popup.WarnYesNo`）──
            //     "There" + gameObject6.Is + " a dangerous-looking " + ShortDisplayName
            //       + " that way. Are you sure you want to move into " + gameObject6.them + "?"
            (new System.Text.RegularExpressions.Regex(
                @"^There (?:is|are) a dangerous-looking (.+?) that way\. Are you sure you want to move into (.+?)\?$"),
             "那边有看起来很危险的{0}。确定要进入{1}吗？"),
            // ── B20/E44：下楼移动确认（同文件 L1146）──
            //     "There" + gameObject6.Is + " " + gameObject6.an()
            //       + " that way. Are you sure you want to move there and drop down a level?"
            (new System.Text.RegularExpressions.Regex(
                @"^There (?:is|are) (?:a|an) (.+?) that way\. Are you sure you want to move there and drop down a level\?$"),
             "那边有{0}。确定要往那边移动并下到下一层吗？"),

            // ── G-03 B27/B31/B33/B34/B36：「对自己动手」确认五连 ──
            //   五处现场（逐条反编译核过），形态**完全固定**（`itself` 对玩家恒为 `yourself`）：
            //     `Cudgel_Conk::PerformConk` L38    "Are you sure you want to conk " + itself
            //                                        + " on " + conkLocation + "?"
            //     `ShortBlades_Hobble::FireEvent` L59  … hobble yourself?
            //     `ShortBlades_Shank::Cast` L45        … shank yourself?
            //     `Cudgel_Slam::Cast` L99              … slam yourself?
            //     `GameObject::PerformThrow` L38       … target yourself?
            //   ★ 能力名与 `Skills.xml` 的 `DisplayName` 一致（敲晕／蹒跚打击／刺击／猛击）
            //   ⚠ `conkLocation` 来自 `Cudgel_Conk::GetConkLocation` =
            //     `"the " + body.GetFirstPart("Head").Name`（反编译实证）
            //     ⇒ 字符串里**带 `the `**，必须用 `ART` 吃掉，否则产出「往the 额头上敲…」
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to conk yourself on " + ART + @"(.+?)\?$"),
             "确定要往{0}上敲晕自己吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to hobble yourself\?$"),
             "确定要对自己使用蹒跚打击吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to shank yourself\?$"),
             "确定要对自己使用刺击吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to slam yourself\?$"),
             "确定要对自己使用猛击吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to target yourself\?$"),
             "确定要指定自己为目标吗？"),

            // ── G-04 B10/B28：拆解确认（`TinkerItem::HandleEvent` L78 / L95 / L102）──
            //   L78 : ParentObject.The + DisplayNameOnly + ("are"|Is)
            //          + " not owned by you. Are you sure you want to disassemble "
            //          + ("them"|them) + "?"
            //   L95 : "Are you sure you want to disassemble " + ("all the " + 复数名 | t()) + "?"
            //   L102: inInventory.Does("are") + " not owned by you. Are you sure you want to"
            //          + " disassemble " + ("items"|an()) + " inside " + inInventory.them + "?"
            //   ⚠ **三条顺序不可换**：①② 必须先于 ③（③ 的单条会把前两种整段吃掉）
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:is|are) not owned by you\. Are you sure you want to disassemble (.+?) inside (.+?)\?$"),
             "{0}不属于你。确定要拆解{2}里面的{1}吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:is|are) not owned by you\. Are you sure you want to disassemble (.+?)\?$"),
             "{0}不属于你。确定要拆解{1}吗？"),
            (new System.Text.RegularExpressions.Regex(
                @"^Are you sure you want to disassemble (.+?)\?$"),
             "确定要拆解{0}吗？"),

            // ── B40：比特修理弹窗（`Tinkering_Repair::HandleEvent` L76）──
            //     Popup.ShowYesNoCancel("Do you want to spend <" + bitCost + "> to repair "
            //       + item + "? You have:\n\n" + bits)
            //   ⚠ **尾不加 `$`** —— 后面紧跟比特清单（多行），写了 `$` 就把它挡在门外
            //     （逐行通道：命中的是本行，清单各行原样拼回）
            (new System.Text.RegularExpressions.Regex(
                @"^Do you want to spend <(.+?)> to repair (.+?)\? You have:"),
             "要花 <{0}> 修理{1}吗？你现有："),

            // ── G-13 E1/E43/E65：水之仪式三条 ──
            //   `WaterRitualBuySecret::RevealEntry` L21（食谱）/ L26（闲话）
            //   `WaterRitualJoinParty::HandleEvent` L15（入队）
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) shares? a recipe with you\.$"),
             "{0}与你分享了一份食谱。"),
            //   ⚠ 尾**不加 `$`**：后面还接着 `\n\n"…"` 的流言正文（正文走 `ZhsHistorySpice`）
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) shares? some gossip with you\."),
             "{0}跟你说了些闲话。"),
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) joins? you!$"),
             "{0}加入了你！"),

            // ── G-14 E19/E22/E26：「听不见 / 未绑定 / 离开队伍」──
            //   `GameObject::CheckCompanionDirection` L12   Popup.ShowFail(Does("can't") + " hear you!")
            //   `VehicleSeat::AttemptPilot` L26             ShowFailure(T() + " is not bound to you.")
            //   `AscensionSystem::BarathrumStartConversation` L36  Popup.Show(Does("have") + " left your party.")
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:can't|cannot) hear you!$"),
             "{0}听不见你！"),
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) is not bound to you\.$"),
             "{0}尚未与你绑定。"),
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:has|have) left your party\.$"),
             "{0}离开了你的队伍。"),

            // ── G-15 E35/E37/E40/E45/E46：场景描述 / 长散文 ──
            //   E35 `ThiefBot::FireEvent` L35
            //       CapitalizeExceptFormatting(MakePossessive(The + ShortDisplayName))
            //         + " pincers pass through you harmlessly."
            //     ⚠ `MakePossessive` 对中文名会**硬加英文 `'s`**（§10 英文语法后缀问题）
            //       ⇒ `(?:\'s|’s|的)` 三种形态都要吃掉，否则产出「贼机器人's的钳子」
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?)(?:'s|’s|的) pincers pass through you harmlessly\.$"),
             "{0}的钳子无害地穿过了你。"),
            //   E37 `Mutation::MagneticPulse::EmitMagneticPulse` L111
            //       Popup.Show(gameObject.Does("are") + " ripped from your body!")
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:is|are) ripped from your body!$"),
             "{0}从你身上被硬扯了下来！"),
            //   E40 `WaterRitualSkillPoint::HandleEvent` L8
            //       Popup.Show("Talking to " + The.Speaker.T(…) + " rouses in you an inert truth."
            //         + " You once wore the frock of a child. You poured salt through the cracks"
            //         + " of your fingers, and you watched worlds form. Can it be all so simple still?")
            //     ★ 尾段是**固定英文散文** ⇒ 整条进 pattern（报告草案同款）
            (new System.Text.RegularExpressions.Regex(
                @"^Talking to " + ART + @"(.+?) rouses in you an inert truth\. You once wore the frock of a child\. You poured salt through the cracks of your fingers, and you watched worlds form\. Can it be all so simple still\?$"),
             "与{0}交谈，在你心中唤起一个惰性的真理。你曾穿着孩童的罩衣，把盐从指缝间洒下，看着一个个世界成形。一切真能依旧如此简单吗？"),
            //   E45 `Enclosing::ExitEnclosure` L23  Popup.Show("It is not " + t() + " that you are enclosed by.")
            //   E46 `Chair::StandUp` L16             Actor.Fail("It is not " + t() + " that you are sitting on.")
            (new System.Text.RegularExpressions.Regex(
                @"^It is not " + ART + @"(.+?) that you are enclosed by\.$"),
             "包裹你的并不是{0}。"),
            (new System.Text.RegularExpressions.Regex(
                @"^It is not " + ART + @"(.+?) that you are sitting on\.$"),
             "你坐着的并不是{0}。"),

            // ── E41：第三人称「听不懂你的话」（`XRL.World.Effects.Stun::HandleEvent` L43）──
            //     E.Message = base.Object.Does("don't") + " seem to understand you."
            //   ★★ 这条是 §19「没翻译 vs 没打上」的**活例**（报告 §2 已留档）：
            //     `Strings.xml:5139`（Context `Confused Effect Conversation`）有**同文 ID**
            //     且值是中文，但那是**另一个效果**（Confused）的 `_T` 调用点 ⇒ **够不到这里**
            //     ⇒ 「我方文件里出现该英文」≠「该处已覆盖」。
            //   ★ 出口：该 `Message` 被 `ConversationScript::IsPhysicalConversationPossible`
            //     取出后走 `Popup.ShowFail(Message)`（报告 §0.3 / E41 行）⇒ 本表够得到
            (new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:doesn't|don't) seem to understand you\.$"),
             "{0}似乎听不懂你的话。"),

            // ── E39：充气轴突（`CyberneticsInflatableAxons::HandleEvent` L30）──
            //     AddDynamicCommand(…, "Inflate Axons", "Cybernetics",
            //       "You gain +" + Bonus + " quickness for " + Duration + " rounds,"
            //       + " then you become sluggish for 10 rounds (-10 quickness).")
            //   ⚠ `rounds` **可能已被 `ZhsUnits.Fix` 换成「回合」**：
            //     弹窗侧 `Arg0Prefix` 先跑 `ZhsUnits.Fix`；`UITextSkin`（`ZhsModernUiPatch`）
            //     那条路**不跑** ⇒ 两种形态都要接
            (new System.Text.RegularExpressions.Regex(
                @"^You gain \+(\d+) quickness for (\d+) (?:rounds?|回合), then you become sluggish for 10 (?:rounds?|回合) \(-10 quickness\)\.$"),
             "你获得 +{0} 迅捷，持续 {1} 回合；随后陷入迟缓 10 回合（-10 迅捷）。"),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  ★★ 2026-10-16（F6 · 声望族 family-wide 严查）：**状态句**
        //
        //  【为什么单独一个函数而不是往 `REASM` 加一条】
        //    `REASM` 的替换串只能做 `{0}` 文本搬运；而**状态词本身是英文**
        //    （`despised` / `favored` …）⇒ 必须**查词表换词**，搬运会把英文原样带进中文句。
        //    ⇒ 用 `REP_STATE` 词表 + 本函数，未命中**放行**（宁缺勿滥，绝不半译）。
        //
        //  【★ 出口取证（反编译 `XRL.World.Reputation::Modify` L142-192）】
        //    该方法在**同一处**拼两种句子（一次 `Popup.Show(textBuilder.ToString())`，L187）：
        //      ① 声望增减行（我方**已覆盖** —— 规则在 `ZhsCombatMsgPatch`，
        //         弹窗侧经 `Arg0Prefix` → `TranslateText` 够得到）：
        //           Append("Your ").Append("reputation with {{C|").Append(FactionName)
        //             .Append("}} ").Append(@for >= 0 ? "increased" : "decreased")
        //             .Append(" by {{").Append('G'/'R').Append('|').Append(Math.Abs(@for))
            //             .Append("}} to {{").Append(color).Append('|').Append(num2).Append("}}.");
        //         ★ 同局日志实证（`Player.log` L5723-5724）：
        //             进 `Your reputation with 多肉植物 increased by 110 to 110.`
        //             出 `你与多肉植物的声望提升了 110 点，现为 110。`
        //      ② **状态句**（本函数新增；原来**一条规则都没有**）：
        //           `text` ∈ { loved, favored, indifferent, disliked, despised }
        //             （L82/87/92/98/104/109/114/120 = **全量 8 个赋值点，5 个不同词**）
        //           if (!flag3)   // 「不是变中立」那一支 —— **主语是玩家**
        //               Append(SingleLine ? " " : "\n\n").Append("You are now {{")
        //                 .Append(color).Append('|').Append(text).Append("}} by {{C|")
        //                 .Append(FactionName).Append("}}.");
        //           else          // 「变中立」那一支（flag3）—— **主语是派系**
        //               Append(SingleLine ? " " : "\n\n")
        //                 .Append(FactionName.Capitalize()).Append(' ')
        //                 .Append(Faction.Plural ? "are" : "is").Append(" now {{")
        //                 .Append(color).Append('|').Append(text).Append("}} to you.");
        //         ⇒ 骨架是 `"You are now "` / `"}} by {{C|"` / `"}}."` 这种**碎片段**，
        //           串表里**没有整句可查**（§38 同族）⇒ 只能按句式改写。
        //         ⚠ **原来为什么会半译**：用户截图那两行是**同一次弹窗的两行**，
        //           行② 落到 `ZhsCombatMsgPatch` 的 `^You are now (.+)\.$`（该文件 L648）上，
        //           那条把 `despised` **整段当变量**搬进「你现在是…。」⇒ 骨架中文、**词仍英文**。
        //           本函数排在 `Reassemble` 的最前面 ⇒ 先于那条通用规则命中。
        //
        //  【★ 状态词全表（5 个，一个不漏）】
        //      loved=深爱 · favored=爱戴 · indifferent=漠然 · disliked=厌恶 · despised=憎恶
        //    ★ 与 `Faction::GetFeelingText` 的五档整句译法（我方 `Strings.xml:3368-3372`
        //      用「憎恨 / 厌恶 / 不在乎 / 爱戴 / 敬慕」）保持同一情感词根；
        //      若两处口径冲突，**以官方整句那 5 条为准**（它们是 (Context,ID) 正式条目）。
        //
        //  【★ 反例（必须不被命中 ⇒ 走原路）】
        //      · `You are now {{C|中毒}} by 咬颚兽.` ⇒ 词不在表里 ⇒ 返回 null ⇒ 放行
        //      · `你现在是despised。` ⇒ 判据要求 `^You are now `（英文骨架）⇒ 不吃
        //      · `You are now a 看守 of 约帕.`（爵阶提升族的自然语序）⇒ `a`+空格 ⇒ 词表不认 ⇒ 放行
        //      · `咬颚兽 is now 憎恶 to you.` ⇒ 词已是中文 ⇒ 词表不认 ⇒ 放行（幂等）
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>`You are now {{C|despised}} by {{C|咬颚兽}}.`（剥壳后 `You are now despised by 咬颚兽.`）</summary>
        private static readonly System.Text.RegularExpressions.Regex REP_STATE_BY =
            new System.Text.RegularExpressions.Regex(
                @"^You are now ([A-Za-z]+) by (.+?)\.$");

        /// <summary>`咬颚兽 is now {{R|despised}} to you.`（`Faction.Plural` 时是 `are`）</summary>
        private static readonly System.Text.RegularExpressions.Regex REP_STATE_TO =
            new System.Text.RegularExpressions.Regex(
                @"^" + ART + @"(.+?) (?:is|are) now ([A-Za-z]+) to you\.$");

        /// <summary>
        /// ★★ F6 声望族「状态词」表 —— `Reputation::Modify` 里 `text` 变量的**全量 5 词**
        /// （反编译 L82/87/92/98/104/109/114/120 的 8 个赋值点去重所得）。
        /// </summary>
        internal static readonly Dictionary<string, string> REP_STATE =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "loved",       "深爱" },
            { "favored",     "爱戴" },
            { "indifferent", "漠然" },
            { "disliked",    "厌恶" },
            { "despised",    "憎恶" },
        };

        /// <summary>英文状态词 → 中文；**不在表里返回 null**（调用方放行，绝不半译）。</summary>
        internal static string RepState(string en)
        {
            if (string.IsNullOrEmpty(en)) return null;
            return REP_STATE.TryGetValue(en, out string zh) ? zh : null;
        }

        /// <summary>
        /// 声望**状态句**改写；命中返回中文，**未命中返回 null**（调用方原样放行）。
        /// 剥壳形态（`StripColorTags` 之后）：
        ///   `You are now despised by 咬颚兽.`      ⇒ `咬颚兽现在憎恶你。`
        ///   `咬颚兽 is now despised to you.`        ⇒ `咬颚兽现在对你憎恶。`
        /// </summary>
        internal static string RepStateSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            System.Text.RegularExpressions.Match m = REP_STATE_BY.Match(t);
            if (m.Success)
            {
                string st = RepState(m.Groups[1].Value);
                if (st == null) return null;          // 词不在表里 ⇒ 放行（绝不半译）
                string fac = m.Groups[2].Value.Trim();
                if (fac.Length == 0) return null;
                // 语序：中文「<派系>现在<情感>你。」（比「你现在受到…」自然，且与
                //       `Faction::GetFeelingText` 的「X 憎恨你。」同构）
                return fac + "现在" + st + "你。";
            }
            m = REP_STATE_TO.Match(t);
            if (m.Success)
            {
                string fac = m.Groups[1].Value.Trim();
                string st = RepState(m.Groups[2].Value);
                if (st == null || fac.Length == 0) return null;
                return fac + "现在对你" + st + "。";
            }
            return null;
        }

        /// <summary>
        /// 声望**弹窗两段式**：`第一句 + \n\n + 状态句`。
        /// 两段都翻成功才返回；否则返回 null（调用方原样放行，绝不半译）。
        /// 第一句复用 `ZhsCombatMsgPatch.TranslateText`（消息栏同一条规则），
        /// 第二句复用 `RepStateSentence`（词表只有一份）。
        /// </summary>
        internal static string RepPopupTwoPart(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int k = s.IndexOf("\n\n", StringComparison.Ordinal);
            if (k <= 0) return null;
            string a = s.Substring(0, k).Trim();
            string b = s.Substring(k + 2).Trim();
            if (a.Length == 0 || b.Length == 0) return null;
            string zb = RepStateSentence(b);
            if (string.IsNullOrEmpty(zb)) return null;          // 第二段不是状态句 ⇒ 不是这一族
            string za = ZhsCombatMsgPatch.TranslateText(a);
            if (string.IsNullOrEmpty(za) || za == a) return null; // 第一段没翻成 ⇒ 放行
            return za + "\n\n" + zb;
        }

        private const string Tag = "[QudZhsCookbook] 弹窗接管：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied || ZhsPopupText.Disabled) return;
            _applied = true;
            try
            {
                MethodInfo pre = typeof(ZhsPopupPatch).GetMethod(
                    "Arg0Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) return;
                // `PickOption` 专用：同时改**选项列表**（第 5 参）
                MethodInfo preOptions = typeof(ZhsPopupPatch).GetMethod(
                    "PrefixOptions", BindingFlags.NonPublic | BindingFlags.Static);

                int n = 0;
                Harmony h = new Harmony("Nattens.QudZhsCookbook.popup");

                // ① Popup 上的静态方法
                Type tp = FindType("XRL.UI.Popup");
                if (tp != null)
                {
                    foreach (MethodInfo m in tp.GetMethods(BindingFlags.Public
                                 | BindingFlags.Static))
                    {
                        // ★★ 2.15.97：补上 `ShowSpace` / `ShowBlock`
                        //
                        //   用户截图：`You're lost! Regain your bearings by exploring
                        //   your surroundings.` **整句英文**（按钮却是中文「继续」）。
                        //
                        //   反编译 `XRL.World.Parts.TeleportGate::CheckPossibleSubject`：
                        //       Popup.ShowSpace(IComponent<GameObject>._S(
                        //           "TeleportGate Lost Popup",
                        //           "You're lost! Regain your bearings by exploring your surroundings."));
                        //   ⇒ 走的是 **`Popup.ShowSpace`**，而本补丁的方法白名单里
                        //     **没有它** ⇒ 这条出口从未被接管。
                        //   （我方 `Strings.xml` 该条**有译文**：你迷路了！探索一下四周，
                        //     重新辨明方位。 —— 只是 `_S()` 查表也没命中，所以整句英文。）
                        //
                        //   ⚠ `ShowSpace` 是「覆盖全屏的弹窗」变体（同族还有 `ShowBlock`）；
                        //     以后见到新出口，**先查它在不在这个白名单里**。
                        // ★★ 2026-10-15 补 **Async 变体**（本族真根因）：
                        //   白名单原来只有同步版 ⇒ `Popup.AskStringAsync` / `ShowYesNoCancelAsync`
                        //   等**一条都没接管**，而代词/性别定制提示**全走 Async**
                        //   ⇒ 那是「半中半英」（英文骨架 + 中文「代词组」+ `?`）的直接原因。
                        //   实测方法集（`native-calls.tsv` 枚举 `XRL.UI.Popup::*Async`）：
                        //     AskNumberAsync · AskStringAsync · PickOptionAsync · ShowAsync ·
                        //     ShowColorPickerAsync · ShowFailAsync · ShowKeybindAsync ·
                        //     ShowYesNoAsync · ShowYesNoCancelAsync
                        //   （`NewPopupMessageAsync` / `ShowProgressAsync` 无玩家提示，不收）
                        //   ⚠ Async 方法的 Prefix 在**调用时**跑（状态机启动前）⇒ 改 arg0 有效 ✓
                        if (m.Name != "ShowYesNoCancel" && m.Name != "ShowYesNo"
                            && m.Name != "AskString" && m.Name != "AskYesNo"
                            && m.Name != "PickOption" && m.Name != "ShowFail"
                            && m.Name != "Show" && m.Name != "ShowSpace"
                            && m.Name != "ShowBlock"
                            // ★★ 2026-10-17（测试窗口 #57）：`Vehicle::HandleEvent` 用
                            //   `Popup.PickGameObject("Choose a follower", …)` 选载具座位
                            //   —— 该出口**一直不在白名单**，所以标题永远英文。
                            //   签名 `PickGameObject(string Title, IList<GameObject> …)` ⇒ 首参是 string ✓
                            && m.Name != "PickGameObject"
                            && m.Name != "ShowYesNoCancelAsync" && m.Name != "ShowYesNoAsync"
                            && m.Name != "AskStringAsync" && m.Name != "AskNumberAsync"
                            && m.Name != "PickOptionAsync" && m.Name != "ShowAsync"
                            && m.Name != "ShowFailAsync" && m.Name != "ShowKeybindAsync"
                            && m.Name != "ShowColorPickerAsync")
                        {
                            continue;
                        }
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length < 1 || ps[0].ParameterType != typeof(string))
                            continue;
                        try
                        {
                            // `PickOption` 有**选项列表**参数（第 5 个 string 集合）
                            // ⇒ 用能改选项的 Prefix；其余仍只改标题。
                            MethodInfo use = pre;
                            if (m.Name == "PickOption" && ps.Length >= 5
                                && preOptions != null)
                                use = preOptions;
                            h.Patch(m, prefix: new HarmonyMethod(use));
                            n++;
                        }
                        catch { }
                    }
                }

                // ② PickDirection.ShowPicker
                Type td = FindType("XRL.UI.PickDirection");
                if (td != null)
                {
                    foreach (MethodInfo m in td.GetMethods(BindingFlags.Public
                                 | BindingFlags.Static))
                    {
                        if (m.Name != "ShowPicker") continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length < 1 || ps[0].ParameterType != typeof(string))
                            continue;
                        try
                        {
                            h.Patch(m, prefix: new HarmonyMethod(
                                (ps.Length >= 5 && preOptions != null)
                                    ? preOptions : pre));
                            n++;
                        }
                        catch { }
                    }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（"
                    + ZhsPopupText.L.Count + " 条弹窗文本）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static Type FindType(string full)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(full, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        /// <summary>把第 0 参（弹窗正文）按白名单精确翻译，未命中时**回落到句式规则**。</summary>
        /// <summary>
        /// **选项**文本翻译（`PickOption` 的第 5 参）。
        ///
        /// ★★ 2.16.3 新增：此前本补丁**只改第 0 参（标题）**，
        ///    `PickOption` 的选项列表**从未被处理** ⇒ 反复出现
        ///    「标题中文 + 选项英文」的乱拼（`Bed` 睡觉弹窗、召回器选单）。
        /// </summary>
        internal static string TrOption(string s)
        {
            if (ZhsPopupText.Disabled || string.IsNullOrEmpty(s)) return s;
            string zh = ZhsPopupText.Tr(s);
            if (ReferenceEquals(s, zh))
            {
                // 前缀式（`Until 浅滩时分`）
                foreach (var kv in OPTION_PREFIX)
                {
                    if (s.StartsWith(kv.Key, StringComparison.Ordinal) && s.Length > kv.Key.Length)
                    {
                        zh = kv.Value + s.Substring(kv.Key.Length);
                        break;
                    }
                }
            }
            if (ReferenceEquals(s, zh))
            {
                // 句式规则兜底（纯英文的完整句）
                string t = ZhsCombatMsgPatch.TranslateText(s);
                if (!string.IsNullOrEmpty(t)) zh = t;
            }
            // ★★ 2.16.94：**状态标签**（最后一道，对上面每一级的产出都生效）
            //   实证（用户截图）：`En Garde! {{Y|[allowed]}}`
            //   反编译出处：`GameObject::ChangeCompanionAbilityUse`
            //       displayName + " {{Y|[allowed]}}"     // AIDisable == false
            //       displayName + " {{K|[forbidden]}}"   // AIDisable == true
            //   ⇒ 标签是**硬编码**拼在能力名后面的（与能力名是否已汉化无关）
            //   ★ 放在**最后**：`L` 精确表里若有含 `[allowed]` 的整条，应当优先由它处理。
            return NormalizeTags(zh);
        }

        /// <summary>把游戏硬编码的状态标签换成中文（幂等：已中文则不动）。</summary>
        private static string NormalizeTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // ★★ 2.16.105：**标签表一次列全** —— 2.16.94 只做了 `[allowed]`/`[forbidden]`，
            //   用户下一张截图立刻撞上 `[toggled on]`（§45「门禁要成组维护」同源）。
            //   反编译实证（`ildex find "toggled on"`，共 4 处、**大小写不一致**）：
            //     `ChangeCompanionAbilityUse`        → `{{g|[toggled on]}}`
            //     `GetMovementCapabilitiesEvent::Add`→ `[toggled on]` / `{{g|[toggled on]}}`
            //     `AbilityManager::Show`             → `{{K|[{{g|Toggled on}}]}}`  ← 首字母大写
            //     `Qud.UI.AbilityManagerLine::setData` → 同上
            //   ⇒ 必须**大小写不敏感**，且要处理**三档形态**。
            s = ReplaceTag3(s, "toggled on", "{{K|[{{g|已开启}}]}}", "{{g|[开启]}}", "[开启]");
            s = ReplaceTag3(s, "toggled off", "{{K|[{{g|已关闭}}]}}", "{{g|[关闭]}}", "[关闭]");
            s = ReplaceTag3(s, "allowed", null, "{{Y|[允许]}}", "[允许]");
            s = ReplaceTag3(s, "forbidden", null, "{{K|[禁止]}}", "[禁止]");
            return s;
        }

        /// <summary>
        /// 一个标签的**三档**大小写不敏感替换（`outerZh` 为 null 表示无嵌套形态）：
        /// `{{K|[{{g|X}}]}}` · `{{g|[X]}}` · `[X]`
        /// ★ 替换顺序：**外层标记优先** —— 若先换内层，外层就再也匹配不上了。
        /// </summary>
        private static string ReplaceTag3(string s, string en, string outerZh,
            string coloredZh, string plainZh)
        {
            if (s.IndexOf("[", StringComparison.Ordinal) < 0) return s;
            if (outerZh != null)
                s = ReplaceCI(s, "{{K|[{{g|" + en + "}}]}}", outerZh);
            if (coloredZh != null)
                s = ReplaceCI(s, "{{g|[" + en + "]}}", coloredZh);
            if (plainZh != null)
                s = ReplaceCI(s, "[" + en + "]", plainZh);
            return s;
        }

        /// <summary>大小写不敏感地整体替换（`string.Replace` 没有这个重载）。</summary>
        private static string ReplaceCI(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length + 16);
            int pos = 0;
            while (i >= 0)
            {
                sb.Append(s, pos, i - pos).Append(to);
                pos = i + from.Length;
                i = s.IndexOf(from, pos, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(s, pos, s.Length - pos);
            return sb.ToString();
        }

        private static void Arg0Prefix(object[] __args)
        {
            try
            {
                if (ZhsPopupText.Disabled || __args == null || __args.Length < 1)
                    return;
                string s = __args[0] as string;
                if (s == null) return;
                ZhsHookTrace.Step("Popup.arg0", s);
                // ★ 2.16.127：先过**单位词替换**（rounds ⇒ 回合，用户指示「不管出口」）
                //   ⇒ 后面的查表/规则都在**替换后**的串上跑，改一次即全局生效
                string su = ZhsUnits.Fix(s);
                if (su != s)
                {
                    s = su;
                    __args[0] = s;
                }
                string zh = ZhsPopupText.Tr(s);
                if (ReferenceEquals(s, zh))
                {
                    // `That is out of range! (8 squares)` ⇒ `太远了！（8 格）`
                    var mm = RANGE_RE.Match(s);
                    if (mm.Success)
                    {
                        __args[0] = "太远了！（" + mm.Groups[1].Value + " 格）";
                        return;
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // ★★★ 2026-10-16【A8 修正】`Reassemble` **提到** `TranslateText` **之前**
                // ═══════════════════════════════════════════════════════════════
                //  【为什么】原顺序是 `精确表 → RANGE_RE → TranslateText → Reassemble`，
                //     而 `TranslateText` 命中就 `return` ⇒ **把更具体的 `REASM` 挡死**。
                //  【实测证据】（`roslyntest -- muttrace`，调真函数跑真串）：
                //     · 弹窗 `…buy 变异 before rapidly mutating?`
                //         `TranslateText` 命中贪婪版 `…buy (.+)\?$` ⇒ 污染：
                //             `…Would 你 like to spend…`（`Would`/`like to spend` 残留英文）
                //         而 `Reassemble` 本可给**完全正确**的
                //             「你的基因组进入兴奋状态！你愿意花费…吗？」
                //     · `You have rapidly advanced 多臂 by 三 ranks to rank 4!`
                //         `TranslateText` 产出**半中半英**（用户截图原样）：
                //             `你有rapidly advanced 多臂 by 三 ranks to rank 4！`
                //         而 `Reassemble` 本可给「你已将多臂快速提升了三级，达到4级！」
                //  【判据】`REASM` 的规则**全是 `^…$` 整串锚定 + 「英文骨架 + 中文变量」**，
                //     比 `TranslateText` 的泛化句式**更具体** ⇒ **具体的必须先跑**。
                //  【兜底】`Reassemble` 未命中时返回 null ⇒ **自然落到 `TranslateText`** ✓
                string rz = Reassemble(s);
                if (!string.IsNullOrEmpty(rz) && rz != s)
                {
                    __args[0] = rz;
                    return;
                }

                if (ReferenceEquals(s, zh))
                {
                    // ★ 白名单未命中 ⇒ 试 `ZhsCombatMsgPatch` 的**句式规则表**。
                    //   为什么必须这一步：`Qud.API.IBaseJournalEntry::DisplayMessage`
                    //   默认走 `Popup.Show(Message, …)`（**不经过** MessageQueue），
                    //   所以「日志笔记」那几条规则在 `EmitMessage` 出口永远跑不到。
                    //   实测症状：`You note this piece of information in the
                    //   Sultan Histories > 雷谢夫 section of your journal.`
                    zh = ZhsCombatMsgPatch.TranslateText(s);
                }
                if (!string.IsNullOrEmpty(zh) && !ReferenceEquals(s, zh) && zh != s)
                    __args[0] = zh;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Arg0Prefix 失败：" + e);
            }
        }

        /// <summary>
        /// 【修法乙】拼装弹窗的**重组**：`英文片段 + 中文变量 + 英文片段` ⇒ 完整中文。
        ///
        /// 命中即返回中文；未命中返回 `null`（调用方保留原文）。
        /// 中文模板用 `{0}` `{1}` … 对应正则的捕获组（**从 1 开始**，与 `$N` 习惯一致）。
        /// </summary>
        internal static string Reassemble(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            // ═══════════════════════════════════════════════════════════════════
            //  ★★ 2026-10-17（测试窗口 #41）：声望弹窗是**两段**
            //   实机：`Your reputation with 昆虫 increased by 100 to -180.`
            //         `\n\n`
            //         `昆虫 are now indifferent to you.`
            //   · 消息栏的**第一句**有规则（`ZhsCombatMsgPatch`），但弹窗是**一整串**，
            //     两条单句规则都锚 `^…$` ⇒ 整段不命中 ⇒ 两段全英文。
            //   · 这里按 `\n\n` 拆两段，**各自走自己的通道**（第一句走规则表、
            //     第二句走 `RepStateSentence`），**两段都成功才返回**（否则放行原文，不半译）。
            string repPop = RepPopupTwoPart(s);
            if (repPop != null)
            {
                ZhsPopupText.Hits++;
                try
                {
                    if (ZhsPopupText.Hits <= 20)
                        UnityEngine.Debug.Log(Tag + "（声望两段）" + s + "  =>  " + repPop);
                }
                catch { }
                return repPop;
            }

            // ═══════════════════════════════════════════════════════════════════
            //  ★★ 2026-10-16（F6）：**声望状态句**必须在下面的早退之前处理
            // ═══════════════════════════════════════════════════════════════════
            //  【为什么排最前】`You are now despised by 咬颚兽.` 会被
            //    `ZhsCombatMsgPatch` 的通用规则 `^You are now (.+)\.$` 命中
            //    ⇒ 产出「你现在是despised。」（骨架中文、**词仍英文**）——
            //    这正是用户截图那一行。本函数先跑一次，命中就整句给中文。
            //  【判据】只认 `^You are now <英文词> by …` / `<派系> is|are now <英文词> to you.`
            //    且**词必须在 `REP_STATE` 表里**；否则返回 null ⇒ 原样放行（绝不半译）。
            string rep = RepStateSentence(s);
            if (rep != null && rep != s)
            {
                ZhsPopupText.Hits++;
                if (ZhsPopupText.Hits <= 20)
                {
                    try
                    {
                        UnityEngine.Debug.Log(Tag + "（声望状态句）" + s.Trim() + "  =>  " + rep);
                    }
                    catch { }
                }
                return rep;
            }

            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2026-10-12：**「X at level N」必须在下面的早退之前处理**
            // ═══════════════════════════════════════════════════════════════════
            //  【为什么】下面的判据要求「**同时**含中文和英文」——
            //     而 `Corrosive Gas Generation at level 1` 是**纯英文**（变异名也是英文）
            //     ⇒ 会被**直接放行** ✗ ⇒ `REASM` 里的任何规则都够不着 ✗✗
            //     **这就是用户说的「at level 拼接段死了」的真根因** ✓
            //  【出处】`GameObjectSkillUnit` / `ItemEffectBonusMutation` 族的详情行；
            //     变量 `=mutation=` 用的是**英文原名**（不走 `GetDisplayName()`）✗
            //  【修法】在这里先查我们的**变异名表**（`ZhsMutationZh`，
            //     `Corrosive Gas Generation ⇒ 腐蚀气体生成` 等全表），查到就拼中文 ✓
            try
            {
                var am = AT_LEVEL2.Match(s.Trim());
                if (am.Success)
                {
                    string nm = am.Groups[1].Value.Trim();
                    // ★ 两种情形都要接：
                    //   ① 名字是**英文**（`Corrosive Gas Generation`）⇒ 查名表换成中文 ✓
                    //   ② 名字**已经是中文**（`双翼` / `双头`）⇒ **直接用** ✓
                    //      （漏了 ② 就会出现「双翼 at level 10」这种半译 —— 实测漏过 ✗）
                    string zh = MutName(nm);
                    if (zh == null && HasCjk(nm)) zh = nm;
                    if (zh != null)
                    {
                        string r = zh + " " + am.Groups[2].Value + " 级";
                        ZhsPopupText.Hits++;
                        if (ZhsPopupText.Hits <= 20)
                            UnityEngine.Debug.Log(Tag + "（at level 段）" + s + "  =>  " + r);
                        return r;
                    }
                }
            }
            catch { }

            // 早退：**只跳过「纯中文」**（那种串不需要重组）。
            //
            // ★★★ 2026-10-12 修正：原来写的是 `if (!hasZh || !hasEn) return null;`
            //   —— 那会把**纯英文**的模板也放行 ✗，而下面这些恰恰**全是纯英文**：
            //       `Has every Cudgel skill`          （`GameObjectSkillUnit`）
            //       `Can use Corrosive Gas Generation at level 1.`（`ItemEffectBonusMutation`）
            //   ⇒ 用户三张截图里那两条**就是被这个早退挡掉的** ✗✗
            //   ⇒ 改为只跳「有中文、无英文」；纯英文串照样过一遍规则表
            //     （规则都是 `^…$` **整串锚定** ⇒ 不会误伤普通英文 ✓）
            bool hasZh = false, hasEn = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u4e00' && c <= '\u9fff') hasZh = true;
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) hasEn = true;
                if (hasZh && hasEn) break;
            }
            if (hasZh && !hasEn) return null;

            // ═══════════════════════════════════════════════════════════════════
            //  ★★★ 2026-10-15：**多行串先走逐行通道**
            // ═══════════════════════════════════════════════════════════════════
            //  【为什么】`REASM` 的规则全是 `^…$` 整串锚定、`Regex` 也没开 Multiline，
            //     而游戏喂进来的是**一个多行串**
            //     （`StatusScreen::ShowMutationPopup` 把「描述 + `{{w|This rank}}:` + 等级文本
            //       + `{{w|Next rank}}:` + 等级文本 + `AppendRankBoost` 的若干行 + cost 行」
            //       拼成一个 `TextBuilder` 再 `Popup.ShowYesNo(...)`）
            //     ⇒ `^` 只匹配串首、`$` 只匹配串尾 ⇒ rank 家族那 17 条规则**全部够不着**
            //     （`Player.log` 实证：`弹窗接管：（拼装重组）` **0 条**）
            //  【另一件事】rankBoost 每行被 `AppendColored` 裹成 `{{C|…}}`/`{{G|…}}`/`{{R|…}}`
            //     ⇒ 逐行通道会**剥壳跑规则、命中再回填**（一套规则同时管「裸文本」与「带色包裹」）
            string byLines = ReassembleLines(s);
            if (byLines != null) return byLines;

            // ★ 2026-10-17：skill-unit 单行（多行由上面的逐行通道走 `TryReasm`）
            //   `-- Has every Cudgel skill` ⇒ `-- 精通所有棍棒技能`
            string sku = SkillUnitZh(s);
            if (sku != null)
            {
                ZhsPopupText.Hits++;
                try
                {
                    if (ZhsPopupText.Hits <= 20)
                        UnityEngine.Debug.Log(Tag + "（技能单位）" + s.Trim()
                            + "  =>  " + sku);
                }
                catch { }
                return sku;
            }

            foreach (var (rx, tpl) in REASM)
            {
                System.Text.RegularExpressions.Match m;
                try { m = rx.Match(s.Trim()); }
                catch { continue; }
                if (!m.Success) continue;
                string outZh = tpl;
                for (int g = 1; g < m.Groups.Count; g++)
                {
                    if (!m.Groups[g].Success) continue;
                    outZh = outZh.Replace("{" + (g - 1) + "}", m.Groups[g].Value.Trim());
                }
                outZh = FixReason(outZh);
                ZhsPopupText.Hits++;
                try
                {
                    if (ZhsPopupText.Hits <= 20)
                        UnityEngine.Debug.Log(Tag + "（拼装重组）" + s + "  =>  " + outZh);
                }
                catch { }
                return outZh;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ★★★ 2026-10-15：逐行重组通道（含「剥 `{{X|…}}` 外壳」）
        // ═══════════════════════════════════════════════════════════════════
        /// <summary>
        /// 把整串按行拆开，**逐行**跑 `REASM`；命中则替换该行、其余行原样拼回。
        /// 无任何改动 ⇒ 返回 null（让调用方走原来的整串逻辑）。
        /// ★ 单行串直接返回 null（交给整串逻辑，避免两条路重复处理）。
        /// </summary>
        private static string ReassembleLines(string s)
        {
            if (s.IndexOf('\n') < 0) return null;
            string[] lines = s.Replace("\r\n", "\n").Split('\n');
            bool touched = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;

                // 保留该行的首尾空白（规则按 trim 后的文本匹配）
                string core = line.Trim();
                if (core.Length == 0) continue;
                string lead = line.Substring(0, line.Length - line.TrimStart().Length);
                string trail = line.Substring(line.TrimEnd().Length);

                // ① 整行精确表（`Line`）—— 优先于正则（零误伤、可读）
                string r;
                Line.TryGetValue(core, out r);
                if (r == null)
                {
                    // ② 整行被 `{{X|…}}` 包裹 ⇒ 剥壳 → 查表/跑规则 → 回填
                    string open, close, inner = UnwrapLine(core, out open, out close);
                    if (inner != null)
                    {
                        string ri;
                        Line.TryGetValue(inner, out ri);
                        if (ri == null) ri = TryReasm(inner);
                        if (ri != null) r = open + ri + close;
                    }
                }
                if (r == null) r = TryReasm(core);      // ③ 正则模板表
                if (r == null || r == core) continue;
                lines[i] = lead + r + trail;
                touched = true;
            }
            if (!touched) return null;
            string outS = string.Join("\n", lines);
            ZhsPopupText.Hits++;
            // ★ 日志**单独包 try**：`UnityEngine.Debug.Log` 是 ECall，抛异常会吃掉
            //   已经算好的重组结果（同一教训在 `ZhsUnits.Fix` 里吃过一次）
            try
            {
                if (ZhsPopupText.Hits <= 20)
                    UnityEngine.Debug.Log(Tag + "（逐行重组）" + s + "  =>  " + outS);
            }
            catch { }
            return outS;
        }

        /// <summary>
        /// 原因短语里的**固定英文短语** ⇒ 中文（属性名走 `ZhsStatPopup.St()`，单一事实源）。
        /// 依据：`BaseMutation::CalcLevel` 的 17 条 reason 句式（本轮全量反编译）。
        /// </summary>
        private static readonly Dictionary<string, string> REASON =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "adrenaline", "肾上腺素" },
            { "metabolizing effect", "代谢效应" },
            { "tonic effect", "药剂效应" },
        };

        /// <summary>
        /// ★★ 2026-10-15：**整行精确表**（键 = 英文整行 ⇒ 中文）。
        ///
        /// 【为什么需要它】`_tools\_funnel_concat2.py` 把「出口表缺 `String.Concat`」那个
        ///   口径洞收敛成 `concat-真缺口.tsv`（68 条），其中一大类是**硬编码在 C# 里的
        ///   逐行碎片**——`Mutation::GetLevelText` / `Effect::GetDetails` /
        ///   `Mutation::GetDescription` 里 `"英文骨架" + 变量` 现拼的行。
        ///   它们既不在 XML 里（隐藏变异连 `<description>` 都没有），也走不到字符串表
        ///   ⇒ 只能在**显示出口**按整行替换。
        ///
        /// 【为什么是「整行精确」而不是正则】
        ///   · 这批文本**只以整行形态出现**（各自以 `\n` 结尾）⇒ 整行相等判据**零误伤**；
        ///   · 它们多是**半句 + 运行时变量**（如 `...within a radius of ` + 数字 + `.`）
        ///     ⇒ 正则化收益低、风险高（本项目在「短词抢先」上吃过亏 §44）。
        ///
        /// 【出口】弹窗入口（`Popup.*` Prefix）+ 现代界面 `UITextSkin.Apply`
        ///   （后者经 `Reassemble` 的**逐行通道**，见 2026-10-15 的 `ReassembleLines`）。
        ///
        /// 【维护】新增条目只需一行；**键必须与游戏字面量逐字节一致**（含尾部空格）。
        /// </summary>
        internal static readonly Dictionary<string, string> Line =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Regenerates at thrice the normal rate.", "\u4ee5\u4e09\u500d\u901f\u5ea6\u81ea\u7136\u6062\u590d\u3002" },
            { "Negative status effects wear off at thrice the normal rate.", "\u8d1f\u9762\u72b6\u6001\u4ee5\u4e09\u500d\u901f\u5ea6\u6d88\u9000\u3002" },
            { "You are covered in a thick coat of fur, which protects you from the elements.", "\u4f60\u88ab\u4e00\u5c42\u539a\u6bdb\u8986\u76d6\uff0c\u80fd\u4fdd\u62a4\u4f60\u514d\u53d7\u98ce\u971c\u4e4b\u82e6\u3002" },
            { "+5 Heat Resistance", "+5 \u70ed\u6297\u6027" },
            { "+5 Cold Resistance", "+5 \u5bd2\u6297\u6027" },
            { "You can sense other mental mutants through the psychic aether.", "\u4f60\u80fd\u901a\u8fc7\u5fc3\u7075\u4ee5\u592a\u611f\u77e5\u5176\u4ed6\u7cbe\u795e\u53d8\u5f02\u8005\u3002" },
            { "There's a chance you identify detected enemies.", "\u6709\u51e0\u7387\u8bc6\u522b\u51fa\u88ab\u63a2\u6d4b\u5230\u7684\u654c\u4eba\u3002" },
            { "You produce a viscous slime that you can spit at things.", "\u4f60\u5206\u6ccc\u51fa\u4e00\u79cd\u9ecf\u7a20\u7684\u8f6f\u6ce5\uff0c\u53ef\u4ee5\u671d\u4e1c\u897f\u5410\u51fa\u53bb\u3002" },
            { "Covers an area with slime", "\u7528\u8f6f\u6ce5\u8986\u76d6\u4e00\u7247\u533a\u57df" },
            { "You can walk over slime without slipping.", "\u4f60\u80fd\u8d70\u8fc7\u8f6f\u6ce5\u800c\u4e0d\u6ed1\u5012\u3002" },
            { "You ignite a small fire with your mind.", "\u4f60\u4ee5\u5fc3\u667a\u70b9\u71c3\u4e00\u5c0f\u4e1b\u706b\u3002" },
            { "You fill a nearby area with frosty webs.", "\u4f60\u7528\u5bd2\u971c\u86db\u7f51\u586b\u6ee1\u9644\u8fd1\u4e00\u7247\u533a\u57df\u3002" },
            { "Shoots a beam with a 3-round windup that dismembers the limbs of its targets.", "\u84c4\u529b 3 \u56de\u5408\u540e\u5c04\u51fa\u4e00\u9053\u5c04\u7ebf\uff0c\u5207\u65ad\u76ee\u6807\u7684\u80a2\u4f53\u3002" },
            { "Effects that make non-biological clones of you produce twice as many.", "\u4f7f\u4f60\u975e\u751f\u7269\u590d\u5236\u4f53\u7684\u4ea7\u51fa\u52a0\u500d\u3002" },
            { "25% chance to refract light-based attacks", "25% \u51e0\u7387\u6298\u5c04\u57fa\u4e8e\u5149\u7684\u653b\u51fb" },
            { "Can launch into a destructive charge after a one round warm-up.", "\u4e00\u8f6e\u9884\u70ed\u540e\u53ef\u53d1\u8d77\u6bc1\u706d\u6027\u51b2\u950b\u3002" },
            { "Additionally, you sometimes scare creatures passively.", "\u6b64\u5916\uff0c\u4f60\u6709\u65f6\u4f1a\u88ab\u52a8\u5730\u5413\u9000\u751f\u7269\u3002" },
            { "You cause adjacent creatures to flee in terror.", "\u4f60\u4f7f\u76f8\u90bb\u7684\u751f\u7269\u60ca\u6050\u9003\u7a9c\u3002" },
            { "Can only use most equipment if it is gigantic.", "\u53ea\u6709\u5de8\u578b\u88c5\u5907\u4f60\u624d\u80fd\u4f7f\u7528\u3002" },
            { "Bilge sphincter acts as a melee weapon.", "\u6c61\u6c34\u62ec\u7ea6\u808c\u53ef\u4f5c\u4e3a\u8fd1\u6218\u6b66\u5668\u3002" },
            { "You belch urchins in a nearby area.", "\u4f60\u671d\u9644\u8fd1\u4e00\u7247\u533a\u57df\u5455\u51fa\u6d77\u80c6\u3002" },
            { "Covers the area in acidic slime.", "\u7528\u9178\u6027\u8f6f\u6ce5\u8986\u76d6\u8be5\u533a\u57df\u3002" },
                        { "Spew chance to knock the targets down: Strength/Agility save vs. character level", "\u51fb\u5012\u76ee\u6807\u7684\u55b7\u5c04\u51e0\u7387\uff1a\u529b\u91cf\uff0f\u654f\u6377 \u5bf9\u6297\u89d2\u8272\u7b49\u7ea7" },
            { "+50 move speed when moving through tiles with 200+ drams of liquid", "\u5728\u542b 200 \u5fb7\u62c9\u59c6\u4ee5\u4e0a\u6db2\u4f53\u7684\u683c\u5b50\u4e0a\u79fb\u52a8\u65f6 +50 \u79fb\u52a8\u901f\u5ea6" },
                                    { "Increased chance when you are injured to ignore all damage for the next 5 rounds [unimplemented]", "\u53d7\u4f24\u65f6\uff0c\u6709\u8f83\u9ad8\u51e0\u7387\u5728\u63a5\u4e0b\u6765 5 \u56de\u5408\u5185\u65e0\u89c6\u4e00\u5207\u4f24\u5bb3\u3014\u672a\u5b9e\u88c5\u3015" },
            { "Small chance when you are injured to ignore all damage for the next 5 rounds [unimplemented]", "\u53d7\u4f24\u65f6\uff0c\u6709\u8f83\u5c0f\u51e0\u7387\u5728\u63a5\u4e0b\u6765 5 \u56de\u5408\u5185\u65e0\u89c6\u4e00\u5207\u4f24\u5bb3\u3014\u672a\u5b9e\u88c5\u3015" },
            { "Small chance that you stubbornly refuse to flee from a fight [unimplemented]", "\u6709\u8f83\u5c0f\u51e0\u7387\u5014\u5f3a\u5730\u62d2\u7edd\u9003\u79bb\u6218\u6597\u3014\u672a\u5b9e\u88c5\u3015" },
            { "Increased chance that you stubbornly refuse to flee from a flight", "\u6709\u8f83\u9ad8\u51e0\u7387\u5014\u5f3a\u5730\u62d2\u7edd\u9003\u79bb\u6218\u6597" },
            { "Increased chance to reveal the entire map in a flash of insight", "\u6709\u8f83\u9ad8\u51e0\u7387\u5728\u7075\u5149\u4e00\u95ea\u4e2d\u63ed\u793a\u6574\u5f20\u5730\u56fe" },
            { "Small chance to reveal the entire map in a flash of insight", "\u6709\u8f83\u5c0f\u51e0\u7387\u5728\u7075\u5149\u4e00\u95ea\u4e2d\u63ed\u793a\u6574\u5f20\u5730\u56fe" },
            { "Increased chance to frighten an adjacent enemy [unimplemented]", "\u6709\u8f83\u9ad8\u51e0\u7387\u5413\u9000\u76f8\u90bb\u7684\u654c\u4eba\u3014\u672a\u5b9e\u88c5\u3015" },
            { "Small chance to frighten an adjacent enemy [unimplemented]", "\u6709\u8f83\u5c0f\u51e0\u7387\u5413\u9000\u76f8\u90bb\u7684\u654c\u4eba\u3014\u672a\u5b9e\u88c5\u3015" },
            { "You can't act while scintillating.", "\u95ea\u70c1\u65f6\u4f60\u65e0\u6cd5\u884c\u52a8\u3002" },
            { "{{rules|Increased bleeding save difficulty and intensity}}", "{{rules|\u63d0\u5347\u6d41\u8840\u8c41\u514d\u96be\u5ea6\u4e0e\u5f3a\u5ea6}}" },
            { "-1d6 Move Speed per turn for 14-18 turns.", "\u6bcf\u56de\u5408 -1d6 \u79fb\u52a8\u901f\u5ea6\uff0c\u6301\u7eed 14-18 \u56de\u5408\u3002" },
                    };

        /// <summary>
        /// 固定原因短语（跨出口共用 · §36「共用纯函数」）。未命中返回 **null**。
        /// 依据：`BaseMutation::CalcLevel` 的 reason 句式（`due to adrenaline` /
        /// `due to a metabolizing effect` / `due to a tonic effect` 等）。
        /// </summary>
        internal static string ReasonPhrase(string mid)
        {
            string zh;
            return REASON.TryGetValue(mid ?? "", out zh) ? zh : null;
        }

        /// <summary>
        /// 把重组结果里的 `因&lt;英文&gt;。` 尾巴换成中文：
        /// ① 固定短语（`REASON`）② 属性名（`ZhsStatPopup.St()`）。
        /// 判据：**只在中段是纯非中文**时才动（已经是中文的原因一律原样保留）✓
        /// </summary>
        private static string FixReason(string zh)
        {
            if (string.IsNullOrEmpty(zh)) return zh;
            int i = zh.LastIndexOf("因", StringComparison.Ordinal);
            if (i < 0) return zh;
            int j = zh.IndexOf('。', i);
            if (j < 0) return zh;
            string mid = zh.Substring(i + 1, j - i - 1).Trim();
            if (mid.Length == 0 || HasCjk(mid)) return zh;

            string zhMid;
            if (!REASON.TryGetValue(mid, out zhMid))
                zhMid = ZhsStatPopup.St(mid);
            if (zhMid == null || zhMid == mid) return zh;      // 表里没有 ⇒ 不动
            return zh.Substring(0, i + 1) + zhMid + zh.Substring(j);
        }

        /// <summary>
        /// `XRL.World.Units.GameObjectSkillUnit::GetDescription` 的两条硬编码句式：
        ///   `Has every &lt;skill&gt; skill`（`Power` 为 `*`/`All`）· `Has the &lt;skill&gt; skill`。
        /// 菜单（蓝图/装备详情）在行首加 `-- `（或 `- `）⇒ 必须容忍并**原样保留**前缀。
        ///
        /// ★ 为什么必须有这一层（2026-10-17 实机 + rulecheck 实证）：
        ///   该类用的是 `value.Name` / `value2.Name`，那是**键**（英文）；
        ///   我方 `Skills.xml` 译的是 `DisplayName`，**这条路径读不到**
        ///   ⇒ 只靠 `REASM` 的 `{1}` 只会得到「精通所有Cudgel技能」（半译）。
        /// ★ 判据：名字查不到 ⇒ 返回 null（**整行保留英文，绝不半译**），
        ///   与 DFCN「未完整翻译就保留整句」一致。
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex SKILLUNIT =
            new System.Text.RegularExpressions.Regex(
                @"^((?:--?\s*)?)Has (every|the) (.+?) skill$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static string SkillUnitZh(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            System.Text.RegularExpressions.Match m;
            try { m = SKILLUNIT.Match(s.Trim()); }
            catch { return null; }
            if (!m.Success) return null;
            string name = m.Groups[3].Value.Trim();
            string zh;
            try
            {
                if (!ZhsSkillReqTable.Map.TryGetValue(name, out zh)
                    || string.IsNullOrEmpty(zh))
                {
                    // ★ 名字**已经是中文**（上游某处先译过）⇒ 直接用，别丢这句
                    if (!HasCjk(name)) return null;
                    zh = name;
                }
            }
            catch { return null; }
            return m.Groups[1].Value
                 + (m.Groups[2].Value == "every" ? "精通所有" : "拥有")
                 + zh + "技能";
        }

        /// <summary>单行跑一遍 `REASM`（**首条命中即返回**）；未命中返回 null。</summary>
        private static string TryReasm(string line)
        {
            string sk = SkillUnitZh(line);
            if (sk != null) return sk;
            foreach (var (rx, tpl) in REASM)
            {
                System.Text.RegularExpressions.Match m;
                try { m = rx.Match(line); }
                catch { continue; }
                if (!m.Success) continue;
                string outZh = tpl;
                for (int g = 1; g < m.Groups.Count; g++)
                {
                    if (!m.Groups[g].Success) continue;
                    outZh = outZh.Replace("{" + (g - 1) + "}", m.Groups[g].Value.Trim());
                }
                return FixReason(outZh);
            }
            return null;
        }

        /// <summary>
        /// 若整行恰好形如 <c>{{X|内容}}</c>（X = 单个字母颜色码）⇒ 返回**内容**并给出外壳；
        /// 否则返回 null。
        /// ★ 用于让「带颜色包裹」与「裸文本」共用同一套 `REASM` 规则。
        /// ★ 用**括号配对**判断（`{{`/`}}` 计数），要求外壳在行尾恰好闭合 ⇒ 不会误剥嵌套标记。
        /// </summary>
        private static string UnwrapLine(string line, out string open, out string close)
        {
            open = null;
            close = null;
            string t = line.Trim();
            if (t.Length < 8 || !t.StartsWith("{{") || !t.EndsWith("}}")) return null;
            int bar = t.IndexOf('|');
            // ★★ 2026-10-15 修正（离线自证抓到）：`{{C|` 里 `|` 在**下标 3**
            //   —— 原判据写成 `bar < 4 ⇒ return null`，把**最常见的单字符色码全拒了**
            //   ⇒ 剥壳从未生效（= 带色包裹的 rankBoost 行照旧匹配不上）✗
            if (bar < 3 || bar > 5) return null;
            string color = t.Substring(2, bar - 2);
            if (color.Length < 1 || color.Length > 3) return null;
            foreach (char cc in color) if (!char.IsLetter(cc)) return null;

            int depth = 0;
            for (int i = 0; i + 1 < t.Length; i++)
            {
                if (t[i] == '{' && t[i + 1] == '{') { depth++; i++; }
                else if (t[i] == '}' && t[i + 1] == '}')
                {
                    depth--;
                    i++;
                    if (depth == 0 && i < t.Length - 1) return null;   // 外壳在行尾之前就闭合
                }
            }
            if (depth != 0) return null;

            open = t.Substring(0, bar + 1);
            close = "}}";
            return t.Substring(bar + 1, t.Length - (bar + 1) - 2);
        }

        /// <summary>`PickOption` 专用：改标题（**第 0 参与第 1 参都试**）**并且**改选项（第 4 参）。
        /// 选项的真实类型是 `IReadOnlyList&lt;string&gt;` / `string[]` ⇒ 只替换元素，
        /// **不重建集合**（避免破坏调用方持有的引用）。
        ///
        /// ★★★ 2026-10-12：**游戏有两种调用约定，标题位置不同**（反编译实证）
        ///   ```
        ///   ① Leveler.RapidAdvancement :
        ///        Popup.PickOption("Choose a physical " + for + " to rapidly advance.", null, "", sound, options)
        ///        ⇒ 标题在 **__args[0]**，__args[1] 是 null
        ///   ② StatusScreen.BuyRandomMutation :
        ///        Popup.PickOption("", "Choose " + Grammar.A(for2) + ".", "", sound, array, …)
        ///        ⇒ 标题在 **__args[1]**，__args[0] 是空串   ← ★ 以前**完全没处理** ✗
        ///   ```
        ///   ⇒ 症状（用户两次截图，正是这两条）：
        ///       `Choose a physical 变异 to rapidly advance.` ⇒ 被通用规则整句吃掉 ⇒「选择a physical 变异…」✗
        ///       `Choose 变异.`                              ⇒ **原样英文** ✗
        ///   ⇒ 两个都处理即可；`null` / 空串自动跳过，**两种约定并存也安全** ✓
        /// </summary>
        private static void PrefixOptions(object[] __args)
        {
            Arg0Prefix(__args);          // 标题（约定 ①：第 0 参）
            try
            {
                if (ZhsPopupText.Disabled || __args == null) return;
                // ★ 约定 ②：标题在**第 1 参**（第 0 参是空串）
                if (__args.Length >= 2)
                {
                    string a0 = __args[0] as string;
                    if (string.IsNullOrEmpty(a0))
                    {
                        string a1 = __args[1] as string;
                        if (!string.IsNullOrEmpty(a1))
                        {
                            string z1 = Arg0Tr(a1);
                            if (!string.IsNullOrEmpty(z1) && z1 != a1)
                            {
                                __args[1] = z1;
                                if (_title1Probe < 5)
                                {
                                    _title1Probe++;
                                    UnityEngine.Debug.Log(Tag + "标题(第1参) #"
                                        + _title1Probe + "：" + a1 + "  =>  " + z1);
                                }
                            }
                        }
                    }
                }
                if (__args.Length < 5) return;
                // ★★ 2.16.32 运行期实证：**改写前**先读一次，判定两个 Prefix 的执行顺序。
                //   若这里已是中文 ⇒ 系统菜单先跑（正常）；仍是英文 ⇒ 顺序反了。
                if (_orderProbe < 3)
                {
                    _orderProbe++;
                    var obs = new System.Text.StringBuilder(Tag + "顺序探针 #" + _orderProbe
                        + "（改写前读 __args[4]）：");
                    try
                    {
                        var tmp = __args[4] as System.Collections.Generic.IList<string>;
                        if (tmp != null)
                            for (int i = 0; i < tmp.Count && i < 4; i++)
                                obs.Append('[').Append(tmp[i]).Append(']');
                        else
                            obs.Append("（非 IList）");
                    }
                    catch { obs.Append("（读取失败）"); }
                    UnityEngine.Debug.Log(obs.ToString());
                }
                RewriteList(__args[4]);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "PrefixOptions 失败：" + e);
            }
        }

        private static int _title1Probe;

        /// <summary>`X at level N` / `X at rank N` —— 效果详情里的「变异 + 等级」拼接段。</summary>
        private static readonly System.Text.RegularExpressions.Regex AT_LEVEL2 =
            new System.Text.RegularExpressions.Regex(
                @"^(.+?) at (?:level|rank) ([0-9IVXivx]+)$");

        /// <summary>
        /// 查**变异名表**（`ZhsMutationZh`）：先按**原名**、再按 **Class**。
        /// 查到返回中文，查不到返回 `null`（调用方据此放行原样）✓
        /// </summary>
        internal static string MutName(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                string zh;
                if (ZhsMutationZh.NameByOrig.TryGetValue(s, out zh)
                    && !string.IsNullOrEmpty(zh)) return zh;
                if (ZhsMutationZh.NameByClass.TryGetValue(s, out zh)
                    && !string.IsNullOrEmpty(zh)) return zh;
            }
            catch { }
            return null;
        }

        /// <summary>串里有没有汉字。</summary>
        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '\u4e00' && s[i] <= '\u9fff') return true;
            return false;
        }

        /// <summary>
        /// 对单个标题串做完整翻译（与 `Arg0Prefix` 同一条链，但**不改 `__args`**）。
        /// 抽出来是为了让「第 1 参标题」复用同一套判据，避免两处逻辑漂移。
        /// </summary>
        private static string Arg0Tr(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string su = ZhsUnits.Fix(s);
                if (su != s) s = su;
                string zh = ZhsPopupText.Tr(s);
                if (ReferenceEquals(s, zh))
                    zh = ZhsCombatMsgPatch.TranslateText(s);
                if (!string.IsNullOrEmpty(zh) && zh != s) return zh;
                string rz = Reassemble(s);
                if (!string.IsNullOrEmpty(rz)) return rz;
            }
            catch (Exception e)
            {
                try { UnityEngine.Debug.LogError(Tag + "Arg0Tr 失败：" + e.Message); } catch { }
            }
            return s;
        }

        /// <summary>
        /// 就地把一个 `IReadOnlyList&lt;string&gt;` 里的文案换成中文。
        ///
        /// ★★ 2.16.4 修正（**上线前查出的真 bug**）：
        ///   `Popup.PickOption` 的第 5 参声明类型是 **`IReadOnlyList&lt;string&gt;`**
        ///   —— **只读接口**。2.16.3 里我写的是
        ///       `string[] arr = __args[4] as string[];`
        ///       `var lst = __args[4] as IList&lt;string&gt;;`   ← `IReadOnlyList` **不是** `IList`
        ///   ⇒ 除恰好传 `ToArray()` 的调用（如 `Bed::AttemptSleep`）外，
        ///     **大部分弹窗的选项都改不到，而且静默放弃**。
        ///   ⇒ 现在按**具体运行时类型**处理：
        ///        ① `string[]`                 ⇒ 直接写下标
        ///        ② `IList&lt;string&gt;`（如 `List&lt;string&gt;`）⇒ 直接写下标
        ///        ③ 其它（真·只读实现）        ⇒ 反射找**有 setter 的索引器**
        ///        ④ 都失败                     ⇒ 记一次日志，不抛
        /// </summary>
        private static void RewriteList(object list)
        {
            if (list == null) return;
            // ★ 首次触发日志（§46 第 ⓪ 道门：只写「已挂」不证明执行过）
            if (_listHits < 3)
            {
                _listHits++;
                UnityEngine.Debug.Log(Tag + "选项列表 #" + _listHits
                    + " 类型 = " + list.GetType().FullName);
            }
            string[] arr = list as string[];
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++) arr[i] = TrOption(arr[i]);
                return;
            }
            System.Collections.Generic.IList<string> il =
                list as System.Collections.Generic.IList<string>;
            if (il != null)
            {
                for (int i = 0; i < il.Count; i++) il[i] = TrOption(il[i]);
                return;
            }
            // ③ 反射：`Item` 索引器 + setter
            Type ty = list.GetType();
            System.Reflection.PropertyInfo ip =
                ty.GetProperty("Item", new Type[] { typeof(int) });
            if (ip == null || !ip.CanWrite)
            {
                if (_listWarned < 3)
                {
                    _listWarned++;
                    UnityEngine.Debug.Log(Tag + "选项列表只读且无 setter（"
                        + ty.FullName + "）⇒ 跳过选项翻译");
                }
                return;
            }
            var cntProp = ty.GetProperty("Count");
            if (cntProp == null) return;
            int n = (int)cntProp.GetValue(list, null);
            for (int i = 0; i < n; i++)
            {
                string cur = ip.GetValue(list, new object[] { i }) as string;
                if (cur == null) continue;
                string zh = TrOption(cur);
                if (!ReferenceEquals(cur, zh) && zh != cur)
                    ip.SetValue(list, zh, new object[] { i });
            }
        }

        private static int _listWarned;
        private static int _listHits;
        private static int _orderProbe;
    }
}
