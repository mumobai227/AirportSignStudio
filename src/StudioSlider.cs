using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AirportSignStudio
{
    internal sealed class StudioSlider : Control
    {
        readonly Func<decimal> read;
        readonly Action<decimal> write;
        readonly decimal minimum, maximum, step;
        readonly int decimals;
        readonly Func<decimal> relativeStep;
        readonly Func<decimal, decimal> startValue;
        bool dragging;
        int lastX;
        double position;
        decimal dragValue;
        decimal dragOrigin, dragStep;
        public event Action DragStarted, DragEnded;

        public StudioSlider(Func<decimal> read, Action<decimal> write, decimal minimum, decimal maximum, decimal step, int decimals,
            Func<decimal> relativeStep = null, Func<decimal, decimal> startValue = null)
        {
            this.read = read; this.write = write; this.minimum = minimum; this.maximum = maximum;
            this.step = step; this.decimals = decimals;
            this.relativeStep = relativeStep; this.startValue = startValue;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = relativeStep == null ? Cursors.Hand : Cursors.SizeWE; AccessibleRole = AccessibleRole.Slider;
            AccessibleDescription = (relativeStep == null ? "" : "从当前值向左右拖动调整。") +
                "左右方向键调整。Ctrl 为十分之一速度，Shift 为百分之一速度。";
        }

        public static StudioSlider ForNumber(NumericUpDown number, Func<decimal> relativeStep = null,
            Func<decimal, decimal> startValue = null, decimal? minimum = null)
        {
            decimal current = number.Value;
            StudioSlider slider = new StudioSlider(() => current, value => number.Value = value, minimum ?? number.Minimum,
                number.Maximum, number.Increment, number.DecimalPlaces, relativeStep, startValue);
            number.ValueChanged += delegate { current = number.Value; slider.Invalidate(); };
            return slider;
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
        decimal StartValue() { return Math.Clamp(startValue == null ? read() : startValue(read()), minimum, maximum); }
        double Fraction(decimal value)
        {
            value = Math.Clamp(value, minimum, maximum);
            return (double)((value - minimum) / (maximum - minimum));
        }
        decimal ValueAt(double fraction)
        {
            if (fraction <= 0) return minimum;
            if (fraction >= 1) return maximum;
            return minimum + (maximum - minimum) * (decimal)fraction;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = StudioTheme.Scale(this), center = Height / 2f, end = Inset + Travel;
            float thumb = relativeStep == null ? Inset + (float)Fraction(read()) * Travel : Width / 2f;
            if (relativeStep != null && dragging)
                thumb = Math.Clamp(thumb + (float)((dragValue - dragOrigin) / dragStep) * scale, Inset, end);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen rail = new Pen(StudioTheme.Selected, 3 * scale))
                e.Graphics.DrawLine(rail, Inset, center, end, center);
            Color active = Enabled ? StudioTheme.Ink : StudioTheme.Line;
            using (Pen fill = new Pen(active, 3 * scale))
                e.Graphics.DrawLine(fill, relativeStep == null ? Inset : Width / 2f, center, thumb, center);
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
            Focus(); DragStarted?.Invoke();
            dragValue = StartValue();
            if (relativeStep != null) { dragOrigin = dragValue; dragStep = relativeStep(); }
            dragging = true; Capture = true; lastX = e.X;
            if (relativeStep != null)
            {
                Invalidate();
                return;
            }
            position = Fraction(dragValue);
            double clicked = Math.Clamp((e.X - Inset) / Travel, 0f, 1f);
            if (Speed(ModifierKeys) == 1m && Math.Abs(clicked - position) * Travel > Inset)
            {
                position = clicked;
                dragValue = ValueAt(position);
                write(Quantize(dragValue, minimum, maximum, decimals));
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || e.X == lastX) return;
            decimal speed = Speed(ModifierKeys);
            double delta = (e.X - lastX) / (double)Travel;
            if (relativeStep != null)
            {
                decimal pixels = (e.X - lastX) / (decimal)StudioTheme.Scale(this);
                dragValue = FineDrag(dragValue, pixels, speed == 1 ? dragStep : step, ModifierKeys, minimum, maximum);
                Invalidate();
            }
            else if (speed == 1)
            {
                position = Math.Clamp(position + delta, 0, 1);
                dragValue = ValueAt(position);
            }
            else
            {
                // Use the field's unit for precision, even when its total range is large.
                decimal pixels = (e.X - lastX) / (decimal)StudioTheme.Scale(this);
                dragValue = FineDrag(dragValue, pixels, step, ModifierKeys, minimum, maximum);
                position = Fraction(dragValue);
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
            write(Quantize(StartValue() + (e.KeyCode == Keys.Right ? increment : -increment), minimum, maximum, decimals));
            e.Handled = true;
        }
    }
}
