// ─────────────────────────────────────────────────────────────────────────
//  ZhsSubtypeGuardPatch —— 阻止「预设天职被误清空」（callings bug 的收口）
//
//  证据（探针 v2 实测）：
//      GetSelections() 返回 12 个正确英文 Id（含 Arconaut）——列表没问题
//      预设 setData(Arconaut) 成功                        ——数据设上了
//      随后 3 次 setData(null/空) 把它清空                  ——handleModuleDataChange 干的
//
//  `QudSubtypeModule.handleModuleDataChange`：
//      if (module is QudGenotypeModule && base.data != null) {
//          if (…Genotype == null) setData(null);
//          else if (!GetSelections().Any(s => s.Id == base.data.Subtype)) setData(null);
//      }
//
//  预设初始化会依次 setData 各个模块，每次广播都让这段跑一遍；
//  在预设的天职刚写入、而 Genotype 尚未就绪（或列表瞬时为空）时，它就把天职清成 null。
//
//  修法（保守）：Prefix 时若「data 已有 Subtype」，先把它摘下来并把 data 置 null，
//  使 `base.data != null` 为假 → 整段跳过；Postfix 再原样放回。
//  ⇒ 不改变任何判定逻辑，只阻止这次误清空；玩家手动改基因型时该清空的仍会清空
//    （因为那时 Subtype 与新的 Genotype 不匹配，本补丁不会无条件保留）。
// ─────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsSubtypeGuardPatch
    {
        private const string Tag = "[QudZhsCookbook] ";
        private static bool _applied;

        /// <summary>按线程记录被摘下的 data（协调前缀与后缀）。</summary>
        [ThreadStatic] private static object _saved;

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = FindType("XRL.CharacterBuilds.Qud.QudSubtypeModule");
            if (t == null) { Debug.LogError(Tag + "天职守卫：找不到 QudSubtypeModule"); return; }

            MethodInfo target = null;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                                                   | BindingFlags.NonPublic
                                                   | BindingFlags.Instance))
            {
                if (mi.Name == "handleModuleDataChange") { target = mi; break; }
            }
            if (target == null)
            {
                Debug.LogError(Tag + "天职守卫：找不到 handleModuleDataChange");
                return;
            }

            try
            {
                MethodInfo pre = typeof(ZhsSubtypeGuardPatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo post = typeof(ZhsSubtypeGuardPatch).GetMethod(
                    "Postfix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.subtypeguard");
                h.Patch(target, prefix: new HarmonyMethod(pre),
                        postfix: new HarmonyMethod(post));
                Debug.Log(Tag + "天职守卫：已挂 QudSubtypeModule.handleModuleDataChange"
                          + "（阻止预设天职被误清空）");
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "天职守卫挂载失败：" + e.Message);
            }
        }

        private static void Prefix(object __instance)
        {
            try
            {
                _saved = null;
                object data = Member(__instance, "data");
                if (data == null) return;
                string sub = Convert.ToString(Member(data, "Subtype"));
                if (string.IsNullOrEmpty(sub)) return;      // 没东西可保
                _saved = data;
                SetMember(__instance, "data", null);        // 让 `base.data != null` 为假
            }
            catch { }
        }

        private static void Postfix(object __instance)
        {
            try
            {
                if (_saved == null) return;
                SetMember(__instance, "data", _saved);      // 原样放回
            }
            catch { }
            finally { _saved = null; }
        }

        private static object Member(object o, string name)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public
                                              | BindingFlags.NonPublic
                                              | BindingFlags.Instance);
                if (f != null) return f.GetValue(o);
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public
                                                     | BindingFlags.NonPublic
                                                     | BindingFlags.Instance);
                if (p != null) return p.GetValue(o, null);
            }
            return null;
        }

        private static void SetMember(object o, string name, object v)
        {
            if (o == null) return;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public
                                              | BindingFlags.NonPublic
                                              | BindingFlags.Instance);
                if (f != null) { f.SetValue(o, v); return; }
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public
                                                     | BindingFlags.NonPublic
                                                     | BindingFlags.Instance);
                if (p != null && p.CanWrite) { p.SetValue(o, v, null); return; }
            }
        }

        private static Type FindType(string full)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { Type t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }
    }
}
