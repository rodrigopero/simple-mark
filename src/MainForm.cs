using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SimpleMark
{
    internal sealed class MainForm : Form
    {
        public const string AppName = "Simple Mark";

        private readonly Icon appIcon;
        private readonly ToolStripMenuItem saveItem;
        private readonly ToolStripMenuItem reloadItem;
        private readonly ToolStripMenuItem closeTabItem;
        private readonly ToolStripMenuItem editItem;
        private readonly ToolStripMenuItem findItem;
        private readonly ToolStripMenuItem findNextItem;
        private readonly ToolStripMenuItem findPreviousItem;
        private readonly ToolStripMenuItem darkItem;
        private readonly MenuStrip menu;
        private readonly TabStrip tabStrip;
        private readonly Panel divider;
        private readonly FindBar findBar;
        private readonly Panel documentHost;
        private readonly StartScreen startScreen;
        private readonly string[] initialPaths;

        private DocumentTab activeTab;
        private FormWindowState lastWindowState;
        private FormWindowState restoreState = FormWindowState.Normal;
        private Palette palette = Palette.FromSystem();

        public MainForm(string[] initialPaths)
        {
            this.initialPaths = initialPaths ?? new string[0];

            Text = AppName;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(1000, 760);
            MinimumSize = new Size(480, 360);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("SimpleMark.app.ico"))
            {
                if (iconStream != null)
                {
                    appIcon = new Icon(iconStream);
                    Icon = appIcon;
                }
            }

            var newTabItem = new ToolStripMenuItem("New &tab", null, delegate { AddTab(); }) { ShortcutKeys = Keys.Control | Keys.T };
            var openItem = new ToolStripMenuItem("&Open...", null, delegate { ShowOpenDialog(); }) { ShortcutKeys = Keys.Control | Keys.O };
            saveItem = new ToolStripMenuItem("&Save", null, delegate { activeTab.Save(); }) { ShortcutKeys = Keys.Control | Keys.S };
            reloadItem = new ToolStripMenuItem("&Reload", null, delegate { activeTab.Reload(); }) { ShortcutKeys = Keys.F5 };
            closeTabItem = new ToolStripMenuItem("&Close tab", null, delegate { CloseTab(activeTab); }) { ShortcutKeys = Keys.Control | Keys.W };
            var exitItem = new ToolStripMenuItem("E&xit", null, delegate { Close(); });
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                newTabItem, openItem, saveItem, reloadItem, new ToolStripSeparator(), closeTabItem, exitItem,
            });

            editItem = new ToolStripMenuItem("&Edit plain text", null, OnToggleEdit) { ShortcutKeys = Keys.Control | Keys.E };
            findItem = new ToolStripMenuItem("&Find...", null, delegate { ShowFind(); }) { ShortcutKeys = Keys.Control | Keys.F };
            findNextItem = new ToolStripMenuItem("Find &next", null, delegate { FindAgain(true); }) { ShortcutKeys = Keys.F3 };
            findPreviousItem = new ToolStripMenuItem("Find &previous", null, delegate { FindAgain(false); }) { ShortcutKeys = Keys.Shift | Keys.F3 };
            darkItem = new ToolStripMenuItem("&Dark mode", null, OnToggleDark) { ShortcutKeys = Keys.Control | Keys.D };
            var viewMenu = new ToolStripMenuItem("&View");
            viewMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                editItem, new ToolStripSeparator(), findItem, findNextItem, findPreviousItem, new ToolStripSeparator(), darkItem,
            });

            menu = new MenuStrip { Padding = new Padding(8, 4, 0, 4) };
            menu.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu });

            tabStrip = new TabStrip { Visible = false };
            tabStrip.SelectedTabChanged += OnSelectedTabChanged;
            tabStrip.CloseRequested += delegate(object sender, TabEventArgs e) { CloseTab(e.Tab); };

            // Thin brand-colored line between the tabs and the document.
            divider = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Palette.Brand };

            findBar = new FindBar { Visible = false };
            findBar.CloseRequested += delegate { HideFind(); };

            startScreen = new StartScreen(appIcon) { Dock = DockStyle.Fill };
            startScreen.OpenRequested += delegate { ShowOpenDialog(); };
            startScreen.NewRequested += delegate { AddTab(); };

            // Documents live in their own host so adding tabs later does not disturb the docking order.
            documentHost = new Panel { Dock = DockStyle.Fill };
            documentHost.Controls.Add(startScreen);

            // Fill controls first: WinForms docks the last added control first.
            Controls.Add(documentHost);
            Controls.Add(findBar);
            Controls.Add(divider);
            Controls.Add(tabStrip);
            Controls.Add(menu);
            MainMenuStrip = menu;

            DragEnter += OnFileDragOver;
            DragOver += OnFileDragOver;
            DragDrop += OnFileDrop;

            ApplyTheme();
            UpdateActiveState();
        }

        /// <summary>Opens files sent by a second launch; safe to call from any thread.</summary>
        public void OpenFromAnotherInstance(string[] paths)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (WindowState == FormWindowState.Minimized) WindowState = restoreState;
                    OpenPaths(paths);
                    Activate();
                });
            }
            catch (InvalidOperationException)
            {
                // The window is closing; there is nothing left to open the files in.
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.ApplyToWindow(this, palette);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OpenPaths(initialPaths);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (activeTab != null) activeTab.RerenderIfWidthChanged();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (WindowState == lastWindowState) return;
            lastWindowState = WindowState;
            if (WindowState == FormWindowState.Minimized) return;
            restoreState = WindowState;
            if (activeTab != null) activeTab.RerenderIfWidthChanged();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            foreach (DocumentTab tab in new List<DocumentTab>(tabStrip.Tabs))
            {
                if (!tab.IsDirty) continue;
                tabStrip.SelectedTab = tab;
                if (tab.ConfirmDiscard(this)) continue;
                e.Cancel = true;
                break;
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                tabStrip.SelectNext((keyData & Keys.Shift) == 0 ? 1 : -1);
                return true;
            }
            if (keyData == Keys.Escape && findBar.Visible)
            {
                HideFind();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && appIcon != null) appIcon.Dispose();
            base.Dispose(disposing);
        }

        private void ShowOpenDialog()
        {
            using (var dialog = new OpenFileDialog { Filter = DocumentTab.FileFilter, CheckFileExists = true, Multiselect = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenPaths(dialog.FileNames);
            }
        }

        private void OnToggleEdit(object sender, EventArgs e)
        {
            activeTab.ToggleEdit();
            if (findBar.Visible) findBar.Research();
        }

        private void OnToggleDark(object sender, EventArgs e)
        {
            palette = palette.IsDark ? Palette.Light : Palette.Dark;
            ApplyTheme();
            if (activeTab != null) activeTab.RerenderIfWidthChanged();
        }

        private void ShowFind()
        {
            findBar.Target = activeTab;
            findBar.Open(activeTab.SelectedLine);
        }

        private void FindAgain(bool forward)
        {
            if (!findBar.Visible) ShowFind();
            if (forward) findBar.FindNext();
            else findBar.FindPrevious();
        }

        private void HideFind()
        {
            findBar.Visible = false;
            if (activeTab != null) activeTab.FocusContent();
        }

        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            bool files = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            var files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;

            // Open after the drop completes so the drag source (e.g. Explorer) is not blocked by dialogs.
            BeginInvoke((MethodInvoker)delegate
            {
                OpenPaths(files);
                Activate();
            });
        }

        private void OnSelectedTabChanged(object sender, EventArgs e)
        {
            DocumentTab previous = activeTab;
            activeTab = tabStrip.SelectedTab;

            Control shown = activeTab ?? (Control)startScreen;
            shown.Visible = true;
            shown.BringToFront();
            if (previous != null && previous != activeTab) previous.Visible = false;
            startScreen.Visible = activeTab == null;

            findBar.Target = activeTab;
            if (activeTab == null)
            {
                findBar.Visible = false;
            }
            else
            {
                activeTab.RerenderIfWidthChanged();
                activeTab.FocusContent();
                if (findBar.Visible) findBar.Research();
            }
            UpdateActiveState();
        }

        private void OnTabStateChanged(object sender, EventArgs e)
        {
            tabStrip.RefreshTabs();
            if (sender == activeTab) UpdateActiveState();
        }

        private void ApplyTheme()
        {
            BackColor = palette.Background;
            documentHost.BackColor = palette.Background;
            menu.Renderer = Theme.CreateMenuRenderer(palette);
            menu.BackColor = palette.Background;
            menu.ForeColor = palette.Text;
            tabStrip.Palette = palette;
            findBar.Palette = palette;
            startScreen.Palette = palette;
            darkItem.Checked = palette.IsDark;
            foreach (DocumentTab tab in tabStrip.Tabs) tab.ApplyPalette(palette);
            WindowChrome.ApplyToWindow(this, palette);
        }

        private DocumentTab AddTab()
        {
            var tab = new DocumentTab(palette) { Dock = DockStyle.Fill, Visible = false };
            tab.StateChanged += OnTabStateChanged;
            documentHost.Controls.Add(tab);
            tabStrip.Add(tab);
            tabStrip.SelectedTab = tab;
            return tab;
        }

        private void OpenPaths(IEnumerable<string> paths)
        {
            foreach (string path in paths) OpenPath(path);
        }

        // Activates the tab already showing the file, else opens it in an empty Untitled active tab or a new one.
        private void OpenPath(string path)
        {
            DocumentTab existing = FindTab(path);
            if (existing != null)
            {
                tabStrip.SelectedTab = existing;
                return;
            }

            DocumentTab previous = activeTab;
            bool reuse = previous != null && previous.IsPristine;
            DocumentTab target = reuse ? previous : AddTab();
            if (target.Open(path) || reuse) return;

            RemoveTab(target);
            if (previous != null) tabStrip.SelectedTab = previous;
        }

        private DocumentTab FindTab(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                if (!DocumentTab.IsFileError(ex)) throw;
                return null;
            }

            foreach (DocumentTab tab in tabStrip.Tabs)
            {
                if (string.Equals(tab.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) return tab;
            }
            return null;
        }

        private void CloseTab(DocumentTab tab)
        {
            if (tab.IsDirty)
            {
                tabStrip.SelectedTab = tab;
                if (!tab.ConfirmDiscard(this)) return;
            }
            RemoveTab(tab);
        }

        private void RemoveTab(DocumentTab tab)
        {
            tabStrip.Remove(tab);
            documentHost.Controls.Remove(tab);
            tab.Dispose();
        }

        // Menu items that need a document are disabled (and their shortcuts inert) while none is open.
        private void UpdateActiveState()
        {
            bool hasTab = activeTab != null;
            tabStrip.Visible = hasTab;
            foreach (ToolStripMenuItem item in new[] { saveItem, reloadItem, closeTabItem, editItem, findItem, findNextItem, findPreviousItem })
            {
                item.Enabled = hasTab;
            }
            editItem.Checked = hasTab && activeTab.IsEditing;
            Text = hasTab
                ? (activeTab.IsDirty ? "*" : string.Empty) + activeTab.DisplayName + " - " + AppName
                : AppName;
        }
    }
}
