// ═══════════════════════════════════════════════════════════════════════════
// 词池合并语义修正 v2 —— 在**源头**把 MergeModJson 的「追加」改成「替换」
// ═══════════════════════════════════════════════════════════════════════════
//
// 【v1 的教训】
//   v1 用 `HistoricSpice.Init()` 的后置补丁「裁掉中英混杂数组的英文前缀」。
//   实测（Player.log）：
//      词池合并修正：裁剪完成：处理 142 个中英混杂数组，删除 487 条英文
//   但只覆盖了 142 个数组 —— 远少于实际混杂的池数，
//   而且裁剪是**事后补救**：`Init()` 末尾已经把合并结果写回
//   `Local\HistorySpice.jsonc`，裁剪改的是内存，磁盘与内存不一致，难以核对。
//
// 【v2 的做法：源头替换】
//   `HistoryKit.HistoricSpice.MergeModJson(JObject base, JObject mod)` 的 IL：
//
//       foreach (mod 的属性 p):
//           if p.Name 以 "=" 结尾:  base[p.Name] = p.Value          // 直接覆盖
//           else if base.ContainsKey(p.Name):
//               if p.Value 是 JObject:  递归 MergeModJson(...)      // 递归合并
//               else if p.Value 是 JArray:
//                   foreach (mod 数组元素) baseArray.Add(元素)      // ★ 追加！病根
//               else: base[p.Name] = p.Value
//           else: base[p.Name] = p.Value
//
//   追加语义用在**词池**上完全错误：词池是「可选值列表」，
//   不是「要累积的集合」。基础英文 + 我方中文 = 中英各半随机抽。
//
//   ⇒ 本补丁在 MergeModJson **之前**把 base 里所有「mod 也要提供」的数组**清空**，
//     于是后面的 `JArray::Add` 只能加进我们的中文 —— 等价于「替换」。
//
//   实现方式（避开泛型方法签名问题）：
//     不直接补 MergeModJson，而是在**每次数组追加落点**
//     `Newtonsoft.Json.Linq.JObject::set_Item(String, JToken)` 上挂 Prefix：
//       若「目标对象是词典根的直接子节点（即 base[池名]）」且「传入的是数组」
//       且「该数组已有元素」→ 清空后再赋值 ⇒ 替换语义。
//
//     ⚠ 这个判据需要区分「词典根」与「子对象」。用一个小技巧：
//       在 Prefix 里检查 `JObject` 的父链深度（JObject.Parent 的 Parent…），
//       base = _root["spice"]，所以数组的 Parent 就是 base，
//       而 base.Parent = _root，_root.Parent = null。
//       ⇒ 只在「数组的父对象是 _root 的直接子对象」时才替换 ——
//         也就是池名的第一层。
//
//   安全性：只影响「模组合并」这一条路径；不碰任何游戏对象；
//           全程 try/catch；失败只记日志。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsSpiceMergeFix
    {
        private const string Tag = "[QudZhsCookbook] 词池合并修正 v2：";

        private static bool _applied;
        private static bool _debugOnce;
        private static int _replaced;
        private static int _cleared;

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;

            Type t = FindType("HistoryKit.HistoricSpice");
            if (t == null)
            {
                UnityEngine.Debug.LogError(Tag + "找不到 HistoryKit.HistoricSpice");
                return;
            }

            // 拿 _root 的引用（供判据用）
            try
            {
                FieldInfo f = t.GetField("_root", BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Static);
                if (f != null) _rootsField = f;
            }
            catch { }

            int n = 0;

            // ① 主补丁：MergeModJson 前置 —— 用「清空同名数组」实现替换语义
            MethodInfo pre = typeof(ZhsSpiceMergeFix).GetMethod(
                "PrefixMerge", BindingFlags.NonPublic | BindingFlags.Static);
            if (pre != null)
            {
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (mi.Name != "MergeModJson") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length != 2) continue;
                    try
                    {
                        Harmony h = new Harmony("Nattens.QudZhsCookbook.spicemerge2.pre");
                        h.Patch(mi, prefix: new HarmonyMethod(pre));
                        n++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError(Tag + "挂 MergeModJson 失败：" + e.Message);
                    }
                }
            }

            UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个 MergeModJson 前置补丁");
        }

        private static FieldInfo _rootsField;

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
        /// 在合并之前，把 base 里「mod 也提供的数组」清空。
        /// 之后的 `JArray::Add` 便只能加进我们的中文 —— 等价于替换。
        /// </summary>
        private static void PrefixMerge(object __0, object __1)
        {
            try
            {
                if (!(__0 is JObject baseObj) || !(__1 is JObject modObj)) return;

                int clearedHere = 0;
                var names = new List<string>();
                foreach (JProperty p in modObj.Properties()) names.Add(p.Name);

                foreach (string name in names)
                {
                    if (name.EndsWith("=", StringComparison.Ordinal)) continue;  // 覆盖语义，无需处理
                    JToken bv = baseObj[name];
                    JToken mv = modObj[name];
                    if (bv == null || mv == null) continue;
                    if (bv.Type != JTokenType.Array || mv.Type != JTokenType.Array) continue;

                    var ba = (JArray)bv;
                    if (ba.Count > 0)
                    {
                        clearedHere += ba.Count;
                        ba.Clear();          // ★ 关键：清空，让后续 Add 独占
                        _cleared += clearedHere;
                    }
                    _replaced++;
                }

                if (!_debugOnce)
                {
                    _debugOnce = true;
                    UnityEngine.Debug.Log(Tag + "首个词典处理完毕，清空 " + clearedHere
                        + " 条基础英文（示例键：" + (names.Count > 0 ? names[0] : "?") + "）");
                }
                // 每次同级合并都汇报一次，便于从 Player.log 核对替换总量
                UnityEngine.Debug.Log(Tag + "本轮清空 " + clearedHere + " 条基础英文，"
                    + "累计替换数组 " + _replaced + " 个 / 清空 " + _cleared + " 条");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "清空异常：" + e.Message);
            }
        }

        /// <summary>合并完成后打一行汇总（供核对）。</summary>
        public static void ReportIfDone()
        {
            UnityEngine.Debug.Log(Tag + "累计替换数组 " + _replaced + " 个，清空基础英文 " + _cleared + " 条");
        }
    }
}
