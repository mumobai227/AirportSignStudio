using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public enum ColorRole { Foreground, Background, Border }

    public sealed class ColorSettings
    {
        public string Foreground = "", Background = "", Border = "";
        public bool Active { get { return Foreground.Length > 0 || Background.Length > 0 || Border.Length > 0; } }
        public ColorSettings Copy() { return (ColorSettings)MemberwiseClone(); }
        public void Validate()
        {
            foreach (string value in new[] { Foreground, Background, Border })
                if (value == null || value.Length > 0 && !Regex.IsMatch(value, @"\A#[0-9A-Fa-f]{6}\z"))
                    throw new ArgumentException("颜色请使用 #RRGGBB，例如 #FFAB09；留空表示继承默认颜色。");
        }
        public Color Resolve(ColorRole role, Color original, ColorSettings shared, ColorSettings type = null, Color? basis = null)
        {
            string value = role == ColorRole.Foreground ? Foreground : role == ColorRole.Background ? Background : Border;
            if (value.Length > 0) return ColorTranslator.FromHtml(value);
            if (basis.HasValue) return basis.Value;
            if (type != null) return type.Resolve(role, original, shared);
            return shared == null ? original : shared.Resolve(role, original, null);
        }
        public static ColorSettings DisplayColors(SignDocument document)
        {
            string[] values = new string[3];
            for (int i = 0; i < 3; i++)
            {
                Shape shape = document.Shapes.FirstOrDefault(s => s.Role == (ColorRole)i);
                values[i] = shape == null ? "#FFFFFF" : ColorPicker.Hex(shape.Fill);
            }
            return new ColorSettings { Foreground = values[0], Background = values[1], Border = values[2] };
        }
    }

    public sealed class ColorsForm : DpiForm
    {
        readonly ColorSettings original;
        readonly Action<ColorSettings> livePreview;
        readonly CheckBox[] enabled = new CheckBox[3];
        readonly TextBox[] hex = new TextBox[3];
        ColorSettings previewValue;
        bool ready;
        public ColorSettings Value { get; private set; }
        public ColorsForm(ColorSettings current, bool individual, Action<ColorSettings> preview = null, ColorSettings inherited = null)
        {
            original = current.Copy(); previewValue = current.Copy(); livePreview = preview;
            Text = individual ? "当前标牌颜色" : "整张拼图统一颜色";
            ClientSize = new Size(512, 210); MinimumSize = new Size(528, 249); StartPosition = FormStartPosition.CenterParent;
            FlowLayoutPanel fields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            Button[] swatches = new Button[3];
            string[] names = { "文字 / 箭头", "底色", "边框 / 分隔线" }, values = { current.Foreground, current.Background, current.Border };
            string[] defaults = inherited == null ? new[] { "#FFFFFF", "#FFFFFF", "#FFFFFF" } : new[] { inherited.Foreground, inherited.Background, inherited.Border };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                FlowLayoutPanel row = new FlowLayoutPanel { Width = 460, Height = 36, WrapContents = false };
                enabled[i] = new CheckBox { Text = names[i], Width = 160, Height = 30, Checked = values[i].Length > 0, AccessibleName = "自定义" + names[i] };
                hex[i] = new TextBox { Text = values[i].Length > 0 ? values[i] : defaults[i], Width = 125, MaxLength = 7, CharacterCasing = CharacterCasing.Upper, AccessibleName = names[i] + "颜色值" };
                swatches[i] = new StudioButton { Width = 56, Height = 30, Swatch = true, AccessibleName = "选择" + names[i] + "颜色" };
                swatches[i].FlatAppearance.BorderColor = SystemColors.ControlDark;
                Action update = delegate
                {
                    hex[index].Enabled = swatches[index].Enabled = enabled[index].Checked;
                    if (Regex.IsMatch(hex[index].Text, @"\A#[0-9A-Fa-f]{6}\z"))
                    {
                        Color color = ColorTranslator.FromHtml(hex[index].Text);
                        swatches[index].BackColor = color; swatches[index].ForeColor = color.GetBrightness() < 0.5f ? Color.White : Color.Black;
                    }
                    Preview();
                };
                enabled[i].CheckedChanged += delegate { update(); }; hex[i].TextChanged += delegate { update(); };
                swatches[i].Click += delegate
                {
                    string beforeText = hex[index].Text; ColorSettings before = previewValue.Copy();
                    using (ColorPicker dialog = new ColorPicker(swatches[index].BackColor, names[index], color => hex[index].Text = ColorPicker.Hex(color)))
                        if (dialog.ShowDialog(this) != DialogResult.OK)
                        {
                            hex[index].Text = beforeText; previewValue = before;
                            if (livePreview != null) livePreview(before.Copy());
                        }
                };
                row.Controls.Add(enabled[i]); row.Controls.Add(StudioTheme.FrameInput(hex[i], 30)); row.Controls.Add(swatches[i]); fields.Controls.Add(row); update();
            }
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(16, 8, 0, 0), WrapContents = false };
            Button apply = new StudioButton { Text = "应用颜色", Primary = true, Width = 120, Height = 32 }, reset = new StudioButton { Text = "全部恢复继承", Width = 145, Height = 32 }, cancel = new StudioButton { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            reset.Click += delegate { foreach (CheckBox box in enabled) box.Checked = false; };
            apply.Click += delegate
            {
                ColorSettings value = Read();
                try { value.Validate(); } catch (ArgumentException error) { MessageBox.Show(this, error.Message, "颜色无效"); return; }
                if (livePreview != null) livePreview(value.Copy());
                Value = value; DialogResult = DialogResult.OK; Close();
            };
            actions.Controls.Add(apply); actions.Controls.Add(reset); actions.Controls.Add(cancel); CancelButton = cancel;
            Controls.Add(fields); Controls.Add(actions); CompleteLayout(); ready = true;
        }
        ColorSettings Read()
        {
            return new ColorSettings { Foreground = enabled[0].Checked ? hex[0].Text.Trim() : "", Background = enabled[1].Checked ? hex[1].Text.Trim() : "", Border = enabled[2].Checked ? hex[2].Text.Trim() : "" };
        }
        void Preview()
        {
            if (!ready) return;
            ColorSettings value = Read();
            try { value.Validate(); } catch (ArgumentException) { return; }
            previewValue = value; if (livePreview != null) livePreview(value.Copy());
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Owner == null) return;
            Rectangle area = Screen.FromControl(Owner).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Owner.Left + 16, area.Right - Width)), Math.Max(area.Top, Math.Min(Owner.Top + 80, area.Bottom - Height)));
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult != DialogResult.OK && livePreview != null) livePreview(original.Copy());
            base.OnFormClosed(e);
        }
    }
}
