using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CrosshairDesktop
{
    internal struct PaintedShape
    {
        internal RectangleF Bounds;
        internal Color Color;
        internal bool Ellipse;
        internal float Stroke;
        internal PaintedShape(RectangleF rect, Color color, bool ellipse, float stroke) { Bounds = rect; Color = color; Ellipse = ellipse; Stroke = stroke; }
    }

    internal static class Renderer
    {
        internal static int CanvasSize(CrosshairStyle s, double scale)
        {
            double radius = s.EnableCircleCrosshair ? s.Size + s.Thickness / 2 : s.Size * 2 + s.Thickness + Math.Abs(s.Gap + 4);
            radius = Math.Max(radius, (s.DotSize > 0 ? s.DotSize : s.Thickness));
            int size = (int)Math.Ceiling((radius + s.Outline + 8) * scale) * 2;
            return Math.Max(64, Math.Min(4096, size));
        }

        internal static List<PaintedShape> Geometry(CrosshairStyle s, PointF center, double scale)
        {
            var shapes = new List<PaintedShape>();
            Color edge = CrosshairStyle.ParseColor(s.OutlineColor);
            float unit = (float)scale, cx = center.X, cy = center.Y;
            if (s.EnableCircleCrosshair)
            {
                if (s.EnableDot) AddDot(shapes, s, cx, cy, unit, 0, s.Fill);
                float radius = (float)s.Size * unit, width = (float)s.Thickness * unit;
                if (radius > 0 && (width > 0 || s.Outline > 0))
                {
                    var box = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
                    if (s.Outline > 0) shapes.Add(new PaintedShape(box, edge, true, width + (float)s.Outline * unit * 2));
                    if (width > 0) shapes.Add(new PaintedShape(box, s.Fill, true, width));
                }
                return shapes;
            }
            float length = (float)(s.Size * 2 * unit), thickness = (float)(s.Thickness * 2 * unit);
            float gap = (float)((s.Thickness + s.Gap + 4) * unit), outline = (float)(s.Outline * unit);
            float before = thickness / 2, shift = 0;
            if (s.PixelSnap)
            {
                cx = Round(cx); cy = Round(cy); length = Pixels(length); thickness = Pixels(thickness);
                gap = Round(gap); outline = Pixels(outline); before = (float)Math.Ceiling(thickness / 2); shift = ((int)thickness % 2) == 1 ? -1 : 0;
            }
            var arms = new List<RectangleF>();
            if (length > 0)
            {
                arms.Add(new RectangleF(cx + gap + shift, cy - before, length, thickness));
                arms.Add(new RectangleF(cx - gap - length, cy - before, length, thickness));
                if (!s.EnableTShape) arms.Add(new RectangleF(cx - before, cy - gap - length, thickness, length));
                arms.Add(new RectangleF(cx - before, cy + gap + shift, thickness, length));
            }
            if (outline > 0)
            {
                foreach (var arm in arms) { var box = arm; box.Inflate(outline, outline); shapes.Add(new PaintedShape(box, edge, false, 0)); }
                if (s.EnableDot) AddDot(shapes, s, cx, cy, unit, outline, edge);
            }
            if (s.EnableDot) AddDot(shapes, s, cx, cy, unit, 0, s.Fill);
            foreach (var arm in arms) shapes.Add(new PaintedShape(arm, s.Fill, false, 0));
            return shapes;
        }
        private static void AddDot(List<PaintedShape> shapes, CrosshairStyle s, float cx, float cy, float scale, float grow, Color color)
        {
            float width = (float)((s.DotSize > 0 ? s.DotSize : s.Thickness) * 2 * scale);
            if (width <= 0) return;
            float before = width / 2;
            if (s.PixelSnap && !s.EnableCircleCrosshair) { width = Pixels(width); before = (float)Math.Ceiling(width / 2); }
            var box = new RectangleF(cx - before - grow, cy - before - grow, width + grow * 2, width + grow * 2);
            shapes.Add(new PaintedShape(box, color, s.DotIsCircle, 0));
        }
        private static float Pixels(float v) { return v > 0 ? Math.Max(1, Round(v)) : 0; }
        private static float Round(float v) { return (float)Math.Round(v, MidpointRounding.AwayFromZero); }

        internal static void Draw(Graphics g, CrosshairStyle s, PointF center, double scale)
        {
            var oldMode = g.SmoothingMode;
            var oldOffset = g.PixelOffsetMode;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            foreach (var shape in Geometry(s, center, scale))
            {
                if (shape.Bounds.Width <= 0 || shape.Bounds.Height <= 0 || shape.Color.A == 0) continue;
                g.SmoothingMode = shape.Ellipse || !s.PixelSnap ? SmoothingMode.AntiAlias : SmoothingMode.None;
                if (shape.Stroke > 0)
                { using (var pen = new Pen(shape.Color, shape.Stroke)) g.DrawEllipse(pen, shape.Bounds); }
                else using (var brush = new SolidBrush(shape.Color))
                {
                    if (shape.Ellipse) g.FillEllipse(brush, shape.Bounds);
                    else g.FillRectangle(brush, shape.Bounds);
                }
            }
            g.SmoothingMode = oldMode; g.PixelOffsetMode = oldOffset;
        }
        internal static Bitmap Bitmap(CrosshairStyle s, double scale, bool editing)
        {
            int size = CanvasSize(s, scale);
            var result = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                Draw(g, s, new PointF(size / 2f, size / 2f), scale);
                if (editing)
                {
                    using (var background = new SolidBrush(Color.FromArgb(30, 120, 170, 220))) g.FillRectangle(background, 0, 0, size, size);
                    using (var pen = new Pen(Color.FromArgb(210, 105, 210, 255)) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, 1, 1, size - 3, size - 3);
                }
            }
            return result;
        }
    }
}
