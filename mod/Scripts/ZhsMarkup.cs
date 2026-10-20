// 颜色/着色器包裹的剥离与复原。
//
// CoQ 的文字颜色有两套写法：
//   1. 现行「着色器」语法：{{code|文本}}，code 取自 Base\Colors.xml（K b B C c g G r R m M W w o O Y y 等），
//      可嵌套（{{K|{{crysteel|crysteel}} aegis}}）。书名的原模板就是 {{g|=name|title=}}，
//      所以直接覆盖 DisplayName 会丢掉绿色。
//   2. 旧式单字符码：以 & 开头，如 BaseCookbook 的 Render ColorString="&y"。
//
// 本类负责：把外层这些包裹逐层剥掉拿到纯文本，改完文本后再按原顺序包回去。

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nattens.QudZhsCookbook
{
    internal static class Markup
    {
        /// <summary>逐层剥掉外层的 {{code|...}} 与 &x 前缀，包裹信息按"从外到内"记入 wrappers。</summary>
        public static string Unwrap(string s, List<string> wrappers)
        {
            if (wrappers == null) throw new ArgumentNullException("wrappers");
            wrappers.Clear();
            if (string.IsNullOrEmpty(s)) return s;

            bool changed = true;
            while (changed && !string.IsNullOrEmpty(s))
            {
                changed = false;

                string code, inner;
                if (TryOuterShader(s, out code, out inner))
                {
                    wrappers.Add("{{" + code + "|");
                    s = inner;
                    changed = true;
                    continue;
                }

                Match amp = Regex.Match(s, @"^(&[A-Za-z0-9])([\s\S]*)$");
                if (amp.Success && amp.Groups[2].Value.Length > 0)
                {
                    wrappers.Add(amp.Groups[1].Value);
                    s = amp.Groups[2].Value;
                    changed = true;
                }
            }
            return s;
        }

        /// <summary>按 wrappers（从外到内）重新包回去。</summary>
        public static string Rewrap(string inner, List<string> wrappers)
        {
            if (wrappers == null || wrappers.Count == 0) return inner;
            string result = inner;
            for (int i = wrappers.Count - 1; i >= 0; i--)
            {
                string w = wrappers[i];
                result = w.StartsWith("{{", StringComparison.Ordinal) ? w + result + "}}" : w + result;
            }
            return result;
        }

        /// <summary>
        /// 判断 s 是否整体被一个 {{code|...}} 包住（按 {{ / }} 配对计数，支持嵌套），
        /// 是则输出 code 与内文。必须整体包住才算，避免把 "{{g|a}} 与 {{g|b}}" 误判成一个包裹。
        /// </summary>
        private static bool TryOuterShader(string s, out string code, out string inner)
        {
            code = null;
            inner = null;
            if (s.Length < 6 || !s.StartsWith("{{", StringComparison.Ordinal)) return false;

            int bar = s.IndexOf('|');
            if (bar < 0) return false;

            code = s.Substring(2, bar - 2);
            if (code.Length == 0) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '#';
                if (!ok) return false;
            }

            int depth = 1;
            int j = bar + 1;
            while (j < s.Length)
            {
                if (j + 1 < s.Length && s[j] == '{' && s[j + 1] == '{')
                {
                    depth++;
                    j += 2;
                    continue;
                }
                if (j + 1 < s.Length && s[j] == '}' && s[j + 1] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        if (j + 2 != s.Length) return false; // 没有整体包住
                        inner = s.Substring(bar + 1, j - (bar + 1));
                        return true;
                    }
                    j += 2;
                    continue;
                }
                j++;
            }
            return false;
        }

        /// <summary>是否含中日韩统一表意文字（用于判断"已经译过"以保持幂等）。</summary>
        public static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (c >= '\u4e00' && c <= '\u9fff') return true;
            }
            return false;
        }
    }
}
