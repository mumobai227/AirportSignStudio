using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AirportSignStudio;

static partial class Verify
{
    static void VerifyBaseColors(SignEngine engine, string directory)
    {
        BaseColorSettings palette = new BaseColorSettings { Yellow = "#E8B754", Black = "#252625", White = "#F1EEE8", Red = "#B72D36" };
        foreach (SignColorType type in Enum.GetValues(typeof(SignColorType)))
        {
            Settings s = TypeColorSettings.Sample(type);
            using (SignDocument original = engine.Build(s))
            using (SignDocument changed = engine.Build(s, null, null, palette))
            {
                Assert(original.Width == changed.Width && original.Height == changed.Height && original.Shapes.Count == changed.Shapes.Count, "Base colors preserve geometry: " + type);
                for (int i = 0; i < original.Shapes.Count; i++)
                {
                    Color? expected = palette.Replacement(original.Shapes[i].Fill);
                    Assert(expected.HasValue && expected.Value.ToArgb() == changed.Shapes[i].Fill.ToArgb(), "Original hue maps independently of type and role: " + type);
                    Assert(original.Shapes[i].Path.PathPoints.SequenceEqual(changed.Shapes[i].Path.PathPoints), "Base color change keeps vector points");
                }
                for (int color = 0; color < 4; color++)
                {
                    BaseColorSettings one = new BaseColorSettings(); one.Set(color, "#379ABC");
                    using (SignDocument single = engine.Build(s, null, null, one))
                        for (int i = 0; i < original.Shapes.Count; i++)
                            Assert(single.Shapes[i].Fill.ToArgb() == (one.Replacement(original.Shapes[i].Fill) ?? original.Shapes[i].Fill).ToArgb(), "Changing one color leaves every other base color untouched");
                }
            }
        }
        Settings combined = TypeColorSettings.Sample(SignColorType.TaxiDirection);
        combined.Panels.Add(new SignPanel { Kind = "位置", Text = "B" });
        using (SignDocument doc = engine.Build(combined, null, null, palette))
        {
            Assert(doc.Shapes.Any(s => s.ColorType == SignColorType.TaxiDirection && s.Role == ColorRole.Background && ColorPicker.Hex(s.Fill) == palette.Yellow), "Yellow direction background uses yellow class");
            Assert(doc.Shapes.Any(s => s.ColorType == SignColorType.TaxiLocation && s.Role == ColorRole.Foreground && ColorPicker.Hex(s.Fill) == palette.Yellow), "Yellow location text uses the same yellow class");
        }
        ColorSettings common = new ColorSettings { Foreground = "#111111", Background = "#222222", Border = "#333333" };
        TypeColorSettings types = new TypeColorSettings(); types.Set(SignColorType.TaxiLocation, new ColorSettings { Foreground = "#406080" });
        combined.Colors.Foreground = "#123456";
        using (SignDocument doc = engine.Build(combined, common, types, palette))
        {
            Assert(doc.Shapes.Where(s => s.Role == ColorRole.Foreground).All(s => ColorPicker.Hex(s.Fill) == "#123456"), "Single-slot override has highest priority");
            Assert(doc.Shapes.Any(s => s.ColorType == SignColorType.TaxiLocation && s.Role == ColorRole.Background && ColorPicker.Hex(s.Fill) == palette.Black), "Base palette overrides legacy common/type colors");
        }
        AtlasWorkspace w = new AtlasWorkspace { BaseColors = palette.Copy(), Colors = common, TypeColors = types };
        w.Slots[0] = combined; w.Slots[1] = TypeColorSettings.Sample(SignColorType.GroundLocation); w.Merge(new[] { 0, 1 });
        string saved = ProjectStore.Encode(w); AtlasWorkspace restored = ProjectStore.Decode(saved), copied = new AtlasWorkspace(); ProjectStore.Apply(copied, restored);
        Assert(ProjectStore.Encode(restored) == saved && ProjectStore.Encode(copied) == saved, "Base palette persists with legacy colors and hidden merged slots");
        copied.BaseColors.Yellow = "#000000"; Assert(restored.BaseColors.Yellow == palette.Yellow, "Copied palette is independent");
        XDocument old = XDocument.Parse(saved); old.Root.Element("BaseColors").Remove();
        AtlasWorkspace migrated = ProjectStore.Decode(old.ToString()); w.BaseColors = new BaseColorSettings();
        using (AtlasLayout expected = AtlasLayout.Build(engine, w))
        using (AtlasLayout actual = AtlasLayout.Build(engine, migrated))
            Assert(Exporter.Svg(expected.Document, true) == Exporter.Svg(actual.Document, true), "v3.16 project appearance remains intact until base colors are edited");
        ColorPreset preset = new ColorPreset { Name = "四色预设", BaseColors = palette.Copy(), Common = common.Copy(), Types = types.Copy() };
        string encoded = ColorPresetStore.Encode(preset); string path = Path.Combine(directory, "四色预设.asscolors"); ColorPresetStore.Save(preset, path);
        Assert(ColorPresetStore.Encode(ColorPresetStore.Decode(File.ReadAllText(path))) == encoded, "Four-color preset round trips with its name");
        XDocument oldPreset = XDocument.Parse(encoded); oldPreset.Root.Element("Version").Value = "1"; oldPreset.Root.Element("BaseColors").Remove();
        ColorPreset legacy = ColorPresetStore.Decode(oldPreset.ToString());
        Assert(!legacy.BaseColors.Active && legacy.Types.Get(SignColorType.TaxiLocation).Foreground == "#406080", "v1 presets retain their original type settings");
        Throws(() => new BaseColorSettings { Yellow = "invalid" }.Validate(), "Invalid base color rejected");
        Throws(() => ColorPresetStore.Decode(encoded.Replace("#E8B754", "bad")), "Invalid palette file rejected");

        // Black remapped to yellow must remain distinguishable from original yellow under paint.
        AtlasWorkspace paint = new AtlasWorkspace { Width = 512, Height = 512 }; paint.Resize(1, 1); paint.Slots[0] = new Settings();
        PaintSettings effect = PaintSettings.Preset(1);
        using (AtlasLayout before = AtlasLayout.Build(engine, paint))
        using (Bitmap clean = Exporter.RasterPixels(before.Document))
        using (Bitmap warm = PaintRenderer.Raster(before, effect, 0))
        {
            paint.BaseColors.Black = ColorPicker.Hex(SignEngine.Yellow);
            using (AtlasLayout changed = AtlasLayout.Build(engine, paint))
            using (Bitmap after = PaintRenderer.Raster(changed, effect, 0))
            using (Bitmap mask = Exporter.RasterPixels(changed.Document, true))
            {
                effect.ReferenceYellow = false;
                using (Bitmap exact = PaintRenderer.Raster(changed, effect, 0))
                {
                    byte[] a = Pixels(clean), b = Pixels(warm), c = Pixels(after), d = Pixels(exact), m = Pixels(mask);
                    int unchangedYellow = 0, protectedBlack = 0, errors = 0;
                    for (int y = 2; y < clean.Height - 2; y++) for (int x = 2; x < clean.Width - 2; x++)
                    {
                        int at = (y * clean.Width + x) * 4;
                        if (a[at + 3] != 255) continue;
                        if (a[at] == 0 && a[at + 1] == 214 && a[at + 2] == 255 && m[at] == 0 &&
                            m[at - 8] == 0 && m[at + 8] == 0 && m[at - clean.Width * 8] == 0 && m[at + clean.Width * 8] == 0)
                        { unchangedYellow++; if (!Enumerable.Range(0, 4).All(i => b[at + i] == c[at + i])) errors++; }
                        if (a[at] == 17 && a[at + 1] == 16 && a[at + 2] == 14 && m[at] == 255)
                        { protectedBlack++; if (!Enumerable.Range(0, 4).All(i => c[at + i] == d[at + i])) errors++; }
                    }
                    Assert(unchangedYellow > 100 && protectedBlack > 100 && errors == 0, "Paint changes only the intended source color even when destination colors coincide");
                }
                using (Bitmap preview = PaintRenderer.Raster(changed, effect, 256)) Assert(preview.Width == 256 && preview.Height == 256, "Four-color paint supports scaled preview");
            }
        }
        Console.WriteLine("PASS base palette: four source colors across all sign types/roles, overrides, persistence, preset compatibility and paint isolation.");
    }
}
