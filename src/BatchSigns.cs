using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AirportSignStudio
{
    internal static class BatchSigns
    {
        public const string MissingStand = "[待补编号]";
        static readonly Regex Stand = new Regex(@"\A[A-Z0-9]+(?:-[A-Z0-9]+)*\z");
        // The degree glyph may extract as two characters on CAAC PDFs.
        static readonly Regex TableRow = new Regex(@"(?<![A-Z0-9])(?<id>[A-Z]?\d{1,4}[A-Z]?)\s*[NS]\s*\d{2}[^\d\r\n]{1,5}\d{2}\s*['’′]\s*\d{1,2}(?:\.\d+)?\s*[""”″]\s*[EW]\s*\d{3}[^\d\r\n]{1,5}\d{2}\s*['’′]\s*\d{1,2}(?:\.\d+)?\s*[""”″]");

        public static string[] ParseList(string text)
        {
            return Regex.Split(text, @"[\r\n\t,，、;；]+").Select(SignEngine.Normalize)
                .Where(value => value.Length != 0).Distinct(StringComparer.Ordinal).ToArray();
        }

        static IEnumerable<string> CoordinateIds(string text)
        {
            return TableRow.Matches(SignEngine.Normalize(text)).Select(match => match.Groups["id"].Value);
        }

        static bool IsStandTableName(string path)
        {
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"(?:\A|[-_ ])0P(?:\z|[-_ ])", RegexOptions.IgnoreCase);
        }

        static IEnumerable<string> WithCompanionTables(IEnumerable<string> files)
        {
            string[] selected = files.Select(Path.GetFullPath).ToArray();
            HashSet<string> seen = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            foreach (string path in seen.ToArray()) yield return path;
            foreach (string path in selected)
            {
                Match chart = Regex.Match(Path.GetFileNameWithoutExtension(path), @"\A([A-Z]{4})-2[APB](?:\z|-)", RegexOptions.IgnoreCase);
                if (!chart.Success) continue;
                string prefix = chart.Groups[1].Value + "-0P";
                string directory = Path.GetDirectoryName(path);
                if (selected.Any(file => string.Equals(Path.GetDirectoryName(file), directory, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsStandTableName(file))) continue;
                foreach (string companion in Directory.EnumerateFiles(directory, prefix + "*.pdf").Where(IsStandTableName).OrderBy(file => file))
                    if (seen.Add(companion)) yield return companion;
            }
        }

        public static string[] FromPdfs(IEnumerable<string> files, out bool table)
        {
            List<string> confirmed = new List<string>();
            foreach (string path in WithCompanionTables(files))
            {
                List<string> ids = new List<string>();
                bool namedTable = IsStandTableName(path);
                bool isTable = namedTable;
                using PdfDocument pdf = PdfDocument.Open(path);
                foreach (var page in pdf.GetPages())
                {
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (!namedTable && !HasStandHeading(text)) continue;
                    isTable = true;
                    ids.AddRange(CoordinateIds(text));
                    var words = page.GetWords(NearestNeighbourWordExtractor.Instance).OrderByDescending(word => word.BoundingBox.Bottom).ToArray();
                    int start = 0;
                    while (start < words.Length)
                    {
                        int end = start + 1;
                        while (end < words.Length && words[start].BoundingBox.Bottom - words[end].BoundingBox.Bottom <= 2) end++;
                        ids.AddRange(CoordinateIds(string.Join(" ", words[start..end].OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))));
                        start = end;
                    }
                }
                if (isTable && ids.Count == 0)
                    throw new ArgumentException(Path.GetFileName(path) + " 中没有读出机位坐标行。扫描表可先用 sign_autogen 识别，再导入 JSON。");
                confirmed.AddRange(ids);
            }
            table = confirmed.Count > 0;
            return confirmed.Distinct().OrderBy(id => Regex.Replace(id, @"\d+", number => number.Value.PadLeft(5, '0')), StringComparer.Ordinal).ToArray();
        }

        internal static bool HasStandHeading(string text)
        {
            string compact = Regex.Replace(SignEngine.Normalize(text), @"\s+", "");
            return Regex.IsMatch(compact, @"停机(?:位置|位)?坐标|机位坐标|AIRCRAFTSTAND(?:POSITION)?COORDINATES|STAND(?:POSITION)?COORDINATES");
        }

        public static string[] FromReport(string json, out int review)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            bool ocr = root.TryGetProperty("records", out JsonElement records);
            if (!ocr && !root.TryGetProperty("stand_label_candidates", out records))
                throw new ArgumentException("请选择 AutoCharts 分析 JSON 或 sign_autogen 识别工程 JSON。");
            var entries = records.EnumerateArray().ToArray();
            bool official = !ocr && entries.Any(row => Read(row, "position_source") == "official_stand_coordinate_table");
            if (official)
                entries = entries.Where(row => Read(row, "position_source") == "official_stand_coordinate_table").ToArray();
            else if (!ocr)
                entries = entries.Where(row => Read(row, "status") == "lead_in_associated" &&
                    row.TryGetProperty("associated_guidance_sources", out JsonElement guidance) &&
                    guidance.ValueKind == JsonValueKind.Array && guidance.GetArrayLength() > 0).ToArray();
            List<string> ids = new List<string>();
            review = 0;
            foreach (JsonElement row in entries)
            {
                if (Flag(row, "excluded")) continue;
                string id = SignEngine.Normalize(Read(row, ocr ? "stand" : "text"));
                if (!Stand.IsMatch(id) || id.Length > 40)
                {
                    ids.Add(MissingStand); review++;
                }
                else
                {
                    ids.Add(id);
                    bool hasIssues = row.TryGetProperty("issues", out JsonElement issues) &&
                        issues.ValueKind == JsonValueKind.Array && issues.GetArrayLength() > 0;
                    bool lowConfidence = row.TryGetProperty("confidence", out JsonElement confidence) &&
                        confidence.ValueKind == JsonValueKind.Number && confidence.TryGetDouble(out double score) && score < 0.9;
                    if ((!ocr && !official) || (ocr && !Flag(row, "reviewed") && (hasIssues || lowConfidence))) review++;
                }
            }
            return ids.ToArray();
        }

        static string Read(JsonElement row, string key)
        {
            return row.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : "";
        }

        static bool Flag(JsonElement row, string key)
        {
            return row.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;
        }

        static bool Fits(AtlasWorkspace page, int slot, SizeF needed)
        {
            try
            {
                RectangleF cell = AtlasLayout.Cell(page, slot, page.Width, page.Height);
                return cell.Width - 2 * page.Padding >= needed.Width && cell.Height - 2 * page.Padding >= needed.Height;
            }
            catch (ArgumentException) { return false; }
        }

        static Size ChooseGrid(AtlasWorkspace page, SizeF[] signs, int start, out int count)
        {
            count = 0;
            Size best = new Size(1, 1);
            int places = int.MaxValue;
            for (int rows = 1; rows <= 8; rows++)
                for (int columns = 1; columns <= 8; columns++)
                {
                    int capacity = rows * columns;
                    float width = (page.Width - 2f * page.Padding + page.Gap) / columns - page.Gap - 2 * page.Padding;
                    float height = (page.Height - 2f * page.Padding + page.Gap) / rows - page.Gap - 2 * page.Padding;
                    int n = 0;
                    while (n < capacity && start + n < signs.Length && signs[start + n].Width <= width && signs[start + n].Height <= height) n++;
                    if (n < count || n == count && capacity >= places) continue;
                    count = n; places = capacity; best = new Size(columns, rows);
                }
            return best;
        }

        public static int Add(AtlasBook book, SignEngine engine, string text, Settings template, bool fillExisting)
        {
            string[] values = ParseList(text);
            if (values.Length == 0) throw new ArgumentException("请输入要生成的内容。");
            AtlasWorkspace current = book.Current;
            Settings[] signs = new Settings[values.Length];
            SizeF[] needed = new SizeF[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                Settings sign = template.Copy();
                if (value == MissingStand || (sign.Kind == AssetKind.GroundStand || sign.Kind == AssetKind.StandSign) && !Stand.IsMatch(value))
                    throw new ArgumentException("请核对机位编号：" + value);
                if (sign.Kind == AssetKind.TaxiSign && sign.Panels.Count > 0) sign.Panels[0].Text = value;
                else sign.Text = value;
                try
                {
                    using SignDocument preview = engine.Build(sign);
                    float scale = current.TargetTextPixels / AtlasLayout.SmallestTextHeight(sign);
                    needed[i] = new SizeF(preview.Width * scale, preview.Height * scale);
                }
                catch (ArgumentException error) { throw new ArgumentException(value + "：" + error.Message); }
                signs[i] = sign;
            }
            int offset = 0;
            var placements = new List<(int Page, int Slot, Settings Sign)>();
            var added = new List<AtlasWorkspace>();
            if (fillExisting)
            {
                for (int page = book.Active; page < book.Pages.Count && offset < signs.Length; page++)
                {
                    AtlasWorkspace atlas = book.Pages[page];
                    for (int slot = 0; slot < atlas.Count && offset < signs.Length; slot++)
                    {
                        if (atlas.Regions[slot] == null || atlas.Slots[slot] != null) continue;
                        SizeF size = needed[offset];
                        float ratio = (float)atlas.TargetTextPixels / current.TargetTextPixels;
                        if (!Fits(atlas, slot, new SizeF(size.Width * ratio, size.Height * ratio))) continue;
                        placements.Add((page, slot, signs[offset++]));
                    }
                }
            }
            while (offset < signs.Length)
            {
                if (book.Pages.Count + added.Count >= AtlasBook.MaxPages)
                    throw new ArgumentException("一个工程最多可有 64 张拼图。请提高分辨率、调整参考字高，或分批建立工程。");
                AtlasWorkspace page = AtlasBook.BlankLike(current);
                Size grid = ChooseGrid(page, needed, offset, out int take);
                page.Resize(grid.Height, grid.Width);
                if (take == 0)
                {
                    if (!Fits(page, 0, SizeF.Empty)) throw new ArgumentException("当前分辨率和留白无法容纳单格，请先调整画布。");
                    take = 1; // Keep the sign at the requested resolution and let the usual density hint mark it.
                }
                int index = book.Pages.Count + added.Count;
                added.Add(page);
                for (int i = 0; i < take; i++) placements.Add((index, i, signs[offset++]));
            }
            book.Pages.AddRange(added);
            foreach (var placement in placements) book.Pages[placement.Page].Slots[placement.Slot] = placement.Sign;
            book.Active = placements[0].Page;
            book.Current.Selected = placements[0].Slot;
            return signs.Length;
        }
    }
}
