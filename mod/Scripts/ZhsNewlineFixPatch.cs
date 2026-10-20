// 换行吞字修复实验（A/B/C 三种策略，靠开关文件切换）。
//
// 【问题】中文长文本里，游戏渲染段间换行时**会吞掉换行后的第一个字符**。
//   数据侧确认无误（文件里 `…帮助。\nEsk 对细节…` 完整），
//   但游戏显示成「…帮助。」换行后剩「sk 对细节…」之类 —— 首字被消费。
//   同一条消息里 `Esk` 能保留、`他` 却被吞，说明是**渲染器行为**，与数据无关。
//
// 【为什么挂 ReplaceBuilder.ToString】
//   它是文本的最终出口，返回渲染完的字符串 —— 唯一能"看到成品"的位置。
//   我们在**输出后**修补首字符，绕过渲染器内部的处理顺序问题。
//
// 【三种策略】（用 _zhs_nlfix.txt 内容切换；默认 C）
//   C  首字重复    ："他非常挑剔" -> "他他非常挑剔"
//                  渲染器吞掉一个「他」，剩下正好是「他非常挑剔」。**最可能直接可用**
//   A  前面补标记  ："他非常" -> "·他非常"（·是可接受的中文间隔号）
//   B  前面补零宽  ："他非常" -> "\u200B他非常"（若游戏不吞零宽字符则无损）
//
// 【A/B 以 C 为基础】若 C 有效说明"吞 1 字符"，A/B 的标记自身会被吞掉，
//   于是正文首字得以保留 —— 三个方案其实是同一原理的不同伪装。
//
// 安全：只处理**含汉字的字符串**；异常全吞；开关可停用。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsNewlineFix
    {
        /// <summary>"A" / "B" / "C"，由 ZhsBootstrap 从开关文件读入。</summary>
        public static string Mode = "C";
        public static bool Disabled;
        /// <summary>只处理含汉字的文本，避免干扰纯英文/代码串。</summary>
        private static readonly Regex Han = new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

        private static int _hits;
        private static bool _logged;

        /// <summary>
        /// 策略 D：把含中文段落里的半角空格替换为不换行空格（U+00A0）。
        /// 目的：阻止游戏在「他是 Ari 的持有者」这类**中英混排处**折行。
        /// 纯英文段落原样保留（那些需要正常按词折行）。
        /// </summary>
        private static string NbspInsideParagraphs(string s)
        {
            var outp = new StringBuilder(s.Length + 8);
            int start = 0;
            while (start <= s.Length)
            {
                int nl = s.IndexOf('\n', start);
                int end = nl < 0 ? s.Length : nl;
                string para = s.Substring(start, end - start);
                // 段落含中文且有空格 -> 空格全部换成 NBSP
                if (para.IndexOf(' ') >= 0 && Han.IsMatch(para))
                    outp.Append(para.Replace(' ', '\u00a0'));
                else
                    outp.Append(para);
                if (nl < 0) break;
                outp.Append('\n');
                start = nl + 1;
            }
            return outp.ToString();
        }

        public static string Apply(string s)
        {
            if (Disabled || string.IsNullOrEmpty(s)) return s;
            if (!Han.IsMatch(s)) return s;              // 纯英文/代码一律不动

            try
            {
                // ★ 策略 D：段落内半角空格 -> 不换行空格（U+00A0）
                //   目的：阻止游戏在「他是 Ari 的持有者」这类中英混排处折行。
                //   注意：必须在"无换行提前返回"之前执行 —— 折行问题恰恰出现在**没有硬换行**的句子里。
                if (Mode.IndexOf('D') >= 0)
                    s = NbspInsideParagraphs(s);

                // 以下策略只针对**含硬换行**的多段文本
                if (s.IndexOf('\n') < 0) return s;

                var sb = new StringBuilder(s.Length + 16);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    sb.Append(c);
                    if (c != '\n') continue;

                    // 换行后跳过继续的换行/空格
                    int j = i + 1;
                    while (j < s.Length && (s[j] == '\n' || s[j] == ' ' || s[j] == '\t')) j++;
                    if (j >= s.Length) continue;
                    char next = s[j];
                    if (!Han.IsMatch(next.ToString())) continue;

                    // ★ 模式串可组合（如 "BD"）。用 IndexOf 判断，**绝不用 switch 的 default**
                    //   —— 曾因 default 落到"重复首字"分支，组合模式下出现叠字。
                    // ★ 诱饵字符改用「不换行空格 U+00A0」而不是零宽空格：
                    //   若渲染器裁掉行首一个字符 -> NBSP 被裁，正文完好（无痕）；
                    //   若没裁 -> NBSP 显示为普通空格，视觉可接受（不会像 C 那样出现叠字）。
                    if (Mode.IndexOf('A') >= 0)
                    {
                        sb.Append('\u00b7');                       // 间隔号
                    }
                    else if (Mode.IndexOf('B') >= 0 || Mode.IndexOf('C') >= 0)
                    {
                        sb.Append('\u00a0');                       // 不换行空格诱饵
                    }
                    if (_hits < 6)
                        UnityEngine.Debug.Log("[QudZhsCookbook] 换行补字：模式=" + Mode
                            + "，在 '" + next + "' 前插入诱饵");
                    _hits++;
                }
                if (!_logged && _hits > 0)
                {
                    _logged = true;
                    UnityEngine.Debug.Log("[QudZhsCookbook] 换行吞字修补生效，策略=" + Mode
                        + "（此日志只打一次）");
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[QudZhsCookbook] 换行修补失败：" + e.Message);
                return s;
            }
        }
    }

    [HarmonyPatch]
    internal static class ZhsNewlineFixPatch
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
            __result = ZhsNewlineFix.Apply(__result);
        }
    }
}
