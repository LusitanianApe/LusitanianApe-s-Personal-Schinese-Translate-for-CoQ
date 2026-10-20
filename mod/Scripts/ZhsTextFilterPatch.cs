// 文本后处理：全局去英文冠词 "the" + 把 a/an 译成「一 + 量词」。
//
// 设计原则（踩了很多坑之后的结论）：
//   * 只挂**最终文本出口** —— `XRL.World.Text.ReplaceBuilder.ToString()`。
//     它返回渲染完的字符串（占位符已展开、着色器已处理），是唯一能"看到成品"的位置。
//   * 规则必须**极其保守**，宁可漏也不能错：
//       - 去 "the"：只删「the + 紧跟汉字」的场合。
//         英文句子里 the 后面不会直接跟汉字，所以这条几乎不可能误伤。
//       - a/an → 量词：只在后面紧跟**已知中文名词**时替换，查表命中才动手。
//   * 所有异常吞掉；开关文件 _zhs_textfilter_off.txt 可停用。
//
// ★ 关于 Harmony 的教训：本文件所有 Patch 参数都必须按值传递
//   （或 __0/__1 序号），**绝不能写 ref**，否则挂载阶段抛异常、
//   PatchAll 中断，会让后面所有补丁一起失效。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    /// <summary>名词 → 量词的查表 + 文本过滤规则。</summary>
    internal static class ZhsTextFilter
    {
        public static bool Disabled;

        private static int _theHits, _mwHits;
        private static readonly Regex Han = new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

        // ---- a/an -> 量词：键是**中文名词**（我们已有物品译名，命中率较高）----
        private static readonly Dictionary<string, string> MW = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 武器 / 工具
            { "剑", "把" }, { "刀", "把" }, { "匕首", "把" }, { "短剑", "把" }, { "长刃", "把" },
            { "枪", "把" }, { "手枪", "把" }, { "步枪", "把" }, { "权杖", "把" }, { "锤", "把" },
            { "斧", "把" }, { "镐", "把" }, { "铲", "把" }, { "钥匙", "把" }, { "弓", "把" },
            { "椅子", "把" }, { "伞", "把" }, { "扇", "把" }, { "梳", "把" }, { "刷", "把" },
            // 瓶 / 容器
            { "药水", "瓶" }, { "瓶", "瓶" }, { "血清", "瓶" }, { "罐", "罐" }, { "桶", "桶" },
            { "壶", "壶" }, { "袋", "袋" }, { "包", "个" }, { "箱", "箱" }, { "匣", "匣" },
            { "杯", "杯" }, { "碗", "碗" }, { "碟", "只" }, { "锅", "口" },
            // 书 / 纸
            { "书", "本" }, { "卷轴", "卷" }, { "笔记", "本" }, { "日志", "本" }, { "信", "封" },
            { "纸", "张" }, { "地图", "张" }, { "票", "张" }, { "画", "幅" }, { "旗", "面" },
            { "页", "页" }, { "牌", "块" }, { "卡", "张" },
            // 石头 / 金属 / 块状
            { "锭", "块" },
            { "齿轮", "个" },
            { "硬币", "枚" }, { "环", "枚" }, { "戒指", "枚" }, { "徽章", "枚" }, { "币", "枚" },
            { "珠子", "颗" }, { "子弹", "发" }, { "弹药", "发" },
            { "炸弹", "枚" }, { "雷", "枚" }, { "电池", "节" }, { "电容", "个" },
            // 食物（★ 2.16.95：`肉`/`饭`/`面包`/`饼`/`汤`/`食` 已迁至下方「哨兵」区段）
            { "果", "个" }, { "水果", "个" }, { "蘑菇", "朵" },
            { "餐", "份" },
            // 生物（★ 2.16.95：`兽`/`虫` 已迁至哨兵区段）
            { "鸟", "只" }, { "鱼", "条" }, { "蛇", "条" },
            { "狗", "只" }, { "猫", "只" }, { "马", "匹" }, { "牛", "头" }, { "猪", "头" },
            { "人", "个" },
            // 衣物 / 装备
            { "衣", "件" }, { "甲", "件" }, { "袍", "件" }, { "裤", "条" }, { "鞋", "双" },
            { "靴", "双" }, { "手套", "副" }, { "帽", "顶" }, { "头盔", "顶" }, { "面具", "张" },
            { "项链", "条" }, { "腰带", "条" }, { "披风", "件" }, { "盾", "面" }, { "眼镜", "副" },
            // 线 / 条状（★ 2.16.95：`绳`/`线`/`链`/`电缆`/`管`/`木`/`枝条` 已迁至哨兵区段）
            { "棍", "根" }, { "杖", "根" },
            { "针", "根" }, { "柱", "根" },
            // 建筑 / 场所
            { "房", "间" }, { "室", "间" }, { "屋", "座" }, { "塔", "座" }, { "城", "座" },
            { "桥", "座" }, { "门", "扇" }, { "窗", "扇" }, { "墙", "面" }, { "营", "处" },
            { "村", "座" }, { "地", "处" }, { "位", "处" }, { "点", "处" },
            // 抽象 / 其它
            { "话", "句" }, { "字", "个" }, { "词", "个" }, { "事", "件" }, { "秘密", "个" },
            { "风", "阵" }, { "味", "股" },
            { "路", "条" }, { "河", "条" },
            { "花", "朵" }, { "云", "朵" }, { "星", "颗" }, { "月", "轮" }, { "日", "轮" },

            // ═══════════════════════════════════════════════════════════════════
            // ★★ 2.16.95【用户 2026-10-10 决定】**不可数 / 集合名词：量词整段清空**
            //
            // 【为什么】英文的 `a`/`an` 对不可数与集合名词也照加，而中文**不加**。
            //   沿用「一+量词」会产出不自然的句子。实证（用户截图）：
            //       `你经过一份水藤。`      ← `a watervine`（`水藤` 命中了键 `水` → `份`）
            //       `你经过一套奇异管束。`   ← 命中了键 `管` → `根`（再被别处改成「套」）
            //       `你经过一块金属工作台。` ← 命中了键 `金属` → `块`
            //   ⇒ 中文里「你经过水藤。」才是对的。
            //
            // 【用户原话】「要不直接把不可数名词的数词全部清空算了，繁中汉化就是这么做的」
            //   并经二选一确认：**整个「一+量词」删掉，只留名词** + **只清不可数/集合名词**
            //   （「一把剑」「一只狗」这类正确量词**保留**）。
            //
            // 【怎么表达「清空」】值 = **空字符串**（哨兵）：
            //   · `FindMeasure` / `MeasureFor` 返回 `""`（≠ `null`）
            //   · 两个消费者分别据此**不写「一+量词」**（详见
            //     `ConvertArticles` 与 `ZhsArticlePatch.Apply` 的注释）
            //   ⚠ 不能删掉这些键：删了就查不到 ⇒ `ZhsArticlePatch` 会回落到
            //     `?? "个"` ⇒ 变成「一个水藤」，**比现状更糟**。
            //
            // 【维护】新增条目时写在**本区段**，不要与上面的具体量词混排
            //   （最长匹配优先，两处键若重合以本区段为准更符合语义）。
            // ═══════════════════════════════════════════════════════════════════
            // —— 物质 / 材料（不可数）——
            { "水", "" }, { "酒", "" }, { "盐", "" }, { "金属", "" }, { "宝石", "" },
            { "水晶", "" }, { "矿石", "" }, { "石", "" }, { "砖", "" }, { "板", "" },
            { "碎片", "" }, { "种子", "" }, { "药丸", "" }, { "肉", "" }, { "饭", "" },
            { "食", "" }, { "面包", "" }, { "饼", "" }, { "汤", "" }, { "叶", "" },
            { "根", "" }, { "枝条", "" }, { "木", "" }, { "油", "" }, { "血", "" },
            { "尘", "" }, { "土", "" }, { "泥", "" }, { "气", "" }, { "烟", "" },
            // —— 集合 / 类名词（中文不加个体量词）——
            { "管", "" }, { "绳", "" }, { "线", "" }, { "链", "" }, { "电缆", "" },
            { "家具", "" }, { "装置", "" }, { "机器", "" }, { "机器人", "" },
            { "植物", "" }, { "生物", "" }, { "兽", "" }, { "虫", "" },
            // —— 抽象 / 事件（中文多用动词或直接说，不加个体量词）——
            { "任务", "" }, { "技能", "" }, { "能力", "" }, { "变异", "" }, { "效果", "" },
            { "原因", "" }, { "办法", "" }, { "机会", "" }, { "声响", "" }, { "声音", "" },
            { "光", "" }, { "痕", "" }, { "伤", "" }, { "裂", "" }
        };

        // a/an + 这些词时，直接给出整段替换（比逐词查更准）
        private static readonly Dictionary<string, string> PHRASE = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "single", "一" }, { "few", "一些" }, { "little", "一点" }, { "lot", "许多" }
        };

        /// <summary>文本后处理主入口。</summary>
        public static string Apply(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            try
            {
                s = StripThe(s);
                s = ConvertArticles(s);
            }
            catch { /* 后处理绝不能影响游戏 */ }
            return s;
        }

        /// <summary>删掉「the + 紧跟汉字」里的 the（英文句子里 the 后面不会直接跟汉字，安全）。</summary>
        private static string StripThe(string s)
        {
            if (s.IndexOf("the ", StringComparison.OrdinalIgnoreCase) < 0) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int j = s.IndexOf("the ", i, StringComparison.OrdinalIgnoreCase);
                if (j < 0) { sb.Append(s, i, s.Length - i); break; }
                // 词边界：the 前必须是行首/非字母
                bool leftOk = (j == 0) || !char.IsLetter(s[j - 1]);
                int after = j + 4;
                bool rightHan = after < s.Length && Han.IsMatch(s[after].ToString());
                if (leftOk && rightHan)
                {
                    sb.Append(s, i, j - i);          // 丢掉 "the "
                    i = after;
                    _theHits++;
                }
                else
                {
                    sb.Append(s, i, after - i);      // 保留 "the "
                    i = after;
                }
            }
            return sb.ToString();
        }

        /// <summary>把 "a/an &lt;中文名词&gt;" 换成「一 + 量词 + 名词」。</summary>
        private static string ConvertArticles(string s)
        {
            if (s.IndexOf("a ", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("an ", StringComparison.OrdinalIgnoreCase) < 0) return s;

            var sb = new StringBuilder(s.Length + 8);
            int i = 0;
            while (i < s.Length)
            {
                int j = -1; int len = 0;
                int ja = s.IndexOf("an ", i, StringComparison.OrdinalIgnoreCase);
                int jb = s.IndexOf("a ", i, StringComparison.OrdinalIgnoreCase);
                if (ja >= 0 && (jb < 0 || ja <= jb)) { j = ja; len = 3; }
                else if (jb >= 0) { j = jb; len = 2; }
                if (j < 0) { sb.Append(s, i, s.Length - i); break; }

                bool leftOk = (j == 0) || !char.IsLetter(s[j - 1]);
                int after = j + len;
                if (!leftOk) { sb.Append(s, i, after - i); i = after; continue; }

                // 向后扫出连续的汉字（最多 6 字），用于查量词表
                int k = after; int n = 0;
                while (k < s.Length && n < 6 && s[k] >= '\u4e00' && s[k] <= '\u9fff') { k++; n++; }
                if (n == 0) { sb.Append(s, i, after - i); i = after; continue; }

                string nounRun = s.Substring(after, n);
                string mw = FindMeasure(nounRun);
                if (mw == null) { sb.Append(s, i, after - i); i = after; continue; }

                sb.Append(s, i, j - i);      // 保留前置内容
                // ★★ 2.16.95：`mw == ""` = **不可数 / 集合名词的哨兵** ⇒ 整段「一+量词」都不写。
                //   `FindMeasure` 返回 `null`（未收录 ⇒ 交还游戏）与 `""`（收录且明确不加）
                //   必须区分，故判据用 `Length > 0` 而不是真假。
                if (mw.Length > 0) sb.Append('一').Append(mw);
                i = after;                    // 丢掉 "a "/"an "，名词本身保留在后面
                _mwHits++;
            }
            return sb.ToString();
        }

        /// <summary>在名词串里找最长的量词命中（优先长词，如「药水」优先于「水」）。</summary>
        private static string FindMeasure(string nounRun)
        {
            for (int len = Math.Min(nounRun.Length, 4); len >= 1; len--)
            {
                string key = nounRun.Substring(0, len);
                string mw;
                if (MW.TryGetValue(key, out mw)) return mw;
            }
            return null;
        }

        /// <summary>
        /// 供冠词补丁使用：按名词取量词。
        /// ★ 返回值有三种含义，调用方**必须分清**（2.16.95）：
        ///     · `null`  —— **未收录** ⇒ 调用方自行回退（如「个」）
        ///     · `""`    —— **已收录且明确不加量词**（不可数 / 集合名词，2.16.95 新增）
        ///     · 非空串  —— 正常量词（`把` / `只` / `瓶` …）
        /// </summary>
        public static string MeasureFor(string noun)
        {
            if (string.IsNullOrEmpty(noun)) return null;
            try
            {
                // 名词串里找最长的量词命中（优先长词，如「药水」优先于「水」）
                for (int len = Math.Min(noun.Length, 4); len >= 1; len--)
                {
                    string key = noun.Substring(0, len);
                    string mw;
                    if (MW.TryGetValue(key, out mw)) return mw;
                }
            }
            catch { }
            return null;
        }

        public static string Stats()
        {
            return "去the " + _theHits + " 次、量词 " + _mwHits + " 次";
        }
    }

    /// <summary>
    /// 文本最终出口：ReplaceBuilder.ToString()。
    /// 它返回渲染完的字符串（占位符已展开），是唯一能"看到成品"的地方。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsTextOutputPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(XRL.World.Text.ReplaceBuilder), "ToString", Type.EmptyTypes, null);
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return TargetMethod() != null; } catch { return false; }
        }

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            __result = ZhsTextFilter.Apply(__result);
        }
    }

    /// <summary>字符串表的直接出口（_S 返回 string，不经过 ReplaceBuilder）。</summary>
    [HarmonyPatch]
    internal static class ZhsStringsOutputPatch
    {
        private static int _done;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            foreach (MethodBase m in typeof(XRL.Language.Strings).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "_S") continue;
                if ((m as System.Reflection.MethodInfo)?.ReturnType != typeof(string)) continue;
                list.Add(m);
            }
            return list;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try { return true; } catch { return false; }
        }

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            if (_done < 6)
            {
                _done++;
                UnityEngine.Debug.Log("[QudZhsCookbook] 文本过滤（字符串表 _S）："
                    + (__result ?? "").Substring(0, Math.Min(60, (__result ?? "").Length)));
            }
            __result = ZhsTextFilter.Apply(__result);
        }
    }

    /// <summary>
    /// 列表连接器出口：`=xxx.andList=` / `=xxx.orList=` 走
    /// `XRL.Language.Grammar.MakeAndList(IReadOnlyList&lt;string&gt;, bool)`。
    ///
    /// ★ 为什么需要单独挂这里：
    ///   MakeAndList 会给**每个列表项**加英语冠词（"a pinch of" → "a 少许"），
    ///   它不知道元素已被译成中文 ⇒ 产出「你凑了些配料：a 少许 …, a 打捞 …」。
    ///   而 ZhsTextFilter 的最终出口跑在这**之后**，那时冠词已经插进去了。
    ///   （存档实证：'你凑了些配料：a 少许 从琥珀色的汤上撕下的一页, a 打捞 一颗山莓, 和 a 少许 …'）
    ///
    /// ★ 保守性：只在**冠词后紧跟汉字**时剥掉。
    ///   英文句子里 "a " 后面不会直接跟汉字，所以这条几乎不可能误伤。
    /// </summary>
    [HarmonyPatch]
    internal static class ZhsListArticlePatch
    {
        private static int _hits;

        private static System.Reflection.MethodBase TargetMethod()
        {
            // Grammar.MakeAndList(IReadOnlyList<string> Words, bool Serial = true)
            foreach (var m in typeof(XRL.Language.Grammar).GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "MakeAndList") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType.IsGenericType
                    && ps[1].ParameterType == typeof(bool))
                {
                    return m;
                }
            }
            return null;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            try
            {
                var m = TargetMethod();
                UnityEngine.Debug.Log("[QudZhsCookbook] 列表冠词补丁 Prepare：MakeAndList="
                    + (m == null ? "❌ null"
                                 : "✅ 参数 " + m.GetParameters().Length));
                return m != null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 列表冠词补丁 Prepare 异常：" + e);
                return false;
            }
        }

        // a/an + 紧跟汉字 ⇒ 删冠词。也处理 ", a 汉字" 与 "和 a 汉字"。
        private static readonly Regex ArtHan = new Regex(
            @"(?<![A-Za-z])(?:an|a)\s+(?=[\u4e00-\u9fff])",
            RegexOptions.Compiled);

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            try
            {
                if (__result.IndexOf("a ", StringComparison.OrdinalIgnoreCase) < 0
                    && __result.IndexOf("an ", StringComparison.OrdinalIgnoreCase) < 0)
                    return;
                string before = __result;
                __result = ArtHan.Replace(__result, "");
                if (__result != before)
                {
                    _hits++;
                    if (_hits <= 6)
                    {
                        UnityEngine.Debug.Log("[QudZhsCookbook] 列表冠词剥除：\n  旧="
                            + before.Substring(0, Math.Min(150, before.Length))
                            + "\n  新=" + __result.Substring(0, Math.Min(150, __result.Length)));
                    }
                }
            }
            catch { /* 绝不能影响游戏 */ }
        }
    }
}
