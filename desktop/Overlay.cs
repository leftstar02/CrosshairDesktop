using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    internal sealed class MonitorChoice
    {
        internal string Device;
        internal Rectangle Bounds;
        internal double Scale;
        internal bool Primary;
        public override string ToString() { return (Primary ? "主屏幕" : "显示器") + "  " + Bounds.Width + " × " + Bounds.Height + "  (" + (int)Math.Round(Scale * 100) + "%)"; }
        internal static List<MonitorChoice> All()
        {
            var choices = new List<MonitorChoice>();
            foreach (var screen in Screen.AllScreens)
            {
                var point = new OverlayNative.Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
                IntPtr monitor = OverlayNative.MonitorFromPoint(point, 2);
                uint x = 96, y = 96;
                try { if (OverlayNative.GetDpiForMonitor(monitor, 0, out x, out y) != 0) x = 96; } catch (EntryPointNotFoundException) { }
                choices.Add(new MonitorChoice { Device = screen.DeviceName, Bounds = screen.Bounds, Primary = screen.Primary, Scale = x / 96.0 });
            }
            return choices;
        }
    }

    internal sealed class OverlayWindow : Form
    {
        private readonly AppSettings settings;
        private readonly ImageAsset image;
        private bool editing, dragging, resizing;
        private double oldPercent;
        private Size resizeSize;
        private Rectangle renderedBounds;
        private Point dragStart, oldOffset;
        internal event Action PositionChanged;
        internal bool Editing { get { return editing; } }
        internal MonitorChoice Monitor;
        internal double RenderScale { get { return settings.ScaleWithDpi ? Monitor.Scale : 1; } }
        internal OverlayWindow(AppSettings settings, ImageAsset image)
        {
            this.settings = settings; this.image = image;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Text = "星准 · 透明准星"; AutoScaleMode = AutoScaleMode.None;
            Monitor = MonitorChoice.All().Find(m => m.Device == settings.Monitor) ?? MonitorChoice.All().Find(m => m.Primary);
            var unused = Handle;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000; return cp; }
        }
        internal void SetEditing(bool value)
        {
            editing = value; dragging = false; Capture = false;
            int style = OverlayNative.GetWindowLong(Handle, -20);
            OverlayNative.SetWindowLong(Handle, -20, value ? style & ~0x20 : style | 0x20);
            RefreshOverlay();
        }
        internal void RefreshOverlay()
        {
            if ((!settings.Enabled && !editing) || (settings.ImageMode && image.Picture == null)) { OverlayNative.ShowWindow(Handle, 0); return; }
            using (var bitmap = settings.ImageMode ? image.Render(settings.Image, RenderScale, editing) : Renderer.Bitmap(settings.Style, RenderScale, editing))
            {
                var desired = new Point(Monitor.Bounds.Left + Monitor.Bounds.Width / 2 + settings.OffsetX - bitmap.Width / 2,
                    Monitor.Bounds.Top + Monitor.Bounds.Height / 2 + settings.OffsetY - bitmap.Height / 2);
                renderedBounds = new Rectangle(desired, bitmap.Size);
                var screenDc = OverlayNative.GetDC(IntPtr.Zero); var memoryDc = OverlayNative.CreateCompatibleDC(screenDc);
                IntPtr hBitmap = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    hBitmap = bitmap.GetHbitmap(Color.FromArgb(0)); previous = OverlayNative.SelectObject(memoryDc, hBitmap);
                    var destination = new OverlayNative.Point(desired.X, desired.Y); var source = new OverlayNative.Point(0, 0);
                    var size = new OverlayNative.Size(bitmap.Width, bitmap.Height);
                    var blend = new OverlayNative.Blend { Operation = 0, Alpha = 255, Format = 1 };
                    if (!OverlayNative.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    if (previous != IntPtr.Zero) OverlayNative.SelectObject(memoryDc, previous);
                    if (hBitmap != IntPtr.Zero) OverlayNative.DeleteObject(hBitmap);
                    OverlayNative.DeleteDC(memoryDc); OverlayNative.ReleaseDC(IntPtr.Zero, screenDc);
                }
                // Native non-activating show avoids WinForms activating the overlay on later toggles.
                if (!OverlayNative.IsWindowVisible(Handle)) OverlayNative.ShowWindow(Handle, 4);
                OverlayNative.SetWindowPos(Handle, new IntPtr(-1), desired.X, desired.Y, bitmap.Width, bitmap.Height, 0x10 | 0x40);
            }
        }
        internal void KeepOnTop()
        {
            if (OverlayNative.IsWindowVisible(Handle)) OverlayNative.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && !editing) { m.Result = new IntPtr(-1); return; }
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!editing || e.Button != MouseButtons.Left) return;
            dragging = true; dragStart = Cursor.Position; oldOffset = new Point(settings.OffsetX, settings.OffsetY); Capture = true;
            resizing = settings.ImageMode && OnResizeHandle(Cursor.Position);
            oldPercent = settings.Image.Percent; resizeSize = image.DisplaySize(settings.Image, RenderScale);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
            {
                Cursor = editing && settings.ImageMode && OnResizeHandle(Cursor.Position) ? Cursors.SizeNWSE : editing ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            var current = Cursor.Position;
            if (resizing)
            {
                double w = Math.Max(16, resizeSize.Width), h = Math.Max(16, resizeSize.Height);
                double factor = 1 + 2 * ((current.X - dragStart.X) * w + (current.Y - dragStart.Y) * h) / (w * w + h * h);
                double max = Math.Min(1000, 204800.0 / (Math.Max(image.Picture.Width, image.Picture.Height) * RenderScale));
                settings.Image.Percent = Math.Max(1, Math.Min(max, oldPercent * factor));
            }
            else
            {
                settings.OffsetX = Math.Max(-16000, Math.Min(16000, oldOffset.X + current.X - dragStart.X));
                settings.OffsetY = Math.Max(-16000, Math.Min(16000, oldOffset.Y + current.Y - dragStart.Y));
            }
            RefreshOverlay(); if (PositionChanged != null) PositionChanged();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
        private bool OnResizeHandle(Point point) { return point.X >= renderedBounds.Right - 20 && point.Y >= renderedBounds.Bottom - 20; }
    }

    internal static class OverlayNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width, Height; internal Size(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr w, IntPtr dc, ref Point dst, ref Size size, IntPtr sourceDc, ref Point source, int color, ref Blend blend, int flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    }
}
