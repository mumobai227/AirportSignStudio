using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class PreviewCanvas : Control
    {
        public SignDocument Document;
        public int BackgroundMode;
        public PreviewCanvas()
        {
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.FromArgb(239, 239, 236);
            AccessibleName = "标志贴图预览";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Color baseColor = BackgroundMode == 1 ? Color.FromArgb(48, 48, 46) : Color.FromArgb(239, 239, 236);
            g.Clear(baseColor);
            if (BackgroundMode == 0)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(231, 231, 228)))
                    for (int y = 0; y < Height; y += 20) for (int x = 0; x < Width; x += 20)
                        if ((x / 20 + y / 20) % 2 == 0) g.FillRectangle(b, x, y, 20, 20);
            }
            Color lineColor = BackgroundMode == 1 ? Color.FromArgb(184, 184, 178) : StudioTheme.Muted;
            using (Font font = DpiLayout.Font(this, 9, FontStyle.Regular))
            using (Brush text = new SolidBrush(lineColor))
            {
                g.DrawString("正视图", font, text, 18, 15);
                if (Document == null)
                {
                    using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("填写或修正左侧内容后预览", font, text, ClientRectangle, center);
                    return;
                }
                float scale = Math.Min((Width - 120) / Document.Width, (Height - 134) / Document.Height);
                if (scale <= 0) return;
                float w = Document.Width * scale, h = Document.Height * scale;
                float x = (Width - w) / 2, y = (Height - h) / 2 - 4;
                GraphicsState state = g.Save();
                using (Matrix m = new Matrix(scale, 0, 0, scale, x, y)) g.Transform = m;
                Document.Draw(g); g.Restore(state);
                using (Pen pen = new Pen(lineColor))
                {
                    g.DrawLine(pen, x, y + h + 22, x + w, y + h + 22);
                    g.DrawLine(pen, x, y + h + 17, x, y + h + 27);
                    g.DrawLine(pen, x + w, y + h + 17, x + w, y + h + 27);
                    using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center })
                        g.DrawString((Document.Width / 1000).ToString("0.###") + " m", font, text, x + w / 2, y + h + 29, centered);
                    g.DrawLine(pen, x + w + 22, y, x + w + 22, y + h);
                    g.DrawLine(pen, x + w + 17, y, x + w + 27, y);
                    g.DrawLine(pen, x + w + 17, y + h, x + w + 27, y + h);
                    state = g.Save(); g.TranslateTransform(x + w + 28, y + h / 2); g.RotateTransform(-90);
                    using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center })
                        g.DrawString((Document.Height / 1000).ToString("0.###") + " m", font, text, 0, 0, centered);
                    g.Restore(state);
                }
            }
            StudioTheme.CanvasFrame(this, g);
        }
    }

    public sealed class MainForm : DpiForm
    {
        static readonly Color Ink = StudioTheme.Ink;
        static readonly Color Muted = StudioTheme.Muted;
        readonly GlyphLibrary glyphs = new GlyphLibrary();
        readonly SignEngine engine;
        readonly Dictionary<AssetKind, Settings> presets = new Dictionary<AssetKind, Settings>();
        readonly AtlasWorkspace atlas = new AtlasWorkspace();
        readonly ListBox navigation = new StudioNavigation();
        readonly FlowLayoutPanel editor = new FlowLayoutPanel();
        readonly PreviewCanvas preview = new PreviewCanvas();
        readonly Label title = new Label(), subtitle = new Label(), reference = new Label(), status = new Label(), exportInfo = new Label();
        readonly ComboBox resolution = new StudioComboBox();
        readonly CheckBox powerOfTwo = new CheckBox();
        readonly Button exportPng, exportSvg, openFolder;
        Settings current;
        SignDocument document;
        Label inputError;
        bool building;
        string lastDirectory = "";
        DataGridView panelGrid;
        Button atlasButton;

        [STAThread]
        public static void Main()
        {
#if NET10_0_OR_GREATER
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (GlyphLibrary library = new GlyphLibrary())
            using (AtlasForm form = new AtlasForm(new SignEngine(library), new AtlasWorkspace()))
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                Application.Run(form);
            }
        }

        public MainForm()
        {
            engine = new SignEngine(glyphs);
            Text = "机场标志工坊 3.24 · Airport Sign Studio";
            ForeColor = Ink;
            BackColor = StudioTheme.Surface;
            ClientSize = new Size(1380, 820); MinimumSize = new Size(1140, 700); StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            presets[AssetKind.GroundTaxi] = new Settings();
            presets[AssetKind.GroundStand] = new Settings { Kind = AssetKind.GroundStand, Text = "324", Arrow = "↑" };
            Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
            taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "B", Arrow = "←" });
            taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无" });
            taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "C2", Arrow = "↗" });
            presets[AssetKind.TaxiSign] = taxi;
            presets[AssetKind.StandSign] = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600 };
            presets[AssetKind.GroundOther] = new Settings { Kind = AssetKind.GroundOther, Text = "36", OtherStyle = "MAX SPAN" };

            status.Dock = DockStyle.Bottom; status.Height = 28; status.Padding = new Padding(18, 5, 8, 0);
            status.ForeColor = Muted; status.Font = new Font(Font.FontFamily, 8.5f); status.BackColor = StudioTheme.Sidebar;
            status.Text = "就绪";
            Panel body = new Panel { Dock = DockStyle.Fill };
            Controls.Add(body); Controls.Add(status);

            Panel nav = new Panel { Dock = DockStyle.Left, Width = 208, Padding = new Padding(8, 16, 8, 14), BackColor = StudioTheme.Sidebar };
            navigation.Dock = DockStyle.Top; navigation.Height = 210; navigation.IntegralHeight = false;
            navigation.AccessibleName = "标牌类型"; navigation.Items.AddRange(SlotEditor.KindNames);
            navigation.SelectedIndexChanged += delegate { if (!building && navigation.SelectedIndex >= 0) SelectKind((AssetKind)navigation.SelectedIndex); };
            nav.Controls.Add(navigation);
            nav.Controls.Add(new Label { Text = "标牌类型", Dock = DockStyle.Top, Height = 30, Padding = new Padding(16, 5, 0, 0), ForeColor = Muted, Font = new Font(Font.FontFamily, 9) });
            nav.Controls.Add(new Label { Text = "标牌工坊", Dock = DockStyle.Top, Height = 54, Padding = new Padding(15, 8, 0, 0), Font = new Font(Font.FontFamily, 12, FontStyle.Bold) });
            StudioButton help = MakeButton("使用说明"); help.Quiet = true; help.Dock = DockStyle.Bottom; help.Height = 34;
            help.Click += delegate { ShowHelp(); }; nav.Controls.Add(help);
            body.Controls.Add(nav);

            Panel work = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 18, 10) };
            body.Controls.Add(work); work.BringToFront();
            editor.Dock = DockStyle.Left; editor.Width = 316; editor.FlowDirection = FlowDirection.TopDown;
            editor.WrapContents = false; editor.AutoScroll = true; editor.Padding = new Padding(18, 10, 12, 12);
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 0, 0) };
            work.Controls.Add(right); work.Controls.Add(editor);

            Panel previewHeader = new Panel { Dock = DockStyle.Top, Height = 70 };
            title.Dock = DockStyle.Top; title.Height = 36; title.Padding = new Padding(0, 8, 0, 0); title.Font = new Font(Font.FontFamily, 12, FontStyle.Bold); title.AutoEllipsis = true;
            subtitle.Dock = DockStyle.Fill; subtitle.Padding = new Padding(0, 3, 0, 0); subtitle.ForeColor = Muted; subtitle.Font = new Font(Font.FontFamily, 9);
            previewHeader.Controls.Add(subtitle); previewHeader.Controls.Add(title);

            Panel export = new Panel { Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(0, 10, 0, 0) };
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
            options.Controls.Add(new Label { Text = "PNG 长边", AutoSize = true, Margin = new Padding(0, 6, 12, 0) });
            resolution.DropDownStyle = ComboBoxStyle.DropDownList; resolution.Width = 108;
            resolution.Items.AddRange(new object[] { "512 px", "1024 px", "2048 px", "4096 px" }); resolution.SelectedIndex = 2;
            resolution.SelectedIndexChanged += delegate { UpdateExportInfo(); }; options.Controls.Add(resolution);
            powerOfTwo.Text = "2 的幂画布"; powerOfTwo.Checked = true; powerOfTwo.AutoSize = true; powerOfTwo.Margin = new Padding(16, 5, 0, 0);
            powerOfTwo.CheckedChanged += delegate { UpdateExportInfo(); }; options.Controls.Add(powerOfTwo);
            exportInfo.Dock = DockStyle.Top; exportInfo.Height = 28; exportInfo.ForeColor = Muted; exportInfo.Font = new Font(Font.FontFamily, 9);
            exportInfo.TextAlign = ContentAlignment.MiddleLeft;
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false };
            exportPng = MakeButton("导出 PNG", true); exportPng.Size = new Size(116, 34); exportPng.Click += delegate { Export(false); };
            exportSvg = MakeButton("导出 SVG"); exportSvg.Size = new Size(112, 34); exportSvg.Click += delegate { Export(true); };
            openFolder = MakeButton("打开导出目录"); openFolder.Size = new Size(128, 34); openFolder.Enabled = false;
            openFolder.Click += delegate { if (Directory.Exists(lastDirectory)) Process.Start(new ProcessStartInfo(lastDirectory) { UseShellExecute = true }); };
            actions.Controls.Add(exportPng); actions.Controls.Add(exportSvg); actions.Controls.Add(openFolder);
            export.Controls.Add(exportInfo); export.Controls.Add(options); export.Controls.Add(actions);

            Panel referenceBar = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(0, 10, 0, 4) };
            ComboBox background = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 142, Dock = DockStyle.Right };
            background.Items.AddRange(new object[] { "预览：透明棋盘", "预览：深色道面", "预览：浅色道面" }); background.SelectedIndex = 0;
            background.SelectedIndexChanged += delegate { preview.BackgroundMode = background.SelectedIndex; preview.Invalidate(); };
            reference.Dock = DockStyle.Fill; reference.ForeColor = Muted; reference.Font = new Font(Font.FontFamily, 8.5f); reference.Padding = new Padding(0, 2, 10, 0);
            referenceBar.Controls.Add(reference); referenceBar.Controls.Add(background);
            preview.Dock = DockStyle.Fill;
            right.Controls.Add(preview); right.Controls.Add(referenceBar); right.Controls.Add(export); right.Controls.Add(previewHeader);
            SelectKind(AssetKind.GroundTaxi);
            CompleteLayout();
        }

        static StudioButton MakeButton(string text, bool primary = false)
        {
            return new StudioButton { Text = text, Primary = primary, Margin = new Padding(0, 0, 8, 0) };
        }

        void Field(string label, Control control, int height)
        {
            height = Math.Max(36, height);
            Panel group = new Panel { Width = 267, Height = height + 26, Margin = new Padding(0, 0, 0, 10) };
            group.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 24, ForeColor = Muted });
            StudioField field = new StudioField(control); field.SetBounds(0, 25, 267, height); group.Controls.Add(field); editor.Controls.Add(group);
            control.AccessibleName = label;
        }
        void Hint(string text, int height)
        {
            editor.Controls.Add(new Label { Text = text, Width = 267, Height = height, ForeColor = Muted, Font = DpiLayout.Font(this, 9, FontStyle.Regular), Margin = new Padding(0, 0, 0, 6) });
        }
        ComboBox Choice(string label, string[] values, string selected, Action<string> changed)
        {
            ComboBox combo = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(values); combo.SelectedItem = selected;
            combo.SelectedIndexChanged += delegate { if (!building) { changed((string)combo.SelectedItem); UpdatePreview(); } };
            Field(label, combo, 30); return combo;
        }
        void TextInput(string label, string value, Action<string> changed, bool large)
        {
            TextBox input = new TextBox { Text = value, MaxLength = 40, CharacterCasing = CharacterCasing.Upper, Font = DpiLayout.Font(this, large ? 14 : 9.5f, FontStyle.Regular) };
            input.TextChanged += delegate { if (!building) { changed(input.Text); UpdatePreview(); } };
            Field(label, input, large ? 34 : 28);
        }
        CheckBox Toggle(string label, bool value, Action<bool> changed)
        {
            CheckBox checkbox = new CheckBox { Text = label, Checked = value, Width = 267, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            checkbox.CheckedChanged += delegate { if (!building) { changed(checkbox.Checked); UpdatePreview(); } };
            editor.Controls.Add(checkbox); return checkbox;
        }

        void NumberInput(string label, float value, float minimum, float maximum, Action<float> changed)
        {
            NumericUpDown input = new NumericUpDown { Minimum = (decimal)minimum, Maximum = (decimal)maximum,
                DecimalPlaces = 0, Increment = 10, Value = (decimal)value, ThousandsSeparator = true };
            input.ValueChanged += delegate { if (!building) { changed((float)input.Value); UpdatePreview(); } };
            Field(label, input, 30);
        }

        void SelectKind(AssetKind kind)
        {
            building = true; editor.SuspendLayout(); current = presets[kind];
            foreach (Control control in editor.Controls.Cast<Control>().ToArray()) control.Dispose();
            editor.Controls.Clear(); panelGrid = null;
            navigation.SelectedIndex = (int)kind;
            FlowLayoutPanel atlasActions = new FlowLayoutPanel { Width = 267, Height = 40, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            Button addAtlas = MakeButton("放入当前标牌"); addAtlas.Size = new Size(130, 34);
            atlasButton = MakeButton("拼图编辑器"); atlasButton.Size = new Size(121, 34);
            addAtlas.Click += delegate
            {
                try
                {
                    if (panelGrid != null) { panelGrid.EndEdit(); ReadPanels(); }
                    using (engine.Build(current)) { }
                    atlas.PutCurrent(current);
                    using (AtlasForm form = new AtlasForm(engine, atlas)) form.ShowDialog(this);
                    status.Text = "混合拼图：" + atlas.Filled + " / " + atlas.RegionCount + " 区已设置。";
                }
                catch (ArgumentException e) { MessageBox.Show(this, e.Message, "无法加入拼图"); }
            };
            atlasButton.Click += delegate
            {
                using (AtlasForm form = new AtlasForm(engine, atlas)) form.ShowDialog(this);
                status.Text = "混合拼图：" + atlas.Filled + " / " + atlas.RegionCount + " 区已设置。";
            };
            atlasActions.Controls.Add(atlasButton); atlasActions.Controls.Add(addAtlas); editor.Controls.Add(atlasActions);
            Hint("内容与版式", 25);
            if (kind == AssetKind.GroundOther)
                Choice("地面标志类型", new[] { "MAX SPAN", "HP", "NO ENTRY", "机型文字" }, current.OtherStyle, v =>
                {
                    current.OtherStyle = v; current.Text = v == "MAX SPAN" ? "36" : v == "HP" ? "4" : v == "NO ENTRY" ? "NO ENTRY" : "A320/B737";
                    current.CharacterHeight = v == "机型文字" ? 300 : 4000; current.Width = current.Height = 0;
                    current.Border = v != "HP"; current.BlackBackground = v != "机型文字";
                    BeginInvoke(new Action(() => SelectKind(kind)));
                });
            if (kind == AssetKind.TaxiSign) BuildPanelsEditor();
            else if (kind != AssetKind.GroundOther || current.OtherStyle != "NO ENTRY")
                TextInput(kind == AssetKind.GroundOther ? (current.OtherStyle == "MAX SPAN" ? "最大翼展 / m" : current.OtherStyle == "HP" ? "HP 编号（例：4）" : "机型文字") : kind == AssetKind.GroundTaxi ? "标志内容" : "机位编号", current.Text, v => current.Text = v, true);

            if (kind == AssetKind.GroundTaxi)
            {
                Choice("标志类型", new[] { "方向", "位置", "强制指令" }, current.GroundStyle, v => { current.GroundStyle = v; BeginInvoke(new Action(() => SelectKind(kind))); });
                if (current.GroundStyle == "方向")
                {
                    Choice("箭头方向", new[] { "←", "↖", "↑", "↗", "→" }, current.Arrow, v => current.Arrow = v);
                    Toggle("箭头放在文字上方", current.ArrowAbove, v => current.ArrowAbove = v);
                }
            }
            if (kind == AssetKind.GroundStand)
                Choice("底板朝向", new[] { "↑", "→", "←", "无" }, current.Arrow, v => current.Arrow = v);

            if (kind == AssetKind.GroundTaxi || kind == AssetKind.GroundStand)
            {
                Choice("实际字高", new[] { "4.0 m", "2.0 m（受限尺寸）" }, current.CharacterHeight == 4000 ? "4.0 m" : "2.0 m（受限尺寸）", v => current.CharacterHeight = v.StartsWith("4") ? 4000 : 2000);
                NumericUpDown gap = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.02m, Maximum = 0.50m, Increment = 0.01m, Value = (decimal)current.GroundGap };
                gap.ValueChanged += delegate { if (!building) { current.GroundGap = (float)gap.Value; UpdatePreview(); } };
                Field("字间净距 / 字高", gap, 30);
                Toggle("增加对比边框（0.10 m）", current.Border, v => current.Border = v);
                Hint(kind == AssetKind.GroundStand ? "保留地面拉长字形。底板尺寸可在下方独立设置；左右箭头不会旋转文字。" : "地面文字与立牌采用不同宽高比例。字间净距为可调整的制作参数。", 62);
            }
            if (kind == AssetKind.TaxiSign)
            {
                Choice("实际字高", new[] { "200 mm", "300 mm", "400 mm" }, current.CharacterHeight.ToString("0") + " mm", v => current.CharacterHeight = float.Parse(v.Split(' ')[0]));
                Hint("方向箭头自动放在文字左侧或右侧。相邻方向牌自动加黑色分隔线。", 58);
                Hint("200 / 300 / 400 mm 按服务对象选择。强制性指令牌：飞行区指标 I 为 1/2 时至少 300 mm，3/4 时至少 400 mm。", 93);
            }
            if (kind == AssetKind.StandSign)
            {
                Choice("机位号码实际字高", new[] { "400 mm（受限）", "600 mm", "800 mm", "1000 mm" }, current.CharacterHeight == 400 ? "400 mm（受限）" : current.CharacterHeight.ToString("0") + " mm", v => current.CharacterHeight = float.Parse(v.Split(' ')[0]));
                Toggle("在号码下方显示经纬度", current.Coordinates, v => { current.Coordinates = v; BeginInvoke(new Action(() => SelectKind(kind))); });
                if (current.Coordinates)
                {
                    TextInput("纬度行（例：N 52 30.2）", current.Latitude, v => current.Latitude = v, false);
                    TextInput("经度行（例：E 114 25.1）", current.Longitude, v => current.Longitude = v, false);
                    Hint("经纬度需自行填写。按牌面文本排版，不转换或推算坐标。", 57);
                }
                Hint("采用附录 H 牌面字形。经纬度字高固定为主号码的 1/4；牌面自动扩展以容纳内容。", 65);
            }
            if (kind == AssetKind.GroundOther)
            {
                if (current.OtherStyle == "机型文字")
                    NumberInput("机型文字字高 / mm（参考 150–300）", current.CharacterHeight, 100, 8000, v => current.CharacterHeight = v);
                else Choice("实际字高", new[] { "4.0 m", "2.0 m（受限尺寸）" }, current.CharacterHeight == 4000 ? "4.0 m" : "2.0 m（受限尺寸）", v => current.CharacterHeight = v.StartsWith("4") ? 4000 : 2000);
                NumericUpDown letterGap = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.02m, Maximum = 0.50m, Increment = 0.01m, Value = (decimal)current.GroundGap };
                letterGap.ValueChanged += delegate { if (!building) { current.GroundGap = (float)letterGap.Value; UpdatePreview(); } };
                Field("字间净距 / 字高", letterGap, 30);
                if (current.OtherStyle == "MAX SPAN" || current.OtherStyle == "机型文字") Toggle("黑色背景（取消为透明）", current.BlackBackground, v => current.BlackBackground = v);
                if (current.OtherStyle != "机型文字") Toggle("增加对比边框", current.Border, v => current.Border = v);
                Hint(current.OtherStyle == "MAX SPAN" ? "两行排版：MAX SPAN 与翼展值。米单位使用小写 m。" : current.OtherStyle == "HP" ? "黑底黄字，自动在编号前加 HP。" : current.OtherStyle == "NO ENTRY" ? "固定 NO ENTRY 文字，红底白字。" : "黄色机型文字。浅色道面可使用黑底；透明背景只输出字形。", 62);
            }
            Button colors = MakeButton("当前标牌颜色…"); colors.Size = new Size(267, 34);
            colors.Click += delegate
            {
                using (ColorsForm dialog = new ColorsForm(current.Colors, true, value => { current.Colors = value; UpdatePreview(); })) dialog.ShowDialog(this);
            };
            editor.Controls.Add(colors);
            Hint("自由尺寸 · 0 = 自动", 25);
            if (kind != AssetKind.TaxiSign) NumberInput("整体宽度 / mm", current.Width, 0, 200000, v => current.Width = v);
            NumberInput("整体高度 / mm", current.Height, 0, 200000, v => current.Height = v);
            Hint("调整底板和留白，字形与字高保持不变。尺寸过小会提示所需最小值。组合牌可逐段设置宽度。", 65);
            inputError = new Label { Width = 267, Height = 108, ForeColor = Color.FromArgb(177, 54, 41), Font = DpiLayout.Font(this, 9, FontStyle.Regular), Margin = new Padding(0, 4, 0, 0) };
            editor.Controls.Add(inputError);
            DpiLayout.ScaleNewChildren(editor); editor.ResumeLayout(true);
            building = false; UpdatePreview();
        }

        void BuildPanelsEditor()
        {
            panelGrid = new DataGridView { Width = 267, Height = 226, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToResizeRows = false, EnableHeadersVisualStyles = false };
            panelGrid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            panelGrid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            panelGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            panelGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
            panelGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells; panelGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            DataGridViewComboBoxColumn kind = new DataGridViewComboBoxColumn { HeaderText = "类型", FillWeight = 36, FlatStyle = FlatStyle.Flat };
            kind.Items.AddRange("位置", "方向", "强制指令"); panelGrid.Columns.Add(kind);
            panelGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "内容", FillWeight = 39 });
            DataGridViewComboBoxColumn arrow = new DataGridViewComboBoxColumn { HeaderText = "箭头", FillWeight = 25, FlatStyle = FlatStyle.Flat };
            arrow.Items.AddRange("无", "←", "↖", "↑", "↗", "→", "↘", "↓", "↙"); panelGrid.Columns.Add(arrow);
            foreach (DataGridViewColumn column in panelGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (SignPanel p in current.Panels) panelGrid.Rows.Add(p.Kind, p.Text, p.Arrow);
            panelGrid.CellValueChanged += delegate { if (!building) { ReadPanels(); UpdatePreview(); } };
            panelGrid.CurrentCellDirtyStateChanged += delegate { if (panelGrid.IsCurrentCellDirty) panelGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            panelGrid.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                TextBox box = e.Control as TextBox;
                if (box != null) { box.MaxLength = 40; box.CharacterCasing = CharacterCasing.Upper; box.TextChanged -= PanelTextChanged; box.TextChanged += PanelTextChanged; }
            };
            panelGrid.AccessibleName = "组合牌面列表"; editor.Controls.Add(panelGrid);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Width = 267, Height = 43, WrapContents = false, Margin = new Padding(0, 7, 0, 12) };
            string[] texts = { "+ 添加", "移除", "上移", "下移" };
            for (int i = 0; i < 4; i++)
            {
                int command = i; Button b = MakeButton(texts[i]); b.Size = new Size(62, 28); b.Margin = new Padding(0, 0, 5, 0);
                b.Click += delegate { ChangePanel(command); }; buttons.Controls.Add(b);
            }
            editor.Controls.Add(buttons);
            NumericUpDown segmentWidth = new NumericUpDown { Minimum = 0, Maximum = 200000, Increment = 10, ThousandsSeparator = true };
            bool selectingWidth = false;
            Action refreshWidth = delegate
            {
                selectingWidth = true;
                int index = panelGrid.CurrentCell == null ? -1 : panelGrid.CurrentCell.RowIndex;
                segmentWidth.Enabled = index >= 0 && index < current.Panels.Count;
                segmentWidth.Value = segmentWidth.Enabled ? (decimal)current.Panels[index].Width : 0;
                selectingWidth = false;
            };
            panelGrid.SelectionChanged += delegate { refreshWidth(); };
            segmentWidth.ValueChanged += delegate
            {
                if (building || selectingWidth || panelGrid.CurrentCell == null) return;
                current.Panels[panelGrid.CurrentCell.RowIndex].Width = (float)segmentWidth.Value; UpdatePreview();
            };
            Field("所选分段宽度 / mm（0 = 自动）", segmentWidth, 30); refreshWidth();
        }
        void PanelTextChanged(object sender, EventArgs e)
        {
            if (building || panelGrid.CurrentCell == null || panelGrid.CurrentCell.ColumnIndex != 1) return;
            int index = panelGrid.CurrentCell.RowIndex;
            if (index < current.Panels.Count) { current.Panels[index].Text = ((TextBox)sender).Text; UpdatePreview(); }
        }
        void ReadPanels()
        {
            float[] widths = current.Panels.Select(p => p.Width).ToArray();
            current.Panels.Clear();
            foreach (DataGridViewRow row in panelGrid.Rows)
                current.Panels.Add(new SignPanel { Kind = Convert.ToString(row.Cells[0].Value), Text = Convert.ToString(row.Cells[1].Value), Arrow = Convert.ToString(row.Cells[2].Value), Width = row.Index < widths.Length ? widths[row.Index] : 0 });
        }
        void ChangePanel(int command)
        {
            panelGrid.EndEdit(); ReadPanels();
            int selected = panelGrid.CurrentCell == null ? -1 : panelGrid.CurrentCell.RowIndex;
            if (command == 0 && current.Panels.Count < 6) { current.Panels.Add(new SignPanel()); selected = current.Panels.Count - 1; }
            else if (command == 1 && selected >= 0) { current.Panels.RemoveAt(selected); selected = Math.Min(selected, current.Panels.Count - 1); }
            else if (command == 2 && selected > 0)
            { SignPanel p = current.Panels[selected]; current.Panels.RemoveAt(selected); current.Panels.Insert(--selected, p); }
            else if (command == 3 && selected >= 0 && selected < current.Panels.Count - 1)
            { SignPanel p = current.Panels[selected]; current.Panels.RemoveAt(selected); current.Panels.Insert(++selected, p); }
            SelectKind(AssetKind.TaxiSign);
            if (selected >= 0 && selected < panelGrid.Rows.Count) panelGrid.CurrentCell = panelGrid.Rows[selected].Cells[1];
        }

        void UpdatePreview()
        {
            if (building) return;
            preview.Document = null;
            if (document != null) { document.Dispose(); document = null; }
            inputError.Text = "";
            try
            {
                document = engine.Build(current); preview.Document = document;
                string[] types = { "滑行道地面标志", "地面机位编号", "滑行道指示牌", "机位号码标记牌", "其他地面标志" };
                title.Text = types[(int)current.Kind] + "  /  " + document.Caption;
                subtitle.Text = "图案尺寸 " + (document.Width / 1000).ToString("0.###") + " × " + (document.Height / 1000).ToString("0.###") + " m     ·     字高 " + (current.CharacterHeight / 1000).ToString("0.###") + " m";
                reference.Text = "MH 5001—2021  ·  " + document.Reference + "\n预览底色不导出。SVG 保留毫米尺寸与字形轮廓。";
                if (current.Kind == AssetKind.StandSign && current.CharacterHeight < 600) inputError.Text = document.Note;
                if (current.Kind == AssetKind.GroundTaxi && current.GroundStyle != "强制指令" && current.Text.ToUpperInvariant().IndexOfAny(new[] { 'I', 'O', 'X' }) >= 0)
                    inputError.Text = "6.1.2：滑行道编号不应使用 I、O、X。请核对当前文字用途。";
                exportPng.Enabled = true; exportSvg.Enabled = true;
            }
            catch (ArgumentException error)
            {
                inputError.Text = error.Message; title.Text = "等待有效内容"; subtitle.Text = "请检查左侧输入";
                reference.Text = "内容修正后会自动恢复预览。"; exportPng.Enabled = false; exportSvg.Enabled = false;
            }
            UpdateExportInfo(); preview.Invalidate();
        }
        int Resolution { get { return new[] { 512, 1024, 2048, 4096 }[Math.Max(0, resolution.SelectedIndex)]; } }
        void UpdateExportInfo()
        {
            if (document == null) { exportInfo.Text = "PNG：RGBA 透明通道  ·  SVG：矢量轮廓"; return; }
            ExportSize size = new ExportSize(document, Resolution, powerOfTwo.Checked, 16);
            exportInfo.Text = "PNG " + size.Width + " × " + size.Height + " px  ·  四周至少 16 px 透明留白  ·  保持比例";
        }
        void Export(bool svg)
        {
            if (document == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = svg ? "导出 SVG 轮廓" : "导出 PNG 贴图";
                dialog.Filter = svg ? "SVG 矢量文件 (*.svg)|*.svg" : "PNG 透明贴图 (*.png)|*.png";
                dialog.DefaultExt = svg ? "svg" : "png"; dialog.AddExtension = true;
                dialog.FileName = current.Kind + "_" + Exporter.SafeName(document.Caption) + (svg ? ".svg" : "_" + Resolution + ".png");
                if (Directory.Exists(lastDirectory)) dialog.InitialDirectory = lastDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true;
                    if (svg) File.WriteAllText(dialog.FileName, Exporter.Svg(document), new UTF8Encoding(false));
                    else using (Bitmap image = Exporter.Raster(document, Resolution, powerOfTwo.Checked, 16)) image.Save(dialog.FileName, ImageFormat.Png);
                    lastDirectory = Path.GetDirectoryName(dialog.FileName); openFolder.Enabled = true;
                    status.Text = "已导出：" + dialog.FileName;
                }
                catch (Exception error) { MessageBox.Show(this, "未能保存文件：\n" + error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { UseWaitCursor = false; }
            }
        }
        void ShowHelp()
        {
            MessageBox.Show(this,
                "选择素材类型 → 输入内容 → 调整版式 → 导出 PNG 或 SVG。\n\n" +
                "地面字形：图 6.2.12-3；滑行道地面标志：6.2.13；地面机位编号：6.2.16。\n" +
                "滑行引导牌：第 8 章与附录 H。机位号码牌：8.5 与图 8.5.6。\n\n" +
                "v2 按图样重绘低节点矢量轮廓，以直线和连续曲线消除网格造成的波纹。仍是图样重绘，不是官方字体母版。字符宽度和配对字距按表 H.0.9。\n\n" +
                "自由尺寸：0 表示自动；设置宽高只改变底板和留白。组合牌可为每段设置独立宽度。\n\n" +
                "拼图编辑器：点击区域配置标牌；Ctrl 或勾选多选后选择相邻区域，合并成矩形。拖动分隔线可局部调整边界，Esc 取消本次拖动。合并显示主选内容，其余内容拆分后恢复。改行列或恢复均分会重建网格并保留全部内容。参考字高默认 128 px，可调整。\n\n" +
                "拼图窗口顶部保存 / 读取 .assproj 工程，保留所有区域内容、合并、边界、分辨率、POL 和油漆设置。单块编辑先用“放入当前标牌”加入工程。退出程序时提醒保存。底部“油漆 / 磨损…”可选四套 PSD 材质预设，调节刷纹、污渍、剥落、褪色、大小、种子和方向；“使用参考暖黄”沿用样例 #FFAB09，取消则保留原标志色。效果统一应用于当前拼图并烘焙到 PNG，SVG 保留干净轮廓。设置总 POL 名称和 layer 后，直接向文件夹导出一张 PNG 和一个包含全部分区的总 POL。实际图案外沿默认各留 2 px 出血，可调 0–32 px，最多到所在格边界。在 WED 预览中点选分区并启用 Use Texture Map。\n\n" +
                "PNG 只导出图案，保留透明通道和至少 16 px 留白；2 的幂画布通过补透明边实现，不拉伸。SVG 全部为轮廓，尺寸单位为 mm。\n\n" +
                "显示尺寸是图案本身的尺寸，不包括 PNG 透明留白。模型贴图请按透明留白调整 UV。\n\n" +
                "本版用于机场场景资产制作；sRGB 配色不代表材料色度或发光亮度检验。未涵盖真实机场的安装、选址及全部组合约束。",
                "版式依据与使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ProjectDialogs.ConfirmSave(this, atlas)) e.Cancel = true;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (document != null) document.Dispose(); glyphs.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
