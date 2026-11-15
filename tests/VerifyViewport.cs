using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AirportSignStudio;

static partial class Verify
{
    static void VerifyViewportAndMode(SignEngine engine, string directory)
    {
        PreviewViewport view = new PreviewViewport();
        view.Resize(new Size(1000, 700), new Size(2048, 2048), 26);
        Assert(view.IsFit, "Preview begins fitted"); Near(view.Scale, 648f / 2048, "Fit scale respects vertical inset");
        Near(view.Bounds.Left, 176, "Fitted image is horizontally centered"); Near(view.Bounds.Top, 26, "Fitted image respects inset");
        PointF anchor = new PointF(600, 400), pixel = view.ImagePoint(anchor);
        view.ZoomAt(0.8f, anchor); PointF after = view.ImagePoint(anchor);
        Near(after.X, pixel.X, "Zoom keeps pointer's image X"); Near(after.Y, pixel.Y, "Zoom keeps pointer's image Y");
        Assert(!view.IsFit, "Explicit zoom leaves fit mode");
        RectangleF beforePan = view.Bounds; view.Move(80, -45);
        Near(view.Bounds.X, beforePan.X + 80, "Pan moves display X"); Near(view.Bounds.Y, beforePan.Y - 45, "Pan moves display Y");
        PointF origin = view.ImagePoint(new PointF(view.Bounds.Left, view.Bounds.Top)); Near(origin.X, 0, "Panned origin hit test X"); Near(origin.Y, 0, "Panned origin hit test Y");
        view.Move(100000, 100000); Near(view.Bounds.Left, 26, "Pan is clamped at image left edge"); Near(view.Bounds.Top, 26, "Pan is clamped at image top edge");
        view.Move(-1000000, -1000000); Near(view.Bounds.Right, 974, "Pan reaches image right edge"); Near(view.Bounds.Bottom, 674, "Pan reaches image bottom edge");
        view.ZoomAt(0.001f, anchor); Assert(view.Scale == PreviewViewport.MinimumZoom, "Manual zoom has exact minimum");
        Near(view.Bounds.Left + view.Bounds.Width / 2, 500, "Small image remains centered horizontally");
        Near(view.Bounds.Top + view.Bounds.Height / 2, 350, "Small image remains centered vertically");
        view.ZoomAt(10000, anchor); Assert(view.Scale == PreviewViewport.MaximumZoom, "Manual zoom has exact maximum");
        RectangleF valid = view.Bounds; view.ZoomAt(float.NaN, anchor); view.ZoomAt(float.PositiveInfinity, anchor);
        Assert(view.Bounds == valid, "Nonfinite zoom cannot damage viewport");
        view.ZoomAt(1, new PointF(500, 350)); view.Resize(new Size(1200, 900), new Size(2048, 2048), 26);
        Near(view.Scale, 1, "Manual 100 percent survives window resize");
        view.Fit(); Near(view.Scale, 848f / 2048, "Fit recomputes after resize");
        view.Resize(new Size(100, 40), new Size(8192, 8192), 26); Assert(view.Bounds.IsEmpty, "Tiny viewport is safe");
        view.Resize(new Size(1000, 700), new Size(4096, 2048), 26); Near(view.Bounds.Width / view.Bounds.Height, 2, "Wide texture aspect preserved");

        AtlasStripGeometry strip = new AtlasStripGeometry(new[] { new Size(2048, 1024), new Size(2048, 1024), new Size(1024, 2048) });
        view.Fit();
        view.Resize(new Size(1000, 700), strip.Extent, 26, strip.FitReference);
        view.Move(0, 100000);
        RectangleF firstPage = strip.Bounds(0, view.Bounds, view.Scale), secondPage = strip.Bounds(1, view.Bounds, view.Scale);
        Near(firstPage.Top, 26, "Strip begins at first page inset");
        Assert(secondPage.Top > firstPage.Bottom && secondPage.Top < 700, "Adjacent pages can be visible together with a gap");
        float midGap = (firstPage.Bottom + secondPage.Top) / 2;
        Assert(strip.Hit(new PointF(500, midGap), view.Bounds, view.Scale) == -1 &&
            strip.Hit(new PointF(500, secondPage.Top + 1), view.Bounds, view.Scale) == 1, "Page hit test preserves boundaries");
        view.Move(0, -100000);
        Near(strip.Bounds(2, view.Bounds, view.Scale).Bottom, 674, "Fitted view scrolls to final page");

        AtlasWorkspace workspace = new AtlasWorkspace(); workspace.Slots[0] = new Settings(); workspace.Slots[1] = new Settings { Text = "B2" };
        Assert(!workspace.XPlane, "New project defaults to PNG only");
        workspace.PolPrefix = "PersistedSigns"; workspace.PolLayer = "airports"; workspace.PolOffset = -2; workspace.PolBleed = 5; workspace.PolBoundary = PolBoundaryMode.Cell;
        workspace.Merge(new[] { 0, 1 });
        string pngProject = ProjectStore.Encode(workspace);
        AtlasHistory history = new AtlasHistory(workspace);
        workspace.XPlane = true; history.Record(new[] { 0 });
        string xplaneProject = ProjectStore.Encode(workspace);
        Assert(history.Undo() && ProjectStore.Encode(workspace) == pngProject, "Mode toggle participates in undo");
        Assert(history.Redo() && ProjectStore.Encode(workspace) == xplaneProject, "Mode toggle participates in redo");
        foreach (bool enabled in new[] { false, true })
        {
            workspace.XPlane = enabled; string encoded = ProjectStore.Encode(workspace);
            AtlasWorkspace loaded = ProjectStore.Decode(encoded);
            Assert(loaded.XPlane == enabled && ProjectStore.Encode(loaded) == encoded, "Both modes survive project round trip");
            AtlasWorkspace target = new AtlasWorkspace(); ProjectStore.Apply(target, loaded);
            Assert(target.XPlane == enabled && target.PolPrefix == "PersistedSigns" && target.PolLayer == "airports" && target.PolOffset == -2 && target.PolBleed == 5 && target.PolBoundary == PolBoundaryMode.Cell, "Mode switching/loading keeps all POL settings");
            Assert(target.Regions[0].Children.Length == 2 && target.Slots[1].Text == "B2", "Mode preserves merged and hidden sign content");
        }
        XDocument legacy = XDocument.Parse(xplaneProject); legacy.Root.Element("XPlane").Remove();
        AtlasWorkspace migrated = ProjectStore.Decode(legacy.ToString());
        Assert(migrated.XPlane && ProjectStore.Encode(migrated) == xplaneProject, "Old project restores previous X-Plane workflow without changing settings");
        workspace.XPlane = false;
        using (AtlasLayout pngLayout = AtlasLayout.Build(engine, workspace))
        {
            string artwork = Exporter.Svg(pngLayout.Document, true), pol = PolExporter.Text(workspace, pngLayout.Slots.Where(s => s.Visible && s.Occupied).ToArray());
            workspace.XPlane = true;
            using (AtlasLayout polLayout = AtlasLayout.Build(engine, workspace))
            {
                Assert(artwork == Exporter.Svg(polLayout.Document, true), "Export mode does not change artwork geometry or colors");
                Assert(pol == PolExporter.Text(workspace, polLayout.Slots.Where(s => s.Visible && s.Occupied).ToArray()), "Re-enabling X-Plane preserves POL UVs and settings");
            }
        }
        string beforeView = ProjectStore.Encode(workspace);
        using (AtlasLayout layout = AtlasLayout.Build(engine, workspace))
        {
            foreach (float zoom in new[] { 0.01f, 0.1f, 0.5f, 1, 2, 8 })
            {
                view.Resize(new Size(1100, 700), new Size(workspace.Width, workspace.Height), 26); view.ZoomAt(zoom, new PointF(550, 350)); view.Move(-150, 100);
                foreach (AtlasSlotInfo slot in layout.Slots.Where(s => s.Visible))
                {
                    PointF image = new PointF(slot.Cell.Left + slot.Cell.Width / 2, slot.Cell.Top + slot.Cell.Height / 2);
                    PointF screen = new PointF(view.Bounds.Left + image.X * view.Scale, view.Bounds.Top + image.Y * view.Scale);
                    Assert(layout.HitTest(view.ImagePoint(screen)) == slot.Index, "Zoom and pan preserve merged-cell hit testing at " + zoom);
                }
            }
        }
        Assert(ProjectStore.Encode(workspace) == beforeView, "Zoom/pan never enter project serialization");
        File.WriteAllText(Path.Combine(directory, "viewport-mode-results.txt"), "PASS: viewport fit/zoom/anchor/pan/clamps/resize/hit tests; PNG/X-Plane defaults, persistence, undo, legacy compatibility, unchanged artwork and POL. Pure geometry/data tests; no windows created.\n");
        Console.WriteLine("PASS viewport and X-Plane mode: geometry, persistence, old projects, undo and export stability; no UI automation.");
    }
}
