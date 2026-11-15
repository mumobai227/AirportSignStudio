using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public enum PolBoundaryMode { Artwork, Cell }

    public static class PolExporter
    {
        public static readonly string[] Layers = { "terrain", "beaches", "shoulders", "taxiways", "runways", "markings", "airports", "roads", "objects", "light_objects", "cars" };
        static string N(double n) { return n.ToString("0.#########", CultureInfo.InvariantCulture); }
        public static string Name(string name)
        {
            if (name == null || !Regex.IsMatch(name, @"\A[A-Za-z0-9_-]{1,60}\z")) throw new ArgumentException("POL 名称请使用 1–60 个英文字母、数字、下划线或短横线，不含扩展名。");
            if (Regex.IsMatch(name, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase)) throw new ArgumentException("此名称是 Windows 保留名称，请换一个 POL 名称。");
            return name;
        }
        public static RectangleF RegionBounds(AtlasSlotInfo slot, int bleed, PolBoundaryMode mode = PolBoundaryMode.Artwork)
        {
            if (bleed < 0 || bleed > 32) throw new ArgumentException("出血范围支持 0–32 px。");
            if (!Enum.IsDefined(typeof(PolBoundaryMode), mode)) throw new ArgumentException("请选择有效的 POL 边界模式。");
            if (mode == PolBoundaryMode.Cell) return slot.Cell;
            return RectangleF.Intersect(RectangleF.Inflate(slot.Artwork, bleed, bleed), slot.Cell);
        }
        public static string Text(AtlasWorkspace w, AtlasSlotInfo[] slots)
        {
            if (!Layers.Contains(w.PolLayer) || w.PolOffset < -5 || w.PolOffset > 5) throw new ArgumentException("LAYER_GROUP 或偏移无效（支持 -5 到 5）。");
            AtlasSlotInfo reference = slots[0];
            StringBuilder text = new StringBuilder("A\n850\nDRAPED_POLYGON\n\nTEXTURE_NOWRAP " + Name(w.PolPrefix.Trim()) + "_atlas.png\n" +
                "SCALE " + N(w.Width / (reference.Scale * 1000d)) + " " + N(w.Height / (reference.Scale * 1000d)) + "\n" +
                "LAYER_GROUP " + w.PolLayer + " " + w.PolOffset.ToString(CultureInfo.InvariantCulture) + "\n" +
                "# Select a subtexture in WED with Use Texture Map enabled.\n" +
                "# Boundary mode: " + w.PolBoundary + ".\n" +
                (w.PolBoundary == PolBoundaryMode.Cell ? "# Cell boundary includes inner padding; bleed is not applied.\n" : "# Bleed margin (pixels): " + w.PolBleed + "; clamped to each layout cell.\n") +
                "# SCALE uses region " + (reference.Index + 1) + " pixel density as a reference; place each subtexture at its listed size.\n");
            foreach (AtlasSlotInfo s in slots)
            {
                RectangleF c = RegionBounds(s, w.PolBleed, w.PolBoundary);
                text.Append("\n# Region " + (s.Index + 1) + "\n" +
                    "# Region size (meters): " + N(c.Width / (s.Scale * 1000d)) + " " + N(c.Height / (s.Scale * 1000d)) + "\n" +
                    "#subtex " + N(c.Left / w.Width) + " " + N(1 - c.Bottom / w.Height) + " " + N(c.Right / w.Width) + " " + N(1 - c.Top / w.Height) + "\n");
            }
            return text.ToString();
        }
        public static void Export(SignEngine engine, AtlasWorkspace w, string folder)
        {
            using (AtlasLayout layout = AtlasLayout.Build(engine, w))
            {
                if (!layout.CanExport) throw new ArgumentException("请先设置标牌，并修正所有可见区域的输入。");
                AtlasSlotInfo[] slots = layout.Slots.Where(s => s.Visible && s.Occupied).ToArray();
                string name = Name(w.PolPrefix.Trim()), text = Text(w, slots);
                Directory.CreateDirectory(folder);
                string[] files = { Path.Combine(folder, name + "_atlas.png"), Path.Combine(folder, name + ".pol") };
                string id = "." + Guid.NewGuid().ToString("N");
                string[] temps = files.Select(f => f + id + ".tmp").ToArray(), backups = files.Select(f => f + id + ".bak").ToArray();
                bool[] installed = new bool[2]; bool completed = false;
                try
                {
                    using (Bitmap bitmap = PaintRenderer.Raster(layout, w.Paint, 0)) bitmap.Save(temps[0], ImageFormat.Png);
                    File.WriteAllText(temps[1], text, new UTF8Encoding(false));
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (File.Exists(files[i])) File.Replace(temps[i], files[i], backups[i]);
                        else File.Move(temps[i], files[i]);
                        installed[i] = true;
                    }
                    completed = true;
                }
                catch
                {
                    // Restore the previous pair if the second replacement fails (for example, an open POL).
                    for (int i = files.Length - 1; i >= 0; i--) if (installed[i])
                    {
                        if (File.Exists(backups[i])) File.Replace(backups[i], files[i], null);
                        else File.Delete(files[i]);
                    }
                    throw;
                }
                finally
                {
                    foreach (string temp in temps) if (File.Exists(temp)) File.Delete(temp);
                    if (completed) foreach (string backup in backups) if (File.Exists(backup)) File.Delete(backup);
                }
            }
        }
    }

    public sealed class PolExportForm : DpiForm
    {
        public PolExportForm(SignEngine engine, AtlasWorkspace w)
        {
            Text = "POL 名称、图层与导出"; ClientSize = new Size(800, 550); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterParent;
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 154, Padding = new Padding(12) };
            TextBox prefix = new TextBox { Text = w.PolPrefix, Width = 170, MaxLength = 60, AccessibleName = "总 POL 名称" };
            ComboBox layer = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = "POL layer" }; layer.Items.AddRange(PolExporter.Layers); layer.SelectedItem = w.PolLayer;
            NumericUpDown offset = new NumericUpDown { Minimum = -5, Maximum = 5, Value = w.PolOffset, Width = 122, AccessibleName = "POL 图层偏移" };
            NumericUpDown bleed = new NumericUpDown { Minimum = 0, Maximum = 32, Value = w.PolBleed, Width = 122, AccessibleName = "POL 出血 px" };
            options.Controls.Add(new Label { Text = "总 POL 名称", AutoSize = true, Margin = new Padding(3, 10, 3, 0) }); options.Controls.Add(StudioTheme.FrameInput(prefix, 34));
            options.Controls.Add(new Label { Text = "layer", AutoSize = true, Margin = new Padding(8, 10, 3, 0) }); options.Controls.Add(StudioTheme.FrameInput(layer, 34));
            options.Controls.Add(new Label { Text = "偏移", AutoSize = true, Margin = new Padding(8, 10, 3, 0) }); StudioField offsetField = StudioTheme.FrameInput(offset, 34); options.Controls.Add(offsetField);
            options.SetFlowBreak(offsetField, true);
            ComboBox boundary = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "POL 边界模式" };
            boundary.Items.AddRange(new object[] { "实际图案边界（含出血）", "分割区域边界（含格内留白）" }); boundary.SelectedIndex = (int)w.PolBoundary;
            options.Controls.Add(new Label { Text = "分区边界", AutoSize = true, Margin = new Padding(3, 10, 3, 0) }); StudioField boundaryField = StudioTheme.FrameInput(boundary, 34); options.Controls.Add(boundaryField); options.SetFlowBreak(boundaryField, true);
            options.Controls.Add(new Label { Text = "出血 px", AutoSize = true, Margin = new Padding(3, 10, 3, 0) }); options.Controls.Add(StudioTheme.FrameInput(bleed, 34));
            DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, AccessibleName = "总 POL 分区预览" };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "区域", ReadOnly = true, FillWeight = 20 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标牌内容", ReadOnly = true, FillWeight = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分区尺寸 / px", FillWeight = 40 });
            AtlasSlotInfo[] slots;
            using (AtlasLayout layout = AtlasLayout.Build(engine, w))
                slots = layout.Slots.Where(s => s.Visible && s.Occupied).ToArray();
            Action refresh = delegate
            {
                bleed.Enabled = boundary.SelectedIndex == (int)PolBoundaryMode.Artwork;
                grid.Rows.Clear();
                foreach (AtlasSlotInfo s in slots)
                {
                    RectangleF bounds = PolExporter.RegionBounds(s, (int)bleed.Value, (PolBoundaryMode)boundary.SelectedIndex);
                    grid.Rows.Add(s.Index + 1, s.Caption, bounds.Width.ToString("0.#") + " × " + bounds.Height.ToString("0.#"));
                }
            };
            bleed.ValueChanged += delegate { refresh(); }; boundary.SelectedIndexChanged += delegate { refresh(); }; refresh();
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(10) };
            Button apply = new StudioButton { Text = "应用设置", Width = 105, Height = 32 }, export = new StudioButton { Text = "导出 PNG + POL…", Primary = true, Width = 165, Height = 32 };
            Action commit = delegate { w.PolPrefix = prefix.Text.Trim(); w.PolLayer = (string)layer.SelectedItem; w.PolOffset = (int)offset.Value; w.PolBleed = (int)bleed.Value; w.PolBoundary = (PolBoundaryMode)boundary.SelectedIndex; };
            apply.Click += delegate { commit(); Close(); };
            export.Click += delegate
            {
                commit();
                try { PolExporter.Name(w.PolPrefix); }
                catch (ArgumentException error) { MessageBox.Show(this, error.Message, "名称无效"); return; }
                using (FolderBrowserDialog save = new FolderBrowserDialog { Description = "输出文件夹", ShowNewFolderButton = true })
                {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    string png = w.PolPrefix + "_atlas.png", pol = w.PolPrefix + ".pol";
                    if ((File.Exists(Path.Combine(save.SelectedPath, png)) || File.Exists(Path.Combine(save.SelectedPath, pol))) &&
                        MessageBox.Show(this, "输出文件已存在，是否覆盖这一组文件？\n" + png + "\n" + pol, "覆盖导出", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { UseWaitCursor = true; PolExporter.Export(engine, w, save.SelectedPath); MessageBox.Show(this, "已导出到：" + save.SelectedPath + "\n" + png + "\n" + pol, "导出完成"); }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "POL 导出失败"); }
                    finally { UseWaitCursor = false; }
                }
            };
            actions.Controls.Add(apply); actions.Controls.Add(export);
            Controls.Add(grid); Controls.Add(actions); Controls.Add(options);
            CompleteLayout();
        }
    }
}
