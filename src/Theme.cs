using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SimpleMark
{
    internal sealed class Palette
    {
        public static readonly Color Brand = Color.FromArgb(0x59, 0x11, 0x73);

        public static readonly Palette Light = new Palette(false)
        {
            Background = Color.White,
            Text = Color.FromArgb(36, 41, 47),
            Muted = Color.FromArgb(101, 109, 118),
            Border = Color.FromArgb(229, 231, 235),
            Hover = Color.FromArgb(244, 236, 247),
            Pressed = Color.FromArgb(235, 221, 241),
            Accent = Brand,
            CodeBackground = Color.FromArgb(243, 244, 246),
            TableHeader = Color.FromArgb(248, 242, 250),
        };

        public static readonly Palette Dark = new Palette(true)
        {
            Background = Color.FromArgb(30, 30, 34),
            Text = Color.FromArgb(228, 228, 231),
            Muted = Color.FromArgb(161, 161, 170),
            Border = Color.FromArgb(63, 63, 70),
            Hover = Color.FromArgb(51, 40, 58),
            Pressed = Color.FromArgb(64, 47, 73),
            // The brand color is too dark to read on a dark background, so use a lighter tint.
            Accent = Color.FromArgb(195, 139, 217),
            CodeBackground = Color.FromArgb(42, 42, 48),
            TableHeader = Color.FromArgb(45, 36, 52),
        };

        private Palette(bool isDark)
        {
            IsDark = isDark;
        }

        public bool IsDark { get; private set; }
        public Color Background { get; private set; }
        public Color Text { get; private set; }
        public Color Muted { get; private set; }
        public Color Border { get; private set; }
        public Color Hover { get; private set; }
        public Color Pressed { get; private set; }
        public Color Accent { get; private set; }
        public Color CodeBackground { get; private set; }
        public Color TableHeader { get; private set; }

        /// <summary>Follows the Windows "app mode" setting; defaults to light if it cannot be read.</summary>
        public static Palette FromSystem()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0 ? Dark : Light;
                }
            }
            catch (System.Security.SecurityException)
            {
                return Light;
            }
            catch (UnauthorizedAccessException)
            {
                return Light;
            }
        }
    }

    internal static class Theme
    {
        public static ToolStripRenderer CreateMenuRenderer(Palette palette)
        {
            return new FlatMenuRenderer(palette);
        }

        private sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly Palette palette;

            public FlatMenuRenderer(Palette palette)
                : base(new FlatColors(palette))
            {
                this.palette = palette;
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? palette.Text : palette.Muted;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = palette.Text;
                base.OnRenderArrow(e);
            }

            // The default check glyph is black, which disappears on a dark background.
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = e.ImageRectangle;
                using (var back = new SolidBrush(palette.Pressed))
                {
                    e.Graphics.FillRectangle(back, r);
                }

                SmoothingMode previous = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(palette.Accent, Math.Max(1.5f, r.Height / 8f)))
                {
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(r.Left + r.Width * 0.25f, r.Top + r.Height * 0.52f),
                        new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.70f),
                        new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.32f),
                    });
                }
                e.Graphics.SmoothingMode = previous;
            }
        }

        /// <summary>Flat colors replacing the default WinForms menu gradients.</summary>
        private sealed class FlatColors : ProfessionalColorTable
        {
            private readonly Palette p;

            public FlatColors(Palette palette)
            {
                p = palette;
                UseSystemColors = false;
            }

            public override Color MenuStripGradientBegin { get { return p.Background; } }
            public override Color MenuStripGradientEnd { get { return p.Background; } }
            public override Color MenuBorder { get { return p.Border; } }
            public override Color MenuItemBorder { get { return p.Hover; } }
            public override Color MenuItemSelected { get { return p.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return p.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return p.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return p.Pressed; } }
            public override Color MenuItemPressedGradientMiddle { get { return p.Pressed; } }
            public override Color MenuItemPressedGradientEnd { get { return p.Pressed; } }
            public override Color ToolStripDropDownBackground { get { return p.Background; } }
            public override Color ImageMarginGradientBegin { get { return p.Background; } }
            public override Color ImageMarginGradientMiddle { get { return p.Background; } }
            public override Color ImageMarginGradientEnd { get { return p.Background; } }
            public override Color SeparatorDark { get { return p.Border; } }
            public override Color SeparatorLight { get { return p.Background; } }
        }
    }

    /// <summary>Applies theme colors to native window parts (title bar, border, scrollbars).</summary>
    internal static class WindowChrome
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;
        private const int DwmwaBorderColor = 34;

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        public static void ApplyToWindow(Form form, Palette palette)
        {
            if (!form.IsHandleCreated) return;

            int dark = palette.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeLegacy, ref dark, sizeof(int));
            }

            // Windows 11 only: a thin brand-colored window border. Ignored on older versions.
            int border = ColorTranslator.ToWin32(Palette.Brand);
            DwmSetWindowAttribute(form.Handle, DwmwaBorderColor, ref border, sizeof(int));
        }

        public static void ApplyToScrollbars(Control control, bool dark)
        {
            if (control.IsHandleCreated) SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }
}
