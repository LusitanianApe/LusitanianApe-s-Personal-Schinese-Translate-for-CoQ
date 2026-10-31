// ═══════════════════════════════════════════════════════════════════════════
//  ZhsProviderOverrides —— 官方 Provider 的**三个覆写**（旧「定论」已被推翻）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【★★★ 2026-10-15 订正：旧结论「做不到」是**错的**】
//   旧结论说：三个覆写签名都带 `ReadOnlySpan<char>`，而 `0Harmony.dll` 把
//   `System.Memory` 的类型**内部化**了 ⇒ `CS0122` ⇒ 连游戏自己都编不过。
//   实测（`_tools\roslyntest`，用**游戏自带的 Roslyn 3.11** + 游戏 `Managed\` 引用集）：
//
//       引用集                     结果        产物里 ReadOnlySpan 的来路
//       ─────────────────────────────────────────────────────────────────
//       全量 188 个（含 0Harmony）  错误 0 ✅    mscorlib   ← ★ 公开版
//       最小集 4 个（含 0Harmony）  错误 0 ✅    mscorlib   ← ★ 公开版
//       全量 − mscorlib            52 错 ⛔      （缺基础类型）
//
//   ⇒ **只要 `mscorlib` 在引用集里，Roslyn 就选它那个 public 的 `ReadOnlySpan`**，
//     `0Harmony` 的 internal 副本**不构成障碍**。而游戏的引用集必然含 `mscorlib`。
//   ⇒ 旧结论的成因：当年那个 `spanprobe` 探针**没把 mscorlib 放进引用集**
//     （于是只剩 0Harmony 的 internal 版 ⇒ CS0122）——「判据本身没验」的老毛病（§31/§32 同族）。
//
// 【三个覆写与它们的语义（反编译实证）】
//   ① `NextPossibleLineBreakIndex(ROS Span, int StartIndex, out int BreakBeforeIndex,
//                                  out bool ReplaceIfBroken)`
//        调用方 `XRL.UI.TextBlock.ParseSpan`：
//            Translator.NextPossibleLineBreakIndex(String, i, out BreakBeforeIndex, out ReplaceIfBroken);
//            int num2 = (BreakBeforeIndex != -1) ? (BreakBeforeIndex - i) : (String.Length - i);
//            chunk = String.Slice(i, num2);          // ★ 本段 = [i, BreakBeforeIndex)（不含断点字符）
//            if (宽度超限) { if (flag) { num3--; num2--; i++; chunk = …; } 换行 }
//            flag = ReplaceIfBroken;                 // ★ 「换行时可丢弃 i 处的字符」（英文=那个空格）
//      基类（英文）：`BreakBeforeIndex = Span.IndexOfAnyStartFrom(StartIndex + 1, " \n")`
//      ⇒ ★★ **中文没有空格 ⇒ 找不到断点 ⇒ BreakBeforeIndex = -1 ⇒ 整段当成一块 ⇒ 不折行**
//         （这正是 AGENTS §20「引擎不会给中文自动折行」的机制级原因）
//      本覆写：CJK 语境 ⇒ **逐字可断**（`BreakBeforeIndex = StartIndex + 1`，`ReplaceIfBroken = false`），
//        并让**右标点跟着上一行走**（断点推到标点之后）。英文语境 ⇒ 原样交还基类。
//
//   ② `GetGraphemeWidth(ROS c) → float`
//      基类：CJK 系范围 → **1.5f**；其余可打印 → 1f；不可打印 → 0f
//      调用链：`ConsoleLib.Console.ColorUtility.WidthExceptFormatting` → `Translator.GetStringWidth`
//              → `Provider.GetGraphemeWidth`；经典 UI 的 `TextBlock.ParseSpan` 也用它量宽。
//      ★ 我方全部排版口径都是「**全角 = 2**」（`DisplayWidth` / 硬折行 46 字 = 92 半角），
//        与基类的 1.5 不一致 ⇒ 游戏会**低估**中文宽度（弹窗按 1.5 计算 ⇒ 框偏窄、文字溢出）。
//      ⇒ 本覆写按 2.0 计；`_zhs_wide_off.txt` 可退回基类 1.5（对照用）。
//      ⚠ 只影响**经典 UI（TextBlock/RTF）**；`Qud.UI.BookScreen`（书）走 TMP，不经这里。
//
//   ③ `ShouldAddSpaceBetween(ROS Building, string Adding) → bool`
//      调用方 `DescriptionBuilder.ToStringCountMany`：
//          if (ShouldAddSpaceBetween(builder.AsSpan(), text)) builder.Append(' ');
//      ⇒ 返回 true 就给两段之间**插一个空格**（英文需要；中文就是「小 石」的来源）
//      ⚠ `Building` 里**含 `{{色|…}}` 标记**（builder 是先写 "{{" + Color + "|" + 文本 + "}}"）
//        ⇒ 取「最后一个可见字符」时必须**跳过尾部的标记**，否则把 '}' 当汉字判。
//
//   ③ 的入口是 `CreateDescriptionBuilder`（**不带 span**，本来就能覆写）：
//      它返回我们的 `ZhsDescriptionBuilder`，后者覆写 `ShouldAddSpaceBetween`。
//      ⚠ 基类实现是 `new DescriptionBuilder(int.MaxValue, BaseOnly)`（**忽略 Cutoff**）——
//        我们照抄同一形态；**不要**改用 `DescriptionBuilder.Create()`
//        （那个是静态工厂，会回调 `Translator.CreateDescriptionBuilder` ⇒ **递归**）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using XRL.Language;
using XRL.World;

namespace Nattens.QudZhsCookbook
{
    public partial class ZhsChineseProvider
    {
        private static bool? _wideOn;
        private static bool _breakLogged;
        private static int _breakHits;

        /// <summary>字宽口径开关：默认「全角=2」；模组目录放 `_zhs_wide_off.txt` 则退回基类 1.5。</summary>
        private static bool WideOn()
        {
            if (_wideOn.HasValue) return _wideOn.Value;
            bool on = true;
            try
            {
                string dir = ZhsTrace.ModDirectory;
                if (!string.IsNullOrEmpty(dir)
                    && File.Exists(Path.Combine(dir, "_zhs_wide_off.txt")))
                    on = false;
            }
            catch { }
            _wideOn = on;
            try
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 中文Provider 覆写已生效："
                    + "折行=逐字断（CJK）· 字宽=" + (on ? "2.0（全角=2）" : "1.5（基类）")
                    + " · 段间空格=按 CJK 抑制");
            }
            catch { }
            return on;
        }

        // ────────────────────────────────────────────────────────────────
        // ② 字宽
        // ────────────────────────────────────────────────────────────────
        public override float GetGraphemeWidth(ReadOnlySpan<char> c)
        {
            if (c.IsEmpty) return 0f;
            if (!WideOn()) return base.GetGraphemeWidth(c);
            if (IsCjk(c[0])) return 2f;                       // 全角 = 2（我方统一口径）
            return GraphemeSplitter.IsNonPrintable(c) ? 0f : 1f;
        }

        // ────────────────────────────────────────────────────────────────
        // ① 折行
        // ────────────────────────────────────────────────────────────────
        public override void NextPossibleLineBreakIndex(ReadOnlySpan<char> Span, int StartIndex,
            out int BreakBeforeIndex, out bool ReplaceIfBroken)
        {
            ReplaceIfBroken = false;
            BreakBeforeIndex = -1;
            if (StartIndex < 0 || StartIndex >= Span.Length) return;

            // ★★ 2026-10-25（A 档）：StartIndex 落在**格式标记**上时，整块跳过标记，断点放标记之后。
            //
            //   为什么必须抢在下面的 CJK 判断之前：
            //     标记是 `{{r|` / `&W` 这种**非汉字字符** ⇒ `IsCjk('{') == false`
            //     ⇒ 原逻辑把整段交回基类 ⇒ 基类一路找到很远的英文空格
            //     ⇒ 孤行 + 括号被劈成 `the / Spindle`（`_tools\_wrap_sim.py` 可复现）
            int afterFmt = SkipFormatBlock(Span, StartIndex);
            if (afterFmt > StartIndex)
            {
                BreakBeforeIndex = afterFmt;
                ReplaceIfBroken = false;
                LogBreak(StartIndex, afterFmt, Span);
                return;
            }

            int next = StartIndex + 1;
            if (next >= Span.Length) return;

            // ★★ 2026-10-25（B 档）：译注括号「能不断就不断」。
            //
            //   断点若落在 `（…）` 内部，就把整对括号当成一个**不可分块**：
            //   断点推到配对的 `）` 之后 ⇒ 括号要么整体在行内，要么整体去下一行。
            //   效果（用户 2026-10-25 要的）：
            //       ……传说中的纺锤
            //       （the Spindle）自这片纷乱中升起…      ← 断在括号后
            //   或者
            //       ……传说中的
            //       纺锤（the Spindle）自这片…            ← 断在译名前
            //
            //   ⚠ 只检查「块首 = `（`」：上一轮已经把整对括号跳过，i 不会停在括号内部。
            //   ⚠ 超长退化（> MaxParenBlock 字符）：允许内部逐字断，
            //      否则整块塞不进一行时 ParseSpan 会直接溢出（不换行）。
            if (IsOpenParen(Span[StartIndex]))
            {
                int close = FindCloseParen(Span, StartIndex);
                if (close > StartIndex && close - StartIndex <= MaxParenBlock)
                {
                    BreakBeforeIndex = close + 1;
                    ReplaceIfBroken = false;
                    LogBreak(StartIndex, close + 1, Span);
                    return;
                }
            }

            // 只有「上一字或下一字是中文语境」时才走逐字断；纯英文段交还基类（保住按词断行 + 丢空格）
            // ★ 用 IsCjkish 而不是 IsCjk：`——`/`……`/`·` 打头的中文段也要逐字断
            if (!IsCjkish(Span[StartIndex]) && !IsCjkish(Span[next]))
            {
                base.NextPossibleLineBreakIndex(Span, StartIndex,
                    out BreakBeforeIndex, out ReplaceIfBroken);
                return;
            }

            // ★ 右标点**跟着上一行走**：断点推到标点（及其连排）之后
            int b = next;
            while (b < Span.Length && IsClosingPunct(Span[b])) b++;
            BreakBeforeIndex = b;
            ReplaceIfBroken = false;

            // 诊断：前 8 次 + 前几次「标点吞并」各记一条（§45 门禁：新句式必须留痕）
            LogBreak(StartIndex, b, Span);
        }

        /// <summary>
        /// 若 `i` 处是格式标记或色码，返回「可以断在哪里」的下标；否则返回 `i`（= 不在格式上）。
        ///
        /// <para>识别三种：`{{…}}`、`&X`、`^X`（与 `ColorUtility.GetNext` 同一套口径）。</para>
        ///
        /// <para>★★ 长标记（<see cref="MaxMarkerBlock"/> 字符以上）**不能返回 i+1** ——
        /// 那会把 `{{` 拆到两行，标记头断掉 ⇒ 屏幕上出现字面量 `{`（2026-10-25 实测到）。
        /// 改为**断在 `|` 之后**：保住 `{{色|` 头，内容照常逐字折；末尾 `}}` 随最后一块走
        /// （`ParseSpan` 的 `FindLastColorCodes` 会延续颜色）。</para>
        /// </summary>
        private static int SkipFormatBlock(ReadOnlySpan<char> Span, int i)
        {
            if (i < 0 || i >= Span.Length) return i;
            char c = Span[i];

            if (c == '{' && i + 1 < Span.Length && Span[i + 1] == '{')
            {
                int depth = 0, end = -1;
                for (int j = i; j + 1 < Span.Length; j++)
                {
                    if (Span[j] == '{' && Span[j + 1] == '{') { depth++; j++; continue; }
                    if (Span[j] == '}' && Span[j + 1] == '}') { depth--; j++; if (depth <= 0) { end = j + 1; break; } }
                }
                if (end <= i) return Span.Length;              // 未闭合 ⇒ 当作到末尾
                if (end - i <= MaxMarkerBlock) return end;     // 短标记：整块不劈

                // 长标记：断在 `|` 之后（保住 `{{色|` 头）；无 `|`（纯色标记）⇒ 整块
                for (int k = i + 2; k < end; k++)
                    if (Span[k] == '|') return k + 1;
                return end;
            }
            if (c == '&' || c == '^')
            {
                if (i + 1 < Span.Length && Span[i + 1] != c) return i + 2;
                return i + 1;
            }
            if (c == '|' || c == '}') return i + 1;    // 标记残片
            return i;
        }

        /// <summary>整块跳过的上限（字符数）；超过就按「断在 `|` 之后」处理。</summary>
        private const int MaxMarkerBlock = 60;

        // ────────────────────────────────────────────────────────────────
        // B 档（2026-10-25）：译注括号不可断
        // ────────────────────────────────────────────────────────────────

        /// <summary>括号块的上限（字符数）。超过就允许内部断（否则整块塞不进一行 ⇒ 溢出）。</summary>
        private const int MaxParenBlock = 40;

        /// <summary>`（`（全角）或 `(`（半角）。</summary>
        private static bool IsOpenParen(char c)
        {
            return c == '\uFF08' || c == '(';
        }

        /// <summary>
        /// 从 `open` 处找配对的右括号，返回其下标；找不到 / 嵌套 / 超长 ⇒ -1。
        ///
        /// <para>只认「同形配对」：全角 `（` 找 `）`，半角 `(` 找 `)`。</para>
        /// </summary>
        private static int FindCloseParen(ReadOnlySpan<char> Span, int open)
        {
            if (open < 0 || open >= Span.Length) return -1;
            char oc = Span[open];
            char cc = (oc == '\uFF08') ? '\uFF09' : ')';
            int limit = Math.Min(Span.Length, open + MaxParenBlock + 1);
            for (int k = open + 1; k < limit; k++)
            {
                if (Span[k] == cc) return k;
                if (Span[k] == oc) return -1;      // 嵌套 ⇒ 放弃（交给逐字断）
            }
            return -1;
        }

        /// <summary>折行诊断日志：前 8 次逐条打（含标记跳过与标点吞并）。</summary>
        private static void LogBreak(int start, int b, ReadOnlySpan<char> Span)
        {
            if (_breakHits >= 8) return;
            _breakHits++;
            try
            {
                UnityEngine.Debug.Log("[QudZhsCookbook] 中文折行 #" + _breakHits
                    + "：i=" + start + " 断点=" + b
                    + (Span[start] == '{' || Span[start] == '&' || Span[start] == '^' ? "（跳过标记）" : "")
                    + (b > start + 1 && Span[start] != '{' && Span[start] != '&' && Span[start] != '^'
                        ? "（含右标点 ×" + (b - start - 1) + "）" : "")
                    + " 片段=[" + Span.Slice(start,
                        Math.Min(24, Span.Length - start)).ToString().Replace("\n", "⏎") + "]");
            }
            catch { }
        }

        /// <summary>
        /// 离线自检入口（供 `_tools\roslyntest run` 反射调用）。
        ///
        /// 【为什么要有它】`ReadOnlySpan<char>` 是 **ref struct** ⇒ 反射调用**无法装箱**
        /// （Harmony 当年也是栽在这 ⇒ `Cannot box IsByRefLike type`）。
        /// ⇒ 体外 harness 只能调「**不带 span 参数**的入口」；本方法内部**直接调用真实覆写**，
        ///   因此验证的是**真实代码路径**，不是抄一份逻辑（§53「不许自证」）。
        /// ⚠ 不参与游戏逻辑，只被工具调用；产物里的存在对运行时无影响。
        /// </summary>
        internal static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            var p = new ZhsChineseProvider();

            sb.Append("W(中)=").Append(p.GetGraphemeWidth("中".AsSpan())).Append('\n');
            sb.Append("W(！)=").Append(p.GetGraphemeWidth("！".AsSpan())).Append('\n');
            sb.Append("W(a)=").Append(p.GetGraphemeWidth("a".AsSpan())).Append('\n');
            sb.Append("W(空)=").Append(p.GetGraphemeWidth("".AsSpan())).Append('\n');

            int b; bool r;
            p.NextPossibleLineBreakIndex("中文测试".AsSpan(), 0, out b, out r);
            sb.Append("折行 中文@0 → b=").Append(b).Append(" r=").Append(r).Append('\n');

            p.NextPossibleLineBreakIndex("中文测试。abc".AsSpan(), 4, out b, out r);
            sb.Append("折行 中文@4（「。」处）→ b=").Append(b).Append('\n');

            p.NextPossibleLineBreakIndex("hello world".AsSpan(), 0, out b, out r);
            sb.Append("折行 英文@0 → b=").Append(b).Append(" r=").Append(r).Append('\n');

            p.NextPossibleLineBreakIndex("中文".AsSpan(), 1, out b, out r);
            sb.Append("折行 末尾@1 → b=").Append(b).Append('\n');

            // ★ 2026-10-25（A 档）：格式标记必须整块跳过（否则退回基类按空格断）
            const string MARK = "{{r|卡德（Qud）}}的丛林";
            p.NextPossibleLineBreakIndex(MARK.AsSpan(), 0, out b, out r);
            sb.Append("折行 标记@0 → b=").Append(b)
              .Append("（期望 ").Append(MARK.IndexOf("的")).Append("）").Append('\n');

            string longMark = "{{W|" + new string('长', 80) + "}}";
            p.NextPossibleLineBreakIndex(longMark.AsSpan(), 0, out b, out r);
            sb.Append("折行 长标记@0（断在 | 之后）→ b=").Append(b).Append("（期望 4）").Append('\n');

            p.NextPossibleLineBreakIndex("中文&W红".AsSpan(), 2, out b, out r);
            sb.Append("折行 色码@2 → b=").Append(b).Append("（期望 4）").Append('\n');

            // ★ 2026-10-25：`——`（U+2014）打头的中文段也要逐字断（IsCjkish）
            p.NextPossibleLineBreakIndex("——哦，趁我还没忘".AsSpan(), 0, out b, out r);
            sb.Append("折行 破折号@0 → b=").Append(b).Append("（期望 1）").Append('\n');

            // ★ 2026-10-25（B 档）：译注括号整对不可断
            const string PAREN = "纺锤（the Spindle）自";
            p.NextPossibleLineBreakIndex(PAREN.AsSpan(), 2, out b, out r);
            sb.Append("折行 括号@2 → b=").Append(b)
              .Append("（期望 ").Append(PAREN.IndexOf("自")).Append("）").Append('\n');

            string longParen = "（" + new string('长', 50) + "）";
            p.NextPossibleLineBreakIndex(longParen.AsSpan(), 0, out b, out r);
            sb.Append("折行 长括号@0（>40 退化）→ b=").Append(b).Append("（期望 1）").Append('\n');

            char v1 = ZhsDescriptionBuilder.LastVisibleChar("小".AsSpan());
            char v2 = ZhsDescriptionBuilder.LastVisibleChar("{{W|小}}".AsSpan());
            char v3 = ZhsDescriptionBuilder.LastVisibleChar("{{W|小}}{{Y|的}}".AsSpan());
            sb.Append("尾可见字符 小=").Append(v1).Append(" · {{W|小}}=").Append(v2)
              .Append(" · 双标记=").Append(v3).Append('\n');

#pragma warning disable CS0618
            var d = new ZhsDescriptionBuilder(int.MaxValue, false);
#pragma warning restore CS0618
            sb.Append("空格(小,石)=").Append(d.TestSpace("小", "石")).Append('\n');
            sb.Append("空格({{W|小}},石)=").Append(d.TestSpace("{{W|小}}", "石")).Append('\n');
            sb.Append("空格(中,。)=").Append(d.TestSpace("中", "。")).Append('\n');
            sb.Append("空格(（,中)=").Append(d.TestSpace("（", "中")).Append('\n');
            sb.Append("空格(big,rock)=").Append(d.TestSpace("big", "rock")).Append('\n');
            sb.Append("空格(big,石)=").Append(d.TestSpace("big", "石")).Append('\n');
            return sb.ToString();
        }

        /// <summary>右标点：断行时**必须跟着上一行走**（不许被甩到行首）。</summary>
        internal static bool IsClosingPunct(char c)
        {
            switch (c)
            {
                case '。': case '，': case '、': case '；': case '：': case '？': case '！':
                case '）': case '」': case '』': case '】': case '〕': case '》': case '〉':
                case '…': case '％':
                    return true;
                default:
                    return false;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // ③ 段间空格：入口
        // ────────────────────────────────────────────────────────────────
        public override DescriptionBuilder CreateDescriptionBuilder(
            int Cutoff = int.MaxValue, bool BaseOnly = false)
        {
#pragma warning disable CS0618   // 与基类同一构造形态（"Use DescriptionBuilder.Create()" 会递归，见文件头）
            return new ZhsDescriptionBuilder(int.MaxValue, BaseOnly);
#pragma warning restore CS0618
        }
    }

    /// <summary>
    /// 只改一件事：段与段之间的**空格**（治「小 石」）。
    /// 其余行为全部继承 `DescriptionBuilder`。
    /// </summary>
    internal class ZhsDescriptionBuilder : DescriptionBuilder
    {
#pragma warning disable CS0618
        public ZhsDescriptionBuilder(int Cutoff, bool BaseOnly) : base(Cutoff, BaseOnly) { }
#pragma warning restore CS0618

        protected override bool ShouldAddSpaceBetween(ReadOnlySpan<char> Building, string Adding)
        {
            if (string.IsNullOrEmpty(Adding)) return true;

            char a = Adding[0];
            // ① 下一段以「不收空格的标点」开头 ⇒ 不插
            if (IsNoSpaceBefore(a)) return false;

            char b = LastVisibleChar(Building);
            if (b != '\0')
            {
                // ② 上一段以「不收空格的开符号」结尾 ⇒ 不插
                if (IsNoSpaceAfter(b)) return false;
                // ③ 汉字贴汉字 ⇒ 不插（★ 治「小 石」）
                if (ZhsChineseProvider.IsCjk(b) && ZhsChineseProvider.IsCjk(a)) return false;
            }
            return base.ShouldAddSpaceBetween(Building, Adding);
        }

        /// <summary>离线自检入口（无 span 参数 ⇒ 可反射；内部调真实覆写）。</summary>
        internal bool TestSpace(string building, string adding)
        {
            return ShouldAddSpaceBetween(building.AsSpan(), adding);
        }

        /// <summary>中文/全角标点：**前面**不要空格（如「。」「，」「）」）。</summary>
        private static bool IsNoSpaceBefore(char c)
        {
            switch (c)
            {
                case '。': case '，': case '、': case '；': case '：': case '？': case '！':
                case '）': case '」': case '』': case '】': case '〕': case '》': case '〉':
                case '…': case '—': case '～': case '％': case '·':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>开符号：**后面**不要空格（如「（」「『」）。</summary>
        private static bool IsNoSpaceAfter(char c)
        {
            switch (c)
            {
                case '（': case '「': case '『': case '【': case '〔': case '《': case '〈':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 取「最后一个**可见**字符」。
        ///
        /// ★ 关键：`DescriptionBuilder` 里 `Building` 含 `{{色|文本}}` 标记，
        ///   而**标记内容（文本）才是可见的** ⇒ 不能把整块标记当不可见跳掉
        ///   （首版就栽在这：`{{W|小}}` 返回 '\0' ⇒ 空格照插 ⇒ 治不了「小 石」）。
        /// 规则：
        ///   · 末尾不是 '}' ⇒ 直接就是它
        ///   · 末尾是 '}' ⇒ 找配对开头，取该标记**内容**（最后一个 '|' 之后、'}}' 之前）的末字符
        ///        内容为空（`{{W}}` 这类纯颜色/纯参数标记）⇒ 继续往前找
        /// </summary>
        internal static char LastVisibleChar(ReadOnlySpan<char> s)
        {
            int i = s.Length - 1;
            while (i >= 0)
            {
                if (s[i] != '}') return s[i];

                // 向前找配对的开头（"{{"）：右括号 +1，左括号 -1，归零即配对
                int depth = 0, j = i;
                for (; j >= 0; j--)
                {
                    if (s[j] == '}') depth++;
                    else if (s[j] == '{')
                    {
                        depth--;
                        if (depth <= 0) break;
                    }
                }
                if (j < 0) return s[i];       // 括号不配对 ⇒ 当普通字符处理

                // 标记内容 = 最后一个 '|'（在 [j, i) 内）之后、i 之前
                // ★ 结尾是 `}}` 两个字符 ⇒ 内容末字符下标 = i-2（首版写成 i-1，返回的是 '}'）
                int closeLen = (i - 1 >= 0 && s[i - 1] == '}') ? 2 : 1;
                int contentEnd = i - closeLen;
                int bar = -1;
                for (int k = contentEnd; k > j; k--)
                    if (s[k] == '|') { bar = k; break; }
                if (bar >= 0 && contentEnd > bar)
                    return s[contentEnd];     // ★ 内容末字符 = 可见字符

                i = j - 1;                    // 无内容 ⇒ 继续往前找
            }
            return '\0';
        }
    }
}
