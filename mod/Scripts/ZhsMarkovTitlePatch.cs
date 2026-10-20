// 随机（马尔科夫）书籍的**标题**接管。
//
// 为什么单独做标题：
//   * 存档实证：书名会写进存档（对象 + 三条阅读纪录），书页不写盘（本次存档 BookPageInfo / Pages 均为 0 处）；
//   * 所以正文会自动跟随语料/seed 重新生成，而**标题必须主动改写**才有中文；
//   * 在"读/生成书页"这两个必经动作上打 Postfix，把父对象的显示名换成中文标题：
//       - 新书：创建时就拿到中文标题；
//       - 旧书：你下次查看/阅读它时被改写，并在存档时保留（和食谱书名落盘的机制一样）。
//
// 安全性：只挂在名字含 Markov 且含 Book 的类型上；写入前用 HasCjk 判重（幂等），
//         并保留原有的颜色/着色器包裹。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class MarkovTitlePatch
    {
        // 追踪实证：正常书籍生成的调用路径是 MarkovBook.SetContents（600 次），
        // wish 出来的是 Wishing.HandleWish（9087 次）；GeneratePages 只被追踪到挂载、调用很少。
        // 所以标题接管必须同时挂 SetContents，否则常规途径生成的书不会被改写。
        private static readonly string[] MethodNames = { "GeneratePages", "SetContents", "OnCreated", "GenerateTitle" };

        // HandleEvent 只在**读/看这本书**的事件上挂。原因（Player.log 实证）：
        //   MarkovBook / StandaloneMarkovBook 上有几百个泛型接口实现 HandleEvent(TEvent)，
        //   逐个尝试 patch 会为每个事件类型抛一次 "You can only patch implemented methods"，
        //   把 Player.log 刷到 536 KB。
        // 事件名取自实测栈：MarkovBook.HandleEvent(XRL.World.InventoryActionEvent)。
        private static readonly string[] AllowedEventTypes =
        {
            "InventoryActionEvent", "HasBeenReadEvent", "ObjectCreatedEvent", "BeforeRenderEvent"
        };

        private static bool IsAllowedMethod(MethodInfo mi)
        {
            foreach (string n in MethodNames) if (mi.Name == n) return true;
            if (mi.Name != "HandleEvent") return false;
            ParameterInfo[] ps = mi.GetParameters();
            if (ps.Length != 1) return false;
            string tn = ps[0].ParameterType.Name;
            foreach (string allowed in AllowedEventTypes) if (tn == allowed) return true;
            return false;
        }

        public static void TryApply()
        {
            MethodInfo postfix = typeof(MarkovTitlePatch).GetMethod("Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix == null) return;

            List<Type> types = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    List<Type> ok = new List<Type>();
                    foreach (Type t in ex.Types) if (t != null) ok.Add(t);
                    all = ok.ToArray();
                }
                catch { continue; }

                foreach (Type t in all)
                {
                    if (t.FullName == null) continue;
                    if (t.FullName.StartsWith("Nattens.", StringComparison.Ordinal)) continue;
                    if (t.FullName.IndexOf("Markov", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (t.FullName.IndexOf("Book", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    types.Add(t);
                }
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] 标题接管：候选书籍部件 " + types.Count + " 个");
            if (types.Count == 0) return;

            int applied = 0, skipped = 0;
            foreach (Type t in types)
            {
                Harmony h = new Harmony("Nattens.QudZhsCookbook.markovtitle." + t.Name);
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name != "HandleEvent" && mi.Name != "GeneratePages" && mi.Name != "SetContents"
                        && mi.Name != "OnCreated" && mi.Name != "GenerateTitle")
                        continue;
                    if (!IsAllowedMethod(mi)) { skipped++; continue; }
                    if (mi.IsAbstract) continue;
                    if (mi.DeclaringType != null && mi.DeclaringType.IsInterface) continue;
                    if (mi.ContainsGenericParameters) continue;
                    try
                    {
                        h.Patch(mi, postfix: new HarmonyMethod(postfix));
                        applied++;
                        UnityEngine.Debug.Log("[QudZhsCookbook] 标题接管已挂：" + t.FullName + "."
                            + mi.Name + "(" + (mi.GetParameters().Length == 1 ? mi.GetParameters()[0].ParameterType.Name : "") + ")");
                    }
                    catch (Exception e)
                    {
                        // 只记一条，不刷屏
                        UnityEngine.Debug.Log("[QudZhsCookbook] 标题接管跳过 " + t.FullName + "." + mi.Name + "：" + e.Message);
                    }
                }
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] 标题接管共挂上 " + applied + " 个方法（按事件类型过滤跳过 " + skipped + " 个 HandleEvent 重载）");
        }

        private static void Postfix(object __instance)
        {
            try { Apply(__instance); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 标题接管异常：" + e); }
        }

        private static void Apply(object part)
        {
            if (part == null) return;
            object parent = ReadMember(part, "ParentObject");
            if (parent == null) return;

            object render = GetRender(parent);
            string current = ReadString(parent, "DisplayName");
            if (string.IsNullOrEmpty(current) && render != null) current = ReadString(render, "DisplayName");
            // ---- 任务1：书名装饰的自愈处理 ----
            // 现象：存档里出现过 `All Three Cases and Consequently: Unabridged: Unabridged`。
            // 机制（已定位）：书名存在部件字段 BookTitle 里，装饰步骤是"读 BookTitle →
            //   套 MarkovBook Decorate Title - (Unabridged)/(Volume #Roman)/(n'th edition) 三条
            //   字符串表模板（内含 =volume.roman= / =edition.ordinal.digits= 替换器）→ 写回"。
            //   它**没有幂等保护**：已经是 `…: Unabridged` 的书再走一次就变成 `…: Unabridged: Unabridged`。
            //   我们自己的中文标题也会被追加（存档实证：中文文本后紧跟 `: Unabridged: Unabridged`）。
            // 对策：既然我们无法拦装饰步骤（元数据里根本没有装饰方法，它是字符串模板 + 变量替换），
            //   就在每次接管时把**重复装饰全部剥掉**，并把干净的标题同步写回 BookTitle，
            //   使后续再装饰时从干净标题出发。
            List<string> decoWrappers = new List<string>();
            string plain = Markup.Unwrap(current, decoWrappers);
            string strippedPlain = StripDecorations(plain);
            if (!string.IsNullOrEmpty(strippedPlain) && strippedPlain != plain)
            {
                string cleaned = Markup.Rewrap(strippedPlain, decoWrappers);
                WriteTitle(part, parent, render, cleaned);
                UnityEngine.Debug.Log("[QudZhsCookbook] 书名装饰清理：" + current + "  →  " + cleaned);
                current = cleaned;
            }

            if (Markup.HasCjk(current)) return; // 已是中文（且已清理装饰）→ 不再改写

            string title = ZhsBookText.Title();
            if (string.IsNullOrEmpty(title)) return;

            // 保留原有的颜色/着色器包裹
            List<string> wrappers = new List<string>();
            Markup.Unwrap(current, wrappers);
            string final = Markup.Rewrap(title, wrappers);

            WriteTitle(part, parent, render, final);
        }

        /// <summary>装饰模板产生的后缀（可重复出现，要循环剥）。
        /// 英文模式匹配未汉化时的 `, Vol. III` / `: Unabridged` / `, 2nd Edition`；
        /// 中文模式匹配我们把装饰模板汉化后的 `，第III卷` / `：未删节版` / `，第2版`
        /// —— 两条都要有，否则汉化装饰后又会重新叠加。</summary>
        private static readonly Regex DecorationRe = new Regex(
            @"(?::\s*Unabridged|,\s*Vol\.\s*(?:[IVXLCDM]+|\d+)|,\s*\d+(?:st|nd|rd|th)?\s+Edition"
            + @"|：\s*未删节版|，\s*第\s*(?:[IVXLCDM]+|\d+)\s*卷|，\s*第\s*\d+(?:st|nd|rd|th)?\s*版)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>循环剥掉尾部装饰（处理 ": Unabridged: Unabridged" 这类叠加）。</summary>
        private static string StripDecorations(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string prev;
            int guard = 0;
            do
            {
                prev = s;
                s = DecorationRe.Replace(s, "").TrimEnd();
            } while (s != prev && s.Length > 0 && ++guard < 8);
            return s;
        }

        /// <summary>把书名同时写进 Render.DisplayName 与部件字段 BookTitle。</summary>
        private static void WriteTitle(object part, object parent, object render, string title)
        {
            bool ok = false;
            if (render != null && WriteMember(render, "DisplayName", title)) ok = true;
            if (WriteMember(parent, "DisplayName", title)) ok = true;
            // BookTitle 是装饰步骤的读取源，必须同步，否则下一次装饰会从旧值出发
            if (WriteMember(part, "BookTitle", title)) ok = true;
            if (!ok)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 未能写入书名（DisplayName / BookTitle 都没找到）。");
            }
            LogTitleDiag(part, parent, render, title);
        }

        // ---- 书名诊断（临时）----
        // 现象：书内正文已是中文，但消息日志（You read =title=.）里的书名仍是英文且与中文名不一致。
        // 需要先弄清 `=title=` 到底取自哪个成员。这里把"写入后"各候选成员的实际值打出来，
        // 再和消息日志里显示的书名对照，就能定位。
        private static int _titleDiag;
        private static readonly object DiagGate = new object();

        private static void LogTitleDiag(object part, object parent, object render, string written)
        {
            lock (DiagGate)
            {
                if (_titleDiag++ >= 15) return;
            }
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[QudZhsCookbook] 书名诊断 #").Append(_titleDiag).Append("：写入=").Append(written);
                sb.Append("  |  part=").Append(part != null ? part.GetType().Name : "null");
                if (part != null)
                {
                    object bt = ReadMember(part, "BookTitle");
                    sb.Append("  part.BookTitle=").Append(bt == null ? "<无此字段>" : bt.ToString());
                }
                sb.Append("  |  parent.DisplayName=")
                  .Append(parent != null ? Str(ReadString(parent, "DisplayName")) : "null");
                if (render != null)
                    sb.Append("  render.DisplayName=").Append(Str(ReadString(render, "DisplayName")));
                UnityEngine.Debug.Log(sb.ToString());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 书名诊断失败：" + e.Message);
            }
        }

        private static string Str(string s)
        {
            if (s == null) return "<null>";
            return "<" + (s.Length > 60 ? s.Substring(0, 60) + "…" : s) + ">";
        }

        // ---------- 反射小工具 ----------

        private static object GetRender(object parent)
        {
            try
            {
                foreach (MethodInfo mi in parent.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name == "GetPart" && mi.GetParameters().Length == 1)
                    {
                        object p = mi.Invoke(parent, new object[] { "Render" });
                        if (p != null) return p;
                    }
                }
            }
            catch { }
            return ReadMember(parent, "Render");
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
                if (f != null && f.FieldType == typeof(string)) { f.SetValue(obj, value); return true; }
                PropertyInfo p = mi as PropertyInfo;
                if (p != null && p.CanWrite && p.PropertyType == typeof(string)) { p.SetValue(obj, value, null); return true; }
            }
            catch { }
            return false;
        }
    }
}
