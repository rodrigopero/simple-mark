using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimpleMark
{
    /// <summary>
    /// Centers the text area of an edit control in a column of limited width by setting its
    /// formatting rectangle, so the scrollbar stays at the window edge.
    /// </summary>
    internal static class ContentLayout
    {
        private const int EmSetRect = 0x00B3;
        private const int MaxContentWidth = 880;
        private const int MinSideMargin = 24;
        private const int TopMargin = 24;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Rect lParam);

        /// <returns>The width in pixels available to the content.</returns>
        public static int Apply(Control control)
        {
            if (!control.IsHandleCreated) return 0;

            float scale = control.DeviceDpi / 96f;
            int width = control.ClientSize.Width;
            int maxWidth = (int)(MaxContentWidth * scale);
            int side = Math.Max((int)(MinSideMargin * scale), (width - maxWidth) / 2);

            var rect = new Rect
            {
                Left = side,
                Top = (int)(TopMargin * scale),
                Right = Math.Max(side + 1, width - side),
                Bottom = Math.Max(1, control.ClientSize.Height),
            };
            SendMessage(control.Handle, EmSetRect, IntPtr.Zero, ref rect);
            return rect.Right - rect.Left;
        }
    }

    internal sealed class PlainTextEditor : TextBox
    {
        private bool darkScrollbars;

        public bool DarkScrollbars
        {
            get { return darkScrollbars; }
            set
            {
                darkScrollbars = value;
                WindowChrome.ApplyToScrollbars(this, value);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.ApplyToScrollbars(this, darkScrollbars);
            ContentLayout.Apply(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ContentLayout.Apply(this);
        }
    }
}
