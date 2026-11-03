param([switch]$HoldOtherKeys)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HotkeyTimingCheck {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
    public static IntPtr Overlay() { return FindWindow(null,"星准 · 透明准星"); }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    public static void Send(int key,bool down) { keybd_event((byte)key,0,down?0u:2u,UIntPtr.Zero); }
    public static bool Held(int key) { return (GetAsyncKeyState(key)&0x8000)!=0; }
}
'@
$settingsPath = Join-Path (Split-Path $PSScriptRoot) 'crosshair-settings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$binding = $settings.Shortcut
$keys = @()
if ($binding.Ctrl) { $keys += 0xA2 }
if ($binding.Alt) { $keys += 0xA4 }
if ($binding.Shift) { $keys += 0xA0 }
if ($binding.Win) { $keys += 0x5B }
if ($binding.Key) { $keys += [int]$binding.Key }
foreach ($key in $binding.ExtraKeys) { $keys += [int]$key }
if ($keys.Count -eq 0) { throw 'No shortcut is configured.' }
foreach ($key in $keys) { if ([HotkeyTimingCheck]::Held($key)) { throw 'Release the shortcut before testing.' } }
$overlay = [HotkeyTimingCheck]::Overlay()
if ($overlay -eq [IntPtr]::Zero) { throw 'Overlay not found.' }
$initial = [HotkeyTimingCheck]::IsWindowVisible($overlay)
$onPress = $binding.Trigger -eq 'Press' -or (-not $binding.Trigger -and $binding.Key -ne 0)
$otherKeys = @()
if ($HoldOtherKeys) {
    if (-not $binding.AllowOtherKeys) { throw 'Enable AllowOtherKeys before running the held-key test.' }
    $otherKeys = @(0x57,0xA0) | Where-Object { $keys -notcontains $_ }
    foreach ($key in $otherKeys) {
        if ([HotkeyTimingCheck]::Held($key)) { throw 'Release movement keys before testing.' }
    }
}
try {
    foreach ($key in $otherKeys) { [HotkeyTimingCheck]::Send($key,$true) }
    foreach ($pass in 1..2) {
        $before = [HotkeyTimingCheck]::IsWindowVisible($overlay)
        foreach ($key in $keys) { [HotkeyTimingCheck]::Send($key,$true) }
        Start-Sleep -Milliseconds 250
        $during = [HotkeyTimingCheck]::IsWindowVisible($overlay)
        if ($during -ne ($before -xor $onPress)) { throw 'Shortcut did not respect its configured down/up timing.' }
        foreach ($repeat in 1..5) { [HotkeyTimingCheck]::Send($keys[-1],$true) }
        Start-Sleep -Milliseconds 150
        if ([HotkeyTimingCheck]::IsWindowVisible($overlay) -ne $during) { throw 'Holding the shortcut toggled repeatedly.' }
        for ($index = $keys.Count - 1; $index -ge 0; $index--) { [HotkeyTimingCheck]::Send($keys[$index],$false) }
        Start-Sleep -Milliseconds 650
        $after = [HotkeyTimingCheck]::IsWindowVisible($overlay)
        if ($before -eq $after) { throw 'Shortcut did not toggle exactly once.' }
        $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($saved.Enabled -ne $after) { throw 'Shortcut visibility change was not saved.' }
        Write-Output "PASS: cycle $pass respected trigger=$($binding.Trigger), ignored repeats, toggled once and saved state"
    }
    if ([HotkeyTimingCheck]::IsWindowVisible($overlay) -ne $initial) { throw 'Original visibility was not restored.' }
    foreach ($key in $otherKeys) { [HotkeyTimingCheck]::Send($key,$false) }
    Start-Sleep -Milliseconds 100
    if ([HotkeyTimingCheck]::IsWindowVisible($overlay) -ne $initial) { throw 'Releasing unrelated keys triggered an extra toggle.' }
    if ($HoldOtherKeys) { Write-Output 'PASS: movement keys stayed held across both cycles; releasing them did not toggle' }
    Write-Output 'ALL LIVE HOTKEY TIMING CHECKS PASSED'
} finally {
    foreach ($key in $keys) { [HotkeyTimingCheck]::Send($key,$false) }
    foreach ($key in $otherKeys) { [HotkeyTimingCheck]::Send($key,$false) }
    Start-Sleep -Milliseconds 100
    if ([HotkeyTimingCheck]::IsWindowVisible($overlay) -ne $initial) {
        foreach ($key in $keys) { [HotkeyTimingCheck]::Send($key,$true) }
        for ($index = $keys.Count - 1; $index -ge 0; $index--) { [HotkeyTimingCheck]::Send($keys[$index],$false) }
    }
}
