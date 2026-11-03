using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    internal static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(18, 22, 31), Card = Color.FromArgb(27, 33, 45), Field = Color.FromArgb(36, 44, 59);
        internal static readonly Color Text = Color.FromArgb(230, 237, 247), Muted = Color.FromArgb(155, 171, 193), Accent = Color.FromArgb(100, 220, 194);
        internal static Label Label(string text, int size, Color color)
        { return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Microsoft YaHei UI", size), Margin = new Padding(0, 5, 0, 5) }; }
        internal static Button Button(string text, int width, Action click, bool accent)
        {
            var button = new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = accent ? Accent : Field, ForeColor = accent ? Background : Text, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 8, 3) };
            button.FlatAppearance.BorderSize = 0; button.Click += (s, e) => click(); return button;
        }
        internal static ComboBox Combo(int width)
        { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Field, ForeColor = Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 8, 5) }; }
        internal static TextBox TextBox(int width, bool multiline)
        { return new TextBox { Width = width, Multiline = multiline, Height = multiline ? 78 : 28, BackColor = Field, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 0, 5), Font = new Font("Consolas", 10) }; }
    }

    internal sealed class PreviewPanel : Panel
    {
        internal CrosshairStyle Style;
        internal ImageAsset Image;
        internal ImageCrosshair ImageStyle;
        internal bool ImageMode;
        internal double RenderScale = 1;
        internal string Surface = "深色";
        internal PreviewPanel() { DoubleBuffered = true; BackColor = Theme.Card; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Surface == "浅色") g.Clear(Color.FromArgb(228, 233, 237));
            else if (Surface == "复杂背景")
            {
                using (var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(36, 79, 91), Color.FromArgb(136, 99, 72), 30)) g.FillRectangle(gradient, ClientRectangle);
                using (var pen = new Pen(Color.FromArgb(65, 255, 255, 255)))
                    for (int n = -Height; n < Width; n += 26) g.DrawLine(pen, n, Height, n + Height, 0);
            }
            else
            {
                g.Clear(Color.FromArgb(20, 27, 38));
                using (var pen = new Pen(Color.FromArgb(35, 44, 59)))
                {
                    for (int x = Width / 2 % 24; x < Width; x += 24) g.DrawLine(pen, x, 0, x, Height);
                    for (int y = Height / 2 % 24; y < Height; y += 24) g.DrawLine(pen, 0, y, Width, y);
                }
            }
            if (ImageMode && Image != null) Image.Draw(g, ImageStyle, new PointF(Width / 2f, Height / 2f), RenderScale);
            else if (Style != null) Renderer.Draw(g, Style, new PointF(Width / 2f, Height / 2f), RenderScale);
            using (var brush = new SolidBrush(Surface == "浅色" ? Color.DimGray : Theme.Muted))
                g.DrawString("实际尺寸 · " + (int)Math.Round(RenderScale * 100) + "%", Font, brush, 12, 12);
            base.OnPaint(e);
        }
    }

    internal sealed class NumberRow : Panel
    {
        internal readonly NumericUpDown Input;
        private readonly TrackBar slider;
        private readonly double step;
        private bool syncing;
        internal event Action<double> Changed;
        internal NumberRow(string text, double min, double max, double step)
        {
            this.step = step; Width = 560; Height = 42; Margin = new Padding(0, 1, 0, 1);
            var label = Theme.Label(text, 10, Theme.Text); label.Location = new Point(0, 9); label.AutoSize = false; label.Size = new Size(112, 26); label.AutoEllipsis = true;
            slider = new TrackBar { Minimum = (int)Math.Round(min / step), Maximum = (int)Math.Round(max / step), TickStyle = TickStyle.None, AutoSize = false, Height = 30, Width = 306, Location = new Point(116, 7), BackColor = Theme.Card, SmallChange = 1, LargeChange = 10 };
            Input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, DecimalPlaces = step < 1 ? 2 : 0, Location = new Point(432, 8), Width = 102, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            Controls.AddRange(new Control[] { label, slider, Input });
            slider.ValueChanged += (s, e) => { if (!syncing) Input.Value = (decimal)(slider.Value * step); };
            Input.ValueChanged += (s, e) =>
            {
                if (syncing) return;
                syncing = true; slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, (int)Math.Round((double)Input.Value / step))); syncing = false;
                if (Changed != null) Changed((double)Input.Value);
            };
        }
        internal void Set(double value)
        {
            syncing = true;
            Input.Value = Math.Max(Input.Minimum, Math.Min(Input.Maximum, (decimal)value));
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, (int)Math.Round(value / step)));
            syncing = false;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly SettingsStore store;
        private readonly AppSettings settings;
        private readonly OverlayWindow overlay;
        private readonly ImageAsset image = new ImageAsset();
        private readonly CheckBox imageMode = Check("使用图片作为准星");
        private readonly Label imageInfo = Theme.Label("尚未选择图片", 10, Theme.Text);
        private readonly Label imageDimensions = Theme.Label("", 10, Theme.Muted);
        private readonly Button imageEditButton;
        private HotkeyService hotkeys;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 450 };
        private readonly System.Windows.Forms.Timer topTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly List<Action> sync = new List<Action>();
        private readonly PreviewPanel preview = new PreviewPanel();
        private readonly Label status = Theme.Label("", 9, Theme.Muted), shortcutLabel = Theme.Label("", 10, Theme.Accent);
        private readonly Button visibility;
        private readonly ComboBox shape = Theme.Combo(220), monitors = Theme.Combo(420), presets = Theme.Combo(282), previewBg = Theme.Combo(282);
        private readonly TextBox colorHex = Theme.TextBox(150, false), outlineHex = Theme.TextBox(150, false), presetName = Theme.TextBox(282, false);
        private readonly TextBox importText = Theme.TextBox(530, true), exportText = Theme.TextBox(530, true);
        private readonly CheckBox dot = Check("中心点"), round = Check("圆形中心点"), pixel = Check("像素对齐（更清晰）"), scaleWithDpi = Check("跟随 Windows 缩放（与原版一致）");
        private readonly TextBox shortcutInput = Theme.TextBox(330, false);
        private readonly ComboBox triggerMode = Theme.Combo(260);
        private readonly CheckBox allowOtherKeys = Check("允许同时按住其他键（游戏移动时也能切换）");
        private readonly Label backgroundInputStatus = Theme.Label("", 9, Theme.Muted);
        private KeyBinding pendingShortcut;
        private readonly NumericUpDown offsetX = Spin(-16000, 16000), offsetY = Spin(-16000, 16000), csHeight = Spin(240, 8640);
        private readonly Button editButton;
        private bool loading, exiting, dirty, startInTray, trayHintShown;

        internal MainForm(SettingsStore store, bool startInTray)
        {
            this.store = store; this.startInTray = startInTray; settings = store.Load();
            pendingShortcut = settings.Shortcut.Copy();
            Text = "星准 · Crosshair Desktop"; ClientSize = new Size(1010, 760); MinimumSize = new Size(1000, 700);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Icon = AppIcon.Create();
            if (!string.IsNullOrEmpty(settings.Image.File))
            {
                try { image.Load(Path.Combine(Path.GetDirectoryName(store.Path), settings.Image.File)); }
                catch (Exception ex) { store.Warning = "图片读取失败：" + ex.Message; }
            }
            overlay = new OverlayWindow(settings, image);
            visibility = Theme.Button("", 170, ToggleVisibility, true);
            editButton = Theme.Button("拖动调整位置", 170, ToggleEditing, false);
            imageEditButton = Theme.Button("在屏幕上调整", 180, ToggleEditing, true);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 12), ColumnCount = 2, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 314)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var header = new Panel { Dock = DockStyle.Fill };
            var title = Theme.Label("星准", 24, Theme.Text); title.Location = new Point(0, 0);
            var subtitle = Theme.Label("CROSSHAIR DESKTOP   /   独立桌面准星", 9, Theme.Muted); subtitle.Location = new Point(2, 56);
            visibility.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            header.Controls.AddRange(new Control[] { title, subtitle, visibility });
            header.Resize += (s, e) => visibility.Location = new Point(header.ClientSize.Width - visibility.Width, 12);
            root.Controls.Add(header, 0, 0); root.SetColumnSpan(header, 2);
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 0, 16, 0) };
            left.Controls.Add(Theme.Label("实时预览", 11, Theme.Text));
            preview.Size = new Size(282, 234); preview.Margin = new Padding(0, 8, 0, 6); left.Controls.Add(preview);
            previewBg.Items.AddRange(new object[] { "深色", "浅色", "复杂背景" });
            previewBg.SelectedIndexChanged += (s, e) => { if (!loading) { settings.PreviewBackground = (string)previewBg.SelectedItem; Changed(); } };
            left.Controls.Add(previewBg);
            left.Controls.Add(Theme.Label("预设", 11, Theme.Text)); left.Controls.Add(presets);
            presets.SelectedIndexChanged += (s, e) =>
            {
                if (loading || !(presets.SelectedItem is SavedPreset)) return;
                Run(() => {
                    var selected = (SavedPreset)presets.SelectedItem;
                    if (selected.Image != null)
                    {
                        var candidate = selected.Image.Copy(); candidate.Validate();
                        image.Load(Path.Combine(Path.GetDirectoryName(store.Path), candidate.File)); settings.Image = candidate; settings.ImageMode = true;
                    }
                    else { string note; settings.Style = ShareCodes.Import(selected.Code, settings.Cs2Height, overlay.RenderScale, out note); settings.ImageMode = false; }
                    presetName.Text = selected.Name; Changed(); SetStatus("已应用预设：" + selected.Name, false);
                });
            };
            presetName.Text = "我的准星"; presetName.MaxLength = 24; left.Controls.Add(presetName);
            left.Controls.Add(Row(Theme.Button("保存为新预设", 136, () => SavePreset(false), false), Theme.Button("覆盖", 62, () => SavePreset(true), false), Theme.Button("删除", 62, DeletePreset, false)));
            left.Controls.Add(Theme.Label("快速开关", 11, Theme.Text)); shortcutLabel.MaximumSize = new Size(282, 120); left.Controls.Add(shortcutLabel);
            var tip = Theme.Label("按设置的触发方式切换。\n设置窗口关闭后仍在托盘运行。", 9, Theme.Muted); tip.MaximumSize = new Size(282, 60); left.Controls.Add(tip);
            root.Controls.Add(left, 0, 1);
            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Font, Padding = new Point(18, 10) };
            tabs.TabPages.Add(StylePage()); tabs.TabPages.Add(ImagePage()); tabs.TabPages.Add(PositionPage()); tabs.TabPages.Add(SharePage());
            root.Controls.Add(tabs, 1, 1);
            var footer = new Panel { Dock = DockStyle.Fill };
            status.Location = new Point(0, 12); status.AutoSize = false; status.Size = new Size(690, 25);
            var hide = Theme.Button("收起到托盘", 132, MinimizeToTray, false); hide.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            footer.Controls.AddRange(new Control[] { status, hide }); footer.Resize += (s, e) => hide.Location = new Point(footer.Width - hide.Width, 5);
            root.Controls.Add(footer, 0, 2); root.SetColumnSpan(footer, 2); Controls.Add(root);
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏准星", null, (s, e) => ToggleVisibility());
            menu.Items.Add("设置", null, (s, e) => OpenSettings());
            menu.Items.Add("准星居中", null, (s, e) => CenterCrosshair());
            menu.Items.Add("退出", null, (s, e) => { exiting = true; Close(); });
            tray = new NotifyIcon { Text = "星准 · Ctrl + Alt", Icon = (Icon)Icon.Clone(), ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Flush(); };
            topTimer.Tick += (s, e) => overlay.KeepOnTop();
            overlay.PositionChanged += () => { SyncAll(); MarkDirty(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Shown += OnShown;
            FormClosing += (s, e) =>
            {
                if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; MinimizeToTray(); }
            };
            Resize += (s, e) => { if (!exiting && tray != null && WindowState == FormWindowState.Minimized) MinimizeToTray(); };
            FormClosed += (s, e) =>
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
                saveTimer.Stop(); topTimer.Stop(); Flush(); if (hotkeys != null) hotkeys.Dispose();
                overlay.Close(); overlay.Dispose(); image.Dispose(); tray.Visible = false; tray.Icon.Dispose(); tray.Dispose(); saveTimer.Dispose(); topTimer.Dispose();
            };
            FillMonitors(); RefreshPresetList(null); SyncAll();
        }

        private static CheckBox Check(string name) { return new CheckBox { Text = name, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 8, 16, 8) }; }
        private static NumericUpDown Spin(int min, int max)
        { return new NumericUpDown { Minimum = min, Maximum = max, Width = 110, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(8, 6, 18, 6) }; }
        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
            row.Controls.AddRange(controls); return row;
        }
        private static FlowLayoutPanel Page(string title, out TabPage page)
        {
            page = new TabPage(title) { BackColor = Theme.Card, ForeColor = Theme.Text, Padding = new Padding(16) };
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card };
            page.Controls.Add(panel); return panel;
        }
        private void Number(FlowLayoutPanel panel, string title, double min, double max, double step, Func<double> get, Action<double> set)
        {
            var row = new NumberRow(title, min, max, step);
            row.Changed += value => { if (!loading) { set(value); Changed(); } };
            sync.Add(() => row.Set(get())); panel.Controls.Add(row);
        }
        private TabPage StylePage()
        {
            TabPage page; var panel = Page("样式", out page);
            panel.Controls.Add(Theme.Label("形状与尺寸", 12, Theme.Accent));
            shape.Items.AddRange(new object[] { "十字准星", "T 形准星", "圆环准星" });
            panel.Controls.Add(Theme.Button("使用形状准星", 180, () => { settings.ImageMode = false; Changed(); }, true));
            shape.SelectedIndexChanged += (s, e) => { if (!loading) { settings.ImageMode = false; settings.Style.EnableCircleCrosshair = shape.SelectedIndex == 2; settings.Style.EnableTShape = shape.SelectedIndex == 1; Changed(); } };
            panel.Controls.Add(shape);
            panel.Controls.Add(Theme.Label("圆环模式：长度为半径，忽略间距与像素对齐。", 9, Theme.Muted));
            Number(panel, "长度 / 半径", 0, 100, .5, () => settings.Style.Size, v => settings.Style.Size = v);
            Number(panel, "线条粗细", 0, 100, .5, () => settings.Style.Thickness, v => settings.Style.Thickness = v);
            Number(panel, "中心间距", -100, 100, .5, () => settings.Style.Gap, v => settings.Style.Gap = v);
            pixel.CheckedChanged += (s, e) => { if (!loading) { settings.Style.PixelSnap = pixel.Checked; Changed(); } }; panel.Controls.Add(pixel);
            panel.Controls.Add(Theme.Label("中心点 · 尺寸 0 跟随线宽", 12, Theme.Accent)); panel.Controls.Add(Row(dot, round));
            dot.CheckedChanged += (s, e) => { if (!loading) { settings.Style.EnableDot = dot.Checked; Changed(); } };
            round.CheckedChanged += (s, e) => { if (!loading) { settings.Style.DotIsCircle = round.Checked; Changed(); } };
            Number(panel, "点尺寸", 0, 100, .5, () => settings.Style.DotSize, v => settings.Style.DotSize = v);
            panel.Controls.Add(Theme.Label("描边", 12, Theme.Accent));
            Number(panel, "描边粗细", 0, 100, .5, () => settings.Style.Outline, v => settings.Style.Outline = v);
            panel.Controls.Add(Row(Theme.Label("描边颜色", 10, Theme.Text), outlineHex, Theme.Button("选色", 64, () => ChooseColor(true), false)));
            outlineHex.Validated += (s, e) => ApplyHex(true); outlineHex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyHex(true); } };
            panel.Controls.Add(Theme.Label("颜色与透明度", 12, Theme.Accent));
            var swatches = new List<Control>();
            foreach (string color in new[] { "#FF5050", "#00FF00", "#FFFF00", "#00AAFF", "#00FFFF", "#FFFFFF" })
            {
                string selected = color; var button = Theme.Button("", 32, () => { settings.Style.Color = selected; Changed(); }, false); button.BackColor = CrosshairStyle.ParseColor(color); button.AccessibleName = color; swatches.Add(button);
            }
            panel.Controls.Add(Row(swatches.ToArray()));
            panel.Controls.Add(Row(Theme.Label("准星颜色", 10, Theme.Text), colorHex, Theme.Button("选色", 64, () => ChooseColor(false), false)));
            colorHex.Validated += (s, e) => ApplyHex(false); colorHex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyHex(false); } };
            for (int channel = 0; channel < 3; channel++)
            {
                int index = channel;
                Number(panel, new[] { "红 R", "绿 G", "蓝 B" }[channel], 0, 255, 1,
                    () => { var c = CrosshairStyle.ParseColor(settings.Style.Color); return new[] { c.R, c.G, c.B }[index]; },
                    v => { var c = CrosshairStyle.ParseColor(settings.Style.Color); int[] rgb = { c.R, c.G, c.B }; rgb[index] = (int)v; settings.Style.Color = string.Format("#{0:X2}{1:X2}{2:X2}", rgb[0], rgb[1], rgb[2]); });
            }
            Number(panel, "不透明度", 0, 255, 1, () => settings.Style.Alpha, v => settings.Style.Alpha = (int)v);
            panel.Controls.Add(Theme.Button("恢复默认样式", 180, () =>
            {
                if (MessageBox.Show(this, "恢复默认绿色十字？已保存的预设会保留。", "恢复默认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { settings.Style = new CrosshairStyle(); settings.ImageMode = false; Changed(); }
            }, false));
            return page;
        }

        private TabPage ImagePage()
        {
            TabPage page; var panel = Page("图片准星", out page);
            panel.Controls.Add(Theme.Label("自选图片", 12, Theme.Accent));
            panel.Controls.Add(imageMode);
            imageMode.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                if (imageMode.Checked && image.Picture == null) { SyncAll(); SetStatus("请先选择一张准星图片。", true); return; }
                overlay.SetEditing(false); settings.ImageMode = imageMode.Checked; Changed();
            };
            panel.Controls.Add(Theme.Button("选择图片…", 180, ChooseImage, true));
            imageInfo.MaximumSize = new Size(520, 70); panel.Controls.Add(imageInfo);
            var hint = Theme.Label("支持 PNG、JPG、BMP、GIF（首帧），保留透明背景。\n图片会复制到软件目录，移动原文件也不影响使用。", 9, Theme.Muted);
            hint.MaximumSize = new Size(530, 65); panel.Controls.Add(hint);
            panel.Controls.Add(Theme.Label("大小与透明度", 12, Theme.Accent));
            Number(panel, "缩放 %", 1, 1000, 1, () => settings.Image.Percent, v => settings.Image.Percent = v);
            Number(panel, "不透明度", 0, 255, 1, () => settings.Image.Alpha, v => settings.Image.Alpha = (int)v);
            panel.Controls.Add(imageDimensions);
            panel.Controls.Add(Row(Theme.Button("原始大小 100%", 180, () => { settings.Image.Percent = 100; Changed(); }, false), Theme.Button("屏幕居中", 136, CenterCrosshair, false)));
            panel.Controls.Add(Theme.Label("在屏幕上调整", 12, Theme.Accent));
            panel.Controls.Add(imageEditButton);
            var instructions = Theme.Label("拖动图片或蓝色区域移动位置；拖动右下角绿色手柄缩放。\n始终保留图片比例，缩放时中心位置不变。\n点击“完成图片调整”恢复鼠标穿透，快捷键仍可显示 / 隐藏。\n屏幕图片单边最大 2048 像素。", 9, Theme.Muted);
            instructions.MaximumSize = new Size(530, 110); panel.Controls.Add(instructions);
            panel.Controls.Add(Theme.Label("图片、缩放和位置自动保存；也可以保存为左侧预设。", 9, Theme.Muted));
            return page;
        }
        private void ChooseImage()
        {
            using (var dialog = new OpenFileDialog { Title = "选择准星图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Run(() =>
                {
                    var candidate = ImageAsset.Import(dialog.FileName, Path.GetDirectoryName(store.Path));
                    image.Load(Path.Combine(Path.GetDirectoryName(store.Path), candidate.File));
                    overlay.SetEditing(false); settings.Image = candidate; settings.ImageMode = true; Changed();
                    SetStatus("图片已载入，可点击“在屏幕上调整”拖动和缩放。", false);
                });
        }
        private void ToggleEditing()
        {
            if (settings.ImageMode && image.Picture == null) { SetStatus("请先重新选择准星图片。", true); return; }
            overlay.SetEditing(!overlay.Editing); SyncAll();
        }

        private TabPage PositionPage()
        {
            TabPage page; var panel = Page("位置与快捷键", out page);
            panel.Controls.Add(Theme.Label("显示位置", 12, Theme.Accent)); panel.Controls.Add(monitors);
            monitors.SelectedIndexChanged += (s, e) =>
            {
                if (loading || !(monitors.SelectedItem is MonitorChoice)) return;
                overlay.Monitor = (MonitorChoice)monitors.SelectedItem; settings.Monitor = overlay.Monitor.Device; settings.OffsetX = settings.OffsetY = 0; Changed();
            };
            panel.Controls.Add(Row(Theme.Label("水平偏移 px", 10, Theme.Text), offsetX, Theme.Label("垂直偏移 px", 10, Theme.Text), offsetY));
            offsetX.ValueChanged += (s, e) => { if (!loading) { settings.OffsetX = (int)offsetX.Value; Changed(); } };
            offsetY.ValueChanged += (s, e) => { if (!loading) { settings.OffsetY = (int)offsetY.Value; Changed(); } };
            panel.Controls.Add(Row(Theme.Button("屏幕居中", 136, CenterCrosshair, true), editButton));
            panel.Controls.Add(Row(Theme.Button("←", 52, () => Nudge(-1, 0), false), Theme.Button("↑", 52, () => Nudge(0, -1), false), Theme.Button("↓", 52, () => Nudge(0, 1), false), Theme.Button("→", 52, () => Nudge(1, 0), false)));
            var hint = Theme.Label("微调每次 1 像素，按住 Shift 时每次 10 像素。\n拖动调整时准星可被鼠标选中，完成后恢复鼠标穿透。", 9, Theme.Muted); hint.MaximumSize = new Size(530, 60); panel.Controls.Add(hint);
            scaleWithDpi.CheckedChanged += (s, e) => { if (!loading) { settings.ScaleWithDpi = scaleWithDpi.Checked; Changed(); } }; panel.Controls.Add(scaleWithDpi);
            panel.Controls.Add(Theme.Label("显示 / 隐藏快捷键", 12, Theme.Accent));
            shortcutInput.ReadOnly = true; shortcutInput.AccessibleName = "待应用的快捷键";
            panel.Controls.Add(Row(shortcutInput, Theme.Button("录入快捷键…", 170, RecordShortcut, true)));
            triggerMode.AccessibleName = "触发方式";
            triggerMode.Items.AddRange(new object[] { "按下时触发", "松开时触发（全部松开）" });
            triggerMode.SelectedIndexChanged += (s, e) => { if (!loading) Run(() =>
            {
                string trigger = triggerMode.SelectedIndex == 0 ? "Press" : "Release";
                var active = settings.Shortcut.Copy(); active.Trigger = trigger;
                hotkeys.SetBinding(active); settings.Shortcut = active; pendingShortcut.Trigger = trigger;
                Changed(); SetStatus("触发方式已生效：" + active.Label + " · " + active.TriggerLabel, false);
            }); };
            panel.Controls.Add(Row(Theme.Label("触发方式", 10, Theme.Text), triggerMode));
            allowOtherKeys.CheckedChanged += (s, e) => { if (!loading) Run(() =>
            {
                var active = settings.Shortcut.Copy(); active.AllowOtherKeys = allowOtherKeys.Checked;
                hotkeys.SetBinding(active); settings.Shortcut = active; pendingShortcut.AllowOtherKeys = active.AllowOtherKeys;
                Changed(); SetStatus("其他按键兼容设置已生效并将自动保存。", false);
            }); };
            panel.Controls.Add(allowOtherKeys);
            panel.Controls.Add(Theme.Label("触发方式和其他键选项立即生效，并自动保存。", 9, Theme.Accent));
            var otherKeysHint = Theme.Label("开启后，按住 W、Shift 等其他键也可触发。\n松开触发只需松开快捷键本身，移动键可以继续按住。", 9, Theme.Muted); otherKeysHint.MaximumSize = new Size(530, 60); panel.Controls.Add(otherKeysHint);
            panel.Controls.Add(Theme.Button("应用快捷键", 170, () => Run(() =>
            {
                var candidate = pendingShortcut.Copy(); candidate.Trigger = triggerMode.SelectedIndex == 0 ? "Press" : "Release";
                hotkeys.SetBinding(candidate); settings.Shortcut = candidate; pendingShortcut = candidate.Copy(); Changed(); SetStatus("快捷键已更新：" + candidate.Label + " · " + candidate.TriggerLabel, false);
            }), true));
            panel.Controls.Add(Theme.Button("填入默认 Ctrl + Alt", 220, () => { pendingShortcut = new KeyBinding(); SyncAll(); SetStatus("已填入默认快捷键，点击“应用快捷键”生效。", false); }, false));
            var keyHint = Theme.Label("单键、Ctrl / Alt / Shift / Win、功能键、方向键、符号键均可录入，\n也支持多个普通键一起按（例如 Q + E）。长按不会重复切换。\n按下：组合按齐时切换；松开：组合全部松开后切换。\n录入期间暂停开关；确认后仍需点击“应用快捷键”。\n系统保留组合（如 Win+L、Ctrl+Alt+Delete）无法接管。\n正常使用时按键仍会传给游戏或前台软件。", 9, Theme.Muted); keyHint.MaximumSize = new Size(530, 150); panel.Controls.Add(keyHint);
            panel.Controls.Add(Theme.Label("游戏显示模式", 12, Theme.Accent));
            var modeHint = Theme.Label("推荐窗口化或无边框全屏。独占全屏可能盖住桌面准星。\n准星置顶、鼠标穿透，切换不会抢走游戏焦点。", 9, Theme.Muted); modeHint.MaximumSize = new Size(530, 70); panel.Controls.Add(modeHint);
            panel.Controls.Add(Theme.Label("游戏快捷键兼容", 12, Theme.Accent));
            panel.Controls.Add(backgroundInputStatus);
            var admin = Theme.Button("以管理员权限重启", 220, RestartAsAdministrator, false); admin.Enabled = !Program.IsAdministrator; panel.Controls.Add(admin);
            var adminHint = Theme.Label("已加入后台键盘接收，支持游戏位于前台时切换。\n若游戏以管理员权限运行且仍无响应，可点击上方按钮。\nWindows 会要求权限确认；取消后原程序继续运行。", 9, Theme.Muted); adminHint.MaximumSize = new Size(530, 80); panel.Controls.Add(adminHint);
            return page;
        }
        private void RestartAsAdministrator()
        {
            Run(() =>
            {
                Flush(); if (dirty) throw new IOException("设置尚未保存成功，暂不重启，请先检查保存位置。");
                var start = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "--wait-for-exit " + System.Diagnostics.Process.GetCurrentProcess().Id)
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory };
                using (var next = System.Diagnostics.Process.Start(start))
                {
                    if (next == null) throw new InvalidOperationException("未能启动管理员模式，原程序继续运行。");
                    exiting = true; Close();
                }
            });
        }
        private void RecordShortcut()
        {
            Run(() =>
            {
                using (var dialog = new HotkeyCaptureDialog(hotkeys))
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                    {
                        var candidate = dialog.Result.Copy(); candidate.Trigger = pendingShortcut.OnPress ? "Press" : "Release";
                        candidate.AllowOtherKeys = pendingShortcut.AllowOtherKeys;
                        pendingShortcut = candidate; SyncAll(); SetStatus("快捷键已录入，点击“应用快捷键”生效。", false);
                    }
            });
        }

        private TabPage SharePage()
        {
            TabPage page; var panel = Page("导入与分享", out page);
            panel.Controls.Add(Theme.Label("导入准星", 12, Theme.Accent));
            var hint = Theme.Label("支持 Murbong 的 MBCH 分享码、CS2 / CSGO 分享码，\n以及 Murbong 旧版和本程序的样式 JSON。", 9, Theme.Muted); hint.MaximumSize = new Size(530, 60); panel.Controls.Add(hint);
            importText.Height = 105; importText.ScrollBars = ScrollBars.Vertical; panel.Controls.Add(importText);
            panel.Controls.Add(Row(Theme.Button("导入内容", 136, () => Import(importText.Text), true), Theme.Button("从剪贴板粘贴", 156, () => Run(() => { importText.Text = Clipboard.GetText(); Import(importText.Text); }), false), Theme.Button("打开文件", 126, ImportFile, false)));
            csHeight.ValueChanged += (s, e) => { if (!loading) { settings.Cs2Height = (int)csHeight.Value; MarkDirty(); } };
            panel.Controls.Add(Row(Theme.Label("CS2 游戏分辨率高度", 10, Theme.Text), csHeight));
            panel.Controls.Add(Theme.Label("CS2 动态准星以静态样式导入；半侧描边用全描边近似。", 9, Theme.Muted));
            panel.Controls.Add(Theme.Label("分享当前准星", 12, Theme.Accent));
            exportText.ReadOnly = true; exportText.Height = 72; panel.Controls.Add(exportText);
            panel.Controls.Add(Row(Theme.Button("复制分享码", 156, () => Run(() => { RequireShape(); Clipboard.SetText(ShareCodes.Export(settings.Style)); SetStatus("分享码已复制。", false); }), true), Theme.Button("导出样式 JSON", 180, ExportFile, false)));
            panel.Controls.Add(Theme.Label("全部设置与预设自动保存在软件同目录，便于备份和迁移。", 9, Theme.Muted));
            return page;
        }

        private void OnShown(object sender, EventArgs e)
        {
            hotkeys = new HotkeyService(this); hotkeys.Pressed += ToggleVisibility;
            Run(() => hotkeys.SetBinding(settings.Shortcut));
            backgroundInputStatus.Text = (hotkeys.BackgroundInput ? "后台键盘接收已启用" : "后台接收不可用，正在使用兼容监听") + " · " + (Program.IsAdministrator ? "管理员权限" : "普通权限");
            overlay.RefreshOverlay(); topTimer.Start();
            SetStatus(store.Warning ?? "就绪 · 所有修改自动保存", store.Warning != null);
            if (startInTray) BeginInvoke((Action)MinimizeToTray);
        }
        private void SyncAll()
        {
            loading = true;
            try
            {
                foreach (var update in sync) update();
                var style = settings.Style;
                shape.SelectedIndex = style.EnableCircleCrosshair ? 2 : style.EnableTShape ? 1 : 0;
                dot.Checked = style.EnableDot; round.Checked = style.DotIsCircle;
                pixel.Checked = style.PixelSnap;
                colorHex.Text = style.Color; outlineHex.Text = style.OutlineColor; scaleWithDpi.Checked = settings.ScaleWithDpi;
                offsetX.Value = settings.OffsetX; offsetY.Value = settings.OffsetY; csHeight.Value = settings.Cs2Height;
                shortcutInput.Text = pendingShortcut.Label; triggerMode.SelectedIndex = pendingShortcut.OnPress ? 0 : 1;
                allowOtherKeys.Checked = pendingShortcut.AllowOtherKeys;
                previewBg.SelectedIndex = Math.Max(0, previewBg.Items.IndexOf(settings.PreviewBackground));
                visibility.Text = settings.Enabled ? "● 已固定 · 点击隐藏" : "○ 已隐藏 · 点击显示";
                visibility.BackColor = settings.Enabled ? Theme.Accent : Theme.Field; visibility.ForeColor = settings.Enabled ? Theme.Background : Theme.Text;
                editButton.Text = overlay.Editing ? "完成位置调整" : "拖动调整位置";
                imageMode.Checked = settings.ImageMode;
                imageEditButton.Text = overlay.Editing ? "完成图片调整" : "在屏幕上调整";
                imageEditButton.Enabled = settings.ImageMode && image.Picture != null;
                imageInfo.Text = image.Picture == null ? (string.IsNullOrEmpty(settings.Image.File) ? "尚未选择图片" : "图片不可用，请重新选择：" + settings.Image.Name) : settings.Image.Name;
                var imageSize = image.DisplaySize(settings.Image, overlay.RenderScale);
                imageDimensions.Text = image.Picture == null ? "" : "原图 " + image.Picture.Width + " × " + image.Picture.Height + " px  ·  屏幕 " + imageSize.Width + " × " + imageSize.Height + " px";
                preview.Image = image; preview.ImageStyle = settings.Image; preview.ImageMode = settings.ImageMode;
                shortcutLabel.Text = settings.Shortcut.Label + "\n" + settings.Shortcut.TriggerLabel; preview.Style = style; preview.RenderScale = overlay.RenderScale; preview.Surface = settings.PreviewBackground; preview.Invalidate();
                exportText.Text = settings.ImageMode ? "图片准星请使用预设保存。MBCH 分享码与样式 JSON 仅支持形状准星。\r\n迁移图片准星时，请复制 images 文件夹和 crosshair-settings.json。" : ShareCodes.Export(style);
                if (tray != null)
                {
                    string trayText = "星准 · " + (settings.Enabled ? "显示" : "隐藏") + " · " + settings.Shortcut.Label + " · " + settings.Shortcut.TriggerLabel;
                    tray.Text = trayText.Length > 63 ? trayText.Substring(0, 62) + "…" : trayText;
                }
            }
            finally { loading = false; }
        }
        private void Changed() { SyncAll(); overlay.RefreshOverlay(); MarkDirty(); }
        private void MarkDirty() { dirty = true; saveTimer.Stop(); saveTimer.Start(); }
        private void Flush()
        {
            if (!dirty) return;
            try { store.Save(settings); dirty = false; SetStatus("已自动保存 · " + (settings.Enabled ? "准星已固定" : "准星已隐藏"), false); }
            catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); }
        }
        private void SetStatus(string text, bool error) { status.Text = text; status.ForeColor = error ? Color.Salmon : Theme.Muted; }
        private void Run(Action action) { try { action(); } catch (Exception ex) { SetStatus(ex.Message, true); } }
        private void ToggleVisibility()
        {
            overlay.SetEditing(false); settings.Enabled = !settings.Enabled; Changed();
            SetStatus(settings.Enabled ? "已固定 · 准星显示" : "已取消固定 · 准星隐藏", false);
        }
        private void MinimizeToTray()
        {
            overlay.SetEditing(false); SyncAll();
            tray.Visible = true;
            Hide(); Flush();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.BalloonTipTitle = "星准正在后台运行";
                tray.BalloonTipText = "单击右下角的星准图标可打开设置。图标也可能在“显示隐藏的图标”中；再次双击程序同样可以恢复窗口。";
                tray.ShowBalloonTip(5000);
            }
        }
        private void OpenSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x8001) { OpenSettings(); return; } base.WndProc(ref m); }
        private void CenterCrosshair() { settings.OffsetX = settings.OffsetY = 0; Changed(); }
        private void Nudge(int x, int y)
        {
            int step = (ModifierKeys & Keys.Shift) != 0 ? 10 : 1;
            settings.OffsetX = Math.Max(-16000, Math.Min(16000, settings.OffsetX + x * step)); settings.OffsetY = Math.Max(-16000, Math.Min(16000, settings.OffsetY + y * step)); Changed();
        }
        private void ChooseColor(bool outline)
        {
            using (var dialog = new ColorDialog { FullOpen = true, Color = CrosshairStyle.ParseColor(outline ? settings.Style.OutlineColor : settings.Style.Color) })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string hex = string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B);
                    if (outline) settings.Style.OutlineColor = hex; else settings.Style.Color = hex;
                    Changed();
                }
        }
        private void ApplyHex(bool outline)
        {
            if (loading) return;
            Run(() =>
            {
                string value = (outline ? outlineHex.Text : colorHex.Text).Trim(); var color = CrosshairStyle.ParseColor(value);
                if (outline) settings.Style.OutlineColor = "#" + unchecked((uint)color.ToArgb()).ToString("X8");
                else settings.Style.Color = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
                Changed();
            });
        }
        private void Import(string text)
        {
            Run(() => { string note; var imported = ShareCodes.Import(text, settings.Cs2Height, overlay.RenderScale, out note); settings.Style = imported; settings.ImageMode = false; Changed(); SetStatus(note, false); });
        }
        private void ImportFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "准星设置|*.json;*.txt|所有文件|*.*", Title = "导入准星样式" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Run(() => { if (new FileInfo(dialog.FileName).Length > 65536) throw new FormatException("样式文件过大。"); importText.Text = File.ReadAllText(dialog.FileName); Import(importText.Text); });
        }
        private void RequireShape()
        {
            if (settings.ImageMode) throw new FormatException("MBCH 分享码仅支持形状准星；图片请保存为预设。");
        }
        private void ExportFile()
        {
            if (settings.ImageMode) { SetStatus("图片准星请保存为预设；分享时复制 images 文件夹和设置文件。", true); return; }
            using (var dialog = new SaveFileDialog { Filter = "准星 JSON|*.json", FileName = "crosshair-style.json", Title = "导出准星样式" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Run(() => { File.WriteAllText(dialog.FileName, ShareCodes.ExportJson(settings.Style)); SetStatus("样式已导出。", false); });
        }
        private void SavePreset(bool overwrite)
        {
            Run(() =>
            {
                if (settings.ImageMode && image.Picture == null) throw new FormatException("请先重新选择准星图片。");
                var selected = presets.SelectedItem as SavedPreset;
                string name = overwrite && selected != null ? selected.Name : presetName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "预设 " + (settings.Presets.Count + 1);
                var existing = settings.Presets.Find(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
                if (existing != null && !overwrite) throw new FormatException("已有同名预设，请改名或选择覆盖。");
                if (overwrite && selected == null) throw new FormatException("请先选择要覆盖的预设。");
                if (existing == null)
                {
                    if (settings.Presets.Count >= 100) throw new FormatException("最多保存 100 个预设。");
                    existing = new SavedPreset { Name = name }; settings.Presets.Add(existing);
                }
                existing.Code = settings.ImageMode ? "" : ShareCodes.Export(settings.Style);
                existing.Image = settings.ImageMode ? settings.Image.Copy() : null;
                RefreshPresetList(name); MarkDirty(); SetStatus("预设已保存：" + name, false);
            });
        }
        private void DeletePreset()
        {
            var selected = presets.SelectedItem as SavedPreset;
            if (selected == null) { SetStatus("请先选择要删除的预设。", true); return; }
            if (MessageBox.Show(this, "删除预设“" + selected.Name + "”？", "删除预设", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.Presets.Remove(selected); RefreshPresetList(null); MarkDirty();
        }
        private void RefreshPresetList(string selected)
        {
            loading = true; presets.Items.Clear();
            foreach (var preset in settings.Presets) { presets.Items.Add(preset); if (preset.Name == selected) presets.SelectedItem = preset; }
            loading = false;
        }
        private void FillMonitors()
        {
            loading = true; monitors.Items.Clear(); var list = MonitorChoice.All();
            overlay.Monitor = list.Find(m => m.Device == settings.Monitor) ?? list.Find(m => m.Primary) ?? list[0];
            foreach (var choice in list) { monitors.Items.Add(choice); if (choice.Device == overlay.Monitor.Device) monitors.SelectedItem = choice; }
            loading = false;
        }
        private void DisplaysChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { FillMonitors(); settings.OffsetX = settings.OffsetY = 0; Changed(); }));
        }
    }
}
