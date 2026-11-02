using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimpleMark
{
    internal sealed class TabEventArgs : EventArgs
    {
        public TabEventArgs(DocumentTab tab)
        {
            Tab = tab;
        }

        public DocumentTab Tab { get; private set; }
    }

    /// <summary>
    /// Owner-drawn tab strip themed from the palette; the stock TabControl cannot be drawn dark.
    /// Tabs shrink towards a minimum width when they do not fit, then the strip scrolls to keep
    /// the selected tab visible.
    /// </summary>
    internal sealed class TabStrip : Control
    {
        private const int MinTabWidth = 72;
        private const int MaxTabWidth = 220;
        private const int TextPadding = 12;
        private const int CloseSize = 16;
        private const int CloseMargin = 6;
        private const int AccentHeight = 2;

        private readonly List<DocumentTab> tabs = new List<DocumentTab>();
        private readonly List<Rectangle> tabBounds = new List<Rectangle>();
        private readonly ToolTip toolTip = new ToolTip();

        private Palette palette = Palette.Light;
        private DocumentTab selected;
        private int scrollOffset;
        private int hoverIndex = -1;
        private bool hoverClose;
        private int pressedCloseIndex = -1;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Clicking a tab must not take focus away from the document.
            SetStyle(ControlStyles.Selectable, false);
            Dock = DockStyle.Top;
            UpdateHeight();
        }

        public event EventHandler SelectedTabChanged;

        public event EventHandler<TabEventArgs> CloseRequested;

        public ReadOnlyCollection<DocumentTab> Tabs
        {
            get { return tabs.AsReadOnly(); }
        }

        public DocumentTab SelectedTab
        {
            get { return selected; }
            set
            {
                if (value == selected || (value != null && !tabs.Contains(value))) return;
                selected = value;
                RefreshTabs();
                OnSelectedTabChanged();
            }
        }

        public Palette Palette
        {
            set
            {
                palette = value;
                Invalidate();
            }
        }

        public void Add(DocumentTab tab)
        {
            tabs.Add(tab);
            RefreshTabs();
        }

        /// <summary>Removes the tab; if it was selected, selects its right neighbor, else its left one.</summary>
        public void Remove(DocumentTab tab)
        {
            int index = tabs.IndexOf(tab);
            if (index < 0) return;
            tabs.RemoveAt(index);
            hoverIndex = -1;
            pressedCloseIndex = -1;
            if (tab != selected)
            {
                RefreshTabs();
                return;
            }
            selected = tabs.Count == 0 ? null : tabs[Math.Min(index, tabs.Count - 1)];
            RefreshTabs();
            OnSelectedTabChanged();
        }

        /// <summary>Selects the tab <paramref name="step"/> positions away, wrapping around.</summary>
        public void SelectNext(int step)
        {
            if (tabs.Count < 2) return;
            int index = tabs.IndexOf(selected);
            SelectedTab = tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count];
        }

        /// <summary>Recomputes tab sizes after a tab's name or dirty state changed.</summary>
        public void RefreshTabs()
        {
            LayoutTabs();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTip.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateHeight();
            RefreshTabs();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTabs();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(palette.CodeBackground);

            for (int i = 0; i < tabs.Count; i++)
            {
                Rectangle r = tabBounds[i];
                if (r.Right <= 0 || r.Left >= ClientSize.Width) continue;
                PaintTab(g, i, r);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            bool overClose = index >= 0 && CloseBounds(tabBounds[index]).Contains(e.Location);
            if (index == hoverIndex && overClose == hoverClose) return;

            if (index != hoverIndex)
            {
                toolTip.SetToolTip(this, index < 0 ? null : ToolTipFor(tabs[index]));
            }
            hoverIndex = index;
            hoverClose = overClose;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            hoverClose = false;
            toolTip.SetToolTip(this, null);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = HitTest(e.Location);
            if (index < 0 || e.Button != MouseButtons.Left) return;

            if (CloseBounds(tabBounds[index]).Contains(e.Location)) pressedCloseIndex = index;
            else SelectedTab = tabs[index];
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int index = HitTest(e.Location);
            int pressed = pressedCloseIndex;
            pressedCloseIndex = -1;
            if (index < 0) return;

            bool closeClicked = e.Button == MouseButtons.Left && index == pressed
                && CloseBounds(tabBounds[index]).Contains(e.Location);
            if (closeClicked || e.Button == MouseButtons.Middle) RequestClose(tabs[index]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta != 0) SelectNext(e.Delta > 0 ? -1 : 1);
        }

        private void OnSelectedTabChanged()
        {
            EventHandler handler = SelectedTabChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RequestClose(DocumentTab tab)
        {
            EventHandler<TabEventArgs> handler = CloseRequested;
            if (handler != null) handler(this, new TabEventArgs(tab));
        }

        private void PaintTab(Graphics g, int index, Rectangle r)
        {
            DocumentTab tab = tabs[index];
            bool active = tab == selected;
            bool hover = index == hoverIndex;

            Color back = active ? palette.Background : hover ? palette.Hover : palette.CodeBackground;
            using (var brush = new SolidBrush(back)) g.FillRectangle(brush, r);

            if (active)
            {
                using (var accent = new SolidBrush(palette.Accent))
                {
                    g.FillRectangle(accent, r.Left, r.Top, r.Width, Scale(AccentHeight));
                }
            }
            else
            {
                int inset = r.Height / 4;
                using (var separator = new Pen(palette.Border))
                {
                    g.DrawLine(separator, r.Right - 1, r.Top + inset, r.Right - 1, r.Bottom - inset);
                }
            }

            Rectangle close = CloseBounds(r);
            var textBounds = Rectangle.FromLTRB(r.Left + Scale(TextPadding), r.Top, close.Left - Scale(2), r.Bottom);
            TextRenderer.DrawText(g, Label(tab), Font, textBounds, active ? palette.Text : palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            PaintClose(g, close, hover && hoverClose, active || hover ? palette.Text : palette.Muted);
        }

        private void PaintClose(Graphics g, Rectangle close, bool highlighted, Color color)
        {
            if (highlighted)
            {
                using (var brush = new SolidBrush(palette.Pressed)) g.FillRectangle(brush, close);
            }

            int inset = close.Width * 5 / 16;
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1f, DeviceDpi / 96f * 1.2f)))
            {
                g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
                g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
            }
            g.SmoothingMode = previous;
        }

        private void LayoutTabs()
        {
            tabBounds.Clear();
            if (tabs.Count == 0) return;

            int minWidth = Scale(MinTabWidth);
            int maxWidth = Scale(MaxTabWidth);
            int chrome = Scale(TextPadding) + Scale(CloseSize) + Scale(CloseMargin) * 2;
            int available = ClientSize.Width;
            var widths = new int[tabs.Count];
            int total = 0;
            for (int i = 0; i < tabs.Count; i++)
            {
                int preferred = TextRenderer.MeasureText(Label(tabs[i]), Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + chrome;
                widths[i] = Math.Max(minWidth, Math.Min(maxWidth, preferred));
                total += widths[i];
            }

            // Too wide: cap every tab at an equal share, never below the minimum width.
            if (total > available)
            {
                int share = Math.Max(minWidth, available / tabs.Count);
                total = 0;
                for (int i = 0; i < widths.Length; i++)
                {
                    widths[i] = Math.Min(widths[i], share);
                    total += widths[i];
                }
            }

            int x = 0;
            int selectedLeft = 0;
            int selectedRight = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                if (tabs[i] == selected)
                {
                    selectedLeft = x;
                    selectedRight = x + widths[i];
                }
                x += widths[i];
            }

            if (selectedRight - scrollOffset > available) scrollOffset = selectedRight - available;
            if (selectedLeft < scrollOffset) scrollOffset = selectedLeft;
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, total - available));

            x = -scrollOffset;
            for (int i = 0; i < widths.Length; i++)
            {
                tabBounds.Add(new Rectangle(x, 0, widths[i], ClientSize.Height));
                x += widths[i];
            }
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < tabBounds.Count; i++)
            {
                if (tabBounds[i].Contains(location)) return i;
            }
            return -1;
        }

        private Rectangle CloseBounds(Rectangle tab)
        {
            int size = Scale(CloseSize);
            return new Rectangle(tab.Right - Scale(CloseMargin) - size, tab.Top + (tab.Height - size) / 2, size, size);
        }

        private void UpdateHeight()
        {
            Height = Font.Height + Scale(14);
        }

        private int Scale(int value)
        {
            return value * DeviceDpi / 96;
        }

        private static string Label(DocumentTab tab)
        {
            return (tab.IsDirty ? "*" : string.Empty) + tab.DisplayName;
        }

        private static string ToolTipFor(DocumentTab tab)
        {
            return tab.FullPath ?? tab.DisplayName;
        }
    }
}
