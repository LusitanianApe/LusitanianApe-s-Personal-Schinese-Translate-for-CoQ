// ═══════════════════════════════════════════════════════════════════════════
// 诊断文件的**落盘位置**解析
// ═══════════════════════════════════════════════════════════════════════════
//
// 【为什么单独一个文件】
//   本项目有**至少三处**要往磁盘写诊断文件：
//     · `ZhsEffectDetailsPatch` 的 `_zhs_effectdetails_dump.txt`
//     · `ZhsTombstoneLayout`     的 `_zhs_tomb_raw.log`
//     · `ZhsPlaqueWidthPatch`    的 `_zhs_plaque_over.log`
//   它们原先**各自复制**了一份「从程序集位置往上找 `manifest.json`」的实现。
//
// 【那套实现为什么必然失败】
//   ★ **本项目没有 DLL**。游戏在运行时直接从 `Scripts\*.cs` **现场编译**，
//     产物落在 Unity 的临时目录 ⇒ `Assembly.GetExecutingAssembly().Location`
//     的父级链上**永远没有** `manifest.json` ⇒ 三个诊断文件**从来没产出过**。
//   实证（2026-10-09）：日志打了「已挂 142 个 GetDetails 出口」，
//     却既没有 dump 成功日志、也没有 dump 失败日志
//     —— 因为旧代码是 `if (modDir == null) return;`，**静默吞掉**。
//
// 【修法】
//   ① 不再依赖程序集位置；改用**多条候选路径**（任一可写即可）
//   ② **先打日志再写文件** —— 写不成功也必须留下痕迹
//   ③ 每个候选目录都试 `CreateDirectory` + 写入
//
// ⚠ 教学：「挂了但没动静」时，先看有没有**静默 early-return**
//    （本项目第 53 条同族：早退分支每条都要留痕）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsFileUtil
    {
        private static List<string> _dirs;
        private static string _ok;

        /// <summary>已确认可写的目录（null = 都不可写）。</summary>
        internal static string WriteDir
        {
            get
            {
                if (_ok != null) return _ok;
                foreach (string d in Dirs())
                {
                    try
                    {
                        Directory.CreateDirectory(d);
                        string probe = Path.Combine(d, "_zhs_writetest.tmp");
                        File.WriteAllText(probe, "ok");
                        File.Delete(probe);
                        _ok = d;
                        return _ok;
                    }
                    catch { }
                }
                return null;
            }
        }

        /// <summary>候选目录（按优先级）。</summary>
        internal static List<string> Dirs()
        {
            if (_dirs != null) return _dirs;
            var list = new List<string>();
            try { list.Add(Application.persistentDataPath); } catch { }
            try
            {
                string up = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrEmpty(up))
                {
                    string coq = Path.Combine(up, "AppData", "LocalLow",
                        "Freehold Games", "CavesOfQud");
                    list.Add(Path.Combine(coq, "Mods", "QudZhsCookbook"));
                    list.Add(coq);
                }
            }
            catch { }
            try
            {
                // 程序集位置（万一将来真有了 DLL，这条仍然有效）
                string asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                    list.Add(Path.GetDirectoryName(asm));
            }
            catch { }
            try { list.Add(Path.GetTempPath()); } catch { }

            var outp = new List<string>();
            foreach (string d in list)
            {
                try
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    string f = Path.GetFullPath(d);
                    if (!outp.Contains(f)) outp.Add(f);
                }
                catch { }
            }
            _dirs = outp;
            return _dirs;
        }

        /// <summary>
        /// 把内容写到 `fileName`（在每个候选目录里依次尝试，成功即止）。
        /// **无论成败都打一条日志**（不再静默失败）。
        /// </summary>
        internal static void Write(string tag, string fileName, string content)
        {
            try
            {
                foreach (string d in Dirs())
                {
                    try
                    {
                        Directory.CreateDirectory(d);
                        string p = Path.Combine(d, fileName);
                        File.WriteAllText(p, content, new System.Text.UTF8Encoding(false));
                        Debug.Log(tag + "已写入 " + p);
                        return;
                    }
                    catch { }
                }
                Debug.LogError(tag + "写入失败：所有候选目录都不可写（"
                    + string.Join(" ; ", Dirs().ToArray()) + "）");
            }
            catch (Exception e)
            {
                Debug.LogError(tag + "写入异常：" + e);
            }
        }
    }
}
