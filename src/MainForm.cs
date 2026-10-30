using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace SimpleMark
{
    internal sealed class MainForm : Form
    {
        private const long MaxFileBytes = 5L * 1024 * 1024;
        private const string AppName = "Simple Mark";
        private const string FileFilter =
            "Markdown (*.md;*.markdown)|*.md;*.markdown|Text (*.txt)|*.txt|All files (*.*)|*.*";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly RichTextView viewer;
        private readonly PlainTextEditor editor;
        private readonly Icon appIcon;
        private readonly ToolStripMenuItem editItem;
        private readonly ToolStripMenuItem darkItem;
        private readonly MenuStrip menu;
        private readonly Panel divider;
        private readonly MarkdownRenderer renderer;
        private readonly string initialPath;

        private string currentPath;
        private bool dirty;
        private bool suppressDirty;
        private bool hasBom;
        private bool useLf;
        private FormWindowState lastWindowState;
        private Palette palette = Palette.FromSystem();

        public MainForm(string initialPath)
        {
            this.initialPath = initialPath;

            Text = AppName;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(1000, 760);
            MinimumSize = new Size(480, 360);
            StartPosition = FormStartPosition.CenterScreen;

            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("SimpleMark.app.ico"))
            {
                if (iconStream != null)
                {
                    appIcon = new Icon(iconStream);
                    Icon = appIcon;
                }
            }

            viewer = new RichTextView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                EnableAutoDragDrop = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };

            editor = new PlainTextEditor
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsTab = true,
                AcceptsReturn = true,
                MaxLength = 0,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10.5f),
                Visible = false,
            };
            editor.TextChanged += OnEditorTextChanged;

            var openItem = new ToolStripMenuItem("&Open...", null, OnOpen) { ShortcutKeys = Keys.Control | Keys.O };
            var saveItem = new ToolStripMenuItem("&Save", null, OnSave) { ShortcutKeys = Keys.Control | Keys.S };
            var exitItem = new ToolStripMenuItem("E&xit", null, delegate { Close(); });
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openItem, saveItem, new ToolStripSeparator(), exitItem });

            editItem = new ToolStripMenuItem("&Edit plain text", null, OnToggleEdit) { ShortcutKeys = Keys.Control | Keys.E };
            var viewMenu = new ToolStripMenuItem("&View");
            darkItem = new ToolStripMenuItem("&Dark mode", null, OnToggleDark) { ShortcutKeys = Keys.Control | Keys.D };
            viewMenu.DropDownItems.AddRange(new ToolStripItem[] { editItem, darkItem });

            menu = new MenuStrip { Padding = new Padding(8, 4, 0, 4) };
            menu.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu });

            // Thin brand-colored line between the menu and the document.
            divider = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Palette.Brand };

            // Fill controls first: WinForms docks the last added control first.
            Controls.Add(viewer);
            Controls.Add(editor);
            Controls.Add(divider);
            Controls.Add(menu);
            MainMenuStrip = menu;

            renderer = new MarkdownRenderer(viewer);
            ApplyTheme();
            UpdateTitle();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.ApplyToWindow(this, palette);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!string.IsNullOrEmpty(initialPath)) OpenFile(initialPath);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RerenderIfWidthChanged();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (WindowState == lastWindowState) return;
            lastWindowState = WindowState;
            if (WindowState != FormWindowState.Minimized) RerenderIfWidthChanged();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                renderer.Dispose();
                editor.Font.Dispose();
                if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnOpen(object sender, EventArgs e)
        {
            if (!ConfirmDiscard()) return;

            using (var dialog = new OpenFileDialog { Filter = FileFilter, CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenFile(dialog.FileName);
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            Save();
        }

        private void OnToggleEdit(object sender, EventArgs e)
        {
            if (editor.Visible)
            {
                renderer.Render(editor.Text, DocumentDirectory());
                editor.Visible = false;
                viewer.Visible = true;
                viewer.Focus();
            }
            else
            {
                viewer.Visible = false;
                editor.Visible = true;
                editor.Focus();
            }
            editItem.Checked = editor.Visible;
        }

        private void OnToggleDark(object sender, EventArgs e)
        {
            palette = palette.IsDark ? Palette.Light : Palette.Dark;
            ApplyTheme();
            if (viewer.Visible) RerenderPreservingScroll();
            else renderer.Rerender();
        }

        private void ApplyTheme()
        {
            BackColor = palette.Background;
            viewer.BackColor = palette.Background;
            viewer.DarkScrollbars = palette.IsDark;
            editor.BackColor = palette.Background;
            editor.ForeColor = palette.Text;
            editor.DarkScrollbars = palette.IsDark;
            menu.Renderer = Theme.CreateMenuRenderer(palette);
            menu.BackColor = palette.Background;
            menu.ForeColor = palette.Text;
            darkItem.Checked = palette.IsDark;
            renderer.Palette = palette;
            WindowChrome.ApplyToWindow(this, palette);
        }

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            if (suppressDirty || dirty) return;
            dirty = true;
            UpdateTitle();
        }

        private void OpenFile(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                byte[] bytes = SafeFile.ReadBounded(fullPath, MaxFileBytes);

                hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                int offset = hasBom ? 3 : 0;
                string text = Utf8NoBom.GetString(bytes, offset, bytes.Length - offset);

                useLf = text.IndexOf('\n') >= 0 && text.IndexOf("\r\n", StringComparison.Ordinal) < 0;
                string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

                suppressDirty = true;
                editor.Text = normalized.Replace("\n", "\r\n");
                suppressDirty = false;

                currentPath = fullPath;
                renderer.Render(normalized, DocumentDirectory());
                dirty = false;
                if (editor.Visible) OnToggleEdit(this, EventArgs.Empty);
                UpdateTitle();
            }
            catch (Exception ex)
            {
                suppressDirty = false;
                if (!IsFileError(ex)) throw;
                ShowError("Could not open the file.", ex);
            }
        }


        private bool Save()
        {
            string path = currentPath;
            if (path == null)
            {
                using (var dialog = new SaveFileDialog { Filter = FileFilter, DefaultExt = "md", OverwritePrompt = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = dialog.FileName;
                }
            }

            try
            {
                string text = editor.Text;
                if (useLf) text = text.Replace("\r\n", "\n");

                SafeFile.WriteAtomic(path, text, hasBom ? new UTF8Encoding(true) : Utf8NoBom);
                currentPath = path;
                dirty = false;
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                ShowError("Could not save the file.", ex);
                return false;
            }
        }


        private bool ConfirmDiscard()
        {
            if (!dirty) return true;

            DialogResult answer = MessageBox.Show(this, "Save changes before continuing?", AppName,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) return Save();
            return answer == DialogResult.No;
        }

        private string DocumentDirectory()
        {
            return currentPath == null ? null : Path.GetDirectoryName(currentPath);
        }

        private void RerenderIfWidthChanged()
        {
            if (!viewer.Visible || viewer.ContentWidth == renderer.RenderedWidth) return;
            RerenderPreservingScroll();
        }

        private void RerenderPreservingScroll()
        {
            int firstVisible = viewer.GetCharIndexFromPosition(new Point(1, 1));
            renderer.Rerender();
            viewer.Select(firstVisible, 0);
            viewer.ScrollToCaret();
        }

        private void UpdateTitle()
        {
            string name = currentPath == null ? "Untitled" : Path.GetFileName(currentPath);
            Text = (dirty ? "*" : string.Empty) + name + " - " + AppName;
        }

        private void ShowError(string message, Exception ex)
        {
            MessageBox.Show(this, message + Environment.NewLine + ex.Message, AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is SecurityException
                || ex is ArgumentException
                || ex is NotSupportedException;
        }
    }
}
