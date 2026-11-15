using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using AirportSignStudio;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

static partial class Verify
{
    static int checks;
    static void Assert(bool pass, string message)
    {
        checks++; if (!pass) throw new Exception("FAIL: " + message);
    }
    static void Near(float actual, float expected, string message) { Assert(Math.Abs(actual - expected) < 0.05f, message + " (" + actual + ")"); }
    static void Throws(Action action, string message)
    {
        bool threw = false; try { action(); } catch (ArgumentException) { threw = true; } Assert(threw, message);
    }
    static void VerifyAtlasBookAndBatch(SignEngine engine, string directory)
    {
        string[] imported = BatchSigns.FromReport("""
            {"records":[{"stand":"101L","reviewed":true},{"stand":"102","confidence":0.81},
            {"stand":"103","excluded":true},{"stand":""},{"stand":"106","reviewed":true}]}
            """, out int review);
        Assert(imported.SequenceEqual(new[] { "101L", "102", BatchSigns.MissingStand, "106" }) && review == 2, "sign_autogen excludes rejected rows and flags uncertain entries");
        string[] confirmed = BatchSigns.FromReport("""
            {"stand_label_candidates":[{"text":"91","position_source":"chart_label"},
            {"text":"101","position_source":"official_stand_coordinate_table"}]}
            """, out review);
        Assert(confirmed.SequenceEqual(new[] { "101" }) && review == 0, "AutoCharts official positions take priority");
        string[] associated = BatchSigns.FromReport("""
            {"stand_label_candidates":[{"text":"A1","status":"vector_label_only"},
            {"text":"B2","status":"lead_in_associated","associated_guidance_sources":["curve:7"]}]}
            """, out review);
        Assert(associated.SequenceEqual(new[] { "B2" }) && review == 1, "Chart label alone is not a stand");
        AtlasWorkspace first = new AtlasWorkspace { Width = 512, Height = 256, XPlane = true, PolPrefix = "Stand", TargetTextPixels = 32 };
        first.Resize(2, 2);
        first.Slots[0] = new Settings { Kind = AssetKind.GroundStand, Text = "099", Arrow = "↑" };
        first.Selected = 2;
        first.Merge(new[] { 2, 3 });
        AtlasBook book = AtlasBook.Decode(ProjectStore.Encode(first));
        AtlasHistory history = new AtlasHistory(book);
        string before = book.Encode();
        Settings template = new Settings { Kind = AssetKind.GroundStand, Arrow = "→", LeftMargin = 600, RightMargin = 700 };
        Throws(() => BatchSigns.Add(book, engine, "101\n" + BatchSigns.MissingStand, template, book.Active), "Unreviewed blank blocks batch");
        Assert(book.Encode() == before, "Invalid batch changes no pages");
        Assert(BatchSigns.Add(book, engine, "101L, 102\n１０２\n103\n104\n105", template, book.Active) == 5, "Batch normalizes and deduplicates");
        Assert(book.Pages.Count == 2 && book.Pages[0].Slots[0].Text == "099" &&
            book.Pages[0].Slots[2].Text == "102" && book.Pages[0].Slots[3] == null, "Batch only fills visible empty slots");
        Assert(book.Pages[1].Width == 512 && book.Pages[1].Slots[0].Text == "103" &&
            book.Pages[1].Slots[0].LeftMargin == 600, "Overflow inherits dimensions and sign style");
        history.Record(new[] { 0 });
        string after = book.Encode();
        Assert(history.Undo() && book.Pages.Count == 1 && book.Encode() == before &&
            history.Redo() && book.Encode() == after, "Batch is one undoable change across atlases");
        string path = Path.Combine(directory, "multi-atlas.assproj");
        book.Save(path);
        AtlasBook loaded = AtlasBook.Decode(File.ReadAllText(path));
        Assert(loaded.Encode() == after, "Multi-atlas file retains individual layouts and settings");
        loaded.Pages[1].XPlane = false;
        string folder = Path.Combine(directory, "multi-atlas-output-" + Guid.NewGuid().ToString("N"));
        loaded.ExportAll(engine, folder);
        Assert(Directory.GetFiles(folder).Length == 4 && File.Exists(Path.Combine(folder, "01_Stand.pol")) &&
            File.Exists(Path.Combine(folder, "02_Atlas_atlas.png")), "Combined export respects each atlas mode");
        string[] csv = File.ReadAllLines(Path.Combine(folder, "AtlasIndex.csv"));
        Assert(csv.Length == 7 && csv.Any(line => line.Contains("\"101L\"")), "Index maps each exported sign to a page and UV");
        loaded.Pages[1].Slots[1].Text = "";
        string failed = folder + "-failed";
        Throws(() => loaded.ExportAll(engine, failed), "Invalid page fails batch export");
        Assert(!Directory.Exists(failed), "Failed batch exposes no partial output");

        AtlasWorkspace smallSlots = new AtlasWorkspace { Width = 1024, Height = 1024, TargetTextPixels = 160 };
        smallSlots.Resize(8, 8);
        AtlasBook large = new AtlasBook(smallSlots);
        string[] ids = Enumerable.Range(100, 40).Select(i => i.ToString()).ToArray();
        Assert(BatchSigns.Add(large, engine, string.Join("\n", ids), template, large.Active) == ids.Length && large.Pages.Count > 2,
            "A large batch creates additional density-aware pages");
        Assert(smallSlots.Filled == 0 && large.Pages.Skip(1).All(p => p.Count < smallSlots.Count),
            "Density-aware packing bypasses undersized existing slots and picks a smaller grid");
        Assert(large.Pages.SelectMany(p => p.Slots).Where(s => s != null).Select(s => s.Text).SequenceEqual(ids),
            "Automatic pagination preserves every identifier in order");
        foreach (AtlasWorkspace page in large.Pages.Skip(1))
            using (AtlasLayout atlas = AtlasLayout.Build(engine, page))
                Assert(atlas.Slots.Where(slot => slot.Occupied).All(slot => slot.SmallestTextPixels >= page.TargetTextPixels - 0.05f),
                    "Automatically split pages meet the requested density when possible");

        AtlasWorkspace untouched = new AtlasWorkspace();
        AtlasWorkspace occupied = new AtlasWorkspace { Width = 512, Height = 256, TargetTextPixels = 32 };
        occupied.Resize(1, 2);
        occupied.Slots[0] = template.Copy();
        occupied.Slots[0].Text = "090";
        AtlasBook targeted = new AtlasBook(untouched);
        targeted.Pages.Add(occupied);
        targeted.Pages.Add(AtlasBook.BlankLike(occupied));
        AtlasHistory targetedHistory = new AtlasHistory(targeted);
        string original = targeted.Encode();
        Size texture = new Size(1000, 576), fixedGrid = new Size(2, 2);
        BatchSigns.Add(targeted, engine, "201,202,203,204,205,206", template, 1, BatchSplit.Fixed, texture, fixedGrid);
        Assert(targeted.Active == 1 && untouched.Filled == 0 && occupied.Slots[1]?.Text == "201" &&
            occupied.Width == 512 && occupied.Height == 256 && occupied.Rows == 1, "Selected start page preserves existing content and layout");
        Assert(targeted.Pages.Count == 4 && targeted.Pages.Skip(2).All(p => p.Width == texture.Width && p.Height == texture.Height &&
            p.Rows == 2 && p.Columns == 2) && targeted.Pages[2].Slots[3].Text == "205" && targeted.Pages[3].Slots[0].Text == "206",
            "Explicit resolution and fixed grid apply to empty and newly added pages, with ordered overflow");
        targetedHistory.Record(new[] { targeted.Current.Selected });
        string generated = targeted.Encode();
        Assert(targetedHistory.Undo() && targeted.Active == 0 && targeted.Encode() == original &&
            targetedHistory.Redo() && targeted.Active == 1 && targeted.Encode() == generated,
            "One undo and redo covers the targeted placements and changed page settings");
        AtlasBook reloaded = AtlasBook.Decode(generated);
        Assert(reloaded.Encode() == generated, "Targeted layouts and custom resolution survive a project round trip");
        string targetedOutput = Path.Combine(directory, "targeted-atlases-" + Guid.NewGuid().ToString("N"));
        reloaded.ExportAll(engine, targetedOutput);
        using (Bitmap exported = new Bitmap(Path.Combine(targetedOutput, "03_Atlas_atlas.png")))
            Assert(exported.Size == texture, "Export uses the exact resolution chosen during generation");

        int appendAt = targeted.Pages.Count;
        BatchSigns.Add(targeted, engine, "301", template, appendAt, BatchSplit.Automatic, new Size(512, 512));
        Assert(targeted.Active == appendAt && targeted.Current.Count == 1 && targeted.Current.Width == 512 &&
            targeted.Current.Slots[0].Text == "301" && targeted.Pages[3].Filled == 1, "New-page destination skips existing gaps and supports automatic splitting");
        string beforeFailure = targeted.Encode();
        Throws(() => BatchSigns.Add(targeted, engine, "401,402,403,404", template, 3, BatchSplit.Fixed, new Size(128, 128), new Size(8, 8)),
            "A grid with no usable area fails after planning a placement on an existing page");
        Assert(targeted.Encode() == beforeFailure && targeted.Active == appendAt, "Invalid generation leaves all source pages untouched");

        AtlasWorkspace merged = new AtlasWorkspace { TargetTextPixels = 32 };
        merged.Merge(new[] { 0, 1 });
        AtlasBook custom = new AtlasBook(merged);
        BatchSigns.Add(custom, engine, "501", template, 0, resolution: texture);
        Assert(custom.Current.Width == texture.Width && custom.Current.Regions[0].CellCount == 2 &&
            custom.Current.Regions[1] == null && custom.Current.Slots[0].Text == "501", "Resizing an empty page can preserve its merged grid");
    }

    static void VerifyStandOcr(SignEngine engine, string directory)
    {
        string DrawChart(string file, string title)
        {
            string path = Path.Combine(directory, file);
            using Bitmap bitmap = new Bitmap(700, 270, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(bitmap);
            using Font label = new Font("Arial", 17, FontStyle.Regular, GraphicsUnit.Pixel);
            using Font heading = new Font("Arial", 26, FontStyle.Regular, GraphicsUnit.Pixel);
            g.Clear(Color.White);
            foreach (int y in new[] { 55, 90, 125, 160, 195, 230 }) g.DrawLine(Pens.Black, 14, y, 685, y);
            foreach (int x in new[] { 14, 146, 685 }) g.DrawLine(Pens.Black, x, 55, x, 230);
            g.DrawString(title, heading, Brushes.Black, 16, 9);
            g.DrawString("TWY G2", label, Brushes.Black, 550, 241);
            g.DrawString("ID", label, Brushes.Black, 38, 63);
            g.DrawString("Coordinates", label, Brushes.Black, 175, 63);
            g.DrawString("001", label, Brushes.Black, 38, 98);
            g.DrawString("N40 28 58.9 E111 34 19.7", label, Brushes.Black, 175, 98);
            g.DrawString("A1", label, Brushes.Black, 38, 133);
            g.DrawString("N40 28 59.0 E111 34 20.2", label, Brushes.Black, 175, 133);
            g.DrawString("G2", label, Brushes.Black, 38, 168); // No paired coordinate.
            g.DrawString("N40 28 59.0 E111 34 20.3", label, Brushes.Black, 175, 203);
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        string stands = DrawChart("ocr-stand-check.png", "Aircraft Stand Coordinates");
        string[] ids = StandChartOcr.Read(stands, out int review);
        Assert(ids.SequenceEqual(new[] { "001", "A1", BatchSigns.MissingStand }) && review == 1,
            "Image OCR keeps leading zeros and legitimate alphanumeric stands, filters unpaired taxiway IDs, and retains missing stand rows");
        Assert(BatchSigns.ImportFiles(new[] { stands }).Rows.SequenceEqual(ids), "Image files use the batch importer");
        string taxiways = DrawChart("ocr-taxiway-check.png", "Taxiway Coordinates");
        Throws(() => StandChartOcr.Read(taxiways, out _), "Taxiway coordinate table cannot become a stand list");
        string scanPath = Path.Combine(directory, "scan-test.pdf");
        PdfDocumentBuilder scan = new PdfDocumentBuilder();
        scan.AddPage(700, 270).AddPng(File.ReadAllBytes(stands), new PdfRectangle(0, 0, 700, 270));
        File.WriteAllBytes(scanPath, scan.Build());
        var scanned = BatchSigns.FromPdfs(new[] { scanPath });
        Assert(scanned.Rows.SequenceEqual(ids) && scanned.Review == 1 && scanned.OcrPages == 1,
            "An image-only PDF imports directly and preserves missing-ID review rows");
        Assert(BatchSigns.ImportFiles(new[] { scanPath }).Rows.SequenceEqual(ids), "Scanned PDF is accepted by the batch import workflow");

        string mixedPath = Path.Combine(directory, "mixed-stand-check-0P.pdf");
        PdfDocumentBuilder mixed = new PdfDocumentBuilder();
        var font = mixed.AddStandard14Font(Standard14Font.Helvetica);
        var textPage = mixed.AddPage(700, 270);
        textPage.AddText("Aircraft Stand Coordinates", 20, new PdfPoint(15, 250), font);
        textPage.AddText("901L N40 28'58.9\" E111 34'19.7\"", 14, new PdfPoint(15, 220), font);
        var tiled = mixed.AddPage(700, 270);
        using (Bitmap original = new Bitmap(stands))
            foreach (int left in new[] { 0, 350 })
            {
                using Bitmap half = original.Clone(new Rectangle(left, 0, 350, 270), PixelFormat.Format24bppRgb);
                using MemoryStream png = new MemoryStream();
                half.Save(png, ImageFormat.Png);
                tiled.AddPng(png.ToArray(), new PdfRectangle(left, 0, left + 350, 270));
            }
        File.WriteAllBytes(mixedPath, mixed.Build());
        var both = BatchSigns.FromPdfs(new[] { mixedPath });
        Assert(both.Rows.SequenceEqual(new[] { "001", "901L", "A1", BatchSigns.MissingStand }) && both.OcrPages == 1,
            "Mixed PDF imports vector text directly and renders its tiled scan page for OCR");

        string taxiPdf = Path.Combine(directory, "taxiway-scan-check.pdf");
        PdfDocumentBuilder taxiScan = new PdfDocumentBuilder();
        taxiScan.AddPage(700, 270).AddPng(File.ReadAllBytes(taxiways), new PdfRectangle(0, 0, 700, 270));
        File.WriteAllBytes(taxiPdf, taxiScan.Build());
        Assert(BatchSigns.FromPdfs(new[] { taxiPdf }).Rows.Length == 0,
            "PDF screenshot OCR does not turn taxiway tables into a stand list");
        AtlasBook book = new AtlasBook(new AtlasWorkspace());
        Settings template = new Settings { Kind = AssetKind.GroundStand, Arrow = "↑" };
        Throws(() => BatchSigns.Add(book, engine, "001\n" + StandChartOcr.ReviewPrefix + "102]", template, book.Active),
            "OCR review rows cannot be batch generated before correction");
    }

    static void VerifySliderPrecision()
    {
        const decimal start = 2400;
        const System.Windows.Forms.Keys ctrl = System.Windows.Forms.Keys.Control, shift = System.Windows.Forms.Keys.Shift;
        Assert(StudioSlider.ValueAt(0.25, 0, 8, SliderScale.Linear) == 2, "A linear slider position selects its fixed value");
        Assert(StudioSlider.ValueAt(0.5, 0, 200000, SliderScale.Millimetres) == 6250,
            "The midpoint of a dimension rail selects the same common-scale value");
        Assert(StudioSlider.ValueAt(0, -1, 200000, SliderScale.Millimetres) == -1 &&
            StudioSlider.ValueAt(1, -1, 200000, SliderScale.Millimetres) == 200000,
            "Margin endpoints select automatic and the supported maximum");
        foreach (decimal value in new[] { -1m, 0m, 80.25m, 2400m, 200000m })
        {
            double position = StudioSlider.Fraction(value, -1, 200000, SliderScale.Millimetres);
            Assert(StudioSlider.Quantize(StudioSlider.ValueAt(position, -1, 200000, SliderScale.Millimetres), -1, 200000, 2) == value,
                "Typed millimetres and slider position use reciprocal scales");
        }
        Assert(StudioSlider.ValueAt(0.5, 0.0001m, 10000, SliderScale.Ratio) == 1 &&
            Math.Abs(StudioSlider.Fraction(10, 0.0001m, 10000, SliderScale.Ratio) - 0.625) < 1e-9,
            "The ratio rail is centered on 1:1 and gives fixed positions to larger ratios");
        Assert(StudioSlider.FineDrag(start, 1, 1, ctrl, 0, 200000) == 2400.1m, "Ctrl allows a tenth on a wide dimension");
        Assert(StudioSlider.FineDrag(start, -1, 1, ctrl | shift, 0, 200000) == 2399.99m, "Shift takes priority and allows a hundredth");
        decimal accumulated = 0;
        for (int i = 0; i < 10; i++) accumulated = StudioSlider.FineDrag(accumulated, 1, 1, ctrl, 0, 8);
        Assert(StudioSlider.Quantize(accumulated, 0, 8, 0) == 1, "Fractional drag deltas accumulate for integer fields");
        Assert(StudioSlider.FineDrag(8, 30, 1, ctrl, 0, 8) == 8, "Fine drag stays within the field's limits");
    }

    static void VerifyMarginsAndWholeDividers(SignEngine engine)
    {
        Settings board = new Settings { Kind = AssetKind.StandSign, CharacterHeight = 600, Text = "A", LeftMargin = 80, RightMargin = 180 };
        using (SignDocument sign = engine.Build(board))
        {
            RectangleF text = sign.Shapes[1].Path.GetBounds();
            Near(text.Left, 80, "Automatic width uses exact left margin");
            Near(sign.Width - text.Right, 180, "Automatic width uses exact right margin");
        }
        board.AspectRatio = "2:1";
        using (SignDocument sign = engine.Build(board))
        {
            RectangleF text = sign.Shapes[1].Path.GetBounds();
            Near(sign.Width / sign.Height, 2, "Margins work with ratio");
            Near(sign.Width - text.Right - text.Left, 100, "Extra width preserves offset");
        }
        board.LeftMargin = -1;
        using (SignDocument sign = engine.Build(board))
            Near(sign.Width - sign.Shapes[1].Path.GetBounds().Right, 180, "One custom margin anchors that side");
        board.Width = 100;
        board.AspectRatio = null;
        Throws(() => { using (engine.Build(board)) { } }, "Explicit width must fit custom margins");
        board.Width = 0;

        Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300, LeftMargin = 80, RightMargin = 180 };
        taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无" });
        taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "B", Arrow = "无" });
        using (SignDocument sign = engine.Build(taxi))
        {
            for (int i = 0; i < 2; i++)
            {
                RectangleF panel = sign.Shapes[i * 2].Path.GetBounds(), text = sign.Shapes[i * 2 + 1].Path.GetBounds();
                Near(text.Left - panel.Left, 80, "Combination left margin " + i);
                Near(panel.Right - text.Right, 180, "Combination right margin " + i);
            }
        }

        foreach (bool vertical in new[] { true, false })
        {
            AtlasWorkspace w = new AtlasWorkspace();
            w.Resize(vertical ? 3 : 2, vertical ? 2 : 3);
            int middle = vertical ? 2 : 1, far = vertical ? 4 : 2;
            w.Selected = middle;
            w.Merge(new[] { middle, vertical ? 3 : 4 });
            w.Slots[0] = board.Copy();
            AtlasHistory history = new AtlasHistory(w);
            string initial = ProjectStore.Encode(w);
            RectangleF mergedBounds = w.Regions[middle].Bounds;
            AtlasEdge edge = vertical ? AtlasEdge.Right : AtlasEdge.Bottom;
            AtlasDivider local = w.Divider(0, edge);
            AtlasDivider whole = w.Divider(0, edge, true);
            Assert(!local.Before.Contains(far) && whole.Before.Contains(far), "Whole divider crosses the gap between aligned segments");
            Assert(w.MoveDivider(whole, 0.65f), "Whole divider moves");
            Near(vertical ? w.Regions[far].Bounds.Right : w.Regions[far].Bounds.Bottom, 0.65f, "Far segment moves with the whole line");
            Assert(w.Regions[middle].Bounds == mergedBounds, "Divider leaves the spanning merged region intact");
            string moved = ProjectStore.Encode(w);
            AtlasWorkspace restored = ProjectStore.Decode(moved);
            Assert(ProjectStore.Encode(restored) == moved && restored.Slots[0].RightMargin == 180, "Project retains moved segments and margins");
            history.Record(new[] { 0 });
            Assert(history.Undo() && ProjectStore.Encode(w) == initial &&
                history.Redo() && ProjectStore.Encode(w) == moved, "Whole divider undoes and redoes together");
        }
    }

    static void VerifyAspectRatio(SignEngine engine)
    {
        Settings board = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600, Width = 2400, Height = 1200 };
        board.AspectRatio = "3：2";
        using (SignDocument sign = engine.Build(board))
        {
            Near(sign.Width, 1500, "Ratio uses automatic board dimensions");
            Near(sign.Height, 1000, "Wide ratio keeps automatic height");
            Near(sign.Shapes[1].Path.GetBounds().Height, 600, "Ratio does not stretch text");
        }
        board.AspectRatio = "1:4";
        using (SignDocument sign = engine.Build(board))
        {
            Near(sign.Width / sign.Height, 0.25f, "Tall ratio adds height");
            Assert(sign.Height > 1000, "Tall ratio fits the original text and margins");
        }
        Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
        taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无", Width = 900 });
        taxi.Panels.Add(new SignPanel { Text = "B", Width = 1300 });
        taxi.AspectRatio = "2:1";
        using (SignDocument sign = engine.Build(taxi))
        {
            Near(sign.Width, 2200, "Tall combination keeps the segment widths");
            Near(sign.Height, 1100, "Combination ratio uses total width");
        }
        taxi.AspectRatio = "6:1";
        using (SignDocument sign = engine.Build(taxi))
        {
            Near(sign.Width, 2700, "Wide combination expands to overall ratio");
            Near(sign.Shapes[0].Path.GetBounds().Width, 1150, "Combination distributes added width");
            Near(sign.Height, 450, "Wide combination keeps automatic height");
        }
        foreach (string invalid in new[] { "3:", "0:2", "NaN:1", "1:1000000" })
        {
            board.AspectRatio = invalid;
            Throws(() => { using (engine.Build(board)) { } }, "Invalid or oversized ratio " + invalid);
        }
        board.AspectRatio = "";
        using (SignDocument sign = engine.Build(board)) Near(sign.Height, 1000, "Empty ratio uses automatic size");
        board.AspectRatio = null;
        using (SignDocument sign = engine.Build(board)) Near(sign.Height, 1200, "Switching back restores millimetre size");

        AtlasWorkspace workspace = new AtlasWorkspace(); workspace.Slots[0] = board;
        AtlasHistory history = new AtlasHistory(workspace);
        board.AspectRatio = "3:2"; history.Record(new[] { 0 });
        AtlasWorkspace loaded = ProjectStore.Decode(ProjectStore.Encode(workspace));
        Assert(loaded.Slots[0].Copy().AspectRatio == "3:2", "Saved and copied slots retain ratio");
        using (AtlasLayout layout = AtlasLayout.Build(engine, loaded))
            Near(layout.Slots[0].Image.Width / layout.Slots[0].Image.Height, 1.5f, "Atlas uses the computed board ratio");
        Assert(history.Undo() && workspace.Slots[0].AspectRatio == null &&
               history.Redo() && workspace.Slots[0].AspectRatio == "3:2", "Ratio participates in undo and redo");
    }

    static void Sample(SignEngine engine, Settings s, string name, string directory, int edge)
    {
        using (SignDocument doc = engine.Build(s))
        {
            foreach (Shape shape in doc.Shapes)
            {
                RectangleF box = shape.Path.GetBounds();
                Assert(box.Left >= -0.5 && box.Top >= -0.5 && box.Right <= doc.Width + 0.5 && box.Bottom <= doc.Height + 0.5, name + " has no clipped geometry");
            }
            using (Bitmap png = Exporter.Raster(doc, edge, true, 16))
            {
                Assert(Math.Max(png.Width, png.Height) == edge, name + " export long edge");
                Assert((png.Width & (png.Width - 1)) == 0 && (png.Height & (png.Height - 1)) == 0, name + " power-of-two");
                Assert(png.PixelFormat == PixelFormat.Format32bppArgb, name + " RGBA");
                for (int x = 0; x < png.Width; x += 11) Assert(png.GetPixel(x, 0).A == 0 && png.GetPixel(x, png.Height - 1).A == 0, name + " top/bottom transparent margin");
                for (int y = 0; y < png.Height; y += 11) Assert(png.GetPixel(0, y).A == 0 && png.GetPixel(png.Width - 1, y).A == 0, name + " left/right transparent margin");
                png.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            }
            string svg = Exporter.Svg(doc);
            XDocument xml = XDocument.Parse(svg);
            Assert(!xml.Descendants().Any(e => e.Name.LocalName == "text" || e.Name.LocalName == "image"), name + " SVG embeds only geometry");
            Assert((string)xml.Root.Attribute("width") == doc.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "mm", name + " physical width");
            File.WriteAllText(Path.Combine(directory, name + ".svg"), svg);
        }
    }
    static void GlyphSheet(GlyphLibrary library, string family, string directory)
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-/↑" + (family == "sign" ? "ⅠⅡⅢ." : ".m");
        using (Bitmap image = new Bitmap(1260, ((chars.Length + 6) / 7) * 220, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(image))
        using (Font label = new Font("Arial", 12))
        using (Brush ink = new SolidBrush(SignEngine.Yellow))
        {
            g.Clear(Color.FromArgb(25, 32, 38)); g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < chars.Length; i++)
            {
                Glyph glyph = library.Get(family, chars[i]);
                float x = (i % 7) * 180, y = (i / 7) * 220, scale = 0.15f;
                g.DrawString(chars[i].ToString(), label, Brushes.White, x + 12, y + 10);
                using (GraphicsPath path = (GraphicsPath)glyph.Path.Clone())
                using (Matrix m = new Matrix(scale, 0, 0, scale, x + (180 - glyph.Width * scale) / 2, y + 50 + (150 - glyph.Height * scale)))
                { path.Transform(m); g.FillPath(ink, path); }
            }
            image.Save(Path.Combine(directory, "glyphs_" + family + ".png"), ImageFormat.Png);
        }
    }

    static void VerifyV2(SignEngine engine, GlyphLibrary library, string directory)
    {
        foreach (string family in new[] { "sign", "ground" })
        {
            foreach (char c in "AEFHIKLMNTVWXYZ")
                Assert(library.Get(family, c).Path.PathTypes.All(t => (t & 7) != 3), family + " " + c + " has exact straight edges");
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
                Assert(library.Get(family, c).Path.PointCount <= 110, family + " " + c + " uses low-node contours");
            GlyphSheet(library, family, directory);
        }

        Settings stand = new Settings { Kind = AssetKind.StandSign, CharacterHeight = 600, Text = "23" };
        using (SignDocument automatic = engine.Build(stand))
        {
            stand.Width = 2500; stand.Height = 1500;
            using (SignDocument custom = engine.Build(stand))
            {
                Near(custom.Width, 2500, "Custom stand width"); Near(custom.Height, 1500, "Custom stand height");
                Near(custom.Shapes[1].Path.GetBounds().Width, automatic.Shapes[1].Path.GetBounds().Width, "Wider board does not stretch glyph width");
                Near(custom.Shapes[1].Path.GetBounds().Height, 600, "Taller board does not stretch glyph height");
            }
        }
        Sample(engine, stand, "v2_custom_stand", directory, 2048);
        stand.Width = 10;
        Throws(() => { using (engine.Build(stand)) { } }, "Board too narrow blocks export");

        Settings customGround = new Settings { Text = "A1", Width = 8000, Height = 6000 };
        Sample(engine, customGround, "v2_custom_ground", directory, 2048);
        customGround.Width = 20;
        Throws(() => { using (engine.Build(customGround)) { } }, "Ground width cannot clip text and arrow");
        foreach (string arrow in new[] { "↑", "←", "→", "无" })
        {
            Settings groundStand = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = arrow, Width = 10000, Height = 9000 };
            using (SignDocument d = engine.Build(groundStand))
            {
                Near(d.Width, 10000, "Custom ground stand width " + arrow); Near(d.Height, 9000, "Custom ground stand height " + arrow);
                Near(d.Shapes.Last().Path.GetBounds().Height, 4000, "Ground stand text height preserved " + arrow);
            }
        }
        Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300, Height = 700 };
        taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Width = 1200 });
        taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "B", Arrow = "→", Width = 1800 });
        using (SignDocument d = engine.Build(taxi)) { Near(d.Width, 3000, "Independent segment widths add up"); Near(d.Height, 700, "Custom taxi height"); }
        Sample(engine, taxi, "v2_custom_taxi", directory, 2048);
        Settings clone = taxi.Copy(); taxi.Panels[0].Text = "ZZ"; taxi.Panels[0].Width = 999;
        Assert(clone.Panels[0].Text == "A" && clone.Panels[0].Width == 1200, "Atlas copies detach panel settings");

        foreach (string type in new[] { "MAX SPAN", "HP", "NO ENTRY", "机型文字" })
        {
            Settings other = new Settings { Kind = AssetKind.GroundOther, OtherStyle = type, Text = type == "MAX SPAN" ? "36" : type == "HP" ? "4" : "A320/B737", CharacterHeight = type == "机型文字" ? 300 : 4000 };
            Sample(engine, other, "v2_ground_" + (type == "机型文字" ? "aircraft" : type.Replace(" ", "_")), directory, 2048);
            using (SignDocument d = engine.Build(other))
            {
                if (type == "MAX SPAN") { Near(d.Height, 9500, "MAX SPAN figure outer height"); Assert(d.Caption.EndsWith("36 m"), "MAX SPAN lowercase unit"); }
                if (type == "HP") Assert(d.Caption == "HP4", "HP prefix added once");
                if (type == "NO ENTRY") Assert(d.Caption == "NO ENTRY" && d.Shapes[1].Fill == SignEngine.Red, "NO ENTRY fixed text and red field");
            }
            if (type == "MAX SPAN" || type == "机型文字")
            {
                other.BlackBackground = false; Sample(engine, other, type == "MAX SPAN" ? "v2_ground_MAX_SPAN_transparent" : "v2_ground_aircraft_transparent", directory, 2048);
                using (SignDocument d = engine.Build(other)) Assert(d.Shapes.All(p => p.Fill == SignEngine.Yellow), type + " transparent background has no black fill");
            }
        }
        foreach (string invalid in new[] { "0", "999", "ABC", "36MM" })
            Throws(() => { using (engine.Build(new Settings { Kind = AssetKind.GroundOther, OtherStyle = "MAX SPAN", Text = invalid })) { } }, "Invalid wingspan " + invalid);
        Throws(() => { using (engine.Build(new Settings { Kind = AssetKind.GroundOther, OtherStyle = "HP", Text = "AA" })) { } }, "Invalid HP number");
    }

    static void VerifyAtlas(SignEngine engine, string directory)
    {
        AtlasWorkspace state = new AtlasWorkspace { Width = 2048, Height = 1024 };
        using (AtlasLayout empty = AtlasLayout.Build(engine, state))
            Assert(empty.Slots.Count == 6 && !empty.CanExport && empty.Document.Shapes.Count == 0, "Empty grid selectable before adding signs");
        Settings ground = new Settings { Text = "A1" };
        state.PutCurrent(ground); ground.Text = "ZZ";
        Assert(state.Slots[0].Text == "A1", "Import current sign stores detached settings");
        state.Slots[1] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "↑" };
        Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
        taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "B", Arrow = "←" });
        taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无" });
        taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "C2", Arrow = "↗" });
        state.Slots[2] = taxi;
        state.Slots[3] = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600, Coordinates = true, Latitude = "N 52 30.2", Longitude = "E 114 25.1" };
        state.Slots[4] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "MAX SPAN", Text = "36" };
        Size suggestion;
        float originalSmallest;
        using (AtlasLayout mixed = AtlasLayout.Build(engine, state))
        {
            Assert(mixed.CanExport && state.Filled == 5, "All five asset classes coexist with an empty slot");
            Near(mixed.Document.Width, 2048, "Atlas canvas width is pixels"); Near(mixed.Document.Height, 1024, "Atlas canvas height is pixels");
            Near(mixed.Slots[0].Cell.Width, 656, "Grid width accounts for gutters and outer padding");
            Near(mixed.Slots[0].Cell.Height, 484, "Grid height accounts for gutters and outer padding");
            int shapeIndex = 0;
            for (int i = 0; i < state.Count; i++)
            {
                AtlasSlotInfo info = mixed.Slots[i];
                Assert(mixed.HitTest(new PointF(info.Cell.X + info.Cell.Width / 2, info.Cell.Y + info.Cell.Height / 2)) == i, "Click targets populated and empty slots " + i);
                if (!info.Occupied) continue;
                using (SignDocument part = engine.Build(state.Slots[i]))
                {
                    Near(info.Image.Width / info.Image.Height, part.Width / part.Height, "Slot fit preserves aspect ratio " + i);
                    Near(info.Image.Left - info.Cell.Left, (info.Cell.Width - info.Image.Width) / 2, "Slot centered horizontally " + i);
                    foreach (Shape shape in mixed.Document.Shapes.Skip(shapeIndex).Take(part.Shapes.Count))
                    {
                        RectangleF b = shape.Path.GetBounds();
                        Assert(b.Left >= info.Cell.Left && b.Right <= info.Cell.Right && b.Top >= info.Cell.Top && b.Bottom <= info.Cell.Bottom, "Mixed content stays inside its own slot " + i);
                    }
                    shapeIndex += part.Shapes.Count;
                }
            }
            Assert(mixed.HitTest(new PointF(mixed.Slots[0].Cell.Right + 12, 200)) == -1, "Gap clicks do not select a neighbour");
            Near(mixed.Slots[3].SmallestTextPixels, mixed.Slots[3].CharacterPixels / 4, "Density accounts for small coordinate text");
            Near(mixed.Slots[4].SmallestTextPixels, mixed.Slots[4].CharacterPixels * 0.7f, "Density accounts for metre unit");
            Assert(mixed.Slots[0].Scale != mixed.Slots[2].Scale, "Ground signs and vertical signs use independent fit scales");
            originalSmallest = mixed.Slots.Where(s => s.Occupied).Min(s => s.SmallestTextPixels);
            Assert(originalSmallest < 128, "Dense mixed layout triggers reference warning");
            suggestion = mixed.Recommend(state);
            Assert(!suggestion.IsEmpty && suggestion.Width > state.Width && suggestion.Width == 2 * suggestion.Height, "Density recommendation increases resolution and preserves aspect");
            using (Bitmap png = Exporter.RasterPixels(mixed.Document))
            {
                Assert(png.Size == new Size(2048, 1024) && png.PixelFormat == PixelFormat.Format32bppArgb, "Export exact custom canvas in RGBA");
                Assert(png.GetPixel(20, 20).A == 0, "Editor slot badge is absent from exported PNG");
                Assert(png.GetPixel(684, 200).A == 0 && png.GetPixel(1700, 760).A == 0, "Gutters and empty cell remain transparent");
                png.Save(Path.Combine(directory, "v3_mixed_atlas.png"), ImageFormat.Png);
            }
            string svg = Exporter.Svg(mixed.Document, true);
            Assert((string)XDocument.Parse(svg).Root.Attribute("width") == "2048px", "Atlas SVG explicitly uses pixel units");
            Assert(!svg.Contains("点击设置") && !svg.Contains("检查输入"), "Editor grid overlays are absent from SVG");
            File.WriteAllText(Path.Combine(directory, "v3_mixed_atlas.svg"), svg);
        }
        state.Width = suggestion.Width; state.Height = suggestion.Height;
        using (AtlasLayout larger = AtlasLayout.Build(engine, state))
            Assert(larger.Slots.Where(s => s.Occupied).All(s => s.SmallestTextPixels >= state.TargetTextPixels), "Suggested resolution actually meets reference for every slot");
        state.Width = 1536; state.Height = 1024;
        using (AtlasLayout custom = AtlasLayout.Build(engine, state))
        using (Bitmap png = Exporter.RasterPixels(custom.Document)) Assert(png.Size == new Size(1536, 1024), "Non-power-of-two canvas supported without rounding");
        state.TargetTextPixels = 1024;
        using (AtlasLayout dense = AtlasLayout.Build(engine, state)) Assert(dense.Recommend(state).IsEmpty, "Unachievable density asks for fewer slots instead of a false recommendation");
        state.Slots[4].Text = "ABC";
        using (AtlasLayout invalid = AtlasLayout.Build(engine, state))
            Assert(!invalid.CanExport && invalid.Slots[4].Error.Length > 0 && invalid.Slots[0].Error.Length == 0 && invalid.Document.Shapes.Count > 0, "One invalid slot blocks export but keeps other slots editable");
        Throws(() => state.Resize(1, 3), "Shrinking cannot silently discard populated slots");
        Assert(state.Rows == 2 && state.Columns == 3 && state.Slots[4].Text == "ABC", "Rejected shrink preserves settings and arrangement");
        Settings saved = state.Slots[0]; state.Resize(4, 4);
        Assert(object.ReferenceEquals(saved, state.Slots[0]), "Increasing grid keeps existing slot contents");
        state.Slots[4] = null; state.Selected = 15; state.Resize(2, 2);
        Assert(state.Selected == 3 && object.ReferenceEquals(saved, state.Slots[0]), "Safe shrink retains occupied slots and valid selection");
        state.Width = state.Height = 128; state.Padding = 64;
        Throws(() => { using (AtlasLayout.Build(engine, state)) { } }, "Impossible gutter/padding combination rejected");
    }
    static void CheckPartition(AtlasWorkspace state, string label)
    {
        AtlasRegion[] regions = state.Regions.Where(r => r != null).ToArray();
        Near(regions.Sum(r => r.Bounds.Width * r.Bounds.Height), 1, label + " covers full canvas");
        foreach (AtlasRegion region in regions)
        {
            RectangleF b = region.Bounds;
            Assert(b.Left >= -0.00001f && b.Top >= -0.00001f && b.Right <= 1.00001f && b.Bottom <= 1.00001f && b.Width > 0 && b.Height > 0, label + " positive bounded region " + region.Index);
            foreach (AtlasRegion other in regions.Where(r => r.Index > region.Index))
            {
                RectangleF overlap = RectangleF.Intersect(b, other.Bounds);
                Assert(overlap.Width * overlap.Height < 0.00001f, label + " no overlap " + region.Index + "/" + other.Index);
            }
        }
    }

    static void VerifyMergedAtlas(SignEngine engine, string directory)
    {
        AtlasWorkspace state = new AtlasWorkspace { Width = 2048, Height = 1024 };
        Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
        taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "APRON", Arrow = "←" });
        state.Slots[0] = taxi;
        state.Slots[1] = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600 };
        Settings hidden = state.Slots[1];
        float before;
        using (AtlasLayout layout = AtlasLayout.Build(engine, state)) before = layout.Slots[0].CharacterPixels;
        state.Merge(new[] { 0, 1 });
        Assert(state.RegionCount == 5 && state.Filled == 1 && state.Regions[1] == null, "Horizontal merge becomes one rendered region");
        Assert(object.ReferenceEquals(hidden, state.Slots[1]), "Merged-away content retained for splitting");
        using (AtlasLayout layout = AtlasLayout.Build(engine, state))
        {
            Near(layout.Slots[0].Cell.Width, 1336, "Merged cell includes old internal gutter");
            Assert(layout.HitTest(new PointF(1000, 250)) == 0 && !layout.Slots[1].Visible, "Former covered cell selects merged owner");
            Assert(layout.Slots[0].CharacterPixels > before, "Larger merged area raises actual text density");
            Size suggestion = layout.Recommend(state);
            Assert(!suggestion.IsEmpty, "Merged layout gives density recommendation");
            int oldWidth = state.Width, oldHeight = state.Height;
            state.Width = suggestion.Width; state.Height = suggestion.Height;
            using (AtlasLayout suggested = AtlasLayout.Build(engine, state))
                Assert(suggested.Slots.Where(s => s.Occupied).All(s => s.SmallestTextPixels >= state.TargetTextPixels), "Recommendation uses merged bounds");
            state.Width = oldWidth; state.Height = oldHeight;
        }
        state.Merge(new[] { 0, 2 }); state.Split(0);
        Assert(state.Regions[0].Children != null && state.Regions[2] != null && state.Regions[1] == null, "Nested merge splits one level at a time");
        AtlasDivider mergedEdge = state.Divider(0, AtlasEdge.Right);
        state.MoveDivider(mergedEdge, 0.78f); state.Split(0);
        Near(state.Regions[0].Bounds.Right, 0.39f, "Split follows resized parent proportions");
        Assert(object.ReferenceEquals(hidden, state.Slots[1]) && state.Filled == 2, "Split restores original secondary content");
        CheckPartition(state, "Resized nested split");
        state.ResetGridLayout();
        Throws(() => state.Merge(new[] { 0, 4 }), "Disconnected selection cannot merge");
        Throws(() => state.Merge(new[] { 0, 1, 3 }), "L-shaped selection cannot merge");
        Assert(state.RegionCount == 6 && state.Regions[0].Children == null, "Rejected merge leaves layout intact");

        RectangleF bottom = state.Regions[3].Bounds;
        AtlasDivider local = state.Divider(0, AtlasEdge.Right);
        Assert(local.Before.Length == 1 && local.After.Length == 1, "Aligned rows allow independent local divider");
        state.MoveDivider(local, 0.44f);
        Near(state.Regions[0].Bounds.Right, 0.44f, "Local boundary follows drag");
        Assert(state.Regions[3].Bounds == bottom, "Dragging top row leaves bottom row widths unchanged");
        AtlasDivider acrossT = state.Divider(0, AtlasEdge.Bottom);
        Assert(acrossT.Before.Length == 2 && acrossT.After.Length == 2, "T-junction includes all adjacent rectangular regions");
        state.MoveDivider(acrossT, 0.62f); CheckPartition(state, "Local T-junction drag");
        state.MoveDivider(local, -10); CheckPartition(state, "Lower drag clamp");
        state.MoveDivider(local, 10); CheckPartition(state, "Upper drag clamp");
        Assert(state.Divider(0, AtlasEdge.Left) == null && state.Divider(0, AtlasEdge.Top) == null, "Outer canvas boundary stays fixed");

        state.ResetGridLayout(); state.Merge(new[] { 0, 3 });
        AtlasDivider tall = state.Divider(0, AtlasEdge.Right);
        Assert(tall.Before.Length == 1 && tall.After.Length == 2, "Tall merge resizes both short neighbours");
        state.MoveDivider(tall, 0.45f);
        Near(state.Regions[1].Bounds.Left, 0.45f, "Upper neighbour follows tall boundary");
        Near(state.Regions[4].Bounds.Left, 0.45f, "Lower neighbour follows tall boundary");
        state.Split(0); CheckPartition(state, "Tall split after drag");

        AtlasWorkspace insert = new AtlasWorkspace(); insert.Merge(new[] { 0, 1 });
        insert.PutCurrent(taxi); insert.PutCurrent(hidden);
        Assert(insert.Selected == 2 && insert.Slots[1] == null, "Add current skips hidden cells even when empty");
        insert.Selected = 0; insert.Slots[1] = hidden;
        Throws(() => insert.Resize(1, 1), "Grid shrink protects hidden merged content");
        insert.ResetGridLayout(); Assert(insert.RegionCount == 6 && insert.Filled == 3, "Reset grid restores hidden content");

        AtlasWorkspace narrow = new AtlasWorkspace(); narrow.Merge(new[] { 0, 1 });
        narrow.MoveDivider(narrow.Divider(0, AtlasEdge.Right), 0);
        Throws(() => narrow.Split(0), "Too-small split rejected before it can hide the preview");
        using (AtlasLayout layout = AtlasLayout.Build(engine, narrow))
            Assert(narrow.RegionCount == 5 && narrow.Regions[0].Children != null && layout.Slots[0].Visible, "Rejected split preserves usable merged region");

        AtlasWorkspace sample = new AtlasWorkspace(); sample.Resize(3, 4);
        sample.Slots[0] = taxi.Copy(); sample.Merge(new[] { 0, 1, 2 });
        sample.Slots[3] = new Settings { Kind = AssetKind.StandSign, Text = "101L", CharacterHeight = 600 };
        sample.Selected = 4; sample.Slots[4] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "↑" }; sample.Merge(new[] { 4, 8 });
        sample.Selected = 5; sample.Slots[5] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "MAX SPAN", Text = "36" }; sample.Merge(new[] { 5, 6, 9, 10 });
        sample.Selected = 7; sample.Slots[7] = new Settings { Text = "A1" }; sample.Merge(new[] { 7, 11 });
        sample.MoveDivider(sample.Divider(5, AtlasEdge.Right), 0.82f);
        sample.MoveDivider(sample.Divider(4, AtlasEdge.Right), 0.18f);
        sample.MoveDivider(sample.Divider(0, AtlasEdge.Bottom), 0.24f);
        CheckPartition(sample, "Mixed merged export");
        using (AtlasLayout layout = AtlasLayout.Build(engine, sample))
        {
            Assert(layout.CanExport && sample.Filled == 5 && sample.RegionCount == 5, "Mixed merged and resized regions export together");
            foreach (AtlasSlotInfo slot in layout.Slots.Where(s => s.Visible))
            {
                Assert(layout.HitTest(new PointF(slot.Cell.X + slot.Cell.Width / 2, slot.Cell.Y + slot.Cell.Height / 2)) == slot.Index, "Merged area hit test " + slot.Index);
                Near(slot.Image.Width / slot.Image.Height, slot.PhysicalWidth / slot.PhysicalHeight, "Merged/resized sign preserves aspect " + slot.Index);
            }
            using (Bitmap png = Exporter.RasterPixels(layout.Document))
            {
                Assert(png.Size == new Size(2048, 2048) && png.GetPixel(20, 20).A == 0, "Merged export exact canvas without selection badges");
                png.Save(Path.Combine(directory, "v3_1_merged_atlas.png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(directory, "v3_1_merged_atlas.svg"), Exporter.Svg(layout.Document, true));
        }
    }

    static void VerifyProjectAndPol(SignEngine engine, string directory)
    {
        AtlasWorkspace w = new AtlasWorkspace { Width = 3072, Height = 1536, Gap = 18, Padding = 12, TargetTextPixels = 96,
            PolPrefix = "Demo", PolLayer = "taxiways", PolOffset = 2 };
        w.Resize(3, 4);
        w.Slots[0] = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300, Height = 700 };
        w.Slots[0].Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无", Width = 1000 });
        w.Slots[0].Panels.Add(new SignPanel { Kind = "方向", Text = "B2", Arrow = "↗", Width = 1600 });
        w.Slots[1] = new Settings { Text = "A2", ArrowAbove = true, Arrow = "←", GroundGap = 0.18f, Border = false };
        w.Slots[2] = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 800, Coordinates = true,
            Latitude = "N 52 30.2", Longitude = "E 114 25.1", Width = 6000, Height = 3000 };
        w.Slots[3] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "→", Width = 16000, Height = 9000 };
        w.Slots[4] = new Settings { Kind = AssetKind.GroundOther, Text = "36", OtherStyle = "MAX SPAN", BlackBackground = false };
        w.Slots[5] = new Settings { Kind = AssetKind.GroundOther, Text = "4", OtherStyle = "HP" };
        w.Slots[6] = new Settings { Kind = AssetKind.GroundOther, Text = "NO ENTRY", OtherStyle = "NO ENTRY", CharacterHeight = 2000 };
        w.Slots[7] = new Settings { Kind = AssetKind.GroundOther, Text = "A320/B737", OtherStyle = "机型文字", CharacterHeight = 500, BlackBackground = false };
        w.Slots[8] = w.Slots[3].Copy(); w.Slots[9] = w.Slots[1].Copy(); w.Slots[10] = w.Slots[2].Copy();
        w.PolNames[0] = "Taxi_A_B2"; w.PolNames[2] = "Hidden_Stand"; w.PolNames[4] = "Wing36";
        w.Merge(new[] { 0, 1 }); w.Merge(new[] { 0, 2, 3 });
        w.MoveDivider(w.Divider(0, AtlasEdge.Bottom), 0.25f);
        w.MoveDivider(w.Divider(4, AtlasEdge.Right), 0.18f);
        string text = ProjectStore.Encode(w);
        AtlasWorkspace restored = ProjectStore.Decode(text);
        Assert(ProjectStore.Encode(restored) == text, "Every serialized parameter and nested boundary survives round trip");
        Assert(restored.Slots[0].Panels.Count == 2 && restored.Slots[0].Panels[1].Text == "B2" && restored.Slots[0].Panels[1].Width == 1600, "Readonly panel list restores all segments");
        Assert(restored.Regions[0].CellCount == 4 && restored.Regions[1] == null && restored.Regions[2] == null, "Nested merged cells reload hidden");
        Assert(restored.Width == 3072 && restored.Height == 1536 && restored.TargetTextPixels == 96 && restored.Gap == 18 && restored.Padding == 12, "Canvas, density, gap and padding restored");
        using (AtlasLayout before = AtlasLayout.Build(engine, w))
        using (AtlasLayout after = AtlasLayout.Build(engine, restored))
        {
            Assert(before.CanExport && after.CanExport, "Loaded mixed project can export");
            Assert(Exporter.Svg(before.Document, true) == Exporter.Svg(after.Document, true), "Loaded project renders identical geometry");
        }
        restored.Split(0); restored.Split(0);
        Assert(restored.Slots[1].Text == "A2" && restored.Slots[2].Coordinates && restored.PolNames[2] == "Hidden_Stand" && restored.Regions[3] != null, "Split after loading restores hidden content and names");
        CheckPartition(restored, "Reloaded nested split");
        XDocument invalid = XDocument.Parse(text); invalid.Root.Element("Version").Value = "999";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Unknown project version rejected");
        invalid = XDocument.Parse(text); invalid.Root.Element("Width").Value = "9000";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Out-of-range project canvas rejected");
        invalid = XDocument.Parse(text); invalid.Descendants("RegionData").First().Element("X").Value = "NaN";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Nonfinite project geometry rejected");
        invalid = XDocument.Parse(text); invalid.Descendants("RegionData").First().Element("Width").Value = "0.9";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Broken merged partition rejected");
        invalid = XDocument.Parse(text); invalid.Descendants("SignPanel").First().Element("Kind").Value = "invalid";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Invalid combo-box choice rejected before UI loading");
        string projectPath = Path.Combine(directory, "示例工程.assproj");
        ProjectStore.Save(w, projectPath); ProjectStore.Save(w, projectPath);
        Assert(File.ReadAllText(projectPath) == text && w.SavedProject == text, "Atomic save and overwrite store exact state");
        bool failed = false;
        try { ProjectStore.AtomicFile(projectPath, p => { File.WriteAllText(p, "partial"); throw new IOException("Simulated interrupted write"); }); }
        catch (IOException) { failed = true; }
        Assert(failed && File.ReadAllText(projectPath) == text, "Failed write leaves previous project intact");
        Assert(!Directory.GetFiles(directory, ".*.tmp").Any(), "Atomic write cleans temporary files");
        restored = ProjectStore.Decode(File.ReadAllText(projectPath)); restored.ProjectPath = projectPath;
        AtlasWorkspace target = new AtlasWorkspace(); ProjectStore.Apply(target, restored);
        Assert(ProjectStore.Encode(target) == text && target.ProjectPath == projectPath && target.SavedProject == text, "Apply loaded project replaces all state");
        target.Selected = 4; Assert(ProjectStore.Encode(target) == text, "Selection alone does not mark project modified");
        target.Slots[4].Text = "";
        Assert(ProjectStore.Decode(ProjectStore.Encode(target)).Slots[4].Text == "", "Draft with incomplete sign content can be saved and loaded");
        target.Slots[0].Panels[0].Width = -100;
        Assert(ProjectStore.Decode(ProjectStore.Encode(target)).Slots[0].Panels[0].Width == -100, "Draft with invalid text-entered width can be reopened for correction");

        string exportFolder = Path.Combine(directory, "示例_POL");
        CultureInfo previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try { System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR"); PolExporter.Export(engine, w, exportFolder); }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = previous; }
        using (AtlasLayout layout = AtlasLayout.Build(engine, w))
        {
            Assert(Directory.GetFiles(exportFolder).Length == 2, "Direct export contains exactly two files, no archive or project");
            Assert(Directory.GetFiles(exportFolder, "*.pol").Length == 1 && File.Exists(Path.Combine(exportFolder, "Demo.pol")), "Exactly one POL uses configured total name");
            using (Bitmap bitmap = new Bitmap(Path.Combine(exportFolder, "Demo_atlas.png"))) Assert(bitmap.Size == new Size(3072, 1536), "Shared PNG uses exact custom resolution");
            string pol = File.ReadAllText(Path.Combine(exportFolder, "Demo.pol"));
            Assert(pol.StartsWith("A\n850\nDRAPED_POLYGON\n") && pol.Contains("TEXTURE_NOWRAP Demo_atlas.png\n") && pol.Contains("LAYER_GROUP taxiways 2\n"), "Total POL header, texture and layer");
            string[] lines = pol.Split('\n'), subtex = lines.Where(l => l.StartsWith("#subtex ")).ToArray();
            AtlasSlotInfo[] slots = layout.Slots.Where(s => s.Visible && s.Occupied).ToArray();
            Assert(subtex.Length == 8 && subtex.Length == slots.Length, "All visible occupied regions have one subtex in total POL");
            Assert(!lines.Contains("# Region 2") && !lines.Contains("# Region 3") && !lines.Contains("# Region 4") && !lines.Contains("# Region 12"), "Hidden merged cells and empty cell excluded from subtex");
            Assert(lines.Count(l => l.StartsWith("TEXTURE_")) == 1 && lines.Count(l => l.StartsWith("LAYER_GROUP ")) == 1, "Total POL has one texture and one layer");
            float[] scale = lines.Single(l => l.StartsWith("SCALE ")).Split(' ').Skip(1).Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
            Near(scale[0] * slots[0].Scale * 1000, w.Width, "Total SCALE references first occupied region density, full atlas width");
            Near(scale[1] * slots[0].Scale * 1000, w.Height, "Total SCALE references first occupied region density, full atlas height");
            for (int i = 0; i < slots.Length; i++)
            {
                AtlasSlotInfo slot = slots[i];
                float[] uv = subtex[i].Split(' ').Skip(1).Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                Near(uv[0] * w.Width, slot.Artwork.Left - 2, "POL left edge includes default bleed " + slot.Index); Near(uv[2] * w.Width, slot.Artwork.Right + 2, "POL right edge includes default bleed " + slot.Index);
                Near((1 - uv[1]) * w.Height, slot.Artwork.Bottom + 2, "POL bottom with bleed flips Y " + slot.Index); Near((1 - uv[3]) * w.Height, slot.Artwork.Top - 2, "POL top with bleed flips Y " + slot.Index);
                Assert(uv.All(n => n >= 0 && n <= 1) && uv[0] < uv[2] && uv[1] < uv[3], "UV is normalized and ordered " + slot.Index);
                int comment = Array.IndexOf(lines, "# Region " + (slot.Index + 1));
                float[] meters = lines[comment + 1].Substring(lines[comment + 1].IndexOf(": ") + 2).Split(' ').Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                Near(meters[0], (slot.Artwork.Width + 4) / (slot.Scale * 1000), "Subtexture size includes bleed width " + slot.Index);
                Near(meters[1], (slot.Artwork.Height + 4) / (slot.Scale * 1000), "Subtexture size includes bleed height " + slot.Index);
            }
            w.PolNames[4] = "../old-unused-name";
            Assert(PolExporter.Text(w, slots) == pol, "Old per-cell names do not affect total POL");
            w.PolNames[4] = "Wing36";
        }
        string oldText = File.ReadAllText(Path.Combine(Directory.GetParent(directory).FullName, "tests", "fixtures", "v3_2.assproj"));
        AtlasWorkspace old = ProjectStore.Decode(oldText);
        XDocument migrated = XDocument.Parse(ProjectStore.Encode(old)); migrated.Root.Element("PolBleed").Remove(); migrated.Root.Element("Paint").Remove();
        migrated.Root.Element("PolBoundary").Remove(); migrated.Descendants("Colors").Remove();
        migrated.Root.Element("TypeColors").Remove();
        migrated.Root.Element("BaseColors").Remove();
        migrated.Root.Element("XPlane").Remove();
        XDocument legacy = XDocument.Parse(oldText);
        // XML namespace declaration order differs between .NET Framework and modern .NET.
        foreach (XElement root in new[] { migrated.Root, legacy.Root }) root.ReplaceAttributes(root.Attributes().OrderBy(a => a.Name.ToString()).ToArray());
        Assert(XNode.DeepEquals(migrated, legacy) && old.XPlane && old.PolBleed == 2 && !old.Paint.Active && old.TypeColors.Entries.Count == 0 && !old.BaseColors.Active, "Old project keeps existing settings, X-Plane workflow, 2 px bleed, clean paint and no palette overrides");
        old.Split(0);
        using (AtlasLayout split = AtlasLayout.Build(engine, old))
        {
            string pol = PolExporter.Text(old, split.Slots.Where(s => s.Visible && s.Occupied).ToArray());
            Assert(pol.Split('\n').Count(l => l.StartsWith("#subtex ")) == 10, "Splitting nested merge updates total POL partition count");
        }
        byte[] priorPng = File.ReadAllBytes(Path.Combine(exportFolder, "Demo_atlas.png")); w.PolPrefix = "../invalid";
        Throws(() => PolExporter.Export(engine, w, exportFolder), "Invalid filename rejected before writing");
        Assert(File.ReadAllBytes(Path.Combine(exportFolder, "Demo_atlas.png")).SequenceEqual(priorPng), "Invalid export preserves prior image");
        w.PolPrefix = "Demo"; w.Paint = PaintSettings.Preset(3);
        failed = false;
        using (FileStream locked = new FileStream(Path.Combine(exportFolder, "Demo.pol"), FileMode.Open, FileAccess.Read, FileShare.None))
            try { PolExporter.Export(engine, w, exportFolder); } catch (IOException) { failed = true; }
        Assert(failed && File.ReadAllBytes(Path.Combine(exportFolder, "Demo_atlas.png")).SequenceEqual(priorPng), "Locked POL rolls PNG back to the prior pair");
        Assert(Directory.GetFiles(exportFolder).Length == 2, "Failed pair replacement cleans staging and backup files");
        Throws(() => PolExporter.Name("CON"), "Reserved Windows device name rejected for total POL");
    }

    static void VerifyArtworkBounds(SignEngine engine, string directory)
    {
        AtlasWorkspace w = new AtlasWorkspace { Width = 2048, Height = 1024, Gap = 32, Padding = 24, PolBleed = 0 };
        w.Resize(2, 4);
        w.Slots[0] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "机型文字", Text = "A320/B737", CharacterHeight = 500, BlackBackground = false, Width = 12000, Height = 5000 };
        w.Slots[1] = w.Slots[0].Copy(); w.Slots[1].BlackBackground = true;
        w.Slots[2] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "MAX SPAN", Text = "36", BlackBackground = false, Border = false, Width = 30000, Height = 16000 };
        w.Slots[3] = w.Slots[2].Copy(); w.Slots[3].Border = true;
        w.Slots[4] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "→", Border = true };
        w.Slots[5] = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600, Width = 4000, Height = 1500 };
        using (AtlasLayout layout = AtlasLayout.Build(engine, w))
        using (Bitmap bitmap = Exporter.RasterPixels(layout.Document))
        {
            Assert(layout.CanExport, "Artwork bounds test contains valid mixed signs");
            AtlasSlotInfo aircraft = layout.Slots[0], span = layout.Slots[2];
            Near(aircraft.Artwork.Width / aircraft.Scale, engine.TextWidth("A320/B737", "ground", 500, 0.1f), "Transparent aircraft crops to glyph width despite wide custom canvas");
            Near(aircraft.Artwork.Height / aircraft.Scale, 500, "Transparent aircraft crops to glyph height despite tall custom canvas");
            Near(span.Artwork.Width / span.Scale, engine.TextWidth("MAX SPAN", "ground", 4000, 0.1f), "Unframed transparent MAX SPAN crops to text block width");
            Near(span.Artwork.Height / span.Scale, 8500, "Unframed transparent MAX SPAN retains both rows");
            foreach (int index in new[] { 1, 3, 4, 5 })
            {
                AtlasSlotInfo slot = layout.Slots[index];
                Near(slot.Artwork.Left, slot.Image.Left, "Visible background or border left retained " + index);
                Near(slot.Artwork.Top, slot.Image.Top, "Visible background or border top retained " + index);
                Near(slot.Artwork.Width, slot.Image.Width, "Visible background or border full width retained " + index);
                Near(slot.Artwork.Height, slot.Image.Height, "Visible background or border full height retained " + index);
            }
            foreach (AtlasSlotInfo slot in layout.Slots.Where(s => s.Visible && s.Occupied))
            {
                int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
                for (int y = (int)Math.Floor(slot.Cell.Top); y < Math.Ceiling(slot.Cell.Bottom); y++)
                    for (int x = (int)Math.Floor(slot.Cell.Left); x < Math.Ceiling(slot.Cell.Right); x++)
                        if (bitmap.GetPixel(x, y).A > 16) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                Assert(right >= left && bottom >= top, "Visible raster content exists " + slot.Index);
                Assert(Math.Abs(left - slot.Artwork.Left) <= 2 && Math.Abs(top - slot.Artwork.Top) <= 2 &&
                    Math.Abs(right + 1 - slot.Artwork.Right) <= 2 && Math.Abs(bottom + 1 - slot.Artwork.Bottom) <= 2,
                    "Tight artwork bounds match independent raster alpha within antialias coverage " + slot.Index);
                Assert(slot.Artwork.Left > slot.Cell.Left && slot.Artwork.Top > slot.Cell.Top && slot.Artwork.Right < slot.Cell.Right && slot.Artwork.Bottom < slot.Cell.Bottom,
                    "Atlas padding excluded from artwork bounds " + slot.Index);
            }
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen cell = new Pen(Color.Gray, 2))
            using (Pen art = new Pen(Color.DeepSkyBlue, 2))
            {
                cell.DashStyle = DashStyle.Dash;
                foreach (AtlasSlotInfo slot in layout.Slots.Where(s => s.Visible && s.Occupied))
                {
                    g.DrawRectangle(cell, slot.Cell.X, slot.Cell.Y, slot.Cell.Width, slot.Cell.Height);
                    g.DrawRectangle(art, slot.Artwork.X, slot.Artwork.Y, slot.Artwork.Width, slot.Artwork.Height);
                }
            }
            bitmap.Save(Path.Combine(directory, "artwork-bounds-check.png"), ImageFormat.Png);
        }
        w.Selected = 0; w.Merge(new[] { 0, 1 }); w.MoveDivider(w.Divider(0, AtlasEdge.Right), 0.6f);
        using (AtlasLayout merged = AtlasLayout.Build(engine, w))
        {
            AtlasSlotInfo slot = merged.Slots[0];
            Near(slot.Artwork.Width / slot.Scale, engine.TextWidth("A320/B737", "ground", 500, 0.1f), "Merge and moved boundary retain tight artwork width");
            string pol = PolExporter.Text(w, merged.Slots.Where(s => s.Visible && s.Occupied).ToArray());
            float[] uv = pol.Split('\n').First(l => l.StartsWith("#subtex ")).Split(' ').Skip(1).Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
            Near(uv[0] * w.Width, slot.Artwork.Left, "Merged POL bounds follow moved artwork left");
            Near((1 - uv[1]) * w.Height, slot.Artwork.Bottom, "Merged POL bounds follow moved artwork bottom");
        }
    }

    static void VerifyBleed(SignEngine engine)
    {
        AtlasWorkspace w = new AtlasWorkspace { Width = 1024, Height = 512, Padding = 8, Gap = 12 };
        w.Resize(1, 2); w.Slots[0] = new Settings { Text = "A1" }; w.Slots[1] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "→" };
        Assert(w.PolBleed == 2, "New project has small 2 px bleed");
        using (AtlasLayout layout = AtlasLayout.Build(engine, w))
        {
            AtlasSlotInfo[] slots = layout.Slots.Where(s => s.Occupied).ToArray();
            string before = Exporter.Svg(layout.Document, true);
            RectangleF a = slots[0].Artwork, b = PolExporter.RegionBounds(slots[0], 6);
            Near(b.Left, a.Left - 6, "Custom bleed expands left by requested final pixels"); Near(b.Right, a.Right + 6, "Custom bleed expands right");
            Near(b.Top, a.Top - 6, "Custom bleed expands top"); Near(b.Bottom, a.Bottom + 6, "Custom bleed expands bottom");
            Assert(PolExporter.RegionBounds(slots[0], 0) == a, "Zero bleed preserves v3.4 tight bounds");
            w.PolBleed = 32;
            string pol = PolExporter.Text(w, slots);
            string[] subtex = pol.Split('\n').Where(l => l.StartsWith("#subtex ")).ToArray();
            for (int i = 0; i < slots.Length; i++)
            {
                AtlasSlotInfo s = slots[i];
                float[] uv = subtex[i].Split(' ').Skip(1).Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                Near(uv[0] * w.Width, Math.Max(s.Cell.Left, s.Artwork.Left - 32), "Large bleed clips to cell left " + i);
                Near(uv[2] * w.Width, Math.Min(s.Cell.Right, s.Artwork.Right + 32), "Large bleed clips to cell right " + i);
                Near((1 - uv[1]) * w.Height, Math.Min(s.Cell.Bottom, s.Artwork.Bottom + 32), "Large bleed clips to cell bottom " + i);
                Near((1 - uv[3]) * w.Height, Math.Max(s.Cell.Top, s.Artwork.Top - 32), "Large bleed clips to cell top " + i);
                Assert(uv.All(n => n >= 0 && n <= 1), "Bleed stays inside texture " + i);
            }
            Assert(!PolExporter.RegionBounds(slots[0], 32).IntersectsWith(PolExporter.RegionBounds(slots[1], 32)), "Bleed does not include neighbouring signs");
            Assert(Exporter.Svg(layout.Document, true) == before, "Changing bleed never stretches or redraws artwork");
            Throws(() => PolExporter.RegionBounds(slots[0], -1), "Negative bleed rejected");
            Throws(() => PolExporter.RegionBounds(slots[0], 33), "Excessive bleed rejected");
        }
        w.PolBleed = 6;
        AtlasWorkspace restored = ProjectStore.Decode(ProjectStore.Encode(w)), target = new AtlasWorkspace();
        ProjectStore.Apply(target, restored);
        Assert(restored.PolBleed == 6 && target.PolBleed == 6, "Custom bleed survives project save/load/apply");
        XDocument invalid = XDocument.Parse(ProjectStore.Encode(w)); invalid.Root.Element("PolBleed").Value = "33";
        Throws(() => ProjectStore.Decode(invalid.ToString()), "Invalid project bleed rejected before opening UI");
        w.Padding = 0; w.Gap = 0; w.Selected = 0; w.Merge(new[] { 0, 1 });
        using (AtlasLayout merged = AtlasLayout.Build(engine, w))
        {
            AtlasSlotInfo s = merged.Slots[0]; RectangleF bounds = PolExporter.RegionBounds(s, 6);
            Assert(s.Cell.Contains(bounds) && bounds.Contains(s.Artwork), "Merged region clips bleed without cutting artwork when no padding remains");
        }
    }

    static byte[] Pixels(Bitmap image)
    {
        BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++) System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * image.Width * 4, image.Width * 4);
            return bytes;
        }
        finally { image.UnlockBits(data); }
    }
    static void VerifyPaint(SignEngine engine, string directory)
    {
        AtlasWorkspace w = new AtlasWorkspace { Width = 512, Height = 512, Gap = 16, Padding = 16 };
        w.Resize(2, 2);
        w.Slots[0] = new Settings { Text = "A1", Arrow = "→" };
        w.Slots[1] = new Settings { Kind = AssetKind.GroundStand, Text = "21", Arrow = "↑" };
        w.Slots[2] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "MAX SPAN", Text = "36", BlackBackground = false, Border = false };
        w.Slots[3] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "机型文字", Text = "A320", CharacterHeight = 500, BlackBackground = false };
        w.Paint = new PaintSettings { Style = 4, Dirt = 57, Wear = 84, Fade = 31, Size = 135, Seed = 827, Angle = 62, Texture = 74, ReferenceYellow = false };
        string project = ProjectStore.Encode(w);
        AtlasWorkspace loaded = ProjectStore.Decode(project), applied = new AtlasWorkspace(); ProjectStore.Apply(applied, loaded);
        Assert(ProjectStore.Encode(loaded) == project && ProjectStore.Encode(applied) == project, "All paint settings round trip and apply to workspace");
        applied.Paint.Seed++; Assert(loaded.Paint.Seed == 827, "Loaded paint settings are independently editable");
        foreach (string field in new[] { "Style", "Dirt", "Wear", "Fade", "Size", "Seed", "Angle", "Texture" })
        {
            XDocument bad = XDocument.Parse(project); bad.Root.Element("Paint").Element(field).Value = "-1";
            Throws(() => ProjectStore.Decode(bad.ToString()), "Invalid paint setting rejected: " + field);
        }
        using (AtlasLayout layout = AtlasLayout.Build(engine, w))
        using (Bitmap clean = Exporter.RasterPixels(layout.Document))
        using (Bitmap off = PaintRenderer.Raster(layout, new PaintSettings(), 0))
        using (Bitmap zero = PaintRenderer.Raster(layout, new PaintSettings { Style = 3, Texture = 0 }, 0))
        using (Bitmap painted = PaintRenderer.Raster(layout, w.Paint, 0))
        using (Bitmap repeat = PaintRenderer.Raster(layout, loaded.Paint, 0))
        {
            byte[] original = Pixels(clean), effect = Pixels(painted);
            Assert(original.SequenceEqual(Pixels(off)) && original.SequenceEqual(Pixels(zero)), "Off and zero strength preserve exact clean raster");
            Assert(effect.SequenceEqual(Pixels(repeat)) && !effect.SequenceEqual(original), "Saved settings reproduce identical non-clean pixels");
            using (Bitmap reference = PaintRenderer.Raster(layout, PaintSettings.Preset(1), 0))
            {
                byte[] rgba = Pixels(reference); bool sameAlpha = true;
                for (int i = 3; i < rgba.Length; i += 4) sameAlpha &= rgba[i] == original[i];
                Assert(sameAlpha, "Reference light dirt preset retains complete paint coverage without random holes");
            }
            PaintSettings referenceColor = w.Paint.Copy(); referenceColor.ReferenceYellow = true;
            using (Bitmap warm = PaintRenderer.Raster(layout, referenceColor, 0)) Assert(!Pixels(warm).SequenceEqual(effect), "Reference warm yellow can be enabled independently");
            bool outside = false, increased = false; int holes = 0, wornPixels = 0, colorChanges = 0;
            for (int i = 0; i < original.Length; i += 4)
            {
                outside |= original[i + 3] == 0 && effect[i + 3] != 0; increased |= effect[i + 3] > original[i + 3];
                if (original[i + 3] == 255 && effect[i + 3] == 0) holes++;
                if (original[i + 3] == 255 && effect[i + 3] < 240) wornPixels++;
                if (original[i + 3] == 255 && original[i + 1] != effect[i + 1]) colorChanges++;
            }
            Console.WriteLine("Paint alpha: holes={0}, worn={1}, changed colors={2}, outside={3}, increased={4}", holes, wornPixels, colorChanges, outside, increased);
            Assert(!outside && !increased && holes > 0 && wornPixels > 100 && colorChanges > 1000, "Wear produces transparent gaps, reduces paint coverage and never adds paint outside artwork");
            foreach (string control in new[] { "Dirt", "Wear", "Fade", "Size", "Seed", "Angle", "Texture" })
            {
                PaintSettings changed = w.Paint.Copy(); var field = typeof(PaintSettings).GetField(control); field.SetValue(changed, (int)field.GetValue(changed) + 1);
                using (Bitmap variant = PaintRenderer.Raster(layout, changed, 0)) Assert(!Pixels(variant).SequenceEqual(effect), "Paint control changes rendered pixels: " + control);
            }
            string pol = PolExporter.Text(w, layout.Slots.Where(s => s.Visible && s.Occupied).ToArray());
            w.Paint = PaintSettings.Preset(0);
            Assert(PolExporter.Text(w, layout.Slots.Where(s => s.Visible && s.Occupied).ToArray()) == pol, "Paint appearance does not change artwork UV or bleed");
            w.Paint = loaded.Paint.Copy();
            string output = Path.Combine(directory, "paint_export_check"); PolExporter.Export(engine, w, output); PolExporter.Export(engine, w, output);
            using (Bitmap exported = new Bitmap(Path.Combine(output, w.PolPrefix + "_atlas.png"))) Assert(Pixels(exported).SequenceEqual(effect), "Direct export and overwrite bake exactly the preview renderer settings");
            Assert(Directory.GetFiles(output).Length == 2 && File.ReadAllText(Path.Combine(output, w.PolPrefix + ".pol")) == pol, "Paint export emits one PNG and one unchanged total POL");
            using (Bitmap preview = PaintRenderer.Raster(layout, w.Paint, 256)) Assert(preview.Size == new Size(256, 256), "Preview retains canvas aspect and requested edge limit");
        }
        string oldPaintPath = Path.Combine(Directory.GetParent(directory).FullName, "tests", "fixtures", "v3_6_paint.assproj");
        AtlasWorkspace previousPaint = ProjectStore.Decode(File.ReadAllText(oldPaintPath));
        Assert(previousPaint.Paint.Style == 1 && previousPaint.Paint.Dirt == 16 && previousPaint.Paint.Texture == 100 && previousPaint.Paint.ReferenceYellow, "v3.6 paint project retains saved parameters and receives PSD texture defaults");
        // Half-open pixel ownership also prevents double application where cells touch.
        using (Bitmap touch = new Bitmap(64, 32, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(touch))
        {
            g.Clear(Color.Yellow);
            AtlasSlotInfo a = new AtlasSlotInfo { Index = 0, Visible = true, Occupied = true, Cell = new RectangleF(0, 0, 32.4f, 32), Image = new RectangleF(0, 0, 32.4f, 32), Artwork = new RectangleF(0, 0, 32.4f, 32), CharacterHeight = 32, Scale = 1 };
            AtlasSlotInfo b = new AtlasSlotInfo { Index = 1, Visible = true, Occupied = true, Cell = new RectangleF(32.4f, 0, 31.6f, 32), Image = new RectangleF(32.4f, 0, 31.6f, 32), Artwork = new RectangleF(32.4f, 0, 31.6f, 32), CharacterHeight = 32, Scale = 1 };
            using (Bitmap reverse = (Bitmap)touch.Clone())
            {
                PaintSettings dirt = PaintSettings.Preset(2); dirt.Wear = 0;
                PaintRenderer.Apply(touch, new[] { a, b }, dirt, 1); PaintRenderer.Apply(reverse, new[] { b, a }, dirt, 1);
                Assert(Pixels(touch).SequenceEqual(Pixels(reverse)), "Adjacent cells with no gap weather each pixel exactly once");
            }
        }
        w.Width = w.Height = 2048;
        string samples = Path.Combine(directory, "PSD油漆示例"); Directory.CreateDirectory(samples);
        using (Bitmap sheet = new Bitmap(1280, 1370, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font title = new Font("Microsoft YaHei UI", 19))
        using (Font label = new Font("Microsoft YaHei UI", 11))
        {
            g.Clear(Color.FromArgb(34, 39, 42));
            g.DrawString("油漆脏污与磨损 · PNG 烘焙预设", title, Brushes.White, 25, 16);
            g.DrawString("预览底色不导出，掉漆处为透明；污渍 / 剥落 / 褪色 / 大小 / 种子均可调整", label, Brushes.LightGray, 25, 55);
            for (int style = 1; style <= 4; style++)
            {
                w.Paint = PaintSettings.Preset(style); w.PolPrefix = "Paint_" + style;
                string folder = Path.Combine(samples, PaintSettings.Names[style]);
                var watch = System.Diagnostics.Stopwatch.StartNew(); PolExporter.Export(engine, w, folder); watch.Stop();
                Console.WriteLine("Paint preset {0}: 2048 x 2048 export {1} ms", style, watch.ElapsedMilliseconds);
                Assert(Directory.GetFiles(folder).Length == 2, "Preset " + style + " contains exactly PNG + POL");
                ProjectStore.Save(w, Path.Combine(samples, PaintSettings.Names[style] + ".assproj"));
                int x = ((style - 1) % 2) * 640, y = 95 + ((style - 1) / 2) * 630;
                g.FillRectangle(Brushes.DimGray, x + 16, y + 47, 608, 570);
                using (Bitmap png = new Bitmap(Path.Combine(folder, w.PolPrefix + "_atlas.png"))) g.DrawImage(png, new Rectangle(x + 35, y + 47, 570, 570));
                g.DrawString(PaintSettings.Names[style], title, Brushes.White, x + 23, y + 3);
            }
            sheet.Save(Path.Combine(directory, "paint-presets-preview.png"), ImageFormat.Png);
        }
        w.Width = w.Height = 4096;
        var largeWatch = System.Diagnostics.Stopwatch.StartNew();
        using (AtlasLayout large = AtlasLayout.Build(engine, w))
        using (Bitmap image = PaintRenderer.Raster(large, w.Paint, 0)) Assert(image.Size == new Size(4096, 4096), "4K weathering preserves exact requested resolution");
        Console.WriteLine("4K paint raster: {0} ms", largeWatch.ElapsedMilliseconds);
    }

    static void PaintReferenceComparison(SignEngine engine, string directory)
    {
        AtlasWorkspace w = new AtlasWorkspace { Width = 1024, Height = 1024, Padding = 32, Paint = PaintSettings.Preset(1), PolPrefix = "Reference_A1" };
        w.Resize(1, 1);
        w.Slots[0] = new Settings { Kind = AssetKind.GroundOther, OtherStyle = "机型文字", Text = "A1", CharacterHeight = 4000, BlackBackground = false, Border = false };
        string folder = Path.Combine(directory, "PSD参考对照导出"); PolExporter.Export(engine, w, folder);
        ProjectStore.Save(w, Path.Combine(directory, "PSD参考对照.assproj"));
        string reference = Path.Combine(Directory.GetParent(directory).FullName, "tests", "fixtures", "paint_reference_preview.png");
        using (Bitmap sheet = new Bitmap(1240, 1010, PixelFormat.Format32bppArgb))
        using (Bitmap input = new Bitmap(reference))
        using (Bitmap output = new Bitmap(Path.Combine(folder, "Reference_A1_atlas.png")))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font title = new Font("Microsoft YaHei UI", 20))
        using (Font label = new Font("Microsoft YaHei UI", 12))
        using (Brush ground = new SolidBrush(Color.FromArgb(84, 84, 84)))
        {
            g.Clear(Color.FromArgb(31, 37, 40));
            g.DrawString("你的 PSD 参考", title, Brushes.White, 25, 20);
            g.DrawString("程序直接导出 · 参考轻污渍", title, Brushes.White, 375, 20);
            g.FillRectangle(ground, 16, 80, 320, 854); g.DrawImage(input, new Rectangle(16, 80, 320, 854));
            g.FillRectangle(ground, 366, 80, 854, 854); g.DrawImage(output, new Rectangle(366, 80, 854, 854));
            g.DrawString("暖黄 #FFAB09 · 顺向细刷纹 · 微颗粒 · 低强度污渍 · 默认无掉漆", label, Brushes.White, 25, 952);
            g.DrawString("右侧来自实际 PNG；灰底仅用于比较，不导出。", label, Brushes.LightGray, 25, 979);
            sheet.Save(Path.Combine(directory, "PSD参考与程序对照.png"), ImageFormat.Png);
        }
    }

    public static int Main(string[] args)
    {
        string directory = args[0]; Directory.CreateDirectory(directory);
        try
        {
            using (GlyphLibrary library = new GlyphLibrary())
            {
                SignEngine engine = new SignEngine(library);
                Assert(SignEngine.Normalize(" ａａ１２−ｂ ") == "AA12-B", "Full-width, case and minus normalization");
                Near(engine.TextWidth("A1", "sign", 300, 0), 400, "H.0.9 A1 width = 255 + 71 + 74");
                Near(engine.TextWidth("AV", "sign", 300, 0), 503, "H.0.9 AV pair code 4");
                Near(engine.TextWidth("AB", "sign", 300, 0), 517, "H.0.9 AB pair code 2");
                Near(engine.TextWidth("41", "sign", 200, 0), 237, "H.0.9 numeric pair 41");
                Near(engine.TextWidth("A B", "sign", 300, 0), 640, "Word spacing replaces pair gap");
                foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
                {
                    Assert(library.Get("sign", c).Path.PointCount > 0, "Sign glyph " + c);
                    Assert(library.Get("ground", c).Path.PointCount > 0, "Ground glyph " + c);
                }
                Settings simple = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
                simple.Panels.Add(new SignPanel { Kind = "位置", Text = "A1", Arrow = "无" });
                using (SignDocument d = engine.Build(simple)) { Near(d.Width, 700, "Standalone location width"); Near(d.Height, 450, "H.0.10 panel height"); }
                Sample(engine, simple, "03_location_A1", directory, 2048);

                Settings stand = new Settings { Kind = AssetKind.GroundStand, Text = "324", Arrow = "↑" };
                using (SignDocument d = engine.Build(stand)) Near(d.Height, 7000, "Ground stand B + E");
                Sample(engine, stand, "02_ground_stand_324", directory, 2048);
                stand.Text = "101L"; stand.Arrow = "→";
                using (SignDocument d = engine.Build(stand)) Near(d.Height, 5000, "Ground stand right B");
                Sample(engine, stand, "02_ground_stand_right", directory, 1024);
                stand.Arrow = "←"; stand.CharacterHeight = 2000;
                using (SignDocument d = engine.Build(stand)) Near(d.Height, 2500, "Ground stand half-size");
                Sample(engine, stand, "02_ground_stand_left_half", directory, 1024);

                Settings ground = new Settings();
                Sample(engine, ground, "01_ground_direction_A1", directory, 2048);
                ground.ArrowAbove = true; ground.Arrow = "←"; ground.Text = "B12";
                Sample(engine, ground, "01_ground_direction_above", directory, 1024);
                ground.GroundStyle = "位置"; ground.Text = "A12";
                Sample(engine, ground, "01_ground_location_A12", directory, 1024);
                ground.GroundStyle = "强制指令"; ground.Text = "09L-27R";
                Sample(engine, ground, "01_ground_mandatory", directory, 1024);
                ground.CharacterHeight = 2000;
                using (SignDocument d = engine.Build(ground)) Near(d.Height, 3200, "2 m mandatory retains 0.5 m red margins plus 0.1 m white border");

                Settings taxi = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
                taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "B", Arrow = "←" });
                taxi.Panels.Add(new SignPanel { Kind = "位置", Text = "A", Arrow = "无" });
                taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "C2", Arrow = "↗" });
                taxi.Panels.Add(new SignPanel { Kind = "方向", Text = "D", Arrow = "→" });
                Sample(engine, taxi, "03_combined_taxi_sign", directory, 2048);
                foreach (string arrow in new[] { "←", "↖", "↑", "↗", "→", "↘", "↓", "↙" })
                {
                    Settings arrows = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 400 };
                    arrows.Panels.Add(new SignPanel { Kind = "方向", Text = "A", Arrow = arrow });
                    using (SignDocument d = engine.Build(arrows)) Assert(d.Shapes.All(p => p.Path.GetBounds().Top >= -0.5 && p.Path.GetBounds().Bottom <= d.Height + 0.5), "Arrow fits " + arrow);
                }
                taxi.Panels.Add(new SignPanel { Kind = "强制指令", Text = "09-27" });
                Throws(() => { using (engine.Build(taxi)) { } }, "Disallow mandatory + direction combination");

                Settings board = new Settings { Kind = AssetKind.StandSign, Text = "23", CharacterHeight = 600 };
                using (SignDocument d = engine.Build(board)) Near(d.Height, 1000, "Stand board 3/5 number height");
                Sample(engine, board, "04_stand_sign_23", directory, 2048);
                board.Coordinates = true; board.Latitude = "N 52 30.2"; board.Longitude = "E 114 25.1";
                using (SignDocument d = engine.Build(board)) Near(d.Height, 1500, "Coordinate board 2/5 number height");
                Sample(engine, board, "04_stand_sign_coordinates", directory, 2048);
                board.Longitude = "";
                Throws(() => { using (engine.Build(board)) { } }, "Missing longitude blocks export");
                board.Coordinates = false; board.Text = "";
                Throws(() => { using (engine.Build(board)) { } }, "Empty text blocks export");
                board.Text = "机位";
                Throws(() => { using (engine.Build(board)) { } }, "Unsupported characters are not silently substituted");
                board.Text = "12345678901234567890";
                Sample(engine, board, "04_long_number_no_clipping", directory, 4096);
                Assert(Exporter.SafeName("A1 / B2:*?") == "A1_B2", "Safe export file names");
                VerifyV2(engine, library, directory);
                VerifyAspectRatio(engine);
                VerifyMarginsAndWholeDividers(engine);
                VerifySliderPrecision();
                VerifyAtlasBookAndBatch(engine, directory);
                VerifyStandOcr(engine, directory);
                VerifyAtlas(engine, directory);
                VerifyMergedAtlas(engine, directory);
                VerifyProjectAndPol(engine, directory);
                VerifyArtworkBounds(engine, directory);
                VerifyBleed(engine);
                VerifyPaint(engine, directory);
                VerifyColorsAndBoundaries(engine, directory);
                VerifyTypeColors(engine, directory);
                VerifyBaseColors(engine, directory);
                VerifyViewportAndMode(engine, directory);
                PaintReferenceComparison(engine, directory);
            }
            string result = "PASS: " + checks + " assertions. Existing artwork/layout/project/total POL/bleed checks; v3.7 PSD brush/particle material, reference warm yellow toggle, no paint loss in reference preset, deterministic settings, adjustable texture and direction, transparent wear, no added alpha, unchanged UV, persisted new settings, v3.6 project defaults, direct PNG + POL and locked-file rollback.\nGUI and simulator acceptance tracked separately.\n";
            File.WriteAllText(Path.Combine(directory, "test-results.txt"), result); Console.WriteLine(result); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
