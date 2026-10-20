// QudZhsCookbook —— 食谱书名的中文马尔科夫链（部件接管）
//
// 设计要点：
//   1. 中文马尔科夫链：字级 order-N 链，语料内嵌在 ZhsCookbookCorpus.cs（由工具生成，勿手改）。
//   2. 部件接管：给 XRL.World.Parts.Cookbook 的命名方法打 Harmony Postfix，
//      在它生成完英文书名之后，用中文链生成的名字覆盖 Render.DisplayName。
//   3. 全反射写入：不假设 Render.DisplayName / ParentObject 的确切签名，
//      逐个尝试多种写法，失败只记日志、不影响游戏运行。
//
// 说明：游戏会在载入模组时用 Roslyn 编译本目录下的 .cs 文件，并自动应用 Harmony 属性补丁，
//      因此不需要预先编译成 dll。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>中文食谱书名生成器：字级马尔科夫链 + 中文书名骨架约束。</summary>
    public static class ZhsTitleGenerator
    {
        public const int MinLen = 5;
        public const int MaxLen = 12;
        private const double SpliceChance = 0.30;

        private static readonly HashSet<char> SentenceEnd = new HashSet<char>("。！？…".ToCharArray());
        private static readonly HashSet<char> Punct = new HashSet<char>("，、；：？！…—～·「」『』（）〈〉《》【】“”‘’".ToCharArray());
        private static readonly HashSet<char> Dangling = new HashSet<char>("的与和之里中以在为到把被跟同就也都很更最又而且".ToCharArray());

        private static readonly object Gate = new object();
        private static readonly System.Random Rng = new System.Random(unchecked(Environment.TickCount * 31 + 17));
        private static Dictionary<string, List<string>> _chain;

        /// <summary>构建语料链（首次调用时构建，并打印样例书名到 Player.log 便于验证）。</summary>
        public static void EnsureBuilt()
        {
            lock (Gate)
            {
                if (_chain != null) return;
                _chain = Build();
                UnityEngine.Debug.Log("[QudZhsCookbook] 中文书名链构建完成：上下文键 " + _chain.Count
                    + " 个 / 语料 " + ZhsCookbookCorpus.Lines.Length + " 句 / order=" + ZhsCookbookCorpus.Order);
                for (int i = 0; i < 5; i++)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 样例书名：" + NextTitle());
                }
            }
        }

        private static List<string> Tokenize(string s)
        {
            List<string> toks = new List<string>();
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (Punct.Contains(ch) || SentenceEnd.Contains(ch))
                {
                    if (toks.Count > 0) toks[toks.Count - 1] = toks[toks.Count - 1] + ch;
                    else toks.Add(ch.ToString());
                }
                else
                {
                    toks.Add(ch.ToString());
                }
            }
            return toks;
        }

        private static Dictionary<string, List<string>> Build()
        {
            int order = ZhsCookbookCorpus.Order;
            Dictionary<string, List<string>> chain = new Dictionary<string, List<string>>();
            foreach (string raw in ZhsCookbookCorpus.Lines)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                List<string> toks = Tokenize(raw.Trim());
                for (int i = 0; i < toks.Count; i++)
                {
                    List<string> ctx = new List<string>();
                    for (int j = Math.Max(0, i - order); j < i; j++) ctx.Add(toks[j]);
                    while (ctx.Count < order) ctx.Insert(0, "");
                    string key = string.Join(" ", ctx.ToArray());
                    List<string> bucket;
                    if (!chain.TryGetValue(key, out bucket))
                    {
                        bucket = new List<string>();
                        chain[key] = bucket;
                    }
                    bucket.Add(toks[i]);
                }
            }
            return chain;
        }

        /// <summary>从"句首上下文"起跳，沿链游走 lo~hi 个字符。</summary>
        private static string Walk(int lo, int hi, int maxSteps)
        {
            if (_chain == null) return null;
            int order = ZhsCookbookCorpus.Order;
            List<string> ctx = new List<string>();
            for (int i = 0; i < order; i++) ctx.Add("");
            List<string> outToks = new List<string>();
            int len = 0;
            for (int step = 0; step < maxSteps; step++)
            {
                List<string> cands;
                if (!_chain.TryGetValue(string.Join(" ", ctx.ToArray()), out cands) || cands.Count == 0) break;
                string nxt = cands[Rng.Next(cands.Count)];
                if (len > 0 && len + nxt.Length > hi) break;
                outToks.Add(nxt);
                len += nxt.Length;
                if (nxt.Length > 0 && SentenceEnd.Contains(nxt[nxt.Length - 1])) break;
                if (len >= lo && len + 1 > hi) break;
                ctx.Add(nxt);
                while (ctx.Count > order) ctx.RemoveAt(0);
            }
            return Clean(string.Concat(outToks.ToArray()));
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("。", "").Replace("！", "").Replace("？", "").Replace("…", "");
            return s.Trim('，', '、', '；', '：', ' ');
        }

        /// <summary>尾字挂空（以"的/与/和"等结尾）就继续延伸；无法修好则返回 null。</summary>
        private static string Polish(string s)
        {
            for (int i = 0; i < 3 && !string.IsNullOrEmpty(s) && Dangling.Contains(s[s.Length - 1]); i++)
            {
                string tail = Walk(1, 2, 3);
                if (string.IsNullOrEmpty(tail)) break;
                s = s + tail;
            }
            if (string.IsNullOrEmpty(s) || Dangling.Contains(s[s.Length - 1])) return null;
            return s;
        }

        /// <summary>生成一个中文书名。
        /// 风格约定（用户要求）：**不带书名号《》、不带句末标点** —— 因为游戏自己会给书名加
        /// 装饰后缀（`, Vol. III` / `: Unabridged` / `, 2nd Edition`），外面再套《》会变成
        /// 《…》: Unabridged 这种夹杂样式。</summary>
        public static string NextTitle()
        {
            EnsureBuilt();
            for (int attempt = 0; attempt < 80; attempt++)
            {
                string s;
                if (Rng.NextDouble() < (1.0 - SpliceChance))
                {
                    s = Polish(Walk(MinLen, MaxLen, 20));
                }
                else
                {
                    string a = Walk(3, 5, 6);
                    string b = Walk(3, 5, 6);
                    bool ok = !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                              && a.Length >= 3 && b.Length >= 3
                              && a.IndexOf('的') < 0 && b.IndexOf('的') < 0
                              && !Dangling.Contains(a[a.Length - 1]) && !Dangling.Contains(b[b.Length - 1])
                              && (a.Length + 1 + b.Length) <= 11;
                    s = ok ? a + "的" + b : null;
                }
                if (string.IsNullOrEmpty(s)) continue;
                if (s.Length < MinLen || s.Length > MaxLen + 2) continue;
                if (s.IndexOf('。') >= 0 || s.IndexOf('，') >= 0 || s.IndexOf('、') >= 0
                    || s.IndexOf('；') >= 0 || s.IndexOf('：') >= 0) continue;
                return s;
            }
            return null;
        }
    }

    /// <summary>把生成的中文书名写进对象的显示名（全反射，容错）。</summary>
    internal static class NameApplier
    {
        private static bool _warned;

        public static void Apply(object cookbookPart)
        {
            if (cookbookPart == null) return;
            object parent = ReadMember(cookbookPart, "ParentObject");
            if (parent == null) return;

            object render = GetRender(parent);
            string current = ReadString(parent, "DisplayName");
            if (string.IsNullOrEmpty(current) && render != null) current = ReadString(render, "DisplayName");

            string title = ZhsTitleGenerator.NextTitle();
            if (string.IsNullOrEmpty(title)) return;

            // 保留原有的颜色/着色器包裹：先剥掉拿到纯文本（用来判断厨师前缀），改完再按原顺序包回去。
            // 书名的原模板是 {{g|=name|title=}}，不这么做就会丢掉绿色。
            List<string> wrappers = new List<string>();
            string plain = Markup.Unwrap(current, wrappers);

            string chef = ExtractChef(plain);
            string body = string.IsNullOrEmpty(chef) ? title : chef + "师傅的" + title;
            string final = Markup.Rewrap(body, wrappers);

            if (!WriteName(render, parent, final) && !_warned)
            {
                _warned = true;
                UnityEngine.Debug.LogError("[QudZhsCookbook] 未能写入 DisplayName，书名保持原样。请把 Player.log 反馈给作者。");
            }
        }

        private static bool WriteName(object render, object parent, string value)
        {
            if (render != null && WriteMember(render, "DisplayName", value)) return true;
            if (WriteMember(parent, "DisplayName", value)) return true;
            try
            {
                MethodInfo mi = FindMethod(parent.GetType(), "SetStringProperty", 2);
                if (mi != null)
                {
                    mi.Invoke(parent, new object[] { "DisplayName", value });
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static object GetRender(object parent)
        {
            try
            {
                MethodInfo mi = FindMethod(parent.GetType(), "GetPart", 1);
                if (mi != null)
                {
                    object p = mi.Invoke(parent, new object[] { "Render" });
                    if (p != null) return p;
                }
            }
            catch { }
            return ReadMember(parent, "Render");
        }

        /// <summary>从 "Chef Banks's ..." 里取出厨师名，取不到就返回 null。</summary>
        private static string ExtractChef(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Match m = Regex.Match(name, @"^Chef\s+(.+?)[’']s\s");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static MemberInfo FindMember(Type t, string name)
        {
            for (Type cur = t; cur != null; cur = cur.BaseType)
            {
                FieldInfo f = cur.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                PropertyInfo p = cur.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) return p;
            }
            return null;
        }

        private static object ReadMember(object obj, string name)
        {
            if (obj == null) return null;
            MemberInfo mi = FindMember(obj.GetType(), name);
            if (mi == null) return null;
            try
            {
                FieldInfo f = mi as FieldInfo;
                if (f != null) return f.GetValue(obj);
                PropertyInfo p = mi as PropertyInfo;
                if (p != null && p.CanRead) return p.GetValue(obj, null);
            }
            catch { }
            return null;
        }

        private static string ReadString(object obj, string name)
        {
            return ReadMember(obj, name) as string;
        }

        private static bool WriteMember(object obj, string name, string value)
        {
            if (obj == null) return false;
            MemberInfo mi = FindMember(obj.GetType(), name);
            if (mi == null) return false;
            try
            {
                FieldInfo f = mi as FieldInfo;
                if (f != null && f.FieldType == typeof(string))
                {
                    f.SetValue(obj, value);
                    return true;
                }
                PropertyInfo p = mi as PropertyInfo;
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(obj, value, null);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static MethodInfo FindMethod(Type t, string name, int argCount)
        {
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mi.Name == name && mi.GetParameters().Length == argCount) return mi;
            }
            return null;
        }
    }

    /// <summary>
    /// 部件接管入口：给 Cookbook.GenerateCookbook 打 Postfix，
    /// 在它生成完书名之后用中文链覆盖显示名。
    /// </summary>
    [HarmonyPatch(typeof(XRL.World.Parts.Cookbook))]
    [HarmonyPatch("GenerateCookbook")]
    public static class CookbookGeneratePatch
    {
        // 守卫：绝不抛异常（PatchAll 遇异常即中断，会连累后面所有补丁）
        [HarmonyPrepare]
        public static bool Prepare() { return ZhsBootstrap.HasMethod(typeof(XRL.World.Parts.Cookbook), "GenerateCookbook"); }

        static CookbookGeneratePatch()
        {
            // 这里的初始化已移到 ZhsBootstrap（由开局钩子触发）。
            // 原因：Harmony 只挂补丁、不执行静态构造函数；把初始化写在这里会导致
            // "只有生成食谱时才会挂载马尔科夫补丁"。这里只作为**第三重保险**保留一次调用。
            UnityEngine.Debug.Log("[QudZhsCookbook] 食谱补丁类已加载（备用引导触发点）");
            try { ZhsBootstrap.Init("CookbookPatch.cctor"); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 备用引导失败：" + e); }
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            try { NameApplier.Apply(__instance); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 书名接管异常：" + e); }
        }
    }

    /// <summary>
    /// 兜底：万一属性补丁的目标方法名在新版本里改了，就按候选方法名再做一次反射补丁。
    /// 不查询既有补丁信息（不同 Harmony 版本的类型名不一致），而是靠幂等：同一个方法最多补一次，
    /// 即使与属性补丁重复，也只是把书名再覆盖一遍，无副作用。
    /// </summary>
    internal static class FallbackPatch
    {        private static readonly string[] Candidates =
        {
            // ★★ 2026-10-15（待办 C3）：**去掉 `SetName`**。
            //   离线探针实测（`roslyntest -- naming`）：
            //     `XRL.World.IPart` 是**类、不是接口**（`接口=False`），`SetName` 有方法体
            //     ⇒ 2.16.91 那条「接口声明的方法跳过」的守卫（`DeclaringType.IsInterface`）
            //       **从未触发过** ⇒ 它一路走到 `harmony.Patch` 才被 Harmony 拒
            //       （`You can only patch implemented methods/constructors. Patch the declared
            //         method System.Void XRL.World.IPart::SetName(System.String newName)`）
            //       ⇒ 每局刷一条 LogError（噪音）
            //   ★ 真正干活的是**第一个候选**：`已通过兜底路径补丁：…Cookbook.GenerateCookbook` ✅
            //   ⇒ 直接把 `SetName` 从候选表移除（不再试、不再报错）
            "GenerateCookbook", "GenerateName", "MakeName", "CreateName"
        };

        private static readonly HashSet<string> Patched = new HashSet<string>();
        private static readonly object Gate = new object();

        public static void TryApply()
        {
            Type t = typeof(XRL.World.Parts.Cookbook);
            MethodInfo postfixMethod = typeof(CookbookGeneratePatch).GetMethod(
                "Postfix", BindingFlags.Public | BindingFlags.Static);
            if (postfixMethod == null) return;

            Harmony harmony = new Harmony("Nattens.QudZhsCookbook.fallback");
            HarmonyMethod postfix = new HarmonyMethod(postfixMethod);

            foreach (string name in Candidates)
            {
                lock (Gate)
                {
                    if (Patched.Contains(name)) continue;
                    Patched.Add(name);
                }
                MethodInfo mi = AccessTools.Method(t, name);
                if (mi == null) continue;
                // 接口/抽象声明的方法不能打补丁。上一版在 IPart.SetName 上抛
                // "You can only patch implemented methods" 异常，把整个兜底循环中断了，
                // 后面的候选名（MakeName / CreateName）根本没轮到。
                // ★★ 2.16.91：**接口声明的方法直接跳过**（不试、不报错）。
                //   实证：日志每次刷一条
                //     `兜底补丁 SetName 失败（继续尝试其它候选）：You can only patch
                //      implemented methods/constructors. Patch the declared method
                //      System.Void XRL.World.IPart::SetName(System.String newName)`
                //   而真正干活的是**第一个候选**：
                //     `已通过兜底路径补丁：XRL.World.Parts.Cookbook.GenerateCookbook` ✅
                //   ⇒ 业务正常，`SetName` 那条纯属噪音 ⇒ 提前跳过，不再打 LogError。
                if (mi.IsAbstract) continue;
                if (mi.DeclaringType != null && mi.DeclaringType.IsInterface)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 兜底候选 " + name
                        + " 是接口声明的方法，按设计跳过（非故障）");
                    continue;
                }
                if (mi.ContainsGenericParameters) continue;
                try
                {
                    harmony.Patch(mi, postfix: postfix);
                    UnityEngine.Debug.Log("[QudZhsCookbook] 已通过兜底路径补丁：" + t.FullName + "." + name);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 兜底补丁 " + name
                        + " 失败（继续尝试其它候选）：" + e.Message);
                }
            }
        }
    }
}
