// ═══════════════════════════════════════════════════════════════════════════
// `=subject.thisCreature=` 的出口接管
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】
//   中文句里夹 `This creature` / `this creature`（用户 2026-10-24 第三次测试窗口 #4）。
//   全模组译文里仍有 `=subject.thisCreature=` **52 处**、`=subject.this=` 8 处、
//   `=object.this=` 1 处、`=object.this.them=` 1 处。
//
// 【已查明（反编译实证）】
//   `XRL.World.Text.Delegates.GameObjectReplacers::ThisCreature` 是
//       `public static void ThisCreature(VariableContext Context, GameObject Object)`
//   —— **返回 void**，往 `Context.Value` 追加文本：
//       · 玩家（且 Grammar.AllowSecondPerson）⇒ 追加代词 Subjective（已由代词补丁中文化）
//       · 其它 ⇒ `=subject.this= =subject.pluralize.word:creature=`
//         ⇒ 输出 `This creature` / `These creatures`
//   ★ 现有 `ZhsPronounPatch` 的 `GameObjectReplacerNames` 白名单**只挂返回 string 的方法**
//     （`if (mi.ReturnType != typeof(string)) continue;`）⇒ 这个方法**从来没被挂过**。
//
// 【修法】
//   Prefix 拦截，自己往 `Context.Value` 追加中文，然后 `return false` 跳过原方法。
//   · 玩家 ⇒ 放行（原方法走 Subjective；那条已被代词补丁译成「你」）
//   · 单数 ⇒ 「该生物」；复数 ⇒ 「这些生物」
//
// ★ `VariableContext.Value` 是 **public readonly 字段**（不是属性）—— 见 §53 与
//   `ZhsPlaqueWidthPatch.ValueOf` 的注释（用 GetProperty 会恒 null）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsThisCreaturePatch
    {
        private const string Tag = "[QudZhsCookbook] thisCreature 接管：";

        private static bool _applied;
        private static int _calls;
        private static int _hits;
        private static int _errors;

        private static readonly Dictionary<Type, MethodInfo> _appendOf =
            new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> _pluralOf =
            new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> _isPlayerOf =
            new Dictionary<Type, MethodInfo>();

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = Find("XRL.World.Text.Delegates.GameObjectReplacers");
            if (t == null) { LogError("找不到 GameObjectReplacers"); return; }

            MethodInfo target = t.GetMethod("ThisCreature",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new Type[] { Find("XRL.World.Text.Delegates.VariableContext") ?? typeof(object),
                                   Find("XRL.World.GameObject") ?? typeof(object) },
                null);
            if (target == null)
            {
                // 兜底：按名字找唯一重载
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Static))
                    if (mi.Name == "ThisCreature" && !mi.ContainsGenericParameters) { target = mi; break; }
            }
            if (target == null) { LogError("找不到 ThisCreature"); return; }

            MethodInfo pre = typeof(ZhsThisCreaturePatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                new Harmony("Nattens.QudZhsCookbook.thiscreature")
                    .Patch(target, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 ThisCreature（void 型，Prefix 改写 Context.Value）");
            }
            catch (Exception e) { LogError("挂载失败：" + e.Message); }
        }

        /// <summary>
        /// `ThisCreature(VariableContext Context, GameObject Object)`。
        /// 返回 false ⇒ 跳过原方法体（我们已经把中文写进 Context.Value）。
        /// </summary>
        private static bool Prefix(object __0, object __1)
        {
            try
            {
                if (__0 == null || __1 == null) return true;
                _calls++;

                // 玩家：放行原方法（它取 Subjective，已被代词补丁中文化）
                if (IsPlayer(__1) && AllowSecondPerson()) return true;

                bool plural = IsPlural(__1);
                string zh = plural ? "这些生物" : "该生物";

                object value = Field(__0, "Value");
                MethodInfo append = AppendOf(value);
                if (value == null || append == null)
                {
                    if (_errors++ < 5)
                        UnityEngine.Debug.LogWarning(Tag + "拿不到 Context.Value，放行");
                    return true;                 // 拿不到就放行，别把原文吞掉
                }
                append.Invoke(value, new object[] { zh });
                _hits++;
                if (_hits <= 10)
                    UnityEngine.Debug.Log(Tag + "#" + _hits + " -> " + zh
                        + "（第 " + _calls + " 次进入）");
                return false;
            }
            catch (Exception e)
            {
                if (_errors++ < 5)
                    UnityEngine.Debug.LogWarning(Tag + "Prefix 异常（放行）：" + e.Message);
                return true;
            }
        }

        // ---------------- 反射工具 ----------------

        private static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object Field(object o, string name)
        {
            if (o == null) return null;
            FieldInfo f = o.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = o.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            return p == null ? null : p.GetValue(o, null);
        }

        private static MethodInfo AppendOf(object sink)
        {
            if (sink == null) return null;
            Type t = sink.GetType();
            MethodInfo mi;
            if (_appendOf.TryGetValue(t, out mi)) return mi;
            try
            {
                mi = t.GetMethod("Append", BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { mi = null; }
            _appendOf[t] = mi;
            return mi;
        }

        private static bool IsPlayer(object go)
        {
            Type t = go.GetType();
            MethodInfo mi;
            if (!_isPlayerOf.TryGetValue(t, out mi))
            {
                try { mi = t.GetMethod("IsPlayer", Type.EmptyTypes); } catch { mi = null; }
                _isPlayerOf[t] = mi;
            }
            if (mi == null) return false;
            try { return (bool)mi.Invoke(go, null); } catch { return false; }
        }

        private static bool IsPlural(object go)
        {
            Type t = go.GetType();
            MethodInfo mi;
            if (!_pluralOf.TryGetValue(t, out mi))
            {
                try
                {
                    mi = t.GetMethod("GetPlurality", BindingFlags.Public | BindingFlags.Instance,
                        null, new Type[] { typeof(bool) }, null);
                }
                catch { mi = null; }
                _pluralOf[t] = mi;
            }
            if (mi == null) return false;
            try { return (bool)mi.Invoke(go, new object[] { false }); } catch { return false; }
        }

        private static bool AllowSecondPerson()
        {
            Type g = Find("XRL.Language.Grammar");
            if (g == null) return true;                 // 拿不到就按官方默认（true）
            PropertyInfo p = g.GetProperty("AllowSecondPerson",
                BindingFlags.Public | BindingFlags.Static);
            if (p == null) return true;
            try { return (bool)p.GetValue(null, null); } catch { return true; }
        }

        private static void LogError(string msg)
        {
            if (_errors++ < 20)
                UnityEngine.Debug.LogError(Tag + msg);
        }
    }
}
