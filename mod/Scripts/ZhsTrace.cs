// 运行期追踪（opt-in）。
//
// 打开方式：在模组目录里放一个空文件 _zhs_trace_on.txt，日志写到同目录的 ZhsTrace.log。
// 关掉就删掉那个标记文件。之所以用标记文件而不是写死常量：不用重编译就能开关。
//
// 追三层：
//   1) XRL.MarkovChain 的生成方法（含参数、返回值、**调用者栈**）——解决"到底谁在生成、生成出什么"；
//   2) 书籍部件（MarkovBook / StandaloneMarkovBook / Bookshelf）的 GeneratePages / 阅读事件；
//   3) 文件读取 API（File.ReadAllText / ReadAllBytes / OpenRead / OpenText / Exists、Resources.Load）
//      ——只记录路径里含 StreamingAssets / LibraryCorpus / Books / .xml / Mods 的，避免刷屏。
//
// 注意：这是诊断插桩，正常运行请保持关闭（会显著拖慢并产生大日志）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsTrace
    {
        private const string MarkerName = "_zhs_trace_on.txt";
        private const string LogName = "ZhsTrace.log";
        private const int MaxLines = 20000;

        private static string _dir;
        private static string _logPath;
        private static StreamWriter _writer;
        private static readonly object Gate = new object();
        private static int _lines;
        private static bool _tooMany;

        public static bool Enabled
        {
            get
            {
                try { return _dir != null && File.Exists(Path.Combine(_dir, MarkerName)); }
                catch { return false; }
            }
        }

        public static void SetModDirectory(string dir)
        {
            _dir = dir;
            if (dir != null) _logPath = Path.Combine(dir, LogName);
        }

        /// <summary>模组目录（`SetModDirectory` 设进来的那个）；未设时为 null。
        /// 供其它部件读「开关文件」用（如 `_zhs_tpl_readfirst.txt`）。</summary>
        public static string ModDirectory
        {
            get { return _dir; }
        }

        // ---------- 日志 ----------

        public static void Log(string tag, string message)
        {
            try
            {
                lock (Gate)
                {
                    if (_tooMany) return;
                    if (_writer == null)
                    {
                        if (_logPath == null) return;
                        _writer = new StreamWriter(_logPath, true, new UTF8Encoding(false));
                        _writer.AutoFlush = true;
                    }
                    if (++_lines > MaxLines)
                    {
                        _tooMany = true;
                        _writer.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] …日志超过 " + MaxLines + " 行，停止记录");
                        return;
                    }
                    _writer.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + tag + "] " + message);
                }
            }
            catch { /* 追踪本身绝不能影响游戏 */ }
        }

        /// <summary>取调用者栈（跳过追踪自身的帧）。</summary>
        public static string Caller(int skip)
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(skip, false);
                StringBuilder sb = new StringBuilder();
                int n = 0;
                for (int i = 0; i < st.FrameCount && n < 5; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null) continue;
                    string decl = m.DeclaringType != null ? m.DeclaringType.FullName : "?";
                    if (decl.StartsWith("Nattens.QudZhsCookbook")) continue;
                    if (decl.StartsWith("HarmonyLib")) continue;
                    sb.Append(decl).Append('.').Append(m.Name).Append(" <- ");
                    n++;
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        private static string Short(string s)
        {
            if (s == null) return "null";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length <= 160 ? s : s.Substring(0, 160) + "…(" + s.Length + ")";
        }

        // ---------- 安装 ----------

        public static void Install()
        {
            if (!Enabled)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 追踪未开启（想开启就在模组目录放空文件 " + MarkerName + "）");
                return;
            }
            Log("trace", "=== 追踪开始 ===");
            UnityEngine.Debug.Log("[QudZhsCookbook] 追踪已开启，日志：" + _logPath);

            try { TraceMarkovChain(); } catch (Exception e) { Log("err", "TraceMarkovChain: " + e); }
            try { TraceBookParts(); } catch (Exception e) { Log("err", "TraceBookParts: " + e); }
            try { TraceFileApi(); } catch (Exception e) { Log("err", "TraceFileApi: " + e); }

            // 顺手把已加载的程序集/插件列出来（"调用了哪些插件"）
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = asm.GetName().Name;
                    if (n.StartsWith("System") || n.StartsWith("UnityEngine") || n.StartsWith("mscorlib")
                        || n.StartsWith("Mono.") || n.StartsWith("netstandard")) continue;
                    string loc = "?";
                    try { loc = asm.Location; } catch { }
                    Log("asm", n + "  →  " + loc);
                }
            }
            catch { }
        }

        // ---------- 1) 马尔科夫链 ----------

        private static void TraceMarkovChain()
        {
            Type t = AccessTools.TypeByName("XRL.MarkovChain");
            if (t == null) { Log("err", "找不到 XRL.MarkovChain"); return; }

            MethodInfo pre = typeof(ZhsTrace).GetMethod("ChainPre", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo post = typeof(ZhsTrace).GetMethod("ChainPost", BindingFlags.NonPublic | BindingFlags.Static);
            Harmony h = new Harmony("Nattens.QudZhsCookbook.trace.chain");

            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance | BindingFlags.Static))
            {
                if (mi.ReturnType != typeof(string)) continue;
                if (!mi.Name.StartsWith("Generate", StringComparison.Ordinal)) continue;
                if (mi.IsAbstract || mi.ContainsGenericParameters) continue;
                try
                {
                    h.Patch(mi, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                    Log("chain", "已追踪 " + mi.Name + "(" + (mi.IsStatic ? "static" : "inst") + ")");
                }
                catch (Exception e) { Log("err", "追踪 " + mi.Name + " 失败：" + e.Message); }
            }
        }

        private static void ChainPre(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(__originalMethod.Name).Append('(');
                if (__args != null)
                {
                    for (int i = 0; i < __args.Length && i < 6; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(__args[i] == null ? "null" : Short(__args[i].ToString()));
                    }
                }
                sb.Append(")  ← ").Append(Caller(3));
                Log("chain>", sb.ToString());
            }
            catch { }
        }

        private static void ChainPost(MethodBase __originalMethod, string __result)
        {
            Log("chain<", __originalMethod.Name + " = " + Short(__result));
        }

        // ---------- 2) 书籍部件 ----------

        private static void TraceBookParts()
        {
            MethodInfo pre = typeof(ZhsTrace).GetMethod("PartPre", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo post = typeof(ZhsTrace).GetMethod("PartPost", BindingFlags.NonPublic | BindingFlags.Static);
            string[] typeNames = { "XRL.World.Parts.MarkovBook", "XRL.World.Parts.StandaloneMarkovBook" };
            string[] eventAllow = { "InventoryActionEvent", "HasBeenReadEvent", "ObjectCreatedEvent" };

            foreach (string tn in typeNames)
            {
                Type t = AccessTools.TypeByName(tn);
                if (t == null) { Log("err", "找不到 " + tn); continue; }
                Harmony h = new Harmony("Nattens.QudZhsCookbook.trace." + t.Name);
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    bool want = mi.Name == "GeneratePages" || mi.Name == "GenerateTitle" || mi.Name == "GenerateBook";
                    if (!want && mi.Name == "HandleEvent")
                    {
                        ParameterInfo[] ps = mi.GetParameters();
                        if (ps.Length != 1) continue;
                        string ptn = ps[0].ParameterType.Name;
                        foreach (string a in eventAllow) if (ptn == a) { want = true; break; }
                    }
                    if (!want) continue;
                    if (mi.IsAbstract || (mi.DeclaringType != null && mi.DeclaringType.IsInterface)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                        Log("part", "已追踪 " + tn + "." + mi.Name);
                    }
                    catch (Exception e) { Log("err", "追踪 " + tn + "." + mi.Name + " 失败：" + e.Message); }
                }
            }
        }

        private static void PartPre(object __instance, MethodBase __originalMethod, object[] __args)
        {
            try
            {
                string name = "?";
                try
                {
                    object parent = AccessTools.Field(__instance.GetType(), "ParentObject") != null
                        ? AccessTools.Field(__instance.GetType(), "ParentObject").GetValue(__instance) : null;
                    if (parent != null)
                    {
                        object dn = AccessTools.Property(parent.GetType(), "DisplayName") != null
                            ? AccessTools.Property(parent.GetType(), "DisplayName").GetValue(parent, null) : null;
                        name = dn as string ?? parent.ToString();
                    }
                }
                catch { }
                Log("part>", __originalMethod.DeclaringType.Name + "." + __originalMethod.Name
                    + "  对象=" + Short(name) + "  ← " + Caller(3));
            }
            catch { }
        }

        private static void PartPost(MethodBase __originalMethod)
        {
            Log("part<", __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + " 返回");
        }

        // ---------- 3) 文件读取 ----------

        private static readonly string[] PathFilters =
        { "StreamingAssets", "LibraryCorpus", "Books", "Corpus", ".xml", "Mods", "ModAssemblies" };

        private static bool WantPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            foreach (string f in PathFilters)
                if (p.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void TraceFileApi()
        {
            MethodInfo pre = typeof(ZhsTrace).GetMethod("FilePre", BindingFlags.NonPublic | BindingFlags.Static);
            Harmony h = new Harmony("Nattens.QudZhsCookbook.trace.file");

            string[][] targets = {
                new[] { "System.IO.File", "ReadAllText" },
                new[] { "System.IO.File", "ReadAllBytes" },
                new[] { "System.IO.File", "ReadAllLines" },
                new[] { "System.IO.File", "OpenRead" },
                new[] { "System.IO.File", "OpenText" },
                new[] { "System.IO.File", "Exists" },
                new[] { "System.IO.StreamReader", ".ctor" },
                new[] { "UnityEngine.Resources", "Load" },
            };
            foreach (string[] tg in targets)
            {
                Type t = AccessTools.TypeByName(tg[0]);
                if (t == null) { Log("err", "找不到类型 " + tg[0]); continue; }
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (mi.Name != tg[1]) continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(pre));
                        Log("file", "已追踪 " + tg[0] + "." + tg[1] + "(" + ps.Length + " 参数)");
                    }
                    catch (Exception e) { Log("err", "追踪 " + tg[0] + "." + tg[1] + " 失败：" + e.Message); }
                }
            }
        }

        private static void FilePre(string __0)
        {
            try
            {
                if (!WantPath(__0)) return;
                // 注意：这里只用我们自己打开的 StreamWriter，不再调用被追踪的 File.* 以免递归
                Log("file", Short(__0) + "  ← " + Caller(3));
            }
            catch { }
        }
    }
}
