using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AirportSignStudio
{
    public enum AssetKind { GroundTaxi, GroundStand, TaxiSign, StandSign, GroundOther }

    public sealed class SignPanel
    {
        public string Kind = "方向";
        public string Text = "A1";
        public string Arrow = "↑";
        public float Width;
    }

    public sealed class Settings
    {
        public AssetKind Kind = AssetKind.GroundTaxi;
        public string Text = "A1";
        public string GroundStyle = "方向";
        public string Arrow = "↗";
        public bool ArrowAbove;
        public bool Border = true;
        public float CharacterHeight = 4000; // All geometry is in millimetres.
        public float GroundGap = 0.10f;
        public bool Coordinates;
        public string Latitude = "";
        public string Longitude = "";
        public float Width, Height; // Zero means automatic; changing these never scales the glyphs.
        public string OtherStyle = "MAX SPAN";
        public bool BlackBackground = true;
        public ColorSettings Colors = new ColorSettings();
        public readonly List<SignPanel> Panels = new List<SignPanel>();
        public Settings Copy()
        {
            Settings copy = new Settings { Kind = Kind, Text = Text, GroundStyle = GroundStyle, Arrow = Arrow,
                ArrowAbove = ArrowAbove, Border = Border, CharacterHeight = CharacterHeight, GroundGap = GroundGap,
                Coordinates = Coordinates, Latitude = Latitude, Longitude = Longitude, Width = Width, Height = Height,
                OtherStyle = OtherStyle, BlackBackground = BlackBackground, Colors = Colors.Copy() };
            foreach (SignPanel p in Panels) copy.Panels.Add(new SignPanel { Kind = p.Kind, Text = p.Text, Arrow = p.Arrow, Width = p.Width });
            return copy;
        }
    }

    public sealed class Glyph : IDisposable
    {
        public GraphicsPath Path;
        public float Width;
        public float Height;
        public void Dispose() { Path.Dispose(); }
    }

    public sealed class GlyphLibrary : IDisposable
    {
        readonly Dictionary<string, Glyph> glyphs = new Dictionary<string, Glyph>();
        public GlyphLibrary()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("glyphs.xml"))
            {
                foreach (XElement element in XDocument.Load(stream).Root.Elements("glyph"))
                {
                    Glyph glyph = new Glyph();
                    glyph.Width = float.Parse((string)element.Attribute("width"), CultureInfo.InvariantCulture);
                    glyph.Height = float.Parse((string)element.Attribute("height"), CultureInfo.InvariantCulture);
                    glyph.Path = ReadPath((string)element.Attribute("d"));
                    RectangleF bounds = glyph.Path.GetBounds();
                    using (Matrix fit = new Matrix(glyph.Width / bounds.Width, 0, 0, glyph.Height / bounds.Height,
                        -bounds.Left * glyph.Width / bounds.Width, -bounds.Top * glyph.Height / bounds.Height)) glyph.Path.Transform(fit);
                    glyphs.Add((string)element.Attribute("family") + ":" + (string)element.Attribute("char"), glyph);
                }
            }
        }

        static GraphicsPath ReadPath(string data)
        {
            string[] t = data.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            PointF last = new PointF();
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            Func<float> number = delegate { return float.Parse(t[i++], CultureInfo.InvariantCulture); };
            while (i < t.Length)
            {
                string command = t[i++];
                if (command == "M") { path.StartFigure(); last = new PointF(number(), number()); }
                else if (command == "L") { PointF next = new PointF(number(), number()); path.AddLine(last, next); last = next; }
                else if (command == "C")
                {
                    PointF c1 = new PointF(number(), number()), c2 = new PointF(number(), number()), next = new PointF(number(), number());
                    path.AddBezier(last, c1, c2, next); last = next;
                }
                else if (command == "Z") path.CloseFigure();
                else throw new InvalidDataException("Unknown outline command: " + command);
            }
            return path;
        }

        public Glyph Get(string family, char c)
        {
            Glyph value;
            if (!glyphs.TryGetValue(family + ":" + c, out value))
                throw new ArgumentException("此类标志暂不支持字符「" + c + "」。请使用英文字母、数字及支持的符号。");
            return value;
        }
        public void Dispose() { foreach (Glyph g in glyphs.Values) g.Dispose(); }
    }

    public sealed class Shape : IDisposable
    {
        public GraphicsPath Path;
        public Color Fill;
        public ColorRole Role;
        public SignColorType ColorType;
        public int ColorSection;
        public bool CustomColors;
        public void Dispose() { Path.Dispose(); }
    }

    public sealed class SignDocument : IDisposable
    {
        public readonly List<Shape> Shapes = new List<Shape>();
        public float Width, Height;
        public string Caption, Reference, Note;
        public SignColorType ColorType;
        public int ColorSection;
        public void Add(GraphicsPath path, Color fill, ColorRole role = ColorRole.Foreground, bool customColors = false) { Shapes.Add(new Shape { Path = path, Fill = fill, Role = role, ColorType = ColorType, ColorSection = ColorSection, CustomColors = customColors }); }
        public void Rect(float x, float y, float w, float h, Color fill, ColorRole role = ColorRole.Background)
        {
            GraphicsPath p = new GraphicsPath(); p.AddRectangle(new RectangleF(x, y, w, h)); Add(p, fill, role);
        }
        public void Polygon(PointF[] points, Color fill, ColorRole role = ColorRole.Background)
        {
            GraphicsPath p = new GraphicsPath(); p.AddPolygon(points); Add(p, fill, role);
        }
        public void Draw(Graphics graphics, bool colorMask = false)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Shape shape in Shapes) using (Brush brush = new SolidBrush(colorMask ? (shape.CustomColors ? Color.White : Color.Black) : shape.Fill)) graphics.FillPath(brush, shape.Path);
        }
        public void Dispose() { foreach (Shape s in Shapes) s.Dispose(); }
    }

    public sealed class SignEngine
    {
        public static readonly Color Yellow = Color.FromArgb(255, 214, 0);
        public static readonly Color Black = Color.FromArgb(14, 16, 17);
        public static readonly Color Red = Color.FromArgb(213, 29, 44);
        public static readonly Color White = Color.FromArgb(255, 255, 255);
        readonly GlyphLibrary library;
        static readonly string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        // Table H.0.9, columns for character heights 200 / 300 / 400 mm.
        static readonly int[][] Widths = {
            new[] {170,137,137,137,124,124,137,137,32,127,140,124,157,137,143,137,143,137,137,124,137,152,178,137,171,137,143,50,137,137,149,137,137,137,137,137},
            new[] {255,205,205,205,186,186,205,205,48,190,210,186,236,205,214,205,214,205,205,186,205,229,267,205,257,205,214,74,205,205,224,205,205,205,205,205},
            new[] {340,274,274,274,248,248,274,274,64,254,280,248,314,274,286,274,286,274,274,248,274,304,356,274,342,274,286,98,274,274,298,274,274,274,274,274}
        };
        static readonly string[] LetterGapCodes = { "224","122","223","122","223","223","122","112","112","112","223","224","112","112","122","122","122","122","122","224","112","224","224","223","224","223" };
        static readonly int[][] Gaps = { new[] {48,38,25,13}, new[] {71,57,38,19}, new[] {96,76,50,26} };

        public SignEngine(GlyphLibrary glyphs) { library = glyphs; }
        public static string Normalize(string value)
        {
            if (value == null) return "";
            StringBuilder b = new StringBuilder();
            foreach (char c in value.Trim())
            {
                if (c >= '\uff01' && c <= '\uff5e') b.Append((char)(c - 0xfee0));
                else if (c == '\u3000') b.Append(' ');
                else if (c == '−' || c == '–' || c == '—') b.Append('-');
                else b.Append(c);
            }
            return b.ToString().ToUpperInvariant();
        }

        static int Column(float h) { return h == 200 ? 0 : h == 300 ? 1 : 2; }
        float Width(char c, string family, float h)
        {
            Glyph g = library.Get(family, c);
            int index = (Letters + "0123456789").IndexOf(c);
            if (family == "sign" && index >= 0)
            {
                int col = Column(h);
                return Widths[col][index] * h / (col == 0 ? 200 : col == 1 ? 300 : 400);
            }
            return g.Width * h / 1000;
        }

        public static float SignGap(char a, char b, float h)
        {
            int code = 1;
            if (Letters.IndexOf(a) >= 0 && Letters.IndexOf(b) >= 0)
            {
                int group = "BDEFHIKLMNPRU".IndexOf(b) >= 0 ? 0 : "CGOQSXZ".IndexOf(b) >= 0 ? 1 : 2;
                code = LetterGapCodes[Letters.IndexOf(a)][group] - '0';
            }
            else if (char.IsDigit(a) && char.IsDigit(b))
            {
                int group = "15".IndexOf(b) >= 0 ? 0 : "236890".IndexOf(b) >= 0 ? 1 : 2;
                string row = a == '1' ? "112" : (a == '4' || a == '7') ? "224" : "122";
                code = row[group] - '0';
            }
            int col = Column(h);
            return Gaps[col][code - 1] * h / (col == 0 ? 200 : col == 1 ? 300 : 400);
        }

        public float TextWidth(string text, string family, float height, float groundGap)
        {
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') { x += height * (family == "sign" ? 0.6f : 0.25f); continue; }
                if (i > 0 && text[i - 1] != ' ') x += family == "sign" ? SignGap(text[i - 1], c, height) : height * groundGap;
                x += Width(c, family, height);
            }
            return x;
        }

        void Text(SignDocument doc, string text, string family, float h, float gap, float x, float y, Color color)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') { x += h * (family == "sign" ? 0.6f : 0.25f); continue; }
                if (i > 0 && text[i - 1] != ' ') x += family == "sign" ? SignGap(text[i - 1], c, h) : h * gap;
                Glyph glyph = library.Get(family, c);
                float w = Width(c, family, h), gh = glyph.Height * h / 1000;
                float gy = c == '-' ? (h - gh) / 2 : (c == '.' || c == 'm') ? h - gh : 0;
                GraphicsPath path = (GraphicsPath)glyph.Path.Clone();
                using (Matrix transform = new Matrix(w / glyph.Width, 0, 0, h / 1000, x, y + gy)) path.Transform(transform);
                doc.Add(path, color); x += w;
            }
        }

        GraphicsPath ArrowPath(string direction, string family, float h)
        {
            if (direction == "无") return null;
            string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            int index = Array.IndexOf(arrows, direction);
            if (index < 0) throw new ArgumentException("请选择有效的箭头方向。");
            GraphicsPath path = (GraphicsPath)library.Get(family, '↑').Path.Clone();
            using (Matrix m = new Matrix()) { m.Scale(h / 1000, h / 1000); path.Transform(m); }
            using (Matrix m = new Matrix()) { m.Rotate(index * 45); path.Transform(m); }
            return path;
        }

        float ArrowWidth(string direction, string family, float h)
        {
            using (GraphicsPath p = ArrowPath(direction, family, h)) return p == null ? 0 : p.GetBounds().Width;
        }
        void Arrow(SignDocument doc, string direction, string family, float h, float x, float centerY, Color color)
        {
            GraphicsPath path = ArrowPath(direction, family, h);
            if (path == null) return;
            RectangleF b = path.GetBounds();
            using (Matrix m = new Matrix()) { m.Translate(x - b.Left, centerY - b.Top - b.Height / 2); path.Transform(m); }
            doc.Add(path, color);
        }

        static PointF[] Inset(PointF[] polygon, float distance)
        {
            PointF[] result = new PointF[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                PointF prev = polygon[(i + polygon.Length - 1) % polygon.Length], curr = polygon[i], next = polygon[(i + 1) % polygon.Length];
                float ax = curr.X - prev.X, ay = curr.Y - prev.Y, bx = next.X - curr.X, by = next.Y - curr.Y;
                float al = (float)Math.Sqrt(ax * ax + ay * ay), bl = (float)Math.Sqrt(bx * bx + by * by);
                float px = curr.X - ay / al * distance, py = curr.Y + ax / al * distance;
                float qx = curr.X - by / bl * distance, qy = curr.Y + bx / bl * distance;
                float t = ((qx - px) * by - (qy - py) * bx) / (ax * by - ay * bx);
                result[i] = new PointF(px + t * ax, py + t * ay);
            }
            return result;
        }
        static string CheckedText(string input)
        {
            string text = Normalize(input);
            if (text.Length == 0) throw new ArgumentException("请输入标志内容。");
            if (text.Length > 40) throw new ArgumentException("每段内容最多支持 40 个字符。");
            if (text.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) throw new ArgumentException("每块牌面使用一行内容；组合牌请添加牌面分段。");
            return text;
        }

        static float Dimension(float requested, float automatic, float minimum, string label)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested < 0 || requested > 200000)
                throw new ArgumentException(label + "应在 0–200000 mm 之间；0 表示自动。");
            if (requested == 0) return automatic;
            if (requested + 0.01f < minimum)
                throw new ArgumentException(label + "太小。保持当前字高至少需要 " + Math.Ceiling(minimum) + " mm；也可设为 0 自动。");
            return requested;
        }

        public SignDocument Build(Settings s, ColorSettings shared = null, TypeColorSettings types = null, BaseColorSettings basis = null)
        {
            s.Colors.Validate(); if (shared != null) shared.Validate();
            if (types != null) types.Validate();
            if (basis != null) basis.Validate();
            if (s.CharacterHeight < 100 || s.CharacterHeight > 8000) throw new ArgumentException("字高超出可用范围。");
            SignDocument doc = new SignDocument { ColorType = TypeColorSettings.For(s) };
            try
            {
                if (s.Kind == AssetKind.TaxiSign) BuildTaxiSign(doc, s);
                else
                {
                    string text = s.Kind == AssetKind.GroundOther && s.OtherStyle == "NO ENTRY" ? "NO ENTRY" : CheckedText(s.Text);
                    doc.Caption = text;
                    if (s.Kind == AssetKind.GroundTaxi) BuildGroundTaxi(doc, s, text);
                    if (s.Kind == AssetKind.GroundStand) BuildGroundStand(doc, s, text);
                    if (s.Kind == AssetKind.StandSign) BuildStandSign(doc, s, text);
                    if (s.Kind == AssetKind.GroundOther) BuildOtherGround(doc, s, text);
                }
                if (s.Width > 0 || s.Height > 0 || s.Panels.Any(p => p.Width > 0)) doc.Note += " 自定义底板尺寸。";
                foreach (Shape shape in doc.Shapes)
                {
                    ColorSettings type = types == null ? null : types.Get(shape.ColorType);
                    Color? replacement = basis == null ? null : basis.Replacement(shape.Fill);
                    shape.Fill = s.Colors.Resolve(shape.Role, shape.Fill, shared, type, replacement);
                    shape.CustomColors = replacement.HasValue || s.Colors.Active || shared != null && shared.Active || type != null && type.Active;
                }
                return doc;
            }
            catch { doc.Dispose(); throw; }
        }

        void BuildGroundTaxi(SignDocument doc, Settings s, string text)
        {
            float h = s.CharacterHeight, pad = h / 8, tw = TextWidth(text, "ground", h, s.GroundGap);
            bool direction = s.GroundStyle == "方向", mandatory = s.GroundStyle == "强制指令";
            if (mandatory) pad = 500 + (s.Border ? 100 : 0); // 6.2.12(8): red background extends at least 0.5 m, including at 2 m cap height.
            if (direction && (s.Arrow == "无" || s.Arrow == "↓" || s.Arrow == "↘" || s.Arrow == "↙"))
                throw new ArgumentException("地面方向标志请选择前进、左转或右转方向（0°、45°、90°）。");
            float ah = h / 2, aw = direction ? ArrowWidth(s.Arrow, "ground", ah) : 0;
            float content = direction && !s.ArrowAbove ? tw + pad + aw : Math.Max(tw, aw);
            float edge = s.Border ? 100 : 0, ch = h + (direction && s.ArrowAbove ? ah + pad : 0);
            float minimumPad = mandatory ? pad : edge + h * 0.025f;
            doc.Width = Dimension(s.Width, content + pad * 2, content + minimumPad * 2, "底板宽度");
            doc.Height = Dimension(s.Height, ch + pad * 2, ch + minimumPad * 2, "底板高度");
            Color bg = mandatory ? Red : direction ? Yellow : Black;
            Color fg = mandatory ? White : direction ? Black : Yellow;
            doc.Rect(0, 0, doc.Width, doc.Height, s.Border ? fg : bg, s.Border ? ColorRole.Border : ColorRole.Background);
            doc.Rect(edge, edge, doc.Width - edge * 2, doc.Height - edge * 2, bg);
            float startX = (doc.Width - content) / 2, startY = (doc.Height - ch) / 2;
            float tx = direction && !s.ArrowAbove ? startX : (doc.Width - tw) / 2, ty = startY;
            if (direction)
            {
                if (s.ArrowAbove)
                {
                    Arrow(doc, s.Arrow, "ground", ah, (doc.Width - aw) / 2, startY + ah / 2, fg);
                    tx = (doc.Width - tw) / 2; ty += ah + pad;
                }
                else if (s.Arrow == "←" || s.Arrow == "↖")
                {
                    Arrow(doc, s.Arrow, "ground", ah, startX, doc.Height / 2, fg); tx += aw + pad;
                }
                else Arrow(doc, s.Arrow, "ground", ah, startX + tw + pad, doc.Height / 2, fg);
            }
            Text(doc, text, "ground", h, s.GroundGap, tx, ty, fg);
            doc.Reference = mandatory ? "6.2.12；图 6.2.12-3" : "6.2.13；图 6.2.13-1";
            doc.Note = "地面字形；黄色/黑色/红色为贴图用 sRGB 配色。";
        }

        void BuildGroundStand(SignDocument doc, Settings s, string text)
        {
            if (!Regex.IsMatch(text, "^[A-Z0-9-]+$")) throw new ArgumentException("机位编号请使用字母、数字或短横线。");
            float h = s.CharacterHeight, pad = h / 8, b = h * 1.25f, e = h / 2;
            float tw = TextWidth(text, "ground", h, s.GroundGap), minimumPad = (s.Border ? 100 : 0) + h * 0.025f;
            float sideHead = s.Arrow == "←" || s.Arrow == "→" ? e : 0, topHead = s.Arrow == "↑" ? e : 0;
            float c = Dimension(s.Width, tw + pad * 2 + sideHead, tw + minimumPad * 2 + sideHead, "底板宽度") - sideHead;
            b = Dimension(s.Height, b + topHead, h + minimumPad * 2 + topHead, "底板高度") - topHead;
            float tx = (c - tw) / 2, ty = (b - h) / 2;
            PointF[] polygon;
            if (s.Arrow == "↑")
            {
                doc.Width = c; doc.Height = b + e; ty += e;
                polygon = new[] { new PointF(c / 2, 0), new PointF(c, e), new PointF(c, b + e), new PointF(0, b + e), new PointF(0, e) };
            }
            else if (s.Arrow == "→")
            {
                doc.Width = c + e; doc.Height = b;
                polygon = new[] { new PointF(0, 0), new PointF(c, 0), new PointF(c + e, b / 2), new PointF(c, b), new PointF(0, b) };
            }
            else if (s.Arrow == "←")
            {
                doc.Width = c + e; doc.Height = b; tx += e;
                polygon = new[] { new PointF(e, 0), new PointF(c + e, 0), new PointF(c + e, b), new PointF(e, b), new PointF(0, b / 2) };
            }
            else
            {
                doc.Width = c; doc.Height = b;
                polygon = new[] { new PointF(0, 0), new PointF(c, 0), new PointF(c, b), new PointF(0, b) };
            }
            doc.Polygon(polygon, s.Border ? Black : Yellow, s.Border ? ColorRole.Border : ColorRole.Background);
            if (s.Border) doc.Polygon(Inset(polygon, 100), Yellow);
            Text(doc, text, "ground", h, s.GroundGap, tx, ty, Black);
            doc.Reference = "6.2.16；图 6.2.16-2";
            doc.Note = "自动尺寸 B = 1.25A；箭头 E = 0.5A；黑边 D = 0.10 m。矩形为制作备选。";
        }

        void BuildTaxiSign(SignDocument doc, Settings s)
        {
            if (s.Panels.Count == 0) throw new ArgumentException("请至少添加一块牌面。");
            if (s.Panels.Count > 6) throw new ArgumentException("最多支持 6 块组合牌面。");
            if (s.Panels.Any(p => p.Kind == "强制指令") && s.Panels.Any(p => p.Kind != "强制指令" && p.Kind != "位置"))
                throw new ArgumentException("8.1.4：强制性指令牌只能与位置牌合设；请分开导出方向牌。");
            float h = s.CharacterHeight;
            if (h != 200 && h != 300 && h != 400) throw new ArgumentException("滑行引导牌的字高请选择 200、300 或 400 mm。");
            doc.Height = Dimension(s.Height, 1.5f * h, h * 1.18f, "牌面高度");
            List<string> captions = new List<string>();
            for (int i = 0; i < s.Panels.Count; i++)
            {
                SignPanel panel = s.Panels[i];
                doc.ColorType = TypeColorSettings.Panel(panel.Kind); doc.ColorSection = i;
                string text = CheckedText(panel.Text);
                bool direction = panel.Kind == "方向", location = panel.Kind == "位置", mandatory = panel.Kind == "强制指令";
                if (!direction && !location && !mandatory) throw new ArgumentException("请选择有效的牌面类型。");
                if (direction && panel.Arrow == "无") throw new ArgumentException("方向标记牌需要选择箭头。");
                float tw = TextWidth(text, "sign", h, 0), aw = direction ? ArrowWidth(panel.Arrow, "sign", h) : 0;
                float pad = h / 2;
                // H.0.11: single runway designator uses H side margins.
                if (mandatory && Regex.IsMatch(text, "^[0-9]{2}[LCR]?$")) pad = h;
                float content = tw + (direction ? aw + h * 0.5f : 0);
                float w = Dimension(panel.Width, content + pad * 2, content + h * 0.18f, "第 " + (i + 1) + " 段宽度");
                float x = doc.Width;
                Color bg = location ? Black : mandatory ? Red : Yellow;
                Color fg = location ? Yellow : mandatory ? White : Black;
                doc.Rect(x, 0, w, doc.Height, bg);
                if (location && s.Panels.Count == 1)
                {
                    float edge = h * 0.08f;
                    doc.Rect(x, 0, w, doc.Height, Yellow, ColorRole.Border);
                    doc.Rect(x + edge, edge, w - edge * 2, doc.Height - edge * 2, Black);
                }
                float tx = x + (w - content) / 2;
                if (direction)
                {
                    bool left = panel.Arrow == "←" || panel.Arrow == "↖" || panel.Arrow == "↙";
                    if (left) { Arrow(doc, panel.Arrow, "sign", h, tx, doc.Height / 2, fg); tx += aw + h * 0.5f; }
                    else Arrow(doc, panel.Arrow, "sign", h, tx + tw + h * 0.5f, doc.Height / 2, fg);
                }
                Text(doc, text, "sign", h, 0, tx, (doc.Height - h) / 2, fg);
                if (i > 0 && direction && s.Panels[i - 1].Kind == "方向") doc.Rect(x - h * 0.056f, 0, h * 0.112f, doc.Height, Black, ColorRole.Border);
                doc.Width += w; captions.Add(text);
            }
            doc.Caption = string.Join(" · ", captions);
            doc.Reference = "8.3 / 8.4；附录 H，表 H.0.9、H.0.10";
            doc.Note = "按表 H.0.9 逐字配宽与配对字距。牌组按列表顺序排列。";
        }

        void BuildStandSign(SignDocument doc, Settings s, string text)
        {
            if (!Regex.IsMatch(text, "^[A-Z0-9-]+$")) throw new ArgumentException("机位编号请使用字母、数字或短横线。");
            float h = s.CharacterHeight, tw = TextWidth(text, "sign", h, 0);
            float autoHeight = h / (s.Coordinates ? 0.4f : 0.6f), blockHeight = s.Coordinates ? 2 * h : h;
            doc.Height = Dimension(s.Height, autoHeight, blockHeight + h * 0.1f, "底板高度");
            float margin = autoHeight / 20, latWidth = 0, lonWidth = 0;
            string lat = "", lon = "";
            if (s.Coordinates)
            {
                if (string.IsNullOrWhiteSpace(s.Latitude) || string.IsNullOrWhiteSpace(s.Longitude))
                    throw new ArgumentException("请填写纬度和经度两行内容。");
                lat = CheckedText(s.Latitude); lon = CheckedText(s.Longitude);
                if (!Regex.IsMatch(lat, "^[NS] [0-9 .]+$") || !Regex.IsMatch(lon, "^[EW] [0-9 .]+$"))
                    throw new ArgumentException("经纬度使用牌面文本格式，例如 N 52 30.2 与 E 114 25.1；数值由你提供。");
                latWidth = TextWidth(lat, "sign", h / 4, 0); lonWidth = TextWidth(lon, "sign", h / 4, 0);
            }
            float maxWidth = Math.Max(tw, Math.Max(latWidth, lonWidth));
            doc.Width = Dimension(s.Width, maxWidth + margin * 2, maxWidth + h * 0.1f, "底板宽度");
            doc.Rect(0, 0, doc.Width, doc.Height, Yellow);
            float y = (doc.Height - blockHeight) / 2;
            Text(doc, text, "sign", h, 0, (doc.Width - tw) / 2, y, Black);
            if (s.Coordinates)
            {
                Text(doc, lat, "sign", h / 4, 0, (doc.Width - latWidth) / 2, y + h * 1.25f, Black);
                Text(doc, lon, "sign", h / 4, 0, (doc.Width - lonWidth) / 2, y + h * 1.75f, Black);
            }
            doc.Reference = "8.5.4—8.5.8；图 8.5.6；附录 H 字形";
            doc.Note = s.CharacterHeight < 600 ? "400 mm 为条件受限方案；8.5.7 要求同时增设地面标志。" : "主编号字高 ≥ 600 mm；经纬度字高为主编号的 1/4。";
        }

        void BuildOtherGround(SignDocument doc, Settings s, string text)
        {
            float h = s.CharacterHeight;
            if (s.OtherStyle == "HP" || s.OtherStyle == "NO ENTRY")
            {
                Settings layout = s.Copy(); layout.Arrow = "无";
                layout.GroundStyle = s.OtherStyle == "HP" ? "位置" : "强制指令";
                if (s.OtherStyle == "HP")
                {
                    text = Regex.Replace(text, "^HP\\s*", "");
                    if (!Regex.IsMatch(text, "^[0-9]+$")) throw new ArgumentException("HP 编号请输入数字，例如 4 或 HP4。");
                    text = "HP" + text;
                }
                else text = "NO ENTRY";
                BuildGroundTaxi(doc, layout, text); doc.Caption = text;
                doc.Reference = s.OtherStyle == "HP" ? "6.2.13(7)；图 6.2.13-4" : "6.2.12；图 6.2.12-2";
                return;
            }
            float pad = h / 8, edge = s.Border ? Math.Min(100, h / 8) : 0;
            if (s.OtherStyle == "MAX SPAN")
            {
                text = Regex.Replace(text, "\\s*M$", "");
                float span;
                if (!Regex.IsMatch(text, "^[0-9]{1,3}(\\.[0-9]{1,2})?$") || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out span) || span <= 0 || span > 100)
                    throw new ArgumentException("最大翼展请输入 0–100 米之间的数值，例如 36 或 65.5。");
                string lower = text + " m";
                float tw = TextWidth("MAX SPAN", "ground", h, s.GroundGap), lw = TextWidth(lower, "ground", h, s.GroundGap);
                float content = Math.Max(tw, lw), block = h * 2 + pad;
                doc.Width = Dimension(s.Width, content + pad * 2, content + edge * 2 + h * 0.05f, "底板宽度");
                doc.Height = Dimension(s.Height, h * 2.375f, block + edge * 2 + h * 0.05f, "底板高度");
                if (s.BlackBackground) doc.Rect(0, 0, doc.Width, doc.Height, Black);
                if (edge > 0) Frame(doc, doc.Width, doc.Height, edge, Yellow);
                float y = (doc.Height - block) / 2;
                Text(doc, "MAX SPAN", "ground", h, s.GroundGap, (doc.Width - tw) / 2, y, Yellow);
                Text(doc, lower, "ground", h, s.GroundGap, (doc.Width - lw) / 2, y + h + pad, Yellow);
                doc.Caption = "MAX SPAN " + lower; doc.Reference = "6.2.13(6)；图 6.2.13-3";
                doc.Note = "自动版式：两行字高 A，外高 2.375A；4 m 字高时外高 9.5 m。";
            }
            else if (s.OtherStyle == "机型文字")
            {
                if (!Regex.IsMatch(text, "^[A-Z0-9 /.-]+$")) throw new ArgumentException("机型文字支持字母、数字、空格、斜线、点和短横线。");
                float tw = TextWidth(text, "ground", h, s.GroundGap);
                doc.Width = Dimension(s.Width, tw + pad * 2, tw, "画幅宽度");
                doc.Height = Dimension(s.Height, h + pad * 2, h, "画幅高度");
                if (s.BlackBackground) doc.Rect(0, 0, doc.Width, doc.Height, Black);
                Text(doc, text, "ground", h, s.GroundGap, (doc.Width - tw) / 2, (doc.Height - h) / 2, Yellow);
                doc.Caption = text; doc.Reference = "6.2.16(7)；图 6.2.16-7 / 8";
                doc.Note = "黄色机型文字，字高宜为 150–300 mm；浅色道面可使用黑底。";
            }
            else throw new ArgumentException("请选择有效的地面标志类型。");
        }

        static void Frame(SignDocument doc, float w, float h, float edge, Color color)
        {
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            path.AddRectangle(new RectangleF(0, 0, w, h));
            path.AddRectangle(new RectangleF(edge, edge, w - 2 * edge, h - 2 * edge));
            doc.Add(path, color, ColorRole.Border);
        }
    }

    public sealed class ExportSize
    {
        public int Width, Height;
        public float Scale, X, Y;
        static int PowerOfTwo(int x) { int n = 1; while (n < x) n *= 2; return n; }
        public ExportSize(SignDocument doc, int longEdge, bool powerOfTwo, int padding)
        {
            if (padding < 0 || longEdge < 64 || longEdge > 4096 || padding * 2 >= longEdge) throw new ArgumentException("导出尺寸或透明留白无效。");
            Scale = (longEdge - padding * 2) / Math.Max(doc.Width, doc.Height);
            int w = (int)Math.Ceiling(doc.Width * Scale - 0.001f) + padding * 2;
            int h = (int)Math.Ceiling(doc.Height * Scale - 0.001f) + padding * 2;
            Width = powerOfTwo ? PowerOfTwo(w) : w; Height = powerOfTwo ? PowerOfTwo(h) : h;
            X = (Width - doc.Width * Scale) / 2; Y = (Height - doc.Height * Scale) / 2;
        }
    }

    public static class Exporter
    {
        public static Bitmap Raster(SignDocument doc, int longEdge, bool powerOfTwo, int padding)
        {
            ExportSize size = new ExportSize(doc, longEdge, powerOfTwo, padding);
            int factor = longEdge <= 2048 ? 2 : 1;
            using (Bitmap large = new Bitmap(size.Width * factor, size.Height * factor, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(large))
                {
                    g.Clear(Color.Transparent);
                    using (Matrix m = new Matrix(size.Scale * factor, 0, 0, size.Scale * factor, size.X * factor, size.Y * factor)) g.Transform = m;
                    doc.Draw(g);
                }
                if (factor == 1) return (Bitmap)large.Clone();
                Bitmap output = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(large, new Rectangle(0, 0, size.Width, size.Height), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel);
                }
                return output;
            }
        }
        static string N(float x) { return x.ToString("0.###", CultureInfo.InvariantCulture); }
        public static Bitmap RasterPixels(SignDocument doc, bool colorMask = false)
        {
            int width = (int)doc.Width, height = (int)doc.Height;
            if (width < 128 || width > 8192 || height < 128 || height > 8192) throw new ArgumentException("画布宽高支持 128–8192 px。");
            Bitmap image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                int factor = Math.Max(width, height) <= 2048 ? 2 : 1;
                using (Bitmap large = new Bitmap(width * factor, height * factor, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(large))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.ScaleTransform(factor, factor);
                        doc.Draw(graphics, colorMask);
                    }
                    using (Graphics graphics = Graphics.FromImage(image))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(large, new Rectangle(0, 0, width, height), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel);
                    }
                }
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        public static string Svg(SignDocument doc, bool pixels = false)
        {
            XNamespace ns = "http://www.w3.org/2000/svg";
            XElement root = new XElement(ns + "svg", new XAttribute("viewBox", "0 0 " + N(doc.Width) + " " + N(doc.Height)),
                new XAttribute("width", N(doc.Width) + (pixels ? "px" : "mm")), new XAttribute("height", N(doc.Height) + (pixels ? "px" : "mm")),
                new XElement(ns + "title", doc.Caption), new XElement(ns + "desc", "MH 5001-2021 reference: " + doc.Reference + (pixels ? ". Geometry in pixels. " : ". Geometry in mm; low-node reference redraw. ") + doc.Note));
            foreach (Shape s in doc.Shapes)
            {
                PointF[] pts = s.Path.PathPoints; byte[] types = s.Path.PathTypes;
                StringBuilder d = new StringBuilder();
                for (int i = 0; i < pts.Length; i++)
                {
                    int type = types[i] & 7;
                    if (type == 0) d.Append("M ").Append(N(pts[i].X)).Append(' ').Append(N(pts[i].Y)).Append(' ');
                    else if (type == 1) d.Append("L ").Append(N(pts[i].X)).Append(' ').Append(N(pts[i].Y)).Append(' ');
                    else if (type == 3)
                    {
                        d.Append("C ");
                        for (int k = 0; k < 3; k++) d.Append(N(pts[i + k].X)).Append(' ').Append(N(pts[i + k].Y)).Append(' ');
                        i += 2;
                    }
                    if ((types[i] & 128) != 0) d.Append("Z ");
                }
                root.Add(new XElement(ns + "path", new XAttribute("d", d.ToString().Trim()), new XAttribute("fill", "#" + s.Fill.R.ToString("X2") + s.Fill.G.ToString("X2") + s.Fill.B.ToString("X2")), new XAttribute("fill-rule", "evenodd")));
            }
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
        }
        public static string SafeName(string value)
        {
            string name = Regex.Replace(SignEngine.Normalize(value), "[^A-Z0-9_-]+", "_").Trim('_');
            if (name.Length > 70) name = name.Substring(0, 70);
            return name.Length == 0 ? "sign" : name;
        }
    }
}
