using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AirportSignStudio;

static partial class Verify
{
    static void VerifyTypeColors(SignEngine engine, string directory)
    {
        ColorSettings colors = new ColorSettings { Foreground = "#357ABC", Background = "#D3A748", Border = "#9D572C" };
        TypeColorSettings palette = new TypeColorSettings();
        foreach (SignColorType type in Enum.GetValues(typeof(SignColorType)))
        {
            Settings sample = TypeColorSettings.Sample(type);
            Assert(TypeColorSettings.For(sample) == type, "Sample matches its color type: " + type);
            palette.Set(type, colors);
            using (SignDocument original = engine.Build(sample))
            using (SignDocument recolored = engine.Build(sample, null, palette))
            {
                Assert(original.Shapes.Count == recolored.Shapes.Count && original.Width == recolored.Width && original.Height == recolored.Height, "Type colors preserve geometry: " + type);
                for (int i = 0; i < recolored.Shapes.Count; i++)
                {
                    Shape shape = recolored.Shapes[i];
                    Assert(shape.ColorType == type && shape.CustomColors, "Rendered shape carries its correct type: " + type);
                    string value = shape.Role == ColorRole.Foreground ? colors.Foreground : shape.Role == ColorRole.Background ? colors.Background : colors.Border;
                    Assert(shape.Fill.ToArgb() == ColorTranslator.FromHtml(value).ToArgb(), "Type color reaches each role: " + type);
                    Assert(shape.Path.PathPoints.SequenceEqual(original.Shapes[i].Path.PathPoints), "Type color keeps vector points: " + type);
                }
            }
            palette.Set(type, new ColorSettings());
        }
        palette.Set(SignColorType.GroundLocation, colors);
        foreach (SignColorType other in Enum.GetValues(typeof(SignColorType)))
            if (other != SignColorType.GroundLocation)
                using (SignDocument before = engine.Build(TypeColorSettings.Sample(other)))
                using (SignDocument after = engine.Build(TypeColorSettings.Sample(other), null, palette))
                    Assert(Exporter.Svg(before, false) == Exporter.Svg(after, false), "Unrelated type remains unchanged: " + other);

        TypeColorSettings combinedPalette = new TypeColorSettings();
        combinedPalette.Set(SignColorType.TaxiDirection, new ColorSettings { Background = "#DFB134" });
        combinedPalette.Set(SignColorType.TaxiLocation, new ColorSettings { Foreground = "#FAF0C1", Background = "#292A2B" });
        Settings combined = TypeColorSettings.Sample(SignColorType.TaxiDirection);
        combined.Panels.Add(new SignPanel { Kind = "位置", Text = "B" }); combined.Panels.Add(new SignPanel { Text = "C", Arrow = "→" });
        using (SignDocument doc = engine.Build(combined, null, combinedPalette))
        {
            Assert(doc.Shapes.Select(s => s.ColorSection).Distinct().Count() == 3, "Combined sign retains separate color sections");
            Assert(doc.Shapes.Where(s => s.ColorType == SignColorType.TaxiLocation && s.Role == ColorRole.Background).All(s => ColorPicker.Hex(s.Fill) == "#292A2B"), "Location panel has its own background");
            Assert(doc.Shapes.Where(s => s.ColorType == SignColorType.TaxiDirection && s.Role == ColorRole.Background).All(s => ColorPicker.Hex(s.Fill) == "#DFB134"), "Both direction panels use the direction palette");
        }
        combined.Colors.Foreground = "#ABCDEF";
        ColorSettings common = new ColorSettings { Foreground = "#111111", Background = "#222222", Border = "#333333" };
        using (SignDocument doc = engine.Build(combined, common, combinedPalette))
        {
            Assert(doc.Shapes.Where(s => s.Role == ColorRole.Foreground).All(s => ColorPicker.Hex(s.Fill) == "#ABCDEF"), "Individual foreground overrides type and common");
            Assert(doc.Shapes.Where(s => s.Role == ColorRole.Background).All(s => ColorPicker.Hex(s.Fill) != common.Background), "Type background overrides common");
            Assert(doc.Shapes.Where(s => s.Role == ColorRole.Border).All(s => ColorPicker.Hex(s.Fill) == common.Border), "Unset role inherits common");
        }
        Settings mandatory = TypeColorSettings.Sample(SignColorType.TaxiMandatory); mandatory.Panels.Add(new SignPanel { Kind = "位置", Text = "B" });
        combinedPalette.Set(SignColorType.TaxiMandatory, new ColorSettings { Background = "#BD3242" });
        using (SignDocument doc = engine.Build(mandatory, null, combinedPalette))
            Assert(doc.Shapes.Any(s => s.ColorType == SignColorType.TaxiMandatory && ColorPicker.Hex(s.Fill) == "#BD3242") &&
                doc.Shapes.Any(s => s.ColorType == SignColorType.TaxiLocation && ColorPicker.Hex(s.Fill) == "#292A2B"), "Mandatory and location segments are independently colored");

        AtlasWorkspace w = new AtlasWorkspace { Colors = common.Copy(), TypeColors = combinedPalette.Copy() };
        w.Slots[0] = combined; w.Slots[1] = mandatory; w.Merge(new[] { 0, 1 });
        string saved = ProjectStore.Encode(w); AtlasWorkspace loaded = ProjectStore.Decode(saved), applied = new AtlasWorkspace(); ProjectStore.Apply(applied, loaded);
        Assert(ProjectStore.Encode(loaded) == saved && ProjectStore.Encode(applied) == saved, "Type colors round-trip with hidden merged contents");
        applied.TypeColors.Get(SignColorType.TaxiLocation).Foreground = "#000000";
        Assert(loaded.TypeColors.Get(SignColorType.TaxiLocation).Foreground == "#FAF0C1", "Applied palette is a deep copy");
        using (AtlasLayout a = AtlasLayout.Build(engine, w))
        using (AtlasLayout b = AtlasLayout.Build(engine, loaded)) Assert(Exporter.Svg(a.Document, true) == Exporter.Svg(b.Document, true), "Reopened palette renders identically");
        XDocument old = XDocument.Parse(saved); old.Root.Element("TypeColors").Remove();
        AtlasWorkspace migrated = ProjectStore.Decode(old.ToString());
        Assert(migrated.TypeColors.Entries.Count == 0 && migrated.Colors.Foreground == common.Foreground, "Old projects retain legacy common colors");
        w.TypeColors = new TypeColorSettings();
        using (AtlasLayout a = AtlasLayout.Build(engine, w))
        using (AtlasLayout b = AtlasLayout.Build(engine, migrated)) Assert(Exporter.Svg(a.Document, true) == Exporter.Svg(b.Document, true), "Old project appearance is unchanged");
        XDocument invalid = XDocument.Parse(saved); invalid.Descendants("TypeColorEntry").First().Element("Type").Value = "UnknownType";
        bool rejected = false;
        try { ProjectStore.Decode(invalid.ToString()); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Unknown color type rejected");
        TypeColorSettings duplicate = combinedPalette.Copy(); duplicate.Entries.Add(duplicate.Entries[0]);
        Throws(() => duplicate.Validate(), "Duplicate color types rejected");

        ColorPreset preset = new ColorPreset { Name = "测试配色", Common = common.Copy(), Types = combinedPalette.Copy() };
        string encoded = ColorPresetStore.Encode(preset), path = Path.Combine(directory, "测试配色.asscolors");
        ColorPresetStore.Save(preset, path); ColorPreset restored = ColorPresetStore.Decode(File.ReadAllText(path));
        Assert(ColorPresetStore.Encode(restored) == encoded, "Named preset survives file save/load");
        restored.Types.Get(SignColorType.TaxiLocation).Background = "#123456";
        Assert(preset.Types.Get(SignColorType.TaxiLocation).Background == "#292A2B", "Preset objects are independent");
        Throws(() => ColorPresetStore.Decode(encoded.Replace("#292A2B", "invalid")), "Malformed preset color rejected");
        Throws(() => ColorPresetStore.Decode(encoded.Replace("<Version>2</Version>", "<Version>99</Version>")), "Future preset version rejected");
        Throws(() => ColorPresetStore.Decode("<!DOCTYPE x [<!ENTITY bad SYSTEM 'file:///does-not-exist'>]><ColorPreset>&bad;</ColorPreset>"), "Preset DTD prohibited");

        // Warm-yellow paint must not recolor a custom panel or stop affecting adjacent default panels.
        AtlasWorkspace painted = new AtlasWorkspace { Width = 1024, Height = 512 }; painted.Resize(1, 1);
        combined.Colors = new ColorSettings(); painted.Slots[0] = combined;
        PaintSettings paint = PaintSettings.Preset(1);
        using (AtlasLayout baseline = AtlasLayout.Build(engine, painted))
        using (Bitmap before = PaintRenderer.Raster(baseline, paint, 0))
        {
            painted.TypeColors.Set(SignColorType.TaxiLocation, new ColorSettings { Foreground = "#FFD600" });
            using (AtlasLayout changed = AtlasLayout.Build(engine, painted))
            using (Bitmap after = PaintRenderer.Raster(changed, paint, 0))
            {
                AtlasSlotInfo slot = changed.Slots[0]; Assert(!slot.CustomColors && slot.CustomColorAreas.Length == 1, "Partial custom palette protects only its panel");
                byte[] beforePixels = Pixels(before), afterPixels = Pixels(after);
                int outsideChanges = 0, insideChanges = 0;
                RectangleF area = RectangleF.Inflate(slot.CustomColorAreas[0], 2, 2);
                for (int y = 0; y < after.Height; y++) for (int x = 0; x < after.Width; x++)
                {
                    int offset = (y * after.Width + x) * 4;
                    bool differs = Enumerable.Range(0, 4).Any(c => beforePixels[offset + c] != afterPixels[offset + c]);
                    if (differs) { if (area.Contains(x + 0.5f, y + 0.5f)) insideChanges++; else outsideChanges++; }
                }
                Assert(outsideChanges == 0 && insideChanges > 0, "Type override leaves neighboring painted panels unchanged");
            }
        }
        Console.WriteLine("PASS type palettes: 12 types, mixed panels, overrides, project/preset compatibility, and painted panel isolation.");
    }
}
