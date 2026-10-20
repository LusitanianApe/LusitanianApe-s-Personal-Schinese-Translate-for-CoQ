// ═══════════════════════════════════════════════════════════════════════════
// 牌匾/墓碑排版 —— **按显示宽度**重写 `PostProcessors.VaryPlaque`
// ═══════════════════════════════════════════════════════════════════════════
//
// 【用户报告】「墓碑排版歪」——右边框被推出去。实测 `Local\test_tombstones.txt`：
//     ···╢·············深情缅怀··············╟      ← 总宽 40（正确 36）
//     ···╢··············蕾贝卡··············╟      ← 总宽 39
//     ···╢········死于舌腐（Glotrot）病。········╟   ← 总宽 44
//     181 条框行里 **41 条错位**（正确应为 36 列）
//
// 【根因（反编译 `XRL.World.Text.Delegates.PostProcessors::VaryPlaque`）】
//     public static void VaryPlaque(VariableContext Context, int WrapWidth = 28, …)
//     {
//         int num = 31;                     // 框内宽
//         int count = 3;                    // 左侧缩进
//         …ClipText(line, WrapWidth)…       // 折行
//         int num2 = ColorUtility.LengthExceptFormatting(list[i]);   // ★ 按**字符数**算宽
//         int num3 = (num - num2) / 2;                               // ★ 中文 1 字算 1 列
//         int count2 = num - num2 - num3;
//         textBuilder.Append(NBSP, num3).Append(list[i]).Append(NBSP, count2);
//     }
//   ⇒ 中文一字**占 2 列**，但 `LengthExceptFormatting` 数成 **1**
//     ⇒ 填充 NBSP 少了将近一半 ⇒ **右边框右移**（每 2 个汉字多推 1 列）。
//   ⇒ 同时 `ClipText` 也不按 CJK 宽度折行 ⇒ 长句不换行（实测 5 条超 25 列）。
//
// 【本补丁做法】Prefix 整体接管，重写为**宽度感知**版本：
//     · 折行：自写 `WrapByWidth`（CJK 记 2 列，绝不切 `{{…}}` / `&X` / `^X`）
//     · 居中：`pad = (31 - 显示宽) / 2`，两侧都用**显示宽**
//     · 框字符 / 颜色码 / 缩进**逐字沿用原实现**（只改宽度计算）
//   `WrapWidth` 取 `Inscription` 传进来的 `wordWrap`（= 部件 `MaxWidth`，实测 28）
//
// 【安全】
//   · Prefix 返回 false 仅当**整块成功写出**；任何异常 ⇒ 返回 true 交还原实现。
//   · `TextBuilder.Append` 走反射（直接引用该类型会拉进 `ReadOnlySpan<>` ⇒ CS7069）。
//   · 开关文件：`_zhs_plaque_off.txt`。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsPlaqueLayout
    {
        private const string Tag = "[QudZhsCookbook] 牌匾排版：";
        internal static bool Disabled;
        internal static int Hits;

        private const int INNER = 31;      // 框内宽（原实现 `int num = 31`）
        private const int INDENT = 3;      // 左缩进（原实现 `int count = 3`）

        /// <summary>
        /// ★★ 2.16.39：正文折行宽 = **20 显示列（中文 10 字）** —— 用户 2026-10-07 裁定
        ///   （记在 `patch_notes/2.15.68.md`）。当时那条裁定落在 `ZhsTombstoneLayout` 上，
        ///   而那个补丁因「字段 vs 属性」的 bug 从未生效；本补丁又自取了官方 `WrapWidth`
        ///   （= 部件 `MaxWidth`，实测 28）⇒ 两条路径口径不一致。现统一为裁定值。
        ///   用法是 `Math.Min(官方值, BODY_W)` ⇒ **只收窄、不放大**，小框不会溢出。
        /// </summary>
        private const int BODY_W = 20;

        /// <summary>
        /// 取 `VariableContext.Value`。
        ///
        /// ★★ 2.16.39：它是 **public readonly 字段**，不是属性 ——
        ///   `XRL.World.Text.Delegates.VariableContext`：
        ///       public readonly TextBuilder Value = new CompositedTextBuilder(128);
        ///   旧代码 `GetProperty("Value")` ⇒ **恒返回 null** ⇒ 每次都在那一行放行
        ///   （证据：`ModAssemblies\ZhsPlaqueDiag.log` 里 **84 条**「Value 属性拿不到，放行」，
        ///     即 `GenerateTombstones` 那一次的 84 次调用**全部**被挡）
        ///   ⇒ 本补丁自 2.15.73 起**一次都没生效过**。
        /// </summary>
        private static object ValueOf(object ctx)
        {
            Type t = ctx.GetType();
            FieldInfo f = t.GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(ctx);
            PropertyInfo p = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return p == null ? null : p.GetValue(ctx, null);
        }

        private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo>
            _cache = new System.Collections.Generic.Dictionary<Type, MethodInfo>();

        /// <summary>按名取 `Append(string)`（只取一次，缓存）。</summary>
        private static MethodInfo AppendOf(object sink)
        {
            if (sink == null) return null;
            Type t = sink.GetType();
            MethodInfo mi;
            if (_cache.TryGetValue(t, out mi)) return mi;
            try
            {
                mi = t.GetMethod("Append",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { mi = null; }
            _cache[t] = mi;
            return mi;
        }

        /// <summary>字符的显示列宽：CJK / 全角 = 2，其余 = 1。</summary>
        internal static int CharWidth(char c)
        {
            int o = c;
            if ((o >= 0x1100 && o <= 0x115F)
                || (o >= 0x2E80 && o <= 0xA4CF)
                || (o >= 0xAC00 && o <= 0xD7A3)
                || (o >= 0xF900 && o <= 0xFAFF)
                || (o >= 0xFE30 && o <= 0xFE6F)
                || (o >= 0xFF00 && o <= 0xFF60)
                || (o >= 0xFFE0 && o <= 0xFFE6))
            {
                return 2;
            }
            return 1;
        }

        /// <summary>
        /// 显示宽度：`{{X|内容}}` 只算**内容**（定界符零宽）；`&X` / `^X` / `=…=` 零宽。
        ///
        /// ★★ 2.16.43 更正（**上一版改错了方向，已回退**）：
        ///   2.16.43 曾改成「把 `{{` `|` `}}` 也算进宽度」，理由是「16 行 `{{|…}}` 的
        ///   N ≈ 显示列 − 5」。**那是错的** —— 当时漏算了 PUA 非打印字符
        ///   （`GraphemeSplitter.IsNonPrintable`：U+E000..U+F8FF 不占列），
        ///   于是把「PUA + 4 个定界符」误记成「5 个定界符」。
        ///
        ///   独立复验（`_tools\_verify_literal_marker.py`，正确跳过 PUA 后**逐行**检验）：
        ///       · 模型 L（把 `{{ | }}` 当字面）：**5 / 20**（只看带标记的行）
        ///       · 模型 M（当标记、定界符不算）：**20 / 20** ★
        ///   ⇒ 游戏**本来就把 `{{|…}}` 当合法标记**（`|` 前是空颜色码）。
        ///   ⇒ 原实现是**对的**，不能动；动了会把填充多加 3 列、把居中弄成左偏。
        /// </summary>
        internal static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '{' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    int e = s.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    if (e < 0) { w += 2; i += 2; continue; }
                    // `{{X|文字}}` ⇒ 只算「文字」部分（递归，兼容嵌套）
                    int bar = s.IndexOf('|', i + 2);
                    if (bar > 0 && bar < e) w += DisplayWidth(s.Substring(bar + 1, e - bar - 1));
                    i = e + 2;
                    continue;
                }
                if ((c == '&' || c == '^') && i + 1 < s.Length)
                {
                    i += 2;                       // 颜色码不占列
                    continue;
                }
                if (c == '=')
                {
                    int e = s.IndexOf('=', i + 1);
                    if (e > 0) { i = e + 1; continue; }
                }
                w += CharWidth(c);
                i++;
            }
            return w;
        }

        /// <summary>
        /// 折行单元：`Text` 是**唯一参与宽度**的部分；`Pre` / `Post` 是零宽骨架
        /// （`{{X|` 前缀、`}}` 后缀、`&X` / `^X` 颜色码），跟着相邻的可见字符一起走。
        /// </summary>
        private struct WrapUnit
        {
            public string Pre;
            public string Text;
            public string Post;
            public int Width;
        }

        /// <summary>
        /// 按**显示宽度**折行：`max` 列以内断行。
        ///
        /// ★★ 2.16.43（**上一版删错了，已恢复并修对**）：
        ///   病根只有一个 —— 旧实现把整个 `{{…}}` 当**一个不可切单元**
        ///   ⇒ 内容超过 `max` 时整块塞进一行、直接溢出：
        ///       39 列  {{|为争夺一个白化猿尸体而在决斗中丧命}}
        ///       25 列  {{|被坠落的晶体钢箭砸死}}
        ///   实测（`audit_tombstones3.py`）：形态 A 框行错位 **20**、正文超 20 列 **18** 条。
        ///
        ///   修法（§八 方案原意）：
        ///     · 前缀 `{{X|`、后缀 `}}` 与 `&X` / `^X` **零宽**，跟随相邻可见字符
        ///     · 标记**内容逐字符**参与折行累加 ⇒ 与普通文本同等对待
        ///     · ⚠️ 绝不从标记中间切断：断行时把**累计零宽后缀**挂到上一行末尾
        ///     · 嵌套 `{{…}}` 按**括号深度**配对
        ///   ⇒ 与 `DisplayWidth`（同样只算标记内容）**同口径**。
        /// </summary>
        internal static List<string> WrapByWidth(string text, int max)
        {
            var outp = new List<string>();
            if (string.IsNullOrEmpty(text)) { outp.Add(""); return outp; }
            if (max < 4) max = 4;
            foreach (string para in text.Split('\n'))
            {
                if (para.Length == 0) { outp.Add(""); continue; }
                var units = new List<WrapUnit>();
                ParseUnits(para, 0, para.Length, "", "", units);

                var cur = new StringBuilder();
                int cw = 0;
                string suffix = "";
                foreach (var u in units)
                {
                    if (cw > 0 && cw + u.Width > max)
                    {
                        // 断行：把累计的零宽后缀挂到上一行末尾（后缀从此不再重复）
                        if (suffix.Length > 0) { cur.Append(suffix); suffix = ""; }
                        outp.Add(cur.ToString());
                        cur.Clear();
                        cw = 0;
                    }
                    cur.Append(u.Pre).Append(u.Text);
                    cw += u.Width;
                    if (u.Post.Length > 0) suffix += u.Post;
                }
                if (suffix.Length > 0) cur.Append(suffix);
                outp.Add(cur.ToString());

                // ★★ 2.16.49 诊断：**输出行超过 max 时自动 dump**（只记前若干次）。
                //    判据落地：若这里报了 dump，说明**折行器自己**产出了超宽行
                //    ⇒ 问题在 `ParseUnits` / 宽度口径；若从不报，
                //    说明超宽行是**别处**拼出来的 ⇒ 去查 `Emit` 之后。
                if (_overDumps < 12)
                {
                    foreach (string seg in outp)
                    {
                        int w = DisplayWidth(seg);
                        if (w <= max) continue;
                        _overDumps++;
                        var d = new StringBuilder();
                        d.Append("★ 产出超宽行 w=").Append(w)
                         .Append(" > max=").Append(max)
                         .Append(" 原样=").Append(seg).Append('\n');
                        d.Append("   输入段(长 ").Append(para.Length).Append(")=")
                         .Append(para).Append('\n');
                        d.Append("   单元数=").Append(units.Count).Append('\n');
                        for (int ui = 0; ui < units.Count; ui++)
                        {
                            d.Append("      u[").Append(ui).Append("] w=")
                             .Append(units[ui].Width)
                             .Append(" pre=").Append(units[ui].Pre)
                             .Append(" text=").Append(units[ui].Text)
                             .Append(" post=").Append(units[ui].Post).Append('\n');
                        }
                        UnityEngine.Debug.Log("[QudZhsCookbook] 折行超宽 dump #"
                            + _overDumps + "：\n" + d);
                        if (_overDumps >= 12) break;
                    }
                }
            }
            return outp;
        }

        /// <summary>
        /// 把一段文本拆成折行单元。
        /// `pre` / `post` 是本层要**追加**到首/末单元的零宽骨架（供嵌套传递）。
        /// </summary>
        private static void ParseUnits(string s, int from, int to, string pre, string post,
                                       List<WrapUnit> outp)
        {
            int first = outp.Count;
            int i = from;
            while (i < to)
            {
                char c = s[i];
                if (c == '{' && i + 1 < to && s[i + 1] == '{')
                {
                    // 按**深度**找配对的 `}}`
                    int depth = 1, j = i + 2;
                    while (j < to && depth > 0)
                    {
                        if (s[j] == '{' && j + 1 < to && s[j + 1] == '{') { depth++; j += 2; continue; }
                        if (s[j] == '}' && j + 1 < to && s[j + 1] == '}') { depth--; j += 2; continue; }
                        j++;
                    }
                    int contentEnd = (depth == 0) ? j - 2 : to;   // 容错：未闭合则吃到结尾
                    int closeEnd = (depth == 0) ? j : to;
                    // `{{X|` 的骨架 = 直到第一个 `|`（含）；没有 `|` 则整块当零宽元数据
                    int bar = s.IndexOf('|', i + 2);
                    if (bar >= 0 && bar < contentEnd)
                    {
                        string mkPre = s.Substring(i, bar + 1 - i);
                        string mkPost = (depth == 0) ? s.Substring(contentEnd, closeEnd - contentEnd) : "";
                        ParseUnits(s, bar + 1, contentEnd, mkPre, mkPost, outp);
                    }
                    else
                    {
                        outp.Add(new WrapUnit { Pre = s.Substring(i, closeEnd - i), Text = "", Post = "", Width = 0 });
                    }
                    i = closeEnd;
                    continue;
                }
                if ((c == '&' || c == '^') && i + 1 < to)
                {
                    // 颜色码：零宽，挂到**前一个**单元后面；若还没有单元则挂到下一个
                    string code = s.Substring(i, 2);
                    if (outp.Count > first) outp[outp.Count - 1] = AppendPost(outp[outp.Count - 1], code);
                    else outp.Add(new WrapUnit { Pre = code, Text = "", Post = "", Width = 0 });
                    i += 2;
                    continue;
                }
                outp.Add(new WrapUnit { Pre = "", Text = c.ToString(), Post = "", Width = CharWidth(c) });
                i++;
            }
            // 本层骨架：前缀挂到首个单元，后缀挂到末个单元
            if (outp.Count > first)
            {
                if (pre.Length > 0) outp[first] = PrependPre(outp[first], pre);
                if (post.Length > 0) outp[outp.Count - 1] = AppendPost(outp[outp.Count - 1], post);
            }
            else if (pre.Length > 0 || post.Length > 0)
            {
                outp.Add(new WrapUnit { Pre = pre, Text = "", Post = post, Width = 0 });
            }
        }

        private static WrapUnit PrependPre(WrapUnit u, string pre)
        {
            u.Pre = pre + u.Pre;
            return u;
        }

        private static WrapUnit AppendPost(WrapUnit u, string post)
        {
            u.Post = u.Post + post;
            return u;
        }

        /// <summary>
        /// Prefix：整体接管 `PostProcessors.VaryPlaque`。
        /// 返回 true ⇒ 交还原实现（任何不确定的情况）。
        /// ⚠ 用 `object[] __args` 取参（原方法有 4 个**可选**参数，按名注入不稳）。
        /// </summary>
        private static bool Prefix(object __0, object[] __args)
        {
            // ★★ 诊断（2.15.74）：把**每一次调用**的判定过程写进日志，
            //   用来区分「没被调到」/「被调到但提前放行」/「接管失败」。
            DiagLog("调用 #" + (_calls + 1)
                + " args=" + (__args == null ? "null" : __args.Length.ToString())
                + " ctx=" + (__0 == null ? "null" : __0.GetType().Name));
            _calls++;
            if (Disabled) { DiagLog("  → 已停用，放行"); return true; }
            try
            {
                if (__0 == null) { DiagLog("  → Context 为 null，放行"); return true; }

                // 参数：0=Context 1=WrapWidth 2=corner 3=edge 4=dark（官方**只有这 5 个**）
                // ★★ 2.16.39：折行宽以用户裁定值 `BODY_W`（20 列 = 中文 10 字）为**上限**
                int wrap = BODY_W;
                string corner = "&Y^y", edge = "^k&y", dark = "&y^k", body = "^k&y";
                if (__args != null)
                {
                    if (__args.Length > 1 && __args[1] is int && (int)__args[1] > 0)
                        wrap = Math.Min((int)__args[1], BODY_W);
                    if (__args.Length > 2 && __args[2] is string && !string.IsNullOrEmpty((string)__args[2]))
                        corner = (string)__args[2];
                    if (__args.Length > 3 && __args[3] is string && !string.IsNullOrEmpty((string)__args[3]))
                        edge = (string)__args[3];
                    if (__args.Length > 4 && __args[4] is string && !string.IsNullOrEmpty((string)__args[4]))
                        dark = (string)__args[4];
                    if (__args.Length > 5 && __args[5] is string && !string.IsNullOrEmpty((string)__args[5]))
                        body = (string)__args[5];
                }

                object tb = ValueOf(__0);
                if (tb == null) { DiagLog("  → Value 拿不到（属性与字段都没有），放行"); return true; }
                MethodInfo ap = AppendOf(tb);
                if (ap == null) { DiagLog("  → Append(string) 拿不到，放行"); return true; }

                // 取原文
                MethodInfo toStr = tb.GetType().GetMethod("ToString",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (toStr == null) { DiagLog("  → ToString 拿不到，放行"); return true; }
                string raw = toStr.Invoke(tb, null) as string;
                DiagLog("  raw=" + (raw == null ? "(null)" : raw.Length + " 字 " + Show(raw, 60)));
                if (string.IsNullOrEmpty(raw)) { DiagLog("  → 原文为空，放行"); return true; }
                // ★ 只接管**含中文**的内容；纯英文交给原实现（其宽度算法本就正确）
                if (!HasCjk(raw)) { DiagLog("  → 不含中文，放行"); return true; }

                // 清空
                MethodInfo clear = null;
                foreach (MethodInfo mi in tb.GetType().GetMethods(
                             BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mi.Name == "Clear" && mi.GetParameters().Length == 0) { clear = mi; break; }
                }
                if (clear == null) { DiagLog("  → Clear() 拿不到，放行"); return true; }
                clear.Invoke(tb, null);

                string nbsp = XRL.Language.TextConstants.NBSP;
                string bullet = XRL.Language.TextConstants.BULLET;
                string horiz = XRL.Language.TextConstants.BOX_SINGLE_HORIZONTAL;
                string vert = "│";

                var lines = WrapByWidth(raw, wrap);
                lines.Insert(0, "");
                lines.Add("");

                var sb = new StringBuilder();
                // 顶边
                sb.Append(Rep(nbsp, INDENT)).Append(corner).Append(bullet)
                  .Append(edge).Append(Rep(horiz, INNER))
                  .Append(corner).Append(bullet).Append('\n');
                // 内容行（**按显示宽度**居中）
                foreach (string line0 in lines)
                {
                    // ★★ 防线：正文绝不能超过框内宽。
                    //   原实现的 `num3 = (31 - num2) / 2` 在 `num2 > 31` 时为**负数**
                    //   ⇒ `Append(NBSP, 负数)` ⇒ 内部 `Insert(…, Count)` ⇒
                    //   `ArgumentOutOfRangeException: Parameter name: Count`
                    //   （实测日志 44853–44856 行就是这个）。
                    string line = ClampByWidth(line0, INNER);
                    int lw = DisplayWidth(line);
                    int left = (INNER - lw) / 2;
                    if (left < 0) left = 0;
                    int right = INNER - lw - left;
                    if (right < 0) right = 0;
                    sb.Append(Rep(nbsp, INDENT)).Append(edge).Append(vert).Append(body)
                      .Append(Rep(nbsp, left)).Append(line).Append(Rep(nbsp, right))
                      .Append(edge).Append(vert).Append('\n');
                }
                // 底边
                sb.Append(Rep(nbsp, INDENT)).Append(corner).Append(bullet)
                  .Append(dark).Append(Rep(horiz, INNER))
                  .Append(corner).Append(bullet).Append(body).Append('\n');

                ap.Invoke(tb, new object[] { sb.ToString() });
                Hits++;
                DiagLog("  → ✅ 已接管（内容行 " + lines.Count + " · 折行宽 " + wrap
                    + " · 产出 " + sb.Length + " 字）");
                if (Hits <= 3)
                {
                    UnityEngine.Debug.Log(Tag + "首次生效（内容行 " + lines.Count
                        + " · 折行宽 " + wrap + " 列）");
                }
                return false;
            }
            catch (Exception e)
            {
                DiagLog("  → ★ 异常，交还原实现：" + e);
                UnityEngine.Debug.LogError(Tag + "Prefix 异常（交还原实现）：" + e.Message);
                return true;
            }
        }

        // ── 诊断用 ──
        private static int _calls;
        private static string _diagLog;
        /// <summary>★ 2.16.49：折行产出超宽行时的 dump 计数（上限 12，防日志爆炸）。</summary>
        private static int _overDumps;

        private static string Show(string s, int n)
        {
            s = s.Replace("\n", "⏎").Replace("\u00a0", "·");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        /// <summary>把诊断写进模组目录的 `ZhsPlaqueDiag.log`（前 400 行）。</summary>
        internal static void DiagLog(string msg)
        {
            try
            {
                if (_diagLog == null)
                {
                    // ★★ 2.16.62：旧实现取 `Assembly.Location` 的目录 ——
                    //   本项目没有 DLL（游戏运行时现场编译）⇒ 落到 Unity 临时目录。
                    //   改用共用工具 `ZhsFileUtil`（多候选 + 成败留痕）。
                    string dir = ZhsFileUtil.WriteDir;
                    if (dir == null)
                    {
                        if (_calls <= 5)
                            UnityEngine.Debug.Log(Tag + "诊断 " + msg);
                        return;
                    }
                    _diagLog = System.IO.Path.Combine(dir, "ZhsPlaqueDiag.log");
                    try { if (System.IO.File.Exists(_diagLog)) System.IO.File.Delete(_diagLog); }
                    catch { }
                }
                if (_calls > 400) return;
                System.IO.File.AppendAllText(_diagLog,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n");
            }
            catch { }
        }

        internal static void SetDiagPath(string p) { _diagLog = p; }

        private static string Rep(string s, int n)
        {
            if (n <= 0) return "";
            var sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        /// <summary>
        /// 按**显示宽度**硬截到 `max` 列以内（不切 `{{…}}` / `&X` / `^X`）。
        /// 用于兜底：绝不让填充量变成负数。
        ///
        /// ★ 2.16.43：**恢复为「整块取单元」** —— 这与 `DisplayWidth`
        ///   （`{{X|内容}}` 只算内容、定界符零宽）是**同一个口径**。
        ///   （2.16.43 中途曾把它改成逐字符，那是与当时错误的 `DisplayWidth` 配套的；
        ///     口径回退后必须一起回退，否则 39 列的标记块会被误判成超宽。）
        /// </summary>
        internal static string ClampByWidth(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (DisplayWidth(s) <= max) return s;
            var sb = new StringBuilder();
            int w = 0, i = 0;
            while (i < s.Length)
            {
                string unit;
                char c = s[i];
                if (c == '{' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    int e = s.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    if (e < 0) { unit = s.Substring(i); i = s.Length; }
                    else { unit = s.Substring(i, e + 2 - i); i = e + 2; }
                }
                else if ((c == '&' || c == '^') && i + 1 < s.Length)
                {
                    unit = s.Substring(i, 2); i += 2;
                }
                else { unit = s[i].ToString(); i++; }
                int uw = DisplayWidth(unit);
                if (w + uw > max) break;
                sb.Append(unit);
                w += uw;
            }
            return sb.ToString();
        }

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

    /// <summary>把 `ZhsPlaqueLayout.Prefix` 挂到 `PostProcessors.VaryPlaque` 上。</summary>
    internal static class ZhsPlaqueLayoutPatch
    {
        private const string Tag = "[QudZhsCookbook] 牌匾排版：";
        private static bool _applied;

        internal static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            if (ZhsPlaqueLayout.Disabled)
            {
                UnityEngine.Debug.Log(Tag + "开关文件存在，已停用");
                return;
            }
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("XRL.World.Text.Delegates.PostProcessors", false); }
                    catch { t = null; }
                    if (t != null) break;
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 PostProcessors");
                    return;
                }
                MethodInfo m = t.GetMethod("VaryPlaque",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 VaryPlaque");
                    return;
                }
                MethodInfo pre = typeof(ZhsPlaqueLayout).GetMethod("Prefix",
                    BindingFlags.NonPublic | BindingFlags.Static);
                new Harmony("Nattens.QudZhsCookbook.plaquewidth")
                    .Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(Tag + "已挂 PostProcessors.VaryPlaque"
                    + "（参数 " + m.GetParameters().Length + " 个 · 按显示宽度居中与折行）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }
    }
}
