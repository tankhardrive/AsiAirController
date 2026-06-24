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
    DateTime CivilDawn
);

public static class SunTimesClient
{
    private static readonly HttpClient Http = new();

    public static async Task<SunTimes?> FetchAsync(double lat, double lon, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var url  = $"https://api.sunrise-sunset.org/json?lat={lat}&lng={lon}&formatted=0";
        var json = await Http.GetStringAsync(url, cts.Token);
        var n    = JsonNode.Parse(json)?["results"];
        if (n == null) return null;

        static DateTime Parse(JsonNode? node) =>
            DateTime.Parse(node!.GetValue<string>()).ToLocalTime();

        return new SunTimes(
            Sunrise:      Parse(n["sunrise"]),
            Sunset:       Parse(n["sunset"]),
            CivilDusk:    Parse(n["civil_twilight_end"]),
            NauticalDusk: Parse(n["nautical_twilight_end"]),
            AstroDusk:    Parse(n["astronomical_twilight_end"]),
            AstroDawn:    Parse(n["astronomical_twilight_begin"]),
            NauticalDawn: Parse(n["nautical_twilight_begin"]),
            CivilDawn:    Parse(n["civil_twilight_begin"])
        );
    }
}
