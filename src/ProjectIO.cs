using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class RegionData
    {
        public int Index;
        public float X, Y, Width, Height;
        public RegionData[] Children;
        public static RegionData From(AtlasRegion r)
        {
            return r == null ? null : new RegionData { Index = r.Index, X = r.Bounds.X, Y = r.Bounds.Y, Width = r.Bounds.Width, Height = r.Bounds.Height,
                Children = r.Children == null ? null : r.Children.Select(From).ToArray() };
        }
        public AtlasRegion ToRegion()
        {
            return new AtlasRegion { Index = Index, Bounds = new RectangleF(X, Y, Width, Height), Children = Children == null ? null : Children.Select(c => c.ToRegion()).ToArray() };
        }
    }
    public sealed class AirportProject
    {
        public int Version = 1, Rows, Columns, Width, Height, Gap, Padding, TargetTextPixels;
        public Settings[] Slots;
        public RegionData[] Regions;
        public string[] PolNames;
        public string PolPrefix, PolLayer;
        public int PolOffset;
        public int PolBleed = 2;
        public bool XPlane = true; // Projects predating the switch already used the POL workflow.
        public PolBoundaryMode PolBoundary = PolBoundaryMode.Artwork;
        public ColorSettings Colors = new ColorSettings();
        public TypeColorSettings TypeColors = new TypeColorSettings();
        public BaseColorSettings BaseColors = new BaseColorSettings();
        public PaintSettings Paint = new PaintSettings();
    }
    public static class ProjectStore
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AirportProject));
        public static string Encode(AtlasWorkspace w)
        {
            AirportProject data = new AirportProject { Rows = w.Rows, Columns = w.Columns, Width = w.Width, Height = w.Height, Gap = w.Gap,
                Padding = w.Padding, TargetTextPixels = w.TargetTextPixels, Slots = w.Slots, Regions = w.Regions.Select(RegionData.From).ToArray(),
                PolNames = w.PolNames, PolPrefix = w.PolPrefix, PolLayer = w.PolLayer, PolOffset = w.PolOffset, PolBleed = w.PolBleed, XPlane = w.XPlane, PolBoundary = w.PolBoundary, Colors = w.Colors, TypeColors = w.TypeColors, BaseColors = w.BaseColors, Paint = w.Paint };
            StringBuilder text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true })) Serializer.Serialize(writer, data);
            return text.ToString();
        }
        static void Require(bool condition) { if (!condition) throw new ArgumentException("工程格式损坏或参数超出支持范围，当前工程未改变。"); }
        static bool Range(float n, float min, float max) { return !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max; }
        static HashSet<int> CheckRegions(RegionData[] regions, RectangleF parent, int count, int depth)
        {
            Require(depth <= 64 && regions != null && regions.Length > 0 && regions.Length <= 64 && regions.All(r => r != null));
            Require(regions.Select(r => r.Index).Distinct().Count() == regions.Length);
            HashSet<int> leaves = new HashSet<int>(); double area = 0;
            for (int i = 0; i < regions.Length; i++)
            {
                RegionData r = regions[i];
                Require(r.Index >= 0 && r.Index < count && Range(r.X, -0.00001f, 1) && Range(r.Y, -0.00001f, 1) && Range(r.Width, 0.000001f, 1.00001f) && Range(r.Height, 0.000001f, 1.00001f));
                RectangleF b = new RectangleF(r.X, r.Y, r.Width, r.Height);
                Require(b.Left >= parent.Left - 0.00001f && b.Top >= parent.Top - 0.00001f && b.Right <= parent.Right + 0.00001f && b.Bottom <= parent.Bottom + 0.00001f);
                area += r.Width * r.Height;
                for (int j = 0; j < i; j++)
                {
                    RegionData other = regions[j]; RectangleF overlap = RectangleF.Intersect(b, new RectangleF(other.X, other.Y, other.Width, other.Height));
                    Require(overlap.Width * overlap.Height < 0.00001f);
                }
                HashSet<int> sub = r.Children == null ? new HashSet<int> { r.Index } : CheckRegions(r.Children, b, count, depth + 1);
                Require(sub.Contains(r.Index) && (r.Children == null || r.Children.Length >= 2));
                foreach (int index in sub) Require(leaves.Add(index));
            }
            Require(Math.Abs(area - parent.Width * parent.Height) < 0.0001);
            return leaves;
        }
        public static AtlasWorkspace Decode(string text)
        {
            AirportProject p;
            using (XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4000000 }))
                p = (AirportProject)Serializer.Deserialize(reader);
            if (p.Version != 1) throw new ArgumentException("此工程版本暂不支持，请使用兼容的程序版本。");
            Require(p.Rows >= 1 && p.Rows <= 8 && p.Columns >= 1 && p.Columns <= 8 && p.Width >= 128 && p.Width <= 8192 && p.Height >= 128 && p.Height <= 8192);
            Require(p.Gap >= 0 && p.Gap <= 512 && p.Padding >= 0 && p.Padding <= 256 && p.TargetTextPixels >= 16 && p.TargetTextPixels <= 1024);
            Require(p.Slots != null && p.Slots.Length == 64 && p.Regions != null && p.Regions.Length == 64 && p.PolNames != null && p.PolNames.Length == 64);
            int count = p.Rows * p.Columns;
            Require(p.Slots.Skip(count).All(s => s == null) && p.Regions.Skip(count).All(r => r == null));
            for (int i = 0; i < count; i++) Require(p.Regions[i] == null || p.Regions[i].Index == i);
            Require(CheckRegions(p.Regions.Where(r => r != null).ToArray(), new RectangleF(0, 0, 1, 1), count, 0).Count == count);
            Require(p.PolPrefix != null && PolExporter.Layers.Contains(p.PolLayer) && p.PolOffset >= -5 && p.PolOffset <= 5);
            Require(p.PolBleed >= 0 && p.PolBleed <= 32);
            Require(Enum.IsDefined(typeof(PolBoundaryMode), p.PolBoundary));
            Require(p.Colors != null); p.Colors.Validate();
            Require(p.TypeColors != null); p.TypeColors.Validate();
            Require(p.BaseColors != null); p.BaseColors.Validate();
            Require(p.Paint != null); p.Paint.Validate();
            foreach (Settings s in p.Slots.Where(s => s != null))
            {
                Require(s.Colors != null); s.Colors.Validate();
                Require(Enum.IsDefined(typeof(AssetKind), s.Kind) && Range(s.Width, 0, 200000) && Range(s.Height, 0, 200000) && Range(s.GroundGap, 0.02f, 0.5f));
                float[] heights = s.Kind == AssetKind.TaxiSign ? new float[] { 200, 300, 400 } : s.Kind == AssetKind.StandSign ? new float[] { 400, 600, 800, 1000 } : new float[] { 2000, 4000 };
                Require(s.Kind == AssetKind.GroundOther && s.OtherStyle == "机型文字" ? Range(s.CharacterHeight, 100, 8000) : heights.Contains(s.CharacterHeight));
                string[] styles = { "方向", "位置", "强制指令" }, arrows = { "无", "←", "↖", "↑", "↗", "→", "↘", "↓", "↙" };
                Require(styles.Contains(s.GroundStyle) && arrows.Contains(s.Arrow) && new[] { "MAX SPAN", "HP", "NO ENTRY", "机型文字" }.Contains(s.OtherStyle));
                Require(s.Text != null && s.Latitude != null && s.Longitude != null && s.Panels.Count <= 6 &&
                    s.Panels.All(a => a != null && a.Text != null && styles.Contains(a.Kind) && arrows.Contains(a.Arrow) && Range(a.Width, -float.MaxValue, float.MaxValue)));
            }
            AtlasWorkspace w = new AtlasWorkspace { Width = p.Width, Height = p.Height, Gap = p.Gap, Padding = p.Padding, TargetTextPixels = p.TargetTextPixels,
                PolPrefix = p.PolPrefix, PolLayer = p.PolLayer, PolOffset = p.PolOffset, PolBleed = p.PolBleed, XPlane = p.XPlane, PolBoundary = p.PolBoundary, Colors = p.Colors, TypeColors = p.TypeColors, BaseColors = p.BaseColors, Paint = p.Paint };
            w.Resize(p.Rows, p.Columns);
            for (int i = 0; i < 64; i++) { w.Slots[i] = p.Slots[i]; w.Regions[i] = p.Regions[i] == null ? null : p.Regions[i].ToRegion(); w.PolNames[i] = p.PolNames[i]; }
            w.Selected = Array.FindIndex(w.Regions, r => r != null);
            w.SavedProject = Encode(w); return w;
        }
        public static void AtomicFile(string path, Action<string> write)
        {
            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "." + Guid.NewGuid().ToString("N") + ".tmp");
            try { write(temp); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Save(AtlasWorkspace w, string path)
        {
            string text = Encode(w); AtomicFile(path, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
            w.ProjectPath = path; w.SavedProject = text;
        }
        public static void Apply(AtlasWorkspace target, AtlasWorkspace source)
        {
            target.Rows = source.Rows; target.Columns = source.Columns; target.Width = source.Width; target.Height = source.Height;
            target.Gap = source.Gap; target.Padding = source.Padding; target.TargetTextPixels = source.TargetTextPixels; target.Selected = source.Selected;
            Array.Copy(source.Slots, target.Slots, 64); Array.Copy(source.Regions, target.Regions, 64); Array.Copy(source.PolNames, target.PolNames, 64);
            target.PolPrefix = source.PolPrefix; target.PolLayer = source.PolLayer; target.PolOffset = source.PolOffset; target.PolBleed = source.PolBleed;
            target.PolBoundary = source.PolBoundary; target.Colors = source.Colors.Copy();
            target.XPlane = source.XPlane;
            target.TypeColors = source.TypeColors.Copy();
            target.BaseColors = source.BaseColors.Copy();
            target.Paint = source.Paint.Copy();
            target.ProjectPath = source.ProjectPath; target.SavedProject = source.SavedProject;
        }
    }
    public static class ProjectDialogs
    {
        public static bool Save(IWin32Window owner, AtlasWorkspace w, bool saveAs)
        {
            string path = w.ProjectPath;
            if (saveAs || string.IsNullOrEmpty(path))
                using (SaveFileDialog dialog = new SaveFileDialog { Filter = "机场标志工程|*.assproj", DefaultExt = "assproj", AddExtension = true, FileName = string.IsNullOrEmpty(path) ? "AirportSigns.assproj" : Path.GetFileName(path) })
                { if (dialog.ShowDialog(owner) != DialogResult.OK) return false; path = dialog.FileName; }
            try { ProjectStore.Save(w, path); return true; }
            catch (Exception error) { MessageBox.Show(owner, error.Message, "工程保存失败"); return false; }
        }
        public static bool ConfirmSave(IWin32Window owner, AtlasWorkspace w)
        {
            if (ProjectStore.Encode(w) == (w.SavedProject ?? ProjectStore.Encode(new AtlasWorkspace()))) return true;
            DialogResult choice = MessageBox.Show(owner, "拼图工程有未保存的更改，是否保存？", "保存工程", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return choice == DialogResult.No || choice == DialogResult.Yes && Save(owner, w, false);
        }
    }
}
