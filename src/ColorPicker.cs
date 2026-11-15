using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class ColorPlane : Control
    {
        public bool HueOnly;
        public double Hue, Saturation, Brightness;
        public Action<double, double> Chosen;
        public ColorPlane() { DoubleBuffered = true; Cursor = Cursors.Cross; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            Rectangle area = new Rectangle(0, 0, Width - 1, Height - 1);
            if (HueOnly)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(area, Color.Red, Color.Red, 90f))
                {
                    brush.InterpolationColors = new ColorBlend { Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red }, Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f } };
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }
                float y = (float)(Hue / 360 * area.Height);
                using (Pen black = new Pen(Color.Black, 4)) e.Graphics.DrawLine(black, 0, y, Width, y);
                using (Pen white = new Pen(Color.White, 2)) e.Graphics.DrawLine(white, 0, y, Width, y);
            }
            else
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(area, Color.White, ColorPicker.FromHsb(Hue, 1, 1), 0f)) e.Graphics.FillRectangle(brush, ClientRectangle);
                using (LinearGradientBrush brush = new LinearGradientBrush(area, Color.FromArgb(0, 0, 0, 0), Color.Black, 90f)) e.Graphics.FillRectangle(brush, ClientRectangle);
                float x = (float)(Saturation * area.Width), y = (float)((1 - Brightness) * area.Height), r = 5 * DeviceDpi / 96f;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen black = new Pen(Color.Black, 3)) e.Graphics.DrawEllipse(black, x - r, y - r, 2 * r, 2 * r);
                using (Pen white = new Pen(Color.White, 1)) e.Graphics.DrawEllipse(white, x - r, y - r, 2 * r, 2 * r);
            }
        }
        void Choose(Point point)
        {
            double x = Math.Max(0, Math.Min(1, point.X / (double)Math.Max(1, Width - 1))), y = Math.Max(0, Math.Min(1, point.Y / (double)Math.Max(1, Height - 1)));
            if (Chosen != null) Chosen(HueOnly ? Math.Min(359.999, y * 360) : x, 1 - y);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Capture = true; Choose(e.Location); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Choose(e.Location); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && Capture) { Choose(e.Location); Capture = false; } }
    }

    public sealed class ColorPicker : DpiForm
    {
        readonly Color original;
        readonly Action<Color> preview;
        readonly ColorPlane square = new ColorPlane { AccessibleName = "饱和度与明度" }, hue = new ColorPlane { HueOnly = true, AccessibleName = "色相" };
        readonly Panel selected = new Panel();
        readonly NumericUpDown[] channels = new NumericUpDown[6];
        readonly TextBox hex = new TextBox();
        readonly Button apply = new StudioButton { Primary = true };
        bool updating, ready;
        double h, s, b;
        public Color Value { get; private set; }
        public static string Hex(Color color) { return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); }
        public static Color FromHsb(double hue, double saturation, double brightness)
        {
            double c = brightness * saturation, sector = hue / 60, x = c * (1 - Math.Abs(sector % 2 - 1)), m = brightness - c;
            double r = 0, g = 0, blue = 0;
            if (sector < 1) { r = c; g = x; } else if (sector < 2) { r = x; g = c; }
            else if (sector < 3) { g = c; blue = x; } else if (sector < 4) { g = x; blue = c; }
            else if (sector < 5) { r = x; blue = c; } else { r = c; blue = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((blue + m) * 255));
        }
        public ColorPicker(Color color, string part, Action<Color> livePreview)
        {
            original = color; preview = livePreview;
            Text = "拾色器 · " + part; ClientSize = new Size(570, 405); MinimumSize = new Size(586, 444); StartPosition = FormStartPosition.CenterParent;
            BackColor = StudioTheme.Surface; ForeColor = StudioTheme.Ink;
            LabelAt("选择颜色", 20, 12, 270, 25); LabelAt("新的", 334, 15, 80, 25); LabelAt("当前", 334, 116, 80, 25);
            square.SetBounds(20, 42, 256, 256); hue.SetBounds(290, 42, 24, 256); Controls.Add(square); Controls.Add(hue);
            selected.SetBounds(334, 42, 80, 36); selected.BorderStyle = BorderStyle.FixedSingle; Controls.Add(selected);
            Panel old = new Panel { Bounds = new Rectangle(334, 78, 80, 36), BackColor = color, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "原始颜色" }; Controls.Add(old);
            apply.Text = "确定"; apply.SetBounds(440, 42, 110, 32); apply.ForeColor = Color.White; apply.BackColor = Color.FromArgb(75, 75, 75); apply.FlatStyle = FlatStyle.Flat; Controls.Add(apply);
            Button cancel = new StudioButton { Text = "取消", Bounds = new Rectangle(440, 82, 110, 32), DialogResult = DialogResult.Cancel, ForeColor = Color.White, BackColor = Color.FromArgb(75, 75, 75), FlatStyle = FlatStyle.Flat }; Controls.Add(cancel); CancelButton = cancel; AcceptButton = apply;
            string[] names = { "H", "S", "B", "R", "G", "B" }, accessible = { "色相 H", "饱和度 S", "明度 B", "红 R", "绿 G", "蓝 B" };
            for (int i = 0; i < channels.Length; i++)
            {
                int index = i, y = 150 + i * 31 + (i >= 3 ? 10 : 0);
                LabelAt(names[i] + ":", 334, y + 3, 28, 25);
                channels[i] = new NumericUpDown { Bounds = new Rectangle(366, y, 164, 28), Maximum = i == 0 ? 359 : i < 3 ? 100 : 255, AccessibleName = accessible[i] };
                Controls.Add(StudioTheme.FrameInput(channels[i], 28)); if (i < 3) LabelAt(i == 0 ? "°" : "%", 536, y + 3, 26, 25);
                channels[i].ValueChanged += delegate
                {
                    if (updating) return;
                    if (index < 3) { h = (double)channels[0].Value; s = (double)channels[1].Value / 100; b = (double)channels[2].Value / 100; UpdateColor(FromHsb(h, s, b)); }
                    else SetColor(Color.FromArgb((int)channels[3].Value, (int)channels[4].Value, (int)channels[5].Value));
                };
            }
            LabelAt("Hex", 324, 361, 40, 25); hex.SetBounds(366, 357, 130, 28); hex.MaxLength = 7; hex.CharacterCasing = CharacterCasing.Upper; hex.AccessibleName = "十六进制颜色"; Controls.Add(StudioTheme.FrameInput(hex, 28));
            hex.TextChanged += delegate
            {
                if (updating) return;
                apply.Enabled = Regex.IsMatch(hex.Text, @"\A#[0-9A-Fa-f]{6}\z");
                if (apply.Enabled) SetColor(ColorTranslator.FromHtml(hex.Text));
            };
            square.Chosen = delegate(double saturation, double brightness) { s = saturation; b = brightness; UpdateColor(FromHsb(h, s, b)); };
            hue.Chosen = delegate(double value, double unused) { h = value; UpdateColor(FromHsb(h, s, b)); };
            apply.Click += delegate { ValidateChildren(); DialogResult = DialogResult.OK; Close(); };
            SetColor(color); CompleteLayout(); ready = true;
        }
        void LabelAt(string text, int x, int y, int width, int height) { Controls.Add(new Label { Text = text, Bounds = new Rectangle(x, y, width, height) }); }
        void SetColor(Color color)
        {
            h = color.GetHue(); int max = Math.Max(color.R, Math.Max(color.G, color.B)), min = Math.Min(color.R, Math.Min(color.G, color.B));
            s = max == 0 ? 0 : (max - min) / (double)max; b = max / 255d; UpdateColor(color);
        }
        void UpdateColor(Color color)
        {
            bool changed = Value.ToArgb() != color.ToArgb(); Value = color; updating = true;
            square.Hue = hue.Hue = h; square.Saturation = s; square.Brightness = b; square.Invalidate(); hue.Invalidate();
            selected.BackColor = color; hex.Text = Hex(color); apply.Enabled = true;
            channels[0].Value = Math.Min(359, (decimal)Math.Round(h)); channels[1].Value = (decimal)Math.Round(s * 100); channels[2].Value = (decimal)Math.Round(b * 100);
            channels[3].Value = color.R; channels[4].Value = color.G; channels[5].Value = color.B; updating = false;
            if (ready && changed && preview != null) preview(color);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult != DialogResult.OK && preview != null) preview(original);
            base.OnFormClosed(e);
        }
    }
}
