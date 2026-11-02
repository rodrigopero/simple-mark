using System;
using System.Drawing;
using System.Windows.Forms;

namespace SimpleMark
{
    /// <summary>Shown in the document area while no tab is open.</summary>
    internal sealed class StartScreen : Control
    {
        private const int IconSize = 64;
        private const int ShortcutGap = 32;

        private static readonly string[] LinkTexts = { "Open a file...", "New document" };
        private static readonly string[] ShortcutTexts = { "Ctrl+O", "Ctrl+T" };
        private const string DropHint = "or drop Markdown files here";

        private readonly Icon sourceIcon;
        private readonly Rectangle[] linkBounds = new Rectangle[LinkTexts.Length];

        private Icon icon;
        private Font titleFont;
        private Font linkFont;
        private Palette palette = Palette.Light;
        private Rectangle iconBounds;
        private Rectangle titleBounds;
        private Rectangle dropHintBounds;
        private int shortcutRight;
        private int hoverLink = -1;

        public StartScreen(Icon appIcon)
        {
            sourceIcon = appIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public event EventHandler OpenRequested;

        public event EventHandler NewRequested;

        public Palette Palette
        {
            set
            {
                palette = value;
                BackColor = value.Background;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeResources();
            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            DisposeResources();
            UpdateLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(palette.Background);
            if (titleFont == null) return;

            if (icon != null) g.DrawIcon(icon, iconBounds);
            DrawCentered(g, MainForm.AppName, titleFont, titleBounds, palette.Text);

            for (int i = 0; i < LinkTexts.Length; i++)
            {
                Rectangle r = linkBounds[i];
                TextRenderer.DrawText(g, LinkTexts[i], i == hoverLink ? linkFont : Font, r, palette.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                var shortcut = Rectangle.FromLTRB(r.Right, r.Top, shortcutRight, r.Bottom);
                TextRenderer.DrawText(g, ShortcutTexts[i], Font, shortcut, palette.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            DrawCentered(g, DropHint, Font, dropHintBounds, palette.Muted);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int link = HitTest(e.Location);
            if (link == hoverLink) return;
            hoverLink = link;
            Cursor = link < 0 ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverLink = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int link = HitTest(e.Location);
            EventHandler handler = link == 0 ? OpenRequested : link == 1 ? NewRequested : null;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void UpdateLayout()
        {
            if (titleFont == null)
            {
                titleFont = new Font(Font.FontFamily, Font.SizeInPoints * 2f, FontStyle.Regular, GraphicsUnit.Point);
                linkFont = new Font(Font, FontStyle.Underline);
            }

            int iconSize = Scale(IconSize);
            if (sourceIcon != null && (icon == null || icon.Width != iconSize))
            {
                if (icon != null) icon.Dispose();
                icon = new Icon(sourceIcon, iconSize, iconSize);
            }

            Size title = TextRenderer.MeasureText(MainForm.AppName, titleFont);
            Size dropHint = TextRenderer.MeasureText(DropHint, Font);
            int rowHeight = Font.Height + Scale(12);
            int linkWidth = 0;
            int shortcutWidth = 0;
            for (int i = 0; i < LinkTexts.Length; i++)
            {
                linkWidth = Math.Max(linkWidth, TextRenderer.MeasureText(LinkTexts[i], linkFont).Width);
                shortcutWidth = Math.Max(shortcutWidth, TextRenderer.MeasureText(ShortcutTexts[i], Font).Width);
            }
            int columnWidth = linkWidth + Scale(ShortcutGap) + shortcutWidth;

            int height = iconSize + Scale(12) + title.Height + Scale(24) + rowHeight * LinkTexts.Length
                + Scale(16) + dropHint.Height;
            // Slightly above the middle reads as centered.
            int y = Math.Max(0, (ClientSize.Height - height) * 2 / 5);
            int center = ClientSize.Width / 2;

            iconBounds = new Rectangle(center - iconSize / 2, y, iconSize, iconSize);
            y += iconSize + Scale(12);
            titleBounds = new Rectangle(center - title.Width / 2, y, title.Width, title.Height);
            y += title.Height + Scale(24);

            int left = center - columnWidth / 2;
            shortcutRight = left + columnWidth;
            for (int i = 0; i < LinkTexts.Length; i++)
            {
                int width = TextRenderer.MeasureText(LinkTexts[i], linkFont).Width;
                linkBounds[i] = new Rectangle(left, y, width, rowHeight);
                y += rowHeight;
            }
            y += Scale(16);
            dropHintBounds = new Rectangle(center - dropHint.Width / 2, y, dropHint.Width, dropHint.Height);
            Invalidate();
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < linkBounds.Length; i++)
            {
                if (linkBounds[i].Contains(location)) return i;
            }
            return -1;
        }

        private void DisposeResources()
        {
            if (titleFont != null) titleFont.Dispose();
            if (linkFont != null) linkFont.Dispose();
            if (icon != null) icon.Dispose();
            titleFont = null;
            linkFont = null;
            icon = null;
        }

        private int Scale(int value)
        {
            return value * DeviceDpi / 96;
        }

        private static void DrawCentered(Graphics g, string text, Font font, Rectangle bounds, Color color)
        {
            TextRenderer.DrawText(g, text, font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
