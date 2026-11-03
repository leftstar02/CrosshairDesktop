$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class DesktopLiveCheck {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    public delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int count);
    public static string Describe(IntPtr window) {
        uint pid; GetWindowThreadProcessId(window,out pid);
        var title=new System.Text.StringBuilder(256); GetWindowText(window,title,256);
        string process="unknown"; try { process=Process.GetProcessById((int)pid).ProcessName; } catch(ArgumentException) {}
        return window.ToString()+" / "+process+" / "+title;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string cls,string name);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    public static IntPtr Overlay() { return FindWindow(null,"星准 · 透明准星"); }
    public static bool GameBarVisible() {
        bool found=false;
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr monitor,IntPtr dc,ref Rect r,IntPtr data) {
            for(int x=1;x<=3;x++) for(int y=1;y<=3;y++) {
                uint pid; GetWindowThreadProcessId(WindowFromPoint(new Point(r.Left+(r.Right-r.Left)*x/4,r.Top+(r.Bottom-r.Top)*y/4)),out pid);
                try { if(Process.GetProcessById((int)pid).ProcessName=="GameBar") found=true; } catch(ArgumentException) {}
            }
            return true;
        },IntPtr.Zero);
        return found;
    }
    public static void Shortcut() {
        if((GetAsyncKeyState(17)&0x8000)!=0 || (GetAsyncKeyState(18)&0x8000)!=0) throw new Exception("Release Ctrl and Alt before testing.");
        keybd_event(0xA2,0,0,UIntPtr.Zero); keybd_event(0xA4,0,0,UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(0xA4,0,2,UIntPtr.Zero); keybd_event(0xA2,0,2,UIntPtr.Zero);
    }
}
'@
$overlay = [DesktopLiveCheck]::Overlay()
if ($overlay -eq [IntPtr]::Zero) { throw 'Independent overlay window not found.' }
$style = [DesktopLiveCheck]::GetWindowLong($overlay,-20)
if (($style -band 0x80000) -eq 0 -or ($style -band 0x20) -eq 0 -or ($style -band 0x8000000) -eq 0) { throw 'Overlay styles are missing layer, click-through or no-activate.' }
Write-Output 'PASS: layered, click-through and no-activate window styles'
if ([DesktopLiveCheck]::GameBarVisible()) { throw 'Game Bar is already open; close it before testing.' }
$initial = [DesktopLiveCheck]::IsWindowVisible($overlay)
$settingsPath = Join-Path (Split-Path $PSScriptRoot) 'crosshair-settings.json'
foreach ($pass in 1..2) {
    $before = [DesktopLiveCheck]::IsWindowVisible($overlay)
    $focus = [DesktopLiveCheck]::GetForegroundWindow()
    [DesktopLiveCheck]::Shortcut()
    Start-Sleep -Milliseconds 900
    $after = [DesktopLiveCheck]::IsWindowVisible($overlay)
    if ($before -eq $after) { throw "Shortcut pass $pass did not change overlay visibility." }
    if ($focus -ne [DesktopLiveCheck]::GetForegroundWindow()) { throw "Shortcut pass $pass changed foreground focus: $([DesktopLiveCheck]::Describe($focus)) -> $([DesktopLiveCheck]::Describe([DesktopLiveCheck]::GetForegroundWindow()))." }
    if ([DesktopLiveCheck]::GameBarVisible()) { throw "Shortcut pass $pass opened Game Bar." }
    $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($saved.Enabled -ne $after) { throw "Shortcut pass $pass was not saved." }
    Write-Output "PASS: Ctrl+Alt pass $pass toggled visibility to $after, preserved focus, kept Game Bar closed, and saved state"
}
if ([DesktopLiveCheck]::IsWindowVisible($overlay) -ne $initial) { throw 'Initial visibility was not restored.' }
Write-Output 'ALL LIVE DESKTOP CHECKS PASSED'
