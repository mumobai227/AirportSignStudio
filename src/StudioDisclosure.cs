using System;
using System.Drawing;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class StudioDisclosure : Panel
    {
        readonly StudioMotion motion;
        readonly StudioButton toggle;
        readonly Label summary;
        public Panel Content { get; private set; }
        public bool Expanded { get; private set; }
        [System.ComponentModel.DefaultValue("")]
        public string Summary { get { return summary.Text; } set { summary.Text = value; } }
        public StudioDisclosure()
        {
            DoubleBuffered = true; BackColor = StudioTheme.Surface;
            toggle = new StudioButton { Text = "布局与分辨率", Quiet = true, IconKind = StudioIcon.Grid, Disclosure = true, AccessibleName = "展开布局与分辨率" };
            summary = new Label { ForeColor = StudioTheme.Muted, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
            Content = new Panel { BackColor = StudioTheme.Surface, Enabled = false, Visible = false };
            Controls.Add(Content); Controls.Add(summary); Controls.Add(toggle);
            motion = new StudioMotion(this, value => { Content.Visible = value > 0; Height = (int)Math.Round((42 + 144 * value) * StudioTheme.Scale(this)); PerformLayout(); });
            toggle.Click += delegate { SetExpanded(!Expanded); };
            Height = 42;
        }
        public void SetExpanded(bool expanded, bool animate = true)
        {
            if (!expanded && Content.ContainsFocus) toggle.Focus();
            Expanded = expanded; toggle.Expanded = expanded;
            toggle.AccessibleName = (expanded ? "收起" : "展开") + "布局与分辨率";
            Content.Enabled = expanded; motion.To(expanded ? 1 : 0, animate); toggle.Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (toggle == null) return;
            float s = StudioTheme.Scale(this);
            toggle.SetBounds(0, 0, (int)(172 * s), (int)(34 * s));
            summary.SetBounds((int)(185 * s), 0, Math.Max(1, Width - (int)(190 * s)), (int)(34 * s));
            Content.SetBounds((int)(8 * s), (int)(42 * s), Math.Max(1, Width - (int)(16 * s)), (int)(136 * s));
        }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); if (motion != null) motion.To(Expanded ? 1 : 0, false); }
        protected override void Dispose(bool disposing) { if (disposing && motion != null) motion.Dispose(); base.Dispose(disposing); }
    }
}
