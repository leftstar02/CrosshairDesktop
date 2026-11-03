using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace CrosshairDesktop
{
    internal static class ShareCodes
    {
        internal static string ExportJson(CrosshairStyle style)
        {
            var fill = CrosshairStyle.ParseColor(style.Color); var edge = CrosshairStyle.ParseColor(style.OutlineColor);
            return SettingsStore.Json.Serialize(new Dictionary<string, object> {
                { "Color", new { A = 255, R = fill.R, G = fill.G, B = fill.B } },
                { "Alpha", style.Alpha }, { "Size", style.Size }, { "Thickness", style.Thickness }, { "Gap", style.Gap },
                { "Outline", style.Outline }, { "OutlineColor", new { A = edge.A, R = edge.R, G = edge.G, B = edge.B } },
                { "DotSize", style.DotSize }, { "EnableDot", style.EnableDot }, { "DotIsCircle", style.DotIsCircle },
                { "EnableTShape", style.EnableTShape }, { "EnableCircleCrosshair", style.EnableCircleCrosshair }, { "PixelSnap", style.PixelSnap }
            });
        }
        internal static string Export(CrosshairStyle style)
        {
            style.Validate();
            var data = new byte[21];
            data[0] = 1;
            var fill = CrosshairStyle.ParseColor(style.Color); var edge = CrosshairStyle.ParseColor(style.OutlineColor);
            data[1] = fill.R; data[2] = fill.G; data[3] = fill.B; data[4] = (byte)style.Alpha;
            data[5] = edge.A; data[6] = edge.R; data[7] = edge.G; data[8] = edge.B;
            double[] numbers = { style.Size, style.Thickness, style.Gap, style.Outline, style.DotSize };
            for (int n = 0; n < numbers.Length; n++)
            {
                short fixedPoint = (short)Math.Round(numbers[n] * 100, MidpointRounding.AwayFromZero);
                data[9 + n * 2] = unchecked((byte)fixedPoint); data[10 + n * 2] = unchecked((byte)(fixedPoint >> 8));
            }
            bool[] flags = { style.EnableDot, style.DotIsCircle, style.EnableTShape, style.EnableCircleCrosshair, style.PixelSnap };
            for (int n = 0; n < flags.Length; n++) if (flags[n]) data[19] |= (byte)(1 << n);
            data[20] = unchecked((byte)data.Take(20).Sum(b => (int)b));
            return "MBCH-" + Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static CrosshairStyle Import(string input, int height, double scale, out string message)
        {
            string text = (input ?? "").Trim();
            if (text.Length > 65536) throw new FormatException("导入内容过长。");
            CrosshairStyle result;
            if (text.StartsWith("MBCH-", StringComparison.OrdinalIgnoreCase))
            {
                string body = text.Substring(5).Replace('-', '+').Replace('_', '/');
                byte[] data;
                try { data = Convert.FromBase64String(body.PadRight((body.Length + 3) / 4 * 4, '=')); }
                catch (FormatException) { throw new FormatException("分享码格式损坏。"); }
                if (data.Length != 21 || data[0] != 1 || data[20] != unchecked((byte)data.Take(20).Sum(b => (int)b)))
                    throw new FormatException("分享码校验失败或版本不受支持。");
                result = new CrosshairStyle
                {
                    Color = string.Format("#{0:X2}{1:X2}{2:X2}", data[1], data[2], data[3]), Alpha = data[4],
                    OutlineColor = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", data[5], data[6], data[7], data[8]),
                    Size = ReadFixed(data, 9), Thickness = ReadFixed(data, 11), Gap = ReadFixed(data, 13), Outline = ReadFixed(data, 15), DotSize = ReadFixed(data, 17),
                    EnableDot = (data[19] & 1) > 0, DotIsCircle = (data[19] & 2) > 0, EnableTShape = (data[19] & 4) > 0,
                    EnableCircleCrosshair = (data[19] & 8) > 0, PixelSnap = (data[19] & 16) > 0
                };
                message = "已导入 Murbong 分享码。";
            }
            else if (text.StartsWith("CS", StringComparison.Ordinal)) result = ImportCs(text, height, scale, out message);
            else if (text.StartsWith("{", StringComparison.Ordinal))
            {
                var map = SettingsStore.Json.DeserializeObject(text) as Dictionary<string, object>;
                if (map == null || !map.ContainsKey("Thickness") || !map.ContainsKey("Size")) throw new FormatException("这不是准星样式 JSON。");
                result = new CrosshairStyle();
                result.Size = Number(map, "Size", 10); result.Thickness = Number(map, "Thickness", 3); result.Gap = Number(map, "Gap", 1);
                result.Outline = Number(map, "Outline", 0); result.DotSize = Number(map, "DotSize", 0); result.Alpha = (int)Number(map, "Alpha", 255);
                result.Color = ReadColor(map, "Color", "#00FF00"); result.OutlineColor = ReadColor(map, "OutlineColor", "#FF000000");
                result.EnableDot = Flag(map, "EnableDot"); result.DotIsCircle = Flag(map, "DotIsCircle"); result.EnableTShape = Flag(map, "EnableTShape");
                result.EnableCircleCrosshair = Flag(map, "EnableCircleCrosshair"); result.PixelSnap = Flag(map, "PixelSnap");
                message = "已导入准星 JSON。";
            }
            else throw new FormatException("请粘贴 MBCH 分享码、CS2 / CSGO 分享码或准星 JSON。");
            result.Validate(); return result;
        }
        private static double ReadFixed(byte[] b, int i) { return (short)(b[i] | b[i + 1] << 8) / 100.0; }
        private static double Number(Dictionary<string, object> map, string key, double fallback)
        {
            object value; return map.TryGetValue(key, out value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
        }
        private static bool Flag(Dictionary<string, object> map, string key)
        {
            object value; return map.TryGetValue(key, out value) && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        private static string ReadColor(Dictionary<string, object> map, string key, string fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value)) return fallback;
            if (value is string) { CrosshairStyle.ParseColor((string)value); return (string)value; }
            var channels = value as Dictionary<string, object>;
            if (channels == null) throw new FormatException("颜色数据无效。");
            var color = Color.FromArgb((int)Number(channels, "A", 255), (int)Number(channels, "R", 0), (int)Number(channels, "G", 255), (int)Number(channels, "B", 0));
            return "#" + unchecked((uint)color.ToArgb()).ToString("X8", CultureInfo.InvariantCulture);
        }

        // Interoperability with CS2's public base-57 wire format; no game process access.
        private static CrosshairStyle ImportCs(string text, int height, double scale, out string message)
        {
            bool legacy = text.StartsWith("CSGO-", StringComparison.Ordinal);
            string body = legacy ? text.Substring(5).Replace("-", "") : text.Substring(2);
            int width = legacy ? 18 : 32;
            if (body.Length != (legacy ? 25 : 44)) throw new FormatException("CS2 分享码长度不正确。");
            const string alphabet = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
            BigInteger value = 0;
            foreach (char digit in body.Reverse())
            {
                int n = alphabet.IndexOf(digit);
                if (n < 0) throw new FormatException("CS2 分享码包含无效字符。");
                value = value * 57 + n;
            }
            var bytes = new byte[width];
            for (int n = width - 1; n >= 0; n--) { bytes[n] = (byte)(value & 255); value >>= 8; }
            if (value != 0 || bytes[0] != unchecked((byte)bytes.Skip(1).Sum(b => (int)b))) throw new FormatException("CS2 分享码校验失败。");
            if (bytes[1] != 1) throw new FormatException("暂不支持这个 CS2 分享码版本。");
            if (height < 240 || height > 8640 || scale <= 0) throw new FormatException("导入分辨率无效。");
            double length, thick, gap, edge;
            int alpha; bool dot, t; string color, outline;
            if (legacy)
            {
                length = Math.Round(bytes[14] / 10.0 * height / 480.0, MidpointRounding.ToEven);
                thick = Math.Max(1, Math.Round(bytes[12] / 10.0 * height / 480.0, MidpointRounding.ToEven));
                gap = Math.Max(0, Math.Truncate(4 + unchecked((sbyte)bytes[2]) / 10.0)) + Math.Ceiling(thick / 2);
                edge = (bytes[10] & 8) == 0 ? 0 : bytes[3] / 2.0;
                string[] palette = { "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#00FFFF" };
                int index = bytes[10] & 7;
                color = index < palette.Length ? palette[index] : string.Format("#{0:X2}{1:X2}{2:X2}", bytes[4], bytes[5], bytes[6]);
                alpha = (bytes[13] & 64) == 0 ? 255 : bytes[7]; dot = (bytes[13] & 16) != 0; t = (bytes[13] & 128) != 0; outline = "#FF000000";
                message = "已导入 CSGO 旧版分享码；动态效果以静态准星显示。";
            }
            else
            {
                int originalHeight = bytes[2] | bytes[3] << 8;
                if (originalHeight == 0) throw new FormatException("CS2 分享码的分辨率无效。");
                double factor = height / (double)Math.Max(240, originalHeight);
                length = ScalePositive(bytes[16], factor); thick = ScalePositive(Math.Min(bytes[13] & 63, 32), factor);
                int rawGap = Math.Max(-3840, Math.Min(3840, (int)(short)(bytes[14] | bytes[15] << 8)));
                gap = rawGap == 0 ? 0 : Math.Sign(rawGap) * Math.Max(1, Math.Round(Math.Abs(rawGap) * factor, MidpointRounding.AwayFromZero));
                edge = (bytes[13] >> 6) == 0 ? 0 : 1;
                color = string.Format("#{0:X2}{1:X2}{2:X2}", bytes[5], bytes[6], bytes[7]); alpha = bytes[8];
                outline = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", bytes[12], bytes[9], bytes[10], bytes[11]);
                dot = (bytes[4] & 64) != 0; t = (bytes[4] & 128) != 0;
                message = "已导入 CS2 分享码；动态准星和半侧描边以静态全描边近似。";
            }
            return new CrosshairStyle { Size = length / (2 * scale), Thickness = thick / (2 * scale), Gap = Math.Max(0, gap) / scale - 4 - thick / (2 * scale), Outline = edge / scale, Color = color, Alpha = alpha, OutlineColor = outline, EnableDot = dot, EnableTShape = t, PixelSnap = true };
        }
        private static double ScalePositive(int n, double s) { return n == 0 ? 0 : Math.Max(1, Math.Round(n * s, MidpointRounding.AwayFromZero)); }
    }
}
