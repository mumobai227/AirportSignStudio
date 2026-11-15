using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AirportSignStudio
{
    internal enum SliderScale { Linear, Ratio }

    internal sealed class StudioSlider : Control
    {
        readonly Func<decimal> read;
        readonly Action<decimal> write;
        readonly decimal step;
        decimal minimum, maximum;
        readonly int decimals;
        readonly SliderScale scale;
        bool dragging;
        int lastX;
        decimal dragValue;
        public event Action DragStarted, DragEnded;

        public StudioSlider(Func<decimal> read, Action<decimal> write, decimal minimum, decimal maximum, decimal step, int decimals,
            SliderScale scale = SliderScale.Linear)
        {
            this.read = read; this.write = write; this.minimum = minimum; this.maximum = maximum;
            this.step = step; this.decimals = decimals; this.scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider;
            AccessibleDescription = "点击或拖动到所需数值。左右方向键调整。Ctrl 为十分之一速度，Shift 为百分之一速度。";
        }

        public static StudioSlider ForNumber(NumericUpDown number, SliderScale scale = SliderScale.Linear)
        {
            decimal current = number.Value;
            StudioSlider slider = new StudioSlider(() => current, value => number.Value = value, number.Minimum,
                number.Maximum, number.Increment, number.DecimalPlaces, scale);
            number.ValueChanged += delegate { current = number.Value; slider.Invalidate(); };
            return slider;
        }

        public void SetRange((decimal Min, decimal Max)? range)
        {
            Visible = range.HasValue;
            if (!range.HasValue) return;
            (minimum, maximum) = range.Value;
            Invalidate();
        }

        internal static decimal Speed(Keys modifiers)
        {
            if ((modifiers & Keys.Shift) != 0) return 0.01m;
            return (modifiers & Keys.Control) != 0 ? 0.1m : 1m;
        }

        internal static decimal Quantize(decimal value, decimal minimum, decimal maximum, int decimals)
        {
            return Math.Clamp(Math.Round(value, decimals, MidpointRounding.AwayFromZero), minimum, maximum);
        }

        internal static decimal FineDrag(decimal value, decimal delta, decimal step, Keys modifiers, decimal minimum, decimal maximum)
        {
            return Math.Clamp(value + delta * step * Speed(modifiers), minimum, maximum);
        }

        float Inset { get { return 6 * StudioTheme.Scale(this); } }
        float Travel { get { return Math.Max(1, Width - Inset * 2); } }
        internal static double Fraction(decimal value, decimal minimum, decimal maximum, SliderScale scale)
        {
            if (value <= minimum) return 0;
            if (value >= maximum) return 1;
            if (scale == SliderScale.Ratio)
                return Math.Log((double)(value / minimum)) / Math.Log((double)(maximum / minimum));
            return (double)((value - minimum) / (maximum - minimum));
        }
        internal static decimal ValueAt(double fraction, decimal minimum, decimal maximum, SliderScale scale)
        {
            if (fraction <= 0) return minimum;
            if (fraction >= 1) return maximum;
            if (scale == SliderScale.Ratio) return minimum * (decimal)Math.Pow((double)(maximum / minimum), fraction);
            return minimum + (maximum - minimum) * (decimal)fraction;
        }

        decimal ValueAtX(int x) => ValueAt((x - Inset) / Travel, minimum, maximum, scale);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = StudioTheme.Scale(this), center = Height / 2f, end = Inset + Travel;
            float thumb = Inset + (float)Fraction(read(), minimum, maximum, this.scale) * Travel;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen rail = new Pen(StudioTheme.Selected, 3 * scale))
                e.Graphics.DrawLine(rail, Inset, center, end, center);
            Color active = Enabled ? StudioTheme.Ink : StudioTheme.Line;
            using (Pen fill = new Pen(active, 3 * scale))
                e.Graphics.DrawLine(fill, Inset, center, thumb, center);
            using (Brush knob = new SolidBrush(active))
                e.Graphics.FillEllipse(knob, thumb - 4 * scale, center - 4 * scale, 8 * scale, 8 * scale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (!Visible) return;
            dragValue = read();
            if (Speed(ModifierKeys) != 1m && (dragValue < minimum || dragValue > maximum)) return;
            DragStarted?.Invoke();
            dragging = true; Capture = true; lastX = e.X;
            if (Speed(ModifierKeys) == 1m)
            {
                dragValue = ValueAtX(e.X);
                write(Quantize(dragValue, minimum, maximum, decimals));
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || e.X == lastX) return;
            Keys modifiers = ModifierKeys;
            if (Speed(modifiers) == 1m)
                dragValue = ValueAtX(e.X);
            else
            {
                decimal pixels = (e.X - lastX) / (decimal)StudioTheme.Scale(this);
                dragValue = FineDrag(dragValue, pixels, step, modifiers, minimum, maximum);
            }
            lastX = e.X;
            write(Quantize(dragValue, minimum, maximum, decimals));
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragging) Capture = false;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture || !dragging) return;
            dragging = false;
            Invalidate();
            DragEnded?.Invoke();
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right) return;
            decimal increment = Math.Max(Quantize(step * Speed(e.Modifiers), 0, step, decimals), (decimal)Math.Pow(10, -decimals));
            decimal value = read();
            if (value < minimum || value > maximum) { e.Handled = true; return; }
            bool right = e.KeyCode == Keys.Right;
            decimal next = value + (right ? increment : -increment);
            write(Quantize(next, minimum, maximum, decimals));
            e.Handled = true;
        }
    }
}
