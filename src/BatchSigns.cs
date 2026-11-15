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
    internal enum BatchSplit { ExistingFirst, Automatic, Fixed }

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

        public static (string[] Rows, int Review, int OcrPages) FromPdfs(IEnumerable<string> files)
        {
            List<string> confirmed = new List<string>();
            List<string> reviewRows = new List<string>();
            int ocrPages = 0;
            foreach (string path in WithCompanionTables(files))
            {
                List<string> ids = new List<string>();
                List<string> unresolvedPages = new List<string>();
                bool namedTable = IsStandTableName(path);
                bool isTable = namedTable;
                using PdfDocument pdf = PdfDocument.Open(path);
                PdfPageRaster raster = null;
                foreach (var page in pdf.GetPages())
                {
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (HasTaxiwayHeading(text)) continue;
                    bool standPage = namedTable || HasStandHeading(text);
                    isTable |= standPage;
                    if (standPage)
                    {
                        int before = ids.Count;
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
                        if (ids.Count > before) continue;
                    }
                    else if (page.Letters.Count >= 40 && (page.NumberOfImages == 0 ||
                        page.GetImages().Sum(img => img.BoundingBox.Width * img.BoundingBox.Height) < page.Width * page.Height * 0.30))
                        continue;

                    try
                    {
                        string[] rows = PdfPageRaster.ReadFullPageImage(page);
                        if (rows == null)
                        {
                            raster ??= new PdfPageRaster(path);
                            using Bitmap image = raster.Render(page.Number);
                            rows = StandChartOcr.Read(image, out _);
                        }
                        ocrPages++;
                        foreach (string row in rows)
                            if (IsReviewRow(row)) reviewRows.Add(row); else ids.Add(row);
                    }
                    catch (StandChartRejectedException)
                    {
                        if (standPage) unresolvedPages.Add(StandChartOcr.ReviewPrefix + "PDF 第 " + page.Number + " 页未识别]");
                    }
                }
                if (isTable && ids.Count == 0 && unresolvedPages.Count > 0)
                    throw new ArgumentException(Path.GetFileName(path) + " 中没有读出可用机位编号。请检查 PDF 是否包含清晰的 0P 机位坐标表。");
                confirmed.AddRange(ids);
                reviewRows.AddRange(unresolvedPages);
            }
            string[] ordered = confirmed.Distinct().OrderBy(id => Regex.Replace(id, @"\d+", number => number.Value.PadLeft(5, '0')), StringComparer.Ordinal)
                .Concat(reviewRows).ToArray();
            return (ordered, reviewRows.Count, ocrPages);
        }

        static bool IsReviewRow(string value)
        {
            return value == MissingStand || value.StartsWith(StandChartOcr.ReviewPrefix, StringComparison.Ordinal);
        }

        static bool HasTaxiwayHeading(string text)
        {
            string compact = Regex.Replace(SignEngine.Normalize(text), @"\s+", "");
            return Regex.IsMatch(compact, @"滑行道(?:交叉点|位置|交叉口)?坐标|TAXIWAY(?:INTERSECTION|LOCATION|POSITION)?COORDINATES");
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

        public static (string[] Rows, string Note) ImportFiles(string[] files)
        {
            List<string> imported = new List<string>();
            int review = 0;
            int ocrPages = 0;
            string[] pdf = files.Where(path => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
            bool table = true;
            if (pdf.Length > 0)
            {
                var result = FromPdfs(pdf);
                imported.AddRange(result.Rows);
                table = result.Rows.Length > 0;
                review += result.Review;
                ocrPages = result.OcrPages;
            }
            foreach (string path in files.Except(pdf))
            {
                if (StandChartOcr.IsImage(path))
                {
                    try
                    {
                        imported.AddRange(StandChartOcr.Read(path, out int needsReview));
                        review += needsReview;
                    }
                    catch (ArgumentException error) { throw new ArgumentException(Path.GetFileName(path) + "：" + error.Message, error); }
                }
                else
                {
                    string text = File.ReadAllText(path);
                    if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        imported.AddRange(FromReport(text, out int needsReview));
                        review += needsReview;
                    }
                    else imported.AddRange(ParseList(text));
                }
            }
            string note = review > 0 ? $"{review} 条记录待核对。请修正或删除带方括号的行。" :
                ocrPages > 0 ? $"已识别 {ocrPages} 页扫描版机位表。请对照原图核对。" :
                !table ? "已跳过普通平面图，机位编号需有坐标表依据。" : "请对照原图检查编号清单。";
            return (imported.ToArray(), note);
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
                    float width = ((page.Width - 2f * page.Padding + page.Gap) / columns - page.Gap - 2 * page.Padding) / page.TargetTextPixels;
                    float height = ((page.Height - 2f * page.Padding + page.Gap) / rows - page.Gap - 2 * page.Padding) / page.TargetTextPixels;
                    int n = 0;
                    while (n < capacity && start + n < signs.Length && signs[start + n].Width <= width && signs[start + n].Height <= height) n++;
                    if (n < count || n == count && capacity >= places) continue;
                    count = n; places = capacity; best = new Size(columns, rows);
                }
            return best;
        }

        public static int Add(AtlasBook book, SignEngine engine, string text, Settings template, int startPage,
            BatchSplit split = BatchSplit.ExistingFirst, Size resolution = default, Size grid = default)
        {
            string[] values = ParseList(text);
            if (values.Length == 0) throw new ArgumentException("请输入要生成的内容。");
            if (startPage < 0 || startPage > book.Pages.Count) throw new ArgumentException("请选择有效的起始拼图。");
            if (!resolution.IsEmpty && (resolution.Width < 128 || resolution.Width > 8192 || resolution.Height < 128 || resolution.Height > 8192))
                throw new ArgumentException("画布宽高支持 128–8192 px。");
            AtlasWorkspace source = startPage < book.Pages.Count ? book.Pages[startPage] : book.Current;
            Settings[] signs = new Settings[values.Length];
            SizeF[] needed = new SizeF[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                Settings sign = template.Copy();
                if (IsReviewRow(value) ||
                    (sign.Kind == AssetKind.GroundStand || sign.Kind == AssetKind.StandSign) && !Stand.IsMatch(value))
                    throw new ArgumentException("请核对机位编号：" + value);
                if (sign.Kind == AssetKind.TaxiSign && sign.Panels.Count > 0) sign.Panels[0].Text = value;
                else sign.Text = value;
                try
                {
                    using SignDocument preview = engine.Build(sign);
                    float scale = 1f / AtlasLayout.SmallestTextHeight(sign);
                    needed[i] = new SizeF(preview.Width * scale, preview.Height * scale);
                }
                catch (ArgumentException error) { throw new ArgumentException(value + "：" + error.Message); }
                signs[i] = sign;
            }
            int offset = 0;
            var placements = new List<(int Page, int Slot, Settings Sign)>();
            var layouts = new List<(int Index, AtlasWorkspace Page)>();
            for (int index = startPage; offset < signs.Length; index++)
            {
                if (index >= AtlasBook.MaxPages)
                    throw new ArgumentException("一个工程最多可有 64 张拼图。请调整分割、分辨率或分批建立工程。");
                AtlasWorkspace existing = index < book.Pages.Count ? book.Pages[index] : null;
                AtlasWorkspace atlas = existing ?? source;
                bool empty = existing == null || !existing.Slots.Any(slot => slot != null);
                bool regrid = empty && (existing == null || split != BatchSplit.ExistingFirst);
                bool resize = empty && !resolution.IsEmpty && (atlas.Width != resolution.Width || atlas.Height != resolution.Height);
                int take = signs.Length - offset;
                if (regrid || resize)
                {
                    atlas = AtlasBook.BlankLike(atlas);
                    if (!resolution.IsEmpty) { atlas.Width = resolution.Width; atlas.Height = resolution.Height; }
                    if (regrid)
                    {
                        Size chosen = grid;
                        if (split != BatchSplit.Fixed)
                        {
                            chosen = ChooseGrid(atlas, needed, offset, out take);
                            take = Math.Max(1, take); // Allow one undersized sign and keep the usual density warning.
                        }
                        atlas.Resize(chosen.Height, chosen.Width);
                    }
                    else Array.Copy(existing.Regions, atlas.Regions, atlas.Regions.Length);
                    if (existing != null)
                    {
                        atlas.ProjectPath = existing.ProjectPath; atlas.SavedProject = existing.SavedProject;
                        Array.Copy(existing.PolNames, atlas.PolNames, atlas.PolNames.Length);
                    }
                    for (int slot = 0; slot < atlas.Count; slot++)
                        if (atlas.Regions[slot] != null) AtlasLayout.Cell(atlas, slot, atlas.Width, atlas.Height);
                }
                int before = offset;
                for (int slot = 0; slot < atlas.Count && offset - before < take; slot++)
                {
                    if (atlas.Regions[slot] == null || atlas.Slots[slot] != null) continue;
                    SizeF size = needed[offset];
                    if (!regrid && !Fits(atlas, slot, new SizeF(size.Width * atlas.TargetTextPixels, size.Height * atlas.TargetTextPixels))) continue;
                    placements.Add((index, slot, signs[offset++]));
                }
                if (offset > before && atlas != existing) layouts.Add((index, atlas));
            }
            foreach (var layout in layouts)
            {
                if (layout.Index < book.Pages.Count) book.Pages[layout.Index] = layout.Page;
                else book.Pages.Add(layout.Page);
            }
            foreach (var placement in placements) book.Pages[placement.Page].Slots[placement.Slot] = placement.Sign;
            book.Active = placements[0].Page;
            book.Current.Selected = placements[0].Slot;
            return signs.Length;
        }
    }
}
