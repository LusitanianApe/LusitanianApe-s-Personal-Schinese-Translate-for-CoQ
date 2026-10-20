// 随机书页改造 · 中文生成器（句模 + 槽位填充）
//
// 取代旧的「字级 3 阶马尔科夫链」（ZhsBookCorpus，100 句固定语料）。
//
// 设计（详见 _tools\随机书页生成-大改造方案.md）：
//   1. **句模**是骨架：一串「固定中文片段」与「槽位引用」交替。
//   2. **词库**按语义槽分组（ZhsMarkovLexicon，由 Python 侧生成）：
//        词 | 量词表 | 材质类型
//      量词**按词走**（纸页→张/叠），不再从整槽乱抽。
//   3. **相容矩阵是穷举的**：每个动词显式列出允许的材质 ——
//      因为「标签求交」会放过「铬会结籽」这类语法通顺但物理荒唐的搭配。
//   4. **因果方向：先定动词 → 再挑材质相容的名词**。
//      反过来做（先挑名词再挑动词）会出「灯眠了整季」—— 因为动词槽自己也会挑名词。
//   5. **全句材质一致**：主名词/宾语/材料槽共用同一材质，否则出
//      「用铅做了账簿，裂了」（铅=metal，账簿=woven，自相矛盾）。
//   6. 同一 seed ⇒ 同一页（游戏里同一本书每次打开内容一致）。
//
// 旧方案的问题（为什么换掉）：
//   * 语料只有 100 句固定文本，换个 seed 也只是从那 100 句里抽；
//   * 字级链会拼出半句（"他们饮井里的人多半没去过塔…"）；
//   * 新版 30 页就产出 309 个不同句子（去重率 96.6%）。

using System;
using System.Collections.Generic;
using System.Text;

namespace Nattens.QudZhsCookbook
{
    /// <summary>抽中的名词：词 + 量词 + 材质类型。</summary>
    internal struct ZhsPick
    {
        public string W;
        public string Cls;
        public string Kind;
        public string Slot;
    }

    internal sealed class ZhsMarkovGen
    {
        private readonly Random Rng;

        // 带「材质」的名词槽（动词门控与全句材质一致只作用于这些）
        private static readonly string[] KindSlots = { "N_OBJECT", "N_NATURE", "N_MATERIAL" };
        private static readonly string[] NounSlots =
        {
            "N_ABSTRACT", "N_NATURE", "N_PLACE", "N_TIME", "N_BODY",
            "N_PERSON", "N_GROUP", "N_OBJECT", "N_MATERIAL", "N_RESONANT", "N_SIMILE"
        };

        // 句模槽位的「量词占位」前缀；渲染时若前文不是「一/这/那/有」则丢弃
        private const string ClsMark = "\u0001CLS\u0001";

        public ZhsMarkovGen(int seed) { Rng = new Random(seed); }

        // ══════════════════════════════════════════════════════════
        // 句模库（骨架层）
        //   "N_X"            → 名词（前接「一/这/那/有」时补量词）
        //   "N_X|bare"       → 名词，不补量词
        //   "ADJ"            → 形容词 + 的
        //   "V_CHANGE" 等    → 动词（受相容门控）
        //   "MEASURE"        → 时长短语
        // ══════════════════════════════════════════════════════════
        private static readonly string[][] FramesOpen =
        {
            new[] { "很久以前，", "N_PLACE", "里有一", "N_OBJECT", "，它记得", "N_ABSTRACT|bare", "。" },
            new[] { "他们说", "N_PLACE|bare", "的尽头有", "N_OBJECT", "，", "N_PERSON", "们都不肯靠近。" },
            new[] { "那年", "N_TIME|bare", "，", "N_NATURE", "落满了", "N_PLACE|bare", "。" },
            new[] { "我在", "N_PLACE|bare", "买过", "N_OBJECT", "，卖的人说它来自", "N_PLACE|bare", "。" },
            new[] { "N_TIME|bare", "的", "N_NATURE|bare", "是", "ADJ", "，", "N_PERSON", "们这样说。" },
            new[] { "N_PERSON", "说，", "N_PLACE|bare", "底下埋着", "N_GROUP", "的", "N_ABSTRACT|bare", "。" },
        };

        private static readonly string[][] FramesBody =
        {
            new[] { "N_NATURE", "沿着", "N_OBJECT|bare", "往下走，走到", "MEASURE", "便成了", "N_NATURE|bare", "。" },
            new[] { "N_PERSON", "用", "N_MATERIAL|bare", "做了", "N_OBJECT", "，", "V_CHANGE", "了", "MEASURE", "。" },
            new[] { "凡是", "V_SPEECH", "过", "N_ABSTRACT|bare", "的", "N_GROUP", "，都记得那", "N_ABSTRACT|bare", "。" },
            new[] { "N_PLACE|bare", "的", "N_RESONANT|bare", "只在", "N_TIME|bare", "响，响声像", "N_SIMILE|bare", "。" },
            new[] { "他们把", "N_ABSTRACT|bare", "写进", "N_OBJECT|bare", "，又用", "N_MATERIAL|bare", "封住。" },
            new[] { "从", "N_PLACE|bare", "到", "N_PLACE|bare", "，要走", "MEASURE", "那么久。" },
            new[] { "N_GROUP", "把", "N_OBJECT|bare", "传给后人，规矩是", "V_SPEECH", "之前不许打开。" },
            new[] { "N_PERSON", "们知道", "N_MATERIAL|bare", "迟早会", "V_CHANGE", "。" },
            new[] { "N_BODY", "沾上", "N_MATERIAL|bare", "，就得用", "N_NATURE|bare", "洗。" },
            new[] { "N_PERSON", "把", "N_OBJECT|bare", "放在", "N_PLACE|bare", "，再也没取走。" },
        };

        private static readonly string[][] FramesClose =
        {
            new[] { "后来", "N_PLACE|bare", "空了，只剩", "N_NATURE|bare", "和", "N_ABSTRACT|bare", "。" },
            new[] { "没有人再", "V_SPEECH", "起那", "N_ABSTRACT|bare", "。" },
            new[] { "如今", "N_PERSON", "多半只", "V_SPEECH", "，不动手。" },
            new[] { "债还清了，", "N_ABSTRACT|bare", "却留了下来。" },
        };

        private static readonly string[] Connect =
        {
            "于是", "后来", "因此", "然而", "就这样", "渐渐地", "再往后"
        };

        private static readonly string[] MeasureUnits =
        {
            "整年", "整季", "整夜", "整个雨季", "好些日子", "许多年", "半辈子", "几个世代"
        };

        private static readonly string[] TitleTailCuts = { "录", "志", "考", "谱" };

        // ══════════════════════════════════════════════════════════
        // 抽取
        // ══════════════════════════════════════════════════════════

        private ZhsWord[] Slot(string slot)
        {
            ZhsWord[] arr;
            if (ZhsMarkovLexicon.Slots.TryGetValue(slot, out arr)) return arr;
            return null;
        }

        private ZhsPick PickNoun(string slot, string kind)
        {
            ZhsPick p = new ZhsPick();
            p.Slot = slot;
            p.W = "（缺槽:" + slot + "）";
            p.Cls = "个";
            p.Kind = "none";
            ZhsWord[] arr = Slot(slot);
            if (arr == null || arr.Length == 0) return p;

            // 材质过滤：只从该材质的词里挑（找不到合用的就退回全槽）
            List<ZhsWord> pool = new List<ZhsWord>();
            if (kind != null)
            {
                foreach (ZhsWord w in arr) if (w.Kind == kind) pool.Add(w);
            }
            if (pool.Count == 0) pool.AddRange(arr);

            ZhsWord c = pool[Rng.Next(pool.Count)];
            p.W = c.W;
            p.Kind = c.Kind;
            p.Cls = (c.Cls != null && c.Cls.Length > 0)
                ? c.Cls[Rng.Next(c.Cls.Length)]
                : ZhsMarkovLexicon.DefaultCls[Rng.Next(ZhsMarkovLexicon.DefaultCls.Length)];
            return p;
        }

        /// <summary>显式矩阵判定：verb 的允许材质里有没有该材质。</summary>
        private bool VerbAllowed(string verb, string kind)
        {
            string[] allow;
            if (!ZhsMarkovLexicon.VerbAllows.TryGetValue(verb, out allow)) return true;
            if (allow == null) return true;
            foreach (string k in allow) if (k == kind) return true;
            return false;
        }

        private string PickVerb(string slot, string kind)
        {
            ZhsWord[] arr = Slot(slot);
            if (arr == null || arr.Length == 0) return "（缺槽:" + slot + "）";
            List<string> pool = new List<string>();
            foreach (ZhsWord w in arr)
            {
                if (kind == null || VerbAllowed(w.W, kind)) pool.Add(w.W);
            }
            if (pool.Count == 0) foreach (ZhsWord w in arr) pool.Add(w.W);
            return pool[Rng.Next(pool.Count)];
        }

        private string Adj()
        {
            string[] a = ZhsMarkovLexicon.Adj;
            return a[Rng.Next(a.Length)];
        }

        // ══════════════════════════════════════════════════════════
        // 句模渲染
        // ══════════════════════════════════════════════════════════

        private static string SlotOf(string tok)
        {
            int i = tok.IndexOf('|');
            return i < 0 ? tok : tok.Substring(0, i);
        }

        private static bool IsBare(string tok)
        {
            int i = tok.IndexOf('|');
            return i >= 0 && tok.Substring(i + 1) == "bare";
        }

        private static bool IsNounSlot(string slot)
        {
            foreach (string s in NounSlots) if (s == slot) return true;
            return false;
        }

        private string Frame(string[][] pool)
        {
            string[] parts = pool[Rng.Next(pool.Length)];

            // ☆ 因果方向：先定动词 → 取它的材质 → 全句统一用这个材质
            //    不能拿「句首名词」去求材质 —— 它常是 N_PERSON（材质 none），
            //    和动词的 metal 无交集 ⇒ 门控形同虚设。
            string main = null;
            foreach (string t in parts) { if (IsNounSlot(SlotOf(t))) { main = SlotOf(t); break; } }

            List<string> vslots = new List<string>();
            foreach (string t in parts)
            {
                string slotName = SlotOf(t);
                if (slotName.StartsWith("V_", StringComparison.Ordinal)) vslots.Add(slotName);
            }

            string fixedVerb = null, wantKind = null;
            if (vslots.Count > 0)
            {
                // 候选槽：句模里出现的、带材质的名词槽
                List<string> candSlots = new List<string>();
                foreach (string t in parts)
                {
                    string slotName = SlotOf(t);
                    foreach (string ks in KindSlots) if (slotName == ks) { candSlots.Add(slotName); break; }
                }
                for (int attempt = 0; attempt < 40 && wantKind == null; attempt++)
                {
                    string vs = vslots[Rng.Next(vslots.Count)];
                    ZhsWord[] va = Slot(vs);
                    if (va == null || va.Length == 0) continue;
                    string cand = va[Rng.Next(va.Length)].W;
                    foreach (string cs in candSlots)
                    {
                        ZhsWord[] ca = Slot(cs);
                        if (ca == null) continue;
                        foreach (ZhsWord cw in ca)
                        {
                            if (VerbAllowed(cand, cw.Kind)) { wantKind = cw.Kind; break; }
                        }
                        if (wantKind != null) break;
                    }
                    if (wantKind != null) { fixedVerb = cand; }
                }
            }

            ZhsPick mainNoun = default(ZhsPick);
            bool haveMain = main != null;
            if (haveMain) mainNoun = PickNoun(main, wantKind);

            StringBuilder sb = new StringBuilder();
            bool usedMain = false;
            foreach (string t in parts)
            {
                string slot = SlotOf(t);
                if (!IsNounSlot(slot) && !slot.StartsWith("V_", StringComparison.Ordinal)
                    && slot != "ADJ" && slot != "MEASURE")
                {
                    sb.Append(t);                       // 固定片段
                    continue;
                }
                if (slot == "ADJ") { sb.Append(Adj()).Append('的'); continue; }
                if (slot == "MEASURE") { sb.Append(MeasureUnits[Rng.Next(MeasureUnits.Length)]); continue; }
                if (slot.StartsWith("V_", StringComparison.Ordinal))
                {
                    sb.Append(fixedVerb != null && slot == vslots[0] ? fixedVerb : PickVerb(slot, wantKind));
                    continue;
                }
                ZhsPick p;
                if (haveMain && slot == main && !usedMain) { p = mainNoun; usedMain = true; }
                else
                {
                    bool wantKindHere = false;
                    if (wantKind != null)
                        foreach (string ks in KindSlots) if (slot == ks) { wantKindHere = true; break; }
                    p = PickNoun(slot, wantKindHere ? wantKind : null);
                }
                if (IsBare(t)) sb.Append(p.W);
                else sb.Append('\u0001').Append(p.Cls).Append('\u0002').Append(p.W).Append('\u0003');
            }

            string s = sb.ToString();
            s = CollapseCls(s);
            s = s.Replace("一一", "一").Replace("有有", "有").Replace("的的", "的");
            s = CollapsePunct(s);
            return s;
        }

        /// <summary>量词只在紧跟「一/这/那/有」时展开，否则丢弃。
        ///
        /// 编码约定：`\u0001` 量词起始 · `\u0002` 量词与词的分隔 · `\u0003` 词结束。
        /// （用单字符哨兵而不是字符串前缀 —— .NET 的 `string.IndexOf(string, int)` 不存在，
        ///   写成 `StartsWith(mark, i)` 又会被重载解析成 `StartsWith(string, StringComparison)`。）
        /// </summary>
        private static string CollapseCls(string s)
        {
            StringBuilder o = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '\u0001')
                {
                    int sep = s.IndexOf('\u0002', i + 1);
                    if (sep < 0) break;
                    int end = s.IndexOf('\u0003', sep + 1);
                    if (end < 0) break;
                    string cls = s.Substring(i + 1, sep - i - 1);
                    string word = s.Substring(sep + 1, end - sep - 1);
                    bool need = o.Length > 0 && "一这那有".IndexOf(o[o.Length - 1]) >= 0;
                    if (need) o.Append(cls);
                    o.Append(word);
                    i = end + 1;
                    continue;
                }
                o.Append(s[i]);
                i++;
            }
            return o.ToString();
        }

        /// <summary>合并连续标点（避免「，。」这类）。</summary>
        private static string CollapsePunct(string s)
        {
            StringBuilder o = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (o.Length > 0 && "，。".IndexOf(c) >= 0 && "，。".IndexOf(o[o.Length - 1]) >= 0) continue;
                o.Append(c);
            }
            return o.ToString();
        }

        // ══════════════════════════════════════════════════════════
        // 对外：四种粒度
        // ══════════════════════════════════════════════════════════

        /// <summary>一个完整句子（12~34 字，以句末标点结尾）。</summary>
        public string Sentence()
        {
            for (int a = 0; a < 40; a++)
            {
                string s = Frame(FramesBody);
                if (string.IsNullOrEmpty(s)) continue;
                if (s.Length < 12 || s.Length > 34) continue;
                int last = s.Length - 1;
                if ("。！？…".IndexOf(s[last]) < 0) continue;
                return s;
            }
            return Frame(FramesOpen);
        }

        /// <summary>一个段落（2~5 句）。</summary>
        public string Paragraph(int sentences)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Frame(FramesOpen));
            for (int i = 0; i < sentences; i++)
            {
                if (Rng.NextDouble() < 0.22) sb.Append(Connect[Rng.Next(Connect.Length)]).Append('，');
                sb.Append(Frame(FramesBody));
            }
            if (Rng.NextDouble() < 0.6) sb.Append(Frame(FramesClose));
            return sb.ToString();
        }

        /// <summary>短片段（4~12 字，不带句末标点）—— 涂鸦 / 纹身 / 短句。</summary>
        public string Short()
        {
            for (int a = 0; a < 40; a++)
            {
                ZhsPick p = PickNoun(Rng.Next(2) == 0 ? "N_NATURE" : "N_OBJECT", null);
                string s;
                switch (Rng.Next(4))
                {
                    case 0: s = p.W + "与" + PickNoun("N_ABSTRACT", null).W; break;
                    case 1: s = PickNoun("N_PLACE", null).W + "的" + p.W; break;
                    case 2: s = p.W + "记"; break;
                    default: s = PickNoun("N_ABSTRACT", null).W + "考"; break;
                }
                if (s.Length < 4 || s.Length > 12) continue;
                return s;
            }
            return PickNoun("N_ABSTRACT", null).W + "考";
        }

        /// <summary>书名（不带书名号、不带句末标点；游戏自己会加装饰后缀）。</summary>
        public string Title()
        {
            string s;
            switch (Rng.Next(6))
            {
                case 0: s = PickNoun("N_ABSTRACT", null).W + "与" + PickNoun("N_NATURE", null).W; break;
                case 1: s = PickNoun("N_PLACE", null).W + "的" + PickNoun("N_ABSTRACT", null).W; break;
                case 2: s = PickNoun("N_NATURE", null).W + "录"; break;
                case 3: s = PickNoun("N_OBJECT", null).W + "志"; break;
                case 4: s = PickNoun("N_MATERIAL", null).W + "与" + PickNoun("N_NATURE", null).W; break;
                default: s = PickNoun("N_ABSTRACT", null).W + "谱"; break;
            }
            return s;
        }
    }
}
