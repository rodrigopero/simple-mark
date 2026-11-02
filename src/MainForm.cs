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
        private readonly Timer reloadTimer;

        private FileSystemWatcher watcher;
        private int reloadRetries;
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
            var reloadItem = new ToolStripMenuItem("&Reload", null, OnReload) { ShortcutKeys = Keys.F5 };
            var exitItem = new ToolStripMenuItem("E&xit", null, delegate { Close(); });
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openItem, saveItem, reloadItem, new ToolStripSeparator(), exitItem });

            // Debounces watcher bursts: editors often write a file in several steps.
            reloadTimer = new Timer { Interval = 300 };
            reloadTimer.Tick += OnReloadTimerTick;

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
                StopWatching();
                reloadTimer.Dispose();
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

        private void OnReload(object sender, EventArgs e)
        {
            if (currentPath == null || !ConfirmDiscard()) return;
            ReloadFromDisk(false);
        }

        private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
        {
            reloadRetries = 0;
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        private void OnReloadTimerTick(object sender, EventArgs e)
        {
            reloadTimer.Stop();
            // Never discard unsaved edits automatically; F5 still offers to.
            if (currentPath == null || dirty) return;
            if (!ReloadFromDisk(true) && reloadRetries++ < 3) reloadTimer.Start();
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
                string normalized = ReadDocument(fullPath);

                suppressDirty = true;
                editor.Text = normalized.Replace("\n", "\r\n");
                suppressDirty = false;

                currentPath = fullPath;
                renderer.Render(normalized, DocumentDirectory());
                dirty = false;
                if (editor.Visible) OnToggleEdit(this, EventArgs.Empty);
                UpdateTitle();
                Watch(fullPath);
            }
            catch (Exception ex)
            {
                suppressDirty = false;
                if (!IsFileError(ex)) throw;
                ShowError("Could not open the file.", ex);
            }
        }

        // Re-reads the current file in place, keeping the active view and its position.
        private bool ReloadFromDisk(bool silent)
        {
            try
            {
                string normalized = ReadDocument(currentPath);
                string editorText = normalized.Replace("\n", "\r\n");
                if (silent && editorText == editor.Text) return true;

                int firstVisible = viewer.Visible ? viewer.GetCharIndexFromPosition(new Point(1, 1)) : 0;
                int caret = editor.SelectionStart;

                suppressDirty = true;
                editor.Text = editorText;
                suppressDirty = false;
                dirty = false;
                renderer.Render(normalized, DocumentDirectory());

                if (viewer.Visible)
                {
                    viewer.Select(Math.Min(firstVisible, viewer.TextLength), 0);
                    viewer.ScrollToCaret();
                }
                else
                {
                    editor.Select(Math.Min(caret, editor.TextLength), 0);
                    editor.ScrollToCaret();
                }
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                suppressDirty = false;
                if (!IsFileError(ex)) throw;
                if (!silent) ShowError("Could not reload the file.", ex);
                return false;
            }
        }

        // Returns the document with "\n" line endings and records its BOM and line-ending style.
        private string ReadDocument(string fullPath)
        {
            byte[] bytes = SafeFile.ReadBounded(fullPath, MaxFileBytes);

            hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            int offset = hasBom ? 3 : 0;
            string text = Utf8NoBom.GetString(bytes, offset, bytes.Length - offset);

            useLf = text.IndexOf('\n') >= 0 && text.IndexOf("\r\n", StringComparison.Ordinal) < 0;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private void Watch(string fullPath)
        {
            StopWatching();
            try
            {
                watcher = new FileSystemWatcher(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    SynchronizingObject = this,
                };
                watcher.Changed += OnWatchedFileChanged;
                watcher.Created += OnWatchedFileChanged;
                watcher.Renamed += OnWatchedFileChanged;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                // Some locations (e.g. certain network shares) cannot be watched; F5 still works.
                StopWatching();
                if (!IsFileError(ex)) throw;
            }
        }

        private void StopWatching()
        {
            reloadTimer.Stop();
            if (watcher == null) return;
            watcher.Dispose();
            watcher = null;
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
                string fullPath = Path.GetFullPath(path);
                if (fullPath != currentPath) Watch(fullPath);
                currentPath = fullPath;
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
