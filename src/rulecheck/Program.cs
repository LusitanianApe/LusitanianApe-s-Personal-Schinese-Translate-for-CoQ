// ─────────────────────────────────────────────────────────────────────────────
// RuleCheck —— **离线跑真实规则**（不启动游戏、不进 Unity）
//
// ★ 核心思想：规则是纯字符串函数 ⇒ 只要把**游戏里那一份 IL** 加载进来，
//   就能在控制台里成千上万次地跑它，不用人肉进游戏试。
//
// ★ 加载方式：**反射**加载 `mods\_compilecheck\bin\Release\net8.0\QudZhsCookbook.dll`
//   （这是 `dotnet build mods\_compilecheck` 的产物，与游戏里跑的源码同源）
//   依赖程序集统一从游戏的 `CoQ_Data\Managed\` 解析。
//
// ★ 两种模式
//   ① fixtures.tsv（默认）  每行 `英文<TAB>期望中文`；期望留空 ⇒ 只查英文残留
//   ② --residue             corpus.txt 每行一句英文；只报**译完仍含拉丁字母**的
//
// ★ 英文残留判定：先剥掉 `{{标记}}` / `=槽=` / `[方括号]` / URL，
//   再看是否还有 `[A-Za-z]{3,}` 的连续段 —— 有就是一处在漏。
//
// 用法：
//   dotnet run -c Release --project _tools\rulecheck -- _tools\_msg_fixtures.tsv
//   dotnet run -c Release --project _tools\rulecheck -- _tools\_msg_corpus.txt --residue
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

internal static class RuleCheckProgram
{
    private static readonly string ManagedDir =
        Environment.GetEnvironmentVariable("COQ_MANAGED")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\CoQ_Data\Managed";

    private static readonly Regex Markup =
        new Regex(@"\{\{.*?\}\}|=[^=\s]+=|\[[^\]]*\]|https?://\S+", RegexOptions.Compiled);

    private static readonly Regex Latin = new Regex("[A-Za-z]{3,}", RegexOptions.Compiled);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine("用法：rulecheck <fixtures.tsv|corpus.txt> [--residue] [--verbose]");
            return 2;
        }

        string path = args[0];
        bool residue = args.Contains("--residue");
        bool verbose = args.Contains("--verbose");

        if (!File.Exists(path))
        {
            Console.WriteLine("找不到文件：" + path);
            return 2;
        }

        // ── 依赖解析：一切游戏程序集都从 Managed\ 找 ──
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(ManagedDir, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };

        Func<string, string> translate = LoadTranslator();

        // ── --trace：把一条输入逐规则过一遍，报「哪几条正则命中」──
        if (args.Contains("--trace"))
        {
            string line = File.ReadAllText(path, Encoding.UTF8).Split('\n')[0].TrimEnd('\r');
            TraceRules(line);
            return 0;
        }

        if (translate == null) return 3;

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        int total = 0, changed = 0, hit = 0, miss = 0, err = 0, resid = 0;
        var bad = new List<string>();
        var residSamples = new List<string>();

        foreach (string raw in lines)
        {
            if (raw.Length == 0 || raw.StartsWith("#")) continue;

            string en = raw, expect = null;
            if (!residue)
            {
                int t = raw.IndexOf('\t');
                if (t >= 0) { en = raw.Substring(0, t); expect = raw.Substring(t + 1).Trim(); }
            }
            en = en.Trim();
            if (en.Length == 0) continue;

            total++;
            string got;
            try { got = translate(en); }
            catch (Exception e)
            {
                err++;
                if (bad.Count < 25)
                    bad.Add("EX   " + en + "\n     " + e.GetType().Name + ": " + e.Message);
                continue;
            }

            if (got != en) changed++;

            if (!string.IsNullOrEmpty(expect))
            {
                if (got == expect) hit++;
                else
                {
                    miss++;
                    if (bad.Count < 25)
                        bad.Add("MISS\n     英: " + en + "\n     期: " + expect + "\n     实: " + got);
                }
                continue;
            }

            string probe = Markup.Replace(got, " ");
            Match m = Latin.Match(probe);
            if (m.Success)
            {
                resid++;
                if (residSamples.Count < 40)
                    residSamples.Add("RES  " + en + "\n     → " + got + "\n     ↑残留「" + m.Value + "」");
            }
            else if (verbose)
            {
                Console.WriteLine("OK   " + en + "\n     → " + got);
            }
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine("样本 " + total + " · 有变化 " + changed + " · 调用异常 " + err);
        if (residue) Console.WriteLine("★ 译后仍含英文残留 = " + resid);
        else Console.WriteLine("命中期望 " + hit + " · 不符 " + miss);

        if (bad.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("── 不符/异常样例 ──");
            foreach (string s in bad) Console.WriteLine(s);
        }
        if (residSamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("── 英文残留样例 ──");
            foreach (string s in residSamples) Console.WriteLine(s);
        }
        return resid > 0 ? 1 : 0;
    }

    /// <summary>
    /// `--trace`：逐条规则试一遍，报**哪几条命中**、命中在哪个下标。
    /// ★ 用途：规则写得对、顺序也对，但结果不对时 —— 一眼看出是不是
    ///   「前一条把它吃了」「下标对了但 Value 写错」「根本没进数组」。
    /// </summary>
    private static void TraceRules(string input)
    {
        Assembly asm = LoadDll();
        if (asm == null) return;
        Type t = asm.GetType("Nattens.QudZhsCookbook.ZhsCombatMsgPatch", false);
        if (t == null) { Console.WriteLine("找不到类型"); return; }
        FieldInfo f = t.GetField("Rules", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) { Console.WriteLine("找不到 Rules 字段"); return; }

        var rules = f.GetValue(null) as System.Collections.IEnumerable;
        if (rules == null) { Console.WriteLine("Rules 不是可枚举的"); return; }

        Console.WriteLine("输入：" + input);
        Console.WriteLine("────────────────────────────────────────────────────────────");
        int i = 0, matched = 0;
        Regex inputRx = null;
        foreach (object kv in rules)
        {
            Type kvt = kv.GetType();
            Regex re = (Regex)kvt.GetProperty("Key").GetValue(kv, null);
            string rep = (string)kvt.GetProperty("Value").GetValue(kv, null);
            bool hit = re.IsMatch(input);
            if (hit)
            {
                matched++;
                Console.WriteLine("#" + i + " 命中  " + re + "\n      → " + rep);
                if (inputRx == null) inputRx = re;
            }
            i++;
        }
        Console.WriteLine("────────────────────────────────────────────────────────────");
        Console.WriteLine("规则总数 " + i + " · 命中 " + matched
            + (matched > 0 ? " · **第一条命中的是** " + inputRx : ""));

        // 再串一遍碎片表（它常是「以为没译，其实在下一道」的元凶）
        Type ft = asm.GetType("Nattens.QudZhsCookbook.ZhsFragmentDictTable", false);
        if (ft != null)
        {
            MethodInfo fa = ft.GetMethod("Apply",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (fa != null)
            {
                string after = (string)fa.Invoke(null, new object[] { input });
                Console.WriteLine();
                Console.WriteLine("碎片表后：" + (after == input ? "（无变化）" : after));
            }
        }
    }

    private static Assembly LoadDll()
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(ManagedDir, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        string dll = Path.Combine(AppContext.BaseDirectory, "QudZhsCookbook.dll");
        if (!File.Exists(dll)) { Console.WriteLine("找不到 QudZhsCookbook.dll"); return null; }
        Console.WriteLine("加载：" + dll);
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
    }

    /// <summary>反射拿到 `Nattens.QudZhsCookbook.ZhsCombatMsgPatch.TranslateText`。</summary>
    private static Func<string, string> LoadTranslator()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "QudZhsCookbook.dll");
        if (!File.Exists(dll))
        {
            // 退回工作区里 compilecheck 的产物
            dll = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "mods", "_compilecheck", "bin", "Release", "net8.0", "QudZhsCookbook.dll"));
        }
        if (!File.Exists(dll))
        {
            Console.WriteLine("找不到 QudZhsCookbook.dll —— 先跑：dotnet build mods\\_compilecheck");
            return null;
        }
        Console.WriteLine("加载：" + dll);

        Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        Type t = asm.GetType("Nattens.QudZhsCookbook.ZhsCombatMsgPatch", false);
        if (t == null)
        {
            Console.WriteLine("找不到类型 ZhsCombatMsgPatch");
            return null;
        }
        MethodInfo m = t.GetMethod("TranslateText",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (m == null)
        {
            Console.WriteLine("找不到 TranslateText");
            return null;
        }
        Console.WriteLine("规则入口：TranslateText ✅");
        Func<string, string> ruleFn = en => (string)m.Invoke(null, new object[] { en });

        // ★★ 串上**碎片字典**（`ZhsFragmentDictTable.Apply`）——
        //   真实管线是 `TranslateText`（规则）→ 消息定稿 → `Apply`（碎片兜底）。
        //   只测前者会**假报残留**（例如 `Because they despise` 是碎片表的活）。
        Type ft = asm.GetType("Nattens.QudZhsCookbook.ZhsFragmentDictTable", false);
        MethodInfo fa = ft == null ? null : ft.GetMethod("Apply",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (fa == null)
        {
            Console.WriteLine("碎片字典：未串（只测规则）");
            return ruleFn;
        }
        Console.WriteLine("碎片字典：ZhsFragmentDictTable.Apply ✅（已串进管线）");
        // ★ `Apply` 在 `Rules.Count == 0` 时**直接放行** ⇒ 必须先 `Load()` 建表。
        //   （离线第一版没调 Load，看起来"碎片表没生效"，其实是空表 —— 这个坑很容易再踩。）
        MethodInfo fl = ft.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (fl != null)
        {
            try { fl.Invoke(null, null); Console.WriteLine("碎片字典：Load() ✅"); }
            catch (Exception e) { Console.WriteLine("碎片字典：Load() 失败 " + e.GetType().Name); }
        }
        // ★★ 三阶段按**真实管线顺序**串：
        //   ① `ZhsJournalNote.Apply`          ← `IBaseJournalEntry.DisplayMessage`（最早）
        //   ② `ZhsCombatMsgPatch.TranslateText` ← 规则表
        //   ③ `ZhsFragmentDictTable.Apply`      ← 消息定稿处兜底
        //   顺序错了会**假报缺陷**（日志提示被规则抢先吃掉，看起来像规则写坏）。
        Type jt = asm.GetType("Nattens.QudZhsCookbook.ZhsJournalNote", false);
        MethodInfo jn = jt == null ? null
            : jt.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine("日志提示：" + (jn == null ? "未串（规则不会经过它）" : "ZhsJournalNote.Apply ✅"));

        return en =>
        {
            string a = en;
            if (jn != null) a = (string)jn.Invoke(null, new object[] { a });
            a = ruleFn(a);
            return (string)fa.Invoke(null, new object[] { a });
        };
    }
}
