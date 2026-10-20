// 随机书籍正文 · 对外门面（粒度适配 + 幂等替换 + 守门）
//
// ═══════════════════════════════════════════════════════════════════════
// 1.1.0 起：生成内核已换成「句模 + 槽位填充」生成器（ZhsMarkovGen）
// ═══════════════════════════════════════════════════════════════════════
// 旧方案（0.x）是**字级 3 阶马尔科夫链** + 100 句固定语料（ZhsBookCorpus）。
// 换掉的原因（详见 _tools\随机书页生成-大改造方案.md）：
//   * 语料只有 100 句固定文本，换个 seed 也只是从那 100 句里抽，很快重复；
//   * 字级链会在半句处拼错（实测 "他们饮井里的人多半没去过塔…"）；
//   * 新方案 30 页就产出 309 个不同句子（去重率 96.6%）。
//
// 本文件保留的职责（**与生成内核无关，别一起删**）：
//   * 四种粒度的适配：句子 / 段落 / 短句 / 标题；
//   * 书页硬折行（中文不会自动折行，见 ZhsWrap）；
//   * **幂等替换**：已含中文的输入原样返回 —— 多个补丁点重叠时不会重复翻译；
//   * **守门 `LooksLikeProse`**：补丁会覆盖所有返回 string 的生成类方法，
//     万一命中返回**语料名/内部键**的方法（"MARKOVSENTENCE"、"LibraryCorpus.json"），
//     换成中文会直接搞坏游戏逻辑；
//   * 颜色/着色器包裹的剥离与复原（Markup）。

using System;
using System.Collections.Generic;
using System.Text;

namespace Nattens.QudZhsCookbook
{
    internal static class ZhsBookText
    {
        private static readonly object Gate = new object();
        private static readonly Random Rng = new Random(unchecked(Environment.TickCount * 17 + 5));
        private static ZhsMarkovGen _gen;

        /// <summary>
        /// 日志必须**绝不抛出**。
        /// 实测教训（与 `ZhsIsnerClue.SafeLog` 同源）：探针环境下
        /// `UnityEngine.Debug.Log` 会抛 `SecurityException: ECall methods must be
        /// packaged into a system module`；若日志能把 `EnsureBuilt()` 带崩，
        /// 整个书页生成就废了 —— **绝不能因为一条日志而丢掉功能**。
        /// </summary>
        private static void SafeLog(string msg, bool warn)
        {
            try
            {
                if (warn) UnityEngine.Debug.LogWarning(msg);
                else UnityEngine.Debug.Log(msg);
            }
            catch { }
        }

        /// <summary>构建生成器（幂等）。词库是编译期常量表，这里只做一次初始化与自检。</summary>
        public static void EnsureBuilt()
        {
            lock (Gate)
            {
                if (_gen != null) return;
                _gen = new ZhsMarkovGen(unchecked(Environment.TickCount * 31 + 7));
                int words = 0;
                foreach (KeyValuePair<string, ZhsWord[]> kv in ZhsMarkovLexicon.Slots)
                    words += kv.Value.Length;
                SafeLog("[QudZhsCookbook] 中文书页生成器就绪（句模+槽位填充）：语义槽 "
                    + ZhsMarkovLexicon.Slots.Count + " 个 / 词 " + words
                    + " 个 / 形容词 " + ZhsMarkovLexicon.Adj.Length
                    + " 个 / 动词相容矩阵 " + ZhsMarkovLexicon.VerbAllows.Count + " 条", false);
                for (int i = 0; i < 3; i++)
                    SafeLog("[QudZhsCookbook] 样例书页：" + Paragraph(), false);
            }
        }

        /// <summary>是否含 ASCII 可见字符（用于排除夹带英文的结果）。</summary>
        private static bool HasAscii(string s)
        {
            foreach (char c in s) { if (c < 128) return true; }
            return false;
        }

        /// <summary>是否同时含 CJK 与 ASCII 字母 —— 即「中英混杂」。</summary>
        private static bool IsMixedScript(string s)
        {
            bool cjk = false, latin = false;
            foreach (char c in s)
            {
                if (c > 127) cjk = true;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin = true;
                if (cjk && latin) return true;
            }
            return false;
        }

        /// <summary>
        /// 一个完整的中文句子（以句末标点结尾），12~40 字。
        ///
        /// ★ 为什么带「重掷」：我们的生成器本身不会产出中英混杂，但
        ///   `Generate*` 的返回值会走 `Translate`，那里对**已含 CJK** 的串是幂等放行。
        ///   而游戏语料里混着官方英文线索的碎片（`Ruin of House Isner` 这类），
        ///   拼出来的句子可能既含 CJK 又含英文 ⇒ 会被幂等放行成半中半英。
        ///   这里发现混杂就重掷（最多 40 次，与官方 `GenerateSentence` 的
        ///   "100 步不收敛就重新掷开头" 同一思路）。
        /// </summary>
        public static string Sentence()
        {
            EnsureBuilt();
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string s = _gen.Sentence();
                if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                if (HasAscii(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>2~4 个句子组成的段落（书籍整页）。结果会按书页宽度硬折行——中文不会自动折行。</summary>
        public static string Paragraph()
        {
            EnsureBuilt();
            int n = Rng.Next(2, 5);
            string s = _gen.Paragraph(n);
            if (string.IsNullOrEmpty(s)) return null;
            return ZhsWrap.Apply(s);
        }

        /// <summary>短片段（涂鸦 / 纹身 / 短句），4~12 字，不带句末标点。</summary>
        public static string Short()
        {
            EnsureBuilt();
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string s = _gen.Short();
                if (string.IsNullOrEmpty(s)) continue;
                s = s.TrimEnd('。', '！', '？', '…', '，', '、', '；', '：');
                if (s.Length < 4 || s.Length > 12) continue;
                if (HasAscii(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>书名（马尔科夫书籍标题）。
        /// 风格约定（用户要求）：**不带书名号《》、不带句末标点** ——
        /// 且游戏自己会给书名加装饰后缀（`, Vol. III` / `: Unabridged`），外面再套《》就成了夹杂样式。</summary>
        public static string Title()
        {
            EnsureBuilt();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string s = _gen.Title();
                if (string.IsNullOrEmpty(s)) continue;
                s = TrimTitleTail(s);
                if (s.Length < 2 || s.Length > 14) continue;
                if (HasAscii(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>裁掉尾部的句末/分隔标点（中英文都处理）。</summary>
        private static string TrimTitleTail(string s)
        {
            int end = s.Length;
            while (end > 0 && "。！？；：，、,.!?;:…—-".IndexOf(s[end - 1]) >= 0) end--;
            return s.Substring(0, end).Trim();
        }

        // ---------- 幂等替换（供补丁调用） ----------

        public static string TranslateSentence(string original) { return Translate(original, Sentence); }
        public static string TranslateParagraph(string original) { return Translate(original, Paragraph); }
        public static string TranslateShort(string original) { return Translate(original, Short); }
        public static string TranslateTitle(string original) { return Translate(original, Title); }

        /// <summary>被跳过的值（只记前几个，用于诊断"到底拦下了什么"）。</summary>
        private static readonly List<string> _skippedSamples = new List<string>();

        /// <summary>
        /// 判断一个返回值是否"像自然语言文本"。
        /// 补丁会覆盖返回 string 的生成类方法；万一命中了一个返回**语料名/内部键**的方法
        /// （例如 "MARKOVSENTENCE"、"LibraryCorpus.json"），把它换成中文就会直接搞坏游戏逻辑。
        /// 这个守门函数要求：有空格或句末标点、含足够字母、不是全大写标识符、不是文件名。
        ///
        /// ★ 伊什纳坐标线索也走这条路：它是**整句英文**（含空格与句末标点），
        ///   会被判定为散文并翻译 ⇒ 所以线索必须由 ZhsIsnerCluePatch 在
        ///   `LoreGenerator.RuinOfHouseIsnerLore` 处就换成中文（含中文 ⇒ 这里幂等放行）。
        /// </summary>
        public static bool LooksLikeProse(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length < 6) return false;
            if (s.IndexOf('.') >= 0 && s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.IndexOf(' ') < 0 && s.IndexOf('.') < 0 && s.IndexOf(',') < 0
                && s.IndexOf('!') < 0 && s.IndexOf('?') < 0 && s.IndexOf(';') < 0) return false;

            int letters = 0, uppers = 0, spaces = 0;
            foreach (char c in s)
            {
                if (c == ' ') spaces++;
                else if (c >= 'a' && c <= 'z') letters++;
                else if (c >= 'A' && c <= 'Z') { letters++; uppers++; }
            }
            if (letters < 4) return false;

            // 全大写：默认判为内部标识符（如 MARKOVSENTENCE）→ 不翻译。
            // 但**全大写的散文**是存在的（实测：`YOU OCCUPY 10-50% OF MY PROCESSING CAPABILITIES`、
            // `HA HA HA.` 这类 Ptoh 台词），它们有多个空格分词 —— 这种情况下放行。
            // 无空格的全大写单 token 仍然会被前面的空格检查挡掉。
            if (uppers == letters && !(spaces >= 2 && s.Length >= 8)) return false;
            return true;
        }

        private static string Translate(string original, Func<string> generator)
        {
            if (string.IsNullOrEmpty(original)) return original;

            // 幂等：已含 CJK 视为已处理
            if (Markup.HasCjk(original))
            {
                // ★ 但「中英混杂」要拦住：游戏语料里混着官方英文线索的碎片，
                //   拼出的句子可能既含 CJK 又含英文。那种串若直接放行，
                //   玩家看到的是半中半英。这里重掷一条纯中文（最多 8 次）。
                if (IsMixedScript(original))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        string alt = generator();
                        if (!string.IsNullOrEmpty(alt) && !IsMixedScript(alt) && !HasAscii(alt))
                            return alt;
                    }
                    // 重掷不出来就退回原文（宁可混杂，也不给空）
                }
                return original;
            }

            if (!LooksLikeProse(original))
            {
                lock (_skippedSamples)
                {
                    if (_skippedSamples.Count < 8 && !_skippedSamples.Contains(original))
                    {
                        _skippedSamples.Add(original);
                        SafeLog("[QudZhsCookbook] 已跳过非文本返回值（不翻译）：" + original, false);
                    }
                }
                return original;
            }

            List<string> wrappers = new List<string>();
            Markup.Unwrap(original, wrappers);

            string generated = generator();
            if (string.IsNullOrEmpty(generated)) return original;
            return Markup.Rewrap(generated, wrappers);
        }
    }
}
