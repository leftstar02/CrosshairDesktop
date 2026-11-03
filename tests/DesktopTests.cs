using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace CrosshairDesktop
{
    internal static class DesktopTests
    {
        private static int count;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            count++; Console.WriteLine("PASS: " + name);
        }
        private static bool Near(double a, double b) { return Math.Abs(a - b) < .0001; }
        private static bool Reject(Action action) { try { action(); return false; } catch (FormatException) { return true; } }
        [STAThread]
        public static int Main()
        {
            try
            {
                string note;
                var original = new CrosshairStyle();
                const string defaultCode = "MBCH-AQD_AP__AAAA6AMsAWQAAAAAAAB6";
                Check(ShareCodes.Export(original) == defaultCode, "MBCH wire format matches independent fixture");
                var imported = ShareCodes.Import(defaultCode, 1080, 1, out note);
                Check(Near(imported.Size, 10) && Near(imported.Gap, 1) && imported.Alpha == 255, "existing Murbong share code imports");
                var detailed = new CrosshairStyle { Size = 12.5, Thickness = .5, Gap = -8, Outline = 1.5, DotSize = 2.5, Color = "#1256AA", Alpha = 140, OutlineColor = "#80332211", EnableDot = true, DotIsCircle = true, EnableTShape = true, PixelSnap = true };
                var roundTrip = ShareCodes.Import(ShareCodes.Export(detailed), 2160, 1.5, out note);
                Check(Near(roundTrip.Size, 12.5) && Near(roundTrip.Thickness, .5) && Near(roundTrip.Gap, -8) && roundTrip.Alpha == 140 && roundTrip.Color == "#1256AA" && roundTrip.OutlineColor == "#80332211" && roundTrip.EnableDot && roundTrip.DotIsCircle && roundTrip.EnableTShape && roundTrip.PixelSnap, "sharing retains fractional sizes, negative gap, alpha, colors and flags");
                Check(Reject(() => { string message; ShareCodes.Import(defaultCode.Substring(0, defaultCode.Length - 1) + "B", 1080, 1, out message); }), "damaged Murbong checksum rejected");
                Check(Reject(() => { string message; ShareCodes.Import("MBCH-!", 1080, 1, out message); }), "malformed Murbong encoding rejected");
                string legacyJson = "{\"Color\":{\"A\":255,\"R\":12,\"G\":34,\"B\":56},\"Alpha\":120,\"Size\":6,\"Thickness\":0.5,\"Gap\":-3,\"Outline\":1,\"EnableDot\":true,\"DotIsCircle\":true,\"EnableTShape\":true,\"EnableCircleCrosshair\":false}";
                var old = ShareCodes.Import(legacyJson, 1080, 1, out note);
                Check(old.Color == "#FF0C2238" && old.Alpha == 120 && Near(old.Thickness, .5) && old.EnableTShape && old.EnableDot && old.DotIsCircle, "Murbong 1.x JSON imports with Windows color object");
                var jsonRound = ShareCodes.Import(SettingsStore.Json.Serialize(detailed), 1080, 1, out note);
                Check(jsonRound.OutlineColor == detailed.OutlineColor && jsonRound.Gap == detailed.Gap, "desktop style JSON round trip");
                var portable = ShareCodes.Import(ShareCodes.ExportJson(detailed), 1080, 1, out note);
                Check(portable.OutlineColor == detailed.OutlineColor && portable.DotIsCircle && portable.Alpha == detailed.Alpha, "exported JSON uses Murbong-compatible color objects");
                Check(Reject(() => { string message; ShareCodes.Import("{\"hello\":1}", 1080, 1, out message); }), "unrelated JSON is rejected");
                const string currentCs = "CSUotJKptbhTQBTQbvXGhitvBUyEB4VwKn3OiyYM6ifykW";
                var cs = ShareCodes.Import(currentCs, 1080, 1, out note);
                Check(Near(cs.Size, 6) && Near(cs.Thickness, 2) && Near(cs.Gap, 1) && Near(cs.Outline, 1) && cs.Alpha == 160 && cs.EnableDot && cs.EnableTShape && cs.PixelSnap && cs.Color == "#0B1621" && cs.OutlineColor == "#C828323C", "current CS2 format decodes shape, alpha and custom outline");
                var csScaled = ShareCodes.Import(currentCs, 2160, 1.5, out note);
                Check(Near(csScaled.Size, 8) && Near(csScaled.Thickness, 8.0 / 3) && Near(csScaled.Outline, 2.0 / 3), "CS2 resolution and Windows DPI conversion");
                var legacy = ShareCodes.Import("CSGO-45QM6-z9tL9-rephu-OLz8G-E43JE", 960, 1, out note);
                Check(Near(legacy.Size, 2) && Near(legacy.Thickness, 1) && Near(legacy.Gap, -1) && Near(legacy.Outline, 1) && legacy.Color == "#37424D" && legacy.EnableDot && legacy.EnableTShape && legacy.Alpha == 128, "legacy CSGO code imports");
                Check(Reject(() => { string message; ShareCodes.Import(currentCs.Substring(0, currentCs.Length - 1) + "A", 1080, 1, out message); }), "damaged CS2 code rejected");
                original.Size = 500; original.Gap = -200; original.Alpha = -1; original.Validate();
                Check(original.Size == 100 && original.Gap == -100 && original.Alpha == 0, "external sizes are constrained");
                Check(Reject(() => { new CrosshairStyle { Size = double.NaN }.Validate(); }), "non-finite geometry rejected");
                Check(CrosshairStyle.ParseColor("#80332211").A == 128, "outline alpha parsing");
                Check(Reject(() => CrosshairStyle.ParseColor("#xyz")), "invalid color rejected");
                var center = new PointF(100, 100);
                var cross = Renderer.Geometry(new CrosshairStyle(), center, 1);
                Check(cross.Count == 4 && cross[0].Bounds == new RectangleF(108, 97, 20, 6), "cross arms retain Murbong dimensions and base gap");
                Check(Renderer.Geometry(new CrosshairStyle { EnableTShape = true }, center, 1).Count == 3, "T shape omits top arm");
                Check(Renderer.Geometry(new CrosshairStyle { Thickness = 0, Outline = 1 }, center, 1).Take(4).All(p => p.Bounds.Height > 0 && p.Bounds.Width > 0), "outline remains visible with zero fill thickness");
                var ring = Renderer.Geometry(new CrosshairStyle { EnableCircleCrosshair = true, Outline = 1, EnableDot = true, DotIsCircle = true }, center, 1);
                Check(ring.Count == 3 && ring.All(p => p.Ellipse) && ring[1].Stroke == 5 && ring[2].Stroke == 3, "circle, dot and outline geometry");
                var odd = Renderer.Geometry(new CrosshairStyle { Size = 3, Thickness = .5, Gap = 1, PixelSnap = true }, center, 1);
                Check(odd.All(p => p.Bounds.X == (int)p.Bounds.X && p.Bounds.Y == (int)p.Bounds.Y && p.Bounds.Width == (int)p.Bounds.Width) && odd[0].Bounds.Y == 99 && odd[0].Bounds.Height == 1, "odd-width pixel snapping lands on pixel boundaries");
                using (var bitmap = Renderer.Bitmap(new CrosshairStyle { Alpha = 0 }, 1, false))
                    Check(bitmap.GetPixel(bitmap.Width / 2 + 10, bitmap.Height / 2).A == 0, "zero alpha stays transparent");
                using (var bitmap = Renderer.Bitmap(new CrosshairStyle { Size = 0, EnableDot = true, Thickness = 2, PixelSnap = true }, 1, false))
                    Check(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).G == 255 && bitmap.GetPixel(0, 0).A == 0, "dot-only rendering and transparent background");
                TestShortcuts();
                TestRecording();
                TestImmediateTrigger();
                TestBackgroundKeyboard();
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-test-settings.json");
                try
                {
                    var store = new SettingsStore(path); var settings = new AppSettings { Style = detailed, Enabled = false, Monitor = "test-monitor", OffsetX = 29, OffsetY = -14 };
                    settings.Presets.Add(new SavedPreset { Name = "my preset", Code = ShareCodes.Export(detailed) }); store.Save(settings);
                    var loaded = store.Load();
                    Check(!loaded.Enabled && loaded.OffsetX == 29 && loaded.OffsetY == -14 && loaded.Monitor == "test-monitor" && loaded.Presets.Count == 1 && Near(loaded.Style.Gap, -8), "position, visibility, style and presets survive restart");
                    settings.Enabled = true; store.Save(settings); File.WriteAllText(path, "broken");
                    var restored = store.Load();
                    Check(!restored.Enabled && restored.Presets.Count == 1 && store.Warning != null, "corrupt settings recover previous complete backup");
                }
                finally { foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
                TestImages();
                MakeContactSheet();
                Console.WriteLine("ALL " + count + " DESKTOP CHECKS PASSED"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void TestShortcuts()
        {
            var gesture = new ShortcutGesture(); var binding = new KeyBinding(); binding.Validate(); gesture.SetBinding(binding);
            Check(!gesture.Feed(0xA2, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, false) && gesture.Feed(0xA2, false) && !gesture.Feed(0xA2, false), "modifier shortcut fires once after full release");
            gesture.Feed(0xA2, true); gesture.Feed(0xA4, true); gesture.Feed(0x41, true); gesture.Feed(0x41, false); gesture.Feed(0xA4, false);
            Check(!gesture.Feed(0xA2, false), "Ctrl+Alt+other keys cancel release shortcut");
            gesture.Feed(0xA2, true); gesture.Feed(0xA5, true); gesture.Feed(0xA5, false);
            Check(!gesture.Feed(0xA2, false), "AltGr does not trigger Ctrl+Alt");
            binding.Trigger = "Press"; gesture.SetBinding(binding);
            Check(!gesture.Feed(0xA4, true) && gesture.Feed(0xA2, true) && !gesture.Feed(0xA2, true) && !gesture.Feed(0xA2, false) && !gesture.Feed(0xA4, false), "press shortcut fires on completion only, ignoring repeat and release");
            Check(!gesture.Feed(0xA2, true) && gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, false) && !gesture.Feed(0xA2, false), "press shortcut rearms for the next chord");
            gesture.Feed(0x41, true); gesture.Feed(0xA2, true); gesture.Feed(0xA4, true);
            Check(!gesture.Feed(0x41, false), "releasing an extra key never synthesizes a press");
            gesture.Feed(0xA4, false); gesture.Feed(0xA2, false);
            binding = KeyBinding.FromKeys(new[] { 0x77 }, "Press"); binding.Validate(); gesture.SetBinding(binding);
            Check(binding.Label == "F8" && gesture.Feed(0x77, true) && !gesture.Feed(0x77, true) && !gesture.Feed(0x77, false), "standalone function key works without modifiers or repeat");
            binding.Trigger = "Release"; gesture.SetBinding(binding);
            Check(!gesture.Feed(0x77, true) && !gesture.Feed(0x77, true) && gesture.Feed(0x77, false), "standalone function key supports release trigger");
            gesture.Feed(0x57, true);
            Check(!gesture.Feed(0x77, true) && gesture.Feed(0x77, false) && !gesture.Feed(0x57, false), "game movement keys can remain held during ordinary shortcut release");
            binding = KeyBinding.FromKeys(new[] { 17, 0x77 }, "Press"); gesture.SetBinding(binding);
            gesture.Feed(0xA2, true);
            Check(gesture.Feed(0x77, true) && !gesture.Feed(0x77, false) && gesture.Feed(0x77, true) && !gesture.Feed(0x77, false) && !gesture.Feed(0xA2, false), "press mode can repeat full presses while a modifier remains held");
            binding = KeyBinding.FromKeys(new[] { 0xA2, 0xA0, 0xDB }, "Release"); binding.Validate(); gesture.SetBinding(binding);
            gesture.Feed(0xA0, true); gesture.Feed(0xDB, true); gesture.Feed(0xA2, true); gesture.Feed(0xDB, false); gesture.Feed(0xA0, false);
            Check(binding.Label == "Ctrl + Shift + [" && gesture.Feed(0xA2, false), "symbol key supports arbitrary press order and release order");
            binding = KeyBinding.FromKeys(new[] { 0x51, 0x45 }, "Press"); binding.Validate(); gesture.SetBinding(binding);
            Check(!gesture.Feed(0x51, true) && gesture.Feed(0x45, true) && !gesture.Feed(0x45, false) && !gesture.Feed(0x51, false), "multiple ordinary keys form a chord without modifiers");
            binding = KeyBinding.FromKeys(new[] { 17 }, "Release"); binding.Validate(); gesture.SetBinding(binding);
            gesture.Feed(0xA2, true); gesture.Feed(0xA3, true);
            Check(!gesture.Feed(0xA2, false) && gesture.Feed(0xA3, false), "single modifier tracks left and right physical keys separately");
            gesture.Feed(0xA2, true); binding.Trigger = "Press"; gesture.SetBinding(binding);
            Check(!gesture.Feed(0xA2, true) && !gesture.Feed(0xA2, false) && gesture.Feed(0xA2, true) && !gesture.Feed(0xA2, false), "applying while held requires a fresh chord");
            gesture.SetBinding(null); Check(!gesture.Feed(0x77, true) && !gesture.Feed(0x77, false), "recording suspension prevents normal shortcut activation");
            binding = KeyBinding.FromKeys(new[] { 91, 0x70 }, "Press"); binding.Validate(); gesture.SetBinding(binding);
            Check(!gesture.Feed(92, true) && gesture.Feed(0x70, true) && !gesture.Feed(0x70, false) && !gesture.Feed(92, false), "Win modifier accepts either Windows key");
            foreach (int key in new[] { 9, 13, 27, 32, 37, 45, 46, 0x60, 0x6B, 0xBA, 0xBF, 0xC0 }) KeyBinding.FromKeys(new[] { key }, "Press").Validate();
            Check(true, "navigation, whitespace, escape, numpad and punctuation keys are accepted");
            Check(Reject(() => new KeyBinding { Ctrl = false, Alt = false, Key = 0 }.Validate()), "empty shortcut is rejected");
            Check(Reject(() => KeyBinding.FromKeys(new[] { 91, 0x4C }, "Press").Validate()) && Reject(() => KeyBinding.FromKeys(new[] { 17, 18, 46 }, "Release").Validate()), "secure system shortcuts are rejected");
            Check(Reject(() => KeyBinding.FromKeys(new[] { 1 }, "Press").Validate()) && Reject(() => KeyBinding.FromKeys(new[] { 0x77 }, "invalid").Validate()), "unsupported key and trigger values are rejected");
            var oldModifiers = SettingsStore.Json.Deserialize<KeyBinding>("{\"Ctrl\":true,\"Alt\":true,\"Key\":0}"); oldModifiers.Validate();
            var oldRegular = SettingsStore.Json.Deserialize<KeyBinding>("{\"Ctrl\":true,\"Alt\":true,\"Key\":119}"); oldRegular.Validate();
            Check(!oldModifiers.OnPress && oldRegular.OnPress, "existing modifier and registered shortcuts retain their trigger timing");
            var saved = KeyBinding.FromKeys(new[] { 17, 0x51, 0x45 }, "Release");
            var copied = SettingsStore.Json.Deserialize<KeyBinding>(SettingsStore.Json.Serialize(saved)); copied.Validate();
            Check(copied.Label == saved.Label && copied.ExtraKeys.Count == 1 && !copied.OnPress, "arbitrary chord and trigger survive serialization");
            TestOtherHeldKeys();
        }
        private static void TestOtherHeldKeys()
        {
            var gesture = new ShortcutGesture();
            var binding = new KeyBinding { AllowOtherKeys = true, Trigger = "Release" };
            gesture.SetBinding(binding);
            gesture.Feed(0x57, true); gesture.Feed(0xA0, true);
            Check(!gesture.Feed(0xA2, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, false) && gesture.Feed(0xA2, false), "permissive release fires while W and Shift remain held");
            Check(!gesture.Feed(0xA2, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0xA2, false) && gesture.Feed(0xA4, false), "permissive release rearms without releasing movement keys");
            Check(!gesture.Feed(0x57, false) && !gesture.Feed(0xA0, false), "releasing unrelated keys never toggles");
            gesture.SetBinding(binding); gesture.Feed(0xA2, true); gesture.Feed(0xA4, true);
            Check(!gesture.Feed(0x41, true) && !gesture.Feed(0xA4, false) && gesture.Feed(0xA2, false) && !gesture.Feed(0x41, false), "extra keys pressed after chord do not cancel permissive release");
            binding.Trigger = "Press"; gesture.SetBinding(binding); gesture.Feed(0x57, true); gesture.Feed(0xA0, true);
            Check(!gesture.Feed(0xA2, true) && gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0x41, true) && !gesture.Feed(0x41, false), "permissive press toggles only on chord completion and ignores repeats");
            gesture.Feed(0xA4, false); gesture.Feed(0xA2, false); gesture.Feed(0x57, false); gesture.Feed(0xA0, false);
            binding.Trigger = "Release"; gesture.SetBinding(binding); gesture.Feed(0xA2, true); gesture.Feed(0xA5, true); gesture.Feed(0xA5, false);
            Check(!gesture.Feed(0xA2, false), "permissive matching retains AltGr protection");
            binding.AllowOtherKeys = false; gesture.SetBinding(binding); gesture.Feed(0x57, true); gesture.Feed(0xA2, true); gesture.Feed(0xA4, true); gesture.Feed(0xA4, false);
            Check(!gesture.Feed(0xA2, false) && !gesture.Feed(0x57, false), "disabling permissive matching restores legacy modifier behavior");
            binding = KeyBinding.FromKeys(new[] { 0x77 }, "Press"); binding.AllowOtherKeys = true; gesture.SetBinding(binding); gesture.Feed(0xA0, true);
            Check(gesture.Feed(0x77, true) && !gesture.Feed(0x77, false) && !gesture.Feed(0xA0, false), "permissive matching accepts extra modifiers with ordinary shortcut keys");
            var restored = SettingsStore.Json.Deserialize<KeyBinding>(SettingsStore.Json.Serialize(binding)); restored.Validate();
            Check(restored.AllowOtherKeys && restored.Copy().AllowOtherKeys && !new KeyBinding().AllowOtherKeys, "other-key preference persists and old configurations default to legacy behavior");
        }
        private static void TestRecording()
        {
            var recorder = new ShortcutRecorder();
            Check(!recorder.Feed(0xA2, false) && !recorder.Ready, "recording ignores stale releases from before opening");
            recorder.Feed(0xA2, true); recorder.Feed(0x77, true); recorder.Feed(0x77, false);
            Check(!recorder.Ready && recorder.Candidate.Label == "Ctrl + F8", "recording waits for its own chord to release");
            recorder.Feed(0xA2, false); Check(recorder.Ready, "recording completes independently of global hook state");
            recorder.Feed(9, true); recorder.Feed(9, false); Check(recorder.Ready && recorder.Candidate.Label == "Tab", "new recording replaces previous chord including Tab");
            recorder.Feed(27, true); recorder.Reset(); recorder.Feed(27, false);
            Check(!recorder.Ready && recorder.Candidate == null, "focus reset clears unfinished recording");
            using (var dispatcher = new System.Windows.Forms.Form())
            {
                var unused = dispatcher.Handle;
                using (var hotkeys = new HotkeyService(dispatcher))
                using (var dialog = new HotkeyCaptureDialog(hotkeys))
                {
                    dialog.Opacity = 0; dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
                    Exception failure = null;
                    dialog.Shown += (s, e) => dialog.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            foreach (int key in new[] { 17, 18, 119 })
                            {
                                var msg = System.Windows.Forms.Message.Create(dialog.Handle, 0x100, new IntPtr(key), IntPtr.Zero);
                                Check(System.Windows.Forms.Application.FilterMessage(ref msg), "dialog consumes recorded key down " + key);
                            }
                            foreach (int key in new[] { 119, 18, 17 })
                            {
                                var msg = System.Windows.Forms.Message.Create(dialog.Handle, 0x101, new IntPtr(key), IntPtr.Zero);
                                System.Windows.Forms.Application.FilterMessage(ref msg);
                            }
                            Check(dialog.Result != null && dialog.Result.Label == "Ctrl + Alt + F8", "real dialog filter records a complete chord");
                            dialog.DialogResult = System.Windows.Forms.DialogResult.OK;
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { dialog.Close(); }
                    }));
                    dialog.ShowDialog(); if (failure != null) throw failure;
                    Check(dialog.Result != null, "accepted recording survives dialog closure");
                }
            }
        }
        private static void TestImmediateTrigger()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-test-settings.json");
            var store = new SettingsStore(path); store.Save(new AppSettings { Enabled = false });
            try
            {
                using (var form = new MainForm(store, false))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    Exception failure = null;
                    form.Shown += (s, e) => form.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                            var mode = (System.Windows.Forms.ComboBox)typeof(MainForm).GetField("triggerMode", flags).GetValue(form);
                            var hotkeys = (HotkeyService)typeof(MainForm).GetField("hotkeys", flags).GetValue(form);
                            mode.SelectedIndex = 0;
                            var active = (KeyBinding)typeof(HotkeyService).GetField("binding", flags).GetValue(hotkeys);
                            Check(active.OnPress, "changing trigger dropdown immediately updates active hotkey without Apply");
                            var gesture = (ShortcutGesture)typeof(HotkeyService).GetField("rawGesture", flags).GetValue(hotkeys);
                            gesture.Clear(); gesture.SetBinding(active);
                            Check(!gesture.Feed(0xA2, true) && gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, false) && !gesture.Feed(0xA2, false), "dropdown press mode toggles on actual matcher down edge only");
                            typeof(MainForm).GetMethod("Flush", flags).Invoke(form, null);
                            Check(store.Load().Shortcut.OnPress, "immediate trigger choice persists to settings");
                            mode.SelectedIndex = 1;
                            active = (KeyBinding)typeof(HotkeyService).GetField("binding", flags).GetValue(hotkeys);
                            Check(!active.OnPress, "dropdown can immediately switch active hotkey back to release");
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { typeof(MainForm).GetField("exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(form, true); form.Close(); }
                    }));
                    form.ShowDialog(); if (failure != null) throw failure;
                }
            }
            finally { foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
        }
        private static void TestBackgroundKeyboard()
        {
            Check(RawKeyboardWindow.Decode(17, 0x1D, 0) == 0xA2 && RawKeyboardWindow.Decode(17, 0x1D, 2) == 0xA3, "background keyboard distinguishes left and right Ctrl");
            Check(RawKeyboardWindow.Decode(18, 0x38, 0) == 0xA4 && RawKeyboardWindow.Decode(18, 0x38, 2) == 0xA5, "background keyboard distinguishes Alt and AltGr");
            Check(RawKeyboardWindow.Decode(16, 0x2A, 0) == 0xA0 && RawKeyboardWindow.Decode(16, 0x36, 1) == 0xA1, "background keyboard preserves shift side on release");
            Check(RawKeyboardWindow.Decode(0x77, 0x42, 0) == 0x77 && RawKeyboardWindow.Decode(0xDB, 0x1A, 0) == 0xDB && RawKeyboardWindow.Decode(0xAF, 0, 0) == 0xAF, "background keyboard supports function, symbol and multimedia keys");
            Check(RawKeyboardWindow.Decode(0xFF, 1, 0) == 0 && RawKeyboardWindow.Decode(17, 0xFF, 0) == 0 && RawKeyboardWindow.Decode(17, 0x1D, 4) == 0, "overrun and Pause synthetic Ctrl packets are ignored");
            using (var keyboard = new RawKeyboardWindow()) Check(keyboard.Registered, "native background keyboard INPUTSINK registration succeeds");
            var gesture = new ShortcutGesture(); var binding = new KeyBinding(); binding.Validate();
            gesture.Seed(0x41); gesture.SetBinding(binding); gesture.Clear(); gesture.SetBinding(binding);
            Check(!gesture.Feed(0xA2, true) && !gesture.Feed(0xA4, true) && !gesture.Feed(0xA4, false) && gesture.Feed(0xA2, false), "device reset clears stale keyboard state without false activation");
        }
        private static void TestImages()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (float dpi in new[] { 72f, 96f, 144f, 300f })
                {
                    string dpiSource = Path.Combine(root, "dpi-" + dpi + ".png");
                    using (var bitmap = new Bitmap(80, 40, PixelFormat.Format32bppArgb))
                    {
                        bitmap.SetResolution(dpi, dpi);
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            g.Clear(Color.Transparent);
                            g.FillRectangle(Brushes.Red, 0, 0, 8, 8);
                            g.FillRectangle(Brushes.Lime, 72, 0, 8, 8);
                            g.FillRectangle(Brushes.Blue, 0, 32, 8, 8);
                            g.FillRectangle(Brushes.Yellow, 72, 32, 8, 8);
                            using (var brush = new SolidBrush(Color.FromArgb(128, 240, 80, 16))) g.FillRectangle(brush, 20, 10, 40, 20);
                        }
                        bitmap.Save(dpiSource, ImageFormat.Png);
                    }
                    var imported = ImageAsset.Import(dpiSource, root);
                    using (var decoded = ImageAsset.Read(Path.Combine(root, imported.File)))
                    using (var original = new Bitmap(dpiSource))
                    {
                        bool matches = decoded.Size == original.Size;
                        for (int y = 0; y < original.Height && matches; y++)
                            for (int x = 0; x < original.Width && matches; x++)
                            {
                                var expected = original.GetPixel(x, y); var actual = decoded.GetPixel(x, y);
                                matches = expected.A == actual.A && (expected.A == 0 || Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1);
                            }
                        Check(matches, "import and reload preserve all image pixels and four corners at " + dpi + " DPI");
                    }
                }
                string source = Path.Combine(root, "source.png");
                using (var bitmap = new Bitmap(80, 40, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); using (var brush = new SolidBrush(Color.FromArgb(128, 255, 80, 20))) g.FillRectangle(brush, 20, 10, 40, 20); }
                    bitmap.Save(source, ImageFormat.Png);
                }
                var style = ImageAsset.Import(source, root);
                File.Delete(source);
                Check(File.Exists(Path.Combine(root, style.File)) && style.Percent == 100, "image import copies original into portable assets");
                using (var asset = new ImageAsset())
                {
                    asset.Load(Path.Combine(root, style.File));
                    File.Delete(Path.Combine(root, style.File));
                    Check(asset.Picture.Width == 80 && asset.Picture.Height == 40, "image loader releases file lock and retains pixels");
                    style.Percent = 150;
                    Check(asset.DisplaySize(style, 2) == new Size(240, 120), "image scale and DPI preserve aspect ratio");
                    style.Percent = 1000;
                    Check(asset.DisplaySize(style, 4) == new Size(2048, 1024), "large images respect overlay pixel limit");
                    style.Percent = 100;
                    using (var render = asset.Render(style, 1, false))
                    {
                        Check(render.Width == 104 && render.Height == 64 && render.GetPixel(0, 0).A == 0 && Math.Abs(render.GetPixel(52, 32).A - 128) <= 1, "image overlay preserves transparent borders and source alpha");
                    }
                    style.Alpha = 128;
                    using (var render = asset.Render(style, 1, false)) Check(Math.Abs(render.GetPixel(52, 32).A - 64) <= 1, "image opacity multiplies source alpha");
                    using (var render = asset.Render(style, 1, true)) Check(render.GetPixel(render.Width - 10, render.Height - 10).A == 255, "screen editing exposes resize handle");
                    string invalid = Path.Combine(root, "invalid.png"); File.WriteAllText(invalid, "invalid image");
                    Check(Reject(() => asset.Load(invalid)) && asset.Picture.Width == 80, "invalid image leaves previous usable bitmap intact");
                    bool missing = false; try { asset.Load(Path.Combine(root, "missing.png")); } catch (IOException) { missing = true; }
                    Check(missing && asset.Picture.Width == 80, "missing image reports error without replacing cache");
                }
                var settings = new AppSettings { ImageMode = true, Image = style, OffsetX = 83, OffsetY = -25 };
                settings.Presets.Add(new SavedPreset { Name = "picture", Code = "", Image = style.Copy() });
                string config = Path.Combine(root, "settings.json"); var store = new SettingsStore(config); store.Save(settings);
                var loaded = store.Load();
                Check(loaded.ImageMode && loaded.Image.File == style.File && loaded.Image.Alpha == 128 && loaded.OffsetX == 83 && loaded.Presets[0].Image.Percent == 100, "image mode, presets, opacity and position survive restart");
                var old = SettingsStore.Json.Deserialize<AppSettings>("{\"Version\":1,\"Enabled\":false}"); old.Validate();
                Check(!old.ImageMode && old.Image.Percent == 100 && !old.Enabled, "existing configurations retain shape mode and visibility");
                Check(Reject(() => new ImageCrosshair { Percent = double.NaN }.Validate()), "invalid image scaling rejected");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void MakeContactSheet()
        {
            var styles = new[] {
                new CrosshairStyle(), new CrosshairStyle { EnableTShape = true, Color = "#64DCC2" },
                new CrosshairStyle { EnableCircleCrosshair = true, Size = 17, Thickness = 2, EnableDot = true, DotIsCircle = true, DotSize = 1.5, Outline = 1 },
                new CrosshairStyle { Size = 0, EnableDot = true, DotIsCircle = true, DotSize = 3, Color = "#FFFFFF", Outline = 1 },
                new CrosshairStyle { Size = 5, Thickness = .5, Gap = -2, PixelSnap = true, Color = "#00FFFF", Outline = 1 },
                new CrosshairStyle { Size = 8, Thickness = 1, Gap = 2, EnableDot = true, DotIsCircle = false, Color = "#FF5050", Outline = 1, Alpha = 140 }
            };
            using (var image = new Bitmap(720, 360))
            using (var g = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI", 10))
            {
                g.Clear(Color.FromArgb(24, 31, 43));
                for (int i = 0; i < styles.Length; i++)
                {
                    int x = i % 3 * 240, y = i / 3 * 180;
                    g.DrawString(new[] { "Classic", "T shape", "Ring + dot", "Dot only", "Pixel snap", "Outline + alpha" }[i], font, Brushes.White, x + 14, y + 12);
                    Renderer.Draw(g, styles[i], new PointF(x + 120, y + 96), 1.5);
                }
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-render-check.png"), ImageFormat.Png);
            }
        }
    }
}
