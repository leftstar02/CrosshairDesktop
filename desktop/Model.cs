using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CrosshairDesktop
{
    public sealed class CrosshairStyle
    {
        public string Color = "#00FF00";
        public int Alpha = 255;
        public double Size = 10, Thickness = 3, Gap = 1, Outline, DotSize;
        public string OutlineColor = "#FF000000";
        public bool EnableDot, DotIsCircle, EnableTShape, EnableCircleCrosshair, PixelSnap;

        public CrosshairStyle Copy() { return (CrosshairStyle)MemberwiseClone(); }
        public void Validate()
        {
            Size = Limit(Size, 0, 100); Thickness = Limit(Thickness, 0, 100);
            Gap = Limit(Gap, -100, 100); Outline = Limit(Outline, 0, 100); DotSize = Limit(DotSize, 0, 100);
            Alpha = Math.Max(0, Math.Min(255, Alpha));
            ParseColor(Color); ParseColor(OutlineColor);
        }
        private static double Limit(double v, double min, double max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("尺寸必须是有效的数字。");
            return Math.Max(min, Math.Min(max, v));
        }
        public static Color ParseColor(string hex)
        {
            string body = (hex ?? "").Trim().TrimStart('#');
            uint value;
            if ((body.Length != 6 && body.Length != 8) || !uint.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                throw new FormatException("颜色应为 #RRGGBB 或 #AARRGGBB。");
            if (body.Length == 6) value |= 0xFF000000;
            return System.Drawing.Color.FromArgb(unchecked((int)value));
        }
        [ScriptIgnore] public Color Fill { get { var c = ParseColor(Color); return System.Drawing.Color.FromArgb(Alpha, c.R, c.G, c.B); } }
    }

    public sealed class KeyBinding
    {
        public bool Ctrl = true, Alt = true, Shift, Win;
        public int Key;
        public List<int> ExtraKeys = new List<int>();
        public string Trigger = "";
        public bool AllowOtherKeys;
        [ScriptIgnore] public bool OnPress { get { return Trigger == "Press" || Trigger == "" && Key != 0; } }
        [ScriptIgnore] public string TriggerLabel { get { return OnPress ? "按下时触发" : "全部松开时触发"; } }
        [ScriptIgnore] public List<int> Codes
        {
            get
            {
                var keys = new List<int>();
                if (Ctrl) keys.Add(17); if (Alt) keys.Add(18); if (Shift) keys.Add(16); if (Win) keys.Add(91);
                if (Key != 0) keys.Add(Key);
                if (ExtraKeys != null) keys.AddRange(ExtraKeys);
                return keys.Select(Normalize).Distinct().ToList();
            }
        }
        public KeyBinding Copy() { var copy = (KeyBinding)MemberwiseClone(); copy.ExtraKeys = ExtraKeys == null ? new List<int>() : new List<int>(ExtraKeys); return copy; }
        [ScriptIgnore]
        public string Label
        {
            get
            {
                return string.Join(" + ", Codes.Select(KeyName));
            }
        }
        public void Validate()
        {
            var codes = Codes;
            if (codes.Count == 0) throw new FormatException("请先录入一个按键或组合键。");
            if (codes.Any(k => !IsKeyboardKey(k))) throw new FormatException("该按键无法作为键盘快捷键，请重新录入。");
            if (codes.Contains(91) && codes.Contains(0x4C) || codes.Contains(17) && codes.Contains(18) && codes.Contains(0x2E))
                throw new FormatException("Win+L 和 Ctrl+Alt+Delete 是系统保留快捷键，请选择其他按键。");
            if (Trigger == "") Trigger = Key == 0 ? "Release" : "Press";
            if (Trigger != "Press" && Trigger != "Release") throw new FormatException("快捷键触发方式无效。");
        }
        internal static int Normalize(int key)
        {
            if (key == 0xA2 || key == 0xA3) return 17;
            if (key == 0xA4 || key == 0xA5) return 18;
            if (key == 0xA0 || key == 0xA1) return 16;
            return key == 92 ? 91 : key;
        }
        internal static bool IsKeyboardKey(int key)
        {
            return key >= 8 && key <= 254 && Enum.IsDefined(typeof(System.Windows.Forms.Keys), key);
        }
        internal static string KeyName(int key)
        {
            if (key == 17) return "Ctrl"; if (key == 18) return "Alt"; if (key == 16) return "Shift"; if (key == 91) return "Win";
            if (key >= 0x30 && key <= 0x39) return ((char)key).ToString();
            switch (key)
            {
                case 0xBA: return ";"; case 0xBB: return "="; case 0xBC: return ","; case 0xBD: return "-"; case 0xBE: return ".";
                case 0xBF: return "/"; case 0xC0: return "`"; case 0xDB: return "["; case 0xDC: return "\\"; case 0xDD: return "]"; case 0xDE: return "'";
                default: return ((System.Windows.Forms.Keys)key).ToString();
            }
        }
        internal static KeyBinding FromKeys(IEnumerable<int> keys, string trigger)
        {
            var codes = keys.Select(Normalize).Distinct().ToList();
            var ordinary = codes.Where(k => k != 16 && k != 17 && k != 18 && k != 91).OrderBy(k => k).ToList();
            return new KeyBinding { Ctrl = codes.Contains(17), Alt = codes.Contains(18), Shift = codes.Contains(16), Win = codes.Contains(91),
                Key = ordinary.FirstOrDefault(), ExtraKeys = ordinary.Skip(1).ToList(), Trigger = trigger };
        }
    }

    public sealed class SavedPreset
    {
        public string Name, Code;
        public ImageCrosshair Image;
        public override string ToString() { return Name; }
    }

    public sealed class AppSettings
    {
        public int Version = 1;
        public CrosshairStyle Style = new CrosshairStyle();
        public bool ImageMode;
        public ImageCrosshair Image = new ImageCrosshair();
        public bool Enabled = true, ScaleWithDpi = true;
        public string Monitor = "", PreviewBackground = "深色";
        public int OffsetX, OffsetY, Cs2Height = 1080;
        public KeyBinding Shortcut = new KeyBinding();
        public List<SavedPreset> Presets = new List<SavedPreset>();
        public void Validate()
        {
            if (Style == null || Shortcut == null || Version != 1) throw new FormatException("设置文件版本或内容不受支持。");
            Style.Validate(); Shortcut.Validate();
            if (Image == null) Image = new ImageCrosshair();
            Image.Validate();
            OffsetX = Math.Max(-16000, Math.Min(16000, OffsetX)); OffsetY = Math.Max(-16000, Math.Min(16000, OffsetY));
            Cs2Height = Math.Max(240, Math.Min(8640, Cs2Height));
            if (Presets == null) Presets = new List<SavedPreset>();
            Presets = Presets.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name) && p.Code != null).Take(100).ToList();
        }
    }

    internal sealed class SettingsStore
    {
        internal readonly string Path;
        internal string Warning;
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        internal SettingsStore(string path) { Path = path; }
        internal AppSettings Load()
        {
            if (!File.Exists(Path)) return new AppSettings();
            try { return Decode(File.ReadAllText(Path)); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)) throw;
                Warning = "设置文件无法读取，已保留原文件并尝试备份。";
                try { return Decode(File.ReadAllText(Path + ".bak")); }
                catch { return new AppSettings(); }
            }
        }
        private static AppSettings Decode(string text)
        {
            var data = Json.Deserialize<AppSettings>(text);
            if (data == null) throw new FormatException("设置为空。");
            data.Validate(); return data;
        }
        internal void Save(AppSettings settings)
        {
            settings.Validate();
            string temp = Path + ".tmp";
            File.WriteAllText(temp, Json.Serialize(settings), new System.Text.UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak");
            else File.Move(temp, Path);
        }
    }
}
