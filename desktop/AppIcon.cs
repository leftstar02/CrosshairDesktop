using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CrosshairDesktop
{
    internal static class AppIcon
    {
        internal static Icon Create()
        {
            // Draw for notification-area sizes directly. A preview-sized crosshair
            // leaves too much empty space when Windows reduces it to 16–20 pixels.
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var background = new SolidBrush(Color.FromArgb(24, 33, 46)))
                    graphics.FillEllipse(background, 2, 2, 28, 28);
                using (var pen = new Pen(Color.FromArgb(100, 220, 194), 3))
                {
                    graphics.DrawEllipse(pen, 7, 7, 18, 18);
                    graphics.DrawLine(pen, 16, 1, 16, 10);
                    graphics.DrawLine(pen, 16, 22, 16, 31);
                    graphics.DrawLine(pen, 1, 16, 10, 16);
                    graphics.DrawLine(pen, 22, 16, 31, 16);
                }
                using (var center = new SolidBrush(Color.White)) graphics.FillEllipse(center, 14, 14, 4, 4);
                var handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}
