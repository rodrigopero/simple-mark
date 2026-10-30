using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimpleMark
{
    /// <summary>
    /// RichTextBox backed by RichEdit 4.1+ (msftedit.dll), which renders tables,
    /// embedded PNG/JPEG pictures and font fallback correctly.
    /// </summary>
    internal sealed class RichTextView : RichTextBox
    {
        private const uint LoadLibrarySearchSystem32 = 0x00000800;
        private const int WmSetFocus = 0x0007;
        private const int WmSetText = 0x000C;
        private const int WmPaint = 0x000F;
        private const int EmSetSel = 0x00B1;
        private const int EmScrollCaret = 0x00B7;
        private const int EmExSetSel = 0x0437;
        private const int EmStreamIn = 0x0449;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmLButtonDown = 0x0201;
        private const int WmMButtonDblClk = 0x0209;
        private const int WmMouseWheel = 0x020A;
        private const int EmGetScrollPos = 0x04DD;
        private const int EmSetScrollPos = 0x04DE;
        private const int MkControl = 0x0008;
        private const int WheelDelta = 120;
        private const int WheelLinePixels = 40;

        private static IntPtr msftedit;
        private int wheelRemainder;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HideCaret(IntPtr hWnd);

        /// <summary>Width in pixels of the centered content column.</summary>
        public int ContentWidth { get; private set; }

        public void UpdateContentRect()
        {
            ContentWidth = ContentLayout.Apply(this);
        }

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
            UpdateContentRect();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateContentRect();
        }

        // RichEdit animates wheel scrolling, which feels laggy; scroll by pixels immediately instead.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel && ScrollByWheel(m.WParam))
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);

            // The view is read-only, so never show the text caret; selection and copy still work.
            if (ShowsCaret(m.Msg)) HideCaret(Handle);
        }

        // Messages after which RichEdit (re)displays the caret: focus, painting (it recreates the
        // caret after new content is laid out), input, loading content and selection changes.
        private static bool ShowsCaret(int msg)
        {
            return msg == WmSetFocus || msg == WmPaint || msg == WmSetText || msg == WmKeyDown || msg == WmKeyUp
                || (msg >= WmLButtonDown && msg <= WmMButtonDblClk)
                || msg == EmSetSel || msg == EmScrollCaret || msg == EmExSetSel || msg == EmStreamIn;
        }

        private bool ScrollByWheel(IntPtr wParam)
        {
            long value = wParam.ToInt64();
            int keys = (int)(value & 0xFFFF);
            int delta = (short)((value >> 16) & 0xFFFF);
            if ((keys & MkControl) != 0 || delta == 0) return false;

            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0
                ? ClientSize.Height
                : lines * WheelLinePixels * DeviceDpi / 96;

            // Keep fractional deltas from high-resolution wheels and touchpads.
            wheelRemainder += delta * step;
            int pixels = wheelRemainder / WheelDelta;
            wheelRemainder %= WheelDelta;
            if (pixels == 0) return true;

            var position = new Point();
            SendMessage(Handle, EmGetScrollPos, IntPtr.Zero, ref position);
            position.Y = Math.Max(0, position.Y - pixels);
            SendMessage(Handle, EmSetScrollPos, IntPtr.Zero, ref position);
            return true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (msftedit == IntPtr.Zero)
                {
                    // Restrict the search to System32 to prevent DLL planting.
                    msftedit = LoadLibraryExW("msftedit.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
                }
                if (msftedit != IntPtr.Zero) cp.ClassName = "RICHEDIT50W";
                return cp;
            }
        }
    }
}
