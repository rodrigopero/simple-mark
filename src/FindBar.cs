using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SimpleMark
{
    /// <summary>
    /// Find box for the active document. Searches the visible view (rendered or plain text),
    /// selects the current match and wraps around at either end.
    /// </summary>
    internal sealed class FindBar : Panel
    {
        private const int MaxMatches = 5000;
        private const int Gap = 6;

        private readonly Panel inputBorder;
        private readonly TextBox input;
        private readonly Label counter;
        private readonly Button previousButton;
        private readonly Button nextButton;
        private readonly CheckBox matchCaseBox;
        private readonly Button closeButton;
        private readonly Timer typingTimer;

        private Palette palette = Palette.Light;
        private DocumentTab target;
        private List<int> matches = new List<int>();
        private string searchedKey;

        public FindBar()
        {
            input = new TextBox { BorderStyle = BorderStyle.None };
            input.TextChanged += delegate { typingTimer.Stop(); typingTimer.Start(); };
            input.KeyDown += OnInputKeyDown;

            // A borderless box inside a 1-pixel frame, since the native border ignores the theme.
            var inputPadding = new Panel { Dock = DockStyle.Fill };
            inputPadding.Controls.Add(input);
            inputBorder = new Panel { Padding = new Padding(1) };
            inputBorder.Controls.Add(inputPadding);

            counter = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            previousButton = CreateButton("↑", "Previous match (Shift+F3)", delegate { FindPrevious(); });
            nextButton = CreateButton("↓", "Next match (F3)", delegate { FindNext(); });
            closeButton = CreateButton("×", "Close (Esc)", delegate { OnCloseRequested(); });

            matchCaseBox = new CheckBox
            {
                Text = "Aa",
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false,
            };
            matchCaseBox.FlatAppearance.BorderSize = 0;
            matchCaseBox.CheckedChanged += delegate
            {
                Search();
                input.Focus();
            };

            // Search as you type, but not on every keystroke of a fast typist in a large document.
            typingTimer = new Timer { Interval = 150 };
            typingTimer.Tick += delegate { typingTimer.Stop(); Search(); };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(previousButton, "Previous match (Shift+F3)");
            toolTip.SetToolTip(nextButton, "Next match (F3)");
            toolTip.SetToolTip(matchCaseBox, "Match case");
            toolTip.SetToolTip(closeButton, "Close (Esc)");
            Disposed += delegate { toolTip.Dispose(); };

            Dock = DockStyle.Top;
            Controls.AddRange(new Control[] { inputBorder, counter, previousButton, nextButton, matchCaseBox, closeButton });
            UpdateHeight();
        }

        public event EventHandler CloseRequested;

        /// <summary>The document searched; set when the active tab changes.</summary>
        public DocumentTab Target
        {
            get { return target; }
            set { target = value; }
        }

        public Palette Palette
        {
            set
            {
                palette = value;
                BackColor = value.Background;
                inputBorder.BackColor = value.Border;
                inputBorder.Controls[0].BackColor = value.Background;
                input.BackColor = value.Background;
                input.ForeColor = value.Text;
                counter.ForeColor = value.Muted;
                foreach (ButtonBase button in new ButtonBase[] { previousButton, nextButton, matchCaseBox, closeButton })
                {
                    button.BackColor = value.Background;
                    button.ForeColor = value.Text;
                    button.FlatAppearance.MouseOverBackColor = value.Hover;
                    button.FlatAppearance.MouseDownBackColor = value.Pressed;
                    button.FlatAppearance.CheckedBackColor = value.Pressed;
                }
                Invalidate();
            }
        }

        /// <summary>Shows the bar with the given text (if any), selected, and searches from the selection.</summary>
        public void Open(string text)
        {
            Visible = true;
            if (text != null) input.Text = text;
            typingTimer.Stop();
            Search();
            input.SelectAll();
            input.Focus();
        }

        public void FindNext()
        {
            Step(true);
        }

        public void FindPrevious()
        {
            Step(false);
        }

        /// <summary>Searches again from the current selection, e.g. after the visible content changed.</summary>
        public void Research()
        {
            Search();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateHeight();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int gap = Scale(Gap);
            int buttonSize = ClientSize.Height - gap * 2;
            int x = gap;
            inputBorder.Bounds = new Rectangle(x, gap, Scale(260), buttonSize);
            Size inner = inputBorder.Controls[0].ClientSize;
            input.Bounds = new Rectangle(Scale(4), Math.Max(0, (inner.Height - input.Height) / 2), Math.Max(1, inner.Width - Scale(8)), input.Height);
            x = inputBorder.Right + gap;
            counter.Bounds = new Rectangle(x, gap, Scale(96), buttonSize);
            x = counter.Right;
            foreach (Control button in new Control[] { previousButton, nextButton, matchCaseBox })
            {
                int width = Math.Max(buttonSize, TextRenderer.MeasureText(button.Text, button.Font).Width + Scale(12));
                button.Bounds = new Rectangle(x, gap, width, buttonSize);
                x += width + Scale(2);
            }
            closeButton.Bounds = new Rectangle(Math.Max(x, ClientSize.Width - gap - buttonSize), gap, buttonSize, buttonSize);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(palette.Border))
            {
                e.Graphics.DrawLine(pen, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Step(!e.Shift);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                OnCloseRequested();
                e.SuppressKeyPress = true;
            }
        }

        private void OnCloseRequested()
        {
            typingTimer.Stop();
            EventHandler handler = CloseRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // Selects the first match at or after the selection, so typing more letters keeps the current match.
        private void Search()
        {
            if (!UpdateMatches(true)) return;
            int start = target.SelectionStart;
            int index = matches.FindIndex(delegate(int match) { return match >= start; });
            ShowMatch(index < 0 ? 0 : index);
        }

        private void Step(bool forward)
        {
            if (target == null || input.TextLength == 0) return;
            if (!UpdateMatches(false)) return;

            int start = target.SelectionStart;
            int index = forward
                ? matches.FindIndex(delegate(int match) { return match > start; })
                : matches.FindLastIndex(delegate(int match) { return match < start; });
            if (index < 0) index = forward ? 0 : matches.Count - 1;
            ShowMatch(index);
        }

        // Recomputes matches when the query or the content changed; false when there is nothing to select.
        private bool UpdateMatches(bool always)
        {
            if (target == null || input.TextLength == 0)
            {
                matches.Clear();
                searchedKey = null;
                counter.Text = string.Empty;
                return false;
            }

            string key = input.Text + "\0" + matchCaseBox.Checked + "\0" + target.IsEditing + "\0" + target.ContentVersion;
            if (always || key != searchedKey)
            {
                matches = target.FindAll(input.Text, matchCaseBox.Checked, MaxMatches);
                searchedKey = key;
            }
            if (matches.Count > 0) return true;
            counter.Text = "No results";
            return false;
        }

        private void ShowMatch(int index)
        {
            target.SelectRange(matches[index], input.TextLength);
            counter.Text = (index + 1) + " of " + matches.Count + (matches.Count >= MaxMatches ? "+" : string.Empty);
        }

        private Button CreateButton(string text, string description, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                AccessibleName = description,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += click;
            button.Click += delegate { if (Visible) input.Focus(); };
            return button;
        }

        private void UpdateHeight()
        {
            Height = Font.Height + Scale(10) + Scale(Gap) * 2;
        }

        private int Scale(int value)
        {
            return value * DeviceDpi / 96;
        }
    }
}
