using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AirportSignStudio;

static class VerifyStudioUi
{
    static int checks;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
    static void Mouse(Control owner, string name) { typeof(Control).GetMethod(name, Private).Invoke(owner, new object[] { EventArgs.Empty }); }
    static void Save(Control owner, string path) { using (Bitmap bitmap = new Bitmap(owner.Width, owner.Height)) { owner.DrawToBitmap(bitmap, owner.ClientRectangle); bitmap.Save(path); } }
    public static void Run(SignEngine engine, string directory)
    {
        AtlasWorkspace workspace = new AtlasWorkspace(); workspace.Slots[0] = new Settings();
        string original = ProjectStore.Encode(workspace);
        using (AtlasForm form = new AtlasForm(engine, workspace))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
            form.Show(); Application.DoEvents();
            StudioDisclosure panel = (StudioDisclosure)Field(form, "layoutOptions");
            StudioMotion motion = (StudioMotion)Field(panel, "motion");
            Assert(Math.Abs((float)form.ClientSize.Height / form.ClientSize.Width - StudioTheme.Golden) < 0.001, "Default workspace uses golden window proportion");
            Assert(!motion.Running, "Collapsed panel has no idle animation timer");
            panel.SetExpanded(true);
            if (StudioMotion.Enabled)
            {
                Assert(motion.Running, "Visible disclosure starts transition"); motion.Advance(65);
                Assert(panel.Height > 42 && panel.Height < 186, "Disclosure occupies an intermediate height");
                int partial = panel.Height; panel.SetExpanded(false); motion.Advance(90);
                Assert(panel.Height < partial && panel.Height > 42, "Mid-transition reversal continues from displayed height");
            }
            motion.Advance(180);
            if (panel.Expanded) panel.SetExpanded(false, false);
            Assert(!motion.Running && panel.Height == 42 && !panel.Content.Visible && !panel.Content.Enabled, "Collapse settles exactly, disables fields and stops timer");
            panel.SetExpanded(true, false);
            NumericUpDown input = (NumericUpDown)Field(form, "columns"); input.Focus();
            Assert(input.ContainsFocus, "Expanded numeric field can receive focus");
            panel.SetExpanded(false, false);
            Assert(!panel.Content.ContainsFocus && !panel.Content.Enabled && panel.ContainsFocus, "Collapsing focused fields returns keyboard focus to header");
            panel.SetExpanded(true); form.Hide();
            Assert(!motion.Running && panel.Height == 186, "Hiding a form finishes active layout animation: running=" + motion.Running + " height=" + panel.Height + " value=" + motion.Value + " target=" + motion.Target + " visible=" + panel.Visible);
            Assert(ProjectStore.Encode(workspace) == original && !((AtlasHistory)Field(form, "history")).CanUndo, "All disclosure animations leave artwork, project and undo unchanged");
        }
        using (Form form = new Form { ClientSize = new Size(240, 80), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), BackColor = StudioTheme.Surface })
        using (StudioButton button = new StudioButton { Text = "保存工程", IconKind = StudioIcon.Save, Quiet = true, Bounds = new Rectangle(20, 20, 160, 36) })
        {
            form.Controls.Add(button); form.Show(); Application.DoEvents();
            StudioMotion motion = (StudioMotion)Field(button, "motion");
            Save(form, Path.Combine(directory, "motion-button-rest.png"));
            Mouse(button, "OnMouseEnter");
            if (StudioMotion.Enabled)
            {
                motion.Advance(60); Assert(motion.Value > 0 && motion.Value < motion.Target, "Hover has an intermediate visual state");
                Save(form, Path.Combine(directory, "motion-button-middle.png"));
            }
            motion.Advance(180); Assert(!motion.Running, "Hover stops at its final state");
            Save(form, Path.Combine(directory, "motion-button-hover.png"));
            Mouse(button, "OnMouseLeave"); motion.Advance(180);
            Assert(motion.Value == 0 && !motion.Running, "Leaving a button returns exactly to idle");
            Mouse(button, "OnMouseEnter"); button.Enabled = false;
            Assert(motion.Value == 0 && !motion.Running, "Disabled buttons cancel their transition");
            button.Enabled = true; Mouse(button, "OnMouseEnter"); form.Hide();
            Assert(!motion.Running, "Hidden buttons have no live animation timer");
        }
        string result = "PASS: " + checks + " motion/UI assertions. Offscreen native forms, deterministic intermediate/final frames, reversal, hide/disable cleanup, keyboard focus, no project or history changes. System animation preference respected.\n";
        File.WriteAllText(Path.Combine(directory, "studio-ui-results.txt"), result); Console.WriteLine(result);
    }
}
