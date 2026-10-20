// 伊什纳家族遗物（Ruin of House Isner）· 中文坐标线索
//
// ═══════════════════════════════════════════════════════════════════════
// 为什么必须做这个
// ═══════════════════════════════════════════════════════════════════════
// 这把枪的位置**只能**通过马尔科夫随机文本（白字书籍、鸟、Euclid、涂鸦）得知。
// 我们换掉了随机文本生成器 ⇒ **必须同时复刻这条线索**，否则玩家永远找不到它。
// 这是功能性 BUG，不是风味问题。
//
// ═══════════════════════════════════════════════════════════════════════
// 官方机制（逆向自 XRL.LoreGenerator.RuinOfHouseIsnerLore(Int32 X, Int32 Y)）
// ═══════════════════════════════════════════════════════════════════════
// 字符串**硬编码在 Assembly-CSharp.dll 的 UTF-16LE 元数据堆**里
// （@10013700–10015390），**没有字符串表 ID** ⇒ 翻译覆盖不了，只能代码层接管。
//
//   1) 随机选地标（`LoreGenerator.landmarks`，7 个）：
//        Red Rock / Grit Gate / the Six Day Stilt / Golgotha /
//        Bethesda Susa / Omonporch / the ruins of Joppa
//   2) dx = X - landmark.X  → dx>=0 东，否则西
//      dy = Y - landmark.Y  → dy>=0 南，否则北   ← 注意 Y 增大是「南」
//   3) Stat.Random(0,1) 随机挑一个轴 ⇒ **一条线索只给一个轴**，
//      完整坐标必须两条线索（一条东西 + 一条南北）
//   4) 列1 介词（Stat.Random(0,40)，每 10 一档）：of the / to the / with the / in the
//   5) 列2 枪的标记（Stat.Random(0,70)，每 10 一档，7 种）
//   6) 列3 距离句式（Stat.Random(0,60)，每 10 一档，6 种）
//   7) 三列拼接后交给 MarkovBook.PostprocessLoadedCorpus **拆词注入**语料
//
// 注入点：`XRL.World.Parts.MarkovBook.PostprocessLoadedCorpus` 读
//   `The.Game.IntGameState["RuinofHouseIsner_xCoordinate" / "...yCoordinate"]`
// 然后**循环 46 次**调 `RuinOfHouseIsnerLore(X, Y)`，
// 每次结果交给 `MarkovChain.AppendSecret(...)`。
//
// ═══════════════════════════════════════════════════════════════════════
// 我们做的两处**改进**（相对官方）
// ═══════════════════════════════════════════════════════════════════════
//   A. **用离枪最近的地标**（官方是随机抽）。枪只在**地图左半 40×25** 生成，
//      随机可能抽到 40 帕拉桑外的地标 ⇒ 线索写「离 X 四十个帕拉桑」，玩家用不上。
//      最近地标实测能把一条轴压到 1~2 帕拉桑。
//   B. 官方「短句档」写死 "under one parasang"（不到一个帕拉桑），**与真实距离无关**，
//      距离 28 也会这么说，会**误导玩家**把搜索范围缩小到 1 帕拉桑。
//      我们保留该档句式，但把数字换成真值（严格更准确，不丢信息）。
//
// 验收脚本：_tools\verify_isner_clue.py（600 条 ×2 线索，六项判据全过）
// 设计文档：_tools\伊什纳线索-逆向与复刻.md

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsIsnerClue
    {
        // ── 地标（中文译名 + 英文原名）
        //
        // ★ 2026-10-04 修正：官方是 **9 个**地标，不是一个 7 个。
        //   运行时字符串堆里 dump 出来的是 7 个，而 `Kyakukya` / `the Yd Freehold`
        //   存放在字符串堆的**别处**，当时被我漏掉。
        //   这 9 个名字与顺序取自官方字段实测输出（启动日志第 162 行）。
        //   坐标在 `Fallback` 里（回退用）；**生产路径读官方字段**。──
        private static readonly string[,] LandmarkNames =
        {
            { "约帕", "Joppa" },
            { "红岩", "Red Rock" },
            { "砾石门", "Grit Gate" },
            { "六日高跷", "the Six Day Stilt" },
            { "戈耳戈塔", "Golgotha" },
            { "贝塞斯达苏萨", "Bethesda Susa" },
            { "卡库基亚", "Kyakukya" },
            { "奥蒙波奇", "Omonporch" },
            { "Yd自由领", "the Yd Freehold" },
        };

        // ── 列1：引导语（对应官方 4 档介词）──
        private static readonly string[] Col1 =
        {
            "据传，", "据说，", "有故事讲，", "老辈人传下来的说法是，"
        };

        // ── 列1 的第一人称版：列3 的 4/5 档是「我把它藏在…」（藏枪者自述），
        //    配「据说」语义打架 ⇒ 这些档改用下面的引导语。
        private static readonly string[] Col1FirstPerson =
        {
            "实话说，", "我承认，", "临死前我写下：", "这事我从没跟人讲过——"
        };

        // ── 列2：枪的标记（对应官方 7 档）──
        private static readonly string[] Col2 =
        {
            "伊什纳家族的遗物",
            "那把杰作手枪「伊什纳家族的遗物」",
            "盛放伊什纳家族遗物的箱子",
            "藏有伊什纳家族遗物的箱子",
            "那把著名的左轮「伊什纳家族的遗物」",
            "伊什纳家族遗物的藏匿处",
            "那把失落已久的杰作手枪",
        };

        private static readonly string[] DistNum =
        {
            "一", "二", "三", "四", "五", "六", "七", "八", "九", "十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "二十一", "二十二", "二十三", "二十四", "二十五", "二十六", "二十七", "二十八",
            "二十九", "三十", "三十一", "三十二", "三十三", "三十四", "三十五", "三十六",
            "三十七", "三十八", "三十九", "四十"
        };

        // ── 官方地标表反射缓存 ──
        private static FieldInfo _landmarksField;
        private static bool _landmarksProbed;
        private static int _landmarkSource;      // 0=未探测 1=官方字段 2=内置回退

        /// <summary>
        /// 内置回退地标表（**只在反射读官方 `LoreGenerator.landmarks` 失败时使用**）。
        ///
        /// ★ 2026-10-04 修正：原先只有 7 项且坐标是**猜的**。现在是官方 **9 项 + 真坐标**
        ///   （取自启动日志「地标表 来源=官方字段」那一行的实测输出）。
        ///   注意：坐标是**世界生成时**的形态，不同版本可能变 ⇒ 正常路径永远读官方字段，
        ///   这份表只为「反射失败时不至于崩/不给错方向」兜底。
        /// </summary>
        private static readonly List<KeyValuePair<string, int[]>> Fallback =
            new List<KeyValuePair<string, int[]>>
        {
            new KeyValuePair<string, int[]>("Joppa", new[] { 11, 22 }),
            new KeyValuePair<string, int[]>("Red Rock", new[] { 11, 20 }),
            new KeyValuePair<string, int[]>("Grit Gate", new[] { 22, 14 }),
            new KeyValuePair<string, int[]>("the Six Day Stilt", new[] { 5, 2 }),
            new KeyValuePair<string, int[]>("Golgotha", new[] { 23, 9 }),
            new KeyValuePair<string, int[]>("Bethesda Susa", new[] { 25, 3 }),
            new KeyValuePair<string, int[]>("Kyakukya", new[] { 27, 20 }),
            new KeyValuePair<string, int[]>("Omonporch", new[] { 53, 4 }),
            new KeyValuePair<string, int[]>("the Yd Freehold", new[] { 67, 17 }),
        };

        /// <summary>
        /// ★ 日志必须**绝不抛出**。
        /// 实测教训：探针环境下 `UnityEngine.Debug.LogWarning` 会抛
        /// `SecurityException: ECall methods must be packaged into a system module`；
        /// 如果日志失败能把 `Make()` 带崩，玩家就会**因为一条日志而拿不到线索**。
        /// 所以所有日志都过这里吞异常。
        /// </summary>
        internal static void SafeLog(string msg, bool warn)
        {
            try
            {
                if (warn) UnityEngine.Debug.LogWarning(msg);
                else UnityEngine.Debug.Log(msg);
            }
            catch { }
        }

        /// <summary>0~40 → 中文数字（官方上界 40，因为地图左半是 40×25）。
        /// 中文习惯：「二」在个位且小于 10 时用「两」，但「十二/二十/二十二」里的「二」不变。</summary>
        public static string Num2Zh(int n)
        {
            if (n <= 0) return "零";
            if (n <= DistNum.Length)
            {
                string s = DistNum[n - 1];
                if (n < 10 && s.Length > 0 && s[0] == '二') s = "两" + s.Substring(1);
                return s;
            }
            return n.ToString();
        }

        /// <summary>读官方 `XRL.LoreGenerator.landmarks`（static Dictionary&lt;string,int[]&gt;）。
        /// 值就是 [X, Y] 世界坐标（单位帕拉桑）—— 这是唯一可靠来源：
        /// `Worlds.xml` 里只有 &lt;cell&gt; 原型，**没有世界坐标**。</summary>
        private static List<KeyValuePair<string, int[]>> GetLandmarks()
        {
            if (!_landmarksProbed)
            {
                _landmarksProbed = true;
                try
                {
                    Type t = AccessTools_TypeByName("XRL.LoreGenerator");
                    if (t != null)
                    {
                        _landmarksField = t.GetField("landmarks",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (_landmarksField != null) _landmarkSource = 1;
                    }
                }
                catch (Exception e)
                {
                    SafeLog("[QudZhsCookbook] 读 LoreGenerator.landmarks 失败：" + e.Message, true);
                }
                if (_landmarkSource == 0)
                {
                    _landmarkSource = 2;
                    SafeLog("[QudZhsCookbook] 未能反射到 LoreGenerator.landmarks，"
                        + "改用内置回退坐标（线索仍可用，但地标位置可能与本局世界不符）。", true);
                }
            }

            if (_landmarkSource == 1)
            {
                try
                {
                    object v = _landmarksField.GetValue(null);
                    Dictionary<string, int[]> d = v as Dictionary<string, int[]>;
                    if (d != null && d.Count > 0)
                    {
                        List<KeyValuePair<string, int[]>> list = new List<KeyValuePair<string, int[]>>();
                        foreach (KeyValuePair<string, int[]> kv in d) list.Add(kv);
                        return list;
                    }
                }
                catch (Exception e)
                {
                    SafeLog("[QudZhsCookbook] 取 landmarks 值失败：" + e.Message, true);
                }
            }
            return Fallback;
        }

        /// <summary>把官方英文地标名映射成中文译名。</summary>
        private static string ChineseName(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            for (int i = 0; i < LandmarkNames.GetLength(0); i++)
            {
                if (string.Equals(LandmarkNames[i, 1], en, StringComparison.OrdinalIgnoreCase))
                    return LandmarkNames[i, 0];
            }
            return en;
        }

        /// <summary>★ 挑**离目标最近**的地标（官方是随机抽，可能抽到 40 帕拉桑外的）。</summary>
        private static bool TryNearestLandmark(int tx, int ty, out string zhName,
                                               out int lx, out int ly, out int dist)
        {
            zhName = null; lx = 0; ly = 0; dist = 0;
            List<KeyValuePair<string, int[]>> lms = GetLandmarks();
            if (lms == null || lms.Count == 0) return false;

            bool found = false;
            int best = int.MaxValue;
            foreach (KeyValuePair<string, int[]> kv in lms)
            {
                int[] xy = kv.Value;
                if (xy == null || xy.Length < 2) continue;
                int d = Math.Abs(tx - xy[0]) + Math.Abs(ty - xy[1]);
                if (!found || d < best)
                {
                    found = true; best = d;
                    zhName = ChineseName(kv.Key);
                    lx = xy[0]; ly = xy[1];
                }
            }
            dist = best;
            return found;
        }

        /// <summary>列3：6 种句式（对应官方 6 档）。short 档保留句式但用真值数字。</summary>
        private static string Col3(int idx, string dir, int dist, string landmark)
        {
            // dist == 0 时中文不能写「零个帕拉桑」，改用「正东/正南 + 不到一个帕拉桑」；
            // ★ 方向词必须保留 —— 否则这条线索就废了（玩家无法确定坐标）。
            if (dist == 0)
                return "就在" + landmark + "正" + dir + "，不到一个帕拉桑。";

            string far = Num2Zh(dist) + "个帕拉桑";
            switch (idx)
            {
                case 0: return "就被存放在" + landmark + dir + far + "的地方。";
                case 1: return "就被存放在" + landmark + dir + far + "的地方。";
                case 2: return "据说被存放在" + landmark + dir + far + "的地方。";
                case 3: return "位于" + landmark + "附近，往" + dir + "约" + far + "。";
                case 4: return "我把它藏在" + landmark + dir + far + "的地方。";
                default: return "我把它收在" + landmark + dir + far + "的地方。";
            }
        }

        /// <summary>
        /// ★ 线索用的随机数发生器。
        ///
        /// 为什么必须是**跨调用持有的同一个实例**（而不是每次 new 一个固定种子）：
        /// `MarkovBook.PostprocessLoadedCorpus` 会**循环 46 次**调
        /// `RuinOfHouseIsnerLore(X, Y)`，官方在循环前先 `Stat.ReseedFrom("HouseIsnerLore")`
        /// 把这个"书页流"的随机数序列固定下来，于是 46 次结果**各不相同**。
        /// 我们若每次 new Random(固定种子) ⇒ **46 条线索一模一样**，
        /// 而一条线索只给一个轴 ⇒ 玩家永远拼不出完整坐标（功能性 BUG）。
        ///
        /// 种子仍由 (X, Y) 派生：同一局同一坐标 ⇒ 同一批线索（与官方意图一致）。
        /// </summary>
        private static Random _rng;
        private static int _rngSeed;
        private static bool _rngReady;

        private static Random RngFor(int tx, int ty)
        {
            int seed = unchecked(tx * 73856093 ^ ty * 19349663);
            if (!_rngReady || seed != _rngSeed)
            {
                _rngSeed = seed;
                _rng = new Random(seed);
                _rngReady = true;
            }
            return _rng;
        }

        /// <summary>
        /// 生成一条中文坐标线索。对应官方 `RuinOfHouseIsnerLore(X, Y)`。
        /// `rng` 传 null 时用内部流式 RNG（**生产路径就该传 null**）。
        /// </summary>
        public static string Make(int targetX, int targetY, Random rng)
        {
            Random R = rng ?? RngFor(targetX, targetY);

            string lmZh; int lx, ly, lmd;
            if (!TryNearestLandmark(targetX, targetY, out lmZh, out lx, out ly, out lmd))
            {
                // 完全拿不到地标表：退回官方 7 个英文名里的第一个，保证不崩
                lmZh = LandmarkNames[0, 0];
                lx = targetX; ly = targetY;
            }

            int dx = targetX - lx, dy = targetY - ly;
            string ew = dx >= 0 ? "东" : "西";
            string ns = dy >= 0 ? "南" : "北";

            // 官方 Stat.Random(0,1) 随机挑轴；我们仍然随机，保证同一本书里两轴都会出现
            // （46 次注入，两条轴各有约 23 次，玩家能拼出完整坐标）
            bool useEw = R.Next(2) == 0;
            string dir = useEw ? ew : ns;
            int dist = Math.Abs(useEw ? dx : dy);

            int i1 = R.Next(Col1.Length);
            int i2 = R.Next(Col2.Length);
            int i3 = R.Next(6);

            // 列3 的 4/5 档自带第一人称主语 ⇒ 用第一人称引导语，不插列2
            // （否则出「伊什纳家族的遗物我把它藏在…」这种双主语）
            if (i3 == 4 || i3 == 5)
            {
                string lead = Col1FirstPerson[R.Next(Col1FirstPerson.Length)];
                return lead + Col3(i3, dir, dist, lmZh);
            }

            string text = Col1[i1] + Col2[i2] + Col3(i3, dir, dist, lmZh);
            // 去重：列1 已含「据说」时，列3 不再重复「据说」
            if (Col1[i1].IndexOf("据说", StringComparison.Ordinal) >= 0)
                text = text.Replace("据说被存放在", "被存放在");
            text = text.Replace("据说据说", "据说");
            return text;
        }

        /// <summary>只用于日志/自检：把 7 个地标及其坐标打出来。</summary>
        public static string DescribeLandmarks()
        {
            List<KeyValuePair<string, int[]>> lms = GetLandmarks();
            if (lms == null || lms.Count == 0) return "(无)";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("来源=").Append(_landmarkSource == 1 ? "官方字段" : "内置回退").Append("；");
            foreach (KeyValuePair<string, int[]> kv in lms)
            {
                int[] xy = kv.Value;
                sb.Append(kv.Key).Append('(').Append(xy != null && xy.Length >= 2
                    ? xy[0] + "," + xy[1] : "?").Append(") ");
            }
            return sb.ToString();
        }

        /// <summary>轻量类型查找（避免对 Harmony/AccessTools 的编译期依赖）。</summary>
        private static Type AccessTools_TypeByName(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(name, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// 启动自检：确认「枪坐标 + 地标表 + 中文线索生成」这条链是通的。
    /// 由 `ZhsBootstrap` 在补丁安装后调用一次。
    ///
    /// 为什么要专门自检：这条线索是**功能性**的 —— 断了玩家就找不到那把枪，
    /// 而且它不报错、只是悄悄变成风味文本，所以必须主动打日志留痕。
    /// </summary>
    internal static class ZhsIsnerClueSelfCheck
    {
        private static bool _done;

        public static void Run()
        {
            if (_done) return;
            _done = true;

            int tx = 0, ty = 0;
            bool haveCoord = false;
            try
            {
                Type theType = AccessTools_TypeByName("XRL.The");
                object game = theType == null ? null : Prop(theType, "Game");
                object dict = game == null ? null : Field(game, "IntGameState");
                System.Collections.IDictionary d = dict as System.Collections.IDictionary;
                if (d != null)
                {
                    haveCoord = d.Contains("RuinofHouseIsner_xCoordinate")
                             && d.Contains("RuinofHouseIsner_yCoordinate");
                    if (haveCoord)
                    {
                        tx = Convert.ToInt32(d["RuinofHouseIsner_xCoordinate"]);
                        ty = Convert.ToInt32(d["RuinofHouseIsner_yCoordinate"]);
                    }
                }
            }
            catch (Exception e)
            {
                ZhsIsnerClue.SafeLog("[QudZhsCookbook] 伊什纳线索自检：读世界坐标失败（" + e.Message + "）", true);
            }

            ZhsIsnerClue.SafeLog("[QudZhsCookbook] 伊什纳线索自检：地标表 "
                + ZhsIsnerClue.DescribeLandmarks(), false);

            if (!haveCoord)
            {
                ZhsIsnerClue.SafeLog("[QudZhsCookbook] 伊什纳线索自检：本局尚未生成枪坐标"
                    + "（世界还没建好，或该局没有这把枪）—— 线索将在书页生成时按需产出。", false);
                return;
            }

            ZhsIsnerClue.SafeLog("[QudZhsCookbook] 伊什纳线索自检：本局枪坐标 = (" + tx + ", " + ty
                + ")，最近地标线索示例：", false);
            Random r = new Random(unchecked(tx * 73856093 ^ ty * 19349663));
            for (int i = 0; i < 3; i++)
                ZhsIsnerClue.SafeLog("[QudZhsCookbook]   " + ZhsIsnerClue.Make(tx, ty, r), false);
        }

        private static Type AccessTools_TypeByName(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(name, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object Prop(Type t, string name)
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance);
            return p == null ? null : p.GetValue(null, null);
        }

        private static object Field(object instance, string name)
        {
            if (instance == null) return null;
            FieldInfo f = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return f == null ? null : f.GetValue(instance);
        }
    }
}
