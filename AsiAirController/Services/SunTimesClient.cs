using System.Text.Json.Nodes;

namespace AsiAirController.Services;

public record SunTimes(
    DateTime Sunrise,
    DateTime Sunset,
    DateTime CivilDusk,
    DateTime NauticalDusk,
    DateTime AstroDusk,
    DateTime AstroDawn,
    DateTime NauticalDawn,
    DateTime CivilDawn,
    TimeZoneInfo TimeZone
);

public static class SunTimesClient
{
    private static readonly HttpClient Http = new();

    public static async Task<SunTimes?> FetchAsync(
        double lat, double lon, TimeZoneInfo tz, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var localDate = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).ToString("yyyy-MM-dd");
        var url  = $"https://api.sunrise-sunset.org/json?lat={lat}&lng={lon}&formatted=0&date={localDate}";
        var json = await Http.GetStringAsync(url, cts.Token);
        var n    = JsonNode.Parse(json)?["results"];
        if (n == null) return null;

        static DateTime Parse(JsonNode? node, TimeZoneInfo tz)
        {
            var utc = DateTime.Parse(
                node!.GetValue<string>(), null,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }

        return new SunTimes(
            Sunrise:      Parse(n["sunrise"], tz),
            Sunset:       Parse(n["sunset"], tz),
            CivilDusk:    Parse(n["civil_twilight_end"], tz),
            NauticalDusk: Parse(n["nautical_twilight_end"], tz),
            AstroDusk:    Parse(n["astronomical_twilight_end"], tz),
            AstroDawn:    Parse(n["astronomical_twilight_begin"], tz),
            NauticalDawn: Parse(n["nautical_twilight_begin"], tz),
            CivilDawn:    Parse(n["civil_twilight_begin"], tz),
            TimeZone:     tz
        );
    }
}
