// -*- coding: utf-8 -*-
// ============================================================================
//  ZhsHookProbe —— ① Harmony 实际补丁顺序 dump ｜ ② 运行期钩子顺序追踪
//
//  背景（2026-10-18 · F16/F17）：
//    `ZhsJournalNotePatch` 把 Postfix 当成能改输入参数用了 ⇒ 日志说替换成功、
//    屏幕纹丝不动。查这个问题时发现：**我们没有任何手段看「钩子的运行期顺序」**。
//
//  ① `DumpPatchInfo()`：启动时遍历 `Harmony.GetAllPatchedMethods()`，
//     把每个方法上的 prefix/postfix/transpiler 按 Harmony 的**实际排序**
//     落盘到 `_zhs_patchinfo.log`（owner / priority / index）。
//     ⇒ 解决「同一方法上谁先谁后」，零运行期开销。
//
//  ② `ZhsHookTrace.Step(tag, text)`：在关键钩子入口记一行（自增序号 + 线程 + 文本），
//     写进内存环形缓冲。命中 **触发串** 时先把缓冲整个 dump（能看到触发前的顺序），
//     随后再记 200 步。⇒ 解决「跨方法全序」，且不淹日志。
//
//  开关（装机目录）：
//    `_zhs_hooktrace_on.txt`      —— 存在才启用（①②都受它控制）
//    `_zhs_hooktrace_trigger.txt` —— 第一行是触发串（如 `destroyed`）；
//                                   没有它 ⇒ 永不 dump（只记内存）
//  输出：
//    `_zhs_patchinfo.log`  补丁顺序表
//    `_zhs_hooktrace.log`  追踪（触发后的那段）
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>① 把 Harmony 的实际补丁顺序写成表。</summary>
    internal static class ZhsHookProbe
    {
        internal static void DumpPatchInfo()
        {
            if (!ZhsHookTrace.Enabled) return;
            try
            {
                string dir = ZhsTrace.ModDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Harmony 实际补丁顺序（GetPatchInfo）");
                sb.AppendLine("# 生成 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("# 列：类型 | owner | priority | index | 同方法内序号");
                int methods = 0, patches = 0;
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    methods++;
                    sb.AppendLine();
                    sb.AppendLine("## " + (m.DeclaringType != null ? m.DeclaringType.FullName : "?")
                                  + "::" + m.Name);
                    Append(sb, "prefix", info.Prefixes, ref patches);
                    Append(sb, "postfix", info.Postfixes, ref patches);
                    Append(sb, "transpiler", info.Transpilers, ref patches);
                }
                File.WriteAllText(Path.Combine(dir, "_zhs_patchinfo.log"),
                    sb.ToString() + "\n# 方法 " + methods + " 个 · 补丁 " + patches + " 个\n",
                    new UTF8Encoding(false));
                UnityEngine.Debug.Log("[QudZhsCookbook] 钩子顺序表已写：方法 " + methods
                                      + " 个 · 补丁 " + patches + " 个");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 钩子顺序表失败：" + e);
            }
        }

        private static void Append(StringBuilder sb, string kind,
                                   System.Collections.Generic.IEnumerable<Patch> list,
                                   ref int n)
        {
            int i = 0;
            foreach (Patch p in list)
            {
                i++;
                n++;
                sb.AppendLine("  " + kind + " | " + p.owner + " | prio=" + p.priority
                              + " | idx=" + p.index + " | #" + i);
            }
        }
    }

    /// <summary>② 运行期钩子顺序追踪（环形缓冲 + 触发串 dump）。</summary>
    internal static class ZhsHookTrace
    {
        internal static bool Enabled;
        private static string _trigger;
        private static string _path;
        private static readonly string[] Ring = new string[4096];
        private static int _pos, _count, _seq, _arm;

        internal static void Init()
        {
            try
            {
                string dir = ZhsTrace.ModDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                Enabled = File.Exists(Path.Combine(dir, "_zhs_hooktrace_on.txt"));
                if (!Enabled) return;
                _path = Path.Combine(dir, "_zhs_hooktrace.log");
                string tf = Path.Combine(dir, "_zhs_hooktrace_trigger.txt");
                if (File.Exists(tf))
                {
                    string[] lines = File.ReadAllLines(tf);
                    if (lines.Length > 0 && lines[0].Trim().Length > 0)
                        _trigger = lines[0].Trim();
                }
                File.WriteAllText(_path,
                    "=== 钩子顺序追踪 " + DateTime.Now.ToString("HH:mm:ss")
                    + " · trigger=" + (_trigger ?? "(none)") + " ===\n",
                    new UTF8Encoding(false));
                UnityEngine.Debug.Log("[QudZhsCookbook] 钩子追踪已启用 · trigger="
                                      + (_trigger ?? "(none)"));
            }
            catch { }
        }

        internal static void Step(string tag, string text)
        {
            if (!Enabled) return;
            try
            {
                _seq++;
                string t = text ?? "";
                t = t.Replace("\n", "\\n");
                if (t.Length > 90) t = t.Substring(0, 90);
                string line = _seq + " [" + System.Threading.Thread.CurrentThread.ManagedThreadId
                              + "] " + tag + " | " + t;
                Ring[_pos] = line;
                _pos = (_pos + 1) % Ring.Length;
                if (_count < Ring.Length) _count++;

                bool hit = _trigger != null && text != null
                           && text.IndexOf(_trigger, StringComparison.Ordinal) >= 0;
                if (hit)
                {
                    DumpRing();
                    _arm = 200;
                }
                else if (_arm > 0)
                {
                    _arm--;
                    File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private static void DumpRing()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("── 触发 · 前 " + _count + " 步 ──");
                int start = (_count == Ring.Length) ? _pos : 0;
                for (int i = 0; i < _count; i++)
                {
                    int k = (start + i) % Ring.Length;
                    if (Ring[k] != null) sb.AppendLine(Ring[k]);
                }
                File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
