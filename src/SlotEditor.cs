using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class SlotEditor : FlowLayoutPanel
    {
        public static readonly string[] KindNames = { "滑行道地面标志", "地面机位编号", "滑行道指示牌", "机位号码标记牌", "其他地面标志" };
        public Action<Settings> Changed;
        public Action EditStarted, EditEnded;
        public Func<Settings, ColorSettings> InheritedColors;
        Settings settings;
        bool building, rebuildPending;

        public SlotEditor()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true;
            Padding = new Padding(18, 8, 14, 10); BackColor = StudioTheme.Sidebar;
        }

        public void LoadSlot(Settings value)
        {
            settings = value; Rebuild();
        }

        static Settings Default(AssetKind kind)
        {
            Settings value = new Settings { Kind = kind };
            if (kind == AssetKind.GroundStand) { value.Text = "21"; value.Arrow = "↑"; }
            if (kind == AssetKind.TaxiSign) { value.CharacterHeight = 300; value.Panels.Add(new SignPanel()); }
            if (kind == AssetKind.StandSign) { value.Text = "23"; value.CharacterHeight = 600; }
            if (kind == AssetKind.GroundOther) value.Text = "36";
            return value;
        }

        void Notify() { if (!building && Changed != null) Changed(settings); }
        void Later(bool keepScroll = false)
        {
            if (rebuildPending) return;
            rebuildPending = true;
            BeginInvoke(new Action(delegate
            {
                rebuildPending = false;
                if (IsDisposed) return;
                Point scroll = AutoScrollPosition;
                Rebuild();
                if (keepScroll) AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            }));
        }
        void Hint(string text, int height)
        {
            Controls.Add(new Label { Text = text, Width = 308, Height = height, ForeColor = StudioTheme.Muted, Margin = new Padding(0, 3, 0, 8) });
        }
        void Field(string title, Control control, int height)
        {
            height = Math.Max(36, height);
            Panel group = new Panel { Width = 308, Height = height + 26, Margin = new Padding(0, 0, 0, 9) };
            group.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = StudioTheme.Muted });
            StudioField field = new StudioField(control); field.SetBounds(0, 25, 308, height); control.AccessibleName = "槽位：" + title;
            group.Controls.Add(field); Controls.Add(group);
        }
        void Choice(string title, string[] values, string selected, Action<string> setter)
        {
            ComboBox input = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            input.Items.AddRange(values); input.SelectedItem = selected;
            input.SelectedIndexChanged += delegate { if (!building) { setter(Convert.ToString(input.SelectedItem)); Notify(); } };
            Field(title, input, 29);
        }
        void TextInput(string title, string value, Action<string> setter)
        {
            TextBox input = new TextBox { Text = value, MaxLength = 40, CharacterCasing = CharacterCasing.Upper };
            input.TextChanged += delegate { if (!building) { setter(input.Text); Notify(); } };
            Field(title, input, 28);
        }
        void Number(string title, float value, float min, float max, int decimals, Action<float> setter)
        {
            NumericUpDown input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = decimals,
                Increment = decimals == 0 ? 10 : 0.01m, ThousandsSeparator = decimals == 0, Value = (decimal)value };
            input.ValueChanged += delegate { if (!building) { setter((float)input.Value); Notify(); } };
            Field(title, input, 29);
        }
        void Toggle(string title, bool value, Action<bool> setter)
        {
            CheckBox input = new CheckBox { Text = title, Checked = value, Width = 308, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            input.CheckedChanged += delegate { if (!building) { setter(input.Checked); Notify(); } }; Controls.Add(input);
        }

        void Rebuild()
        {
            building = true; SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToArray()) c.Dispose();
            Controls.Clear(); AutoScrollPosition = Point.Empty;
            Choice("标牌类型", new[] { "空白槽位" }.Concat(KindNames).ToArray(), settings == null ? "空白槽位" : KindNames[(int)settings.Kind], v =>
            { settings = v == "空白槽位" ? null : Default((AssetKind)Array.IndexOf(KindNames, v)); Later(); });
            if (settings == null)
            {
                DpiLayout.ScaleNewChildren(this); ResumeLayout(true); building = false; return;
            }
            Settings s = settings;
            Button colors = new StudioButton { Text = "当前标牌颜色…", Width = 308, Height = 34, Margin = new Padding(0, 0, 0, 10) };
            colors.Click += delegate
            {
                if (EditStarted != null) EditStarted();
                try { using (ColorsForm dialog = new ColorsForm(s.Colors, true, value => { s.Colors = value; Notify(); }, InheritedColors == null ? null : InheritedColors(s))) dialog.ShowDialog(FindForm()); }
                finally { if (EditEnded != null) EditEnded(); }
            };
            Controls.Add(colors);
            if (s.Kind == AssetKind.GroundOther)
                Choice("地面标志类型", new[] { "MAX SPAN", "HP", "NO ENTRY", "机型文字" }, s.OtherStyle, v =>
                {
                    s.OtherStyle = v; s.Text = v == "MAX SPAN" ? "36" : v == "HP" ? "4" : v == "NO ENTRY" ? "NO ENTRY" : "A320/B737";
                    s.CharacterHeight = v == "机型文字" ? 300 : 4000; s.Border = v != "HP"; s.BlackBackground = v != "机型文字"; s.Width = s.Height = 0;
                    if (s.AspectRatio != null) s.AspectRatio = "";
                    Later();
                });
            if (s.Kind == AssetKind.TaxiSign) Panels(s);
            else if (s.Kind != AssetKind.GroundOther || s.OtherStyle != "NO ENTRY")
                TextInput(s.Kind == AssetKind.GroundOther && s.OtherStyle == "MAX SPAN" ? "翼展数值 / m" : "内容", s.Text, v => s.Text = v);
            if (s.Kind == AssetKind.GroundTaxi)
            {
                Choice("标志类型", new[] { "方向", "位置", "强制指令" }, s.GroundStyle, v => { s.GroundStyle = v; Later(); });
                if (s.GroundStyle == "方向")
                {
                    Choice("箭头", new[] { "←", "↖", "↑", "↗", "→" }, s.Arrow, v => s.Arrow = v);
                    Toggle("箭头在文字上方", s.ArrowAbove, v => s.ArrowAbove = v);
                }
            }
            if (s.Kind == AssetKind.GroundStand) Choice("底板朝向", new[] { "↑", "←", "→", "无" }, s.Arrow, v => s.Arrow = v);
            if (s.Kind == AssetKind.TaxiSign)
                Choice("字高 / mm", new[] { "200", "300", "400" }, s.CharacterHeight.ToString("0"), v => s.CharacterHeight = float.Parse(v));
            else if (s.Kind == AssetKind.StandSign)
            {
                Choice("字高 / mm", new[] { "400", "600", "800", "1000" }, s.CharacterHeight.ToString("0"), v => s.CharacterHeight = float.Parse(v));
                Toggle("显示经纬度", s.Coordinates, v => { s.Coordinates = v; Later(); });
                if (s.Coordinates)
                {
                    TextInput("纬度", s.Latitude, v => s.Latitude = v);
                    TextInput("经度", s.Longitude, v => s.Longitude = v);
                }
            }
            else
            {
                if (s.Kind == AssetKind.GroundOther && s.OtherStyle == "机型文字") Number("字高 / mm", s.CharacterHeight, 100, 8000, 0, v => s.CharacterHeight = v);
                else Choice("字高 / mm", new[] { "2000", "4000" }, s.CharacterHeight.ToString("0"), v => s.CharacterHeight = float.Parse(v));
                Number("字间距 / 字高", s.GroundGap, 0.02f, 0.5f, 2, v => s.GroundGap = v);
                if (s.Kind != AssetKind.GroundOther || s.OtherStyle != "机型文字") Toggle("对比边框", s.Border, v => s.Border = v);
                if (s.Kind == AssetKind.GroundOther && (s.OtherStyle == "MAX SPAN" || s.OtherStyle == "机型文字")) Toggle("黑色背景（取消为透明）", s.BlackBackground, v => s.BlackBackground = v);
            }
            Choice("自由尺寸", new[] { "宽高 / mm", "长宽比" }, s.AspectRatio == null ? "宽高 / mm" : "长宽比", v =>
            {
                s.AspectRatio = v == "长宽比" ? "" : null;
                Later(true);
            });
            if (s.AspectRatio != null)
                TextInput("长宽比 · 例如 3:2，留空自动", s.AspectRatio, v => s.AspectRatio = v);
            else
            {
                Hint("0 = 自动", 25);
                if (s.Kind != AssetKind.TaxiSign) Number("整体宽度 / mm", s.Width, 0, 200000, 0, v => s.Width = v);
                Number("整体高度 / mm", s.Height, 0, 200000, 0, v => s.Height = v);
            }
            DpiLayout.ScaleNewChildren(this); ResumeLayout(true); building = false;
        }

        void Panels(Settings s)
        {
            DataGridView grid = new DataGridView { Width = 308, Height = 214, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "槽位：组合牌分段" };
            DataGridViewComboBoxColumn type = new DataGridViewComboBoxColumn { HeaderText = "类型", FillWeight = 29, FlatStyle = FlatStyle.Flat };
            type.Items.AddRange("方向", "位置", "强制指令"); grid.Columns.Add(type);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "内容", FillWeight = 29 });
            DataGridViewComboBoxColumn arrow = new DataGridViewComboBoxColumn { HeaderText = "箭头", FillWeight = 20, FlatStyle = FlatStyle.Flat };
            arrow.Items.AddRange("无", "←", "↖", "↑", "↗", "→", "↘", "↓", "↙"); grid.Columns.Add(arrow);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "宽mm", FillWeight = 22 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (SignPanel p in s.Panels) grid.Rows.Add(p.Kind, p.Text, p.Arrow, p.Width.ToString("0"));
            Action<int, int, string> set = delegate(int row, int column, string value)
            {
                if (row < 0 || row >= s.Panels.Count) return;
                SignPanel p = s.Panels[row];
                if (column == 0) p.Kind = value;
                if (column == 1) p.Text = value;
                if (column == 2) p.Arrow = value;
                if (column == 3) { float w; p.Width = float.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out w) ? w : -1; }
                Notify();
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            { if (!building && e.RowIndex >= 0 && e.ColumnIndex >= 0) set(e.RowIndex, e.ColumnIndex, Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)); };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            EventHandler live = delegate(object sender, EventArgs e)
            { if (!building && grid.CurrentCell != null) set(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, ((TextBox)sender).Text); };
            grid.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            { TextBox text = e.Control as TextBox; if (text != null) { text.MaxLength = 40; text.CharacterCasing = CharacterCasing.Upper; text.TextChanged -= live; text.TextChanged += live; } };
            Controls.Add(grid);
            FlowLayoutPanel actions = new FlowLayoutPanel { Width = 308, Height = 36, Margin = new Padding(0, 3, 0, 6) };
            Button add = new StudioButton { Text = "+ 分段", Width = 96, Height = 30 }, remove = new StudioButton { Text = "移除分段", Width = 96, Height = 30 };
            add.Click += delegate { if (s.Panels.Count < 6) { grid.EndEdit(); s.Panels.Add(new SignPanel()); Notify(); Later(); } };
            remove.Click += delegate { if (grid.CurrentCell != null) { grid.EndEdit(); s.Panels.RemoveAt(grid.CurrentCell.RowIndex); Notify(); Later(); } };
            actions.Controls.Add(add); actions.Controls.Add(remove); Controls.Add(actions);
        }
    }
}
