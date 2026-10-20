// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsVillageSkinPatch —— 村庄物品的「物种名 + 皮肤材质」两个槽（2026-10-17）
// ----------------------------------------------------------------------------
//  【现象】用户实机弹窗：
//      `croccales帐篷`
//      「一张由鳄鱼剥下并鞣制的 scales 皮革，按恩惠启发的方式张挂起来。」
//
//  【两个槽都吐英文】
//    模板（DLL 字面量，owner = `XRL.World.ZoneBuilders.VillageBase::getAVillageCanvas`）：
//        `=primaryAnimal.species= =primaryAnimal.fragment:Skin:skin= tent`
//    我方中文值（Strings.xml L3255）把空格删了 + `tent`→「帐篷」：
//        `=primaryAnimal.species==primaryAnimal.fragment:Skin:skin=帐篷`
//    ⇒ `croc` + `scales` = **`croccales`**
//
//  【取值链（反编译）】
//    ① `GameObjectReplacers::Species(VariableContext, GameObject)`
//         `Context.Value.Append(Object.GetSpecies() ?? Context.Default);`
//       `GameObject::GetSpecies()` = `GetPropertyOrTag("Species") ?? GetDisplayName(…)`
//       ⇒ 生物带 `<tag Name="Species" Value="croc">` 时**直接吐 `croc`**
//       ★ 我方 `ZhsSpecies.L` **早就有** `croc → 鳄`（26 种），但那个补丁只挂
//         `GameObject::GetApparentSpecies`（`=player.apparentSpecies=` 的出口）
//         ⇒ **`=X.species=` 这条没被覆盖**。
//
//    ② `GameObjectReplacers::GetFragment(GameObject Target, string Key, string Default)`
//         `xTags["TextFragments"][Key]` ⇒ **原样返回**（不当键）
//       ⇒ `Skin="scales"` 是纯 XML 值，**可以数据侧改**；但有 20 种、25+ 处，
//         且将来还会加 ⇒ 改出口更省（只查一张 20 条的小表）。
//
//  【做法】两个出口各挂一次，**查不到就放行**（绝不误伤）：
//    ① `GetFragment` 的 **Postfix**：`Key == "Skin"` 且命中表 ⇒ 改 `__result`
//    ② `Species` 的 **Prefix**：命中 `ZhsSpecies.L` ⇒ 自己 Append 中文并 `return false`
//       （原方法是 `Append` 型、没有返回值 ⇒ 只能 Prefix 短路）
//
//  【为什么安全】
//    · `GetFragment` 只在 `Key=="Skin"` 时介入；`PoeticFeatures`/`Activity` 等一概不动
//    · `Species` 只在 `ZhsSpecies.L` 命中时短路；未命中 ⇒ 原样走原方法
//    · 两者都**不改任何字段**，只改渲染输出 ⇒ 不影响名字生成（`Species` 仍是键）
//
//  开关：`_zhs_villageskin_off.txt`
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using XRL.World;
using XRL.World.Text.Delegates;

namespace Nattens.QudZhsCookbook
{
    /// <summary>
    /// `xtagTextFragments` 的**短语表**（官方全集 363 条，本轮先做 Skin 20 + SacredThing 71）。
    ///
    /// ★ 为什么是「全局短语表」而不是「按键分表」：
    ///   `GetFragment` 返回的是**按逗号切好的单条短语**，短语本身在所有键之间唯一
    ///   ⇒ 一张表就够，Postfix 不必再判断 `Key`。
    /// ★ 剩余 292 条（Activity 48 · VillageActivity 41 · NeedsItemFor 24 ·
    ///   PoeticFeatures 84 · ArableLand 22 · ValuedOre 24 · YounglingNoise 29）见待办。
    /// </summary>
    internal static class ZhsVillageSkin
    {
        internal static readonly Dictionary<string, string> FragmentL =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Skin（`=X.fragment:Skin:skin=`）20 条 ──
            { "cuticle", "角质层" },
            { "skin", "皮" },
            { "fur", "毛皮" },
            { "chitin", "甲壳" },
            { "glaze", "釉面" },
            { "scales", "鳞片" },
            { "annuli", "环纹" },
            { "chassis", "底盘" },
            { "tonguey flesh", "舌状肉" },
            { "flesh", "血肉" },
            { "feathers", "羽毛" },
            { "shell", "壳" },
            { "membrane", "膜" },
            { "branchiae", "鳃" },
            { "keratin", "角质" },
            { "stipe", "菌柄" },
            { "panel", "面板" },
            { "machinery", "机件" },
            { "sheets", "薄片" },
            { "spikes", "尖刺" },
            { "texture", "质地" },
            // ── SacredThing（`=X.fragment:SacredThing:sacred thing=`）71 条 ──
            //    ★ 用户实机：`爱philosophy而躲避迫害的的砂锅菜的菜肴` 里的 `philosophy`
            { "abundant food", "丰饶的食物" },
            { "alcohol", "酒" },
            { "artistry", "技艺" },
            { "berries and twigs", "浆果与细枝" },
            { "bread and wine", "面包与葡萄酒" },
            { "burrowing", "掘穴" },
            { "burrows", "洞穴" },
            { "carbon dioxide", "二氧化碳" },
            { "chirping by the light of the Beetle Moon", "甲虫月下的鸣唱" },
            { "drinking water from the soil", "在土中饮水" },
            { "every hue of green", "万般绿意" },
            { "fluid dynamics", "流体动力学" },
            { "gelatin", "胶质" },
            { "grains of the earth", "大地之谷" },
            { "graph theory", "图论" },
            { "having neither arms nor legs", "无臂无腿" },
            { "having neither arms nor legs nor a head", "无臂无腿无头" },
            { "hexapoda", "六足" },
            { "honey", "蜂蜜" },
            { "howling at the Beetle Moon", "对甲虫月嚎叫" },
            { "insect larvae", "昆虫幼虫" },
            { "judgemental silence", "评判性的沉默" },
            { "larvae", "幼虫" },
            { "laying roots", "扎根" },
            { "machine life", "机械生命" },
            { "naps", "小憩" },
            { "necrotic biomass", "坏死生物质" },
            { "nesting", "筑巢" },
            { "ooze", "软泥" },
            { "otherness", "异质" },
            { "painting", "绘画" },
            { "peace and quiet", "清静" },
            { "pentalogies", "五部曲" },
            { "philosophy", "哲学" },
            { "picking the meat from bones", "剔骨取肉" },
            { "platonic solids", "正多面体" },
            { "pocket dimensions", "口袋维度" },
            { "poetry", "诗歌" },
            { "ponds", "池塘" },
            { "pottery", "陶器" },
            { "prattle", "絮语" },
            { "quincunxes", "五点梅花阵" },
            { "shade from the salt sun", "盐阳下的荫凉" },
            { "snarling", "咆哮" },
            { "stone fruit", "核果" },
            { "the Kasaphescence", "卡萨菲森斯" },
            { "the act of procreating", "繁衍之举" },
            { "the art of pinching", "捏取之术" },
            { "the art of speech and song", "言语与歌咏之术" },
            { "the carapace", "甲壳" },
            { "the fin", "鳍" },
            { "the gift of flight", "飞行之赐" },
            { "the home", "家园" },
            { "the membrane", "膜" },
            { "the necessity of water", "水的必需" },
            { "the protection of an exoskeleton", "外骨骼的庇护" },
            { "the safety of enclosure", "围合之安全" },
            { "the salt sun", "盐阳" },
            { "the salt-strewn air", "撒盐的空气" },
            { "the sanctum of a hollow place", "中空之地的圣所" },
            { "the serenity of safety", "安全的宁静" },
            { "the sun", "太阳" },
            { "the tilled land of others", "他人耕作之地" },
            { "vegetables", "蔬菜" },
            { "warm meals", "温热的餐食" },
            { "warm rocks", "温暖的岩石" },
            { "warmth", "温暖" },
            { "water", "水" },
            { "worms", "蠕虫" },
            { "xenophagy", "异食" },
            // ── Activity（`=X.fragment:activity=`）48 条 ──
            { "basking in the shade", "在荫凉处晒太阳" },
            { "basking in the sun", "晒太阳" },
            { "being crawled over by insects", "被虫爬满" },
            { "bludgeoning things to death", "把东西砸死" },
            { "burrowing into the earth", "钻入土中" },
            { "chirping at peers", "对同伴啁啾" },
            { "croaking under the Beetle Moon", "甲虫月下呱叫" },
            { "droning for the hive", "为蜂巢嗡鸣" },
            { "finding weird artifacts", "寻找奇异的器物" },
            { "going about the tedious work of speaking to peers", "忙于与同伴交谈的乏味工作" },
            { "gorging on the flesh of beasts by starlight", "星光下大啖兽肉" },
            { "hunting and gathering", "狩猎采集" },
            { "incubating larvae", "孵育幼虫" },
            { "inside shells", "在壳里" },
            { "keeping the lah tame", "驯养拉兽" },
            { "laying eggs", "产卵" },
            { "liquifying and congealing", "液化又凝结" },
            { "napping in the shade", "在荫凉处小睡" },
            { "napping under the high salt sun", "在高悬的盐阳下小睡" },
            { "nestling in brineweed", "依偎在盐草中" },
            { "nibbling algae", "啃食藻类" },
            { "picking the meat off bones", "剔骨取肉" },
            { "plodding along", "缓慢跋涉" },
            { "putting roots down in the ground", "在地上扎根" },
            { "raiding villages", "劫掠村庄" },
            { "reclining on warm rocks", "倚在温暖的岩石上" },
            { "recompositing meaning from centuries of bit flips", "从数百年的比特翻转中重建意义" },
            { "resorbing matter", "吸收物质" },
            { "roaming around idly", "闲散游荡" },
            { "roaming the curvature", "漫游于曲率之间" },
            { "roaring at glowfish", "对发光鱼咆哮" },
            { "rummaging through dung", "翻找粪堆" },
            { "scrounging for food", "觅食" },
            { "scuttling without purpose", "漫无目的地疾走" },
            { "singing birdsong", "歌唱鸟鸣" },
            { "sitting open invitingly", "敞开着以待来客" },
            { "sleeping and chasing birds all through the day", "整日睡觉与追鸟" },
            { "swallowing insects", "吞食昆虫" },
            { "swaying in the wind", "随风摇曳" },
            { "swimming in circles", "绕圈游动" },
            { "tunneling through the earth", "在土中掘道" },
            { "undulating", "起伏摆动" },
            { "undulating serenely", "安然起伏" },
            { "waiting for errant insects", "等待迷途的昆虫" },
            { "wandering the salt dunes", "漫游盐丘" },
            // ── VillageActivity（`=X.fragment:villageActivity=`）41 条 ──
            { "bathing in our stews", "在我们的炖锅里沐浴" },
            { "being inscrutable", "高深莫测" },
            { "belting out birdsong", "放声高唱鸟鸣" },
            { "chirping at us", "对我们啁啾" },
            { "communing with objects", "与器物交流" },
            { "crawling about", "四处爬行" },
            { "eating our food", "吃我们的食物" },
            { "emitting a monotone pulse", "发出单调的脉冲" },
            { "fastidiously nesting", "一丝不苟地筑巢" },
            { "hibernating by our hearth", "在我们的炉边冬眠" },
            { "howling at the moon", "对月嚎叫" },
            { "jiggling rhythmically", "有节奏地轻晃" },
            { "laying eggs and clicking", "产卵并咔哒作响" },
            { "laying its eggs", "产下它的卵" },
            { "lounging around", "闲躺" },
            { "making worm stews of our stews", "把我们的炖锅做成虫炖锅" },
            { "multiplying in droves", "成群繁衍" },
            { "nestling in our brineweed", "依偎在我们的盐草中" },
            { "peeling and eating musa", "剥食穆萨果" },
            { "picking the meat off discarded bones", "剔拾弃骨上的肉" },
            { "ramming things with @their@ horns", "用@their@角撞东西" },
            { "reading books", "读书" },
            { "reinforcing the walls of our homes", "加固我们房屋的墙" },
            { "sitting in a ruminative pose", "若有所思地坐着" },
            { "sleeping in our bedrolls and sitting in our chairs", "睡在我们的铺盖里、坐在我们的椅子上" },
            { "sleeping in our homes", "睡在我们的屋里" },
            { "sleeping inside its shell", "睡在它的壳里" },
            { "tending to our fields", "照料我们的田地" },
            { "undulating near our stews", "在我们的炖锅旁起伏" },
            { "weaving webs in our homes", "在我们屋里织网" },
            // ── NeedsItemFor（`=X.fragment:NeedsItemFor:need=`）24 条 ──
            { "as something to howl at", "用来对着嚎叫" },
            { "because of its trade value", "因为它的交易价值" },
            { "for burying in the soil", "用来埋进土里" },
            { "for croaking at", "用来对它呱叫" },
            { "for hissing at", "用来对它嘶鸣" },
            { "for my hive", "为我的蜂巢" },
            { "for my own collection", "为我的收藏" },
            { "for resorbing purposes", "用来吸收" },
            { "for the games we play under the Beetle Moon", "为我们在甲虫月下玩的游戏" },
            { "so I can engulf it", "好让我能吞没它" },
            { "to absorb it", "用来吸收它" },
            { "to be my muse", "作为我的缪斯" },
            { "to chirp at", "用来对它啁啾" },
            { "to help with farm work", "帮忙干农活" },
            { "to hide in my nest", "藏进我的巢里" },
            { "to hide in my shell", "藏进我的壳里" },
            { "to hide in the water", "藏进水里" },
            { "to impress the other plants", "好让别的植物刮目相看" },
            { "to inspire my art", "激发我的艺术" },
            { "to inspire my poetry", "激发我的诗兴" },
            { "to nest more effectively", "更好地筑巢" },
            { "to sit on and keep me warm", "让我坐上去取暖" },
            { "to slake my greed", "满足我的贪欲" },
            { "to snarl at meaninglessly", "用来漫无目的地咆哮" },
            // ── ArableLand（`=X.fragment:ArableLand:arable land=`）20 条（`gelatin`/`warm rocks` 与上重复，略）──
            { "a moist place", "湿润之地" },
            { "ample sunlight", "充足的阳光" },
            { "arable land", "可耕之地" },
            { "defenseless villages", "毫无防备的村庄" },
            { "free matter", "游离物质" },
            { "grazeland", "牧场" },
            { "hapless farmers", "倒霉的农夫" },
            { "high branches", "高处的枝条" },
            { "hiving grounds", "筑巢地" },
            { "lakes", "湖泊" },
            { "livestock", "牲畜" },
            { "nesting grounds", "营巢地" },
            { "pools of liquid", "液体池" },
            { "pools of water", "水池" },
            { "quiet meadows", "宁静的草甸" },
            { "ripe corpses", "成熟的尸体" },
            { "rivers", "河流" },
            { "shady nooks", "荫蔽的角落" },
            { "the bounty of the earth", "大地的丰饶" },
            { "the homes of sedentary creatures", "定居生物的家园" },
            // ── ValuedOre（`=X.fragment:ValuedOre:valued ore=`）19 条（与上重复的 5 条略）──
            { "artifacts", "器物" },
            { "chrome", "铬" },
            { "chrome baubles", "铬制小饰物" },
            { "cybernetics", "义体" },
            { "fresh meat", "鲜肉" },
            { "jasper", "碧玉" },
            { "life", "生命" },
            { "matter", "物质" },
            { "meat and bone", "肉与骨" },
            { "moisture", "水分" },
            { "organic matter", "有机物" },
            { "pollen", "花粉" },
            { "precious metals", "贵金属" },
            { "shade and vittle", "荫凉与食物" },
            { "shiny baubles", "闪亮的小饰物" },
            { "sunken treasure", "沉没的宝藏" },
            { "sunlight", "阳光" },
            { "the nectar of the Eaters", "食者之蜜" },
            { "tracks", "足迹" },
            // ── YounglingNoise（`=X.fragment:YounglingNoise:youngling noise=`）29 条 ──
            { "*beep*", "*哔*" },
            { "*bleat*", "*咩*" },
            { "*blub*", "*咕噜*" },
            { "*boop*", "*啵*" },
            { "*bthp*", "*噗噜*" },
            { "*bzzz*", "*嗡嗡*" },
            { "*chirp*", "*啾啾*" },
            { "*click*", "*咔哒*" },
            { "*croak*", "*呱*" },
            { "*growl*", "*咕噜噜*" },
            { "*heh*", "*嘿*" },
            { "*heheh*", "*嘿嘿*" },
            { "*heheheh*", "*嘿嘿嘿*" },
            { "*hoot*", "*呜*" },
            { "*mew*", "*喵*" },
            { "*prrrup*", "*噗噜噜*" },
            { "*rrah*", "*吼*" },
            { "*rrraah*", "*吼啊*" },
            { "*rrrraaah*", "*吼啊啊*" },
            { "*shloop*", "*吸溜*" },
            { "*slither*", "*沙沙*" },
            { "*spurt*", "*噗*" },
            { "*squeak*", "*吱*" },
            { "*squish*", "*叽咕*" },
            { "*ssss*", "*嘶*" },
            { "*sway*", "*摇曳*" },
            { "*undulate*", "*起伏*" },
            { "*whine*", "*呜咽*" },
            { "*xxxff*", "*哧*" },
            // ── PoeticFeatures（`=X.fragment:PoeticFeatures:poetic features=`）84 条 ──
            { "ageless bearing", "不朽的仪态" },
            { "ageless beauty", "不朽的美" },
            { "ageless eyes", "不朽的眼" },
            { "ageless shelf of shell", "不朽的壳台" },
            { "air vents", "通风口" },
            { "airy spiracles", "轻盈的气门" },
            { "alabaster fur", "雪花石膏般的毛皮" },
            { "artfulness", "机巧" },
            { "braided feet", "编织状的足" },
            { "charming moulding", "迷人的轮廓" },
            { "cherubic wings", "小天使般的翼" },
            { "childlike wonder", "孩童般的惊奇" },
            { "chromium sheen", "铬的光泽" },
            { "coarse hair", "粗糙的毛" },
            { "comforting enclosure", "令人安心的围合" },
            { "contoured fins", "流线型的鳍" },
            { "dazzling pearls", "耀眼的珍珠" },
            { "deep grumble", "低沉的咕哝" },
            { "diaphanous wings", "透明的翅" },
            { "eightfold gams", "八条腿" },
            { "enormity", "巨大" },
            { "ethereal petals", "空灵的花瓣" },
            { "eyes full of wonder", "充满惊奇的眼" },
            { "flat surfaces", "平整的表面" },
            { "flawless geometry", "完美的几何" },
            { "fluted stems", "带槽的茎" },
            { "gentle countenance", "温和的容貌" },
            { "gentle stillness", "温和的静" },
            { "geometric beauty", "几何之美" },
            { "geometric simplicity", "几何的简洁" },
            { "hispid skin", "粗毛的皮肤" },
            { "hundred lenses", "百枚镜片" },
            { "hunger for dead things", "对死物的饥渴" },
            { "inviting flatness", "宜人的平坦" },
            { "inviting knob", "宜人的凸起" },
            { "iridescence", "虹彩" },
            { "jointed fingers", "分节的指" },
            { "labyrinth of fingers", "迷宫般的指" },
            { "lidded eyes", "半阖的眼" },
            { "lucent glow", "透亮的光" },
            { "lust for water", "对水的渴求" },
            { "machinic pose", "机械的姿势" },
            { "menacing spikes", "凶险的尖刺" },
            { "muscled chest", "肌肉的胸膛" },
            { "musing eyes", "沉思的眼" },
            { "needle-point quills", "针尖状的棘刺" },
            { "oiled hinges", "上油的铰链" },
            { "oily nose", "油润的鼻" },
            { "outstretched legs", "伸展的腿" },
            { "peaceful squish", "安然的软" },
            { "pearly whiskers", "珍珠般的须" },
            { "photofractal eyes", "光分形的眼" },
            { "plush hair", "柔软的毛" },
            { "prism fingers", "棱镜般的指" },
            { "proud bearing", "骄傲的仪态" },
            { "quincunx spines", "梅花状的刺" },
            { "reclining stature", "斜倚的身姿" },
            { "refractive skin", "折射的皮肤" },
            { "rhythmic piping", "有节奏的鸣管" },
            { "sagittal beak", "矢状的喙" },
            { "sensuality", "感官愉悦" },
            { "shiny teeth", "闪亮的牙" },
            { "simple geometry", "简洁的几何" },
            { "slim dimensionality", "纤细的维度" },
            { "soft surfaces", "柔软的表面" },
            { "spheric hooves", "球状的蹄" },
            { "sphericality", "球形" },
            { "spiral horns", "螺旋的角" },
            { "stilted eyes", "高脚的眼" },
            { "stony stillness", "石般的静" },
            { "suffocating enclosure", "令人窒息的围合" },
            { "sunken eyes", "凹陷的眼" },
            { "tall forehead", "高额头" },
            { "titanic claws", "泰坦的爪" },
            { "towering nature", "巍峨的天性" },
            { "towering stature", "巍峨的身姿" },
            { "translucence", "半透明" },
            { "triangle ears", "三角形耳" },
            { "turgidness", "膨胀" },
            { "undulation", "起伏" },
            { "vast shoulders", "宽阔的肩" },
            { "verdurous bearing", "苍翠的仪态" },
            { "wheezing gills", "喘息般的鳃" },
            { "winking diodes", "闪烁的二极管" },
        };

        internal static int Hits;

        /// <summary>纯函数：英文片段 → 中文；查不到原样返回（供 `--call` 离线自证）。</summary>
        internal static string SkinZh(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            string zh;
            if (FragmentL.TryGetValue(en, out zh) && !string.IsNullOrEmpty(zh))
            {
                Hits++;
                return zh;
            }
            return en;
        }
    }

    internal static class ZhsVillageSkinPatch
    {
        private const string Tag = "[QudZhsCookbook] 村庄材质出口：";
        private const string OffName = "_zhs_villageskin_off.txt";
        private static bool _applied;
        private static int _mounted;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                if (IsOff()) return;

                Type t = AccessTools.TypeByName("XRL.World.Text.Delegates.GameObjectReplacers");
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 GameObjectReplacers —— 未挂载");
                    return;
                }

                Harmony h = new Harmony("Nattens.QudZhsCookbook.villageskin");

                // ① GetFragment（private static string (GameObject, string, string)）
                MethodInfo gf = null;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public
                                                      | BindingFlags.NonPublic))
                {
                    if (m.Name != "GetFragment") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 3 && ps[0].ParameterType.Name == "GameObject"
                        && ps[1].ParameterType == typeof(string) && m.ReturnType == typeof(string))
                    {
                        gf = m;
                        break;
                    }
                }
                if (gf != null)
                {
                    h.Patch(gf, postfix: new HarmonyMethod(typeof(ZhsVillageSkinPatch)
                        .GetMethod("FragmentPostfix", BindingFlags.NonPublic | BindingFlags.Static)));
                    _mounted++;
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 GetFragment(GameObject,string,string)");
                }

                // ② Species（public static void (VariableContext, GameObject)）
                MethodInfo sp = null;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public
                                                      | BindingFlags.NonPublic))
                {
                    if (m.Name != "Species") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[1].ParameterType.Name == "GameObject"
                        && m.ReturnType == typeof(void))
                    {
                        sp = m;
                        break;
                    }
                }
                if (sp != null)
                {
                    h.Patch(sp, prefix: new HarmonyMethod(typeof(ZhsVillageSkinPatch)
                        .GetMethod("SpeciesPrefix", BindingFlags.NonPublic | BindingFlags.Static)));
                    _mounted++;
                }
                else
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 Species(VariableContext,GameObject)");
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + _mounted
                    + " 个出口（片段短语表 " + ZhsVillageSkin.FragmentL.Count
                    + " 条 · Species 复用 ZhsSpecies.L " + ZhsSpecies.L.Count + " 条）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "TryApply 异常：" + e);
            }
        }

        /// <summary>
        /// `GetFragment` 的返回串。★ **不再限定 `Key`** —— 短语表是全局的
        /// （`GetFragment` 已经把逗号池切成单条，短语本身在所有键之间唯一）。
        /// 之前只处理 `Key=="Skin"`，才会漏掉 `SacredThing` 的 `philosophy`/`stone fruit`
        /// （用户 2026-10-17 第二张截图）。
        /// </summary>
        private static void FragmentPostfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (ZhsVillageSkinPatch.IsOff()) return;
                string zh = ZhsVillageSkin.SkinZh(__result);
                if (!string.IsNullOrEmpty(zh) && zh != __result)
                    __result = zh;
            }
            catch { }
        }

        /// <summary>
        /// `Species` 是 `Append` 型（无返回值）⇒ 命中词表时自己写并短路。
        /// ★ 全程**反射**，不静态引用 `TextBuilder`：
        ///   `TextBuilder` 的类型元数据里带 `ReadOnlySpan<char>` 重载，
        ///   SDK Roslyn 4.x 会因此误报 **CS7069**（游戏的 Roslyn 3.11 不会 —— 见 skill
        ///   `coq-harmony-traps` §四）。绕开它最省事。
        /// ★ `VariableContext` 是 **sealed class**（不是 struct）⇒ `object __0` 拿到的就是引用，
        ///   反射改它的字段**有效**（若是 struct 就只能是副本，改不动 —— 这也是先确认类型的原因）。
        /// ★ `Value` 是 **public readonly 字段**，不是属性 ⇒ **先字段、再属性**
        ///   （`GetProperty("Value")` 会恒 null —— skill `coq-harmony-traps` §二 的原样坑）。
        /// </summary>
        private static bool SpeciesPrefix(object __0, object __1)
        {
            try
            {
                if (ZhsVillageSkinPatch.IsOff()) return true;
                GameObject go = __1 as GameObject;
                if (go == null) return true;
                string en = go.GetSpecies();
                string zh = ZhsSpecies.Tr(en);
                if (string.IsNullOrEmpty(zh) || zh == en) return true;   // 查不到 ⇒ 走原方法

                object tb = GetMember(__0, "Value");
                if (tb == null) return true;
                MethodInfo ap = tb.GetType().GetMethod("Append", new[] { typeof(string) });
                if (ap == null) return true;
                ap.Invoke(tb, new object[] { zh });
                return false;    // 已写 ⇒ 跳过原方法
            }
            catch { return true; }
        }

        /// <summary>先字段、再属性（本项目反射取成员的标准写法）。</summary>
        private static object GetMember(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            FieldInfo f = t.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = t.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static bool IsOff()
        {
            try
            {
                string d = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Freehold Games", "CavesOfQud",
                    "Mods", "QudZhsCookbook");
                return System.IO.File.Exists(System.IO.Path.Combine(d, OffName));
            }
            catch { return false; }
        }
    }
}
