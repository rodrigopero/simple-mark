using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MdViewer
{
    /// <summary>
    /// Renders Markdown into a RichTextBox by applying formatting to plain text.
    /// No HTML, RTF or script from the document is ever interpreted.
    /// </summary>
    internal sealed class MarkdownRenderer : IDisposable
    {
        private enum Kind { None, Paragraph, Heading, List, Quote, Code, Rule, Table }

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
        private const RegexOptions Options = RegexOptions.CultureInvariant;

        private static readonly Regex HeadingRx = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)(\s+#+)?\s*$", Options, RegexTimeout);
        private static readonly Regex RuleRx = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", Options, RegexTimeout);
        private static readonly Regex BulletRx = new Regex(@"^([ \t]*)[-*+]\s+(.*)$", Options, RegexTimeout);
        private static readonly Regex OrderedRx = new Regex(@"^([ \t]*)(\d{1,9})[.)]\s+(.*)$", Options, RegexTimeout);
        private static readonly Regex QuoteRx = new Regex(@"^\s{0,3}>\s?(.*)$", Options, RegexTimeout);
        private static readonly Regex FenceRx = new Regex(@"^\s{0,3}(`{3,}|~{3,})", Options, RegexTimeout);

        private static readonly Color TextColor = Color.FromArgb(36, 41, 47);
        private static readonly Color MutedColor = Color.FromArgb(101, 109, 118);
        private static readonly Color LinkColor = Color.FromArgb(9, 105, 218);
        private static readonly Color CodeBackColor = Color.FromArgb(240, 242, 245);

        private const int ListIndentStep = 22;
        private const int HangingIndent = 16;

        private readonly RichTextBox box;
        private readonly Font bodyFont;
        private readonly Font monoFont;
        private readonly Font[] headingFonts;
        private readonly Dictionary<string, Font> derivedFonts = new Dictionary<string, Font>();
        private Kind previous;

        public MarkdownRenderer(RichTextBox box)
        {
            this.box = box;
            bodyFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            monoFont = new Font("Consolas", 10f, FontStyle.Regular);
            float[] sizes = { 20f, 17f, 15f, 13f, 12f, 11f };
            headingFonts = new Font[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                headingFonts[i] = new Font("Segoe UI", sizes[i], FontStyle.Bold);
            }
        }

        public void Render(string markdown)
        {
            box.ReadOnly = false;
            box.Clear();
            previous = Kind.None;

            try
            {
                RenderBlocks(markdown ?? string.Empty);
            }
            finally
            {
                box.ReadOnly = true;
                box.SelectionStart = 0;
                box.SelectionLength = 0;
                box.ScrollToCaret();
            }
        }

        private void RenderBlocks(string markdown)
        {
            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new StringBuilder();
            int i = 0;

            while (i < lines.Length)
            {
                string line = lines[i];
                Match m;

                m = FenceRx.Match(line);
                if (m.Success)
                {
                    FlushParagraph(paragraph);
                    i = RenderCodeBlock(lines, i, m.Groups[1].Value);
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    FlushParagraph(paragraph);
                    i++;
                    continue;
                }

                m = HeadingRx.Match(line);
                if (m.Success)
                {
                    FlushParagraph(paragraph);
                    BeginBlock(Kind.Heading);
                    WriteInline(m.Groups[2].Value, headingFonts[m.Groups[1].Length - 1], TextColor, 0, 0);
                    i++;
                    continue;
                }

                if (RuleRx.IsMatch(line))
                {
                    FlushParagraph(paragraph);
                    BeginBlock(Kind.Rule);
                    Write(new string('─', 40), bodyFont, MutedColor, 0, 0, box.BackColor);
                    i++;
                    continue;
                }

                m = QuoteRx.Match(line);
                if (m.Success)
                {
                    FlushParagraph(paragraph);
                    BeginBlock(Kind.Quote);
                    Write("▌ ", bodyFont, MutedColor, 12, 0, box.BackColor);
                    WriteInline(m.Groups[1].Value, bodyFont, MutedColor, 12, 0);
                    i++;
                    continue;
                }

                m = BulletRx.Match(line);
                if (m.Success)
                {
                    FlushParagraph(paragraph);
                    int level = IndentLevel(m.Groups[1].Value);
                    RenderListItem(BulletFor(level), m.Groups[2].Value, level);
                    i++;
                    continue;
                }

                m = OrderedRx.Match(line);
                if (m.Success)
                {
                    FlushParagraph(paragraph);
                    RenderListItem(m.Groups[2].Value + ".", m.Groups[3].Value, IndentLevel(m.Groups[1].Value));
                    i++;
                    continue;
                }

                if (line.TrimStart().StartsWith("|", StringComparison.Ordinal))
                {
                    FlushParagraph(paragraph);
                    BeginBlock(Kind.Table);
                    Write(line.Trim(), monoFont, TextColor, 0, 0, box.BackColor);
                    i++;
                    continue;
                }

                AppendParagraphLine(paragraph, line);
                i++;
            }

            FlushParagraph(paragraph);
        }

        private int RenderCodeBlock(string[] lines, int start, string fence)
        {
            char fenceChar = fence[0];
            var code = new StringBuilder();
            int i = start + 1;

            while (i < lines.Length)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length >= fence.Length && trimmed.Trim(fenceChar).Length == 0)
                {
                    i++;
                    break;
                }
                if (code.Length > 0) code.Append('\n');
                code.Append(lines[i]);
                i++;
            }

            BeginBlock(Kind.Code);
            Write(code.Length > 0 ? code.ToString() : " ", monoFont, TextColor, 12, 0, CodeBackColor);
            return i;
        }

        private void RenderListItem(string marker, string content, int level)
        {
            if (content.StartsWith("[ ] ", StringComparison.Ordinal))
            {
                marker = "☐";
                content = content.Substring(4);
            }
            else if (content.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase))
            {
                marker = "☑";
                content = content.Substring(4);
            }

            int indent = ListIndentStep * (level + 1);
            BeginBlock(Kind.List);
            Write(marker + " ", bodyFont, TextColor, indent, HangingIndent, box.BackColor);
            WriteInline(content, bodyFont, TextColor, indent, HangingIndent);
        }

        private static void AppendParagraphLine(StringBuilder paragraph, string line)
        {
            bool hardBreak = line.EndsWith("  ", StringComparison.Ordinal) || line.EndsWith("\\", StringComparison.Ordinal);
            string text = line.Trim();
            if (text.EndsWith("\\", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);

            if (paragraph.Length > 0 && paragraph[paragraph.Length - 1] != '\n') paragraph.Append(' ');
            paragraph.Append(text);
            if (hardBreak) paragraph.Append('\n');
        }

        private void FlushParagraph(StringBuilder paragraph)
        {
            if (paragraph.Length == 0) return;
            BeginBlock(Kind.Paragraph);
            WriteInline(paragraph.ToString().TrimEnd('\n'), bodyFont, TextColor, 0, 0);
            paragraph.Clear();
        }

        private void BeginBlock(Kind kind)
        {
            if (previous != Kind.None)
            {
                bool tight = kind == previous && (kind == Kind.List || kind == Kind.Quote || kind == Kind.Table);
                Write(tight ? "\n" : "\n\n", bodyFont, TextColor, 0, 0, box.BackColor);
            }
            previous = kind;
        }

        private void WriteInline(string text, Font baseFont, Color color, int indent, int hanging)
        {
            foreach (InlineRun run in InlineParser.Parse(text))
            {
                if (run.Code)
                {
                    Write(run.Text, monoFont, color, indent, hanging, CodeBackColor);
                    continue;
                }

                FontStyle style = baseFont.Style;
                if (run.Bold) style |= FontStyle.Bold;
                if (run.Italic) style |= FontStyle.Italic;
                if (run.Strike) style |= FontStyle.Strikeout;
                if (run.Link) style |= FontStyle.Underline;

                Color runColor = run.Link ? LinkColor : (run.Muted ? MutedColor : color);
                Write(run.Text, GetFont(baseFont, style), runColor, indent, hanging, box.BackColor);
            }
        }

        private void Write(string text, Font font, Color color, int indent, int hanging, Color back)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = font;
            box.SelectionColor = color;
            box.SelectionBackColor = back;
            box.SelectionIndent = indent;
            box.SelectionHangingIndent = hanging;
            box.SelectedText = text;
        }

        private Font GetFont(Font baseFont, FontStyle style)
        {
            if (style == baseFont.Style) return baseFont;

            string key = baseFont.Name + "|" + baseFont.Size + "|" + (int)style;
            Font font;
            if (!derivedFonts.TryGetValue(key, out font))
            {
                font = new Font(baseFont, style);
                derivedFonts[key] = font;
            }
            return font;
        }

        private static int IndentLevel(string whitespace)
        {
            int width = 0;
            foreach (char c in whitespace) width += c == '\t' ? 4 : 1;
            return Math.Min(width / 2, 6);
        }

        private static string BulletFor(int level)
        {
            switch (level)
            {
                case 0: return "•";
                case 1: return "◦";
                default: return "▪";
            }
        }

        public void Dispose()
        {
            bodyFont.Dispose();
            monoFont.Dispose();
            foreach (Font f in headingFonts) f.Dispose();
            foreach (Font f in derivedFonts.Values) f.Dispose();
            derivedFonts.Clear();
        }
    }
}
