using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CrosshairDesktop
{
    public sealed class ImageCrosshair
    {
        public string File = "", Name = "";
        public double Percent = 100;
        public int Alpha = 255;
        public ImageCrosshair Copy() { return (ImageCrosshair)MemberwiseClone(); }
        public void Validate()
        {
            if (double.IsNaN(Percent) || double.IsInfinity(Percent)) throw new FormatException("图片缩放比例无效。");
            Percent = Math.Max(1, Math.Min(1000, Percent));
            Alpha = Math.Max(0, Math.Min(255, Alpha));
        }
    }

    internal sealed class ImageAsset : IDisposable
    {
        internal Bitmap Picture { get; private set; }
        internal void Load(string path)
        {
            Bitmap next = Read(path);
            if (Picture != null) Picture.Dispose();
            Picture = next;
        }
        internal static Bitmap Read(string path)
        {
            if (!System.IO.File.Exists(path)) throw new IOException("找不到准星图片，请重新选择图片。");
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new FormatException("图片文件不能超过 20 MB。");
            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                using (var source = System.Drawing.Image.FromStream(stream, true, true))
                {
                    if (source.Width > 8192 || source.Height > 8192 || (long)source.Width * source.Height > 16000000)
                        throw new FormatException("图片尺寸过大，请使用不超过 1600 万像素、单边不超过 8192 像素的图片。");
                    var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                    result.SetResolution(96, 96);
                    using (var g = Graphics.FromImage(result))
                    {
                        // DrawImageUnscaled uses physical size and can crop when image and desktop DPI differ.
                        g.PageUnit = GraphicsUnit.Pixel;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    return result;
                }
            }
            catch (ArgumentException) { throw new FormatException("无法读取图片，请选择 PNG、JPG、BMP 或 GIF 图片。"); }
            catch (OutOfMemoryException) { throw new FormatException("无法解码图片或图片过大。"); }
        }
        internal static ImageCrosshair Import(string source, string root)
        {
            using (var picture = Read(source))
            {
                string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder);
                string relative = Path.Combine("images", Guid.NewGuid().ToString("N") + ".png");
                picture.Save(Path.Combine(root, relative), ImageFormat.Png);
                return new ImageCrosshair { File = relative, Name = Path.GetFileName(source),
                    Percent = Math.Min(100, 12800.0 / Math.Max(picture.Width, picture.Height)) };
            }
        }
        internal Size DisplaySize(ImageCrosshair style, double dpi)
        {
            if (Picture == null) return Size.Empty;
            double factor = style.Percent / 100 * dpi;
            factor = Math.Min(factor, 2048.0 / Math.Max(Picture.Width, Picture.Height));
            return new Size(Math.Max(1, (int)Math.Round(Picture.Width * factor)), Math.Max(1, (int)Math.Round(Picture.Height * factor)));
        }
        internal void Draw(Graphics g, ImageCrosshair style, PointF center, double dpi)
        {
            if (Picture == null) return;
            Size size = DisplaySize(style, dpi);
            var state = g.Save();
            try
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = style.Alpha / 255f });
                    g.DrawImage(Picture, new Rectangle((int)Math.Round(center.X - size.Width / 2f), (int)Math.Round(center.Y - size.Height / 2f), size.Width, size.Height),
                        0, 0, Picture.Width, Picture.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            finally { g.Restore(state); }
        }
        internal Bitmap Render(ImageCrosshair style, double dpi, bool editing)
        {
            Size size = DisplaySize(style, dpi);
            var result = new Bitmap(Math.Max(64, size.Width + 24), Math.Max(64, size.Height + 24), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result))
            {
                if (editing) using (var fill = new SolidBrush(Color.FromArgb(30, 120, 170, 220))) g.FillRectangle(fill, 0, 0, result.Width, result.Height);
                Draw(g, style, new PointF(result.Width / 2f, result.Height / 2f), dpi);
                if (editing)
                {
                    using (var pen = new Pen(Color.FromArgb(230, 105, 210, 255)) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, 1, 1, result.Width - 3, result.Height - 3);
                    using (var fill = new SolidBrush(Color.FromArgb(255, 100, 220, 194))) g.FillRectangle(fill, result.Width - 15, result.Height - 15, 13, 13);
                }
            }
            return result;
        }
        public void Dispose() { if (Picture != null) { Picture.Dispose(); Picture = null; } }
    }
}
