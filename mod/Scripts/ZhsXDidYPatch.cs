// ═══════════════════════════════════════════════════════════════════════════
// `Messaging.XDidY` 补语（Extra）英文清理
// ═══════════════════════════════════════════════════════════════════════════
//
// 【问题】实证消息（存栏实证，非推测）：
//     `阿盖夫 **has restocked 他的 inventory**!`
//     `尼玛·鲁达 **has restocked 她的 inventory**!`
//     `The 沾血的 巨蜈蚣**死亡**!`
//     `你经过**trash**。`
//
// 【根因（反编译）】`XRL.World.Parts.GenericInventoryRestocker::PerformStock`：
//     Messaging.XDidY(ParentObject, **"have"**,
//                     **"restocked " + ParentObject.its + " inventory"**, "!", …);
//   ⇒ 玩法：**动词原形** `"have"` + **补语字符串**（已含英文过去分词与名词）
//   ⇒ 我方 `ZhsMessagingVerbPatch` 只译了 `Verb` 形参；
//     **补语 `Extra` 原样输出** ⇒ `has restocked 他的 inventory`
//   ⇒ 且动词表里**没有 `have`** ⇒ 动词未变位、原样 "have"
//
// 【做法】给 `XDidY` / `XDidYToZ` 挂 **Prefix**，只改 `Extra`（补语）参数：
//   一组**通用英文短语规则**，把「英文动词/名词 + 中文」的混排清成纯中文。
//   规则**锚定在英文词上**，中文部分原样保留 ⇒ 绝不误伤纯中文消息。
//
// 例：
//     `restocked 他的 inventory`  ⇒  `补满了 他的 货物`
//     `The 巨蜈蚣死亡!`           ⇒  `巨蜈蚣死亡!`        （去掉英冠词）
//     末词名词映射：`trash`⇒垃圾 · `stairs up`⇒向上的楼梯 · `boar corpse`⇒野猪尸体 …
//
// 开关：装机目录放 `_zhs_xdidy_off.txt` 即停用。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsXDidYText
    {
        public static bool Disabled;
        public static int Hits;

        /// <summary>英文短语 ⇒ 中文（**从左到右依次应用**，每条都是 `old` 的精确串）。</summary>
        internal static readonly List<KeyValuePair<string, string>> Phrases =
            new List<KeyValuePair<string, string>>
        {
            // ── 商店补货（`GenericInventoryRestocker`）──
            // ★★ 2026-10-17 修（用户实机 `Syshwushwaun, village tinker有补满了 他的 的货物!`）：
            //   **长的必须排在短的前面** —— 原来 `" inventory"` 排在 `"his inventory"` **之前**，
            //   于是 `his inventory` 先被拆成 `his` + 「 的货物」⇒ `his` 再被别处译成「他的」
            //   ⇒ 输出「他的 的货物」（双「的」）。
            //   现在把带所有格的整条放前面，让 `his inventory` 先整条命中。
            // ★ 另：完成时的 `has`（助动词）仍会被 `ZhsVerbPatch` 译成「有」
            //   ⇒「有补满了」；那半条由 `ZhsCombatMsgPatch` 的规则收拾（见那边注释）。
            new KeyValuePair<string, string>("his inventory", "他的货物"),
            new KeyValuePair<string, string>("her inventory", "她的货物"),
            new KeyValuePair<string, string>("its inventory", "它的货物"),
            new KeyValuePair<string, string>("their inventory", "他们的货物"),
            new KeyValuePair<string, string>(" inventory", "的货物"),
            new KeyValuePair<string, string>("restocked ", "补满了 "),

            // ── 其它常见补语（逐个补齐）──
            new KeyValuePair<string, string>("is overwhelmed with terror", "被恐惧压倒"),
            new KeyValuePair<string, string>("overwhelmed with terror", "被恐惧压倒"),
            new KeyValuePair<string, string>("reels from the force of", "被…的力道打得踉跄"),
            new KeyValuePair<string, string>("heals for", "恢复了"),
            new KeyValuePair<string, string>("hit point", "点生命值"),
            new KeyValuePair<string, string>("hit points", "点生命值"),
        };

        /// <summary>
        /// 末词名词映射（`你经过 trash。` / `你经过 stairs up。` 这类）。
        /// 只处理**末尾**的英文词，避免误伤句中。
        /// </summary>
        internal static readonly Dictionary<string, string> Nouns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "trash", "垃圾" },
            { "trash x2", "垃圾 x2" },
            { "stairs up", "向上的楼梯" },
            { "stairs down", "向下的楼梯" },
            { "stairs", "楼梯" },
            { "boar corpse", "野猪尸体" },
            { "oolite", "鲕粒岩" },
            { "door", "门" },
            { "waydroid", "寻路机器人" },
        };

        /// <summary>英冠词（后接中文时必须删）。</summary>
        private static readonly Regex Art = new Regex(
            @"(?<![A-Za-z])(?:The|the|THE|A|a|An|an)\s+(?=[\u4e00-\u9fff])",
            RegexOptions.Compiled);

        /// <summary>英文所有格/s 残留。</summary>
        private static readonly Regex Poss = new Regex(
            @"['\u2019]s\s*(?=[\u4e00-\u9fff])", RegexOptions.Compiled);

        private static readonly Regex NounTail = new Regex(
            @"(?<![A-Za-z])([A-Za-z][A-Za-z ]{2,24})\s*([。！？!?]?)$",
            RegexOptions.Compiled);

        internal static string Clean(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            string before = s;
            try
            {
                // ① 短语替换
                for (int i = 0; i < Phrases.Count; i++)
                {
                    if (s.IndexOf(Phrases[i].Key, StringComparison.Ordinal) >= 0)
                        s = s.Replace(Phrases[i].Key, Phrases[i].Value);
                }
                // ② 英冠词 + 中文
                s = Art.Replace(s, "");
                // ③ `'s` + 中文
                s = Poss.Replace(s, "");
                // ⑤ ★ 2026-10-15：**孤立系词**（本补丁把谓语翻成中文后，
                //    `Messaging` 变位出来的 `is` 会留在句中 ⇒ 见 `ZhsCopula` 头注）
                s = ZhsCopula.Fix(s);
                // ④ 末词名词
                Match m = NounTail.Match(s);
                if (m.Success)
                {
                    string w = m.Groups[1].Value.Trim();
                    string zh;
                    if (Nouns.TryGetValue(w, out zh))
                        s = s.Substring(0, m.Groups[1].Index) + zh + m.Groups[2].Value;
                }
            }
            catch { return before; }
            if (!ReferenceEquals(before, s) && before != s)
            {
                Hits++;
                if (Hits <= 12)
                    UnityEngine.Debug.Log("[QudZhsCookbook] XDidY补语："
                        + before + "  =>  " + s);
            }
            return s;
        }
    }

    internal static class ZhsXDidYPatch
    {
        private const string Tag = "[QudZhsCookbook] XDidY 补语接管：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied || ZhsXDidYText.Disabled) return;
            _applied = true;
            try
            {
                Type t = Find("XRL.World.Capabilities.Messaging");
                if (t == null)
                {
                    UnityEngine.Debug.Log(Tag + "找不到 Messaging");
                    return;
                }
                MethodInfo pre = typeof(ZhsXDidYPatch).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                if (pre == null) return;

                int n = 0;
                Harmony h = new Harmony("Nattens.QudZhsCookbook.xdidy");
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != "XDidY" && m.Name != "XDidYToZ") continue;
                    if (m.ContainsGenericParameters) continue;
                    try
                    {
                        h.Patch(m, prefix: new HarmonyMethod(pre));
                        n++;
                    }
                    catch { }
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（"
                    + ZhsXDidYText.Phrases.Count + " 条短语 + "
                    + ZhsXDidYText.Nouns.Count + " 个名词）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        private static Type Find(string full)
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
        /// `XDidY(Actor, Verb, Extra, EndMark, …)` ⇒ 第 2 参是补语。
        /// `XDidYToZ(Actor, Verb, Preposition, Object, Extra, EndMark, …)` ⇒ 第 4 参是补语。
        /// 用**参数位置 + 方法名**双重判定，不依赖参数名。
        /// </summary>
        private static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (ZhsXDidYText.Disabled || __args == null) return;
                string nm = __originalMethod == null ? "" : __originalMethod.Name;
                int idx = (nm == "XDidYToZ") ? 4 : 2;
                if (__args.Length <= idx) return;
                string s = __args[idx] as string;
                if (s == null) return;
                ZhsHookTrace.Step("XDidY." + nm, s);
                __args[idx] = ZhsXDidYText.Clean(s);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "Prefix 失败：" + e);
            }
        }
    }
}
