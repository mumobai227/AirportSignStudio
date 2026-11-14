using System;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;

#if !NET10_0_OR_GREATER
[assembly: TargetFramework(".NETFramework,Version=v4.8")]
#endif

namespace AirportSignStudio
{
    public class DpiForm : Form
    {
        internal bool LayoutReady;
        protected DpiForm()
        {
            SuspendLayout();
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = DpiLayout.Font(9.5f);
            BackColor = StudioTheme.Surface; ForeColor = StudioTheme.Ink;
        }
        protected void CompleteLayout()
        {
            StudioTheme.Apply(this);
            ResumeLayout(true);
            LayoutReady = true;
        }
    }

    internal static class DpiLayout
    {
        static readonly string family = FontFamily.Families.Any(f => f.Name == "Microsoft YaHei UI") ? "Microsoft YaHei UI" : SystemFonts.MessageBoxFont.FontFamily.Name;
        public static Font Font(float points) { return new Font(family, points); }
        public static Font Font(Control owner, float points, FontStyle style)
        {
            Form form = owner.FindForm();
            float fontScale = form == null ? 1 : form.Font.SizeInPoints / 9.5f;
            return new Font(family, points * fontScale, style);
        }
        public static void ScaleNewChildren(Control parent)
        {
            StudioTheme.Apply(parent);
            DpiForm form = parent.FindForm() as DpiForm;
            // The initial batch is scaled once by the form. Later batches start in 96-DPI units.
            if (form == null || !form.LayoutReady) return;
            float scale = form.DeviceDpi / 96f;
            if (Math.Abs(scale - 1) < 0.001f) return;
            foreach (Control child in parent.Controls) child.Scale(new SizeF(scale, scale));
        }
    }
}
