using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { OverlayNative.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { OverlayNative.SetProcessDPIAware(); }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            int waitIndex = Array.IndexOf(args, "--wait-for-exit"), previousPid;
            if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out previousPid) && previousPid > 0)
            {
                try
                {
                    using (var previous = System.Diagnostics.Process.GetProcessById(previousPid))
                        if (!previous.WaitForExit(10000)) { MessageBox.Show("原程序尚未退出，请稍后再重试管理员模式。", "星准"); return; }
                }
                catch (ArgumentException) { }
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\CrosshairDesktop.Standalone", out first))
            {
                if (!first)
                {
                    var window = FindWindow(null, "星准 · Crosshair Desktop");
                    if (window != IntPtr.Zero) PostMessage(window, 0x8001, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                try
                {
                    var store = new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crosshair-settings.json"));
                    Application.Run(new MainForm(store, args.Contains("--tray")));
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "星准启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        internal static bool IsAdministrator
        {
            get
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr w, uint msg, IntPtr wp, IntPtr lp);
    }
}
