using System.Collections.Generic;
using System.Text;

namespace MdViewer
{
    internal struct InlineRun
    {
        public string Text;
        public bool Bold;
        public bool Italic;
        public bool Strike;
        public bool Code;
        public bool Link;
        public bool Muted;
    }

    /// <summary>
    /// Parses a small, safe subset of Markdown inline syntax into styled text runs.
    /// Nothing is ever executed or fetched: links and images are rendered as text only.
    /// </summary>
    internal static class InlineParser
    {
        private const string Escapable = "\\`*_{}[]()#+-.!|~<>";

        public static List<InlineRun> Parse(string s)
        {
            var runs = new List<InlineRun>();
            var buf = new StringBuilder();
            bool bold = false, italic = false, strike = false;
            int i = 0;

            while (i < s.Length)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < s.Length && Escapable.IndexOf(s[i + 1]) >= 0)
                {
                    buf.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '`')
                {
                    int ticks = CountRun(s, i, '`');
                    int close = FindTickClose(s, i + ticks, ticks);
                    if (close >= 0)
                    {
                        Flush(runs, buf, bold, italic, strike);
                        string code = s.Substring(i + ticks, close - i - ticks);
                        runs.Add(new InlineRun { Text = code.Trim(), Code = true });
                        i = close + ticks;
                        continue;
                    }
                    buf.Append(s, i, ticks);
                    i += ticks;
                    continue;
                }

                if (c == '*' || c == '_' || (c == '~' && i + 1 < s.Length && s[i + 1] == '~'))
                {
                    bool isDouble = i + 1 < s.Length && s[i + 1] == c;
                    int len = isDouble ? 2 : 1;
                    string marker = new string(c, len);

                    bool intraword = c == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1])
                        && i + len < s.Length && char.IsLetterOrDigit(s[i + len]);

                    if (!intraword)
                    {
                        bool isOpen = c == '~' ? strike : (isDouble ? bold : italic);
                        if (isOpen || s.IndexOf(marker, i + len + 1, System.StringComparison.Ordinal) > 0)
                        {
                            Flush(runs, buf, bold, italic, strike);
                            if (c == '~') strike = !strike;
                            else if (isDouble) bold = !bold;
                            else italic = !italic;
                            i += len;
                            continue;
                        }
                    }
                    buf.Append(marker);
                    i += len;
                    continue;
                }

                string label, target;
                int end;
                if (c == '!' && i + 1 < s.Length && s[i + 1] == '[' && TryParseLink(s, i + 1, out label, out target, out end))
                {
                    Flush(runs, buf, bold, italic, strike);
                    runs.Add(new InlineRun { Text = "[image: " + label + "]", Italic = true, Muted = true });
                    i = end;
                    continue;
                }

                if (c == '[' && TryParseLink(s, i, out label, out target, out end))
                {
                    Flush(runs, buf, bold, italic, strike);
                    runs.Add(new InlineRun { Text = label, Bold = bold, Italic = italic, Strike = strike, Link = true });
                    i = end;
                    continue;
                }

                buf.Append(c);
                i++;
            }

            Flush(runs, buf, bold, italic, strike);
            return runs;
        }

        private static void Flush(List<InlineRun> runs, StringBuilder buf, bool bold, bool italic, bool strike)
        {
            if (buf.Length == 0) return;
            runs.Add(new InlineRun { Text = buf.ToString(), Bold = bold, Italic = italic, Strike = strike });
            buf.Clear();
        }

        private static int CountRun(string s, int start, char c)
        {
            int n = 0;
            while (start + n < s.Length && s[start + n] == c) n++;
            return n;
        }

        private static int FindTickClose(string s, int from, int ticks)
        {
            int i = from;
            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int n = CountRun(s, i, '`');
                    if (n == ticks) return i;
                    i += n;
                }
                else
                {
                    i++;
                }
            }
            return -1;
        }

        private static bool TryParseLink(string s, int start, out string label, out string target, out int end)
        {
            label = null;
            target = null;
            end = start;

            int close = s.IndexOf(']', start + 1);
            if (close < 0 || close + 1 >= s.Length || s[close + 1] != '(') return false;

            int paren = s.IndexOf(')', close + 2);
            if (paren < 0) return false;

            label = s.Substring(start + 1, close - start - 1);
            target = s.Substring(close + 2, paren - close - 2).Trim();
            end = paren + 1;
            return true;
        }
    }
}
