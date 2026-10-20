// ═══════════════════════════════════════════════════════════════════════════
// 墓碑排版修正 v2 —— 按**显示宽度**重排（CJK 1 字 = 2 列）
// ═══════════════════════════════════════════════════════════════════════════
//
// 【游戏原实现】（反编译 XRL.World.Text.Delegates.PostProcessors::Tombstone）
//     int num = 31;                     // 框内宽（字符数）
//     int num2 = 3; int count = 3;
//     int WrapWidth = num - num2 * 2;   // 25
//     list = value.Split('\n').SelectMany(
//                line => StringFormat.ClipText(line, WrapWidth).Split('\n'))  // ① 按字符数折行
//     …
//     int num3 = ColorUtility.LengthExceptFormatting(list[i]);                // ② 按字符数
//     int num4 = (num - num3) / 2;                                            //    居中
//     int count2 = num - num3 - num4;                                         //    右填充
//
//   中文 1 字占 2 列 ⇒ ①折行溢出/浪费 ②居中偏移 ③右边框错位。
//
// 【v1 的教训（已回滚）】
//   v1 在**已装好框的输出**上用 ╔║═ 反向拆分再重排 —— 正文里一旦出现
//   同样的制表符或跨行颜色标记，拆分即错位 ⇒ **碑文被吃掉**。
//   ⇒ 绝不在输出上做反向解析。
//
// 【v2】
//   Prefix 整体替换，直接在**输入**上处理；用官方迭代器
//   `ColorUtility.GetNextSpanWithoutFormatting` 逐「可见段」扫描，
//   它在遇到 `{{…}}` / `&X` / PUA 控制符时整段跳过 ⇒ **格式标记永不被切断**。
//
// 开关：装机目录放 `_zhs_tombstone_off.txt` 即停用本补丁。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsTombstoneLayout
    {
        private const string Tag = "[QudZhsCookbook] 墓碑排版：";

        internal static bool Disabled;
        private static bool _applied;
        private static int _hits;

        // ── 与官方一致的框参数（单位：显示列）──
        private const int BOX_INNER = 31;     // 顶/底横线长度（**官方值，不动**）
        private const int LEFT_PAD = 3;       // 左边距（NBSP）
        private const int CONTENT_W = BOX_INNER - LEFT_PAD * 2;   // = 25 官方内容可用列

        /// <summary>
        /// ★ 实际折行宽度 = **20 显示列 = 中文 10 字**（用户 2026-10-07 裁定）。
        ///
        /// 【为什么比官方的 25 窄】
        ///   官方 `WrapWidth = 31 - 3*2 = 25` 是按**字符数**设计的；
        ///   中文 1 字占 **2 列** ⇒ 25 列能塞 12.5 个汉字，长碑文会显得很挤。
        ///   用户要求**中文每行 10 字** ⇒ 取 20 列。
        ///
        /// 【框宽为何不同步缩窄（用户选择「框宽不变」）】
        ///   `BOX_INNER` 保持官方 31 列 ⇒ 正文居中后左右各多留约 2.5 列空白，
        ///   视觉更松动；且**不改变框宽**就不会影响其它依赖固定框宽的排版。
        /// </summary>
        private const int BODY_W = 20;        // = 中文 10 字

        /// <summary>
        /// ★★ 2.16.47：**加上 `Urn`（食者瓮）**。
        ///
        /// 它是与墓碑**完全同构**的后处理器（同样的 `num=31 / num2=3 / count=3`、
        /// 同样的 `#` 系框 `╔═╗║╟╢╚╝`、同样 `ClipText(line, 25)` 折行），
        /// 但我们**从来没接管过它** —— 于是瓮的正文一直按官方的 25 列折行 + 标记不感知，
        /// 表现为「框行 >36 列 / 正文 >20 列」。
        ///
        /// 为什么之前没发现：`audit_tombstones*.py` 的「形态 A」是按**框字符**分类的
        /// （`[╔╚╢╟║]`），而墓碑与瓮**共用同一套框字符** ⇒ 两者被混在一起统计。
        /// 实测佐证：`Player.log` 的 ClipText 诊断里 30 条**全部**来自 `PostProcessors.Urn`。
        /// </summary>
        private static readonly string[] TARGETS = { "Tombstone", "TombstoneRebekah", "Urn" };

        // ══════════════════════════════════════════════════════════
        // ★★ 2.16.43（方案 C）：把**游戏自己的**折行宽从 25 夹到 20
        // ══════════════════════════════════════════════════════════
        //
        // 【为什么必须从源头夹】
        //   反编译 `PostProcessors::Tombstone`：
        //       int num = 31; int num2 = 3;
        //       int WrapWidth = num - num2 * 2;        // ★ = 25（框内宽）
        //       Context.Value.ToString().Split('\n')
        //         .SelectMany(line => StringFormat.ClipText(line, WrapWidth).Split('\n'))
        //   ⇒ 游戏**先用 `ClipText(line, 25)` 折一遍**，才把文本交给我们的 `Layout`。
        //   ⇒ 而 `ClipText` 是「按词折行 + 只在**整串末尾**补 `}}`」的
        //      ⇒ 长标记会被它从中间切开，留下**不配平**的标记：
        //            `{{|被坠落的碳化物折叠锤`  +  `砸死}}`
        //   ⇒ 我们收到的是**已被切坏的片段**，再怎么折也补不回标记
        //      ⇒ 那几条正文永远超过 20 列（实测 13/64 条）。
        //
        // 【本补丁】`ClipText` 的 Prefix：当调用方是 `PostProcessors.Tombstone` /
        //   `TombstoneRebekah` 时，把 `MaxWidth` 从 25 夹到 `BODY_W`(20)。
        //   ⇒ 折行仍由**游戏自己那个标记感知的折行器**做（它会在换行时正确
        //     续接控制码），我们只是把宽度口径统一到 20 列。
        //
        // 【为什么用「看调用栈」定位】
        //   `ClipText` 是全局工具，很多界面都调它。直接夹宽会波及全游戏。
        //   调用栈里出现 `PostProcessors.Tombstone` 才是我们要的那两处。
        //
        // 【防自证】只写「已挂」不算数（§46）⇒ 首次真正夹成功时打一条日志。
        internal const string ClipTextTag = "[QudZhsCookbook] 墓碑折行宽：";
        internal static int ClipClampHits;

        /// <summary>本栈帧是否来自墓碑后处理器（只看前若干帧，避免无谓开销）。</summary>
        private static bool CalledFromTombstone()
        {
            return TombstoneCaller(out _) != null;
        }

        /// <summary>
        /// 找「墓碑 / 瓮语境」的那一帧，并回传它的 `DeclaringType.Name`（用于日志定位路径）。
        /// 认三处：`PostProcessors.Tombstone` / `TombstoneRebekah` / **`Urn`**
        /// （★ `Urn` 与墓碑同构，2.16.47 起一并接管）。
        /// </summary>
        private static string TombstoneCaller(out string methodName)
        {
            methodName = null;
            var st = new System.Diagnostics.StackTrace(false);
            int n = st.FrameCount;
            if (n > 32) n = 32;
            for (int i = 0; i < n; i++)
            {
                System.Reflection.MethodBase mb = st.GetFrame(i).GetMethod();
                if (mb == null) continue;
                if (mb.Name == "Tombstone" || mb.Name == "TombstoneRebekah"
                    || mb.Name == "Urn")
                {
                    methodName = mb.Name;
                    return mb.DeclaringType == null ? "?" : mb.DeclaringType.Name;
                }
            }
            return null;
        }

        private static bool DiagIsTombstoneContext()
        {
            return TombstoneCaller(out _) != null;
        }

        /// <summary>
        /// `XRL.UI.StringFormat.ClipText(string Input, int MaxWidth, …)` 的 Prefix。
        /// 只改 `MaxWidth`（`__1`）⇒ 其余行为完全沿用官方实现。
        ///
        /// ★★ 条件用「墓碑栈 + 宽度 > BODY_W」，**不写死 25**：
        ///   反编译 `PostProcessors::Tombstone` 是 `WrapWidth = 31 - 3*2 = 25`，
        ///   但实测导出里首段断在 **23 列**（= `31 - 4*2`）⇒ **还有另一条路径**
        ///   先用 23 折了一遍。写死 25 会漏掉它（第一版就是这么漏的）。
        /// </summary>
        private static void ClipTextPrefix(ref int __1)
        {
            if (Disabled) return;
            // ★★ 诊断（临时）：只记**墓碑/瓮**语境里的 ClipText 调用（宽度 > 20）。
            //    —— 上一版的诊断被 `PostProcessors.Urn` 的 30 条塞满，
            //       墓碑那条根本没轮到；现在按「栈里有 Tombstone/Urn/Rebekah」过滤。
            if (DiagClipCalls < 30 && __1 > BODY_W && DiagIsTombstoneContext())
            {
                DiagClipCalls++;
                UnityEngine.Debug.Log(ClipTextTag + "诊断 #" + DiagClipCalls
                    + " MaxWidth=" + __1 + " 直接调用方=" + DirectCaller());
            }
            if (__1 <= BODY_W) return;          // 已经不宽了，无需夹
            string caller = TombstoneCaller(out string mname);
            if (caller == null) return;         // 不是墓碑语境 ⇒ 一律不动
            int old = __1;
            __1 = BODY_W;
            ClipClampHits++;
            if (ClipClampHits <= 3)
            {
                UnityEngine.Debug.Log(ClipTextTag + "第 " + ClipClampHits
                    + " 次把折行宽 " + old + " 夹到 " + BODY_W
                    + "（调用方=" + caller + "." + mname + "）");
            }
        }

        internal static int DiagClipCalls;

        /// <summary>诊断用：取**直接**调用方（栈里第一帧非本类的）。</summary>
        private static string DirectCaller()
        {
            var st = new System.Diagnostics.StackTrace(false);
            var sb = new StringBuilder();
            int shown = 0;
            int n = st.FrameCount > 40 ? 40 : st.FrameCount;
            for (int i = 0; i < n && shown < 5; i++)
            {
                System.Reflection.MethodBase mb = st.GetFrame(i).GetMethod();
                if (mb == null) continue;
                string tn = mb.DeclaringType == null ? "?" : mb.DeclaringType.Name;
                if (tn == "ZhsTombstoneLayout") continue;
                sb.Append(shown == 0 ? "" : " → ").Append(tn).Append('.').Append(mb.Name);
                shown++;
            }
            return sb.Length == 0 ? "(未知)" : sb.ToString();
        }

        private static bool _clipApplied;

        /// <summary>挂 `ClipText` 的宽度夹取（幂等：只挂一次）。</summary>
        private static void TryApplyClipTextPatch()
        {
            if (_clipApplied) return;
            _clipApplied = true;
            try
            {
                Type sf = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        sf = asm.GetType("XRL.UI.StringFormat", false);
                        if (sf != null) break;
                    }
                    catch { }
                }
                if (sf == null)
                {
                    UnityEngine.Debug.LogError(ClipTextTag + "找不到 XRL.UI.StringFormat");
                    return;
                }
                // 重载有 3 个（5/4/3 参数）；我们要签名最全的那个（第 2 个参数是 int MaxWidth）
                MethodInfo m = null;
                foreach (MethodInfo mi in sf.GetMethods(
                             BindingFlags.Public | BindingFlags.Static))
                {
                    if (mi.Name != "ClipText") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(int))
                    {
                        if (m == null || ps.Length > m.GetParameters().Length) m = mi;
                    }
                }
                if (m == null)
                {
                    UnityEngine.Debug.LogError(ClipTextTag + "找不到 ClipText(string,int,…)");
                    return;
                }
                MethodInfo pre = typeof(ZhsTombstoneLayout).GetMethod(
                    "ClipTextPrefix", BindingFlags.NonPublic | BindingFlags.Static);
                Harmony h = new Harmony("Nattens.QudZhsCookbook.tombstone.cliptext");
                h.Patch(m, prefix: new HarmonyMethod(pre));
                UnityEngine.Debug.Log(ClipTextTag + "已挂 ClipText 宽度夹取（"
                    + m.GetParameters().Length + " 参数重载 · 25 → " + BODY_W + "）");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(ClipTextTag + "挂载失败：" + e);
            }
        }

        public static void TryApply()
        {
            if (_applied) return;
            _applied = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("XRL.World.Text.Delegates.PostProcessors", false);
                        if (t != null) break;
                    }
                    catch { }
                }
                if (t == null)
                {
                    UnityEngine.Debug.LogError(Tag + "找不到 PostProcessors");
                    return;
                }
                MethodInfo pre = typeof(ZhsTombstoneLayout).GetMethod(
                    "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                foreach (string name in TARGETS)
                {
                    MethodInfo m = t.GetMethod(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) continue;
                    Harmony h = new Harmony("Nattens.QudZhsCookbook.tombstone." + name);
                    h.Patch(m, prefix: new HarmonyMethod(pre));
                    n++;
                }
                UnityEngine.Debug.Log(Tag + "已挂 " + n + " 个出口（宽度感知）");
                // ★ 2.16.43：再从**源头**把游戏自己的折行宽夹到 BODY_W（方案 C）
                TryApplyClipTextPatch();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "挂载失败：" + e);
            }
        }

        /// <summary>
        /// 取 `VariableContext.Value`。
        ///
        /// ★★ 2.16.39：它是 **public readonly 字段**，不是属性 ——
        ///   `XRL.World.Text.Delegates.VariableContext`：
        ///       public readonly TextBuilder Value = new CompositedTextBuilder(128);
        ///   旧代码用 `GetProperty("Value")` ⇒ **恒返回 null** ⇒ Prefix 在这里
        ///   **静默 `return true`**（连日志都没有）⇒ 本补丁自 2.15.x 起
        ///   **一次都没生效过**：`Player.log` 只有「已挂 2 个出口」，
        ///   永远没有「首次生效」。实测 `test_tombstones.txt` 错位 45/192 就是**原实现**的效果。
        ///   （同族的 bug 见 `ZhsPlaqueWidthPatch` 的 `Value 属性拿不到` 84/84 条诊断。）
        /// </summary>
        private static object ValueOf(object ctx)
        {
            Type t = ctx.GetType();
            FieldInfo f = t.GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(ctx);
            PropertyInfo p = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return p == null ? null : p.GetValue(ctx, null);
        }

        private static bool _missLogged;

        /// <summary>取不到 Value 时**必须留痕** —— 静默失败害了这个补丁一整轮。</summary>
        private static void TraceOnce(string msg)
        {
            if (_missLogged) return;
            _missLogged = true;
            UnityEngine.Debug.LogError(Tag + msg + " ⇒ 补丁未生效（回退原实现）");
        }

        // ══════════════════════════════════════════════════════════
        // ★★ 2.16.49 诊断：墓碑/瓮的**逐次原文记账**
        // ══════════════════════════════════════════════════════════
        //
        // 目的：导出里那批「标记不配平、内容断在半句」的块（`{{|因失去一条链甲手套的`）
        //       到底是谁弄坏的 —— 瓮**生成端**就残缺，还是我们**排版**时弄丢？
        //       旧日志只记前 2 次「首次生效」且被墓碑占满 ⇒ 瓮的原文从没出现过。
        //
        // 做法：按**处理器名**分开计数（Tombstone / TombstoneRebekah / Urn 各记前 N 次），
        //       每次记：序号 · 长度 · 是否含 `{{` · **标记是否配平** · 原文指纹。
        //       「配平」是关键判据：
        //           配平 + 导出里坏  ⇒ 我们排版弄丢的（★ 我们的 bug）
        //           不配平          ⇒ 上游就残缺（★ 生成端的问题）
        private const int RAW_LOG_PER_PROC = 12;
        private const string RawLogName = "_zhs_tomb_raw.log";

        private static readonly Dictionary<string, int> _rawCounts =
            new Dictionary<string, int>();
        private static bool _rawLogInit;

        /// <summary>
        /// ★★ 2.16.62 已废弃：原「从程序集位置上溯找 manifest.json」恒返回 null
        /// （本项目没有 DLL，游戏运行时从 `Scripts\*.cs` 现场编译）。
        /// 现由共用工具 `ZhsFileUtil` 统一提供落盘目录。
        /// </summary>
        // （原 `FindModDir()` 已删除）

        private static void LogRawOnce(string raw)
        {
            try
            {
                string proc = TombstoneCaller(out string mname) ?? "(未知)";
                int n;
                _rawCounts.TryGetValue(proc, out n);
                if (n >= RAW_LOG_PER_PROC) return;
                _rawCounts[proc] = n + 1;

                int opens = 0, closes = 0;
                for (int i = 0; i + 1 < raw.Length; i++)
                {
                    if (raw[i] == '{' && raw[i + 1] == '{') { opens++; i++; }
                    else if (raw[i] == '}' && raw[i + 1] == '}') { closes++; i++; }
                }
                bool hasMk = opens > 0 || closes > 0;
                bool balanced = opens == closes;

                var sb = new StringBuilder();
                if (!_rawLogInit)
                {
                    _rawLogInit = true;
                    sb.Append("=== 墓碑/瓮 原文记账（每处理器前 ")
                      .Append(RAW_LOG_PER_PROC).Append(" 次）===\n");
                }
                sb.Append(proc).Append(" #").Append(n + 1)
                  .Append(" len=").Append(raw.Length)
                  .Append(" {{=").Append(opens).Append(" }}=").Append(closes)
                  .Append(balanced ? " ✅配平" : " ★不配平")
                  .Append(hasMk ? "" : "（无标记）")
                  .Append("\n    指纹: ").Append(RawDump(raw, 120)).Append('\n');

                // ★★ 2.16.62：改用共用工具 `ZhsFileUtil`。
                //   旧实现用 `Assembly.GetExecutingAssembly().Location` 上溯找
                //   `manifest.json` —— **本项目没有 DLL**（游戏运行时从 `Scripts\*.cs`
                //   现场编译）⇒ 恒返回 null ⇒ 本日志**从来没产出过**（KNOWN-ISSUES 记过）。
                string dir = ZhsFileUtil.WriteDir;
                if (dir != null)
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, RawLogName), sb.ToString(),
                        new System.Text.UTF8Encoding(false));
                }
                else
                {
                    // 兜底：至少进 Player.log
                    UnityEngine.Debug.Log(Tag + sb.ToString().Replace("\n", " | "));
                }
            }
            catch { }
        }

        /// <summary>诊断用特征串：命中就把折行细节打出来（默认永不命中）。</summary>
        internal const string WrapDumpNeedle = "@@WRAPDUMP@@";

        /// <summary>
        /// 按需 dump 折行细节：原文含 `WrapDumpNeedle` 时，
        /// 打印**每个折行单元**的 Pre / Text / Post / Width，以及折行后每行的可见宽。
        /// 用来回答「为什么某行内容会有 11 字（22 列）」。
        /// </summary>
        private static void DumpWrapDetail(string raw, List<string> lines)
        {
            try
            {
                if (raw == null || raw.IndexOf(WrapDumpNeedle, StringComparison.Ordinal) < 0)
                    return;
                var sb = new StringBuilder();
                sb.Append("原文: ").Append(RawDump(raw, 200)).Append('\n');
                sb.Append("折行后 ").Append(lines.Count).Append(" 行：\n");
                for (int i = 0; i < lines.Count; i++)
                {
                    sb.Append("   [").Append(i).Append("] 可见宽=")
                      .Append(ZhsPlaqueLayout.DisplayWidth(lines[i]))
                      .Append(" 原样=").Append(RawDump(lines[i], 120)).Append('\n');
                }
                UnityEngine.Debug.Log(Tag + "折行 dump：\n" + sb);
            }
            catch { }
        }

        /// <summary>Prefix：整体替换。返回 false = 跳过原实现。</summary>
        private static bool Prefix(object __0)        {
            if (Disabled) return true;
            try
            {
                if (__0 == null) return true;

                // Context.Value（TextBuilder）—— 见 `ValueOf` 的注释（字段 vs 属性的坑）
                object tb = ValueOf(__0);
                if (tb == null) { TraceOnce("Value 拿不到（属性与字段都没有）"); return true; }
                string raw = tb.ToString();
                if (string.IsNullOrEmpty(raw)) return true;

                // ★★ 2.16.49 诊断：**逐次**记录墓碑/瓮的原文（前若干次）。
                //    为什么需要：旧实现只记前 2 次「首次生效」，两次都被墓碑占掉，
                //    于是瓮的原文从没进日志 ⇒ 导出里那批「标记不配平、内容断在半句」
                //    的块**无法归因**（究竟是瓮生成端就残缺，还是我们排版弄丢）。
                //    本记账按**处理器**分开计数，并给出「标记是否配平 + 指纹」，
                //    使导出里每个坏块都能在日志里对上号。
                LogRawOnce(raw);

                // 打开输出流
                MethodInfo clear = null;
                foreach (MethodInfo mi in tb.GetType().GetMethods(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name == "Clear" && mi.GetParameters().Length == 0)
                    {
                        clear = mi;
                        break;
                    }
                }
                if (clear == null) return true;
                object sink = clear.Invoke(tb, null) ?? tb;

                List<string> lines = Layout(raw);
                // ★★ 2.16.49 诊断：把「折行后的每行宽度」也记下来（只对命中的特征串）。
                //    目的：见到「第 2 行内容 11 字（22 列）」这种反例时，
                //    能直接判断是**折行器**没折，还是**别处**又拼了回去。
                DumpWrapDetail(raw, lines);
                Emit(sink, lines, raw);

                _hits++;
                if (_hits <= 2)
                {
                    var dbg = new StringBuilder();
                    dbg.Append(Tag).Append("首次生效（内容行 ").Append(lines.Count)
                       .Append("，按显示宽度折行/居中）");
                    dbg.Append("\n  原文字符数=").Append(raw.Length)
                       .Append("，转义前 200 字符：\n    ").Append(RawDump(raw, 200));
                    dbg.Append("\n  折行后各行显示宽度：");
                    for (int i = 0; i < lines.Count && i < 24; i++)
                        dbg.Append(' ').Append(DisplayWidth(lines[i]));
                    dbg.Append("（正文宽 ").Append(BODY_W)
                .Append(" 列 = 中文 ").Append(BODY_W / 2).Append(" 字 · 框内宽 ")
                .Append(BOX_INNER)
                       .Append("，含框边应为 ").Append(BOX_INNER + LEFT_PAD * 2 + 2)
                       .Append("）");
                    UnityEngine.Debug.Log(dbg.ToString());
                }
                return false;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(Tag + "出错（回退原实现）：" + e.Message);
                return true;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 排版：折行 + 补空行（不含装框）
        // ══════════════════════════════════════════════════════════
        private static List<string> Layout(string raw)
        {
            var outp = new List<string>();
            int stripped = 0;
            foreach (string para in raw.Split('\n'))
            {
                // ★★ 2.16.45：**剥掉行首尾的 NBSP**（上游有可能已经做过缩进/居中）。
                //   为什么必须剥：NBSP 会被计入显示宽度 ⇒ 制造**假折行**
                //   （实测 `死于舌腐（Glotrot）病。` 被拆成 `死于舌腐（Glotrot）` + `病。`，
                //     就是行首 4 个 NBSP 把 7 列挤掉的结果；而它整句只有 24 列，
                //     在 31 列框内本可放一行或按 20 折得更自然）。
                //   缩进/居中由本补丁的 `Emit` 统一负责 ⇒ 上游缩进一律丢弃。
                string p = para;
                if (p.Length > 0)
                {
                    string t = p.Trim('\u00a0', ' ', '\t', '\r');
                    if (t.Length != p.Length) stripped++;
                    p = t;
                }
                if (p.Length == 0)
                {
                    outp.Add("");
                    continue;
                }
                foreach (string seg in WrapWidth(p, BODY_W))
                    outp.Add(seg);
            }
            outp.Insert(0, "");
            outp.Add("");
            if (stripped > 0 && _padStrippedLogged < 2)
            {
                _padStrippedLogged++;
                UnityEngine.Debug.Log(Tag + "剥掉 " + stripped + " 行的上游缩进（NBSP/空格）后重排");
            }
            return outp;
        }

        private static int _padStrippedLogged;

        /// <summary>
        /// ★★ 2.16.40：**改为复用 `ZhsPlaqueLayout` 的折行/宽度实现**（§36「共用纯函数」）。
        ///
        /// 原来这里自带一套 `WrapWidth` + `VisibleSpan` + `DisplayWidth` + `IsWide`，有两个 bug：
        ///   ① `VisibleSpan` 的「可见字符」分支在收完首字后**无条件继续收后续所有可见字符**
        ///      ⇒ 返回的是**整段**而不是一个字素 ⇒ `segW` 一大，
        ///        `if (curW > 0 &amp;&amp; curW + segW > width)` 因 `curW == 0` 不成立
        ///      ⇒ **整段塞进一行，永不折开**
        ///      （实测：`{{|忘了一个闪耀手榴弹 Mk III}}` 30 列未折，
        ///         `死于舌腐（Glotrot）病。` 23 列未折）
        ///   ② `DisplayWidth` **不跳过 `{{…}}` 标记**
        ///      ⇒ `{{|寿终正寝}}` 按 13 列算（实际 8 列）⇒ 居中偏移
        ///
        /// 实测（`_tools\audit_tombstones3.py`，2.16.39 的导出）：
        ///   · 形态 A（Tombstone 路径，本文件）：正确 187 · 错位 4 · 正文超 20 列 16 条
        ///   · 形态 B（VaryPlaque 路径，走 `ZhsPlaqueLayout`）：正确 360 · 错位 0 · 超宽 0 条
        /// ⇒ 后者是对的，故直接复用。
        /// </summary>
        private static List<string> WrapWidth(string line, int width)
        {
            return ZhsPlaqueLayout.WrapByWidth(line, width);
        }

        private static int DisplayWidth(string s)
        {
            return ZhsPlaqueLayout.DisplayWidth(s);
        }

        // ══════════════════════════════════════════════════════════
        // 装框（结构与官方逐字一致）
        // ══════════════════════════════════════════════════════════
        private static void Emit(object sink, List<string> lines, string raw)
        {
            var sb = new StringBuilder();
            string NB = "\u00a0";
            string H = "\u2550";                 // ═
            string DR = "\u2554", DL = "\u2557"; // ╔ ╗
            string UR = "\u255a", UL = "\u255d"; // ╚ ╝
            string V = "\u2551";                 // ║
            string VL = "\u255f", VR = "\u2562"; // ╟ ╢

            sb.Append(Rep(NB, LEFT_PAD)).Append(DR).Append(Rep(H, BOX_INNER)).Append(DL);
            sb.Append('\n');

            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(Rep(NB, LEFT_PAD));
                sb.Append(i % 2 == 0 ? V : VL);
                int w = DisplayWidth(lines[i]);
                int left = Math.Max(0, (BOX_INNER - w) / 2);
                int right = Math.Max(0, BOX_INNER - w - left);
                sb.Append(Rep(NB, left));
                sb.Append(lines[i]);
                sb.Append(Rep(NB, right));
                sb.Append(i % 2 == 0 ? V : VR);
                sb.Append('\n');
            }

            sb.Append(Rep(NB, LEFT_PAD)).Append(UR).Append(Rep(H, BOX_INNER)).Append(UL);

            AppendText(sink, sb.ToString());
        }


        /// <summary>
        /// 把原文转成**可见转义形式**，便于在日志里看清真实结构：
        ///   `\n` `\t` `\r` 显式化；控制字符 / PUA / 非 ASCII 用 `\uXXXX`；
        ///   ASCII 可见字符原样保留。
        /// </summary>
        private static string RawDump(string s, int max)
        {
            if (s == null) return "(null)";
            var sb = new StringBuilder();
            int n = Math.Min(s.Length, max);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\u00a0') sb.Append("\\u00a0");
                else if (c >= 0x20 && c < 0x7f) sb.Append(c);
                else if (c >= 0xE000 && c <= 0xF8FF) sb.Append("\\u").Append(((int)c).ToString("X4"));
                else if (c >= 0x20) sb.Append(c);          // 普通 CJK 原样显示
                else sb.Append("\\u").Append(((int)c).ToString("X4"));
            }
            if (s.Length > max) sb.Append("…(共 ").Append(s.Length).Append(" 字符)");
            return sb.ToString();
        }

        private static string Rep(string s, int n)
        {
            if (n <= 0) return "";
            var sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        /// <summary>把字符串写进 TextBuilder（反射找 Append(string)）。</summary>
        private static void AppendText(object sink, string text)
        {
            if (sink == null || text == null) return;
            Type t = sink.GetType();
            foreach (MethodInfo mi in t.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mi.Name != "Append") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length != 1) continue;
                if (ps[0].ParameterType == typeof(string))
                {
                    mi.Invoke(sink, new object[] { text });
                    return;
                }
            }
            // 兜底：Append(char) 逐字
            foreach (MethodInfo mi in t.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mi.Name != "Append") continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(char))
                {
                    foreach (char c in text) mi.Invoke(sink, new object[] { c });
                    return;
                }
            }
            throw new InvalidOperationException("TextBuilder 没有可用的 Append");
        }
    }
}
