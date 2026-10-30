using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SimpleMark
{
    /// <summary>
    /// Parses Markdown with Markdig and renders the syntax tree as RTF into a RichTextBox.
    /// Every piece of document text goes through <see cref="AppendEscaped"/>, so the
    /// document can never inject RTF control words; only this class emits formatting.
    /// </summary>
    internal sealed class MarkdownRenderer : IDisposable
    {
        private const int MaxNestingDepth = 32;
        private static readonly MarkdownPipeline Pipeline = BuildPipeline();

        private const string Fonts =
            "{\\fonttbl{\\f0\\fswiss Segoe UI;}{\\f1\\fmodern Consolas;}{\\f2\\fnil Segoe UI Symbol;}}";

        private const string BaseChar = "\\f0\\fs21\\cf1";
        private const string AccentChar = "\\cf3";
        private const string QuoteChar = "\\cf2\\i";
        private const string CellBorders =
            "\\clbrdrt\\brdrs\\brdrw10\\brdrcf5\\clbrdrl\\brdrs\\brdrw10\\brdrcf5" +
            "\\clbrdrb\\brdrs\\brdrw10\\brdrcf5\\clbrdrr\\brdrs\\brdrw10\\brdrcf5";
        private static readonly int[] HeadingSizes = { 40, 34, 30, 26, 24, 22 };

        private const int IndentTw = 360;
        private const int ListHangTw = 280;
        private const int ParagraphAfterTw = 160;
        private const int TightItemAfterTw = 60;
        private const int CellPaddingPx = 16;
        private const int MinColumnPx = 48;

        private readonly RichTextView box;
        private readonly Font measureFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
        private readonly Font measureBoldFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        private readonly System.Collections.Generic.Dictionary<string, string> imageCache =
            new System.Collections.Generic.Dictionary<string, string>();

        private MarkdownDocument document;
        private string baseDirectory;
        private StringBuilder rtf;
        private float dpi = 96f;
        private int availableWidthPx;

        public MarkdownRenderer(RichTextView box)
        {
            this.box = box;
        }

        public int RenderedWidth { get; private set; }

        private Palette palette = Palette.Light;

        public Palette Palette
        {
            get { return palette; }
            set { palette = value ?? Palette.Light; }
        }

        // Color table indexes: 1 text, 2 muted, 3 accent, 4 code background, 5 border, 6 table header.
        private string BuildHeader()
        {
            var header = new StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1");
            header.Append(Fonts).Append("{\\colortbl ;");
            foreach (Color c in new[] { palette.Text, palette.Muted, palette.Accent, palette.CodeBackground, palette.Border, palette.TableHeader })
            {
                header.Append("\\red").Append(c.R).Append("\\green").Append(c.G).Append("\\blue").Append(c.B).Append(';');
            }
            return header.Append("}\\viewkind4\n").ToString();
        }

        private static MarkdownPipeline BuildPipeline()
        {
            var builder = new MarkdownPipelineBuilder();
            builder.MaximumNestingDepth = MaxNestingDepth;
            builder.UsePipeTables()
                   .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
                   .UseTaskLists()
                   .UseAutoLinks()
                   .DisableHtml();
            return builder.Build();
        }

        /// <param name="baseDirectory">Folder used to resolve relative image paths; null disables images.</param>
        public void Render(string markdown, string baseDirectory)
        {
            document = Markdown.Parse(markdown ?? string.Empty, Pipeline, null);
            this.baseDirectory = baseDirectory;
            imageCache.Clear();
            Rerender();
        }

        /// <summary>Renders the current document again, e.g. after the viewer width changed.</summary>
        public void Rerender()
        {
            using (Graphics g = box.CreateGraphics()) dpi = g.DpiX;
            box.UpdateContentRect();
            RenderedWidth = box.ContentWidth;
            availableWidthPx = Math.Max(200, RenderedWidth - 8);

            rtf = new StringBuilder(4096);
            rtf.Append(BuildHeader());
            if (document != null)
            {
                foreach (Block block in document) RenderBlock(block, 0, string.Empty, 0);
            }
            rtf.Append('}');

            box.ReadOnly = false;
            try
            {
                box.Rtf = rtf.ToString();
                box.UpdateContentRect();
            }
            finally
            {
                box.ReadOnly = true;
                rtf = null;
            }
        }

        private void RenderBlock(Block block, int leftTw, string charFormat, int listLevel)
        {
            var heading = block as HeadingBlock;
            if (heading != null)
            {
                int level = Math.Max(1, Math.Min(heading.Level, HeadingSizes.Length));
                BeginParagraph(leftTw, 0, level <= 2 ? 360 : 240, 120, string.Empty,
                    charFormat + (level <= 2 ? AccentChar : string.Empty) + "\\b\\fs" + HeadingSizes[level - 1]);
                RenderInlines(heading.Inline);
                EndParagraph();
                return;
            }

            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
            {
                BeginParagraph(leftTw, 0, 0, ParagraphAfterTw, string.Empty, charFormat);
                RenderInlines(paragraph.Inline);
                EndParagraph();
                return;
            }

            var list = block as ListBlock;
            if (list != null)
            {
                RenderList(list, leftTw, charFormat, listLevel);
                return;
            }

            var quote = block as QuoteBlock;
            if (quote != null)
            {
                foreach (Block child in quote) RenderBlock(child, leftTw + IndentTw, QuoteChar, listLevel);
                return;
            }

            var table = block as Table;
            if (table != null)
            {
                RenderTable(table);
                return;
            }

            var code = block as CodeBlock;
            if (code != null)
            {
                RenderCode(code, leftTw);
                return;
            }

            if (block is ThematicBreakBlock)
            {
                RenderRule();
                return;
            }

            if (block is LinkReferenceDefinitionGroup || block is BlankLineBlock) return;

            // Fallback for blocks without dedicated styling: render their content as plain text.
            var container = block as ContainerBlock;
            if (container != null)
            {
                foreach (Block child in container) RenderBlock(child, leftTw, charFormat, listLevel);
                return;
            }

            var leaf = block as LeafBlock;
            if (leaf != null)
            {
                BeginParagraph(leftTw, 0, 0, ParagraphAfterTw, string.Empty, charFormat);
                if (leaf.Inline != null) RenderInlines(leaf.Inline);
                else AppendEscaped(leaf.Lines.ToString());
                EndParagraph();
            }
        }

        private void BeginParagraph(int leftTw, int firstTw, int beforeTw, int afterTw, string paraFormat, string charFormat)
        {
            rtf.Append("\\pard\\plain\\li").Append(leftTw).Append("\\fi").Append(firstTw)
               .Append("\\sb").Append(beforeTw).Append("\\sa").Append(afterTw)
               .Append(paraFormat).Append(BaseChar).Append(charFormat).Append(' ');
        }

        private void EndParagraph()
        {
            rtf.Append("\\par\n");
        }

        private void RenderList(ListBlock list, int leftTw, string charFormat, int level)
        {
            int number;
            if (!int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out number)) number = 1;

            int itemLeft = leftTw + IndentTw;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i] as ListItemBlock;
                if (item == null) continue;

                bool lastTopLevel = level == 0 && i == list.Count - 1;
                int itemAfter = list.IsLoose || lastTopLevel ? ParagraphAfterTw : TightItemAfterTw;
                string marker = list.IsOrdered
                    ? (number + i).ToString(CultureInfo.InvariantCulture) + list.OrderedDelimiter
                    : BulletFor(level);

                RenderListItem(item, marker, itemLeft, itemAfter, charFormat, level);
            }
        }

        private void RenderListItem(ListItemBlock item, string marker, int left, int after, string charFormat, int level)
        {
            bool markerWritten = false;
            foreach (Block child in item)
            {
                var paragraph = child as ParagraphBlock;
                if (!markerWritten && paragraph != null)
                {
                    BeginParagraph(left, -ListHangTw, 0, after, "\\tx" + left, charFormat);
                    TaskList task = paragraph.Inline != null ? paragraph.Inline.FirstChild as TaskList : null;
                    rtf.Append('{').Append(AccentChar).Append(' ');
                    if (task != null) AppendSymbol(task.Checked ? "\u2611" : "\u2610");
                    else if (level > 0 && marker.Length == 1) AppendSymbol(marker);
                    else AppendEscaped(marker);
                    rtf.Append("}\\tab ");
                    RenderInlines(paragraph.Inline);
                    EndParagraph();
                    markerWritten = true;
                    continue;
                }

                if (!markerWritten)
                {
                    BeginParagraph(left, -ListHangTw, 0, TightItemAfterTw, "\\tx" + left, charFormat);
                    AppendEscaped(marker);
                    EndParagraph();
                    markerWritten = true;
                }

                var nested = child as ListBlock;
                if (nested != null) RenderList(nested, left, charFormat, level + 1);
                else RenderBlock(child, left, charFormat, level);
            }
        }

        private void RenderCode(CodeBlock code, int leftTw)
        {
            var text = new StringBuilder();
            for (int i = 0; i < code.Lines.Count; i++)
            {
                if (i > 0) text.Append('\n');
                text.Append(code.Lines.Lines[i].Slice.ToString());
            }

            // A single shaded cell gives the code block a solid background box.
            rtf.Append("\\trowd\\trgaph144\\trleft").Append(leftTw)
               .Append("\\clcbpat4\\cellx").Append(ToTwips(availableWidthPx));
            rtf.Append("\\pard\\plain\\intbl\\sb80\\sa80\\f1\\fs19\\cf1 ");
            AppendEscaped(text.Length > 0 ? text.ToString() : " ");
            rtf.Append("\\cell\\row\n");
            AppendSpacer();
        }

        private void RenderRule()
        {
            // A cell with only a bottom border draws a horizontal line.
            rtf.Append("\\trowd\\trgaph0\\trleft0\\clbrdrb\\brdrs\\brdrw15\\brdrcf5\\cellx").Append(ToTwips(availableWidthPx));
            rtf.Append("\\pard\\plain\\intbl\\fs4\\cell\\row\n");
            AppendSpacer();
        }

        private void AppendSpacer()
        {
            rtf.Append("\\pard\\plain\\sa0\\fs12\\par\n");
        }

        private void RenderTable(Table table)
        {
            int columns = table.ColumnDefinitions.Count;
            foreach (Block rowBlock in table)
            {
                var row = rowBlock as TableRow;
                if (row != null) columns = Math.Max(columns, row.Count);
            }
            if (columns == 0) return;

            int[] widths = ColumnWidths(table, columns);
            foreach (Block rowBlock in table)
            {
                var row = rowBlock as TableRow;
                if (row == null || (row.IsHeader && IsEmptyRow(row))) continue;
                AppendTableRow(table, row, widths);
            }
            AppendSpacer();
        }

        private void AppendTableRow(Table table, TableRow row, int[] widths)
        {
            rtf.Append("\\trowd\\trgaph108\\trleft0");
            int edge = 0;
            for (int c = 0; c < widths.Length; c++)
            {
                edge += ToTwips(widths[c]);
                if (row.IsHeader) rtf.Append("\\clcbpat6");
                rtf.Append(CellBorders).Append("\\cellx").Append(edge);
            }

            for (int c = 0; c < widths.Length; c++)
            {
                rtf.Append("\\pard\\plain\\intbl\\sb40\\sa40").Append(AlignmentOf(table, c)).Append(BaseChar);
                if (row.IsHeader) rtf.Append("\\b");
                rtf.Append(' ');
                if (c < row.Count) RenderCellContent((TableCell)row[c]);
                rtf.Append("\\cell");
            }
            rtf.Append("\\row\n");
        }

        private void RenderCellContent(TableCell cell)
        {
            bool first = true;
            foreach (Block child in cell)
            {
                var leaf = child as LeafBlock;
                if (leaf == null || leaf.Inline == null) continue;
                if (!first) rtf.Append("\\line ");
                RenderInlines(leaf.Inline);
                first = false;
            }
        }

        private static string AlignmentOf(Table table, int column)
        {
            if (column >= table.ColumnDefinitions.Count) return string.Empty;
            TableColumnAlign? align = table.ColumnDefinitions[column].Alignment;
            if (align == TableColumnAlign.Center) return "\\qc";
            if (align == TableColumnAlign.Right) return "\\qr";
            return string.Empty;
        }

        private int[] ColumnWidths(Table table, int columns)
        {
            var widths = new int[columns];
            foreach (Block rowBlock in table)
            {
                var row = rowBlock as TableRow;
                if (row == null) continue;
                Font font = row.IsHeader ? measureBoldFont : measureFont;
                for (int c = 0; c < row.Count && c < columns; c++)
                {
                    string plain = PlainText((TableCell)row[c]);
                    int width = TextRenderer.MeasureText(plain, font, Size.Empty,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                    widths[c] = Math.Max(widths[c], width);
                }
            }

            int total = 0;
            for (int c = 0; c < columns; c++)
            {
                widths[c] = Math.Max(MinColumnPx, widths[c] + CellPaddingPx);
                total += widths[c];
            }

            if (total > availableWidthPx)
            {
                double scale = availableWidthPx / (double)total;
                for (int c = 0; c < columns; c++) widths[c] = Math.Max(MinColumnPx, (int)(widths[c] * scale));
            }
            return widths;
        }

        private static bool IsEmptyRow(TableRow row)
        {
            foreach (Block cell in row)
            {
                if (PlainText((TableCell)cell).Trim().Length > 0) return false;
            }
            return true;
        }

        private static string PlainText(TableCell cell)
        {
            var text = new StringBuilder();
            foreach (Block child in cell)
            {
                var leaf = child as LeafBlock;
                if (leaf != null && leaf.Inline != null) AppendPlainText(leaf.Inline, text);
            }
            return text.ToString();
        }

        private static void AppendPlainText(ContainerInline container, StringBuilder text)
        {
            for (Inline inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            {
                var literal = inline as LiteralInline;
                if (literal != null) { text.Append(literal.Content.ToString()); continue; }

                var code = inline as CodeInline;
                if (code != null) { text.Append(code.Content); continue; }

                var auto = inline as AutolinkInline;
                if (auto != null) { text.Append(auto.Url); continue; }

                var entity = inline as HtmlEntityInline;
                if (entity != null) { text.Append(entity.Transcoded.ToString()); continue; }

                var nested = inline as ContainerInline;
                if (nested != null) AppendPlainText(nested, text);
            }
        }

        private void RenderInlines(ContainerInline container)
        {
            if (container == null) return;

            for (Inline inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            {
                var literal = inline as LiteralInline;
                if (literal != null)
                {
                    AppendEscaped(literal.Content.ToString());
                    continue;
                }

                var emphasis = inline as EmphasisInline;
                if (emphasis != null)
                {
                    if (emphasis.DelimiterChar == '~') rtf.Append("{\\strike ");
                    else if (emphasis.DelimiterCount >= 2) rtf.Append("{\\b ");
                    else rtf.Append("{\\i ");
                    RenderInlines(emphasis);
                    rtf.Append('}');
                    continue;
                }

                var code = inline as CodeInline;
                if (code != null)
                {
                    rtf.Append("{\\f1\\fs19\\highlight4 ");
                    AppendEscaped(code.Content);
                    rtf.Append('}');
                    continue;
                }

                var link = inline as LinkInline;
                if (link != null)
                {
                    if (link.IsImage) RenderImage(link);
                    else
                    {
                        rtf.Append("{\\ul\\cf3 ");
                        RenderInlines(link);
                        rtf.Append('}');
                    }
                    continue;
                }

                var auto = inline as AutolinkInline;
                if (auto != null)
                {
                    rtf.Append("{\\ul\\cf3 ");
                    AppendEscaped(auto.Url);
                    rtf.Append('}');
                    continue;
                }

                var lineBreak = inline as LineBreakInline;
                if (lineBreak != null)
                {
                    // Keep a caption written right under an image on its own line.
                    bool afterImage = inline.PreviousSibling is LinkInline && ((LinkInline)inline.PreviousSibling).IsImage;
                    rtf.Append(lineBreak.IsHard || afterImage ? "\\line " : " ");
                    continue;
                }

                var entity = inline as HtmlEntityInline;
                if (entity != null)
                {
                    AppendEscaped(entity.Transcoded.ToString());
                    continue;
                }

                var html = inline as HtmlInline;
                if (html != null)
                {
                    AppendEscaped(html.Tag);
                    continue;
                }

                if (inline is TaskList) continue;

                var nested = inline as ContainerInline;
                if (nested != null) RenderInlines(nested);
            }
        }

        private void RenderImage(LinkInline image)
        {
            string key = image.Url + "|" + availableWidthPx;
            string picture;
            if (!imageCache.TryGetValue(key, out picture))
            {
                picture = ImageLoader.TryLoadRtf(image.Url, baseDirectory, availableWidthPx, dpi);
                imageCache[key] = picture;
            }

            if (picture != null)
            {
                rtf.Append(picture);
                return;
            }

            var alt = new StringBuilder();
            AppendPlainText(image, alt);
            rtf.Append("{\\i\\cf2 ");
            AppendEscaped("[image: " + alt + "]");
            rtf.Append('}');
        }

        private void AppendSymbol(string symbol)
        {
            rtf.Append("{\\f2 ");
            AppendEscaped(symbol);
            rtf.Append('}');
        }

        // The single point where document text enters the RTF stream.
        private void AppendEscaped(string text)
        {
            if (text == null) return;
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': rtf.Append("\\\\"); break;
                    case '{': rtf.Append("\\{"); break;
                    case '}': rtf.Append("\\}"); break;
                    case '\t': rtf.Append("\\tab "); break;
                    case '\n': rtf.Append("\\line "); break;
                    default:
                        if (c < 0x20) break;
                        if (c < 0x80) rtf.Append(c);
                        else rtf.Append("\\u").Append((int)(short)c).Append('?');
                        break;
                }
            }
        }

        private int ToTwips(int pixels)
        {
            return (int)Math.Round(pixels * 1440.0 / dpi);
        }

        private static string BulletFor(int level)
        {
            switch (level)
            {
                case 0: return "\u2022";
                case 1: return "\u25E6";
                default: return "\u25AA";
            }
        }

        public void Dispose()
        {
            measureFont.Dispose();
            measureBoldFont.Dispose();
        }
    }
}
