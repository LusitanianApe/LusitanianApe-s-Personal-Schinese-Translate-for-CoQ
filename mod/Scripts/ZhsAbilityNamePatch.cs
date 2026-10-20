// ─────────────────────────────────────────────────────────────────────────
//  ZhsAbilityNamePatch —— 接管底边栏（技能栏）的能力名
//
//  背景（IL 实证）：
//    `XRL.World.Parts.Skill.*::AddSkill` 里调用
//        IComponent<GameObject>._S("<技能类名> ActivatedAbility Name", "<英文名>")
//    取能力显示名，再交给
//        IComponent<GameObject>.AddMyActivatedAbility(Name, Command, Class, …)
//    → `ActivatedAbilities.AddAbility`：
//        activatedAbilityEntry.DisplayName = Name;      // Name 是纯显示名
//
//  问题：
//    我们已在 Strings.xml 里放了 23 条
//        <string Context="Camp ActivatedAbility Name" ID="Make Camp">扎营</string>
//    与官方示例一一对应（差集 0），但底边栏仍显示英文 ——
//    说明 `_S()` 的查找有额外限定，纯 XML 走不通。
//
//  修法：
//    直接 Prefix 掉 `AddAbility`，把 `Name` 形参换成中文。
//    `Name` 只用于显示（`DisplayName = Name`），不影响 Command / Class 的逻辑键，
//    所以替换是安全的。
// ─────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsAbilityNamePatch
    {
        private const string Tag = "[QudZhsCookbook] ";
        private static bool _applied;

        /// <summary>英文能力名 → 中文。键取自游戏源码里的 _S(ID) 字面量。</summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                                // ── 字面量注册的能力名（不经字符串表，必须在此列出）──
                //    证据：RecoilAbility::Initialize
                //          AddMyActivatedAbility("Recoil", COMMAND_NAME, "Items", …)
                { "Recoil", "传送回城" },
                // ★ 2026-10-18（#61）：`Fly` / `Eject` / `Quill Fling`（飞行/载具/棘刺）
                { "Fly", "飞行" },
                { "Eject", "弹射" },
                { "Quill Fling", "掷刺" },
                { "Make Camp", "扎营" },
                { "Aggressive Stance", "进攻姿态" },
                { "Defensive Stance", "防御姿态" },
                { "Dueling Stance", "决斗姿态" },
                { "Shield Slam", "盾击" },
                { "Rebuke Robot", "斥退机器人" },
                { "Mark Target", "标记目标" },
                { "Charge", "冲锋" },
                { "Sprint", "疾行" },
                { "Dismember", "截肢" },
                { "Decapitate", "斩首" },
                { "Flurry", "疾攻" },
                { "Change Grip", "变更握法" },
                { "Confusion", "混乱" },
                { "Chill", "骤冷" },
                { "Toast", "炙烤" },
                { "Kindle", "引燃" },
                { "Bask", "曝晒" },
                { "Metamorphosis", "变形" },
                { "Time Dilation", "时间膨胀" },
                { "Disintegration", "解体" },
                { "Teleport", "传送" },
                { "Teleport Other", "传送他人" },
                { "Force Bubble", "力场泡" },
                { "Force Wall", "力场墙" },
                { "Stunning Force", "震慑之力" },
                { "Syphon Vim", "汲取活力" },
                { "Butcher Corpses", "屠宰尸体" },
                { "Akimbo", "双持" },
                { "Rejoinder", "反击" },
                { "Menacing Stare", "威吓凝视" },
                { "Magnetic Pulse", "磁脉冲" },
                { "Icy Vapor", "寒霜之息" },
                { "Freezing Ray", "冰冻射线" },
                { "Flaming Ray", "烈焰射线" },
                { "Freeze Breath", "冰息" },
                { "Night Vision", "夜视" },

                // ── ★★ 2.16.94 补：**长刃系全族 + 一批近战技能** ────────────────
                //   【为什么之前漏】本表靠**逐个发现**累积，长刃这三个从没被碰到。
                //   【实证】用户截图（同伴「人类魔像」的能力开关弹窗）：
                //       `[a] En Garde! [allowed]` / `[b] Lunge [allowed]` / `[d] Swipe [allowed]`
                //       而相邻的 `[e] 决斗姿态` `[f] 灵测` `[g] 疾行` 已是中文
                //     ⇒ 证明补丁**在跑**（日志 L181「已挂 … 38 条映射」），
                //       只是这三个名字不在表里。
                //   【注册形态（decomp member 实证）】
                //       LongBladesLunge::AddSkill     AddMyActivatedAbility("Lunge", "CommandLunge", "Skills", …)
                //       LongBladesSwipe::AddSkill     AddMyActivatedAbility("Swipe", "CommandSwipe", "Skills", …)
                //       LongBladesDeathblow::AddSkill AddMyActivatedAbility("En Garde!", "CommandDeathblow", "Skills", …)
                //   【译名来源】★ **不是我另起译名** —— 直接取我方 `Skills.xml` 的既有
                //       `DisplayName`（用户早已见过的技能树译名）：
                //           <power Name="Lunge"     … DisplayName="突刺"/>
                //           <power Name="Swipe"     … DisplayName="挥击"/>
                //           <power Name="En Garde!" … DisplayName="准备姿势！"/>
                //     ⇒ 底边栏与技能树从此**同词同译**（§10 原则①「同一英文词→同一中文译名」）。
                { "Lunge", "突刺" },
                { "Swipe", "挥击" },
                { "En Garde!", "准备姿势！" },

                // ── ★★ 2.16.94 补：其余**已能在 `Skills.xml` 找到权威译名**的技能 ──
                //   判据：`AddMyActivatedAbility("<Name>", …)` 的首参正是该 `<power Name="…">`。
                //   ⇒ 一次性补齐，避免同类问题再被逐个发现。
                //   ⚠ `Dueling Stance` **本表开头已有**，不要重复添加 ——
                //     集合初始化器的重复键会让 `.cctor` 抛 `ArgumentException`
                //     ⇒ **整个类失效、补丁静默不生效**。
                //     （本版实际踩到并被 `validate_mod.py` 的「C# 字典重复键」检查抓到）
                { "Berserk!", "狂暴！" },                  // Axe_Berserk：("Berserk!","CommandAxeBerserk","Stances")
                { "Conk", "敲晕" },                        // Cudgel_Conk: ("Conk", COMMAND_NAME, "Skills")
                { "Slam", "猛击" },                        // Cudgel_Slam
                { "Demolish", "摧毁" },                    // Cudgel_Demolish
                { "Hobble", "蹒跚打击" },                   // ShortBlades_Hobble
                { "Shank", "刺击" },                       // ShortBlades_Shank
                { "Berate", "羞辱" },                      // Persuasion_Berate
                { "Intimidate", "恐吓" },                   // Persuasion_Intimidate
                { "Juke", "闪避" },                        // Acrobatics_Juke

                // ── ★★ 2026-10-17 补：**字面量能力名全量收口（28 条）** ─────────────
                //   【起因】用户截图 `你获得了主动能力 Tongue。`
                //   【取证】`StickyTongue::Mutate` 调
                //       AddMyActivatedAbility("Tongue", "CommandStickyTongue", …)
                //     `ActivatedAbilities::AddAbility` 里 `activatedAbilityEntry.DisplayName = Name;`
                //     ⇒ 名字是**调用方传的字面量**，数据侧（ActivatedAbilities.xml 只有
                //       `<description>`）**改不到** ⇒ 只有本表能兜。
                //   【为什么以前没发现】本表一直是**逐个发现**累积的。
                //     本次用脚本一次算差集：`_tools\_abilityname_census.py`
                //        Add*ActivatedAbility 的**字面量首参** 47 个 − 本表 50 键
                //        ⇒ **缺 28 个**（就是下面这批）
                //   【译名来源】★ 不另起译名（§10 原则①）——优先取
                //       (a) 我方 `HiddenMutations.xml` / `Mutations.xml` 的 `DisplayName`
                //           （该变异能力的名字，玩家在变异界面见过）
                //       (b) 我方 `Skills.xml` 的 `<power DisplayName>`
                //     工具：`_tools\_authority_names.py`
                { "Tongue", "黏舌" },                      // StickyTongue::Mutate ←「Sticky Tongue」变异名
                { "Burrow", "掘地" },                      // Burrowing::Mutate ←「Burrowing」变异名
                { "Excavate down", "向下掘进" },            // BurrowingClaws::Mutate
                { "Excavate up", "向上掘进" },              // BurrowingClaws::Mutate
                { "Spit Acid", "吐酸" },                    // AcidSlimeGlands::Mutate ←「Acid Slime Glands」酸液腺
                { "Spit Liquid", "喷吐液体" },              // LiquidSpitter::Mutate ←「Liquid Spitter」喷液者
                { "Spew", "呕出" },                        // SlogGlands::Mutate
                { "Spin Webs", "结网" },                    // SpiderWebs::Mutate ←「Spider Webs」蛛网
                { "Knit Frosty Webs", "编织霜网" },          // FrostWebs::Mutate ←「Frost Webs」霜网
                { "Scintillate", "闪烁" },                  // DefensiveChromatophores::Mutate
                { "Ley Shift", "灵脉移转" },                // LeyShifting::Mutate ← 同名变异
                { "Waveform Dash", "波形冲刺" },            // WaveformWorm::Mutate
                { "Wrecking Charge", "破城冲锋" },          // MultiHorns::Mutate
                { "Run Over", "碾压" },                     // RunOver::Initialize
                { "Engulf", "吞噬" },                       // Engulfing::Initialize
                { "Decarbonize", "脱碳" },                  // Decarbonizer::Mutate ←「Decarbonizer」脱碳者
                { "Clone", "克隆" },                        // Cloneling::Initialize
                { "Serenity", "宁神" },                     // Serenity::Mutate
                { "Fear Aura", "恐惧灵光" },                // FearAura::Mutate ← 同名变异
                { "Irisdual Beam", "虹双光束" },            // IrisdualBeam::Mutate ← 同名变异
                { "Freezing Ultraray", "冰冻超射线" },       // FreezeBreath::AddAbility
                { "Boost Strength", "强化力量" },            // WillForce::Mutate
                { "Boost Agility", "强化敏捷" },            // WillForce::Mutate
                { "Boost Toughness", "强化韧性" },           // WillForce::Mutate
                { "Death From Above", "从天而降" },          // Tactics_DeathFromAbove::AddSkill
                { "Deploy Turret", "部署炮塔" },            // Tinkering_DeployTurret::AddSkill ← Skills.xml
                { "Lay Mine", "布设地雷" },                 // Tinkering_LayMine::AddSkill
                { "Set Bomb", "布设炸弹" },                 // Tinkering_LayMine::AddSkill

                // ── ★★ 2026-10-17 补（测试窗口 #60）：载具「离开驾驶座」──────
                //   【取证】反编译 `XRL.World.Parts.Vehicle::CheckAbility`：
                //       `ParentObject.AddActivatedAbility("Exit pilot seat",
                //            "CommandReleaseVehicle", "Vehicle", null, "÷")`
                //     ⇒ 走 `GameObject.AddActivatedAbility` → `ActivatedAbilities.AddAbility`
                //       ⇒ **本 Prefix 够得到**（`ZhsAbilityNamePatch.Prefix` 换第 0 参）。
                //   【为什么 census 没报】`_abilityname_census.py` 只收
                //     `Add*ActivatedAbility` 的**字面量首参**，而这条的字面量落在
                //     `GameObject::AddActivatedAbility` 的调用点上（census 的 owner 过滤没收）
                //     ⇒ 「census 0 缺口」≠「全项目 0 缺口」（QudJP §① 同一判据）。
                { "Exit pilot seat", "离开驾驶座" },
            };


        /// <summary>预设名 → 中文。Id（键）保持英文，只换 Title。</summary>
        private static readonly Dictionary<string, string> PregenNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Praetorian Prime", "首席禁卫" },
                { "First Gardener", "初代园丁" },
                { "First Child of the Hearth", "初代炉灶之子" },
                { "Marsh Taur", "沼泽牛头人" },
                { "Dream Tortoise", "梦龟" },
                { "Gunwing", "枪翼" },
                { "Star-Eye Esper", "星眼灵能者" },
                { "Firefrond", "火叶" },
                { "bzzzt", "嗡嗡" },
            };

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = FindType("XRL.World.Parts.ActivatedAbilities");
            if (t == null) { Debug.LogError(Tag + "找不到 ActivatedAbilities"); return; }

            MethodInfo target = null;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                                                   | BindingFlags.NonPublic
                                                   | BindingFlags.Instance))
            {
                if (mi.Name != "AddAbility") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length < 2) continue;
                if (ps[0].ParameterType != typeof(string)) continue;
                target = mi;
                break;
            }
            if (target == null)
            {
                Debug.LogError(Tag + "找不到 ActivatedAbilities.AddAbility");
                return;
            }

            try
            {
                MethodInfo pre = typeof(ZhsAbilityNamePatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.abilityname");
                h.Patch(target, prefix: new HarmonyMethod(pre));
                Debug.Log(Tag + "能力名接管：已挂 " + target.DeclaringType.Name + "."
                          + target.Name + "（" + Names.Count + " 条映射）");

            // ★ 角色创建预设名：GetSelections 是 Iterator，
            //   真正执行体在编译器生成的嵌套类 MoveNext 里，所以挂 MoveNext。
            PatchPregenTitles();
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "挂 AddAbility 失败：" + e.Message);
            }
        }

        /// <summary>把 Name 形参换中文；未命中则原样放行。</summary>
        private static void Prefix(object[] __args)
        {
            if (__args == null || __args.Length == 0) return;
            string name = __args[0] as string;
            if (string.IsNullOrEmpty(name)) return;

            string zh;
            if (Names.TryGetValue(name, out zh))
            {
                __args[0] = zh;
                return;
            }
            // 兜底：去掉尾部换行/空白后再试（有的调用点传 "Make Camp\n"）
            string trimmed = name.Trim();
            if (trimmed != name && Names.TryGetValue(trimmed, out zh))
            {
                __args[0] = zh;
                return;
            }

            // ★ 2026-10-18（#61）：`Recoil to <地点>` 是**运行时拼接**
            //   （`CyberneticsOnboardRecoilerImprinting::UpdateName` / `TetheredOnboardRecoilerTeleporter::CheckAbility`）
            //   ⇒ 字典查不到，按前缀处理；地点名再走小表。
            if (trimmed.StartsWith("Recoil to ", StringComparison.Ordinal))
            {
                string loc = trimmed.Substring("Recoil to ".Length).Trim();
                __args[0] = "传送回" + RecoilPlace(loc);
            }
        }

        /// <summary>`Recoil to` 的地点名（未登记的英文地点原样保留，不半译）。</summary>
        private static string RecoilPlace(string loc)
        {
            switch (loc)
            {
                case "Court of the Sultans": return "苏丹宫廷";
                case "Omonporch": return "奥蒙波奇";
                case "Grit Gate": return "砂砾门";
                case "Joppa": return "约帕";
                case "Six Day Stilt": return "六日高跷";
                default: return loc;
            }
        }


        /// <summary>
        /// 挂 `QudPregenModule.GetSelections()` 返回的迭代器。
        /// 该方法是 Iterator，真正执行体在编译器生成的嵌套类的 MoveNext 里；
        /// 直接 Postfix 外层方法拿不到元素，所以挂 MoveNext 并在返回 true 时改 state。
        /// </summary>
        private static void PatchPregenTitles()
        {
            // ★ GetSelections 属于 **窗口类**（`XRL.CharacterBuilds.Qud.UI.QudPregenModuleWindow`），
            //   不属于模块类 —— 挂错类会得到「找不到 GetSelections」。
            Type modType = FindType(
                "XRL.CharacterBuilds.Qud.UI.QudPregenModuleWindow");
            if (modType == null)
            {
                Debug.LogError(Tag + "找不到 QudPregenModuleWindow");
                return;
            }

            MethodInfo outer = null;
            foreach (MethodInfo mi in modType.GetMethods(BindingFlags.Public
                                                         | BindingFlags.NonPublic
                                                         | BindingFlags.Instance))
            {
                if (mi.Name == "GetSelections") { outer = mi; break; }
            }
            if (outer == null) { Debug.LogError(Tag + "找不到 GetSelections"); return; }

            // 找编译器生成的迭代器类型
            Type iter = null;
            foreach (Type nt in modType.GetNestedTypes(BindingFlags.Public
                                                       | BindingFlags.NonPublic))
            {
                if (nt.Name.Contains("GetSelections") && nt.Name.Contains("d__"))
                {
                    iter = nt;
                    break;
                }
            }
            if (iter == null)
            {
                Debug.LogError(Tag + "找不到 GetSelections 的迭代器类型");
                return;
            }

            MethodInfo moveNext = iter.GetMethod("MoveNext",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (moveNext == null)
            {
                Debug.LogError(Tag + "找不到 MoveNext");
                return;
            }

            try
            {
                MethodInfo post = typeof(ZhsAbilityNamePatch).GetMethod(
                    "PregenMoveNextPostfix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.pregenname");
                h.Patch(moveNext, postfix: new HarmonyMethod(post));
                Debug.Log(Tag + "预设名接管：已挂 " + iter.Name + ".MoveNext（"
                          + PregenNames.Count + " 条映射）");
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "挂 MoveNext 失败：" + e.Message);
            }
        }

        /// <summary>MoveNext 返回 true 后，state.&lt;&gt;2__current 是本次产出的选项；换其 Title。</summary>
        private static void PregenMoveNextPostfix(object __instance, bool __result)
        {
            if (!__result || __instance == null) return;
            try
            {
                FieldInfo f = __instance.GetType().GetField("<>2__current",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object cur = f?.GetValue(__instance);
                if (cur == null) return;

                PropertyInfo idP = cur.GetType().GetProperty("Id");
                PropertyInfo titleP = cur.GetType().GetProperty("Title");
                if (idP == null || titleP == null) return;

                string id = idP.GetValue(cur, null) as string;
                string zh;
                if (id != null && PregenNames.TryGetValue(id, out zh))
                {
                    titleP.SetValue(cur, zh, null);
                }
            }
            catch { }
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
    }
}
