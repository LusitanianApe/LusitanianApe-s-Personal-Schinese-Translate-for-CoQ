// ═══════════════════════════════════════════════════════════════════════════
// 称呼词「字段级」汉化 —— 第 3 版（前两版为何失败已查明）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证截图：`对你的嗅觉表示同情，**friend**。`
//   该句来自 `=player.formalAddressTerm=`。我方 `Genders.xml` 已写
//   `FormalAddressTerm="朋友"`，但游戏仍显示英文。
//
// 【前两版复盘】
//   第 1 版（0.26.0）：给 `BasePronounProvider` 的 22 个**属性 getter** 挂 Postfix
//                     ⇒ 挂钩成功但**从未执行**（残留数百处、中文 0 处）
//   第 2 版（诊断版）：改挂 `GameObjectReplacers` 的替换器 + Grammar 代词
//                     ⇒ 0.40 起**停用**（`ZhsBootstrap` 里那几行被注释）
//
// 【本版根因（反编译实证）】`XRL.World.BasePronounProvider` 的值是**公开字段**，
//   而游戏**直接读字段**，不经过属性 getter：
//
//     public string _PersonTerm        = **"human"**;
//     public string _ImmaturePersonTerm = **"child"**;
//     public string _FormalAddressTerm = **"friend"**;      // ← 截图这个词
//     public string _OffspringTerm     = **"child"**;
//     public string _SiblingTerm       = **"sib"**;
//     public string _ParentTerm        = **"progenitor"**;
//     public string _Subjective = "they"; _Objective = "them"; _PossessiveAdjective = "their";
//     + 各 `_Capitalized*` 版本
//
//   ⇒ 且 `Load` 逐字段 `Reader.ReadString()`：
//        Obj._FormalAddressTerm = Reader.ReadString();
//     ⇒ **旧存档里存的就是 `"friend"`** ⇒ 改 XML 对已有存档无效。
//
//   ⚠️ 为何 `Genders.xml` 的「朋友」没生效？（待验证）
//      `Gender::LoadGenderNode` 确实会 `Value.FormalAddressTerm = GetAttribute("FormalAddressTerm")`，
//      但玩家对象的 provider 可能未经该路径（走默认字段值），或该赋值被後续 CopyFrom 覆盖。
//      ⇒ **不去猜**：本补丁在**字段级**兜底，与 XML 是否生效无关。
//
// 【做法】两处 **Postfix**，参数最小化（第 1 版就栽在参数过多）：
//   ① `BasePronounProvider` 的**全部构造函数** ⇒ 新对象建立後翻译字段
//   ② `BasePronounProvider.Load`（静态）⇒ 存档反序列化後翻译字段
//   词典**精确匹配**；非白名单值（含中文）一律不动 ⇒ 绝不误伤。
//
// 开关：装机目录放 `_zhs_pronounfield_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPronounField
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>英文称呼/代词 ⇒ 中文（**精确匹配**）。</summary>
        internal static readonly Dictionary<string, string> L =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 直接呼语（★ 截图这条）──
            { "friend", "朋友" },        { "Friend", "朋友" },
            { "friends", "诸位" },       { "Friends", "诸位" },
            { "sir", "先生" },           { "Sir", "先生" },
            { "madam", "女士" },         { "Madam", "女士" },
            { "stranger", "陌生人" },    { "Stranger", "陌生人" },
            { "traveler", "旅人" },      { "Traveler", "旅人" },
            { "wayfarer", "行路者" },    { "Wayfarer", "行路者" },
            { "wanderer", "漫游者" },    { "Wanderer", "漫游者" },
            { "sojourner", "旅居者" },   { "Sojourner", "旅居者" },

            // ── 人称名词 ──
            { "human", "人类" },         { "Human", "人类" },
            { "man", "男人" },           { "Man", "男人" },
            { "woman", "女人" },         { "Woman", "女人" },
            { "men", "男人们" },         { "Men", "男人们" },
            { "women", "女人们" },       { "Women", "女人们" },
            { "people", "人们" },        { "People", "人们" },
            { "person", "人" },          { "Person", "人" },
            { "creature", "生物" },      { "Creature", "生物" },
            { "being", "存在者" },       { "Being", "存在者" },

            // ── 幼体 ──
            { "child", "孩子" },         { "Child", "孩子" },
            { "children", "孩子们" },    { "Children", "孩子们" },
            { "boy", "男孩" },           { "Boy", "男孩" },
            { "girl", "女孩" },          { "Girl", "女孩" },
            { "boys", "男孩们" },        { "Boys", "男孩们" },
            { "girls", "女孩们" },       { "Girls", "女孩们" },
            { "youth", "少年" },         { "Youth", "少年" },

            // ── 亲属 ──
            { "son", "儿子" },           { "Son", "儿子" },
            { "daughter", "女儿" },      { "Daughter", "女儿" },
            { "sons", "儿子们" },        { "Sons", "儿子们" },
            { "daughters", "女儿们" },   { "Daughters", "女儿们" },
            { "brother", "兄弟" },       { "Brother", "兄弟" },
            { "sister", "姐妹" },        { "Sister", "姐妹" },
            { "brothers", "兄弟们" },    { "Brothers", "兄弟们" },
            { "sisters", "姐妹们" },     { "Sisters", "姐妹们" },
            { "sib", "同胞" },           { "Sib", "同胞" },
            { "sibs", "同胞" },          { "Sibs", "同胞" },
            { "father", "父亲" },        { "Father", "父亲" },
            { "mother", "母亲" },        { "Mother", "母亲" },
            { "fathers", "父亲们" },     { "Fathers", "父亲们" },
            { "mothers", "母亲们" },     { "Mothers", "母亲们" },
            { "progenitor", "双亲" },    { "Progenitor", "双亲" },
            { "progenitors", "双亲" },   { "Progenitors", "双亲" },
            { "parent", "亲代" },        { "Parent", "亲代" },
            { "parents", "双亲" },       { "Parents", "双亲" },
            { "offspring", "后代" },     { "Offspring", "后代" },

            // ── 代词（少数路径会走到）──
            { "they", "他们" },   { "They", "他们" },
            { "them", "他们" },   { "Them", "他们" },
            { "their", "他们的" }, { "Their", "他们的" },
            { "theirs", "他们的" }, { "Theirs", "他们的" },
            { "themself", "他们自己" }, { "Themself", "他们自己" },
            { "themselves", "他们自己" }, { "Themselves", "他们自己" },
            { "it", "它" },       { "It", "它" },
            { "its", "它的" },    { "Its", "它的" },
            { "itself", "它自己" }, { "Itself", "它自己" },
            { "he", "他" },       { "He", "他" },
            { "him", "他" },      { "Him", "他" },
            { "his", "他的" },    { "His", "他的" },
            { "himself", "他自己" }, { "Himself", "他自己" },
            { "she", "她" },      { "She", "她" },
            { "her", "她" },      { "Her", "她" },
            { "hers", "她的" },   { "Hers", "她的" },
            { "herself", "她自己" }, { "Herself", "她自己" },
        };

        /// <summary>要翻译的字段（含 `_Capitalized*` 变体）。</summary>
        internal static readonly string[] FIELDS =
        {
            "_Subjective", "_CapitalizedSubjective",
            "_Objective", "_CapitalizedObjective",
            "_PossessiveAdjective", "_CapitalizedPossessiveAdjective",
            "_SubstantivePossessive", "_CapitalizedSubstantivePossessive",
            "_Reflexive", "_CapitalizedReflexive",
            "_PersonTerm", "_CapitalizedPersonTerm",
            "_ImmaturePersonTerm", "_CapitalizedImmaturePersonTerm",
            "_FormalAddressTerm", "_CapitalizedFormalAddressTerm",
            "_OffspringTerm", "_CapitalizedOffspringTerm",
            "_SiblingTerm", "_CapitalizedSiblingTerm",
            "_ParentTerm", "_CapitalizedParentTerm",
        };

        internal static bool TryTr(ref string v)
        {
            if (Disabled || string.IsNullOrEmpty(v)) return false;
            string zh;
            if (!L.TryGetValue(v, out zh)) return false;
            v = zh;
            Hits++;
            return true;
        }
    }

    internal static class ZhsPronounFieldPatch
    {
        private const string Tag = "[QudZhsCookbook] 称呼词（字段级）：";
        private static bool _applied;
        private static int _logged;

        internal static void TryApply()
        {
            if (_applied || ZhsPronounField.Disabled) return;
            _applied = true;
            try
            {
                Type t = FindType("XRL.World.BasePronounProvider");
                if (t == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 XRL.World.BasePronounProvider");
                    return;
                }
                MethodInfo post = typeof(ZhsPronounFieldPatch).GetMethod(
                    "ObjPostfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (post == null) return;

                int n = 0;
                Harmony h = new Harmony("Nattens.QudZhsCookbook.pronounfield");

                // ① 全部构造函数（含无参与拷贝构造）
                foreach (ConstructorInfo ci in t.GetConstructors(
                             BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Instance))
                {
                    try
                    {
                        h.Patch(ci, postfix: new HarmonyMethod(post));
                        n++;
                    }
                    catch (Exception e) { LogOnce("ctor: " + e.Message); }
                }

                // ② 静态 Load(SerializationReader, BasePronounProvider)
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != "Load") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2 || ps[1].ParameterType != t) continue;
                    try
                    {
                        h.Patch(m, postfix: new HarmonyMethod(post));
                        n++;
                    }
                    catch (Exception e) { LogOnce("load: " + e.Message); }
                }

                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（"
                    + ZhsPronounField.L.Count + " 条称呼词 · 字段 "
                    + ZhsPronounField.FIELDS.Length + " 个）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static void LogOnce(string msg)
        {
            if (_logged++ < 6) UnityEngine.Debug.LogError(Tag + msg);
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

        /// <summary>
        /// 後置：翻译字段。`Load` 是静态方法、对象在第 2 参 ⇒ args 里找；
        /// 构造函数用 `__instance`。参数只有这两个（第 1 版就栽在参数过多）。
        /// </summary>
        private static void ObjPostfix(object __instance, object[] __args)
        {
            try
            {
                object obj = __instance;
                if (obj == null && __args != null)
                {
                    for (int i = 0; i < __args.Length; i++)
                    {
                        object a = __args[i];
                        if (a != null && a.GetType().Name == "BasePronounProvider")
                        {
                            obj = a;
                            break;
                        }
                    }
                }
                if (obj == null) return;

                Type t = obj.GetType();
                int changed = 0;
                for (int i = 0; i < ZhsPronounField.FIELDS.Length; i++)
                {
                    FieldInfo f = null;
                    try
                    {
                        f = t.GetField(ZhsPronounField.FIELDS[i],
                            BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.Instance);
                    }
                    catch { }
                    if (f == null) continue;
                    string v = f.GetValue(obj) as string;
                    if (v == null) continue;
                    if (ZhsPronounField.TryTr(ref v))
                    {
                        f.SetValue(obj, v);
                        changed++;
                    }
                }
                if (changed > 0 && _logged < 40)
                {
                    _logged++;
                    UnityEngine.Debug.Log(Tag + "翻译 " + changed + " 个字段（累计命中 "
                        + ZhsPronounField.Hits + "）");
                }
            }
            catch (Exception e)
            {
                LogOnce("ObjPostfix: " + e.Message);
            }
        }
    }
}
