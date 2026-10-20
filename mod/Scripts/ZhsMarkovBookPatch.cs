// 随机书籍（马尔科夫书籍）正文的部件接管。
//
// 两个接管点，都走运行时反射，对游戏内部签名零编译期依赖：
//
//  A. XRL.MarkovChain 的生成方法（总闸）
//     元数据里 "GenerateFormattedSentence | MarkovCorvidSentence | MarkovWaterBirdSentence |
//     MarkovWeirdSentence | GenerateMarkovMessageSentence | GenerateSentence | MarkovFishSentence |
//     GenerateShortSentence | MarkovSentence | sentence" 是相邻的一簇，
//     说明这些方法同属一个类型（几乎可以确定是 XRL.MarkovChain）。
//     GenerateSentence 返回的是**整句字符串**（不是逐词），所以直接换掉返回值不会出现
//     "天 地 人" 那种被空格拆开的乱象。
//     在这一点上打补丁，会同时覆盖：无限书架的书籍正文、马尔科夫书名、涂鸦、纹身、
//     骨灰瓮铭文、鸟/鱼对话、睡眠心灵感应等所有马尔科夫文本。
//
//  B. MarkovBook / StandaloneMarkovBook 的 GeneratePages（书页兜底）
//     get_Pages / set_Pages / GeneratePages / AutoformatPages 在元数据里相邻，
//     说明 Pages 是书籍部件上的属性、GeneratePages 负责填页。若 A 没生效，
//     这里再尝试直接改页对象里的文本字段；字段名未知，所以按候选名探测并打印日志。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class MarkovTextPatch
    {
        // ---------- A. MarkovChain 生成方法 ----------

        /// <summary>已知类型名（优先尝试）；找不到时会退化为"全量扫描名字含 Markov 的类型"。</summary>
        private static readonly string[] ChainTypeCandidates =
        {
            "XRL.MarkovChain",
            "XRL.Language.MarkovChain",
            "MarkovChain"
        };

        // 只补**生成类**方法：名字以 Generate / Make / Build / Compose / Produce / Random 开头。
        // 为什么不按 Sentence/Title 这类词随便匹配：程序集里还有叫 MarkovSentence / MARKOVSENTENCE 的
        // **语料名 getter**，把它们的返回值换成中文会直接搞坏游戏逻辑。
        private static readonly string[] GenerationPrefixes =
        {
            "Generate", "Make", "Build", "Compose", "Produce", "Random"
        };

        private static bool IsGenerationMethod(string name)
        {
            foreach (string p in GenerationPrefixes)
            {
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---------- B. 书页兜底 ----------

        private static readonly string[] BookTypeCandidates =
        {
            "XRL.World.Parts.MarkovBook",
            "XRL.World.Parts.StandaloneMarkovBook"
        };

        private static readonly string[] PageTextMemberCandidates =
        {
            "Text", "Body", "Contents", "PageText", "BookText", "Line", "Lines", "String", "Value"
        };

        private static bool _pagesLogged;

        public static void TryApply()
        {
            try { ZhsBookText.EnsureBuilt(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[QudZhsCookbook] 书籍正文链构建失败：" + e); }

            MethodInfo pSentence = PostfixOf("PostfixSentence");
            MethodInfo pParagraph = PostfixOf("PostfixParagraph");
            MethodInfo pShort = PostfixOf("PostfixShort");
            MethodInfo pTitle = PostfixOf("PostfixTitle");
            if (pSentence == null || pParagraph == null || pShort == null || pTitle == null) return;

            // ===== 候选类型发现 =====
            // 1) 先试已知类型名
            // 2) 再**全量扫描**所有已加载程序集里名字含 "Markov" 的类型（排除本模组自己）
            //    这样即使真实类型/命名空间与推测不同，也能命中。
            List<Type> targets = new List<Type>();
            foreach (string typeName in ChainTypeCandidates)
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t != null && !targets.Contains(t)) targets.Add(t);
                else UnityEngine.Debug.Log("[QudZhsCookbook] 已知类型名未找到：" + typeName);
            }

            int scanned = 0;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    List<Type> ok = new List<Type>();
                    foreach (Type t in ex.Types) if (t != null) ok.Add(t);
                    types = ok.ToArray();
                }
                catch { continue; }

                foreach (Type t in types)
                {
                    scanned++;
                    if (t.FullName == null) continue;
                    if (t.FullName.StartsWith("Nattens.", StringComparison.Ordinal)) continue;
                    if (t.FullName.IndexOf("Markov", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!targets.Contains(t)) targets.Add(t);
                }
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] 扫描 " + scanned + " 个类型，名字含 Markov 的候选 "
                + targets.Count + " 个：" + string.Join(", ", ToNames(targets)));

            // ===== 逐类型打补丁 =====
            bool anyChain = false;
            foreach (Type t in targets)
            {
                List<string> strMethods = new List<string>();
                List<string> genMethods = new List<string>();
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                        | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    strMethods.Add(mi.Name + (mi.IsStatic ? "(static)" : ""));
                    if (IsGenerationMethod(mi.Name)) genMethods.Add(mi.Name);
                }
                UnityEngine.Debug.Log("[QudZhsCookbook] " + t.FullName + " 返回 string 的方法 "
                    + strMethods.Count + " 个：" + string.Join(", ", strMethods.ToArray()));
                UnityEngine.Debug.Log("[QudZhsCookbook] " + t.FullName + " 其中生成类方法 "
                    + genMethods.Count + " 个：" + string.Join(", ", genMethods.ToArray()));

                Harmony h = new Harmony("Nattens.QudZhsCookbook.markov." + t.Name);
                bool hit = false;
                hit |= PatchGenerationMethods(h, t, K_TITLE, pTitle, "标题");
                hit |= PatchGenerationMethods(h, t, K_PARAGRAPH, pParagraph, "段落");
                hit |= PatchGenerationMethods(h, t, K_SHORT, pShort, "短句");
                hit |= PatchGenerationMethods(h, t, K_SENTENCE, pSentence, "句子");
                anyChain |= hit;
            }

            if (!anyChain)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 未能在任何 Markov 类型上找到可补丁的生成方法，"
                    + "书籍正文将保持英文。上面那两行日志列出了这些类型真实的方法名，把它贴回来即可精确定位。");
            }

            // B：书页兜底（无论 A 是否命中都挂上，内部按"已含中文"跳过，幂等）
            MethodInfo pPages = PostfixOf("PostfixPages");
            if (pPages == null) return;
            List<Type> bookTypes = new List<Type>();
            foreach (string typeName in BookTypeCandidates)
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t != null) bookTypes.Add(t);
            }
            // 同时把扫描到的其它 Markov*Book 类型也纳入
            foreach (Type t in targets)
            {
                if (t.FullName != null && t.FullName.IndexOf("Book", StringComparison.OrdinalIgnoreCase) >= 0
                    && !bookTypes.Contains(t)) bookTypes.Add(t);
            }
            // 书页级反射兜底已移除（0.9.2 起）。
            // 依据：存档实证显示书页字段（BookPageInfo / Pages）并不存在 —— 书页是按语料/seed 每次重新生成的，
            // 不写盘。因此"用反射猜字段名去改 Pages"既没必要，又可能写坏游戏对象
            // （帧内症状就是 MarkovBook.HandleEvent 里 =title= 为 null）。
            // 真正的杠杆是上面的链生成方法补丁；标题另有 ZhsMarkovTitlePatch 专门接管。
            UnityEngine.Debug.Log("[QudZhsCookbook] 书页兜底已停用（书页不存盘、按 seed 重新生成；标题由标题接管处理）");
        }

        private static MethodInfo PostfixOf(string name)
        {
            return typeof(MarkovTextPatch).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }

        // 关键词分组：命中任一关键词即采用对应粒度
        private static readonly string[] K_TITLE = { "Title" };
        private static readonly string[] K_PARAGRAPH = { "Paragraph" };
        private static readonly string[] K_SHORT = { "Short" };
        private static readonly string[] K_SENTENCE = { "Sentence", "Markov", "Generate", "Text", "Prose", "Corpus" };

        private static string[] ToNames(List<Type> types)
        {
            List<string> names = new List<string>();
            foreach (Type t in types) names.Add(t.FullName ?? t.Name);
            return names.ToArray();
        }

        /// <summary>只补"生成类方法"（名字以 Generate/Make/… 开头）且名字命中关键词的。
        /// 不用名字里含 Sentence/Title 就无脑补 —— 那样会连 MarkovSentence 这类**语料名 getter** 一起改掉，
        /// 直接把游戏逻辑搞坏。</summary>
        private static bool PatchGenerationMethods(Harmony h, Type t, string[] keywords, MethodInfo postfix, string label)
        {
            bool applied = false;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static))
            {
                if (mi.ReturnType != typeof(string)) continue;
                if (!IsGenerationMethod(mi.Name)) continue;
                // 这些不是散文，是链的内部数据（Player.log 实证存在 GenerateSeedFromWord）
                if (mi.Name == "GenerateSeedFromWord") continue;
                if (mi.IsAbstract) continue;
                if (mi.DeclaringType != null && mi.DeclaringType.IsInterface) continue;
                if (mi.ContainsGenericParameters) continue;

                bool match = false;
                foreach (string k in keywords)
                {
                    if (mi.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                }
                if (!match) continue;
                try
                {
                    h.Patch(mi, postfix: new HarmonyMethod(postfix));
                    applied = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 书籍正文" + label + "补丁已应用："
                        + t.FullName + "." + mi.Name + "(" + (mi.IsStatic ? "static" : "inst") + ")");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 补丁 " + t.FullName + "." + mi.Name
                        + " 失败（继续其它候选）：" + e.Message);
                }
            }
            return applied;
        }

        private static bool PatchByKeyword(Harmony h, Type t, string[] keywords, MethodInfo postfix, string label)
        {
            bool applied = false;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static))
            {
                if (mi.ReturnType != typeof(string)) continue;
                if (mi.IsAbstract) continue;
                // 接口/抽象声明的方法不能直接打补丁（上一版就是在 IPart.SetName 上抛异常、
                // 把整个兜底循环中断掉的）
                if (mi.DeclaringType != null && mi.DeclaringType.IsInterface) continue;
                if (mi.ContainsGenericParameters) continue;

                bool match = false;
                foreach (string k in keywords)
                {
                    if (mi.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                }
                if (!match) continue;
                try
                {
                    h.Patch(mi, postfix: new HarmonyMethod(postfix));
                    applied = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 书籍正文" + label + "补丁已应用："
                        + t.FullName + "." + mi.Name + "()");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 补丁 " + t.FullName + "." + mi.Name + " 失败：" + e.Message);
                }
            }
            return applied;
        }

        private static bool PatchGroup(Harmony h, Type t, string[] methodNames, MethodInfo postfix, string label)
        {
            bool applied = false;
            foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mi.ReturnType != typeof(string)) continue;
                if (mi.IsAbstract) continue;
                bool match = false;
                foreach (string name in methodNames)
                {
                    if (mi.Name == name) { match = true; break; }
                }
                if (!match) continue;
                try
                {
                    h.Patch(mi, postfix: new HarmonyMethod(postfix));
                    applied = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 书籍正文" + label + "补丁已应用："
                        + t.FullName + "." + mi.Name + "()");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[QudZhsCookbook] 补丁 " + t.FullName + "." + mi.Name + " 失败：" + e.Message);
                }
            }
            return applied;
        }

        // ---------- 补丁体 ----------

        private static void PostfixSentence(ref string __result)
        {
            Run(ref __result, ZhsBookText.TranslateSentence, "句子");
        }

        private static void PostfixParagraph(ref string __result)
        {
            Run(ref __result, ZhsBookText.TranslateParagraph, "段落");
        }

        private static void PostfixShort(ref string __result)
        {
            Run(ref __result, ZhsBookText.TranslateShort, "短句");
        }

        private static void PostfixTitle(ref string __result)
        {
            Run(ref __result, ZhsBookText.TranslateTitle, "标题");
        }

        private static void Run(ref string result, Func<string, string> translate, string label)
        {
            try
            {
                result = translate(result);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 书籍正文（" + label + "）替换异常：" + e);
            }
        }

        /// <summary>
        /// 书页兜底：GeneratePages 跑完后，尝试把 Pages 里每个元素的文本字段换成中文。
        /// 只做"能认出来的就换"，认不出来就打印一次诊断日志（字段名 + 元素类型），便于后续按实况修正。
        /// </summary>
        private static void PostfixPages(object __instance)
        {
            try
            {
                if (__instance == null) return;
                object pages = ReadMember(__instance, "Pages");
                if (pages == null)
                {
                    if (!_pagesLogged) { _pagesLogged = true; UnityEngine.Debug.Log("[QudZhsCookbook] 书页兜底：部件上没有 Pages 成员，跳过。"); }
                    return;
                }

                IEnumerable seq = pages as IEnumerable;
                if (seq == null) return;

                int changed = 0, seen = 0;
                foreach (object page in seq)
                {
                    seen++;
                    if (page == null) continue;
                    if (page is string)
                    {
                        // 页就是字符串时不方便就地替换（值类型语义），交给 A 路
                        continue;
                    }
                    foreach (string member in PageTextMemberCandidates)
                    {
                        MemberInfo mi = FindMember(page.GetType(), member);
                        if (mi == null) continue;
                        string current = ReadMember(page, member) as string;
                        if (string.IsNullOrEmpty(current)) continue;
                        string translated = ZhsBookText.TranslateParagraph(current);
                        if (!ReferenceEquals(translated, current) && translated != current && WriteMember(page, member, translated))
                        {
                            changed++;
                        }
                        break;
                    }
                }

                if (!_pagesLogged)
                {
                    _pagesLogged = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 书页兜底：Pages 共 " + seen + " 页，改写 " + changed
                        + " 页；元素类型=" + (seen > 0 ? FirstTypeName(pages) : "空"));
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 书页兜底异常：" + e);
            }
        }

        private static string FirstTypeName(object seq)
        {
            IEnumerable e = seq as IEnumerable;
            if (e == null) return "?";
            foreach (object o in e)
            {
                return o == null ? "null" : o.GetType().FullName;
            }
            return "空";
        }

        // ---------- 反射小工具（与 NameApplier 同一套做法） ----------

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
