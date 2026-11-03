using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    // RIDEV_INPUTSINK receives keyboard events while a different application owns focus.
    internal sealed class RawKeyboardWindow : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Device { internal ushort Page, Usage; internal uint Flags; internal IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Header { internal uint Type, Size; internal IntPtr Device, Parameter; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Keyboard { internal ushort MakeCode, Flags, Reserved, VKey; internal uint Message, Extra; }
        internal bool Registered { get; private set; }
        internal event Action<int, bool> Key;
        internal event Action DevicesChanged;
        private IntPtr buffer;
        private const int BufferSize = 256;
        internal RawKeyboardWindow()
        {
            CreateHandle(new CreateParams { Caption = "CrosshairDesktop.BackgroundKeyboard", Parent = new IntPtr(-3) });
            buffer = Marshal.AllocHGlobal(BufferSize);
            var devices = new[] { new Device { Page = 1, Usage = 6, Flags = 0x100 | 0x2000, Target = Handle } };
            Registered = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(Device)));
        }
        internal static int Decode(ushort virtualKey, ushort makeCode, ushort flags)
        {
            if (makeCode == 0xFF || virtualKey >= 0xFF || virtualKey < 8) return 0;
            // E1's synthetic Ctrl belongs to the Pause scan sequence.
            if (virtualKey == 17 && (flags & 4) != 0) return 0;
            if (virtualKey == 16) return makeCode == 0x36 ? 0xA1 : 0xA0;
            if (virtualKey == 17) return (flags & 2) != 0 ? 0xA3 : 0xA2;
            if (virtualKey == 18) return (flags & 2) != 0 ? 0xA5 : 0xA4;
            return virtualKey;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0xFF && buffer != IntPtr.Zero)
            {
                uint size = BufferSize, headerSize = (uint)Marshal.SizeOf(typeof(Header));
                uint read = GetRawInputData(message.LParam, 0x10000003, buffer, ref size, headerSize);
                if (read != uint.MaxValue && read >= headerSize + Marshal.SizeOf(typeof(Keyboard)))
                {
                    var header = (Header)Marshal.PtrToStructure(buffer, typeof(Header));
                    // Software-generated input is handled by the injected-input hook path only.
                    if (header.Type == 1 && header.Device != IntPtr.Zero)
                    {
                        var keyboard = (Keyboard)Marshal.PtrToStructure(IntPtr.Add(buffer, (int)headerSize), typeof(Keyboard));
                        int key = Decode(keyboard.VKey, keyboard.MakeCode, keyboard.Flags);
                        if (key != 0 && Key != null) Key(key, (keyboard.Flags & 1) == 0);
                    }
                }
            }
            else if (message.Msg == 0xFE && DevicesChanged != null) DevicesChanged();
            // DefWindowProc must still see foreground WM_INPUT to perform its cleanup.
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            if (Registered)
            {
                var devices = new[] { new Device { Page = 1, Usage = 6, Flags = 1, Target = IntPtr.Zero } };
                RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(Device))); Registered = false;
            }
            DestroyHandle();
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] Device[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    }
}
