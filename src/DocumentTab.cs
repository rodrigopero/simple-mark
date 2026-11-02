using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace SimpleMark
{
    /// <summary>One open document: its viewer, editor, file state and disk watcher.</summary>
    internal sealed class DocumentTab : Panel
    {
        public const string FileFilter =
            "Markdown (*.md;*.markdown)|*.md;*.markdown|Text (*.txt)|*.txt|All files (*.*)|*.*";

        private const long MaxFileBytes = 5L * 1024 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly RichTextView viewer;
        private readonly PlainTextEditor editor;
        private readonly MarkdownRenderer renderer;
        private readonly Timer reloadTimer;

        private FileSystemWatcher watcher;
        private int reloadRetries;
        private string currentPath;
        private bool dirty;
        private bool suppressDirty;
        private bool hasBom;
        private bool useLf;
        private bool editing;
        private bool paletteChanged;
        private int contentVersion;

        public DocumentTab(Palette palette)
        {
            viewer = new RichTextView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                EnableAutoDragDrop = false,
                // Keeps a find match visible while the find box has focus.
                HideSelection = false,
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
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10.5f),
                Visible = false,
            };
            editor.TextChanged += OnEditorTextChanged;

            // Debounces watcher bursts: editors often write a file in several steps.
            reloadTimer = new Timer { Interval = 300 };
            reloadTimer.Tick += OnReloadTimerTick;

            Controls.Add(viewer);
            Controls.Add(editor);

            renderer = new MarkdownRenderer(viewer);
            ApplyPalette(palette);
            paletteChanged = false;
        }

        /// <summary>Raised when the path, dirty flag or edit mode changes.</summary>
        public event EventHandler StateChanged;

        public string FullPath
        {
            get { return currentPath; }
        }

        public string DisplayName
        {
            get { return currentPath == null ? "Untitled" : Path.GetFileName(currentPath); }
        }

        public bool IsDirty
        {
            get { return dirty; }
        }

        public bool IsEditing
        {
            get { return editing; }
        }

        /// <summary>An untitled, empty, unmodified document that can be replaced by an opened file.</summary>
        public bool IsPristine
        {
            get { return currentPath == null && !dirty && editor.TextLength == 0; }
        }

        /// <summary>Changes whenever the text of either view changes, so cached search results can expire.</summary>
        public int ContentVersion
        {
            get { return contentVersion; }
        }

        /// <summary>Start of the selection in the visible view (viewer or editor).</summary>
        public int SelectionStart
        {
            get { return ActiveView.SelectionStart; }
        }

        /// <summary>The selected text of the visible view if it is a short single line, else null.</summary>
        public string SelectedLine
        {
            get
            {
                string text = ActiveView.SelectedText;
                bool usable = text.Length > 0 && text.Length <= 200 && text.IndexOfAny(new[] { '\r', '\n' }) < 0;
                return usable ? text : null;
            }
        }

        private TextBoxBase ActiveView
        {
            get { return editing ? (TextBoxBase)editor : viewer; }
        }

        public bool Open(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string normalized = ReadDocument(fullPath);

                suppressDirty = true;
                editor.Text = normalized.Replace("\n", "\r\n");
                suppressDirty = false;

                currentPath = fullPath;
                Render(normalized);
                dirty = false;
                if (editing) ToggleEdit();
                OnStateChanged();
                Watch(fullPath);
                return true;
            }
            catch (Exception ex)
            {
                suppressDirty = false;
                if (!IsFileError(ex)) throw;
                ShowError("Could not open the file.", ex);
                return false;
            }
        }

        public void Reload()
        {
            if (currentPath == null || !ConfirmDiscard(FindForm())) return;
            ReloadFromDisk(false);
        }

        public bool Save()
        {
            string path = currentPath;
            if (path == null)
            {
                using (var dialog = new SaveFileDialog { Filter = FileFilter, DefaultExt = "md", OverwritePrompt = true })
                {
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return false;
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
                OnStateChanged();
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                ShowError("Could not save the file.", ex);
                return false;
            }
        }

        public bool ConfirmDiscard(IWin32Window owner)
        {
            if (!dirty) return true;

            DialogResult answer = MessageBox.Show(owner, "Save changes to " + DisplayName + "?", MainForm.AppName,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) return Save();
            return answer == DialogResult.No;
        }

        public void ToggleEdit()
        {
            if (editing)
            {
                Render(editor.Text);
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
            editing = !editing;
            OnStateChanged();
        }

        public void FocusContent()
        {
            if (editing) editor.Focus();
            else viewer.Focus();
        }

        /// <summary>Applies colors now; the document itself is re-rendered by the next RerenderIfWidthChanged.</summary>
        public void ApplyPalette(Palette palette)
        {
            BackColor = palette.Background;
            viewer.BackColor = palette.Background;
            viewer.DarkScrollbars = palette.IsDark;
            editor.BackColor = palette.Background;
            editor.ForeColor = palette.Text;
            editor.DarkScrollbars = palette.IsDark;
            renderer.Palette = palette;
            paletteChanged = true;
        }

        /// <summary>Re-renders the visible view if its width or the palette changed since the last render.</summary>
        public void RerenderIfWidthChanged()
        {
            if (editing || !viewer.Visible) return;
            if (!paletteChanged && viewer.ContentWidth == renderer.RenderedWidth) return;
            RerenderPreservingScroll();
        }

        /// <summary>
        /// Start positions of non-overlapping matches in the visible view, at most <paramref name="limit"/>.
        /// Positions index the view's own text, so they can be passed to <see cref="SelectRange"/>.
        /// </summary>
        public List<int> FindAll(string query, bool matchCase, int limit)
        {
            var matches = new List<int>();
            if (string.IsNullOrEmpty(query)) return matches;

            if (editing)
            {
                StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                string text = editor.Text;
                int index = text.IndexOf(query, comparison);
                while (index >= 0 && matches.Count < limit)
                {
                    matches.Add(index);
                    index = text.IndexOf(query, index + query.Length, comparison);
                }
                return matches;
            }

            // RichEdit's own search keeps positions consistent with its tables and pictures,
            // which the plain Text property does not represent one to one.
            RichTextBoxFinds options = RichTextBoxFinds.NoHighlight | (matchCase ? RichTextBoxFinds.MatchCase : 0);
            int length = viewer.TextLength;
            int start = 0;
            while (start < length && matches.Count < limit)
            {
                int found = viewer.Find(query, start, -1, options);
                if (found < 0) break;
                matches.Add(found);
                start = found + Math.Max(1, query.Length);
            }
            return matches;
        }

        /// <summary>Selects a range in the visible view and scrolls it into view.</summary>
        public void SelectRange(int start, int length)
        {
            TextBoxBase view = ActiveView;
            view.Select(start, length);
            view.ScrollToCaret();
        }

        internal static bool IsFileError(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is SecurityException
                || ex is ArgumentException
                || ex is NotSupportedException;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopWatching();
                reloadTimer.Dispose();
                renderer.Dispose();
                editor.Font.Dispose();
            }
            base.Dispose(disposing);
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

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            contentVersion++;
            if (suppressDirty || dirty) return;
            dirty = true;
            OnStateChanged();
        }

        private void OnStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // Re-reads the current file in place, keeping the active view and its position.
        private bool ReloadFromDisk(bool silent)
        {
            try
            {
                string normalized = ReadDocument(currentPath);
                string editorText = normalized.Replace("\n", "\r\n");
                if (silent && editorText == editor.Text) return true;

                int firstVisible = editing ? 0 : viewer.GetCharIndexFromPosition(new Point(1, 1));
                int caret = editor.SelectionStart;

                suppressDirty = true;
                editor.Text = editorText;
                suppressDirty = false;
                dirty = false;
                Render(normalized);

                if (editing)
                {
                    editor.Select(Math.Min(caret, editor.TextLength), 0);
                    editor.ScrollToCaret();
                }
                else
                {
                    viewer.Select(Math.Min(firstVisible, viewer.TextLength), 0);
                    viewer.ScrollToCaret();
                }
                OnStateChanged();
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

        private void Render(string markdown)
        {
            renderer.Render(markdown, currentPath == null ? null : Path.GetDirectoryName(currentPath));
            paletteChanged = false;
            contentVersion++;
        }

        private void RerenderPreservingScroll()
        {
            int firstVisible = viewer.GetCharIndexFromPosition(new Point(1, 1));
            renderer.Rerender();
            paletteChanged = false;
            contentVersion++;
            viewer.Select(firstVisible, 0);
            viewer.ScrollToCaret();
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

        private void ShowError(string message, Exception ex)
        {
            MessageBox.Show(FindForm(), message + Environment.NewLine + ex.Message, MainForm.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
