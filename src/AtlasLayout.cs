using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AirportSignStudio
{
    public enum AtlasEdge { Left, Right, Top, Bottom }

    public sealed class AtlasRegion
    {
        public int Index;
        public RectangleF Bounds;
        public AtlasRegion[] Children;
        public int CellCount { get { return Children == null ? 1 : Children.Sum(c => c.CellCount); } }

        public void SetBounds(RectangleF bounds)
        {
            if (Children != null)
                foreach (AtlasRegion child in Children)
                {
                    RectangleF b = child.Bounds;
                    child.SetBounds(new RectangleF(bounds.X + (b.X - Bounds.X) / Bounds.Width * bounds.Width,
                        bounds.Y + (b.Y - Bounds.Y) / Bounds.Height * bounds.Height,
                        b.Width / Bounds.Width * bounds.Width, b.Height / Bounds.Height * bounds.Height));
                }
            Bounds = bounds;
        }
    }

    public sealed class AtlasDivider
    {
        public bool Vertical;
        public float Position, Start, End;
        public int[] Before, After;
    }

    public sealed class AtlasWorkspace
    {
        public readonly Settings[] Slots = new Settings[64];
        public readonly AtlasRegion[] Regions = new AtlasRegion[64];
        public readonly string[] PolNames = new string[64];
        public string PolPrefix = "AirportSigns", PolLayer = "markings";
        public bool XPlane;
        public int PolOffset = 1, PolBleed = 2;
        public PolBoundaryMode PolBoundary = PolBoundaryMode.Artwork;
        public ColorSettings Colors = new ColorSettings();
        public TypeColorSettings TypeColors = new TypeColorSettings();
        public BaseColorSettings BaseColors = new BaseColorSettings();
        public PaintSettings Paint = new PaintSettings();
        public string ProjectPath, SavedProject;
        public int Rows = 2, Columns = 3;
        public int Width = 2048, Height = 2048;
        public int Gap = 24, Padding = 16, TargetTextPixels = 128;
        public int Selected;
        public int Count { get { return Rows * Columns; } }
        public int RegionCount { get { return Regions.Take(Count).Count(r => r != null); } }
        public int Filled { get { return Regions.Take(Count).Count(r => r != null && Slots[r.Index] != null); } }

        public AtlasWorkspace() { ResetGridLayout(); }

        public void ResetGridLayout()
        {
            Array.Clear(Regions, 0, Regions.Length);
            for (int i = 0; i < Count; i++)
                Regions[i] = new AtlasRegion { Index = i, Bounds = RectangleF.FromLTRB((i % Columns) / (float)Columns,
                    (i / Columns) / (float)Rows, (i % Columns + 1) / (float)Columns, (i / Columns + 1) / (float)Rows) };
        }

        public void Resize(int rows, int columns)
        {
            if (rows < 1 || rows > 8 || columns < 1 || columns > 8) throw new ArgumentException("行列数分别支持 1–8。");
            if (Slots.Skip(rows * columns).Any(s => s != null))
                throw new ArgumentException("缩小后会放不下已有标牌。请先清空超出新数量的槽位；现有排列已保留。");
            Rows = rows; Columns = columns; Selected = Math.Min(Selected, Count - 1); ResetGridLayout();
        }

        public void PutCurrent(Settings settings)
        {
            int index = Enumerable.Range(0, Count).Where(i => Regions[i] != null && Slots[i] == null).DefaultIfEmpty(-1).First();
            if (index < 0) throw new ArgumentException("当前槽位已满。请打开拼图编辑器增加行列，或选中槽位更改内容。");
            Slots[index] = settings.Copy(); Selected = index;
        }

        public void Merge(IEnumerable<int> selection)
        {
            int[] indices = selection.Distinct().ToArray();
            if (indices.Length < 2 || indices.Any(i => i < 0 || i >= Count || Regions[i] == null) || !indices.Contains(Selected))
                throw new ArgumentException("请多选至少两个相邻区域，再合并。");
            AtlasRegion[] children = indices.Select(i => Regions[i]).ToArray();
            RectangleF bounds = children.Select(r => r.Bounds).Aggregate(RectangleF.Union);
            float area = children.Sum(r => r.Bounds.Width * r.Bounds.Height);
            if (Math.Abs(area - bounds.Width * bounds.Height) > 0.00001f)
                throw new ArgumentException("所选区域必须完整拼成一个矩形，不能留洞或组成 L 形。");
            foreach (int i in indices) Regions[i] = null;
            Regions[Selected] = new AtlasRegion { Index = Selected, Bounds = bounds, Children = children };
        }

        public void Split(int index)
        {
            AtlasRegion region = Regions[index];
            if (region == null || region.Children == null) return;
            float spanW = Width - 2f * Padding + Gap, spanH = Height - 2f * Padding + Gap;
            if (region.Children.Any(c => c.Bounds.Width * spanW - Gap <= 2 * Padding || c.Bounds.Height * spanH - Gap <= 2 * Padding))
                throw new ArgumentException("当前区域太小，拆分后无法容纳留白。请先拉大合并区、提高分辨率或减小留白。");
            Regions[index] = null;
            foreach (AtlasRegion child in region.Children) Regions[child.Index] = child;
        }

        // A divider joins only overlapping edge segments, not a whole row/column.
        // A tall neighbour may require several shorter edges to move together.
        public AtlasDivider Divider(int index, AtlasEdge edge)
        {
            AtlasRegion region = Regions[index];
            if (region == null) return null;
            bool vertical = edge == AtlasEdge.Left || edge == AtlasEdge.Right;
            float position = edge == AtlasEdge.Left ? region.Bounds.Left : edge == AtlasEdge.Right ? region.Bounds.Right :
                edge == AtlasEdge.Top ? region.Bounds.Top : region.Bounds.Bottom;
            if (position < 0.00001f || position > 0.99999f) return null;
            float start = vertical ? region.Bounds.Top : region.Bounds.Left, end = vertical ? region.Bounds.Bottom : region.Bounds.Right;
            List<int> before = new List<int>(), after = new List<int>();
            bool changed;
            do
            {
                changed = false;
                foreach (AtlasRegion other in Regions.Where(r => r != null))
                {
                    RectangleF b = other.Bounds;
                    float a = vertical ? b.Top : b.Left, z = vertical ? b.Bottom : b.Right;
                    if (Math.Min(z, end) - Math.Max(a, start) <= 0.00001f) continue;
                    bool isBefore = Math.Abs((vertical ? b.Right : b.Bottom) - position) < 0.00001f;
                    bool isAfter = Math.Abs((vertical ? b.Left : b.Top) - position) < 0.00001f;
                    if (!isBefore && !isAfter) continue;
                    if (isBefore && !before.Contains(other.Index)) before.Add(other.Index);
                    if (isAfter && !after.Contains(other.Index)) after.Add(other.Index);
                    if (a < start - 0.00001f || z > end + 0.00001f) { start = Math.Min(start, a); end = Math.Max(end, z); changed = true; }
                }
            } while (changed);
            return before.Count == 0 || after.Count == 0 ? null : new AtlasDivider
                { Vertical = vertical, Position = position, Start = start, End = end, Before = before.ToArray(), After = after.ToArray() };
        }

        public bool MoveDivider(AtlasDivider divider, float position)
        {
            float pixels = (divider.Vertical ? Width : Height) - 2f * Padding + Gap;
            float minimum = (Gap + 2f * Padding + 8) / pixels;
            float lower = divider.Before.Max(i => (divider.Vertical ? Regions[i].Bounds.Left : Regions[i].Bounds.Top) + minimum);
            float upper = divider.After.Min(i => (divider.Vertical ? Regions[i].Bounds.Right : Regions[i].Bounds.Bottom) - minimum);
            if (lower > upper) return false;
            position = Math.Max(lower, Math.Min(upper, position));
            if (Math.Abs(position - divider.Position) < 0.000001f) return false;
            foreach (int i in divider.Before.Concat(divider.After))
            {
                RectangleF b = Regions[i].Bounds;
                bool before = divider.Before.Contains(i);
                Regions[i].SetBounds(divider.Vertical ? RectangleF.FromLTRB(before ? b.Left : position, b.Top, before ? position : b.Right, b.Bottom) :
                    RectangleF.FromLTRB(b.Left, before ? b.Top : position, b.Right, before ? position : b.Bottom));
            }
            divider.Position = position; return true;
        }
    }

    public sealed class AtlasSlotInfo
    {
        public int Index;
        public bool Visible;
        public RectangleF Cell, Image, Artwork;
        public float PhysicalWidth, PhysicalHeight, CharacterHeight, SmallestTextHeight, Scale;
        public string Caption = "", Error = "";
        public bool Occupied;
        public bool CustomColors;
        public RectangleF[] CustomColorAreas = new RectangleF[0];
        public float CharacterPixels { get { return CharacterHeight * Scale; } }
        public float SmallestTextPixels { get { return SmallestTextHeight * Scale; } }
    }

    public sealed class AtlasLayout : IDisposable
    {
        public readonly SignDocument Document = new SignDocument();
        public readonly List<AtlasSlotInfo> Slots = new List<AtlasSlotInfo>();
        public bool UseColorMask;
        public bool CanExport { get { return Slots.Any(s => s.Occupied) && Slots.All(s => s.Error.Length == 0); } }

        public static RectangleF Cell(AtlasWorkspace workspace, int index, int width, int height)
        {
            AtlasRegion region = workspace.Regions[index];
            if (region == null) return RectangleF.Empty;
            RectangleF b = region.Bounds;
            float spanW = width - 2f * workspace.Padding + workspace.Gap, spanH = height - 2f * workspace.Padding + workspace.Gap;
            float w = b.Width * spanW - workspace.Gap, h = b.Height * spanH - workspace.Gap;
            if (w <= 2 * workspace.Padding || h <= 2 * workspace.Padding)
                throw new ArgumentException("画布太小或间距太大，槽位没有可用面积。请提高分辨率或减小行列、间距、留白。");
            return new RectangleF(workspace.Padding + b.X * spanW, workspace.Padding + b.Y * spanH, w, h);
        }

        public static AtlasLayout Build(SignEngine engine, AtlasWorkspace workspace)
        {
            if (workspace.Width < 128 || workspace.Width > 8192 || workspace.Height < 128 || workspace.Height > 8192)
                throw new ArgumentException("画布宽高支持 128–8192 px。");
            if (workspace.Rows < 1 || workspace.Rows > 8 || workspace.Columns < 1 || workspace.Columns > 8 || workspace.Gap < 0 || workspace.Padding < 0)
                throw new ArgumentException("行列或间距设置无效。");
            AtlasLayout result = new AtlasLayout();
            result.UseColorMask = workspace.BaseColors.Active;
            try
            {
                result.Document.Width = workspace.Width; result.Document.Height = workspace.Height;
                result.Document.Caption = "Mixed_atlas_" + workspace.Filled;
                result.Document.Reference = "MH 5001-2021 reference layouts";
                result.Document.Note = "Pixel canvas. Each slot independently preserves its sign aspect ratio; grid and selection overlays are not exported.";
                for (int i = 0; i < workspace.Count; i++)
                {
                    AtlasSlotInfo info = new AtlasSlotInfo { Index = i, Visible = workspace.Regions[i] != null };
                    result.Slots.Add(info);
                    if (!info.Visible) continue;
                    info.Cell = Cell(workspace, i, workspace.Width, workspace.Height); info.Occupied = workspace.Slots[i] != null;
                    Settings settings = workspace.Slots[i];
                    if (settings == null) continue;
                    try
                    {
                        using (SignDocument part = engine.Build(settings, workspace.Colors, workspace.TypeColors, workspace.BaseColors))
                        {
                            info.CustomColors = part.Shapes.All(s => s.CustomColors);
                            info.PhysicalWidth = part.Width; info.PhysicalHeight = part.Height;
                            info.CharacterHeight = settings.CharacterHeight;
                            info.SmallestTextHeight = settings.CharacterHeight * (settings.Kind == AssetKind.StandSign && settings.Coordinates ? 0.25f :
                                settings.Kind == AssetKind.GroundOther && settings.OtherStyle == "MAX SPAN" ? 0.7f : 1);
                            info.Caption = part.Caption;
                            info.Scale = FitScale(info, info.Cell, workspace.Padding);
                            float w = part.Width * info.Scale, h = part.Height * info.Scale;
                            info.Image = new RectangleF(info.Cell.X + (info.Cell.Width - w) / 2, info.Cell.Y + (info.Cell.Height - h) / 2, w, h);
                            if (!info.CustomColors && !result.UseColorMask)
                                info.CustomColorAreas = part.Shapes.Where(s => s.CustomColors).GroupBy(s => s.ColorSection)
                                    .Select(g => g.Select(s => s.Path.GetBounds()).Aggregate(RectangleF.Union))
                                    .Select(b => new RectangleF(info.Image.X + b.X * info.Scale, info.Image.Y + b.Y * info.Scale, b.Width * info.Scale, b.Height * info.Scale)).ToArray();
                            using (Matrix matrix = new Matrix(info.Scale, 0, 0, info.Scale, info.Image.X, info.Image.Y))
                                foreach (Shape shape in part.Shapes)
                                {
                                    GraphicsPath path = (GraphicsPath)shape.Path.Clone(); path.Transform(matrix); result.Document.Add(path, shape.Fill, shape.Role, shape.CustomColors);
                                    if (shape.Fill.A > 0 && path.PointCount > 0)
                                    {
                                        RectangleF bounds = path.GetBounds();
                                        info.Artwork = info.Artwork.IsEmpty ? bounds : RectangleF.Union(info.Artwork, bounds);
                                    }
                                }
                        }
                    }
                    catch (ArgumentException error) { info.Error = error.Message; }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        static float FitScale(AtlasSlotInfo slot, RectangleF cell, int padding)
        {
            return Math.Min((cell.Width - 2 * padding) / slot.PhysicalWidth, (cell.Height - 2 * padding) / slot.PhysicalHeight);
        }

        public int HitTest(PointF point)
        {
            for (int i = 0; i < Slots.Count; i++) if (Slots[i].Cell.Contains(point)) return i;
            return -1;
        }

        public Size Recommend(AtlasWorkspace workspace)
        {
            if (!CanExport) return Size.Empty;
            float aspect = (float)workspace.Width / workspace.Height;
            foreach (int edge in new[] { 512, 1024, 2048, 4096, 8192 })
            {
                int w = aspect >= 1 ? edge : (int)Math.Round(edge * aspect);
                int h = aspect >= 1 ? (int)Math.Round(edge / aspect) : edge;
                if (w < 128 || h < 128) continue;
                try
                {
                    // Validate every visible region, including narrow empty ones.
                    foreach (AtlasSlotInfo slot in Slots.Where(s => s.Visible)) Cell(workspace, slot.Index, w, h);
                    if (Slots.Where(s => s.Occupied).All(s => s.SmallestTextHeight * FitScale(s, Cell(workspace, s.Index, w, h), workspace.Padding) >= workspace.TargetTextPixels)) return new Size(w, h);
                }
                catch (ArgumentException) { }
            }
            return Size.Empty;
        }

        public void Dispose() { Document.Dispose(); }
    }
}
