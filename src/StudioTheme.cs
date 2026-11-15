using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AirportSignStudio
{
    internal sealed class StudioMenuRenderer : ToolStripProfessionalRenderer
    {
        sealed class Colors : ProfessionalColorTable
        {
            public Colors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return StudioTheme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return StudioTheme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return StudioTheme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return StudioTheme.Surface; } }
            public override Color MenuItemSelected { get { return StudioTheme.Selected; } }
            public override Color MenuItemBorder { get { return StudioTheme.Line; } }
            public override Color MenuBorder { get { return StudioTheme.Line; } }
            public override Color MenuItemSelectedGradientBegin { get { return StudioTheme.Sidebar; } }
            public override Color MenuItemSelectedGradientEnd { get { return StudioTheme.Sidebar; } }
            public override Color MenuItemPressedGradientBegin { get { return StudioTheme.Selected; } }
            public override Color MenuItemPressedGradientMiddle { get { return StudioTheme.Selected; } }
            public override Color MenuItemPressedGradientEnd { get { return StudioTheme.Selected; } }
            public override Color SeparatorDark { get { return StudioTheme.Line; } }
            public override Color SeparatorLight { get { return StudioTheme.Surface; } }
            public override Color CheckBackground { get { return StudioTheme.Selected; } }
            public override Color CheckSelectedBackground { get { return StudioTheme.Selected; } }
        }
        public StudioMenuRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        { e.TextColor = e.Item.Enabled ? StudioTheme.Ink : StudioTheme.Muted; base.OnRenderItemText(e); }
    }

    internal static class StudioTheme
    {
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color Sidebar = Color.FromArgb(247, 247, 245);
        public static readonly Color Field = Color.FromArgb(252, 252, 251);
        public static readonly Color Ink = Color.FromArgb(36, 36, 33);
        public static readonly Color Muted = Color.FromArgb(118, 118, 111);
        public static readonly Color Line = Color.FromArgb(231, 231, 226);
        public static readonly Color Selected = Color.FromArgb(233, 233, 228);
        public static readonly Color Canvas = Color.FromArgb(246, 246, 243);
        public const float Golden = 0.618034f;
        public static Color Blend(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        public static float Scale(Control control) { Form form = control.FindForm(); return (form == null ? control.DeviceDpi : form.DeviceDpi) / 96f; }
        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            bounds.Width = Math.Max(1, bounds.Width); bounds.Height = Math.Max(1, bounds.Height);
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static void CanvasFrame(Control control, Graphics graphics)
        {
            if (control.Width < 2 || control.Height < 2) return;
            using (GraphicsPath frame = Rounded(new RectangleF(0.5f, 0.5f, control.Width - 1, control.Height - 1), 13 * Scale(control)))
            using (GraphicsPath corners = new GraphicsPath(FillMode.Alternate))
            using (Brush background = new SolidBrush(control.Parent == null ? Surface : control.Parent.BackColor))
            using (Pen border = new Pen(Line))
            {
                corners.AddRectangle(control.ClientRectangle); corners.AddPath(frame, false);
                graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.FillPath(background, corners); graphics.DrawPath(border, frame);
            }
        }
        public static void Apply(Control root)
        {
            foreach (Control control in root.Controls)
            {
                TextBoxBase text = control as TextBoxBase;
                if (text != null && !(text.Parent is UpDownBase)) { text.BorderStyle = text.Parent is StudioField ? BorderStyle.None : BorderStyle.FixedSingle; text.BackColor = Field; text.ForeColor = Ink; }
                NumericUpDown number = control as NumericUpDown;
                if (number != null) { number.BorderStyle = number.Parent is StudioField ? BorderStyle.None : BorderStyle.FixedSingle; number.BackColor = Field; number.ForeColor = Ink; }
                DataGridView grid = control as DataGridView;
                if (grid != null)
                {
                    grid.BackgroundColor = Surface; grid.GridColor = Line; grid.BorderStyle = BorderStyle.None;
                    grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                    grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Ink;
                    grid.DefaultCellStyle.SelectionBackColor = Selected; grid.DefaultCellStyle.SelectionForeColor = Ink;
                    grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
                }
                Apply(control);
            }
        }
        public static StudioField FrameInput(Control input, int height)
        {
            int width = input.Width; Point location = input.Location; Padding margin = input.Margin;
            StudioField field = new StudioField(input) { Width = width, Height = height, Margin = margin, Location = location };
            return field;
        }
    }

    public sealed class StudioField : Panel
    {
        readonly Control input;
        readonly StudioMotion focus;
        readonly StudioSlider slider;
        internal void GroupDrag(Action started, Action ended)
        {
            if (slider == null) return;
            slider.DragStarted += started;
            slider.DragEnded += ended;
        }
        public StudioField(Control control) : this(control, null) { }

        internal StudioField(Control control, StudioSlider customSlider)
        {
            input = control; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = StudioTheme.Field; TabStop = false; Controls.Add(input);
            focus = new StudioMotion(this, value => Invalidate());
            input.Enter += delegate { focus.To(1); }; input.Leave += delegate { focus.To(0); };
            Click += delegate { input.Focus(); };
            slider = customSlider;
            if (control is NumericUpDown number && slider == null) slider = StudioSlider.ForNumber(number);
            if (slider != null)
            {
                Controls.Add(slider);
                input.TextChanged += delegate { slider.Invalidate(); };
                input.EnabledChanged += delegate { slider.Enabled = input.Enabled; };
                slider.Enabled = input.Enabled;
            }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (input == null) return;
            int padding = (int)(9 * StudioTheme.Scale(this));
            int height = input.PreferredSize.Height;
            int sliderWidth = slider == null ? 0 : Math.Max(0, (int)(Width * (input is ComboBox ? 0.43 : 0.47)) - padding);
            input.SetBounds(padding + sliderWidth, Math.Max(1, (Height - height) / 2), Math.Max(1, Width - padding * 2 - sliderWidth), height);
            if (slider != null)
            {
                slider.AccessibleName = (input.AccessibleName ?? "数值") + "滑块";
                slider.SetBounds(padding, 1, Math.Max(1, sliderWidth - 2), Math.Max(1, Height - 2));
            }
            if (input is ComboBox && input.Width > 2 && input.Height > 2)
            {
                Region previous = input.Region;
                input.Region = new Region(new Rectangle(1, 1, input.Width - 2, input.Height - 2));
                if (previous != null) previous.Dispose();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? StudioTheme.Surface : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = StudioTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * StudioTheme.Scale(this)))
            using (Brush fill = new SolidBrush(StudioTheme.Field))
            using (Pen border = new Pen(StudioTheme.Blend(StudioTheme.Line, StudioTheme.Muted, focus.Value), StudioTheme.Scale(this)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
        }
        protected override void Dispose(bool disposing) { if (disposing && focus != null) focus.Dispose(); base.Dispose(disposing); }
    }

    public enum StudioIcon { None, Folder, Save, Undo, Redo, Export, Palette, Brush, Grid, Copy, Paste, Clear }

    public sealed class StudioButton : Button
    {
        [System.ComponentModel.DefaultValue(false)]
        public bool Primary { get; set; }
        [System.ComponentModel.DefaultValue(false)]
        public bool Quiet { get; set; }
        [System.ComponentModel.DefaultValue(StudioIcon.None)]
        public StudioIcon IconKind { get; set; }
        [System.ComponentModel.DefaultValue(false)]
        public bool Disclosure { get; set; }
        [System.ComponentModel.DefaultValue(false)]
        public bool Expanded { get; set; }
        [System.ComponentModel.DefaultValue(false)]
        public bool Swatch { get; set; }
        bool hover, pressed;
        readonly StudioMotion motion;
        public StudioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            BackColor = StudioTheme.Surface; ForeColor = StudioTheme.Ink;
            Cursor = Cursors.Hand; motion = new StudioMotion(this, value => Invalidate());
        }
        void UpdateMotion() { motion.To(!Enabled ? 0 : pressed ? 1 : hover ? 0.62f : 0); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; UpdateMotion(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; UpdateMotion(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; UpdateMotion(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; UpdateMotion(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; UpdateMotion(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; UpdateMotion(); base.OnKeyUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (motion != null) { hover = pressed = false; motion.To(0, false); } base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; UpdateMotion(); Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = StudioTheme.Scale(this); Graphics g = e.Graphics;
            Color parent = Parent == null ? StudioTheme.Surface : Parent.BackColor;
            g.Clear(parent); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color normal = Swatch ? BackColor : Primary ? StudioTheme.Ink : Quiet ? parent : StudioTheme.Surface;
            Color fill = !Enabled ? StudioTheme.Field : StudioTheme.Blend(normal, Swatch ? StudioTheme.Blend(normal, Color.White, 0.18f) : Primary ? Color.FromArgb(68, 68, 63) : StudioTheme.Selected, motion.Value);
            Color foreground = !Enabled ? Color.FromArgb(166, 166, 160) : Swatch ? ForeColor : Primary ? Color.White : StudioTheme.Ink;
            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath path = StudioTheme.Rounded(bounds, 8 * scale))
            using (Brush brush = new SolidBrush(fill))
            using (Pen border = new Pen(Focused && ShowFocusCues ? StudioTheme.Ink : StudioTheme.Line, scale))
            {
                g.FillPath(brush, path);
                if (!Primary && (!Quiet || Focused && ShowFocusCues)) g.DrawPath(border, path);
            }
            if (Primary && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(4 * scale), -(int)(4 * scale)), foreground, fill);
            Rectangle text = Rectangle.Inflate(ClientRectangle, -(int)(6 * scale), 0);
            if (IconKind != StudioIcon.None)
            {
                int labelWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                float start = Math.Max(10 * scale, (Width - labelWidth - 25 * scale - (Disclosure ? 16 * scale : 0)) / 2);
                DrawIcon(g, IconKind, new PointF(start, Height / 2f - 7 * scale), scale, foreground);
                text.X = (int)(start + 24 * scale); text.Width = Math.Max(1, Width - text.X - (int)((Disclosure ? 28 : 8) * scale));
            }
            if (Disclosure)
                using (Pen pen = new Pen(foreground, 1.4f * scale))
                {
                    float x = Width - 19 * scale, y = Height / 2f, direction = Expanded ? -1 : 1;
                    g.DrawLines(pen, new[] { new PointF(x - 4 * scale, y - direction * 2 * scale), new PointF(x, y + direction * 2 * scale), new PointF(x + 4 * scale, y - direction * 2 * scale) });
                }
            TextRenderer.DrawText(g, Text, Font, text, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        }
        static void DrawIcon(Graphics g, StudioIcon icon, PointF origin, float scale, Color color)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(origin.X, origin.Y); g.ScaleTransform(scale, scale);
            using (Pen pen = new Pen(color, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (icon == StudioIcon.Grid) { g.DrawRectangle(pen, 1, 1, 13, 13); g.DrawLine(pen, 6, 1, 6, 14); g.DrawLine(pen, 1, 7, 14, 7); }
                else if (icon == StudioIcon.Folder) { g.DrawLines(pen, new[] { new PointF(1, 12), new PointF(1, 3), new PointF(6, 3), new PointF(8, 5), new PointF(14, 5), new PointF(12, 13), new PointF(1, 13), new PointF(3, 7), new PointF(12, 7) }); }
                else if (icon == StudioIcon.Save) { g.DrawRectangle(pen, 1, 1, 13, 13); g.DrawRectangle(pen, 4, 1, 7, 4); g.DrawRectangle(pen, 4, 9, 7, 5); }
                else if (icon == StudioIcon.Undo || icon == StudioIcon.Redo)
                {
                    if (icon == StudioIcon.Redo) { g.TranslateTransform(15, 0); g.ScaleTransform(-1, 1); }
                    g.DrawLines(pen, new[] { new PointF(5, 1), new PointF(1, 5), new PointF(5, 9) }); g.DrawLine(pen, 1, 5, 9, 5); g.DrawArc(pen, 6, 5, 8, 8, -90, 180); g.DrawLine(pen, 10, 13, 6, 13);
                }
                else if (icon == StudioIcon.Export) { g.DrawLine(pen, 7, 1, 7, 10); g.DrawLines(pen, new[] { new PointF(3, 6), new PointF(7, 10), new PointF(11, 6) }); g.DrawLines(pen, new[] { new PointF(1, 10), new PointF(1, 14), new PointF(14, 14), new PointF(14, 10) }); }
                else if (icon == StudioIcon.Palette) { g.DrawEllipse(pen, 1, 1, 13, 13); g.DrawEllipse(pen, 4, 4, 1, 1); g.DrawEllipse(pen, 9, 4, 1, 1); g.DrawEllipse(pen, 4, 9, 1, 1); g.DrawArc(pen, 8, 8, 6, 6, 90, 180); }
                else if (icon == StudioIcon.Brush) { g.DrawPolygon(pen, new[] { new PointF(7, 10), new PointF(5, 8), new PointF(12, 1), new PointF(14, 3) }); g.DrawBezier(pen, 5, 9, 0, 8, 5, 14, 0, 14); g.DrawBezier(pen, 0, 14, 5, 15, 9, 13, 7, 10); }
                else if (icon == StudioIcon.Copy) { g.DrawRectangle(pen, 5, 5, 9, 9); g.DrawLines(pen, new[] { new PointF(10, 3), new PointF(10, 1), new PointF(1, 1), new PointF(1, 10), new PointF(3, 10) }); }
                else if (icon == StudioIcon.Paste) { g.DrawRectangle(pen, 2, 3, 11, 11); g.DrawRectangle(pen, 5, 1, 5, 4); g.DrawLine(pen, 5, 9, 10, 9); }
                else if (icon == StudioIcon.Clear) { g.DrawLine(pen, 2, 3, 13, 3); g.DrawRectangle(pen, 5, 1, 5, 2); g.DrawLines(pen, new[] { new PointF(3, 5), new PointF(4, 14), new PointF(11, 14), new PointF(12, 5) }); }
            }
            g.Restore(state);
        }
        protected override void Dispose(bool disposing) { if (disposing && motion != null) motion.Dispose(); base.Dispose(disposing); }
    }

    public sealed class StudioNavigation : ListBox
    {
        int hover = -1;
        public StudioNavigation()
        {
            BorderStyle = BorderStyle.None; DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false;
            BackColor = StudioTheme.Sidebar; ForeColor = StudioTheme.Ink; UpdateItemHeight();
        }
        void UpdateItemHeight() { ItemHeight = Math.Max(Font.Height + 14, (int)Math.Round(40 * StudioTheme.Scale(this))); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateItemHeight(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateItemHeight(); }
        protected override void OnMouseMove(MouseEventArgs e) { int next = IndexFromPoint(e.Location); if (hover != next) { hover = next; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            float scale = StudioTheme.Scale(this); bool selected = e.Index == SelectedIndex;
            using (Brush background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(e.Bounds.X + 4 * scale, e.Bounds.Y + 3 * scale, e.Bounds.Width - 8 * scale, e.Bounds.Height - 6 * scale);
            if (selected || hover == e.Index)
                using (GraphicsPath path = StudioTheme.Rounded(bounds, 7 * scale))
                using (Brush fill = new SolidBrush(selected ? StudioTheme.Selected : Color.FromArgb(239, 239, 236))) e.Graphics.FillPath(fill, path);
            float x = bounds.Left + 12 * scale, y = bounds.Top + bounds.Height / 2;
            using (Pen pen = new Pen(selected ? StudioTheme.Ink : StudioTheme.Muted, 1.3f * scale))
            {
                if (e.Index == 0 || e.Index == 4)
                {
                    e.Graphics.DrawLine(pen, x, y + 5 * scale, x + 12 * scale, y - 5 * scale);
                    e.Graphics.DrawLine(pen, x + 5 * scale, y - 5 * scale, x + 12 * scale, y - 5 * scale);
                    e.Graphics.DrawLine(pen, x + 12 * scale, y - 5 * scale, x + 12 * scale, y + 2 * scale);
                    if (e.Index == 4) e.Graphics.DrawLine(pen, x, y + 9 * scale, x + 14 * scale, y + 9 * scale);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, x, y - 6 * scale, 14 * scale, 12 * scale);
                    e.Graphics.DrawLine(pen, x + 4 * scale, y, x + 10 * scale, y);
                    if (e.Index >= 2) e.Graphics.DrawLine(pen, x + 7 * scale, y + 6 * scale, x + 7 * scale, y + 10 * scale);
                }
            }
            Rectangle text = new Rectangle((int)(x + 25 * scale), e.Bounds.Top, e.Bounds.Right - (int)(x + 31 * scale), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(Items[e.Index]), Font, text, StudioTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Round(bounds), StudioTheme.Muted, BackColor);
        }
    }

    public sealed class StudioComboBox : ComboBox
    {
        public StudioComboBox() { FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; BackColor = StudioTheme.Field; ForeColor = StudioTheme.Ink; }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 2; }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (Brush brush = new SolidBrush(selected ? StudioTheme.Selected : BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index < 0 ? Text : GetItemText(Items[e.Index]);
            Rectangle bounds = e.Bounds; bounds.X += (int)(6 * StudioTheme.Scale(this)); bounds.Width -= (int)(9 * StudioTheme.Scale(this));
            TextRenderer.DrawText(e.Graphics, text, Font, bounds, Enabled ? StudioTheme.Ink : StudioTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
