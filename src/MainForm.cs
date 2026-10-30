using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace MdViewer
{
    internal sealed class MainForm : Form
    {
        private const long MaxFileBytes = 5L * 1024 * 1024;
        private const string AppName = "MD Viewer";
        private const string FileFilter =
            "Markdown (*.md;*.markdown)|*.md;*.markdown|Text (*.txt)|*.txt|All files (*.*)|*.*";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly RichTextBox viewer;
        private readonly TextBox editor;
        private readonly ToolStripMenuItem editItem;
        private readonly MarkdownRenderer renderer;
        private readonly string initialPath;

        private string currentPath;
        private bool dirty;
        private bool suppressDirty;
        private bool hasBom;
        private bool useLf;

        public MainForm(string initialPath)
        {
            this.initialPath = initialPath;

            Text = AppName;
            ClientSize = new Size(900, 700);
            StartPosition = FormStartPosition.CenterScreen;

            viewer = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                EnableAutoDragDrop = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };

            editor = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsTab = true,
                AcceptsReturn = true,
                MaxLength = 0,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
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
            viewMenu.DropDownItems.Add(editItem);

            var menu = new MenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu });

            Controls.Add(viewer);
            Controls.Add(editor);
            Controls.Add(menu);
            MainMenuStrip = menu;

            renderer = new MarkdownRenderer(viewer);
            UpdateTitle();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!string.IsNullOrEmpty(initialPath)) OpenFile(initialPath);
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
                renderer.Render(editor.Text);
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
                byte[] bytes = ReadBounded(fullPath);

                hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                int offset = hasBom ? 3 : 0;
                string text = Utf8NoBom.GetString(bytes, offset, bytes.Length - offset);

                useLf = text.IndexOf('\n') >= 0 && text.IndexOf("\r\n", StringComparison.Ordinal) < 0;
                string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

                suppressDirty = true;
                editor.Text = normalized.Replace("\n", "\r\n");
                suppressDirty = false;

                renderer.Render(normalized);
                currentPath = fullPath;
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

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxFileBytes)
                {
                    throw new IOException("The file exceeds the " + (MaxFileBytes / (1024 * 1024)) + " MB limit.");
                }

                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }

                if (read == buffer.Length) return buffer;
                var trimmed = new byte[read];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, read);
                return trimmed;
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

                WriteAtomic(path, text, hasBom ? new UTF8Encoding(true) : Utf8NoBom);
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

        // Writes to a sibling temp file first so a failure never leaves a truncated document.
        private static void WriteAtomic(string path, string text, Encoding encoding)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            string tempPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                File.WriteAllText(tempPath, text, encoding);
                if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null);
                else File.Move(tempPath, fullPath);
            }
            catch
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
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
