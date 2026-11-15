using System;
using System.Drawing;

namespace AirportSignStudio
{
    // View-only geometry: never changes the atlas, its UVs or its edit history.
    internal sealed class PreviewViewport
    {
        public const float MinimumZoom = 0.01f, MaximumZoom = 8;
        Size viewport, image, fitReference;
        float inset, zoom = 1;
        PointF pan;
        public bool IsFit { get; private set; } = true;
        public float Scale
        {
            get
            {
                if (image.Width <= 0 || image.Height <= 0) return 0;
                return IsFit ? Math.Max(0, Math.Min((viewport.Width - 2 * inset) / fitReference.Width, (viewport.Height - 2 * inset) / fitReference.Height)) : zoom;
            }
        }
        public RectangleF Bounds
        {
            get
            {
                float scale = Scale; if (scale <= 0) return RectangleF.Empty;
                return new RectangleF((viewport.Width - image.Width * scale) / 2 + pan.X, (viewport.Height - image.Height * scale) / 2 + pan.Y, image.Width * scale, image.Height * scale);
            }
        }
        public void Resize(Size client, Size pixels, float margin, Size fitPixels = default)
        {
            viewport = client; image = pixels; fitReference = fitPixels.IsEmpty ? pixels : fitPixels;
            inset = margin; ClampPan();
        }
        public void Fit() { IsFit = true; pan = PointF.Empty; }
        public PointF ImagePoint(PointF screen)
        {
            RectangleF bounds = Bounds;
            return bounds.IsEmpty ? PointF.Empty : new PointF((screen.X - bounds.X) / Scale, (screen.Y - bounds.Y) / Scale);
        }
        public void ZoomAt(float value, PointF anchor)
        {
            if (Scale <= 0 || float.IsNaN(value) || float.IsInfinity(value)) return;
            PointF pixel = ImagePoint(anchor);
            zoom = Math.Max(MinimumZoom, Math.Min(MaximumZoom, value)); IsFit = false;
            pan = new PointF(anchor.X - pixel.X * zoom - (viewport.Width - image.Width * zoom) / 2,
                anchor.Y - pixel.Y * zoom - (viewport.Height - image.Height * zoom) / 2);
            ClampPan();
        }
        public void Move(float dx, float dy) { pan = new PointF(pan.X + dx, pan.Y + dy); ClampPan(); }
        void ClampPan()
        {
            float x = Math.Max(0, (image.Width * Scale - Math.Max(1, viewport.Width - 2 * inset)) / 2);
            float y = Math.Max(0, (image.Height * Scale - Math.Max(1, viewport.Height - 2 * inset)) / 2);
            pan = new PointF(Math.Max(-x, Math.Min(x, pan.X)), Math.Max(-y, Math.Min(y, pan.Y)));
        }
    }

    internal sealed class AtlasStripGeometry
    {
        public readonly Size Extent, FitReference;
        readonly Rectangle[] pages;
        public AtlasStripGeometry(Size[] sizes)
        {
            int widest = 0, tallest = 0;
            foreach (Size size in sizes) { widest = Math.Max(widest, size.Width); tallest = Math.Max(tallest, size.Height); }
            int gap = Math.Max(24, widest / 24);
            pages = new Rectangle[sizes.Length];
            int top = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                Size size = sizes[i];
                pages[i] = new Rectangle((widest - size.Width) / 2, top, size.Width, size.Height);
                top += size.Height + gap;
            }
            Extent = new Size(widest, top - gap);
            FitReference = new Size(widest, tallest);
        }
        public RectangleF Bounds(int index, RectangleF strip, float scale)
        {
            Rectangle box = pages[index];
            return new RectangleF(strip.X + box.X * scale, strip.Y + box.Y * scale, box.Width * scale, box.Height * scale);
        }
        public int Hit(PointF point, RectangleF strip, float scale)
        {
            for (int i = 0; i < pages.Length; i++) if (Bounds(i, strip, scale).Contains(point)) return i;
            return -1;
        }
    }
}
