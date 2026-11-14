using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using AirportSignStudio;

#if !NET10_0_OR_GREATER
[assembly: TargetFramework(".NETFramework,Version=v4.8")]
#endif

// Sends DPI messages only to this test process's hidden forms; never changes Windows display settings.
static class VerifyDpi
{
    static int checks;
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref Rect rect);
    static void Assert(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Click(Button button) { typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty }); }
    static IEnumerable<Control> Tree(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (Control child in Tree(c)) yield return child; } }
    static void Prepare(Form form, int dpi)
    {
        IntPtr handle = form.Handle; foreach (Control c in Tree(form)) { handle = c.Handle; }
        int previous = form.DeviceDpi;
        float previousFont = form.Font.SizeInPoints;
        Rect rect = new Rect { Right = form.Width * dpi / previous, Bottom = form.Height * dpi / previous };
        SendMessage(form.Handle, 0x02E0, new IntPtr(dpi | (dpi << 16)), ref rect);
        form.PerformLayout();
        Assert(form.DeviceDpi == dpi, "DPI message reached " + form.GetType().Name);
        Assert(Math.Abs(form.Font.SizeInPoints - previousFont * dpi / previous) < 0.1f, "Form font follows DPI: " + form.GetType().Name);
    }
    static void CheckEditor(Control editor, int logicalWidth, int dpi)
    {
        foreach (Control c in Tree(editor)) { IntPtr handle = c.Handle; }
        editor.PerformLayout();
        foreach (Control field in editor.Controls)
        {
            Assert(Math.Abs(field.Width - logicalWidth * dpi / 96f) <= 2, "Fresh editor width at " + dpi + " DPI: " + field.GetType().Name + " " + field.Width);
            if (field.GetType() == typeof(Panel))
                foreach (Control c in field.Controls)
                    Assert(c.Bottom <= field.ClientSize.Height + 1, "Input fits its field: " + c.AccessibleName + " " + dpi);
        }
        foreach (Control c in Tree(editor))
        {
            if (c is TextBox && !(c.Parent is UpDownBase)) Assert(c.Height >= c.Font.Height, "TextBox does not clip its font: " + c.AccessibleName + " " + dpi);
            DataGridView grid = c as DataGridView;
            if (grid != null) foreach (DataGridViewRow row in grid.Rows) Assert(row.Height >= grid.Font.Height + 2, "Grid rows fit font at " + dpi + ": " + grid.AccessibleName + " row=" + row.Height + " font=" + grid.Font.Height);
        }
    }
    static void Save(Control control, string path)
    {
        using (Bitmap image = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb))
        { control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path, ImageFormat.Png); }
    }
    [STAThread] public static int Main(string[] args)
    {
#if NET10_0_OR_GREATER
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string directory = args[0];
        try
        {
            using (GlyphLibrary glyphs = new GlyphLibrary())
            {
                SignEngine engine = new SignEngine(glyphs);
                VerifyAtlasWorkflow.Run(engine, directory);
                VerifyStudioUi.Run(engine, directory);
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                {
                    CheckBasePalette(engine, directory, dpi);
                    using (MainForm form = new MainForm())
                    {
                        Prepare(form, dpi);
                        Control editor = (Control)Field(form, "editor");
                        ListBox navigation = (ListBox)Field(form, "navigation");
                        // Force a selection change after the synthetic DPI message, including the first type.
                        navigation.SelectedIndex = navigation.Items.Count - 1;
                        for (int repeat = 0; repeat < 2; repeat++) foreach (AssetKind kind in Enum.GetValues(typeof(AssetKind)))
                        {
                            navigation.SelectedIndex = (int)kind; CheckEditor(editor, 267, dpi);
                            Assert(((Settings)Field(form, "current")).Kind == kind, "Type list changes the active editor");
                            TextBox input = Tree(editor).OfType<TextBox>().FirstOrDefault(c => !(c.Parent is UpDownBase));
                            if (input != null && kind != AssetKind.TaxiSign) Assert(Math.Abs(input.Font.SizeInPoints - 14 * dpi / 96f) < 0.1f, "Fresh content font matches current DPI: " + kind + " " + dpi + " font=" + input.Font.SizeInPoints);
                        }
                        if (dpi == 96)
                        {
                            Save(form, Path.Combine(directory, "main-form-100.png"));
                            form.Size = form.MinimumSize; form.PerformLayout();
                            Save(form, Path.Combine(directory, "main-form-minimum.png"));
                        }
                        if (dpi == 144) { Save(editor, Path.Combine(directory, "main-editor-150.png")); Save(form, Path.Combine(directory, "main-form-150.png")); }
                    }
                    AtlasWorkspace w = new AtlasWorkspace(); w.Slots[0] = new Settings();
                    using (AtlasForm form = new AtlasForm(engine, w))
                    {
                        Prepare(form, dpi); SlotEditor editor = (SlotEditor)Field(form, "editor");
                        StudioDisclosure options = Tree(form).OfType<StudioDisclosure>().Single();
                        Assert(!options.Expanded && !options.Content.Enabled, "Layout options start collapsed without hidden tab stops");
                        string beforeLayout = ProjectStore.Encode(w);
                        options.SetExpanded(true, false); form.PerformLayout();
                        Assert(Math.Abs(options.Height - 186 * dpi / 96f) <= 1, "Expanded layout panel scales at " + dpi);
                        foreach (StudioField field in Tree(options).OfType<StudioField>())
                        {
                            Control input = field.Controls[0];
                            Assert(field.ClientRectangle.Contains(input.Bounds), "Expanded layout input fits its frame: " + input.AccessibleName);
                            Assert(input.Height >= input.Font.Height, "Expanded layout input fits its font");
                        }
                        if (dpi == 96) Save(form, Path.Combine(directory, "atlas-layout-expanded.png"));
                        options.SetExpanded(false, false);
                        Assert(ProjectStore.Encode(w) == beforeLayout, "Layout disclosure does not change engineering data");
                        foreach (AssetKind kind in Enum.GetValues(typeof(AssetKind)))
                        {
                            Settings value = new Settings { Kind = kind, CharacterHeight = kind == AssetKind.TaxiSign ? 300 : kind == AssetKind.StandSign ? 600 : 4000 };
                            if (kind == AssetKind.TaxiSign) value.Panels.Add(new SignPanel());
                            editor.LoadSlot(value); CheckEditor(editor, 308, dpi);
                        }
                        editor.LoadSlot(null); CheckEditor(editor, 308, dpi);
                        editor.LoadSlot(new Settings()); CheckEditor(editor, 308, dpi);
                        if (dpi == 96)
                        {
                            Save(form, Path.Combine(directory, "atlas-form-100.png"));
                            form.Size = form.MinimumSize; form.PerformLayout();
                            Save(form, Path.Combine(directory, "atlas-form-minimum.png"));
                        }
                        if (dpi == 192) { Save(editor, Path.Combine(directory, "atlas-editor-200.png")); Save(form, Path.Combine(directory, "atlas-form-200.png")); }
                    }
                    using (PaintForm form = new PaintForm(engine, w))
                    {
                        Prepare(form, dpi);
                        foreach (NumericUpDown n in Tree(form).OfType<NumericUpDown>())
                            Assert(Math.Abs(n.Parent.Width - 238 * dpi / 96f) < 2 && n.Height >= n.Font.Height && n.Parent.ClientRectangle.Contains(n.Bounds), "Paint input and rounded frame match DPI and font");
                        if (dpi == 192) Save(form, Path.Combine(directory, "paint-form-200.png"));
                    }
                    using (PolExportForm form = new PolExportForm(engine, w))
                    {
                        TextBox name = Tree(form).OfType<TextBox>().First(c => c.AccessibleName == "总 POL 名称");
                        int logicalClientWidth = name.ClientSize.Width;
                        Prepare(form, dpi);
                        Assert(Math.Abs(name.ClientSize.Width - logicalClientWidth * dpi / 96f) <= 2 && name.Height >= name.Font.Height, "POL name size matches font: dpi=" + dpi + " width=" + name.Width + " height=" + name.Height + " font=" + name.Font.Height);
                        ComboBox boundary = Tree(form).OfType<ComboBox>().First(c => c.AccessibleName == "POL 边界模式");
                        NumericUpDown bleed = Tree(form).OfType<NumericUpDown>().First(c => c.AccessibleName == "POL 出血 px");
                        DataGridView grid = Tree(form).OfType<DataGridView>().Single();
                        string artworkSize = Convert.ToString(grid.Rows[0].Cells[2].Value);
                        boundary.SelectedIndex = 1;
                        Assert(!bleed.Enabled && Convert.ToString(grid.Rows[0].Cells[2].Value) != artworkSize, "Cell mode updates preview size and disables bleed");
                        boundary.SelectedIndex = 0;
                        Assert(bleed.Enabled && Convert.ToString(grid.Rows[0].Cells[2].Value) == artworkSize, "Artwork mode restores bleed and preview size");
                        if (dpi == 120) Save(form, Path.Combine(directory, "pol-form-125.png"));
                        boundary.SelectedIndex = 1; Click(Tree(form).OfType<Button>().First(c => c.Text == "应用设置"));
                        Assert(w.PolBoundary == PolBoundaryMode.Cell && w.PolBleed == 2, "POL apply persists mode and retains saved bleed");
                    }
                    int liveChanges = 0;
                    string originalProject = ProjectStore.Encode(w);
                    using (ColorsForm form = new ColorsForm(w.Slots[0].Colors, true, value => { w.Slots[0].Colors = value; liveChanges++; }))
                    {
                        Prepare(form, dpi);
                        CheckBox enabled = Tree(form).OfType<CheckBox>().First();
                        TextBox hex = Tree(form).OfType<TextBox>().First();
                        Assert(!hex.Enabled, "Inherited color input starts disabled");
                        enabled.Checked = true; hex.Text = "#40C8FF";
                        Assert(hex.Enabled && hex.Height >= hex.Font.Height, "Color override enables scaled hex input");
                        Assert(liveChanges > 0 && w.Slots[0].Colors.Foreground == "#40C8FF", "Valid color edits update slot before Apply");
                        using (SignDocument d = engine.Build(w.Slots[0]))
                            Assert(d.Shapes.Where(p => p.Role == ColorRole.Foreground).All(p => p.Fill.ToArgb() == Color.FromArgb(64, 200, 255).ToArgb()), "Live slot edit reaches real artwork renderer");
                        hex.Text = "#12";
                        Assert(w.Slots[0].Colors.Foreground == "#40C8FF", "Incomplete hex keeps last valid preview");
                        hex.Text = "#40C8FF";
                        if (dpi == 120) Save(form, Path.Combine(directory, "colors-form-125.png"));
                        Click(Tree(form).OfType<Button>().First(c => c.Text == "全部恢复继承"));
                        Assert(!enabled.Checked && !hex.Enabled, "Reset restores inheritance");
                        Assert(ProjectStore.Encode(w) == originalProject, "Reset previews inherited colors immediately");
                        enabled.Checked = true; Click(Tree(form).OfType<Button>().First(c => c.Text == "应用颜色"));
                        Assert(form.Value.Foreground == "#40C8FF" && form.Value.Background == "", "Color apply retains per-role overrides");
                        Assert(w.Slots[0].Colors.Foreground == "#40C8FF", "Apply keeps live colors after closing");
                    }
                    string beforeCancel = ProjectStore.Encode(w);
                    using (ColorsForm form = new ColorsForm(w.Colors, false, value => w.Colors = value))
                    {
                        Prepare(form, dpi);
                        Tree(form).OfType<CheckBox>().First().Checked = true;
                        TextBox colorInput = Tree(form).OfType<TextBox>().First(); colorInput.Text = "#FF8000";
                        Assert(w.Colors.Foreground == "#FF8000", "Shared color previews before Apply");
                        using (ColorPicker picker = new ColorPicker(Color.FromArgb(255, 128, 0), "文字 / 箭头", color => colorInput.Text = ColorPicker.Hex(color)))
                        {
                            Prepare(picker, dpi);
                            TextBox hex = Tree(picker).OfType<TextBox>().Single(c => c.AccessibleName == "十六进制颜色"); hex.Text = "#2080C0";
                            Assert(w.Colors.Foreground == "#2080C0", "Nested picker updates live project");
                            picker.DialogResult = DialogResult.Cancel; picker.Close();
                            Assert(w.Colors.Foreground == "#FF8000", "Cancel picker restores the current color editing session");
                        }
                        form.DialogResult = DialogResult.Cancel; form.Close();
                    }
                    Assert(ProjectStore.Encode(w) == beforeCancel, "Cancel colors restores exact project and dirty state");
                    Color live = Color.Cyan;
                    using (ColorPicker picker = new ColorPicker(live, "文字 / 箭头", color => live = color))
                    {
                        Prepare(picker, dpi);
                        NumericUpDown[] values = Tree(picker).OfType<NumericUpDown>().ToArray();
                        TextBox hex = Tree(picker).OfType<TextBox>().Single(c => c.AccessibleName == "十六进制颜色");
                        values[0].Value = 120; values[1].Value = 100; values[2].Value = 100;
                        Assert(live.ToArgb() == Color.Lime.ToArgb() && hex.Text == "#00FF00", "HSB input syncs RGB, hex and preview");
                        values[3].Value = 64; values[4].Value = 200; values[5].Value = 255;
                        Assert(hex.Text == "#40C8FF", "RGB input syncs hex and preview");
                        ColorPlane plane = Tree(picker).OfType<ColorPlane>().First(c => !c.HueOnly);
                        ColorPlane hue = Tree(picker).OfType<ColorPlane>().First(c => c.HueOnly);
                        hue.Chosen(0, 0); plane.Chosen(1, 1);
                        Assert(live.ToArgb() == Color.Red.ToArgb(), "Hue and saturation/value planes drive preview");
                        typeof(ColorPlane).GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plane, new object[] { new Point(0, 0) });
                        Assert(live.ToArgb() == Color.White.ToArgb(), "Top-left color plane selects white at each DPI");
                        typeof(ColorPlane).GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plane, new object[] { new Point(plane.Width - 1, plane.Height - 1) });
                        Assert(live.ToArgb() == Color.Black.ToArgb(), "Bottom of color plane selects black at each DPI");
                        hex.Text = "#FF8000";
                        Assert(values[3].Value == 255 && values[4].Value == 128 && values[5].Value == 0, "Hex syncs RGB inputs");
                        hex.Text = "#12";
                        Assert(!Tree(picker).OfType<Button>().First(c => c.Text == "确定").Enabled && live.ToArgb() == Color.FromArgb(255, 128, 0).ToArgb(), "Incomplete picker hex cannot commit or change preview");
                        hex.Text = "#FF8000";
                        foreach (Control c in picker.Controls) Assert(picker.ClientRectangle.Contains(c.Bounds), "Picker control fits scaled dialog: " + c.AccessibleName);
                        if (dpi == 120) Save(picker, Path.Combine(directory, "ps-color-picker-125.png"));
                        Click(Tree(picker).OfType<Button>().First(c => c.Text == "确定"));
                        Assert(live.ToArgb() == Color.FromArgb(255, 128, 0).ToArgb(), "Confirm picker preserves chosen color");
                    }
                    Console.WriteLine("PASS UI {0}%: 6 forms, live PS-style picker, cancellation, POL controls and field/font sizes", dpi * 100 / 96);
                }
                using (MainForm form = new MainForm())
                {
                    Control editor = (Control)Field(form, "editor");
                    MethodInfo select = typeof(MainForm).GetMethod("SelectKind", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (int dpi in new[] { 144, 192, 120, 96 })
                    {
                        Prepare(form, dpi);
                        select.Invoke(form, new object[] { AssetKind.GroundTaxi }); CheckEditor(editor, 267, dpi);
                        TextBox input = Tree(editor).OfType<TextBox>().First(c => !(c.Parent is UpDownBase));
                        Assert(Math.Abs(input.Font.SizeInPoints - 14 * dpi / 96f) < 0.1f, "Content font survives DPI round trip");
                    }
                }
            }
            string result = "PASS: " + checks + " UI assertions. Synthetic WM_DPICHANGED tests at 100/125/150/200 percent on hidden native WinForms; dynamic controls rebuilt across all types without accumulating scale. Native DrawToBitmap snapshots saved. Windows display settings were not changed. Real secondary-device and physical monitor transitions still require user verification.\n";
            File.WriteAllText(Path.Combine(directory, "dpi-test-results.txt"), result); Console.WriteLine(result); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void CheckBasePalette(SignEngine engine, string directory, int dpi)
    {
        AtlasWorkspace w = new AtlasWorkspace(); w.Slots[0] = new Settings();
        AtlasHistory history = new AtlasHistory(w); string before = ProjectStore.Encode(w);
        Action<ColorPreset> changed = value => { w.Colors = value.Common; w.TypeColors = value.Types; w.BaseColors = value.BaseColors; history.Record(new[] { 0 }); };
        history.Begin(new[] { 0 });
        using (PaletteForm form = new PaletteForm(new ColorPreset(), changed))
        {
            Prepare(form, dpi);
            Assert(!Tree(form).OfType<ComboBox>().Any() && !Tree(form).OfType<CheckBox>().Any(), "Palette shows four colors without type selectors");
            for (int i = 0; i < 4; i++)
            {
                Button swatch = Tree(form).OfType<Button>().Single(b => b.AccessibleName == BaseColorSettings.Names[i] + "配色");
                Assert(swatch.Text == ColorPicker.Hex(BaseColorSettings.Defaults[i]) && swatch.Height >= swatch.Font.Height, "Default swatch is correct and fits its font");
            }
            Assert(ProjectStore.Encode(w) == before, "Opening palette does not modify the project");
            MethodInfo set = typeof(PaletteForm).GetMethod("SetColor", BindingFlags.Instance | BindingFlags.NonPublic);
            set.Invoke(form, new object[] { 0, "#40C8FF" }); set.Invoke(form, new object[] { 1, "#292A2B" });
            Assert(w.BaseColors.Yellow == "#40C8FF" && w.BaseColors.Black == "#292A2B" && w.BaseColors.White == "" && w.BaseColors.Red == "", "Live palette edits change only their color classes");
            Click(Tree(form).OfType<Button>().Single(b => b.AccessibleName == "重置黑色"));
            Assert(w.BaseColors.Black == "" && w.BaseColors.Yellow == "#40C8FF", "Per-color reset preserves other colors");
            if (dpi == 96 || dpi == 192) Save(form, Path.Combine(directory, "base-colors-" + dpi + ".png"));
            form.DialogResult = DialogResult.Cancel; form.Close();
        }
        history.End(new[] { 0 });
        Assert(ProjectStore.Encode(w) == before && !history.CanUndo, "Cancel palette restores exact project and creates no history");
        history.Begin(new[] { 0 });
        using (PaletteForm form = new PaletteForm(new ColorPreset(), changed))
        {
            Prepare(form, dpi);
            ColorPreset preset = new ColorPreset { Name = "测试预设", BaseColors = new BaseColorSettings { Yellow = "#E8B754", Black = "#252625" } };
            typeof(PaletteForm).GetMethod("ApplyPreset", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { preset });
            preset.BaseColors.Yellow = "#123456";
            Assert(w.BaseColors.Yellow == "#E8B754", "Imported palette is an independent copy");
            Click(Tree(form).OfType<Button>().Single(b => b.Text == "应用配色"));
        }
        history.End(new[] { 0 }); string applied = ProjectStore.Encode(w);
        Assert(history.CanUndo && applied != before, "Confirmed palette is one undo operation");
        Assert(history.Undo() && ProjectStore.Encode(w) == before && !history.CanUndo, "Undo restores the complete palette in one step");
        Assert(history.Redo() && ProjectStore.Encode(w) == applied, "Redo restores all palette colors");
        using (ColorsForm form = new ColorsForm(new ColorSettings(), true, null, new ColorSettings { Foreground = "#F0D030", Background = "#292A2B", Border = "#FFFFFF" }))
        {
            Prepare(form, dpi);
            Assert(Tree(form).OfType<TextBox>().First().Text == "#F0D030", "Individual picker displays its inherited color");
        }
    }
}
