using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileNameNormalizer
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(17, 19, 22);
        public static readonly Color Surface = Color.FromArgb(24, 27, 31);
        public static readonly Color SurfaceHover = Color.FromArgb(34, 38, 43);
        public static readonly Color Border = Color.FromArgb(48, 53, 60);
        public static readonly Color Text = Color.FromArgb(231, 234, 238);
        public static readonly Color Muted = Color.FromArgb(148, 157, 168);
        public static readonly Color Accent = Color.FromArgb(133, 199, 177);
        public static readonly Color ButtonSurface = Color.FromArgb(31, 42, 38);
        public static readonly Color ButtonBorder = Color.FromArgb(91, 135, 120);
        public static readonly Color ButtonHover = Color.FromArgb(40, 58, 50);
        public static readonly Color ButtonPressed = Color.FromArgb(28, 43, 36);
        public static readonly Color PrimaryHover = Color.FromArgb(160, 220, 199);
        public static readonly Color PrimaryPressed = Color.FromArgb(105, 172, 149);
        public static readonly Color DisabledButton = Color.FromArgb(22, 25, 29);
        public static readonly Color DisabledButtonBorder = Color.FromArgb(43, 48, 55);
        public static readonly Color DisabledButtonText = Color.FromArgb(105, 114, 125);
        public static readonly Color Warning = Color.FromArgb(221, 181, 116);
        public static readonly Color Error = Color.FromArgb(231, 137, 142);
        public static readonly Font Normal = new Font("맑은 고딕", 10F);

        public static Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, Font = new Font("맑은 고딕", size), ForeColor = color,
                AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
        }
    }

    internal sealed class ActionButton : Button
    {
        private readonly bool primary;
        private bool mouseOver, mousePressed, keyboardPressed;
        private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        public ActionButton(string text, bool isPrimary)
        {
            primary = isPrimary;
            Text = text;
            Font = new Font("맑은 고딕", 9F, isPrimary ? FontStyle.Bold : FontStyle.Regular);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.MouseOverBackColor = isPrimary ? Theme.PrimaryHover : Theme.ButtonHover;
            FlatAppearance.MouseDownBackColor = isPrimary ? Theme.PrimaryPressed : Theme.ButtonPressed;
            Size = new Size(136, 40);
            Margin = new Padding(0, 0, 10, 0);
            UseVisualStyleBackColor = false;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoSize = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateAppearance();
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            // Measure the full caption, independent of the column's current width.
            // The same flags and padding are used when painting below.
            Size textSize = TextRenderer.MeasureText(Text ?? "", Font,
                new Size(Int32.MaxValue, Int32.MaxValue), TextFlags);
            float scale = DeviceDpi / 96F;
            int horizontalPadding = (int)Math.Ceiling(16 * scale);
            int verticalPadding = (int)Math.Ceiling(6 * scale);
            return new Size(Math.Max(MinimumSize.Width, textSize.Width + horizontalPadding * 2 + 2),
                Math.Max(MinimumSize.Height, textSize.Height + verticalPadding * 2 + 2));
        }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Parent != null) Parent.PerformLayout(this, "PreferredSize");
            Invalidate();
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (Parent != null) Parent.PerformLayout(this, "PreferredSize");
            Invalidate();
        }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            mousePressed = keyboardPressed = false;
            mouseOver = Enabled && IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));
            UpdateAppearance();
        }
        private void UpdateAppearance()
        {
            BackColor = Enabled ? (primary ? Theme.Accent : Theme.ButtonSurface) : Theme.DisabledButton;
            ForeColor = Enabled ? (primary ? Theme.Background : Theme.Accent) : Theme.DisabledButtonText;
            FlatAppearance.BorderColor = Enabled ? (primary ? Theme.Accent : Theme.ButtonBorder) : Theme.DisabledButtonBorder;
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            mouseOver = true;
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            mouseOver = false;
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left) mousePressed = true;
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) mousePressed = false;
            base.OnMouseUp(e);
            Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) mousePressed = false;
            Invalidate();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && e.KeyCode == Keys.Space) keyboardPressed = true;
            base.OnKeyDown(e);
            Invalidate();
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) keyboardPressed = false;
            base.OnKeyUp(e);
            Invalidate();
        }
        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }
        protected override void OnLostFocus(EventArgs e)
        {
            keyboardPressed = false;
            base.OnLostFocus(e);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = BackColor;
            Color border = FlatAppearance.BorderColor;
            if (Enabled)
            {
                if (keyboardPressed || (mousePressed && mouseOver))
                    fill = primary ? Theme.PrimaryPressed : Theme.ButtonPressed;
                else if (mouseOver)
                    fill = primary ? Theme.PrimaryHover : Theme.ButtonHover;
                if (primary) border = fill;
                else if (mouseOver || Focused) border = Theme.Accent;
            }
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (Width > 0 && Height > 0)
                using (Pen pen = new Pen(border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            float scale = DeviceDpi / 96F;
            int inset = (int)Math.Ceiling(16 * scale) + 1;
            Rectangle textBounds = new Rectangle(inset, 1, Math.Max(0, Width - inset * 2), Math.Max(0, Height - 2));
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor,
                TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Enabled && Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, fill);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly List<string> roots = new List<string>();
        private readonly string historyDirectory;
        private readonly string legacyHistoryDirectory;
        private List<RenameEntry> entries = new List<RenameEntry>();
        private DragFeedbackGrid grid;
        private CheckBox changesOnly, selectAll, gatherToRoot;
        private ActionButton clearButton, refreshButton, applyButton, undoButton;
        private Label folderLabel, summaryLabel, statusLabel, selectionLabel;
        private ProgressBar progress;
        private Panel contentViewport;
        private TableLayoutPanel contentLayout;
        private Size minimumContentSize;
        private bool sizingContent, layoutReady;
        private bool busy, binding;
        private List<string> scanWarnings = new List<string>();
        private List<string> cleanupWarnings = new List<string>();
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 350 };
        private readonly System.Windows.Forms.Timer dragFeedbackTimer = new System.Windows.Forms.Timer { Interval = 50 };
        private readonly Stopwatch dragFeedbackClock = new Stopwatch();
        private string[] dragPaths = new string[0];
        private int dragFolderCount;
        private bool dragLeavePending, dragUsesMouseButtons;
        private long dragLeaveAt;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public MainForm()
        {
            SuspendLayout();
            historyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileNameNormalizer", "history");
            legacyHistoryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
            Text = "파일명 정리";
            Font = Theme.Normal;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            ClientSize = new Size(1140, 760);
            MinimumSize = new Size(940, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Padding = new Padding(20, 16, 20, 16);
            try
            {
                using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("FileNameNormalizer.AppIcon"))
                {
                    if (iconStream != null)
                    {
                        using (Icon embeddedIcon = new Icon(iconStream, SystemInformation.IconSize))
                            Icon = (Icon)embeddedIcon.Clone();
                    }
                    else Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch { }
            BuildLayout();
            EnableFolderDrop(this);
            dragFeedbackTimer.Tick += delegate { AdvanceDragFeedback(); };
            Deactivate += delegate { EndDragFeedback(); };
            Shown += delegate { UpdateActions(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                EndDragFeedback();
                if (busy)
                {
                    e.Cancel = true;
                    SetStatus("진행 중인 작업이 끝나면 창을 닫을 수 있습니다.", Theme.Warning);
                }
            };
            // All dimensions above are authored at 96 DPI. Scale only after the full
            // control tree exists, so point-sized fonts and pixel-sized cells agree.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformAutoScale();
            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dragFeedbackTimer.Stop();
                dragFeedbackTimer.Dispose();
                dragFeedbackClock.Stop();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float scale;
            using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
            minimumContentSize = new Size((int)Math.Ceiling(880 * scale), (int)Math.Ceiling(500 * scale));
            grid.ColumnHeadersHeight = (int)Math.Ceiling(36 * scale);
            grid.RowTemplate.Height = (int)Math.Ceiling(34 * scale);
            int cellPadding = (int)Math.Ceiling(10 * scale);
            grid.DefaultCellStyle.Padding = new Padding(cellPadding, 0, cellPadding, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(cellPadding, 0, cellPadding, 0);
            grid.Columns[0].Width = (int)Math.Ceiling(52 * scale);
            grid.Columns[1].MinimumWidth = (int)Math.Ceiling(90 * scale);
            grid.Columns[1].Width = (int)Math.Ceiling(104 * scale);
            for (int i = 2; i < grid.Columns.Count; i++)
                grid.Columns[i].MinimumWidth = (int)Math.Ceiling(130 * scale);

            Rectangle work = Screen.FromControl(this).WorkingArea;
            int margin = Math.Max(8, (int)Math.Ceiling(12 * scale));
            int availableWidth = Math.Max(1, work.Width - margin * 2);
            int availableHeight = Math.Max(1, work.Height - margin * 2);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, availableWidth),
                Math.Min(MinimumSize.Height, availableHeight));
            Size = new Size(Math.Min(Width, availableWidth), Math.Min(Height, availableHeight));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            layoutReady = true;
            SizeContent();
        }

        private void SizeContent()
        {
            if (!layoutReady || sizingContent) return;
            sizingContent = true;
            try
            {
                // Keep a useful preview and expose scroll bars when a small/high-DPI
                // screen cannot contain every row. No footer controls fall off-screen.
                Size viewport = contentViewport.ClientSize;
                int width = Math.Max(viewport.Width, minimumContentSize.Width);
                int height = Math.Max(viewport.Height, minimumContentSize.Height);
                if (height > viewport.Height) width = Math.Max(minimumContentSize.Width,
                    viewport.Width - SystemInformation.VerticalScrollBarWidth);
                if (width > viewport.Width) height = Math.Max(minimumContentSize.Height,
                    viewport.Height - SystemInformation.HorizontalScrollBarHeight);
                contentLayout.Size = new Size(width, height);
            }
            finally { sizingContent = false; }
        }

        private void BuildLayout()
        {
            contentViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
            Controls.Add(contentViewport);
            TableLayoutPanel layout = new TableLayoutPanel { Location = Point.Empty, ColumnCount = 1,
                RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
            contentLayout = layout;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new int[] { 46, 38 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles[0].SizeType = SizeType.AutoSize;
            contentViewport.Controls.Add(layout);
            contentViewport.Resize += delegate { SizeContent(); };

            TableLayoutPanel folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 6) };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderLabel = Theme.Label("아직 추가한 폴더가 없습니다", 9, Theme.Muted);
            folderLabel.AutoEllipsis = true;
            refreshButton = new ActionButton("다시 읽기", false) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            clearButton = new ActionButton("목록 비우기", false) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            refreshButton.Click += async delegate { await Scan(); };
            clearButton.Click += delegate
            {
                roots.Clear(); entries.Clear(); scanWarnings.Clear(); cleanupWarnings.Clear(); BindRows(); UpdateFolders();
                SetStatus("폴더를 추가하면 변경할 이름을 미리 볼 수 있습니다.", Theme.Muted);
            };
            folderRow.Controls.Add(folderLabel, 0, 0); folderRow.Controls.Add(refreshButton, 1, 0);
            folderRow.Controls.Add(clearButton, 2, 0); layout.Controls.Add(folderRow, 0, 0);

            TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = Padding.Empty };
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selectAll = MakeCheckBox("변경 가능 전체 선택", true);
            changesOnly = MakeCheckBox("변경할 파일만 보기", false);
            gatherToRoot = MakeCheckBox("하위 폴더 파일 모으기", false);
            selectAll.CheckedChanged += delegate
            {
                if (binding || busy) return;
                foreach (RenameEntry entry in entries) if (entry.Status == EntryStatus.Ready) entry.Selected = selectAll.Checked;
                BindRows();
            };
            changesOnly.CheckedChanged += delegate { BindRows(); };
            gatherToRoot.CheckedChanged += async delegate { if (roots.Count > 0) await Scan(); };
            toolTip.SetToolTip(gatherToRoot,
                "켜면 선택한 파일을 이름 변경과 함께 추가한 폴더로 옮깁니다.\n이동 후 비어 있는 원래 하위 폴더는 삭제하며, 파일이 남아 있으면 유지합니다.");
            summaryLabel = Theme.Label("전체 0개", 9, Theme.Muted);
            summaryLabel.AutoEllipsis = true;
            summaryLabel.TextAlign = ContentAlignment.MiddleRight;
            options.Controls.Add(selectAll, 0, 0);
            options.Controls.Add(changesOnly, 1, 0); options.Controls.Add(gatherToRoot, 2, 0);
            options.Controls.Add(summaryLabel, 3, 0);
            layout.Controls.Add(options, 0, 1);

            BuildGrid(); layout.Controls.Add(grid, 0, 2);
            statusLabel = Theme.Label("폴더를 추가하면 변경할 이름을 미리 볼 수 있습니다.", 9, Theme.Muted);
            statusLabel.AutoEllipsis = true;
            statusLabel.Click += delegate
            {
                if (busy) return;
                if (cleanupWarnings.Count > 0)
                    MessageBox.Show(this, string.Join("\n", cleanupWarnings.Take(30)), "폴더 정리 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else if (scanWarnings.Count > 0)
                    MessageBox.Show(this, string.Join("\n", scanWarnings.Take(30)), "읽지 못한 항목", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            layout.Controls.Add(statusLabel, 0, 3);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 4) };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Panel note = new Panel { Dock = DockStyle.Fill, Height = 24, Margin = Padding.Empty };
            selectionLabel = Theme.Label("선택 0개", 9, Theme.Muted);
            progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 3,
                Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false };
            note.Controls.Add(selectionLabel); note.Controls.Add(progress);
            undoButton = new ActionButton("되돌리기", false) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            applyButton = new ActionButton("선택한 파일 변경", true) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            undoButton.Click += async delegate { await Undo(); };
            applyButton.Click += async delegate { await Apply(); };
            toolTip.SetToolTip(undoButton, "마지막 작업의 이름과 위치를 되돌립니다. 다른 파일이 생겼거나 내용이 바뀌었으면 덮어쓰지 않습니다.");
            footer.Controls.Add(note, 0, 0);
            footer.Controls.Add(undoButton, 1, 0); footer.Controls.Add(applyButton, 2, 0);
            layout.Controls.Add(footer, 0, 4);
        }

        private CheckBox MakeCheckBox(string text, bool value)
        {
            return new CheckBox { Text = text, Checked = value, ForeColor = Theme.Muted,
                Font = new Font("맑은 고딕", 9F), Anchor = AnchorStyles.Left, AutoSize = true,
                Cursor = Cursors.Hand, Margin = new Padding(0, 0, 18, 0) };
        }

        private void BuildGrid()
        {
            grid = new DragFeedbackGrid { Dock = DockStyle.Fill, BackgroundColor = Theme.Surface,
                BorderStyle = BorderStyle.None, GridColor = Theme.Border, RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                MultiSelect = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 36, RowTemplate = { Height = 34 }, Margin = Padding.Empty,
                ShowCellErrors = false, ShowRowErrors = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Text,
                SelectionBackColor = Color.FromArgb(39, 50, 47), SelectionForeColor = Theme.Text,
                Font = new Font("맑은 고딕", 10F), Padding = new Padding(10, 0, 10, 0), NullValue = "" };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(27, 30, 34);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 34, 39),
                ForeColor = Theme.Muted, Font = new Font("맑은 고딕", 9F), Padding = new Padding(10, 0, 10, 0),
                SelectionBackColor = Color.FromArgb(30, 34, 39) };
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "선택", Width = 52,
                Resizable = DataGridViewTriState.False, SortMode = DataGridViewColumnSortMode.NotSortable });
            AddColumn("Status", "상태", 104, 0);
            AddColumn("Original", "현재 파일명", 250, 38);
            AddColumn("New", "바뀔 파일명", 230, 34);
            AddColumn("Folder", "폴더 / 안내", 200, 28);
            grid.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (grid.FeedbackActive || grid.Rows.Count > 0) return;
                Rectangle area = grid.ClientRectangle;
                area.Y += grid.ColumnHeadersHeight;
                area.Height = Math.Max(0, area.Height - grid.ColumnHeadersHeight);
                area.Inflate(-16, 0);
                string hint = busy ? "파일을 불러오는 중입니다…" : roots.Count == 0
                    ? "폴더를 끌어다 놓으면 파일 목록이 여기에 표시됩니다."
                    : changesOnly.Checked ? "표시할 변경 대상이 없습니다." : "표시할 파일이 없습니다.";
                if (area.Width > 0 && area.Height > 0)
                    TextRenderer.DrawText(e.Graphics, hint, Theme.Normal, area, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (binding || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                RenameEntry entry = grid.Rows[e.RowIndex].Tag as RenameEntry;
                if (entry != null && entry.Status == EntryStatus.Ready)
                {
                    entry.Selected = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);
                    UpdateActions();
                }
            };
            grid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e)
            {
                RenameEntry entry = grid.Rows[e.RowIndex].Tag as RenameEntry;
                if (busy || e.ColumnIndex != 0 || entry == null || entry.Status != EntryStatus.Ready) e.Cancel = true;
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
                RenameEntry entry = grid.Rows[e.RowIndex].Tag as RenameEntry;
                if (entry == null) return;
                string path = File.Exists(entry.TargetPath) && entry.Status == EntryStatus.Done ? entry.TargetPath : entry.SourcePath;
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).SetValue(grid, true, null);
        }

        private void AddColumn(string name, string title, int width, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width,
                ReadOnly = true, MinimumWidth = name == "Status" ? 90 : 130,
                AutoSizeMode = weight == 0 ? DataGridViewAutoSizeColumnMode.None : DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight == 0 ? 1 : weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        private void EnableFolderDrop(Control target)
        {
            target.AllowDrop = true;
            target.DragEnter += delegate(object sender, DragEventArgs e)
            {
                string[] paths;
                if (busy || !TryReadDropPaths(e.Data, out paths))
                {
                    e.Effect = DragDropEffects.None;
                    EndDragFeedback();
                    return;
                }
                // Keep validation across child-control transitions; do not perform
                // filesystem queries on every DragOver or animation frame.
                if (!paths.SequenceEqual(dragPaths, StringComparer.OrdinalIgnoreCase))
                {
                    dragPaths = paths;
                    dragFolderCount = paths.Where(x => Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                }
                UpdateDragFeedback(e);
            };
            target.DragOver += delegate(object sender, DragEventArgs e) { UpdateDragFeedback(e); };
            target.DragLeave += delegate
            {
                if (!grid.FeedbackActive) return;
                // A new Enter/Over cancels this grace period when moving between
                // child windows. A true leave/cancel clears even with a stationary cursor.
                dragLeavePending = true;
                dragLeaveAt = dragFeedbackClock.ElapsedMilliseconds;
            };
            target.DragDrop += async delegate(object sender, DragEventArgs e)
            {
                string[] paths;
                bool readable = TryReadDropPaths(e.Data, out paths);
                EndDragFeedback();
                if (busy || !readable) return;
                await AddFolders(paths);
            };
            foreach (Control child in target.Controls) EnableFolderDrop(child);
        }

        private static bool TryReadDropPaths(IDataObject data, out string[] paths)
        {
            paths = new string[0];
            try
            {
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
                paths = data.GetData(DataFormats.FileDrop) as string[] ?? new string[0];
                return paths.Length > 0;
            }
            catch { return false; }
        }

        private void UpdateDragFeedback(DragEventArgs e)
        {
            if (busy || dragFolderCount == 0)
            {
                e.Effect = DragDropEffects.None;
                EndDragFeedback();
                return;
            }
            // Adding a folder only imports its path; never report a source-folder move.
            e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (e.Effect == DragDropEffects.None) { EndDragFeedback(); return; }
            dragLeavePending = false;
            dragUsesMouseButtons = dragUsesMouseButtons || (e.KeyState & (1 | 2 | 16)) != 0;
            if (!grid.FeedbackActive) dragFeedbackClock.Restart();
            grid.ShowDropFeedback(dragFolderCount);
            if (!dragFeedbackTimer.Enabled) dragFeedbackTimer.Start();
        }

        private void AdvanceDragFeedback()
        {
            if (IsDisposed || Disposing || grid == null || grid.IsDisposed) return;
            bool mouseDown = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0
                || (GetAsyncKeyState(4) & 0x8000) != 0 || (GetAsyncKeyState(5) & 0x8000) != 0
                || (GetAsyncKeyState(6) & 0x8000) != 0;
            if (busy || !grid.FeedbackActive || !ClientRectangle.Contains(PointToClient(Control.MousePosition))
                || (GetAsyncKeyState(27) & 0x8000) != 0 || (dragUsesMouseButtons && !mouseDown)
                || (dragLeavePending && dragFeedbackClock.ElapsedMilliseconds - dragLeaveAt >= 100))
            {
                EndDragFeedback();
                return;
            }
            grid.AdvanceDropFeedback(dragFeedbackClock.Elapsed.TotalSeconds);
        }

        private void EndDragFeedback()
        {
            dragFeedbackTimer.Stop();
            dragFeedbackClock.Reset();
            dragLeavePending = false;
            dragUsesMouseButtons = false;
            dragFolderCount = 0;
            dragPaths = new string[0];
            if (grid != null && !grid.IsDisposed) grid.HideDropFeedback();
        }

        private async Task AddFolders(IEnumerable<string> paths)
        {
            if (busy) return;
            int rejected = 0;
            foreach (string path in paths)
            {
                try
                {
                    string full = Path.GetFullPath(path);
                    if (!Directory.Exists(full)) { rejected++; continue; }
                    if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
                }
                catch { rejected++; }
            }
            UpdateFolders();
            if (roots.Count > 0) await Scan();
            if (rejected > 0) SetStatus("파일은 직접 추가할 수 없습니다. 파일이 들어 있는 폴더를 추가하세요.", Theme.Warning);
        }

        private void UpdateFolders()
        {
            folderLabel.Text = roots.Count == 0 ? "아직 추가한 폴더가 없습니다" :
                (roots.Count == 1 ? roots[0] : string.Format("폴더 {0}개   ·   {1}", roots.Count, roots[0]));
            toolTip.SetToolTip(folderLabel, string.Join("\n", roots));
        }

        private async Task Scan()
        {
            if (busy || roots.Count == 0) return;
            SetBusy(true, "폴더에서 파일을 읽는 중입니다…");
            try
            {
                string[] paths = roots.ToArray(); bool collectFiles = gatherToRoot.Checked;
                ScanResult result = await Task.Run(delegate { return RenameEngine.Scan(paths, true, collectFiles); });
                entries = result.Entries; scanWarnings = result.Warnings;
                foreach (RenameEntry entry in entries) entry.Selected = entry.Status == EntryStatus.Ready;
                BindRows();
                int ready = entries.Count(x => x.Status == EntryStatus.Ready);
                string message = entries.Count == 0 ? "폴더에 파일이 없습니다." :
                    string.Format("미리보기 완료 · 변경 가능 {0:N0}개. 이름과 도착 위치를 확인한 후 아래 변경 버튼을 누르세요.", ready);
                int numbered = entries.Count(x => x.Status == EntryStatus.Ready && x.CollisionNumbered);
                if (numbered > 0) message += string.Format("  중복 이름 {0:N0}개는 번호를 붙입니다.", numbered);
                int moving = entries.Count(x => x.Status == EntryStatus.Ready && x.MoveToRoot);
                if (moving > 0) message += string.Format("  하위 폴더의 {0:N0}개 파일을 추가한 폴더로 옮깁니다.", moving);
                if (scanWarnings.Count > 0) message += string.Format("  읽기 경고 {0}개 · 여기를 누르면 확인", scanWarnings.Count);
                SetStatus(message, scanWarnings.Count > 0 ? Theme.Warning : Theme.Muted);
                toolTip.SetToolTip(statusLabel, string.Join("\n", scanWarnings.Take(10)));
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false, null); }
        }

        private async Task Apply()
        {
            if (busy) return;
            grid.EndEdit();
            List<RenameEntry> selected = entries.Where(x => x.Status == EntryStatus.Ready && x.Selected).ToList();
            if (selected.Count == 0) return;
            bool includesMoves = selected.Any(x => x.MoveToRoot);
            SetBusy(true, string.Format(includesMoves ? "{0:N0}개 파일의 이름을 변경하고 이동하는 중입니다…" :
                "{0:N0}개 파일의 이름을 변경하는 중입니다…", selected.Count));
            try
            {
                BatchResult result = await Task.Run(delegate { return RenameEngine.Apply(selected, historyDirectory); });
                cleanupWarnings = result.CleanupWarnings;
                BindRows();
                string message = result.Message + (cleanupWarnings.Count > 0 ? " · 여기를 누르면 폴더 정리 경고 확인" : "");
                SetStatus(message, result.Failed > 0 || cleanupWarnings.Count > 0 ? Theme.Warning : Theme.Accent);
                toolTip.SetToolTip(statusLabel, string.Join("\n", new string[] { result.Message }.Concat(cleanupWarnings)));
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false, null); }
        }

        private async Task Undo()
        {
            if (busy) return;
            SetBusy(true, "마지막 작업의 이름과 위치를 되돌리는 중입니다…");
            BatchResult result = null;
            try { result = await Task.Run(delegate { return RenameEngine.UndoLast(FindUndoDirectory()); }); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false, null); }
            if (result != null)
            {
                if (roots.Count > 0) await Scan();
                SetStatus(result.Message, result.Failed > 0 ? Theme.Warning : Theme.Accent);
                toolTip.SetToolTip(statusLabel, result.Message);
            }
        }

        private void BindRows()
        {
            binding = true;
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (RenameEntry entry in entries)
            {
                if (changesOnly.Checked && entry.Status != EntryStatus.Ready && entry.Status != EntryStatus.Done &&
                    entry.Status != EntryStatus.Conflict && entry.Status != EntryStatus.Error) continue;
                string detail = FolderDisplay(entry.Folder);
                if (entry.MoveToRoot) detail += "  →  " + FolderDisplay(entry.TargetFolder);
                if (entry.CollisionNumbered) detail += "   ·   중복 이름에 번호 추가";
                if (!string.IsNullOrEmpty(entry.Message) && entry.Status != EntryStatus.Ready && entry.Status != EntryStatus.Done)
                    detail += "   ·   " + entry.Message;
                string status = entry.Status == EntryStatus.Ready && entry.CollisionNumbered ? "번호 추가" : StatusText(entry.Status);
                if (entry.MoveToRoot && entry.Status == EntryStatus.Ready)
                    status = string.Equals(entry.OriginalName, entry.NewName, StringComparison.Ordinal) ? "이동 예정" : "변경·이동";
                else if (entry.MoveToRoot && entry.Status == EntryStatus.Done) status = "이동 완료";
                int index = grid.Rows.Add(entry.Status == EntryStatus.Ready && entry.Selected, status,
                    entry.OriginalName, entry.NewName ?? "—", detail);
                DataGridViewRow row = grid.Rows[index]; row.Tag = entry;
                row.Cells[0].ReadOnly = entry.Status != EntryStatus.Ready;
                Color color = entry.Status == EntryStatus.Ready || entry.Status == EntryStatus.Done ? Theme.Accent :
                    entry.Status == EntryStatus.Conflict ? Theme.Warning : entry.Status == EntryStatus.Error ? Theme.Error : Theme.Muted;
                row.Cells[1].Style.ForeColor = color;
                row.Cells[3].Style.ForeColor = entry.Status == EntryStatus.Ready || entry.Status == EntryStatus.Done ? Theme.Accent : Theme.Muted;
                string tooltip = entry.SourcePath + (entry.MoveToRoot ? "\n이동 위치: " + entry.TargetPath : "")
                    + (string.IsNullOrEmpty(entry.NewName) ? "" : "\n변경 이름: " + entry.NewName)
                    + (string.IsNullOrEmpty(entry.Message) ? "" : "\n" + entry.Message);
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = tooltip;
            }
            grid.ClearSelection(); grid.ResumeLayout();
            binding = false;
            int readyCount = entries.Count(x => x.Status == EntryStatus.Ready);
            int doneCount = entries.Count(x => x.Status == EntryStatus.Done);
            int movingCount = entries.Count(x => x.Status == EntryStatus.Ready && x.MoveToRoot);
            int movedCount = entries.Count(x => x.Status == EntryStatus.Done && x.MoveToRoot);
            int errorCount = entries.Count(x => x.Status == EntryStatus.Conflict || x.Status == EntryStatus.Error);
            summaryLabel.Text = string.Format("전체 {0:N0}개   ·   변경 가능 {1:N0}개", entries.Count, readyCount);
            if (doneCount > 0) summaryLabel.Text += string.Format("   ·   완료 {0:N0}개", doneCount);
            if (movingCount > 0) summaryLabel.Text += string.Format("   ·   이동 대상 {0:N0}개", movingCount);
            if (errorCount > 0) summaryLabel.Text += string.Format("   ·   오류 {0:N0}개", errorCount);
            if (changesOnly.Checked) summaryLabel.Text += string.Format("   ·   표시 {0:N0}개", grid.Rows.Count);
            toolTip.SetToolTip(summaryLabel, string.Format(
                "전체 {0:N0}개\n변경 가능 {1:N0}개\n완료 {2:N0}개\n변경 없음 {3:N0}개\n형식 제외 {4:N0}개\n번호 추가 {5:N0}개\n충돌 또는 오류 {6:N0}개\n이동 대상 {7:N0}개\n이동 완료 {8:N0}개",
                entries.Count, readyCount, doneCount, entries.Count(x => x.Status == EntryStatus.Unchanged),
                entries.Count(x => x.Status == EntryStatus.Skipped), entries.Count(x => x.CollisionNumbered), errorCount, movingCount, movedCount));
            grid.Invalidate();
            UpdateActions();
        }

        private string FolderDisplay(string folder)
        {
            return string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        }

        private string FindUndoDirectory()
        {
            return RenameEngine.HasUndo(historyDirectory) ? historyDirectory : legacyHistoryDirectory;
        }

        private string StatusText(EntryStatus status)
        {
            switch (status)
            {
                case EntryStatus.Ready: return "변경 예정";
                case EntryStatus.Unchanged: return "변경 없음";
                case EntryStatus.Skipped: return "형식 제외";
                case EntryStatus.Conflict: return "이름 충돌";
                case EntryStatus.Error: return "처리 오류";
                case EntryStatus.Done: return "변경 완료";
                default: return "확인 필요";
            }
        }

        private void SetBusy(bool value, string message)
        {
            if (value) { EndDragFeedback(); cleanupWarnings.Clear(); }
            busy = value; UseWaitCursor = value; progress.Visible = value;
            statusLabel.Cursor = !busy && (cleanupWarnings.Count > 0 || scanWarnings.Count > 0) ? Cursors.Hand : Cursors.Default;
            grid.Invalidate();
            changesOnly.Enabled = !value;
            gatherToRoot.Enabled = !value;
            selectAll.Enabled = !value;
            if (message != null) SetStatus(message, Theme.Muted);
            UpdateActions();
        }

        private void UpdateActions()
        {
            int count = entries.Count(x => x.Status == EntryStatus.Ready && x.Selected);
            int moving = entries.Count(x => x.Status == EntryStatus.Ready && x.Selected && x.MoveToRoot);
            selectionLabel.Text = string.Format("선택 {0:N0}개", count);
            applyButton.Text = count == 0 ? "선택한 파일 변경" :
                string.Format(moving > 0 ? "{0:N0}개 변경 · 이동" : "{0:N0}개 파일 변경", count);
            toolTip.SetToolTip(applyButton, moving > 0 ? string.Format("선택한 {0:N0}개 중 {1:N0}개를 하위 폴더에서 추가한 폴더로 옮깁니다.\n이동 후 비어 있는 원래 하위 폴더는 삭제합니다.", count, moving) :
                "선택한 파일의 이름을 변경합니다.");
            applyButton.Enabled = !busy && count > 0;
            clearButton.Enabled = !busy && roots.Count > 0;
            refreshButton.Enabled = !busy && roots.Count > 0;
            try { undoButton.Enabled = !busy && (RenameEngine.HasUndo(historyDirectory) || RenameEngine.HasUndo(legacyHistoryDirectory)); }
            catch { undoButton.Enabled = false; }
            binding = true;
            selectAll.Checked = entries.Any(x => x.Status == EntryStatus.Ready) &&
                entries.Where(x => x.Status == EntryStatus.Ready).All(x => x.Selected);
            binding = false;
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text; statusLabel.ForeColor = color;
            statusLabel.Cursor = !busy && (cleanupWarnings.Count > 0 || scanWarnings.Count > 0) ? Cursors.Hand : Cursors.Default;
        }
        private void ShowError(string text)
        {
            SetStatus(text, Theme.Error);
            MessageBox.Show(this, text, "파일명 정리", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
