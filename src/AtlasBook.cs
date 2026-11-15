using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace AirportSignStudio
{
    public sealed class AtlasBookData
    {
        public int Version = 1;
        public AirportProject[] Pages;
    }

    internal sealed class AtlasBook
    {
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AtlasBookData));
        public const int MaxPages = 64;
        public readonly List<AtlasWorkspace> Pages;
        public int Active;
        public string ProjectPath, SavedProject;
        public AtlasWorkspace Current { get { return Pages[Active]; } }

        public AtlasBook(AtlasWorkspace first)
        {
            Pages = new List<AtlasWorkspace> { first };
            ProjectPath = first.ProjectPath; SavedProject = first.SavedProject;
        }

        public string Encode()
        {
            return Pages.Count == 1 ? ProjectStore.Encode(Pages[0]) :
                ProjectStore.EncodeXml(Serializer, new AtlasBookData { Pages = Pages.Select(ProjectStore.ToData).ToArray() });
        }

        public static AtlasBook Decode(string text)
        {
            using (XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000000 }))
            {
                reader.MoveToContent();
                if (reader.LocalName == "AirportProject") return new AtlasBook(ProjectStore.Decode(text));
                AtlasBookData data = (AtlasBookData)Serializer.Deserialize(reader);
                if (data.Version != 1 || data.Pages == null || data.Pages.Length < 1 || data.Pages.Length > MaxPages)
                    throw new ArgumentException("拼图工程版本不支持或拼图张数无效。");
                AtlasWorkspace[] pages = data.Pages.Select(ProjectStore.FromData).ToArray();
                AtlasBook book = new AtlasBook(pages[0]);
                book.Pages.AddRange(pages.Skip(1));
                book.SavedProject = book.Encode();
                return book;
            }
        }

        public void Save(string path)
        {
            string text = Encode();
            ProjectStore.AtomicFile(path, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
            ProjectPath = path; SavedProject = text;
            if (Pages.Count == 1)
            {
                Current.ProjectPath = path;
                Current.SavedProject = text;
            }
        }

        public static AtlasWorkspace BlankLike(AtlasWorkspace source)
        {
            AtlasWorkspace page = new AtlasWorkspace
            {
                Width = source.Width, Height = source.Height, Gap = source.Gap, Padding = source.Padding,
                TargetTextPixels = source.TargetTextPixels, XPlane = source.XPlane, PolPrefix = source.PolPrefix,
                PolLayer = source.PolLayer, PolOffset = source.PolOffset, PolBleed = source.PolBleed, PolBoundary = source.PolBoundary,
                Colors = source.Colors.Copy(), TypeColors = source.TypeColors.Copy(),
                BaseColors = source.BaseColors.Copy(), Paint = source.Paint.Copy()
            };
            page.Resize(source.Rows, source.Columns);
            return page;
        }

        public void Restore(IReadOnlyList<AtlasWorkspace> snapshots, int active, bool preserveSave = false)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                AtlasWorkspace snapshot = snapshots[i];
                if (i == Pages.Count) Pages.Add(new AtlasWorkspace());
                AtlasWorkspace page = Pages[i];
                string path = page.ProjectPath, saved = page.SavedProject;
                ProjectStore.Apply(page, snapshot);
                for (int slot = 0; slot < 64; slot++)
                {
                    page.Slots[slot] = snapshot.Slots[slot]?.Copy();
                    page.Regions[slot] = RegionData.From(snapshot.Regions[slot])?.ToRegion();
                }
                if (preserveSave) { page.ProjectPath = path; page.SavedProject = saved; }
            }
            if (Pages.Count > snapshots.Count) Pages.RemoveRange(snapshots.Count, Pages.Count - snapshots.Count);
            Active = active;
        }

        public string ExportAll(SignEngine engine, string destination)
        {
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("输出子文件夹已存在，请重新选择。");
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)), "." + Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(folder);
            try
            {
                StringBuilder manifest = new StringBuilder("atlas,png,pol,region,marking,u_min,v_min,u_max,v_max\n");
                bool any = false;
                for (int i = 0; i < Pages.Count; i++)
                {
                    AtlasWorkspace page = Pages[i];
                    if (page.Filled == 0) continue;
                    AtlasWorkspace output = new AtlasWorkspace();
                    ProjectStore.Apply(output, page);
                    string index = (i + 1).ToString("D2", CultureInfo.InvariantCulture);
                    string prefix = output.XPlane ? PolExporter.Name(output.PolPrefix.Trim()) : "Atlas";
                    output.PolPrefix = index + "_" + prefix.Substring(0, Math.Min(prefix.Length, 57));
                    string png = output.PolPrefix + "_atlas.png", pol = output.XPlane ? output.PolPrefix + ".pol" : "";
                    using (AtlasLayout layout = AtlasLayout.Build(engine, output))
                    {
                        if (!layout.CanExport) throw new ArgumentException("拼图 " + index + " 有输入错误：" +
                            string.Join("；", layout.Slots.Where(slot => slot.Error.Length > 0).Select(slot => (slot.Index + 1) + " · " + slot.Error)));
                        if (output.XPlane) PolExporter.Export(engine, output, folder);
                        else using (Bitmap bitmap = PaintRenderer.Raster(layout, output.Paint, 0)) bitmap.Save(Path.Combine(folder, png), ImageFormat.Png);
                        foreach (AtlasSlotInfo slot in layout.Slots.Where(slot => slot.Visible && slot.Occupied))
                        {
                            RectangleF bounds = output.XPlane ? PolExporter.RegionBounds(slot, output.PolBleed, output.PolBoundary) : slot.Artwork;
                            manifest.Append(index).Append(',').Append(png).Append(',').Append(pol).Append(',').Append(slot.Index + 1)
                                .Append(",\"").Append(slot.Caption.Replace("\"", "\"\"")).Append("\",")
                                .AppendFormat(CultureInfo.InvariantCulture, "{0:0.#########},{1:0.#########},{2:0.#########},{3:0.#########}\n",
                                    bounds.Left / page.Width, 1 - bounds.Bottom / page.Height, bounds.Right / page.Width, 1 - bounds.Top / page.Height);
                        }
                    }
                    any = true;
                }
                if (!any) throw new ArgumentException("工程里还没有可导出的标牌。");
                File.WriteAllText(Path.Combine(folder, "AtlasIndex.csv"), manifest.ToString(), new UTF8Encoding(true));
                Directory.Move(folder, destination);
                return destination;
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
