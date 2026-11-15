using System;
using System.Drawing;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Content;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AirportSignStudio
{
    internal sealed class PdfPageRaster
    {
        readonly PdfDocument document;

        public PdfPageRaster(string path)
        {
            StorageFile file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
            document = PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        }

        public Bitmap Render(int pageNumber)
        {
            using PdfPage page = document.GetPage((uint)(pageNumber - 1));
            using InMemoryRandomAccessStream buffer = new InMemoryRandomAccessStream();
            double longest = Math.Max(page.Size.Width, page.Size.Height);
            double scale = 2400 / longest;
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 },
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
                IsIgnoringHighContrast = true
            };
            page.RenderToStreamAsync(buffer, options).AsTask().GetAwaiter().GetResult();
            buffer.Seek(0);
            using Stream stream = buffer.AsStreamForRead();
            using Bitmap rendered = new Bitmap(stream);
            return (Bitmap)rendered.Clone();
        }

        public static string[] ReadFullPageImage(Page page)
        {
            if (page.NumberOfImages != 1) return null;
            IPdfImage image = page.GetImages().First();
            if (image.BoundingBox.Width * image.BoundingBox.Height < page.Width * page.Height * 0.75 ||
                (long)image.WidthInSamples * image.HeightInSamples > 24000000) return null;
            var raw = image.RawMemory;
            byte[] encoded;
            if (raw.Length > 2 && raw.Span[0] == 0xff && raw.Span[1] == 0xd8)
                encoded = raw.ToArray();
            else if (!image.TryGetPng(out encoded)) return null;
            using MemoryStream stream = new MemoryStream(encoded, false);
            Bitmap bitmap;
            try { bitmap = new Bitmap(stream); }
            catch (ArgumentException) { return null; }
            using (bitmap)
            {
                try { return StandChartOcr.Read(bitmap, out _); }
                catch (StandChartRejectedException) { return null; }
            }
        }
    }
}
