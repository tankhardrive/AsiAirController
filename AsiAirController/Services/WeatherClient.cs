using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class WeatherClient
{
    private static readonly HttpClient Http = new();
    private const string LiveUrl = "https://api.bortle.org/api/sfro/weather/live";

    public static async Task<WeatherData?> GetBestAsync(string filePath, CancellationToken ct = default)
    {
        var tasks = new List<Task<WeatherData?>> { TryAsync(() => FetchLiveAsync(ct)) };
        if (!string.IsNullOrEmpty(filePath))
            tasks.Add(TryAsync(() => ReadFileAsync(filePath)));

        var results = await Task.WhenAll(tasks);
        return results
            .OfType<WeatherData>()
            .OrderByDescending(w => w.Timestamp ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    private static async Task<WeatherData?> TryAsync(Func<Task<WeatherData?>> fn)
    {
        try { return await fn(); } catch { return null; }
    }

    private static async Task<WeatherData?> FetchLiveAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var json = await Http.GetStringAsync(LiveUrl, cts.Token);
        return ParseJson(json, "API");
    }

    public static async Task<WeatherData?> ReadFileAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        var content = (await reader.ReadToEndAsync()).Trim();
        // Try JSON first (unlikely for a .txt file but harmless), then Boltwood II positional
        return ParseJson(content, "File") ?? ParseBoltwood(content, "File");
    }

    // ── Parsers ─────────────────────────────────────────────────────────────

    // Handles the bortle.org SFRO JSON format (and similar JSON blobs)
    private static WeatherData? ParseJson(string text, string source)
    {
        if (!text.TrimStart().StartsWith('{') && !text.TrimStart().StartsWith('['))
            return null;
        try
        {
            var n = JsonNode.Parse(text);
            if (n == null) return null;

            // "temperature_scale":"F" or "C"
            var scale = n["temperature_scale"]?.GetValue<string>() ?? "C";

            var rawTemp = GetJsonDouble(n, "ambient_temperature", "temperature", "temp", "outTemp");
            var rawDew  = GetJsonDouble(n, "dew_point", "dewpoint", "dewPoint", "dew_c", "outDewpoint");
            var hum     = GetJsonDouble(n, "humidity", "rh", "outHumidity");

            if (rawTemp == null && rawDew == null) return null;

            // Convert from °F to °C at parse time so storage is always °C
            var tempC = rawTemp.HasValue ? ToC(rawTemp.Value, scale) : (double?)null;
            var dewC  = rawDew.HasValue  ? ToC(rawDew.Value,  scale) : (double?)null;

            // Timestamp from file_write_date + file_write_time fields
            DateTime? ts = null;
            var dateStr = n["file_write_date"]?.GetValue<string>();
            var timeStr = n["file_write_time"]?.GetValue<string>() ?? n["time"]?.GetValue<string>();
            if (dateStr != null && timeStr != null)
                ts = ParseDateTime($"{dateStr} {timeStr.Split('.')[0]}");
            else if (timeStr != null)
                ts = ParseDateTime(timeStr.Split('.')[0]);

            var cloudText = n["cloud_clear_text"]?.GetValue<string>();
            var windText  = n["wind_limit_text"]?.GetValue<string>();

            return new WeatherData(tempC, dewC, hum, ts, source, cloudText, windText);
        }
        catch { return null; }
    }

    // Handles Boltwood Cloud Sensor II space-delimited positional format:
    //   date time tempUnit windUnit skyT ambT sensorT wind humidity dewPt heater% rain wet since julianDay cloudFlag windFlag rain2Flag darknessFlag roofFlag alertFlag
    //   2026-06-11 21:38:57.00 F M 64.4 85.6 86 3 70 74.9 000 0 0 00020 046184.90205 3 1 1 1 1 1
    private static WeatherData? ParseBoltwood(string content, string source)
    {
        try
        {
            var line  = content.Trim().Split('\n')[0].Trim();
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 10) return null;

            // [2]=tempUnit "F"/"C", [5]=ambientTemp, [8]=humidity, [9]=dewPoint
            var scale = parts[2];

            if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var rawTemp)) return null;
            if (!double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var rawDew))  return null;

            double? hum = double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : null;

            // Timestamp from [0] date + [1] time (strip fractional seconds)
            var ts = ParseDateTime($"{parts[0]} {parts[1].Split('.')[0]}");

            // [15]=cloudClearFlag  1=Clear 2=Cloudy 3=VeryCloud 0=Unknown
            string? cloudText = parts.Length > 15 && int.TryParse(parts[15], out var cf) ? cf switch
            {
                1 => "Clear",
                2 => "Cloudy",
                3 => "Very Cloudy",
                _ => null
            } : null;

            // [16]=windFlag  1=Calm 2=Windy 3=VeryWindy 0=Unknown
            string? windText = parts.Length > 16 && int.TryParse(parts[16], out var wf) ? wf switch
            {
                1 => "Calm",
                2 => "Windy",
                3 => "Very Windy",
                _ => null
            } : null;

            return new WeatherData(
                ToC(rawTemp, scale), ToC(rawDew, scale), hum, ts, source,
                cloudText, windText);
        }
        catch { return null; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static double ToC(double val, string scale) =>
        scale.Equals("F", StringComparison.OrdinalIgnoreCase) ? (val - 32.0) * 5.0 / 9.0 : val;

    private static DateTime? ParseDateTime(string s)
    {
        return DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
    }

    private static double? GetJsonDouble(JsonNode node, params string[] keys)
    {
        foreach (var key in keys)
        {
            var val = node[key];
            if (val == null) continue;
            try { return val.GetValue<double>(); } catch { }
            if (double.TryParse(val.GetValue<string>(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var d)) return d;
        }
        return null;
    }
}
