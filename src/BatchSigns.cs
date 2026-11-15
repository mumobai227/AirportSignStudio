using System;
using System.Collections.Generic;
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
        static readonly Regex MapStand = new Regex(@"\A(?:[A-Z]?\d{1,4}[A-Z]?)\z");
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

        public static string[] FromPdfs(IEnumerable<string> files, out bool table)
        {
            List<string> confirmed = new List<string>(), candidates = new List<string>();
            foreach (string path in files)
            {
                List<string> ids = new List<string>(), labels = new List<string>();
                using PdfDocument pdf = PdfDocument.Open(path);
                foreach (var page in pdf.GetPages())
                {
                    string text = ContentOrderTextExtractor.GetText(page);
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
                    if (ids.Count == 0 && !Path.GetFileName(path).Contains("-0P", StringComparison.OrdinalIgnoreCase))
                    {
                        labels.AddRange(words.Where(word => word.BoundingBox.Bottom >= page.Height * 0.18 && word.BoundingBox.Top <= page.Height * 0.8)
                            .Select(word => SignEngine.Normalize(word.Text)).Where(label => MapStand.IsMatch(label) && !Regex.IsMatch(label, @"\A\d{1,2}[LCR]?\z")));
                    }
                }
                if (ids.Count == 0 && labels.Count == 0)
                    throw new ArgumentException(Path.GetFileName(path) + " 未找到可用机位编号。扫描图可先用 sign_autogen 识别后导入 JSON。");
                confirmed.AddRange(ids);
                candidates.AddRange(labels);
            }
            table = confirmed.Count > 0;
            return (table ? confirmed : candidates).Distinct().OrderBy(id => Regex.Replace(id, @"\d+", number => number.Value.PadLeft(5, '0')), StringComparer.Ordinal).ToArray();
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

        public static int Add(AtlasBook book, SignEngine engine, string text, Settings template, bool fillCurrent)
        {
            string[] values = ParseList(text);
            if (values.Length == 0) throw new ArgumentException("请输入要生成的内容。");
            AtlasWorkspace current = book.Current;
            int[] empty = fillCurrent ? Enumerable.Range(0, current.Count).Where(i => current.Regions[i] != null &&
                current.Slots[i] == null).ToArray() : Array.Empty<int>();
            int newCount = (Math.Max(0, values.Length - empty.Length) + current.Count - 1) / current.Count;
            if (book.Pages.Count + newCount > AtlasBook.MaxPages)
                throw new ArgumentException("一个工程最多可有 64 张拼图。请增加每张拼图的行列数或分批建立工程。");
            Settings[] signs = values.Select(value =>
            {
                Settings sign = template.Copy();
                if (value == MissingStand || (sign.Kind == AssetKind.GroundStand || sign.Kind == AssetKind.StandSign) && !Stand.IsMatch(value))
                    throw new ArgumentException("请核对机位编号：" + value);
                if (sign.Kind == AssetKind.TaxiSign && sign.Panels.Count > 0) sign.Panels[0].Text = value;
                else sign.Text = value;
                try { using SignDocument preview = engine.Build(sign); }
                catch (ArgumentException error) { throw new ArgumentException(value + "：" + error.Message); }
                return sign;
            }).ToArray();
            int offset = 0;
            while (offset < Math.Min(empty.Length, signs.Length))
            {
                current.Slots[empty[offset]] = signs[offset];
                offset++;
            }
            if (offset > 0) current.Selected = empty[0];
            while (offset < signs.Length)
            {
                AtlasWorkspace page = AtlasBook.BlankLike(current);
                if (offset == 0) book.Active = book.Pages.Count;
                book.Pages.Add(page);
                for (int i = 0; i < page.Count && offset < signs.Length; i++) page.Slots[i] = signs[offset++];
            }
            return signs.Length;
        }
    }
}
