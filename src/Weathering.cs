using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class PaintSettings
    {
        public int Style, Dirt, Wear, Fade;
        public int Size = 100, Seed = 1, Angle = 90, Texture = 100;
        public bool ReferenceYellow = true;
        public bool Active { get { return Style > 0 && (Texture > 0 || Dirt > 0 || Wear > 0 || Fade > 0); } }
        public PaintSettings Copy() { return (PaintSettings)MemberwiseClone(); }
        public static readonly string[] Names = { "干净（关闭）", "参考轻污渍", "积尘污斑", "轻微掉漆", "短促擦磨" };
        public static PaintSettings Preset(int style)
        {
            int[] dirt = { 0, 35, 70, 45, 55 }, wear = { 0, 0, 4, 22, 12 }, fade = { 0, 0, 3, 5, 2 };
            return new PaintSettings { Style = style, Dirt = dirt[style], Wear = wear[style], Fade = fade[style] };
        }
        public void Validate()
        {
            if (Texture < 0 || Texture > 100 || Style < 0 || Style >= Names.Length || Dirt < 0 || Dirt > 100 || Wear < 0 || Wear > 100 || Fade < 0 || Fade > 100 || Size < 25 || Size > 400 || Seed < 0 || Seed > 999999 || Angle < 0 || Angle > 180)
                throw new ArgumentException("油漆参数超出范围：强度 0–100，纹理大小 25–400%，种子 0–999999，角度 0–180°。");
        }
    }

    public static class PaintRenderer
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint n = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
                n = (n ^ (n >> 13)) * 1274126177u; n ^= n >> 16;
                return (n & 0xffffff) / 16777215f;
            }
        }
        // Direct PSD mask and brush samples, with mip levels for stable reduced previews.
        sealed class MaterialMap
        {
            readonly System.Collections.Generic.List<float[]> levels = new System.Collections.Generic.List<float[]>();
            readonly System.Collections.Generic.List<Size> sizes = new System.Collections.Generic.List<Size>();
            public MaterialMap(BinaryReader reader)
            {
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                byte[] bytes = reader.ReadBytes(width * height); float[] values = bytes.Select(b => b / 255f).ToArray();
                levels.Add(values); sizes.Add(new Size(width, height));
                while (width > 1 && height > 1)
                {
                    int nextWidth = Math.Max(1, width / 2), nextHeight = Math.Max(1, height / 2);
                    float[] next = new float[nextWidth * nextHeight];
                    for (int y = 0; y < nextHeight; y++) for (int x = 0; x < nextWidth; x++)
                    { int at = y * 2 * width + x * 2; next[y * nextWidth + x] = (values[at] + values[at + 1] + values[at + width] + values[at + width + 1]) * 0.25f; }
                    levels.Add(next); sizes.Add(new Size(nextWidth, nextHeight)); values = next; width = nextWidth; height = nextHeight;
                }
            }
            public int Level(float pixelsPerTile) { return Math.Max(0, Math.Min(levels.Count - 1, (int)Math.Floor(Math.Log(sizes[0].Width / Math.Max(1, pixelsPerTile), 2)))); }
            static float Mirror(float value) { value -= (float)Math.Floor(value / 2) * 2; return value <= 1 ? value : 2 - value; }
            public float Sample(float u, float v, int level)
            {
                level = Math.Min(levels.Count - 1, level); Size size = sizes[level]; float[] values = levels[level];
                float x = Mirror(u) * (size.Width - 1), y = Mirror(v) * (size.Height - 1);
                int ix = (int)x, iy = (int)y, nx = Math.Min(ix + 1, size.Width - 1), ny = Math.Min(iy + 1, size.Height - 1);
                float a = values[iy * size.Width + ix], b = values[iy * size.Width + nx], c = values[ny * size.Width + ix], d = values[ny * size.Width + nx];
                return a + (b - a) * (x - ix) + (c - a + (a - b - c + d) * (x - ix)) * (y - iy);
            }
        }
        static readonly Lazy<MaterialMap[]> material = new Lazy<MaterialMap[]>(delegate
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("paint.reference.bin"))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(9)) != "PSDPAINT1") throw new InvalidDataException("内置油漆材质无效。");
                MaterialMap[] maps = new MaterialMap[reader.ReadInt32()];
                for (int i = 0; i < maps.Length; i++) maps[i] = new MaterialMap(reader);
                return maps;
            }
        });
        static float SoftLight(float a, float b)
        {
            if (b <= 0.5f) return a - (1 - 2 * b) * a * (1 - a);
            float d = a <= 0.25f ? ((16 * a - 12) * a + 4) * a : (float)Math.Sqrt(a);
            return a + (2 * b - 1) * (d - a);
        }
        static float Overlay(float a, float b) { return a <= 0.5f ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b); }
        static float Smooth(float a, float b, float x) { float t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
        static byte Channel(float n) { return (byte)Math.Max(0, Math.Min(255, n + 0.5f)); }

        // Distance to transparent paint edges: a capped two-pass Manhattan transform.
        static byte[] Edges(byte[] pixels, int width, int height)
        {
            byte[] distance = new byte[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (pixels[i * 4 + 3] <= 64) continue;
                distance[i] = (byte)Math.Min(254, Math.Min(x == 0 ? 1 : distance[i - 1] + 1, y == 0 ? 1 : distance[i - width] + 1));
            }
            for (int y = height - 1; y >= 0; y--) for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                if (distance[i] == 0) continue;
                distance[i] = (byte)Math.Min(distance[i], Math.Min(x == width - 1 ? 1 : distance[i + 1] + 1, y == height - 1 ? 1 : distance[i + width] + 1));
            }
            return distance;
        }
        public static void Apply(Bitmap bitmap, AtlasSlotInfo[] slots, PaintSettings p, float canvasScale, byte[] colorMask = null)
        {
            p.Validate(); if (!p.Active) return;
            MaterialMap[] maps = material.Value;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int width = bitmap.Width, height = bitmap.Height;
                byte[] pixels = new byte[width * height * 4];
                for (int y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * width * 4, width * 4);
                byte[] edges = p.Wear > 0 ? Edges(pixels, width, height) : null;
                float dirt = p.Dirt / 100f, wear = p.Wear / 100f, fade = p.Fade / 100f;
                float cos = (float)Math.Cos((p.Angle - 90) * Math.PI / 180), sin = (float)Math.Sin((p.Angle - 90) * Math.PI / 180);
                float texture = p.Texture / 100f;
                foreach (AtlasSlotInfo slot in slots.Where(s => s.Visible && s.Occupied && s.Error.Length == 0))
                {
                    RectangleF[] customAreas = slot.CustomColorAreas.Select(b => new RectangleF(b.X * canvasScale, b.Y * canvasScale, b.Width * canvasScale, b.Height * canvasScale)).ToArray();
                    float unit = Math.Max(1, slot.CharacterPixels * canvasScale * p.Size / 100f);
                    int seed = unchecked(p.Seed + slot.Index * 7919);
                    float span = unit * 0.24f, du = Hash(7, 13, seed) * 1.6f, dv = Hash(17, 29, seed) * 1.6f;
                    int level = maps[0].Level(span);
                    int x0 = Math.Max(0, (int)Math.Floor(slot.Artwork.Left * canvasScale) - 1), y0 = Math.Max(0, (int)Math.Floor(slot.Artwork.Top * canvasScale) - 1);
                    int x1 = Math.Min(width, (int)Math.Ceiling(slot.Artwork.Right * canvasScale) + 1), y1 = Math.Min(height, (int)Math.Ceiling(slot.Artwork.Bottom * canvasScale) + 1);
                    x0 = Math.Max(x0, (int)Math.Ceiling(slot.Cell.Left * canvasScale - 0.5f)); y0 = Math.Max(y0, (int)Math.Ceiling(slot.Cell.Top * canvasScale - 0.5f));
                    x1 = Math.Min(x1, (int)Math.Ceiling(slot.Cell.Right * canvasScale - 0.5f)); y1 = Math.Min(y1, (int)Math.Ceiling(slot.Cell.Bottom * canvasScale - 0.5f));
                    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                    {
                        int i = (y * width + x) * 4; byte alpha = pixels[i + 3]; if (alpha == 0) continue;
                        float dx = x + 0.5f - slot.Image.X * canvasScale, dy = y + 0.5f - slot.Image.Y * canvasScale;
                        float u = (dx * cos + dy * sin) / span + du, v = (-dx * sin + dy * cos) / (span * 6.4f) + dv;
                        float pu = u * 0.43f + 0.37f, pv = v * 1.6f + pu * 0.37f;
                        float thin = maps[0].Sample(u, v, level) * (204 / 255f) * texture;
                        float brush = maps[1].Sample(u, v, level), grain = maps[2].Sample(u, v, level);
                        float highlight = maps[3].Sample(u, v, level) * (31 / 255f) * texture;
                        float shade = maps[4].Sample(u, v, level) * (23 / 255f) * texture;
                        float dust = maps[5].Sample(u, v, level) * (31 / 255f) * dirt / 0.35f;
                        float spot = maps[6].Sample(u, v, level) * (41 / 255f) * dirt / 0.35f;
                        float scrape = maps[7].Sample(u, v, level) * (31 / 255f) * dirt / 0.35f * (p.Style == 4 ? 4 : 1);
                        float patch = p.Style > 1 ? maps[9].Sample(pu, pv, level) * dirt * (p.Style == 2 ? 0.8f : 0.4f) : 0;
                        bool custom = slot.CustomColors;
                        if (colorMask != null) custom = colorMask[y * width + x] >= 128;
                        else for (int area = 0; !custom && area < customAreas.Length; area++) custom = customAreas[area].Contains(x + 0.5f, y + 0.5f);
                        bool yellow = !custom && pixels[i + 2] > 190 && pixels[i + 1] > 120 && pixels[i] < 70;
                        for (int c = 0; c < 3; c++)
                        {
                            float value = pixels[i + c] / 255f;
                            if (yellow && p.ReferenceYellow) value = (c == 0 ? 9 : c == 1 ? 171 : 255) / 255f;
                            float thinColor = yellow && p.ReferenceYellow ? (c == 0 ? 46 : c == 1 ? 149 : 213) / 255f : value * 0.88f + 0.035f;
                            value += (thinColor - value) * thin;
                            value += (SoftLight(value, brush) - value) * (66 / 255f) * texture;
                            value += (Overlay(value, grain) - value) * (36 / 255f) * texture;
                            value += (1 - value) * highlight; value *= 1 - shade;
                            value *= 1 - dust * (1 - (c == 0 ? 65 : c == 1 ? 73 : 77) / 255f);
                            value *= 1 - spot * (1 - (c == 0 ? 61 : c == 1 ? 75 : 83) / 255f);
                            value *= 1 - scrape * (1 - (c == 0 ? 63 : c == 1 ? 70 : 73) / 255f);
                            value *= 1 - patch * 0.72f;
                            float faded = (c == 0 ? 0.48f : c == 1 ? 0.65f : 0.72f);
                            value += (faded - value) * fade * 0.45f;
                            pixels[i + c] = Channel(value * 255);
                        }
                        float loss = 0;
                        if (wear > 0)
                        {
                            float worn = 1 - maps[8].Sample(pu, pv, level);
                            float exposure = maps[10].Sample(pu, pv, level);
                            float edge = 1 - Smooth(0, Math.Max(1, unit * 0.006f), edges[i / 4]);
                            loss = Math.Min(1, worn * wear / 0.22f + exposure * wear * 0.55f + edge * wear * maps[0].Sample(u, v, level));
                        }
                        pixels[i + 3] = Channel(alpha * (1 - loss));
                    }
                }
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
            finally { bitmap.UnlockBits(data); }
        }
        public static Bitmap Raster(AtlasLayout layout, PaintSettings settings, int maxEdge)
        {
            Bitmap bitmap; float factor = 1;
            if (maxEdge <= 0 || Math.Max(layout.Document.Width, layout.Document.Height) <= maxEdge) bitmap = Exporter.RasterPixels(layout.Document);
            else
            {
                factor = maxEdge / Math.Max(layout.Document.Width, layout.Document.Height);
                bitmap = new Bitmap((int)Math.Round(layout.Document.Width * factor), (int)Math.Round(layout.Document.Height * factor), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); g.ScaleTransform(factor, factor); layout.Document.Draw(g); }
            }
            try
            {
                byte[] mask = null;
                if (layout.UseColorMask && settings.Active && settings.ReferenceYellow)
                {
                    using (Bitmap image = factor == 1 ? Exporter.RasterPixels(layout.Document, true) : new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb))
                    {
                        if (factor != 1) using (Graphics g = Graphics.FromImage(image)) { g.ScaleTransform(factor, factor); layout.Document.Draw(g, true); }
                        mask = new byte[image.Width * image.Height]; byte[] row = new byte[image.Width * 4];
                        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            for (int y = 0; y < image.Height; y++)
                            {
                                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                                for (int x = 0; x < image.Width; x++) mask[y * image.Width + x] = row[x * 4];
                            }
                        }
                        finally { image.UnlockBits(data); }
                    }
                }
                Apply(bitmap, layout.Slots.ToArray(), settings, factor, mask); return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }

    public sealed class PaintForm : DpiForm
    {
        readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = StudioTheme.Canvas };
        readonly Timer timer = new Timer { Interval = 180 };
        readonly AtlasLayout layout;
        public PaintForm(SignEngine engine, AtlasWorkspace workspace)
        {
            Text = "油漆脏污与磨损"; ClientSize = new Size(1120, 700); MinimumSize = new Size(940, 620); StartPosition = FormStartPosition.CenterParent;
            layout = AtlasLayout.Build(engine, workspace);
            PaintSettings working = workspace.Paint.Copy(); bool updating = false;
            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 276, Padding = new Padding(14), BackColor = StudioTheme.Sidebar, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            ComboBox preset = new StudioComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 238, AccessibleName = "油漆磨损预设" };
            preset.Items.AddRange(PaintSettings.Names); preset.SelectedIndex = working.Style;
            controls.Controls.Add(new Label { Text = "油漆预设", AutoSize = true, ForeColor = StudioTheme.Muted }); controls.Controls.Add(StudioTheme.FrameInput(preset, 34));
            NumericUpDown[] values = new NumericUpDown[7];
            string[] names = { "污渍强度 %", "剥落 / 擦磨 %", "粉化褪色 %", "纹理大小 %", "随机种子", "刷纹方向 °", "刷纹 / 颗粒强度 %" };
            int[] max = { 100, 100, 100, 400, 999999, 180, 100 };
            Action show = delegate
            {
                updating = true; int[] numbers = { working.Dirt, working.Wear, working.Fade, working.Size, working.Seed, working.Angle, working.Texture };
                for (int i = 0; i < values.Length; i++) { values[i].Value = numbers[i]; values[i].Enabled = working.Style > 0; }
                updating = false; timer.Stop(); timer.Start();
            };
            for (int i = 0; i < values.Length; i++)
            {
                controls.Controls.Add(new Label { Text = names[i], AutoSize = true, ForeColor = StudioTheme.Muted, Margin = new Padding(0, 10, 0, 2) });
                values[i] = new NumericUpDown { Minimum = i == 3 ? 25 : 0, Maximum = max[i], Width = 238, AccessibleName = names[i] };
                controls.Controls.Add(StudioTheme.FrameInput(values[i], 34));
                values[i].ValueChanged += delegate
                {
                    if (updating) return;
                    working.Dirt = (int)values[0].Value; working.Wear = (int)values[1].Value; working.Fade = (int)values[2].Value;
                    working.Size = (int)values[3].Value; working.Seed = (int)values[4].Value; working.Angle = (int)values[5].Value; working.Texture = (int)values[6].Value;
                    timer.Stop(); timer.Start();
                };
            }
            preset.SelectedIndexChanged += delegate { bool color = working.ReferenceYellow; working = PaintSettings.Preset(preset.SelectedIndex); working.ReferenceYellow = color; show(); };
            CheckBox referenceColor = new CheckBox { Text = "使用参考暖黄 #FFAB09", Checked = working.ReferenceYellow, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
            referenceColor.CheckedChanged += delegate { working.ReferenceYellow = referenceColor.Checked; timer.Stop(); timer.Start(); };
            controls.Controls.Add(referenceColor);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10) };
            Button apply = new StudioButton { Text = "应用到拼图", Primary = true, Width = 120, Height = 30 }, cancel = new StudioButton { Text = "取消", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            apply.Click += delegate { ValidateChildren(); workspace.Paint = working.Copy(); DialogResult = DialogResult.OK; Close(); };
            actions.Controls.Add(apply); actions.Controls.Add(cancel); CancelButton = cancel;
            Label note = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 7, 0, 0), Visible = false };
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(21, 13, 21, 13) }; right.Controls.Add(preview); right.Controls.Add(note);
            preview.Paint += delegate(object sender, PaintEventArgs e) { StudioTheme.CanvasFrame(preview, e.Graphics); };
            Controls.Add(right); Controls.Add(controls); Controls.Add(actions);
            timer.Tick += delegate
            {
                timer.Stop();
                try { Bitmap next = PaintRenderer.Raster(layout, working, 1280); Image old = preview.Image; preview.Image = next; if (old != null) old.Dispose(); note.Visible = false; }
                catch (Exception error) { note.Text = error.Message; note.Visible = true; }
            };
            show();
            CompleteLayout();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (preview.Image != null) preview.Image.Dispose(); layout.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
