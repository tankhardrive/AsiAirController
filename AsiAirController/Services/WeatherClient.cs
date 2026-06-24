using System.Globalization;
using System.Text.Json.Nodes;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class WeatherClient
{
    private static readonly HttpClient Http = new();
    private const string AlpacaBase = "https://alpaca-api.tx.starfront.space/api/v1/observingconditions/1";
    private const string LiveUrl     = "https://api.bortle.org/api/sfro/weather/live";

    // Runs Alpaca and bortle.org in parallel and merges: Alpaca for live numerics,
    // bortle.org fills in text labels (rain, darkness, alert) that Alpaca doesn't expose.
    public static async Task<WeatherData?> GetBestAsync(CancellationToken ct = default)
    {
        var alpacaTask = TryAsync(() => FetchAlpacaAsync(ct));
        var liveTask   = TryAsync(() => FetchLiveAsync(ct));

        await Task.WhenAll(alpacaTask, liveTask);

        var alpaca = alpacaTask.Result;
        var live   = liveTask.Result;

        if (alpaca == null) return live;
        if (live   == null) return alpaca;

        return alpaca with
        {
            CloudText    = alpaca.CloudText    ?? live.CloudText,
            WindText     = alpaca.WindText     ?? live.WindText,
            DarknessText = live.DarknessText,
            RainText     = live.RainText,
            AlertFlag    = live.AlertFlag,
            Source       = "Alpaca"
        };
    }

    private static async Task<WeatherData?> FetchAlpacaAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var tempTask    = GetDoubleAsync("temperature",    cts.Token);
        var dewTask     = GetDoubleAsync("dewpoint",       cts.Token);
        var humTask     = GetDoubleAsync("humidity",       cts.Token);
        var skyTempTask = GetDoubleAsync("skytemperature", cts.Token);
        var windTask    = GetDoubleAsync("windspeed",      cts.Token);

        await Task.WhenAll(tempTask, dewTask, humTask, skyTempTask, windTask);

        var tempC    = tempTask.Result;
        var dewC     = dewTask.Result;
        var humidity = humTask.Result;
        var skyTemp  = skyTempTask.Result;
        var wind     = windTask.Result;

        if (tempC == null && dewC == null) return null;

        // Sky-ambient delta → cloud condition (SkyAlert algorithm)
        string? cloudText = (tempC.HasValue && skyTemp.HasValue) ? (tempC.Value - skyTemp.Value) switch
        {
            >= 22 => "Clear",
            >= 14 => "Cloudy",
            _     => "Overcast"
        } : null;

        string? windText = wind switch
        {
            null  => null,
            < 1.5 => "Calm",
            < 6.0 => "Breezy",
            _     => "Windy"
        };

        return new WeatherData(tempC, dewC, humidity, DateTime.Now, "Alpaca",
            cloudText, windText, skyTemp);
    }

    private static async Task<WeatherData?> FetchLiveAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var json = await Http.GetStringAsync(LiveUrl, cts.Token);
        return ParseLiveJson(json);
    }

    private static WeatherData? ParseLiveJson(string text)
    {
        try
        {
            var n = JsonNode.Parse(text);
            if (n == null) return null;

            var scale = n["temperature_scale"]?.GetValue<string>() ?? "C";

            var rawTemp = GetJsonDouble(n, "ambient_temperature", "temperature");
            var rawDew  = GetJsonDouble(n, "dew_point", "dewpoint");
            var hum     = GetJsonDouble(n, "humidity");

            if (rawTemp == null && rawDew == null) return null;

            var tempC = rawTemp.HasValue ? ToC(rawTemp.Value, scale) : (double?)null;
            var dewC  = rawDew.HasValue  ? ToC(rawDew.Value,  scale) : (double?)null;

            DateTime? ts = null;
            var dateStr = n["file_write_date"]?.GetValue<string>();
            var timeStr = n["file_write_time"]?.GetValue<string>() ?? n["time"]?.GetValue<string>();
            if (dateStr != null && timeStr != null)
                ts = ParseDateTime($"{dateStr} {timeStr.Split('.')[0]}");
            else if (timeStr != null)
                ts = ParseDateTime(timeStr.Split('.')[0]);

            var cloudText    = n["cloud_clear_text"]?.GetValue<string>();
            var windText     = n["wind_limit_text"]?.GetValue<string>();
            var rawSkyTemp   = GetJsonDouble(n, "sky_temperature");
            var skyTempC     = rawSkyTemp.HasValue ? ToC(rawSkyTemp.Value, scale) : (double?)null;
            var darknessText = n["darkness_text"]?.GetValue<string>();
            var rainText     = n["rain_text"]?.GetValue<string>();
            var alertFlag    = (n["alert_flag"]?.GetValue<int>() ?? 0) != 0;

            return new WeatherData(tempC, dewC, hum, ts, "Live",
                cloudText, windText, skyTempC, darknessText, rainText, alertFlag);
        }
        catch { return null; }
    }

    private static async Task<double?> GetDoubleAsync(string property, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync($"{AlpacaBase}/{property}", ct);
            return JsonNode.Parse(json)?["Value"]?.GetValue<double>();
        }
        catch { return null; }
    }

    private static async Task<WeatherData?> TryAsync(Func<Task<WeatherData?>> fn)
    {
        try { return await fn(); } catch { return null; }
    }

    private static double ToC(double val, string scale) =>
        scale.Equals("F", StringComparison.OrdinalIgnoreCase) ? (val - 32.0) * 5.0 / 9.0 : val;

    private static DateTime? ParseDateTime(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;

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
