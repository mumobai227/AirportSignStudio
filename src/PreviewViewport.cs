using System;
using System.Drawing;

namespace AirportSignStudio
{
    // View-only geometry: never changes the atlas, its UVs or its edit history.
    internal sealed class PreviewViewport
    {
        public const float MinimumZoom = 0.01f, MaximumZoom = 8;
        Size viewport, image;
        float inset, zoom = 1;
        PointF pan;
        public bool IsFit { get; private set; } = true;
        public float Scale
        {
            get
            {
                if (image.Width <= 0 || image.Height <= 0) return 0;
                return IsFit ? Math.Max(0, Math.Min((viewport.Width - 2 * inset) / image.Width, (viewport.Height - 2 * inset) / image.Height)) : zoom;
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
        public void Resize(Size client, Size pixels, float margin)
        {
            viewport = client; image = pixels; inset = margin; ClampPan();
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
            pan = IsFit ? PointF.Empty : new PointF(Math.Max(-x, Math.Min(x, pan.X)), Math.Max(-y, Math.Min(y, pan.Y)));
        }
    }
}
