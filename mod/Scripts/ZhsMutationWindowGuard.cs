// 变异窗口数据同步护栏 —— 修游戏自身缺失的 null 检查。
//
// ★ 背景（1.58.0 实测崩溃栈）：
//     XRL.CharacterBuilds.Qud.UI.QudMutationsModuleWindow.UpdateNodesFromData()
//     [0x0004b] NullReferenceException
//       at ...UpdateNodesFromData()
//       at ...EnsureData()
//       at ...BeforeShow(descriptor)
//       at EmbarkBuilderModuleWindowDescriptor.show()
//       at EmbarkBuilder.ShowWindow(...)
//       at EmbarkBuilder.advance(...)
//
//   原实现（反编译）：
//       foreach (QudMutationModuleDataRow row in base.module.data.selections)
//       {
//           MLNode mLNode = mutationNodes.Find(n => n.Entry != null && n.Entry.Name == row.Mutation);
//           mLNode.Selected = row.Count;      // ← 没有 null 检查
//           mLNode.Variant = row.Variant;
//           num += mLNode.Selected * mLNode.Entry.Cost;
//       }
//
//   我方 `ZhsMutationPatch` 把 `MutationEntry.Name` 改成了中文（为了让 UI 显示中文），
//   而预设里存的 `row.Mutation` 可能是英文 ⇒ `Find` 落空 ⇒ mLNode = null ⇒ 崩。
//
// ★ 本补丁只做三件事：**加 null 护栏、加三层名字回退、绝不改变原有正确路径的行为**。
//   不改数据结构、不改显示名、不写回任何东西。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>变异窗口 UpdateNodesFromData 的护栏版实现（Prefix 整体替换）。</summary>
    internal static class ZhsMutationWindowGuard
    {
        public static bool Disabled;
        private static bool _applied;
        private static int _missLogged;
        private static int _fixed;

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type t = typeof(XRL.CharacterBuilds.Qud.UI.QudMutationsModuleWindow);
                MethodInfo m = t.GetMethod("UpdateNodesFromData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 变异窗口护栏：找不到 UpdateNodesFromData");
                    return;
                }
                MethodInfo pre = typeof(ZhsMutationWindowGuard).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.mutwindow.guard");
                h.Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log("[QudZhsCookbook] 变异窗口护栏：已挂 UpdateNodesFromData");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异窗口护栏挂载失败：" + e);
            }
        }

        /// <summary>把「英文原名 → 中文译名」的反向别名表建起来。</summary>
        private static Dictionary<string, string> _alias;
        private static Dictionary<string, string> Alias
        {
            get
            {
                if (_alias != null) return _alias;
                _alias = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    foreach (var kv in ZhsMutationZh.NameByOrig)
                    {
                        // NameByOrig: 官方英文名 → 中文名
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)
                            && !string.Equals(kv.Key, kv.Value, StringComparison.Ordinal))
                        {
                            _alias[kv.Key] = kv.Value;
                            _alias[kv.Value] = kv.Key;   // 双向
                        }
                    }
                    foreach (var kv in ZhsMutationZh.NameByClass)
                    {
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                        {
                            if (!_alias.ContainsKey(kv.Key)) _alias[kv.Key] = kv.Value;
                            if (!_alias.ContainsKey(kv.Value)) _alias[kv.Value] = kv.Key;
                        }
                    }
                }
                catch { }
                return _alias;
            }
        }

        private static object F(object o, string name)
        {
            if (o == null) return null;
            try
            {
                for (Type t = o.GetType(); t != null; t = t.BaseType)
                {
                    FieldInfo f = AccessTools.Field(t, name);
                    if (f != null) return f.GetValue(o);
                }
            }
            catch { }
            return null;
        }

        private static string S(object o, string name)
        {
            object v = F(o, name);
            return v as string;
        }

        private static int I(object o, string name)
        {
            object v = F(o, name);
            return (v is int i) ? i : 0;
        }

        /// <summary>
        /// Prefix：完全替代原方法。返回 false = 跳过原实现。
        /// 与原语义一致：清空节点 → 按 selections 回填 Selected/Variant → 累加点数。
        /// </summary>
        private static bool Prefix(object __instance)
        {
            if (Disabled) return true;      // 开关关闭 → 走原实现
            try
            {
                IList nodes = F(__instance, "mutationNodes") as IList;
                if (nodes == null) return true;

                object module = F(__instance, "module");
                object data = module == null ? null : F(module, "data");
                IList sels = data == null ? null : F(data, "selections") as IList;
                if (sels == null) return true;

                // ★ 必须调用**原版** ClearNodes() —— 它会重建 mutationNodes 与
                //   categoryMenus（遍历 MutationFactory.GetCategories() 逐个建 MLNode）。
                //   只清 Selected/Variant 是不够的：那会漏掉重建，导致列表为空。
                bool cleared = false;
                try
                {
                    MethodInfo cm = __instance.GetType().GetMethod("ClearNodes",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (cm != null) { cm.Invoke(__instance, null); cleared = true; }
                }
                catch (Exception ce)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 变异窗口护栏：ClearNodes 失败 → 回退原实现：" + ce.Message);
                    return true;
                }
                if (!cleared) return true;

                // ClearNodes 重建了列表 ⇒ 重新取一次引用
                nodes = F(__instance, "mutationNodes") as IList;
                if (nodes == null) return true;

                int total = 0;
                foreach (object row in sels)
                {
                    if (row == null) continue;
                    string want = S(row, "Mutation");
                    string wantVar = S(row, "Variant");
                    int cnt = I(row, "Count");
                    if (string.IsNullOrEmpty(want)) continue;

                    object node = FindNode(nodes, want, wantVar);
                    if (node == null)
                    {
                        _fixed++;
                        if (_missLogged < 12)
                        {
                            _missLogged++;
                            UnityEngine.Debug.Log("[QudZhsCookbook] 变异窗口护栏：找不到节点，已跳过 row="
                                + want + "（variant=" + (wantVar ?? "-") + "）");
                        }
                        continue;
                    }
                    SetF(node, "Selected", cnt);
                    SetF(node, "Variant", wantVar);
                    object entry = F(node, "Entry");
                    int cost = entry == null ? 0 : I(entry, "Cost");
                    total += cnt * cost;
                }
                // 原方法把 num 只用于内部累加（未写回任何字段），故不写回。
                return false;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 变异窗口护栏出错（回退原实现）：" + e);
                return true;    // 出错就让原实现跑（宁可原样崩，也不要行为不一致）
            }
        }

        private static void SetF(object o, string name, object val)
        {
            try
            {
                for (Type t = o.GetType(); t != null; t = t.BaseType)
                {
                    FieldInfo f = AccessTools.Field(t, name);
                    if (f != null) { f.SetValue(o, val); return; }
                }
            }
            catch { }
        }

        /// <summary>三层回退找节点。</summary>
        private static object FindNode(IList nodes, string want, string wantVar)
        {
            // ① 名字直接匹配（原逻辑）
            object hit = Match(nodes, want);
            if (hit != null) return hit;

            // ② 别名回退：英文名 ↔ 中文名
            string alt;
            if (Alias.TryGetValue(want, out alt) && !string.IsNullOrEmpty(alt))
            {
                hit = Match(nodes, alt);
                if (hit != null) return hit;
            }

            // ③ Class 回退：拿 row 的 Entry.Class（若有）去比节点的 Entry.Class
            try
            {
                string cls = null;
                object re = null;
                try
                {
                    // row.Entry 是只读属性，走 getMutationEntryByName
                    Type rt = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        rt = asm.GetType("XRL.CharacterBuilds.Qud.QudMutationModuleDataRow");
                        if (rt != null) break;
                    }
                    if (rt != null)
                    {
                        PropertyInfo pe = rt.GetProperty("Entry");
                        // 需要 row 实例；此处由调用方传入更合适，简化处理：
                        // 直接用 XRL.MutationFactory.GetMutationEntryByName(want)
                        Type mf = null;
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            mf = asm.GetType("XRL.MutationFactory");
                            if (mf != null) break;
                        }
                        if (mf != null)
                        {
                            MethodInfo gm = mf.GetMethod("GetMutationEntryByName",
                                BindingFlags.Public | BindingFlags.Static);
                            if (gm != null) re = gm.Invoke(null, new object[] { want });
                        }
                    }
                }
                catch { }
                if (re != null) cls = S(re, "Class");
                if (string.IsNullOrEmpty(cls) && Alias.TryGetValue(want, out alt))
                {
                    try
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            Type mf2 = asm.GetType("XRL.MutationFactory");
                            if (mf2 == null) continue;
                            MethodInfo gm2 = mf2.GetMethod("GetMutationEntryByName",
                                BindingFlags.Public | BindingFlags.Static);
                            if (gm2 != null)
                            {
                                object e2 = gm2.Invoke(null, new object[] { alt });
                                if (e2 != null) { cls = S(e2, "Class"); break; }
                            }
                        }
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(cls))
                {
                    foreach (object n in nodes)
                    {
                        if (n == null) continue;
                        object e = F(n, "Entry");
                        if (e != null && string.Equals(S(e, "Class"), cls, StringComparison.Ordinal))
                            return n;
                    }
                }
            }
            catch { }
            return null;
        }

        private static object Match(IList nodes, string want)
        {
            foreach (object n in nodes)
            {
                if (n == null) continue;
                object e = F(n, "Entry");
                if (e != null && string.Equals(S(e, "Name"), want, StringComparison.Ordinal))
                    return n;
            }
            return null;
        }
    }
}
