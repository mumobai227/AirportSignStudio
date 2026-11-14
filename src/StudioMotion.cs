using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AirportSignStudio
{
    // UI-thread animation only. The timer is stopped at rest and disposed with its owner.
    internal sealed class StudioMotion : IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(uint action, uint parameter, out bool value, uint flags);
        readonly Control owner;
        readonly Action<float> changed;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly Stopwatch clock = new Stopwatch();
        Form host;
        float from;
        public float Value { get; private set; }
        public float Target { get; private set; }
        public bool Running { get { return timer.Enabled; } }
        public static bool Enabled
        {
            get { bool enabled; return !SystemInformation.HighContrast && (!SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled); }
        }
        public StudioMotion(Control control, Action<float> update)
        {
            owner = control; changed = update;
            timer.Tick += delegate { if (!owner.Visible || !Enabled) Finish(); else Advance(clock.Elapsed.TotalMilliseconds); };
            owner.VisibleChanged += VisibilityChanged;
        }
        void VisibilityChanged(object sender, EventArgs e) { if (!owner.Visible) Finish(); }
        public void To(float target, bool animate = true)
        {
            Form next = owner.FindForm();
            if (host != next)
            {
                if (host != null) host.VisibleChanged -= VisibilityChanged;
                host = next; if (host != null) host.VisibleChanged += VisibilityChanged;
            }
            Target = target; from = Value;
            if (!animate || !owner.IsHandleCreated || !owner.Visible || !Enabled || Math.Abs(target - Value) < 0.001f) { Finish(); return; }
            clock.Restart(); timer.Start();
        }
        internal void Advance(double milliseconds)
        {
            float t = (float)Math.Min(1, Math.Max(0, milliseconds / 180));
            Value = from + (Target - from) * (1 - (1 - t) * (1 - t) * (1 - t));
            if (t >= 1) { timer.Stop(); clock.Stop(); }
            changed(Value);
        }
        public void Finish() { timer.Stop(); clock.Stop(); Value = Target; changed(Value); }
        public void Dispose() { owner.VisibleChanged -= VisibilityChanged; if (host != null) host.VisibleChanged -= VisibilityChanged; timer.Dispose(); clock.Stop(); }
    }
}
