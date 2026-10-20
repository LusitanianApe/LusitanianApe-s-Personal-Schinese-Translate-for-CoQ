// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsSkillReqTable —— 技能界面「前置要求」里**技能名**的中英对照表（N6）
//
//  本文件由 `_tools\_gen_skill_req_table.py` **自动生成，请勿手改**。
//
//  【为什么需要它】
//    `XRL.UI.SPNode::ModernUIText`（现代技能界面）里两条分支取法不同：
//        Requires（前置）  → `argument = Entry.Name;`                     ← 英文 ✗
//        Exclusion（排斥） → `argument = Entry2.GetDisplayName();`        ← 中文 ✓
//    ⇒ 官方自己不一致；前置那一支永远出英文
//      （用户截图：`19点 Strength` 后面跟 `Cleave, Charge, Dismember`）。
//    而 `argument` 是**直接赋值**的字符串，随后走
//        Strings._T("Skill UI Prereq Ok", "{{G|=name=}}").SetArgument("name", argument)
//    ⇒ `=name=` 不是 replacer 槽 ⇒ 改模板 / 改串表都够不着 ⇒ 只能挂 C# 出口。
//
//  【键为什么安全】
//    键 = `Skills.xml` 的 `Name` 属性（**是查表键，我们没有译它**），
//    值 = `DisplayName`（我方中文）。本表**只用于显示**，不参与任何查表。
//
//  【自证】
//    生成器对「同 Name 不同 DisplayName」做**硬失败** —— 否则 C# 静态字典
//    会在静态构造里抛 `ArgumentException`，整个 Map 坏掉且异常被 try/catch 吞掉
//    （AGENTS skill `coq-harmony-traps` §三·补1 的原样坑）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsSkillReqTable
    {
        /// <summary>键 = 官方 `Name`（英文，未经我们改动）· 值 = 我方 `DisplayName`。</summary>
        internal static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Acrobatics", "杂技" },   // skill
            { "Akimbo", "双持连射" },   // power
            { "Amputate Limb", "断肢" },   // power
            { "Apothecary", "药剂师" },   // power
            { "Axe", "斧头" },   // skill
            { "Axe Proficiency", "斧头熟练度" },   // power
            { "Backswing", "回旋打击" },   // power
            { "Beacon Fire", "信标之焰" },   // power
            { "Berate", "羞辱" },   // power
            { "Berserk!", "狂暴！" },   // power
            { "Block", "格挡" },   // power
            { "Bloodletter", "血祭者" },   // power
            { "Bludgeon", "钝击" },   // power
            { "Bow and Rifle", "弓箭与步枪" },   // skill
            { "Butchery", "屠宰术" },   // power
            { "Calloused", "茧皮" },   // power
            { "Carbide Chef", "碳化物厨师" },   // power
            { "Charge", "冲锋" },   // power
            { "Charging Strike", "冲锋打击" },   // power
            { "Cleave", "劈砍" },   // power
            { "Conatus", "意志奔腾" },   // power
            { "Conk", "敲晕" },   // power
            { "Cooking and Gathering", "烹饪与采集" },   // skill
            { "Cudgel", "棍棒" },   // skill
            { "Cudgel Proficiency", "棍棒熟练" },   // power
            { "Customs and Folklore", "风俗与传说" },   // skill
            { "Dead Shot", "死神之击" },   // power
            { "Decapitate", "斩首" },   // power
            { "Deft Blocking", "灵巧格挡" },   // power
            { "Deft Throwing", "灵巧投掷" },   // power
            { "Demolish", "摧毁" },   // power
            { "Deploy Turret", "部署炮塔" },   // power
            { "Disarming Shot", "解除武装射击" },   // power
            { "Disassemble", "拆解" },   // power
            { "Dismember", "肢解" },   // power
            { "Disorienting Fire", "迷惑之火" },   // power
            { "Draw a Bead", "瞄准标记" },   // power
            { "Dueling Stance", "决斗姿态" },   // power
            { "Empty the Clips", "清空弹匣" },   // power
            { "En Garde!", "准备姿势！" },   // power
            { "Endurance", "耐力" },   // skill
            { "Fastest Gun in the Rust", "锈蚀之地最快的枪" },   // power
            { "Fasting Way", "禁食之道" },   // power
            { "Flattening Fire", "碾压之火" },   // power
            { "Flurry", "疾风骤雨" },   // power
            { "Gadget Inspector", "装置检查员" },   // power
            { "Harvestry", "收割术" },   // power
            { "Heavy Weapon", "重型武器" },   // skill
            { "Hobble", "蹒跚打击" },   // power
            { "Hook and Drag", "钩拉" },   // power
            { "Hurdle", "跨越障碍" },   // power
            { "Improved Aggressive Stance", "改进攻击姿态" },   // power
            { "Improved Defensive Stance", "强化防御姿态" },   // power
            { "Improved Dueling Stance", "改良决斗姿态" },   // power
            { "Inspiring Presence", "鼓舞人心" },   // power
            { "Intimidate", "恐吓" },   // power
            { "Iron Mind", "铁心" },   // power
            { "Jab", "刺击" },   // power
            { "Juicer", "榨汁者" },   // power
            { "Juke", "闪避" },   // power
            { "Jump", "跳跃" },   // power
            { "Kickback", "反冲踢" },   // power
            { "Lay Mine / Set Bomb", "布设地雷/安置炸弹" },   // power
            { "Lionheart", "狮心" },   // power
            { "Long Blade", "长刃" },   // skill
            { "Long Blade Proficiency", "长刃专精" },   // power
            { "Longstrider", "长行者" },   // power
            { "Lunge", "突刺" },   // power
            { "Make Camp", "扎营" },   // power
            { "Meal Preparation", "餐食准备" },   // power
            { "Meditate", "冥想" },   // power
            { "Menacing Stare", "威吓凝视" },   // power
            { "Mind over Body", "心灵胜过肉体" },   // power
            { "Mind's Compass", "心灵罗盘" },   // power
            { "Multiweapon Expertise", "多武器专精" },   // power
            { "Multiweapon Fighting", "多武器战斗" },   // skill
            { "Multiweapon Mastery", "多武器精通" },   // power
            { "Multiweapon Proficiency", "多武器熟练" },   // power
            { "Nonlinearity", "非线性" },   // skill
            { "Nostrums", "诺斯特拉姆" },   // power
            { "Opportune Attacks", "适时攻击" },   // power
            { "Penetrating Strikes", "穿透打击" },   // power
            { "Persuasion", "劝服" },   // skill
            { "Physic", "治疗术" },   // skill
            { "Pistol", "手枪" },   // skill
            { "Pointed Circle", "尖锐圆环" },   // power
            { "Poison Tolerance", "毒抗" },   // power
            { "Proselytize", "劝诱" },   // power
            { "Rejoinder", "反击" },   // power
            { "Repair", "修复" },   // power
            { "Reverse Engineer", "逆向工程" },   // power
            { "Scavenger", "拾荒者" },   // power
            { "Self-discipline", "自律" },   // skill
            { "Shake It Off", "摆脱影响" },   // power
            { "Shank", "刺击" },   // power
            { "Shield", "盾牌" },   // skill
            { "Shield Slam", "盾击" },   // power
            { "Shield Wall", "护盾壁垒" },   // power
            { "Short Blade", "短刃" },   // skill
            { "Short Blade Expertise", "短刃专精" },   // power
            { "Single Weapon Fighting", "单手武器格斗" },   // skill
            { "Slam", "猛击" },   // power
            { "Sling and Run", "边跑边射" },   // power
            { "Snake Oiler", "蛇油贩子" },   // power
            { "Spicer", "调味大师" },   // power
            { "Spry", "敏捷" },   // power
            { "Staggering Block", "震慑格挡" },   // power
            { "Staunch Wounds", "止血" },   // power
            { "Steady Hand", "稳手" },   // power
            { "Steady Hands", "稳定之手" },   // power
            { "Strapping Shoulders", "强健肩膀" },   // power
            { "Suppressive Fire", "压制射击" },   // power
            { "Sure Fire", "精准射击" },   // power
            { "Sweep", "扫射" },   // power
            { "Swift Blocking", "迅捷格挡" },   // power
            { "Swift Reflexes", "迅捷反应" },   // power
            { "Swimming", "游泳" },   // power
            { "Swipe", "挥击" },   // power
            { "Tactful", "审慎" },   // power
            { "Tactics", "战术" },   // skill
            { "Tank", "重装战士" },   // power
            { "Tinker I", "修补 I" },   // power
            { "Tinker II", "修补 II" },   // power
            { "Tinker III", "修补匠 III" },   // power
            { "Tinkering", "修补" },   // skill
            { "Tomorrowful", "明日预见" },   // power
            { "Trash Divining", "垃圾占卜" },   // power
            { "Tumble", "翻滚" },   // power
            { "Ultra Fire", "超能火焰" },   // power
            { "Wayfaring", "流浪之道" },   // skill
            { "Weak Spotter", "弱点瞄准者" },   // power
            { "Weapon Expertise", "武器专精" },   // power
            { "Weapon Mastery", "武器精通" },   // power
            { "Weathered", "风霜" },   // power
            { "Wilderness Lore: Canyons", "荒野知识： 峡谷" },   // power
            { "Wilderness Lore: Flower Fields", "荒野知识： 花田" },   // power
            { "Wilderness Lore: Hills and Mountains", "荒野学识： 丘陵与山脉" },   // power
            { "Wilderness Lore: Jungles", "荒野知识： 丛林" },   // power
            { "Wilderness Lore: Marshes", "荒野知识： 盐沼与水藤原" },   // power
            { "Wilderness Lore: Rivers and Lakes", "荒野知识： 河流与湖泊" },   // power
            { "Wilderness Lore: Ruins", "荒野知识： 废墟" },   // power
            { "Wilderness Lore: Salt Dunes", "荒野知识： 盐丘" },   // power
            { "Wounding Fire", "创伤之火" },   // power
        };

        /// <summary>
        /// 只认 `{{G|X}}` / `{{G|X}}` / `{{R|X}}` / `{{r|X}}` 这四种**颜色标记内**的整段，
        /// 且要求整段**完全等于**表里的键。
        /// ★ 为什么不让它替换裸英文：技能名里有 `Jump` / `Charge` / `Cleave` 这种常用词，
        ///   裸替换会改坏别的句子（§44「短词抢先」同族）。
        /// ★ 幂等：已中文的片段查不到键 ⇒ 原样返回。
        /// </summary>
        private static readonly Regex Marked =
            new Regex(@"\{\{([GgRr])\|([^{}|]+)\}\}", RegexOptions.Compiled);

        internal static int Hits;
        internal static int Calls;

        internal static string Apply(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (Map.Count == 0) return s;
            Calls++;
            try
            {
                string r = Marked.Replace(s, m =>
                {
                    string key = m.Groups[2].Value;
                    string zh;
                    if (Map.TryGetValue(key, out zh) && !string.IsNullOrEmpty(zh))
                    {
                        Hits++;
                        return "{{" + m.Groups[1].Value + "|" + zh + "}}";
                    }
                    return m.Value;
                });
                return r;
            }
            catch
            {
                return s;
            }
        }
    }
}
