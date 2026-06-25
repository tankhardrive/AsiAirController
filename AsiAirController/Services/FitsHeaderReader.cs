using System.Globalization;
using System.Text;

namespace AsiAirController.Services;

public static class FitsHeaderReader
{
    public record FitsInfo(double? ExpTimeSec, double? RaHours, double? DecDegrees);

    // Read FITS keywords from the first two blocks (72 cards) — sufficient for any ASI Air header.
    public static FitsInfo Read(string path)
    {
        try
        {
            var buf = new byte[5760]; // 2 × 2880-byte FITS blocks
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            int read = fs.Read(buf, 0, buf.Length);

            double? expTime = null, ra = null, dec = null;
            int cards = read / 80;

            for (int i = 0; i < cards; i++)
            {
                int o = i * 80;
                if (buf[o + 8] != (byte)'=') continue; // no value indicator — skip

                var keyword = Encoding.ASCII.GetString(buf, o, 8).TrimEnd();
                if (keyword == "END") break;
                var rawValue = Encoding.ASCII.GetString(buf, o + 10, 70);

                switch (keyword)
                {
                    case "EXPTIME":
                    case "EXPOSURE":
                        expTime ??= ParseDouble(rawValue);
                        break;
                    case "RA":
                        // Standard FITS RA is in degrees — convert to hours
                        var raDeg = ParseDouble(rawValue);
                        if (raDeg.HasValue) ra ??= raDeg.Value / 15.0;
                        break;
                    case "DEC":
                        dec ??= ParseDouble(rawValue);
                        break;
                    case "OBJCTRA":
                        // Sexagesimal string "HH MM SS.SS"
                        ra ??= ParseSexRa(ParseString(rawValue));
                        break;
                    case "OBJCTDEC":
                        // Sexagesimal string "+DD MM SS.SS"
                        dec ??= ParseSexDec(ParseString(rawValue));
                        break;
                }
            }
            return new FitsInfo(expTime, ra, dec);
        }
        catch
        {
            return new FitsInfo(null, null, null);
        }
    }

    private static double? ParseDouble(string raw)
    {
        var s = raw.Split('/')[0].Trim();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string ParseString(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith('\''))
        {
            int end = s.IndexOf('\'', 1);
            return end > 0 ? s[1..end].Trim() : s[1..].Trim();
        }
        return s.Split('/')[0].Trim();
    }

    // "HH MM SS.SS" → decimal hours
    private static double? ParseSexRa(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !double.TryParse(parts[0], out var h)) return null;
        double.TryParse(parts.Length > 1 ? parts[1] : "0", out var m);
        double.TryParse(parts.Length > 2 ? parts[2] : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var sec);
        return h + m / 60.0 + sec / 3600.0;
    }

    // "+DD MM SS.SS" or "-DD MM SS.SS" → decimal degrees
    private static double? ParseSexDec(string s)
    {
        bool neg = s.StartsWith('-');
        var parts = s.TrimStart('+', '-').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !double.TryParse(parts[0], out var d)) return null;
        double.TryParse(parts.Length > 1 ? parts[1] : "0", out var m);
        double.TryParse(parts.Length > 2 ? parts[2] : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var sec);
        double result = d + m / 60.0 + sec / 3600.0;
        return neg ? -result : result;
    }
}
