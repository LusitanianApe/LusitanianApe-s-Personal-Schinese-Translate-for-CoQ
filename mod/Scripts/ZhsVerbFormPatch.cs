// ═══════════════════════════════════════════════════════════════════════════
// 动词变形：**中文直通闸** —— 挂 `Grammar.ThirdPerson` / `Grammar.PastTenseOf`
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户截图「严查」】
//     :: 一个年轻象牙 strikes 此处!
//     :: 你受到 8 点来自年轻象牙的 impalement的伤害。
//
// 【反编译实证 —— 这两句的产出链】
//   ① 消息模板来自部件**可覆盖的数据字段**：
//        // XRL.World.Parts.Impaler
//        public string Message       = "{{R|=subject.An= =verb:strike= =subject.directionIfAny=!}}";
//        public string DamageMessage = "from %t impalement.";
//      官方 object `Young Ivory`（「年轻象牙」）用的就是默认值。
//   ② `=verb:strike=` 的实现：
//        // XRL.World.Text.Delegates.StringReplacers
//        [VariableReplacer(Capitalization = true)]
//        public static void ThirdPerson(VariableContext Context, string Verb)
//        {
//            Context.Value.Append(Grammar.ThirdPerson(Verb));      // ★ 先变形
//            if (Context.Capitalize) Context.Value.InitUpper();
//        }
//      ⇒ `Grammar.ThirdPerson("strike")` → **"strikes"**
//   ③ 我方动词表里有 `"strike"`，**没有 `"strikes"`**
//      ⇒ 变形**之后**才查表 ⇒ 查不到 ⇒ 漏出英文。
//      （同族：`impale`/`impales` 两条都收了 ⇒ 所以那条例外没漏）
//
// 【为什么不该「逐个补变形词」】
//   英语第三人称/过去式变体太多（-s/-es/-ies/不规则 + 过去式 -ed/-ied/-t）
//   ⇒ 逐个补是**打地鼠**，而且 `Grammar.PastTenseOf` 那条路同样会漏。
//
// 【正解】给**变形函数**加中文直通闸：
//     · 输入已是中文（含 CJK）⇒ **原样返回**（中文动词没有形态变化）
//     · 输入仍是英文 ⇒ 交还原实现（英文行为完全不变）
//   ⇒ 一处修**全族**：`=verb:` / `=pastTense:` / `GameObject.Does(...)` 全部受益。
//
// ★★ 2026-10-16（F8 · 追加授权）：**英文输入不再是「一律放行」** ——
//   原判据「输入含 CJK 才动」让 **C# 直调路**（`Object.Does` → `GetVerb`
//   → `Grammar.ThirdPerson`）上的英文动词**永远出英文**（英文动词小表只有 2 条）。
//   现补一级**动词表回查**：小表未命中 ⇒ 查 `ZhsVerbZh.V`（整词 +
//   保守去变形 `-ies→y`/`-es`/`-s`）⇒ 命中即译；系词/助动词（`AuxSkip`）排除；
//   仍未命中 ⇒ **原样放行**（不猜、不丢实义）。详见 `Postfix` 内的长注释。
//   ⚠ 本段注释**故意不写**那个字段名 —— `_count_baseline.py` 的
//     「变形直通闸已知动词」口径是 `seg(text, 字段名)`（从**第一次**出现到其后
//     第一个 `};`），注释里提前出现该名字会把区间拉长、把文档里的示例串算进去
//     （实测 2 ⇒ 7 的假差异）。
//
// 【安全】`_zhs_verbform_off.txt` 可停用；异常一律交还原实现。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsVerbForm
    {
        public static bool Disabled;
        internal static int Hits;

        /// <summary>
        /// ★★ 英文动词 → 中文（2.16.77 新增）。
        ///
        /// 【为什么需要】
        ///   告示牌的短描述是 **C# 里直接拼的**，不经过字符串表（反编译实证）：
        ///       `XRL.World.Parts.Chat::HandleEvent(GetShortDescriptionEvent E)`
        ///           textBuilder.Append('\n')
        ///                      .Append(ParentObject.It)                // → "It"
        ///                      .Append(ParentObject.GetVerb("read"))   // → "reads"  ★
        ///                      .Append(", '").Append(text3).Append('\'');
        ///   ⇒ 输出 `It reads, 'Starapples and Cider'.`
        ///   ⇒ 「reads」不是词条，是 **`GetVerb("read")` 的产物**，
        ///     而 `GetVerb` → `Grammar.ThirdPerson("read")` → "reads"。
        ///
        /// 【为什么原来的补丁没管】
        ///   本补丁的 Postfix 里有一句 `if (!HasCjk(__0)) return;` ——
        ///   **英文输入有意放行**（保证英文行为不变）⇒ `read` 永远变形成 `reads`。
        ///   ⇒ 现在补一张**英文动词表**：命中就整词换成中文（不做英文变形）。
        ///
        /// 【安全】
        ///   · 只对**整词精确匹配**（Ordinal，忽略大小写）生效；不在表里的英文词一律放行
        ///   · 译文取「动词原形」的中文，`It` + 中文动词 = 「它写道」
        /// </summary>
        internal static readonly Dictionary<string, string> EnVerbs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 告示牌 / 铭牌 / 涂鸦（用户要求：read → 写道）
            { "read", "写道" },
            // 同一机制（`Chat.HandleEvent` 的另一分支：`[方括号]` 内容 ⇒ 动词 `bear`）
            { "bear", "载有" },
        };

        internal static int EnHits;

        /// <summary>★★ 2026-10-16 F8：回查动词表 `ZhsVerbZh.V` 命中的次数。</summary>
        internal static int VHits;

        /// <summary>
        /// ★★ 2026-10-16 F8：**不回查动词表**的词（系词 / 助动词）。
        ///
        /// 【为什么要这张排除表】
        ///   `ZhsVerbZh.V` 里收着 `be/is/are/was/were`（是/曾是）与
        ///   `do/does/did`（做/做了）等。它们**同时**是系词 / 助动词：
        ///     · `GameObject.GetVerb("are")` → `Grammar.ThirdPerson("are")` → **"is"**
        ///       —— 这正是消息栏 `甲虫臀 is 被恐惧压倒!` 的来源，已由
        ///       `ZhsCopula`（丢孤立系词）与 `ZhsDidXZh` 专线负责；
        ///     · F9（主系表 SVC 的区分与隔离）正在设计「系词何时**要译**」的判据
        ///       ⇒ 本补丁**不抢**这条线，避免「两处都在管系词」。
        ///   ⇒ 本表只影响**本文件的英文动词回查**；`ZhsVerbZh.V` 本身一字未动。
        ///   ★ 命中排除表 ⇒ **原样放行**（与「表里没有」同一下场）。
        /// </summary>
        internal static readonly HashSet<string> AuxSkip = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "be", "am", "is", "are", "was", "were",
            "have", "has", "had",
            "do", "does", "did",
            "will", "would", "shall", "should",
            "can", "could", "may", "might", "must", "ought",
        };

        internal static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u4e00' && c <= '\u9fff')
                    || (c >= '\u3400' && c <= '\u4dbf')
                    || (c >= '\u3000' && c <= '\u303f')
                    || (c >= '\uff00' && c <= '\uffef')) return true;
            }
            return false;
        }
    }

    internal static class ZhsVerbFormPatch
    {
        private const string Tag = "[QudZhsCookbook] 动词变形：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsVerbForm.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.Language.Grammar", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 XRL.Language.Grammar");
                    return;
                }
                MethodInfo post = typeof(ZhsVerbFormPatch).GetMethod("Postfix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.verbform");
                int n = 0;
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public
                             | BindingFlags.Static))
                {
                    if (mi.ReturnType != typeof(string)) continue;
                    // 只挂「动词变形」两个入口
                    if (mi.Name != "ThirdPerson" && mi.Name != "PastTenseOf") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        h.Patch(mi, postfix: new HarmonyMethod(post));
                        n++;
                        UnityEngine.Debug.Log(Tag + "已挂 Grammar." + mi.Name
                            + "（" + ps.Length + " 参数）");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log(Tag + "跳过 " + mi.Name + "：" + e.Message);
                    }
                }
                UnityEngine.Debug.Log(Tag + "共挂 " + n
                    + " 个出口（中文动词原样直通 · 不做英文变形）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// Postfix：参数里只要有中文，就把结果还原成**原词**（= 不做变形）。
        /// 判定用「输入是否含 CJK」而不是「输出是否含 CJK」——
        /// 因为英文词变形后仍不含 CJK，而中文词变形后可能是垃圾串。
        /// </summary>
        private static void Postfix(string __0, ref string __result)
        {
            if (ZhsVerbForm.Disabled) return;
            try
            {
                if (string.IsNullOrEmpty(__0) || string.IsNullOrEmpty(__result)) return;

                // ★★ 2.16.77：英文动词表 —— 命中就整词换中文（如 `read` → 「写道」）
                //   （告示牌的 `It reads, '…'` 是 C# 硬拼的，字符串表够不到，
                //     只能从 `Grammar.ThirdPerson` 这个动词出口下手）
                string zh;
                if (!ZhsVerbForm.HasCjk(__0)
                    && ZhsVerbForm.EnVerbs.TryGetValue(__0.Trim(), out zh))
                {
                    if (__result != zh)
                    {
                        ZhsVerbForm.EnHits++;
                        // ★★ 2026-10-16（F8 追加授权）：**先赋值、后打日志**，且日志自带 try。
                        //   原写法是「先打日志、后赋值」—— 正撞本项目已踩三次的坑：
                        //   `UnityEngine.Debug.Log` 在**体外**（离线自证路由）会抛
                        //   `SecurityException: ECall methods must be packaged into a system module`
                        //   ⇒ 被外层 `catch { }` 吞掉 ⇒ **连已算好的结果一起丢**。
                        //   实测判据：`-- combat` 里 `read → 写道` 会变成「原样返回」。
                        __result = zh;
                        if (ZhsVerbForm.EnHits <= 8)
                        {
                            try
                            {
                                UnityEngine.Debug.Log(Tag + "英文动词：'" + __0
                                    + "' → '" + zh + "'（放弃 '" + __result + "'）");
                            }
                            catch { }
                        }
                    }
                    return;
                }

                // ══════════════════════════════════════════════════════════
                // ★★ 2026-10-16 F8（本轮追加）：**回查动词表 `ZhsVerbZh.V`**
                //
                // 【为什么必须在这里补】
                //   本 Postfix 是 **C# 直调路**的唯一收口：
                //       Object.Does("begin")
                //         → GameObject.GetVerb("begin", PrependSpace: true)
                //         → Grammar.ThirdPerson("begin", true)        ← 就是这里
                //   而它原来的判据是「**输入含 CJK 才动**」（下面那行
                //   `if (!ZhsVerbForm.HasCjk(__0)) return;`）—— 英文输入**有意放行**；
                //   英文动词表 `EnVerbs` 又只有 `read`/`bear` 两条
                //   ⇒ `begin` / `swoop` / `stuck` … 在**这条路**上永远出英文。
                //   实证：`巨蜻蜓 begins flying.`（`Flight::StartFlying`）与
                //        `巨蜻蜓 swoops down to attack.`（`Combat::SwoopAttack`）——
                //        两条都是 `Object.Does(根动词) + 硬编码半句`。
                //   （模板槽路 `=object.Does:verb=` 走 `GameObjectReplacers.Does`
                //     → `ZhsVerbZh.Apply` → 一直查得到表 ⇒ 两路只通一条。）
                //
                // 【判据（保守 · 不许猜 · 不许丢实义）】
                //   ① 输入必须**纯英文**（`!HasCjk`，与下一条分支同口径）；
                //   ② `Trim()` 后**整词**命中 `ZhsVerbZh.V`（忽略大小写）⇒ 结果换中文；
                //   ③ 未命中 ⇒ 再试一次**保守去变形**（`-ies→y` / `-es` / `-s`，词长 > 3）
                //      后查表 —— 与 `ZhsVerbZh.SweepEnglishVerbs` 的同口径；
                //   ④ 系词 / 助动词（`AuxSkip`：be/is/are/was/were/have/do/…）**排除**，
                //      交给 `ZhsCopula` / `ZhsDidXZh` / F9 专线，本补丁不抢；
                //   ⑤ 仍未命中 ⇒ **原样放行**（`__result` 一个字都不动）。
                //   ⇒ 表里没有的词、英文局、多词短语的行为**完全不变**。
                //   ⚠ `__result` **先赋值、后打日志**（同上，日志自带 try）。
                // ══════════════════════════════════════════════════════════
                if (!ZhsVerbForm.HasCjk(__0))
                {
                    string v = __0.Trim();
                    string zhv = null;
                    if (!ZhsVerbForm.AuxSkip.Contains(v))
                    {
                        if (!ZhsVerbZh.V.TryGetValue(v, out zhv))
                        {
                            // ★ 去变形：**按候选逐个试**，取第一个命中。
                            //   顺序必须是「先削 `-s`、再削 `-es`、再 `-ies→y`」——
                            //   `strikes` 削 `-s` ⇒ `strike` ✓（若先削 `-es` 会得 `strik` ✗，
                            //   本条就是靠 harness 的自证抓出来的：初版只有单分支 `-es`，
                            //   `strikes` 判成不动）。
                            //   `misses` 削 `-s` ⇒ `misse` ✗ ⇒ 削 `-es` ⇒ `miss` ✓
                            string[] cand = null;
                            if (v.Length > 3 && v.EndsWith("s", StringComparison.Ordinal))
                            {
                                cand = new string[]
                                {
                                    v.Substring(0, v.Length - 1),                  // -s
                                    v.EndsWith("es", StringComparison.Ordinal)
                                        ? v.Substring(0, v.Length - 2) : null,     // -es
                                    (v.EndsWith("ies", StringComparison.Ordinal) && v.Length > 4)
                                        ? v.Substring(0, v.Length - 3) + "y" : null, // -ies → -y
                                };
                            }
                            if (cand != null)
                            {
                                string hit;
                                foreach (string c2 in cand)
                                {
                                    if (string.IsNullOrEmpty(c2)) continue;
                                    if (ZhsVerbZh.V.TryGetValue(c2, out hit)) { zhv = hit; break; }
                                }
                            }
                            else
                            {
                                zhv = null;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(zhv) && __result != zhv)
                    {
                        string lost = __result;
                        __result = zhv;                     // ★ 先赋值
                        ZhsVerbForm.VHits++;
                        if (ZhsVerbForm.VHits <= 10)
                        {
                            try
                            {
                                UnityEngine.Debug.Log(Tag + "动词表回查：'" + v
                                    + "' → '" + zhv + "'（放弃 '" + lost + "'）");
                            }
                            catch { }
                        }
                    }
                    return;                                 // 英文输入到此为止（英文行为不变）
                }

                if (__result == __0) return;
                if (ZhsVerbForm.Hits < 8)
                {
                    // ★ 同上：日志自带 try（体外会抛 SecurityException）
                    try
                    {
                        UnityEngine.Debug.Log(Tag + "'" + __0 + "' → 原样直通（放弃 '"
                            + __result + "'）");
                    }
                    catch { }
                }
                ZhsVerbForm.Hits++;
                __result = __0;
            }
            catch { }
        }
    }
}
