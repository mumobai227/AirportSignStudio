using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AirportSignStudio;

static partial class Verify
{
    static void VerifyColorsAndBoundaries(SignEngine engine, string directory)
    {
        ColorSettings shared = new ColorSettings { Foreground = "#F4BC39", Background = "#18374D", Border = "#D6E0E8" };
        foreach (AssetKind kind in Enum.GetValues(typeof(AssetKind)))
        {
            Settings s = new Settings { Kind = kind, Text = kind == AssetKind.GroundOther ? "36" : "23", CharacterHeight = kind == AssetKind.TaxiSign ? 300 : kind == AssetKind.StandSign ? 600 : 4000 };
            if (kind == AssetKind.TaxiSign) { s.Panels.Add(new SignPanel()); s.Panels.Add(new SignPanel { Text = "B2" }); }
            using (SignDocument before = engine.Build(s))
            using (SignDocument after = engine.Build(s, shared))
            {
                Assert(before.Width == after.Width && before.Height == after.Height && before.Shapes.Count == after.Shapes.Count, "Color does not change dimensions: " + kind);
                for (int i = 0; i < before.Shapes.Count; i++)
                {
                    Shape shape = after.Shapes[i];
                    string expected = shape.Role == ColorRole.Foreground ? shared.Foreground : shape.Role == ColorRole.Background ? shared.Background : shared.Border;
                    Assert(shape.Fill.ToArgb() == ColorTranslator.FromHtml(expected).ToArgb(), "Role recolored: " + kind + " " + shape.Role);
                    Assert(shape.Path.PathPoints.SequenceEqual(before.Shapes[i].Path.PathPoints), "Recolor keeps vector geometry: " + kind);
                }
            }
            s.Colors.Foreground = "#40C8FF";
            using (SignDocument d = engine.Build(s, shared))
                Assert(d.Shapes.Where(p => p.Role == ColorRole.Foreground).All(p => p.Fill.ToArgb() == Color.FromArgb(64, 200, 255).ToArgb()), "Individual foreground overrides shared color: " + kind);
            Settings copied = s.Copy(); copied.Colors.Foreground = "#FFFFFF";
            Assert(s.Colors.Foreground == "#40C8FF", "Copied color settings are independent");
        }
        Throws(() => new ColorSettings { Foreground = "#XYZ123" }.Validate(), "Reject malformed color");
        Throws(() => new ColorSettings { Background = "#00112233" }.Validate(), "Reject alpha color input");

        AtlasWorkspace w = new AtlasWorkspace { Width = 1024, Height = 512, Padding = 18, Gap = 16, Colors = shared, PolPrefix = "ColorDemo" };
        w.Slots[0] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "→" };
        w.Slots[1] = new Settings { Text = "B2", Colors = new ColorSettings { Foreground = "#FF8844" } };
        w.Slots[2] = new Settings { Kind = AssetKind.GroundOther, Text = "A320", OtherStyle = "机型文字", CharacterHeight = 300, BlackBackground = false, Border = false, Colors = new ColorSettings { Foreground = "#40C8FF" } };
        w.Slots[3] = new Settings { Text = "A1" };
        w.Slots[4] = new Settings { Kind = AssetKind.GroundOther, Text = "36", Colors = new ColorSettings { Background = "#47342E", Border = "#FF8844" } };
        w.Merge(new[] { 0, 1 }); w.MoveDivider(w.Divider(0, AtlasEdge.Right), 0.60f);
        w.PolBoundary = PolBoundaryMode.Cell; w.PolBleed = 5;
        string saved = ProjectStore.Encode(w);
        AtlasWorkspace restored = ProjectStore.Decode(saved), applied = new AtlasWorkspace(); ProjectStore.Apply(applied, restored);
        Assert(ProjectStore.Encode(restored) == saved && ProjectStore.Encode(applied) == saved, "Colors and boundary survive save/load/apply");
        applied.Colors.Foreground = "#FFFFFF"; Assert(restored.Colors.Foreground == shared.Foreground, "Applied shared colors are independent");
        restored.Split(0); Assert(restored.Slots[1].Colors.Foreground == "#FF8844", "Hidden merged colors restored on split");
        XDocument old = XDocument.Parse(saved); old.Descendants("Colors").Remove(); old.Root.Element("PolBoundary").Remove();
        AtlasWorkspace migrated = ProjectStore.Decode(old.ToString());
        Assert(!migrated.Colors.Active && migrated.Slots.Where(s => s != null).All(s => !s.Colors.Active) && migrated.PolBoundary == PolBoundaryMode.Artwork, "Missing new fields preserve old colors and artwork boundaries");
        XDocument invalid = XDocument.Parse(saved); invalid.Root.Element("Colors").Element("Foreground").Value = "bad";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Invalid project colors rejected");

        using (AtlasLayout layout = AtlasLayout.Build(engine, w))
        using (AtlasLayout reloaded = AtlasLayout.Build(engine, ProjectStore.Decode(saved)))
        {
            Assert(Exporter.Svg(layout.Document, true) == Exporter.Svg(reloaded.Document, true), "Reopened color project renders identically");
            AtlasSlotInfo[] slots = layout.Slots.Where(s => s.Visible && s.Occupied).ToArray();
            Assert(slots.Length == 4 && slots.All(s => s.CustomColors), "Visible custom-color regions identified");
            Assert(layout.Slots[2].Artwork.Width < layout.Slots[2].Image.Width, "Custom background does not fill transparent text margins");
            PaintSettings paint = PaintSettings.Preset(1); paint.ReferenceYellow = true;
            using (Bitmap warm = PaintRenderer.Raster(layout, paint, 0))
            {
                paint.ReferenceYellow = false;
                using (Bitmap exact = PaintRenderer.Raster(layout, paint, 0)) Assert(Pixels(warm).SequenceEqual(Pixels(exact)), "Reference warm yellow cannot replace custom colors");
                using (Bitmap clean = Exporter.RasterPixels(layout.Document)) Assert(!Pixels(warm).SequenceEqual(Pixels(clean)), "Paint texture still affects custom colors");
            }
            foreach (PolBoundaryMode mode in Enum.GetValues(typeof(PolBoundaryMode)))
            {
                w.PolBoundary = mode;
                string[] lines = PolExporter.Text(w, slots).Split('\n'), uvLines = lines.Where(l => l.StartsWith("#subtex ")).ToArray();
                Assert(uvLines.Length == slots.Length, "Boundary mode keeps one section per visible occupied region");
                for (int i = 0; i < slots.Length; i++)
                {
                    AtlasSlotInfo s = slots[i]; RectangleF expected = mode == PolBoundaryMode.Cell ? s.Cell : RectangleF.Intersect(RectangleF.Inflate(s.Artwork, 5, 5), s.Cell);
                    Assert(PolExporter.RegionBounds(s, 5, mode) == expected, "Boundary follows merged/adjusted region or artwork");
                    float[] uv = uvLines[i].Split(' ').Skip(1).Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                    Near(uv[0] * w.Width, expected.Left, "Selected left boundary exported"); Near(uv[2] * w.Width, expected.Right, "Selected right boundary exported");
                    Near((1 - uv[1]) * w.Height, expected.Bottom, "Selected bottom boundary exported"); Near((1 - uv[3]) * w.Height, expected.Top, "Selected top boundary exported");
                    int line = Array.IndexOf(lines, "# Region " + (s.Index + 1));
                    float[] meters = lines[line + 1].Split(':')[1].Trim().Split(' ').Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                    Near(meters[0], expected.Width / (s.Scale * 1000), "Physical width uses chosen boundary"); Near(meters[1], expected.Height / (s.Scale * 1000), "Physical height uses chosen boundary");
                }
                string folder = Path.Combine(directory, "颜色与边界示例", mode.ToString()); PolExporter.Export(engine, w, folder);
                Assert(Directory.GetFiles(folder).Length == 2, "Still exports one PNG and one total POL");
                ProjectStore.Save(w, Path.Combine(directory, "颜色与边界示例", mode + ".assproj"));
            }
            Assert(PolExporter.RegionBounds(slots[0], 0, PolBoundaryMode.Cell) == PolExporter.RegionBounds(slots[0], 32, PolBoundaryMode.Cell), "Cell mode ignores saved bleed without expanding outside cell");
            Throws(() => PolExporter.RegionBounds(slots[0], 2, (PolBoundaryMode)99), "Invalid boundary rejected");
            using (Bitmap left = new Bitmap(Path.Combine(directory, "颜色与边界示例", "Artwork", "ColorDemo_atlas.png")))
            using (Bitmap right = new Bitmap(Path.Combine(directory, "颜色与边界示例", "Cell", "ColorDemo_atlas.png")))
                Assert(Pixels(left).SequenceEqual(Pixels(right)), "POL boundary choice never changes PNG pixels");
            using (Bitmap sheet = new Bitmap(1080, 1160))
            using (Bitmap png = Exporter.RasterPixels(layout.Document))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Microsoft YaHei UI", 16))
            using (Pen border = new Pen(Color.FromArgb(68, 224, 183), 2))
            {
                g.Clear(Color.FromArgb(48, 55, 60));
                for (int i = 0; i < 2; i++)
                {
                    int y = i * 572;
                    g.DrawString(i == 0 ? "实际图案边界 + 5 px 出血" : "分割区域边界（包含格内留白）", font, Brushes.White, 28, y + 14);
                    g.DrawImageUnscaled(png, 28, y + 52);
                    foreach (AtlasSlotInfo s in slots)
                    {
                        RectangleF box = PolExporter.RegionBounds(s, 5, (PolBoundaryMode)i); box.Offset(28, y + 52);
                        g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
                    }
                }
                sheet.Save(Path.Combine(directory, "颜色与POL边界对照.png"), ImageFormat.Png);
            }
        }
        Console.WriteLine("PASS v3.9: shared/individual colors, transparent artwork, paint priority, persistence, merged/adjusted POL modes and direct export.");
    }
}
