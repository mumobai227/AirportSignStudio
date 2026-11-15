using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AirportSignStudio
{
    internal static class StandChartOcr
    {
        internal const string ReviewPrefix = "[待核对:";
        static readonly Regex StandId = new Regex(@"\A(?=[A-Z0-9-]*[0-9])[A-Z0-9]+(?:-[A-Z0-9]+)*\z");
        static readonly Regex ChartNumber = new Regex(@"\A[0O]P(?:-[0-9]{1,2})?\z");
        static readonly Regex CoordinatePair = new Regex(@"\A[NS]([0-9]{2})[0-9]{4,9}[EW]([0-9]{3})[0-9]{4,9}\z");
        static readonly Lazy<bool> LoadBundledRuntime = new Lazy<bool>(PreloadRuntime);

        static bool PreloadRuntime()
        {
            string[] libraries = { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll" };
            string search = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "";
            foreach (string path in search.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!File.Exists(Path.Combine(path, "onnxruntime.dll")) || !libraries.All(name => File.Exists(Path.Combine(path, name)))) continue;
                foreach (string name in libraries) NativeLibrary.Load(Path.Combine(path, name));
                return true;
            }
            return false;
        }

        public static bool IsImage(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
        }

        internal static bool HasCoordinatePair(string text)
        {
            string compact = Regex.Replace(SignEngine.Normalize(text), @"[^\p{L}0-9]", "");
            Match match = CoordinatePair.Match(compact);
            return match.Success && int.Parse(match.Groups[1].Value) <= 90 && int.Parse(match.Groups[2].Value) <= 180;
        }

        static bool IsTaxiway(string text)
        {
            return text.Contains("滑行道") || text.Contains("TAXIWAY");
        }

        static bool IsStandColumn(string text)
        {
            return text.Contains("机位") || text.Contains("停机位") || text.Contains("STAND");
        }

        public static string[] Read(string path, out int review)
        {
            using Bitmap input = new Bitmap(path);
            if ((long)input.Width * input.Height > 24000000)
                throw new ArgumentException("截图过大，请裁到完整的机位坐标表和表头后再导入。");
            Pixels pixels = new Pixels(input);
            (int[] xs, int[] ys) = FindGrid(pixels);
            using Recognizer recognizer = new Recognizer(pixels);
            Rectangle heading = Rectangle.FromLTRB(xs[0], Math.Max(0, ys[0] - (ys[1] - ys[0]) * 6), xs[^1], ys[0] - 2);
            string[] title = HeadingLines(pixels, heading).Select(area => Compact(recognizer.Read(area).Text)).ToArray();
            bool identifiedTable = title.Any(text => BatchSigns.HasStandHeading(text) || ChartNumber.IsMatch(text));
            if (title.Any(IsTaxiway) && !title.Any(BatchSigns.HasStandHeading))
                throw new ArgumentException("截图是滑行道表。请导入 0P 机位坐标表截图。");

            var rows = new List<string>();
            review = 0;
            for (int group = 0; group + 2 < xs.Length; group += 2)
            {
                string idHeader = Compact(recognizer.Read(Cell(xs[group], xs[group + 1], ys[0], ys[1])).Text);
                string coordinateHeader = Compact(recognizer.Read(Cell(xs[group + 1], xs[group + 2], ys[0], ys[1])).Text);
                if (IsTaxiway(idHeader) || IsTaxiway(coordinateHeader)) continue;
                if (!identifiedTable && !(IsStandColumn(idHeader) &&
                    (coordinateHeader.Contains("坐标") || coordinateHeader.Contains("COORDINATE")))) continue;

                for (int y = 1; y < ys.Length - 1; y++)
                {
                    Rectangle coordinate = Cell(xs[group + 1], xs[group + 2], ys[y], ys[y + 1]);
                    if (pixels.InkBounds(coordinate).IsEmpty) continue;
                    (string idText, float score) = recognizer.Read(Cell(xs[group], xs[group + 1], ys[y], ys[y + 1]));
                    string id = Compact(idText);
                    bool validId = id.Length <= 16 && StandId.IsMatch(id);
                    bool paired = HasCoordinatePair(recognizer.Read(coordinate).Text);
                    if (validId && paired && score >= 0.90f) rows.Add(id);
                    else
                    {
                        rows.Add(validId ? ReviewPrefix + id + "]" : BatchSigns.MissingStand);
                        review++;
                    }
                }
            }
            if (rows.Count == 0)
                throw new ArgumentException("没有识别出可核对的机位表行。请保留机位坐标表标题、列名和完整表格边线。");
            return rows.ToArray();
        }

        static string Compact(string text) { return Regex.Replace(SignEngine.Normalize(text), @"\s+", ""); }

        static Rectangle Cell(int left, int right, int top, int bottom)
        {
            int y = Math.Max(2, (bottom - top) / 12);
            // Retain left-aligned letters while skipping the ruling.
            return Rectangle.FromLTRB(left + 3, top + y, right - 3, bottom - y);
        }

        static IEnumerable<(int Start, int End)> Runs(IEnumerable<int> positions, int gap)
        {
            int start = -1, end = -1;
            foreach (int position in positions)
            {
                if (start >= 0 && position - end > gap) { yield return (start, end); start = -1; }
                if (start < 0) start = position;
                end = position;
            }
            if (start >= 0) yield return (start, end);
        }

        static int Centre((int Start, int End) run) { return (run.Start + run.End) / 2; }

        static (int[] Columns, int[] Rows) FindGrid(Pixels pixels)
        {
            ArgumentException Error() { return new ArgumentException("没有找到清晰的机位坐标表网格。请导入带表头和边线的 0P 截图。"); }
            int[] horizontal = Runs(Enumerable.Range(0, pixels.Height)
                .Where(y => pixels.InkOnRow(y, 0, pixels.Width) >= pixels.Width * .60), 1).Select(Centre).ToArray();
            if (horizontal.Length < 3) throw Error();
            int top = horizontal[0], bottom = horizontal[^1];
            int[] cols = Runs(Enumerable.Range(0, pixels.Width)
                .Where(x => pixels.InkOnColumn(x, top, bottom + 1) >= (bottom - top) * .65), 1).Select(Centre).ToArray();
            if (cols.Length < 3 || cols.Length > 17 || cols.Length % 2 == 0) throw Error();
            for (int i = 0; i + 2 < cols.Length; i += 2)
            {
                int id = cols[i + 1] - cols[i], coordinate = cols[i + 2] - cols[i + 1];
                if (id < 10 || coordinate < id * 1.3) throw Error();
            }
            // Internal rulings end above merged footer rows.
            var spans = cols.Skip(1).Take(cols.Length - 2).Select(x =>
                Runs(Enumerable.Range(top, bottom - top + 1).Where(y => pixels.InkOnRow(y, x - 1, x + 2) > 0), 3)
                    .OrderByDescending(span => span.End - span.Start).First()).ToArray();
            int start = spans.Select(span => span.Start).OrderBy(value => value).ElementAt(spans.Length / 2);
            int end = spans.Select(span => span.End).OrderBy(value => value).ElementAt(spans.Length / 2);
            int[] rows = horizontal.Where(y => y >= start - 3 && y <= end + 3).ToArray();
            if (rows.Length < 3 || rows.Zip(rows.Skip(1), (a, b) => b - a).Any(height => height < 8)) throw Error();
            return (cols, rows);
        }

        static IEnumerable<Rectangle> HeadingLines(Pixels pixels, Rectangle area)
        {
            if (area.Height <= 0) yield break;
            foreach (var line in Runs(Enumerable.Range(area.Top, area.Height)
                .Where(y => pixels.InkOnRow(y, area.Left, area.Right) >= 5), 3))
            {
                int height = line.End - line.Start + 1;
                if (height < 5) continue;
                foreach (var phrase in Runs(Enumerable.Range(area.Left, area.Width)
                    .Where(x => pixels.InkOnColumn(x, line.Start, line.End + 1) > 0), height))
                    if (phrase.End - phrase.Start >= height)
                        yield return Rectangle.FromLTRB(phrase.Start, line.Start, phrase.End + 1, line.End + 1);
            }
        }

        sealed class Pixels
        {
            readonly byte[] gray;
            public readonly int Width, Height;
            public Pixels(Bitmap source)
            {
                Width = source.Width; Height = source.Height;
                gray = new byte[Width * Height];
                using Bitmap rgb = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
                using (Graphics graphics = Graphics.FromImage(rgb))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(source, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel);
                }
                BitmapData data = rgb.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] row = new byte[Width * 3];
                    for (int y = 0; y < Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (int x = 0; x < Width; x++)
                            gray[y * Width + x] = (byte)((29 * row[x * 3] + 150 * row[x * 3 + 1] + 77 * row[x * 3 + 2]) >> 8);
                    }
                }
                finally { rgb.UnlockBits(data); }
            }
            public int InkOnRow(int y, int left, int right)
            {
                int count = 0;
                for (int x = Math.Max(0, left); x < Math.Min(Width, right); x++) if (gray[y * Width + x] < 160) count++;
                return count;
            }
            public int InkOnColumn(int x, int top, int bottom)
            {
                int count = 0;
                for (int y = top; y < bottom; y++) if (gray[y * Width + x] < 160) count++;
                return count;
            }
            public Rectangle InkBounds(Rectangle area)
            {
                area.Intersect(new Rectangle(0, 0, Width, Height));
                int left = area.Right, right = area.Left, top = area.Bottom, bottom = area.Top, count = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        if (gray[y * Width + x] >= 180) continue;
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y); count++;
                    }
                return count < 6 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }
            public float Sample(Rectangle ink, float x, float y)
            {
                int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
                float dx = x - x0, dy = y - y0;
                float Value(int a, int b) { return ink.Contains(a, b) ? gray[b * Width + a] : 255; }
                return (Value(x0, y0) * (1 - dx) + Value(x0 + 1, y0) * dx) * (1 - dy) +
                    (Value(x0, y0 + 1) * (1 - dx) + Value(x0 + 1, y0 + 1) * dx) * dy;
            }
        }

        sealed class Recognizer : IDisposable
        {
            readonly InferenceSession session;
            readonly Pixels pixels;
            readonly string[] characters;
            public Recognizer(Pixels pixels)
            {
                this.pixels = pixels;
                _ = LoadBundledRuntime.Value;
                using Stream model = typeof(StandChartOcr).Assembly.GetManifestResourceStream("ocr.stand.onnx");
                using MemoryStream bytes = new MemoryStream();
                model.CopyTo(bytes);
                using SessionOptions options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
                session = new InferenceSession(bytes.ToArray(), options);
                characters = session.ModelMetadata.CustomMetadataMap["character"].Replace("\r", "").TrimEnd('\n').Split('\n');
            }
            public (string Text, float Score) Read(Rectangle area)
            {
                Rectangle ink = pixels.InkBounds(area);
                if (ink.IsEmpty) return ("", 0);
                const int height = 48;
                float padding = 8 / Math.Max(1, 48f / ink.Height);
                float sourceWidth = ink.Width + padding * 2, sourceHeight = ink.Height + padding * 2;
                int resized = Math.Clamp((int)MathF.Ceiling(height * sourceWidth / sourceHeight), 1, 1536);
                int width = Math.Max(320, resized), plane = height * width;
                float[] data = new float[3 * plane];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < resized; x++)
                    {
                        float gray = pixels.Sample(ink, ink.Left - padding + (x + 0.5f) * sourceWidth / resized - 0.5f,
                            ink.Top - padding + (y + 0.5f) * sourceHeight / height - 0.5f);
                        int i = y * width + x;
                        data[i] = data[plane + i] = data[2 * plane + i] = gray / 127.5f - 1;
                    }
                var input = NamedOnnxValue.CreateFromTensor(session.InputNames[0], new DenseTensor<float>(data, new[] { 1, 3, height, width }));
                using var result = session.Run(new[] { input });
                var prediction = result.First().AsTensor<float>();
                var logits = prediction.ToArray();
                int steps = prediction.Dimensions[1], classes = prediction.Dimensions[2];
                if (classes != characters.Length + 2) throw new InvalidOperationException("OCR 字符表与模型不匹配。");
                StringBuilder text = new StringBuilder();
                int previous = 0, count = 0;
                float total = 0;
                for (int t = 0; t < steps; t++)
                {
                    int offset = t * classes, best = 0;
                    for (int c = 1; c < classes; c++) if (logits[offset + c] > logits[offset + best]) best = c;
                    if (best != 0 && best != previous)
                    {
                        text.Append(best <= characters.Length ? characters[best - 1] : " ");
                        total += logits[offset + best]; count++;
                    }
                    previous = best;
                }
                return (text.ToString(), count == 0 ? 0 : total / count);
            }
            public void Dispose() { session.Dispose(); }
        }
    }
}
