// 实体属性写入接管 —— 代词/称谓汉化的**最终正确落点**。
//
// 来龙去脉（靠自建 IL 反汇编器 _tools/ildump 一路查出来）：
//   1. 历史文本里的 =spice.entity:subjectPronoun= 由
//      HistoryKit.HistoricStringExpander.InternalExpandQuery 解析，
//      它做的是 HistoricEntitySnapshot::GetProperty(key, "unknown")
//      —— 也就是**从实体快照里取一个早就存好的字符串**。
//   2. 快照又来自历史事件：HistoricEntity.GetEntityProperty 遍历 events，
//      读 HistoricEvent::entityProperties[key]。
//   3. 那么代词是**谁写进事件的**？XRL.Annals.InitializeSultan::Generate：
//         ldstr "female"/"male"  → SetEntityProperty("@gender", …)
//         call XRL.World.Gender::Get(...)            ← 取 Gender **共享单例**
//         SetEntityProperty("subjectPronoun",    gender.get_Subjective())
//         SetEntityProperty("possessivePronoun", gender.get_PossessiveAdjective())
//         SetEntityProperty("objectPronoun",     gender.get_Objective())
//         SetEntityProperty("reflexivePronoun",  gender.get_Reflexive())
//   4. 于是代词在**世界生成时就被算成英文字符串、写进事件、存进存档**。
//      这解释了为什么改 HistorySpice.jsonc 的 pronouns 池、以及给
//      BasePronounProvider 的 getter 打补丁**全都无效**——
//      前者不是它的来源，后者调用点被内联/走的不是那条路
//      （实测 Postfix 只收到 [PronounSet]，一次 [Gender] 都没有）。
//
// 结论：**唯一可靠的落点是写入点** HistoricEvent.SetEntityProperty(key, value)——
//   单一、非虚、且 value 是 ref 可改。在这里把代词换成中文，
//   之后无论谁读快照、怎么渲染，拿到的都是中文。
//
// 注意：绝不能去改 Gender 那个共享单例（Gender.Get 是字典查表，全局共用），
//       否则会影响游戏其它读取性别的逻辑，并可能被存档固化。

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsEntityPropPatch
    {
        // 需要拦的实体属性名（InitializeSultan 写的四个代词）
        private static readonly HashSet<string> PronounKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "subjectPronoun", "possessivePronoun", "objectPronoun", "reflexivePronoun",
        };

        // 称谓类属性名（若历史事件也写这些，一并处理）
        private static readonly HashSet<string> TermKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "personTerm", "immaturePersonTerm", "formalAddressTerm",
            "offspringTerm", "siblingTerm", "parentTerm",
        };

        private static int _hits;
        private static int _scanned;
        private static int _readHits;
        private static readonly object Gate = new object();

        public static void TryApply()
        {
            MethodInfo pre = typeof(ZhsEntityPropPatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (pre == null) return;

            Type ev = null;
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
                    if (t.FullName == "HistoryKit.HistoricEvent") { ev = t; break; }
                if (ev != null) break;
            }

            if (ev == null)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性接管：找不到 HistoryKit.HistoricEvent，跳过");
                return;
            }

            Harmony h = new Harmony("Nattens.QudZhsCookbook.entityprop");
            int n = 0;
            foreach (MethodInfo mi in ev.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance | BindingFlags.Static))
            {
                if (mi.Name != "SetEntityProperty" && mi.Name != "SetEventProperty") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length != 2) continue;
                if (ps[1].ParameterType != typeof(string)) continue;
                try
                {
                    h.Patch(mi, prefix: new HarmonyMethod(pre));
                    n++;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性接管已挂（写）：" + mi.Name
                        + "(" + ps[0].ParameterType.Name + ", " + ps[1].ParameterType.Name + ")");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性接管跳过 " + mi.Name + "：" + e.Message);
                }
            }

            // ---- ★ 读侧（更关键）：快照读取时替换 ----
            // 实证：写入侧补丁挂了却一次没触发（那一轮世界生成没走到），
            // 而文本渲染**每次**都会走 HistoricEntitySnapshot.GetProperty(key, default)。
            // 挂读侧可以：
            //   1) 兜住"写入时没拦到"的情况；
            //   2) 连**旧存档**里已经存成英文的代词也能在显示时变中文。
            MethodInfo postGet = typeof(ZhsEntityPropPatch).GetMethod("PostfixGet",
                BindingFlags.NonPublic | BindingFlags.Static);
            Type snap = null;
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
                    if (t.FullName == "HistoryKit.HistoricEntitySnapshot") { snap = t; break; }
                if (snap != null) break;
            }
            int nRead = 0;
            // ★ 读侧默认**关闭**：GetProperty 是全局基础设施，挂它有崩游戏的风险
            //   （需要时在模组目录放空文件 _zhs_snapshot_read_on.txt 启用）。
            bool readEnabled = false;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                // 模组目录不好取，改用"进程当前目录 + 常见模组路径"两种探测
                readEnabled = System.IO.File.Exists(
                    System.IO.Path.Combine(ModDirHint, "_zhs_snapshot_read_on.txt"));
            }
            catch { }
            if (snap != null && postGet != null && readEnabled)
            {
                Harmony h2 = new Harmony("Nattens.QudZhsCookbook.snapshotread");
                foreach (MethodInfo mi in snap.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                          | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    if (mi.Name != "GetProperty" && mi.Name != "TryGetProperty") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h2.Patch(mi, postfix: new HarmonyMethod(postGet));
                        nRead++;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log("[QudZhsCookbook] 快照读接管跳过 " + mi.Name + "：" + e.Message);
                    }
                }
            }
            UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性接管共挂 写 " + n + " 个、读 " + nRead
                + " 个（snapshot=" + (snap != null) + " 读侧启用=" + readEnabled + "）");
        }

        /// <summary>模组目录提示：Bootstrap 会把真实目录塞进来，读侧开关用它判断。</summary>
        public static string ModDirHint = "";

        /// <summary>读侧：把快照里的英文代词/称谓换成中文。即使存档里已是英文也能救。
        /// ★ 必须极端保守：GetProperty 是全局基础设施（successorChance / name / tier …
        ///   都走它），一旦把返回值改坏（比如写成 null），开局生成世界会直接 NRE。
        ///   所以：先存原值 → 只在确认拿到中文时替换 → 任何异常都恢复原值。</summary>
        private static void PostfixGet(MethodBase __originalMethod, object[] __args, ref string __result)
        {
            string original = __result;
            try
            {
                if (__args == null || __args.Length < 1) return;
                string key = __args[0] as string;
                if (string.IsNullOrEmpty(key)) return;
                // 只在"代词/称谓"这几个白名单键上动手，其余键**完全不碰**
                if (!PronounKeys.Contains(key) && !TermKeys.Contains(key)) return;

                string zh = Translate(key, original);
                if (string.IsNullOrEmpty(zh)) return;

                lock (Gate)
                {
                    _readHits++;
                    if (_readHits <= 30)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 快照读汉化 #" + _readHits
                            + "：" + key + " = " + original + " -> " + zh);
                }
                __result = zh;
            }
            catch
            {
                // 无论如何都要把原值还原，绝不让补丁把游戏搞崩
                __result = original;
            }
        }

        /// <summary>
        /// 在实体/事件属性落库前，把代词与称谓换成中文。
        /// 用 __args 而不是具名参数 —— 上一版栽在"具名参数注入失败被 catch 吞掉"，这次留日志。
        /// </summary>
        private static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 2) return;
                string key = __args[0] as string;
                string val = __args[1] as string;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val)) return;

                lock (Gate) { _scanned++; }

                string zh = Translate(key, val);
                if (zh == null) return;

                __args[1] = zh;
                lock (Gate)
                {
                    _hits++;
                    if (_hits <= 30)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性汉化 #" + _hits
                            + "：" + key + " = " + val + " -> " + zh);
                }
            }
            catch (Exception e)
            {
                lock (Gate)
                {
                    if (_hits + _scanned < 5)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 实体属性接管异常：" + e.Message);
                }
            }
        }

        /// <summary>返回中文，或 null 表示不改。</summary>
        private static string Translate(string key, string val)
        {
            bool isPronoun = PronounKeys.Contains(key);
            bool isTerm = TermKeys.Contains(key);
            if (!isPronoun && !isTerm) return null;

            string s = val.Trim();
            if (s.Length == 0 || s.Length > 24) return null;
            if (Markup.HasCjk(s)) return null;              // 幂等
            // 含未展开的查询串就不动它
            if (s.IndexOf('=') >= 0 || s.IndexOf('@') >= 0) return null;

            string low = s.ToLowerInvariant();
            if (isPronoun)
            {
                // ★ 按"格"取词：her 既可能是宾格(她)也可能是所有格(她的)，
                //   扁平表会把它们合并，必须按属性键区分（实测踩过：possessivePronoun=她）。
                string r;
                if (ZhsPronounPatch.TryTranslatePronounByCase(key, low, out r)) return r;
                return null;
            }
            string t;
            if (ZhsPronounPatch.TryTranslateTerm(low, out t)) return t;
            return null;
        }
    }
}
