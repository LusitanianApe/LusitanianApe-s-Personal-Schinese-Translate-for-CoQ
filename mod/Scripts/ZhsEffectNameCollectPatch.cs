// ═══════════════════════════════════════════════════════════════════════════
// 效果名采集 + 汉化扩展 —— `Effect.DisplayName` 是**构造函数里硬编码**的
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图「查看效果」面板】
//     swimming                      ← 效果名（英文）
//     Moving at full speed.         ← 状态描述（英文）
//     湿的                           ← 这条是**中文**（来自字符串表）
//     Covered in 28 drams of 咸的 水.  ← ★ 英文骨架 + 中文词
//
// 【反编译实证】效果名**不走字符串表**，是构造函数里直接赋的：
//     // XRL.World.Effects.LiquidCovered::.ctor
//     public LiquidCovered() {
//         DisplayName = "covered in liquid";     // ★★ 硬编码
//         Duration = 1;
//     }
//   ⇒ 任何 XML 都改不到（我方既有 `ZhsEffectNamePatch` 只覆盖长刃 6 个字面量）。
//   同理 `GetStateDescription()` 也是硬编码返回（如 `Moving at full speed.`）。
//
// 【本补丁做两件事】
//   ① **采集**：Postfix 挂 `XRL.World.Effect` 的构造函数，把开局的
//      `类型名 → DisplayName` 全部记进 `<模组目录>\ZhsEffectNames.log`
//      （去重、上限 1500 行）⇒ **拿到全量真实清单**，不必再靠串堆猜。
//   ② **汉化**：查表替换 `DisplayName`（键 = 原文，**精确匹配**，绝不误伤）；
//      未命中的原样保留，等采集结果回来后补译。
//
// 【安全】`_zhs_effectname_off.txt` 可停用；异常一律放行原值。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsEffectNameCollect
    {
        public static bool Disabled;
        private static string _log;
        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static int _n;
        private const int Max = 1500;
        internal static int Fixed;

        /// <summary>硬编码效果名 → 中文（键为**原文**，精确匹配）。</summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 长刃架势（原 `ZhsEffectNamePatch` 的 6 条，统一收到这里）──
            { "{{G|defensive stance}}",     "{{G|防御架势}}" },
            { "{{G|aggressive stance}}",    "{{G|进攻架势}}" },
            { "{{G|dueling stance}}",       "{{G|决斗架势}}" },
            { "{{G|in defensive stance}}",  "{{G|处于防御架势}}" },
            { "{{G|in aggressive stance}}", "{{G|处于进攻架势}}" },
            { "{{G|in dueling stance}}",    "{{G|处于决斗架势}}" },

            // ══════════════════════════════════════════════════════════════
            // ★★ 2.16.65：**效果名整批补完**（用户截图 `sitting` 仍是英文）
            //
            // 【盲区】效果名**不走字符串表**，是**构造函数里直接赋 `DisplayName`**
            //   ⇒ 任何 XML 都改不到，只能挂构造函数替换。
            //   本表此前**只有 8 条**（液体 2 + 长刃 6）⇒ 其余全英文。
            //
            // 【范围（机械枚举，不靠猜）】
            //   工具 `_tools\_census_effect_names.py`：
            //     从 `GetDetails` 覆盖（142）+ `DisplayName = "` 赋值点（100）
            //     合并 152 个类型，扫**全部构造函数**的 IL/反编译体
            //     ⇒ 去重 **103 条**效果名字面量（产出 `_tools\_effect_displaynames.txt`）
            //   ⚠ 其中若干条是**前缀**（如 `{{C|lying on `、`{{B|engulfed by `），
            //     代码会再拼上对象名 ⇒ 译文只译到标记内文本、**保留标记与前缀形态**。
            // ══════════════════════════════════════════════════════════════
            // —— 无标记 ——
            // ★★ 2026-10-17 补：**色标被剥掉后的同名形态**
            //   【起因】用户截图：查看面板里状态名显示成**光秃秃的 `sitting`**
            //     而本表当时只有带色标的 `{{C|sitting}}` ⇒ 精确匹配落空 ⇒ 留英文。
            //   【取证】`XRL.World.Effects.Sitting::.ctor` 赋的确实是 `"{{C|sitting}}"`
            //     （`Swimming::.ctor` 同样是 `"{{B|swimming}}"`），
            //     但**查看/检查面板会把色标剥掉后再渲染** ⇒ 命中不到。
            //   【做法】给最高频的状态名**成对登记**（带色标 + 无标记），
            //     精确匹配 ⇒ 不会误伤；两条都是白名单内的原文，安全。
            //   ⚠ 与上面「带色标」那组是**不同的键**，不构成重复键。
            { "sitting", "坐着" },
            { "swimming", "游泳" },
            { "wading", "涉水" },
            { "submerged", "淹没" },
            { "prone", "倒地" },
            { "stunned", "眩晕" },
            { "paralyzed", "麻痹" },
            { "hobbled", "跛行" },
            { "piloting", "驾驶中" },
            { "warming up", "预热中" },
            { "camouflaged", "伪装" },
            { "corrected vision", "视力矫正" },
            { "covered in liquid", "被液体覆盖" },
            { "cracked", "破裂" },
            { "demolishing", "正在摧毁" },
            { "flying", "飞行" },
            { "greased", "涂满油脂" },
            { "healing", "治疗中" },
            { "hemorrhaging", "大出血" },
            { "hooked", "被钩住" },
            { "immobilized", "被定身" },
            { "incommunicado", "无法通讯" },
            { "lost", "迷失" },
            { "meditating", "冥想中" },
            { "shaken", "动摇" },
            { "shield wall", "盾墙" },
            { "springing", "蓄势待发" },
            { "sprinting", "疾奔" },
            { "stained by liquid", "被液体染色" },
            { "suppressed", "被压制" },
            { "synapse snap", "突触崩断" },
            { "syphoned", "被汲取" },
            { "time cubed", "时间立方" },
            { "time-dilated", "时间膨胀" },
            { "trance", "出神" },
            { "waking dream", "醒着的梦" },
            { "&Wwell fed (+", "&W饱食（+" },
            { "&minhabited", "&m被寄生" },
            // —— 带色标 ——
            { "{{B|engulfed by ", "{{B|被吞没于 " },
            { "{{B|frenzied}}", "{{B|狂乱}}" },
            { "{{B|mobility impaired}}", "{{B|行动不便}}" },
            { "{{B|stressed}}", "{{B|超载}}" },
            { "{{B|submerged}}", "{{B|淹没}}" },
            { "{{B|swimming}}", "{{B|游泳}}" },
            { "{{B|wading}}", "{{B|涉水}}" },
            { "{{C|enclosed in ", "{{C|被封闭于 " },
            { "{{C|hobbled}}", "{{C|跛行}}" },
            { "{{C|interdicted}}", "{{C|被禁制}}" },
            { "{{C|lying on ", "{{C|躺在 " },
            { "{{C|paralyzed}}", "{{C|麻痹}}" },
            { "{{C|piloting ", "{{C|驾驶 " },
            { "{{C|piloting}}", "{{C|驾驶中}}" },
            { "{{C|prone}}", "{{C|倒地}}" },
            { "{{C|sitting on ", "{{C|坐在 " },
            { "{{C|sitting}}", "{{C|坐着}}" },
            { "{{C|stunned}}", "{{C|眩晕}}" },
            { "{{C|unpiloted}}", "{{C|无人驾驶}}" },
            { "{{C|warming up}}", "{{C|预热中}}" },
            { "{{G|En garde!}}", "{{G|架势已起！}}" },
            { "{{G|poisoned by gas}}", "{{G|气体中毒}}" },
            { "{{G|poisoned}}", "{{G|中毒}}" },
            { "{{G|refreshed}}", "{{G|神清气爽}}" },
            { "{{K|gleaming}}", "{{K|闪光}}" },
            { "{{K|overburdened}}", "{{K|负重过度}}" },
            { "{{K|unpowered}}", "{{K|无动力}}" },
            { "{{M|mutating}}", "{{M|突变中}}" },
            { "{{M|stasis}}", "{{M|停滞}}" },
            { "{{O|invulnerable}}", "{{O|无敌}}" },
            { "{{O|shimmering}}", "{{O|闪耀}}" },
            { "{{R|FURIOUS}}", "{{R|暴怒}}" },
            { "{{R|confused}}", "{{R|困惑}}" },
            { "{{R|crippled}}", "{{R|残废}}" },
            { "{{R|enraged}}", "{{R|激怒}}" },
            { "{{R|marked}}", "{{R|被标记}}" },
            { "{{W|cardiac arrest}}", "{{W|心搏停止}}" },
            { "{{W|covered in spores}}", "{{W|覆满孢子}}" },
            { "{{W|floppy}}", "{{W|松软}}" },
            { "{{W|queasy}}", "{{W|恶心}}" },
            { "{{W|terrified}}", "{{W|恐惧}}" },
            { "{{W|wakeful}}", "{{W|清醒}}" },
            { "{{Y|omniphase}}", "{{Y|全相位}}" },
            { "{{Y|proselytized}}", "{{Y|被劝诱}}" },
            { "{{amorous|love}} tonic", "{{amorous|爱之}}补剂" },
            { "{{b|deep dreaming}}", "{{b|深度入梦}}" },
            { "{{camouflage|camouflaged}}", "{{camouflage|伪装}}" },
            { "{{c|cybernetic rejection syndrome}}", "{{c|义体排斥综合征}}" },
            { "{{freezing|freezing}}", "{{freezing|冰冻中}}" },
            { "{{freezing|frozen}}", "{{freezing|冻结}}" },
            { "{{g|illness}}", "{{g|患病}}" },
            { "{{g|nullphased}}", "{{g|零相位}}" },
            { "{{g|phase spider venom}}", "{{g|相位蛛毒}}" },
            { "{{g|vitalized}}", "{{g|活力充盈}}" },
            { "{{m|quantum-locked}}", "{{m|量子锁定}}" },
            { "{{psionic|psionically cleaved}}", "{{psionic|灵能劈裂}}" },
            { "{{rainbow|irisdual molting}}", "{{rainbow|虹彩蜕皮}}" },
            { "{{rainbow|scintillating}}", "{{rainbow|闪烁}}" },
            { "{{r|bleeding}}", "{{r|流血}}" },
            { "{{r|callow}}", "{{r|稚嫩}}" },
            { "{{r|cleaved}}", "{{r|被劈裂}}" },
            { "{{r|drained}}", "{{r|被汲取}}" },
            { "{{r|flagging}}", "{{r|衰退}}" },
            { "{{r|latched onto}}", "{{r|被附着}}" },
            { "{{r|nosebleed}}", "{{r|鼻血}}" },
            { "{{r|prowling}}", "{{r|夜行强化}}" },
            { "{{r|rusted}}", "{{r|锈蚀}}" },
            { "{{r|shamed}}", "{{r|蒙羞}}" },
            { "{{r|war trance}}", "{{r|战意出神}}" },
            { "{{urban camouflage|urban camouflage}}", "{{urban camouflage|都市伪装}}" },
            { "{{w|grounded}}", "{{w|接地}}" },
            { "{{w|hampered}}", "{{w|受阻}}" },

            // ── 架势的**其它色标变体**（`{{R|` / `{{W|` 版，2.16.65 对账时补出）──
            //   对账工具 `_tools\_reconcile_effect_names3.py`：
            //   103 条枚举名 → 缺这 2 条（`{{G|` 版早已有，红/白标版漏了）。
            { "{{R|aggressive stance}}", "{{R|进攻架势}}" },
            { "{{W|dueling stance}}", "{{W|决斗架势}}" },
        };

        /// <summary>精确查表；未命中原样返回。</summary>
        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (!L.TryGetValue(en, out zh)) return en;
            Fixed++;
            return zh;
        }

        /// <summary>记录一条「类型名 → DisplayName」（去重）。</summary>
        internal static void Note(string type, string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return;
                if (name.Length > 120) return;
                if (_log == null)
                {
                    // ★★ 2.16.65：旧实现用 `Assembly.GetExecutingAssembly().Location`
                    //   —— **本项目没有 DLL**（游戏运行时从 `Scripts\*.cs` 现场编译）
                    //   ⇒ 落到 Unity 临时目录 ⇒ 采集日志实测只有 **26 B**（等于没采到）。
                    //   改用共用工具 `ZhsFileUtil`（多候选目录 + 成败留痕）。
                    string dir = ZhsFileUtil.WriteDir;
                    if (dir == null) return;
                    _log = Path.Combine(dir, "ZhsEffectNames.log");
                    try { if (File.Exists(_log)) File.Delete(_log); } catch { }
                }
                string key = type + "\t" + name;
                if (_seen.Contains(key)) return;
                _seen.Add(key);
                if (_n >= Max) return;
                _n++;
                File.AppendAllText(_log, type + "\t" + name + "\n");
            }
            catch { }
        }
    }

    /// <summary>挂 `XRL.World.Effect` 的构造函数上（所有效果子类共用）。</summary>
    internal static class ZhsEffectNameCollectPatch
    {
        private const string Tag = "[QudZhsCookbook] 效果名：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsEffectNameCollect.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.World.Effect", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.World.Effect");
                    return;
                }
                FieldInfo fld = t.GetField("DisplayName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fld == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到字段 DisplayName");
                    return;
                }
                MethodInfo postCtor = typeof(ZhsEffectNameCollectPatch).GetMethod(
                    "PostCtor", BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo postState = typeof(ZhsEffectNameCollectPatch).GetMethod(
                    "PostState", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.effectnames");

                // ★ Harmony 支持对**构造函数**打补丁
                int n = 0;
                foreach (ConstructorInfo ci in t.GetConstructors(
                             BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Instance))
                {
                    try
                    {
                        h.Patch(ci, postfix: new HarmonyMethod(postCtor));
                        n++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log(Tag + "构造函数跳过：" + e.Message);
                    }
                }
                // `GetStateDescription` 是**虚方法**且各效果类**重写**它
                // ⇒ 只挂基类不够，必须**扫全程序集里的所有重写**。
                int nState = 0;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] all;
                    try { all = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex)
                    {
                        var ok = new List<Type>();
                        foreach (Type x in ex.Types) if (x != null) ok.Add(x);
                        all = ok.ToArray();
                    }
                    catch { continue; }

                    foreach (Type ty in all)
                    {
                        if (ty == null || ty.IsAbstract) continue;
                        if (!t.IsAssignableFrom(ty)) continue;
                        MethodInfo mi;
                        try
                        {
                            mi = ty.GetMethod("GetStateDescription",
                                BindingFlags.Public | BindingFlags.NonPublic
                                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        }
                        catch { continue; }
                        if (mi == null || mi.ReturnType != typeof(string)) continue;
                        try
                        {
                            h.Patch(mi, postfix: new HarmonyMethod(postState));
                            nState++;
                        }
                        catch { }
                    }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个构造函数 + " + nState
                    + " 个 GetStateDescription 重写（采集 + 汉化 "
                    + ZhsEffectNameCollect.L.Count + " 条硬编码名）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>构造函数 Postfix：读 `DisplayName` 采集 + 就地汉化。</summary>
        private static void PostCtor(object __instance)
        {
            if (ZhsEffectNameCollect.Disabled) return;
            try
            {
                if (__instance == null) return;
                FieldInfo fld = __instance.GetType().GetField("DisplayName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fld == null) return;
                string cur = fld.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(cur)) return;
                ZhsEffectNameCollect.Note(__instance.GetType().Name, cur);
                string zh = ZhsEffectNameCollect.Tr(cur);
                if (!ReferenceEquals(zh, cur) && zh != cur) fld.SetValue(__instance, zh);
            }
            catch { }
        }

        /// <summary>`GetStateDescription` Postfix：采集 + 汉化返回值。</summary>
        private static void PostState(ref string __result)
        {
            if (ZhsEffectNameCollect.Disabled) return;
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                string zh = ZhsEffectNameCollect.Tr(__result);
                if (ReferenceEquals(zh, __result) || zh == __result)
                {
                    ZhsEffectNameCollect.Note("(state)", __result);
                    return;
                }
                __result = zh;
            }
            catch { }
        }
    }
}
