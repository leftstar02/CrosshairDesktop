using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    // Track physical keys so releasing one Ctrl does not release the other Ctrl.
    internal sealed class ShortcutGesture
    {
        private readonly HashSet<int> down = new HashSet<int>();
        private HashSet<int> required = new HashSet<int>();
        private bool armed, blocked, onPress, avoidAltGr, modifiersOnly, allowOtherKeys;
        internal int DownCount { get { return down.Count; } }
        internal void Seed(int key) { down.Add(key); }
        internal void Clear() { down.Clear(); armed = blocked = false; }
        internal void SetBinding(KeyBinding binding)
        {
            required = binding == null ? new HashSet<int>() : new HashSet<int>(binding.Codes);
            onPress = binding != null && binding.OnPress;
            allowOtherKeys = binding != null && binding.AllowOtherKeys;
            avoidAltGr = required.Contains(17) && required.Contains(18);
            modifiersOnly = required.All(IsModifier);
            armed = false; blocked = down.Any(k => required.Contains(KeyBinding.Normalize(k)));
        }
        internal bool Feed(int key, bool pressed)
        {
            bool changed = pressed ? down.Add(key) : down.Remove(key);
            if (!changed) return false;
            var current = new HashSet<int>(down.Select(KeyBinding.Normalize));
            if (!current.Overlaps(required))
            {
                bool fire = armed && !blocked && !onPress;
                armed = blocked = false; return fire;
            }
            if (required.Count == 0 || blocked) return false;
            if ((!allowOtherKeys && current.Any(k => !required.Contains(k) && (modifiersOnly || IsModifier(k)))) || avoidAltGr && down.Contains(0xA5)) { blocked = true; return false; }
            bool complete = required.IsSubsetOf(current);
            if (onPress && !complete) armed = false;
            if (pressed && !armed && required.Contains(KeyBinding.Normalize(key)) && complete) { armed = true; return onPress; }
            return false;
        }
        private static bool IsModifier(int key) { return key == 16 || key == 17 || key == 18 || key == 91; }
    }

    internal sealed class HotkeyService : IDisposable
    {
        private readonly ShortcutGesture gesture = new ShortcutGesture();
        private readonly ShortcutGesture rawGesture = new ShortcutGesture();
        private readonly RawKeyboardWindow raw;
        private readonly KeyboardProc callback;
        private readonly Control dispatcher;
        private IntPtr hook;
        private IntPtr captureWindow;
        private KeyBinding binding;
        private bool capturing;
        private int revision;
        internal event Action Pressed;
        internal bool BackgroundInput { get { return raw.Registered; } }
        internal HotkeyService(Control dispatcher)
        {
            this.dispatcher = dispatcher; callback = Hook;
            raw = new RawKeyboardWindow();
            raw.Key += (key, down) => { if (rawGesture.Feed(key, down) && !capturing) QueuePress(); };
            raw.DevicesChanged += ResetGestures;
            ResetGestures();
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        }
        internal void SetBinding(KeyBinding value)
        {
            value.Validate();
            if (hook == IntPtr.Zero && !raw.Registered) throw new Win32Exception("快捷键监听不可用，请重新启动程序。");
            binding = value.Copy(); revision++; ResetGestures();
        }
        internal void BeginCapture(IntPtr window)
        {
            revision++; capturing = true; captureWindow = window;
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            // Optional fresh relay prevents Win/Alt system shortcuts while recording.
            // The dialog still accepts native key messages if this hook is unavailable.
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            ResetGestures();
        }
        internal void EndCapture()
        {
            revision++; capturing = false;
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            ResetGestures();
        }
        private void ResetGestures()
        {
            gesture.Clear(); rawGesture.Clear();
            for (int key = 8; key < 255; key++)
                if (key != 16 && key != 17 && key != 18 && (GetAsyncKeyState(key) & 0x8000) != 0) { gesture.Seed(key); rawGesture.Seed(key); }
            gesture.SetBinding(capturing ? null : binding); rawGesture.SetBinding(capturing ? null : binding);
        }
        private void QueuePress() { Post(() => { if (Pressed != null) Pressed(); }); }
        private void Post(Action action)
        {
            int version = revision;
            if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated)
                dispatcher.BeginInvoke((Action)(() => { if (version == revision) action(); }));
        }
        private IntPtr Hook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int key = Marshal.ReadInt32(data), msg = message.ToInt32();
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                if (down || up)
                {
                    if (capturing && GetForegroundWindow() == captureWindow)
                    {
                        int scan = Marshal.ReadInt32(data, 4), flags = Marshal.ReadInt32(data, 8);
                        PostMessage(captureWindow, (uint)msg, new IntPtr(key), new IntPtr((scan << 16) | ((flags & 1) << 24)));
                        return new IntPtr(1);
                    }
                    bool trigger = gesture.Feed(key, down);
                    // Hardware is matched only by Raw Input, avoiding duplicate toggles.
                    if (!capturing && trigger && (!raw.Registered || (Marshal.ReadInt32(data, 8) & 0x10) != 0)) QueuePress();
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        public void Dispose()
        {
            revision++;
            raw.Dispose();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
        private delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, KeyboardProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr key, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    }
}
