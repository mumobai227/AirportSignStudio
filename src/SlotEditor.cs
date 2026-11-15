using System;
using System.Collections.Generic;
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
        readonly SignEngine engine;
        Settings settings;
        bool building, rebuildPending, dragging;
        readonly List<Action<SignDocument>> dimensionSliders = new List<Action<SignDocument>>();

        public SlotEditor(SignEngine engine)
        {
            this.engine = engine;
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

        void Notify()
        {
            if (building) return;
            SyncDimensionSliders();
            Changed?.Invoke(settings);
        }
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
        Label Field(string title, Control control, int height, StudioSlider slider = null)
        {
            height = Math.Max(36, height);
            Panel group = new Panel { Width = 308, Height = height + 26, Margin = new Padding(0, 0, 0, 9) };
            Label heading = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = StudioTheme.Muted };
            group.Controls.Add(heading);
            control.AccessibleName = "槽位：" + title;
            StudioField field = new StudioField(control, slider); field.SetBounds(0, 25, 308, height);
            field.GroupDrag(() => { dragging = true; EditStarted?.Invoke(); }, () => { dragging = false; SyncDimensionSliders(); EditEnded?.Invoke(); });
            group.Controls.Add(field); Controls.Add(group);
            return heading;
        }
        void Choice(string title, string[] values, string selected, Action<string> setter)
        {
            ComboBox input = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            input.Items.AddRange(values); input.SelectedItem = selected;
            input.SelectedIndexChanged += delegate { if (!building) { setter(Convert.ToString(input.SelectedItem)); Notify(); } };
            Field(title, input, 29);
        }
        void TextInput(string title, string value, Action<string> setter, bool wingspan = false, bool ratio = false)
        {
            TextBox input = new TextBox { Text = value, MaxLength = 40, CharacterCasing = CharacterCasing.Upper };
            input.TextChanged += delegate { if (!building) { setter(input.Text); Notify(); } };
            StudioSlider slider = null;
            if (wingspan)
            {
                slider = new StudioSlider(() =>
                {
                    string metres = SignEngine.Normalize(input.Text).TrimEnd('M').Trim();
                    return decimal.TryParse(metres, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal span) ? span : 36;
                }, v => input.Text = v.ToString("0.##", CultureInfo.InvariantCulture), 0.01m, 100, 1, 2);
            }
            if (ratio)
            {
                slider = new StudioSlider(() =>
                {
                    try
                    {
                        double number = SignEngine.ReadAspectRatio(input.Text);
                        return number == 0 ? 1 : (decimal)Math.Clamp(number, 0.0001, 10000);
                    }
                    catch (ArgumentException) { return 1; }
                }, v => input.Text = v.ToString("0.####", CultureInfo.InvariantCulture) + ":1", 0.0001m, 10000, 0.01m, 4, SliderScale.Ratio);
            }
            if (slider != null) input.TextChanged += delegate { slider.Invalidate(); };
            Field(title, input, 28, slider);
        }

        void Number(string title, float value, float min, float max, int decimals, Action<float> setter, decimal increment = 1)
        {
            NumericUpDown input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = decimals,
                Increment = increment, ThousandsSeparator = min >= 0 && max >= 1000, Value = (decimal)value };
            input.ValueChanged += delegate
            {
                if (building) return;
                setter((float)input.Value); Notify();
            };
            Field(title, input, 29);
        }

        internal static (int Width, int Height, int Margin) CommonDimensionLimits(Settings s) => (s.Kind, s.OtherStyle) switch
        {
            (AssetKind.GroundTaxi, _) => (16000, 10000, 2000),
            (AssetKind.GroundStand, _) => (12000, 10000, 2000),
            (AssetKind.TaxiSign, _) => (4000, 1200, 600),
            (AssetKind.StandSign, _) => (4000, 3000, 600),
            (AssetKind.GroundOther, "HP") => (12000, 8000, 1800),
            (AssetKind.GroundOther, "NO ENTRY") => (18000, 8000, 2400),
            (AssetKind.GroundOther, "机型文字") => (6000, 1600, 750),
            _ => (18000, 13000, 2400) // MAX SPAN
        };

        internal static decimal ToMetres(float mm) => Math.Round((decimal)(double)mm / 1000, 5);
        internal static float ToMillimetres(decimal metres) => (float)(metres * 1000);

        static NumericUpDown SizeInput(float mm, bool margin = false) => new NumericUpDown
        {
            Minimum = margin ? -1 : 0, Maximum = 200, DecimalPlaces = 5, Increment = 0.001m,
            Value = margin && mm == -1 ? -1 : ToMetres(mm)
        };

        void SizeField(string title, float mm, int limit, Func<SignDocument, float> minimum, Action<Settings, float> set, bool margin = false)
        {
            NumericUpDown input = SizeInput(mm, margin);
            input.ValueChanged += delegate
            {
                if (building) return;
                if (input.Value is < 0 and > -1) { input.Value = -1; return; }
                set(settings, input.Value == -1 ? -1 : ToMillimetres(input.Value)); Notify();
            };
            SizeRail(title, input, limit, minimum, set, margin ? -1 : 0);
        }

        void SizeRail(string title, NumericUpDown input, int limit, Func<SignDocument, float> minimum,
            Action<Settings, float> set, float automatic = 0)
        {
            StudioSlider slider = StudioSlider.ForNumber(input);
            Label heading = Field(title, input, 29, slider);
            dimensionSliders.Add(doc =>
            {
                (decimal Min, decimal Max)? range = null;
                if (input.Enabled)
                {
                    using SignDocument fallback = doc == null ? TryAutomatic() : null;
                    SignDocument sizing = doc ?? fallback;
                    if (sizing != null) range = DimensionRange(engine, settings, minimum(sizing), limit, set);
                }
                slider.SetRange(range);
                heading.Text = range.HasValue ? $"{title} · {range.Value.Min:0.###}–{range.Value.Max:0.###}" : title + " · 手动输入";
            });
            SignDocument TryAutomatic()
            {
                Settings draft = settings.Copy();
                set(draft, automatic);
                try { return engine.Build(draft); }
                catch (ArgumentException) { return null; }
            }
        }

        internal static (decimal Min, decimal Max)? DimensionRange(SignEngine engine, Settings settings, float minimum, int limit,
            Action<Settings, float> set)
        {
            int low = (int)Math.Ceiling(minimum);
            if (low > limit) return null;
            Settings draft = settings.Copy();
            bool Fits(int mm)
            {
                set(draft, mm);
                try { using SignDocument doc = engine.Build(draft); return true; }
                catch (ArgumentException) { return false; }
            }
            if (!Fits(low)) return null;
            int high = limit;
            // Other fixed dimensions can impose a tighter upper bound, especially for margins.
            if (high > low && !Fits(high))
            {
                int accepted = low;
                while (accepted + 1 < high)
                {
                    int mid = (accepted + high) / 2;
                    if (Fits(mid)) accepted = mid; else high = mid;
                }
                high = accepted;
            }
            return (low / 1000m, high / 1000m);
        }

        void SyncDimensionSliders()
        {
            if (building || rebuildPending || dragging || dimensionSliders.Count == 0) return;
            SignDocument doc;
            try { doc = engine.Build(settings); }
            catch (ArgumentException) { doc = null; }
            using (doc)
                foreach (var update in dimensionSliders) update(doc);
        }
        void Toggle(string title, bool value, Action<bool> setter)
        {
            CheckBox input = new CheckBox { Text = title, Checked = value, Width = 308, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            input.CheckedChanged += delegate { if (!building) { setter(input.Checked); Notify(); } }; Controls.Add(input);
        }

        void Rebuild()
        {
            building = true; SuspendLayout();
            dimensionSliders.Clear();
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
            var limits = CommonDimensionLimits(s);
            if (s.Kind == AssetKind.TaxiSign) Panels(s, limits.Width);
            else if (s.Kind != AssetKind.GroundOther || s.OtherStyle != "NO ENTRY")
            {
                bool wingspan = s.Kind == AssetKind.GroundOther && s.OtherStyle == "MAX SPAN";
                TextInput(wingspan ? "翼展数值 / m" : "内容", s.Text, v => s.Text = v, wingspan: wingspan);
            }
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
                if (s.Kind == AssetKind.GroundOther && s.OtherStyle == "机型文字") Number("字高 / mm", s.CharacterHeight, 100, 8000, 2, v => s.CharacterHeight = v);
                else Choice("字高 / mm", new[] { "2000", "4000" }, s.CharacterHeight.ToString("0"), v => s.CharacterHeight = float.Parse(v));
                Number("字间距 / 字高", s.GroundGap, 0.02f, 0.5f, 4, v => s.GroundGap = v, 0.01m);
                if (s.Kind != AssetKind.GroundOther || s.OtherStyle != "机型文字") Toggle("对比边框", s.Border, v => s.Border = v);
                if (s.Kind == AssetKind.GroundOther && (s.OtherStyle == "MAX SPAN" || s.OtherStyle == "机型文字")) Toggle("黑色背景（取消为透明）", s.BlackBackground, v => s.BlackBackground = v);
            }
            Choice("自由尺寸", new[] { "宽高 / m", "长宽比" }, s.AspectRatio == null ? "宽高 / m" : "长宽比", v =>
            {
                s.AspectRatio = v == "长宽比" ? "" : null;
                Later(true);
            });
            if (s.AspectRatio != null)
                TextInput("长宽比 · 例如 3:2，留空自动", s.AspectRatio, v => s.AspectRatio = v, ratio: true);
            else
            {
                Hint("0 = 自动", 25);
                if (s.Kind != AssetKind.TaxiSign)
                    SizeField("整体宽度 / m", s.Width, limits.Width, doc => doc.MinimumWidth, (draft, v) => draft.Width = v);
                SizeField("整体高度 / m", s.Height, limits.Height, doc => doc.MinimumHeight, (draft, v) => draft.Height = v);
            }
            Hint(s.Kind == AssetKind.TaxiSign ? "各段左右留边 · −1 = 自动" : "左右留边 · −1 = 自动", 25);
            SizeField("左留边 / m", s.LeftMargin, limits.Margin, doc => doc.MinimumMargin, (draft, v) => draft.LeftMargin = v, margin: true);
            SizeField("右留边 / m", s.RightMargin, limits.Margin, doc => doc.MinimumMargin, (draft, v) => draft.RightMargin = v, margin: true);
            DpiLayout.ScaleNewChildren(this); ResumeLayout(true); building = false;
            SyncDimensionSliders();
        }

        void Panels(Settings s, int widthLimit)
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
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "宽 / m", FillWeight = 22 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (SignPanel p in s.Panels) grid.Rows.Add(p.Kind, p.Text, p.Arrow, ToMetres(p.Width).ToString("0.#####"));
            NumericUpDown panelWidth = SizeInput(0);
            bool syncingWidth = false;
            Action syncWidth = delegate
            {
                int row = grid.CurrentCell?.RowIndex ?? -1;
                syncingWidth = true;
                panelWidth.Enabled = row >= 0 && row < s.Panels.Count;
                float width = panelWidth.Enabled ? s.Panels[row].Width : 0;
                panelWidth.Value = ToMetres(Math.Clamp(width, 0, 200000));
                syncingWidth = false;
            };
            Action<int, int, string> set = delegate(int row, int column, string value)
            {
                if (row < 0 || row >= s.Panels.Count) return;
                SignPanel p = s.Panels[row];
                if (column == 0) p.Kind = value;
                if (column == 1) p.Text = value;
                if (column == 2) p.Arrow = value;
                if (column == 3)
                {
                    p.Width = decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal w) && w is >= 0 and <= 200
                        ? ToMillimetres(w) : -1;
                    syncWidth();
                }
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
            add.Click += delegate { if (s.Panels.Count < 6) { grid.EndEdit(); s.Panels.Add(new SignPanel()); Later(); Notify(); } };
            remove.Click += delegate { if (grid.CurrentCell != null) { grid.EndEdit(); s.Panels.RemoveAt(grid.CurrentCell.RowIndex); Later(); Notify(); } };
            actions.Controls.Add(add); actions.Controls.Add(remove); Controls.Add(actions);
            grid.CurrentCellChanged += delegate { syncWidth(); SyncDimensionSliders(); };
            panelWidth.ValueChanged += delegate
            {
                if (building || syncingWidth || grid.CurrentCell == null) return;
                grid.Rows[grid.CurrentCell.RowIndex].Cells[3].Value = panelWidth.Value.ToString("0.#####");
            };
            SizeRail("分段宽度 / m（0 = 自动）", panelWidth, widthLimit, doc => doc.MinimumPanelWidths[grid.CurrentCell.RowIndex],
                (draft, v) => draft.Panels[grid.CurrentCell.RowIndex].Width = v);
            syncWidth();
        }
    }
}
