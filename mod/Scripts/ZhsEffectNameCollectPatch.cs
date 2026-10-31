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
        internal static Dictionary<string, string> L => ZhsLexicon.Effects;

        /// <summary>精确查表；未命中原样返回。</summary>
        internal static string Tr(string en)
        {
            if (Disabled || string.IsNullOrEmpty(en)) return en;
            string zh;
            if (L.TryGetValue(en, out zh)) { Fixed++; return zh; }

            // ★ 拼装句（主体无法枚举 ⇒ 只能锚定前缀，不能进精确表）
            //   【取证】`XRL.World.Effects.Stuck::GetStateDescription`：
            //     textBuilder.Append(Adjective)            // 字段默认 "stuck"
            //       .Append(' ').Append(Preposition)       // 字段默认 "in"
            //       .Append(' ').Append(From.an());        // ← 网对象（我方已译成中文）
            //   ⇒ 实例 `stuck in 霜冻蛛网`（ZhsEffectNames.log 3 条，2026-10-25 实机）
            //   ⇒ `stuck` 本身在 Effects 精确表里（`卡住`），但带主体的整串永远不在表里。
            //   同族：`Stuck::GetDetails` = Adjective.InitUpper() + " in " + an()
            //        （`Stuck in …`；GetDetails 尚未挂出口，规则先写好备用）
            if (en.StartsWith("stuck in ", StringComparison.Ordinal))
            { Fixed++; return "被困在" + en.Substring(9) + "中"; }
            if (en.StartsWith("Stuck in ", StringComparison.Ordinal))
            { Fixed++; return "被困在" + en.Substring(9) + "中"; }
            // `GetDetails` 无 DependsOn 分支：`stuck where it is.`
            if (en.StartsWith("stuck where it is", StringComparison.Ordinal))
            { Fixed++; return "被困在原地。"; }

            return en;
        }

        /// <summary>记录一条「类型名 → DisplayName」（去重）。</summary>
        internal static void Note(string type, string name, bool hit)
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
                    // 2026-10-25: no longer delete; load existing lines as dedup seed
                    try
                    {
                        if (File.Exists(_log))
                            foreach (string line in File.ReadAllLines(_log))
                            {
                                string[] parts = line.Split((char)9);
                                if (parts.Length >= 2) _seen.Add(parts[0] + (char)9 + parts[1]);
                            }
                    }
                    catch { }
                }
                string key = type + "\t" + name;
                if (_seen.Contains(key)) return;
                _seen.Add(key);
                if (_n >= Max) return;
                _n++;
                File.AppendAllText(_log, type + (char)9 + name + (char)9 + (hit ? "HIT" : "MISS") + (char)10);
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
                string zh = ZhsEffectNameCollect.Tr(cur);
                bool hit = !ReferenceEquals(zh, cur) && zh != cur;
                ZhsEffectNameCollect.Note(__instance.GetType().Name, cur, hit);
                if (hit) fld.SetValue(__instance, zh);
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
                    ZhsEffectNameCollect.Note("(state)", __result, false);
                    return;
                }
                __result = zh;
            }
            catch { }
        }
    }
}
