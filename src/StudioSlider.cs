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
        readonly bool logarithmic;
        bool dragging;
        int lastX;
        double position;
        decimal dragValue;
        public event Action DragStarted, DragEnded;

        public StudioSlider(Func<decimal> read, Action<decimal> write, decimal minimum, decimal maximum, decimal step, int decimals, bool logarithmic = false)
        {
            this.read = read; this.write = write; this.minimum = minimum; this.maximum = maximum;
            this.step = step; this.decimals = decimals; this.logarithmic = logarithmic;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider;
            AccessibleDescription = "左右方向键调整。Ctrl 为十分之一速度，Shift 为百分之一速度。";
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
        double Fraction(decimal value)
        {
            value = Math.Clamp(value, minimum, maximum);
            if (logarithmic) return Math.Log((double)(value / minimum)) / Math.Log((double)(maximum / minimum));
            return (double)((value - minimum) / (maximum - minimum));
        }
        decimal ValueAt(double fraction)
        {
            if (fraction <= 0) return minimum;
            if (fraction >= 1) return maximum;
            return logarithmic ? minimum * (decimal)Math.Pow((double)(maximum / minimum), fraction) :
                minimum + (maximum - minimum) * (decimal)fraction;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = StudioTheme.Scale(this), center = Height / 2f, end = Inset + Travel;
            float thumb = Inset + (float)Fraction(read()) * Travel;
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
            Focus(); DragStarted?.Invoke();
            dragging = true; Capture = true; lastX = e.X;
            dragValue = Math.Clamp(read(), minimum, maximum);
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
            if (speed == 1)
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
            write(Quantize(read() + (e.KeyCode == Keys.Right ? increment : -increment), minimum, maximum, decimals));
            e.Handled = true;
        }
    }
}
