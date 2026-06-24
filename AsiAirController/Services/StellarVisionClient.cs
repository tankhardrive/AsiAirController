using System.Text.Json.Nodes;

namespace AsiAirController.Services;

public record StellarVisionData(
    // Astrospheric forecast
    int     ImagingScore,
    string  SeeingLabel,
    double  TransparencyRaw,
    double  CloudCoverPct,
    // Weather safety
    bool    IsSafe,
    string[] SafetyIssues,
    string  SkyCondition,
    bool    DewSafe,
    // Moon
    double  MoonIlluminationPct,
    string  MoonPhaseLabel,
    bool    MoonAboveHorizon,
    // Space weather
    double  KpIndex,
    string  KpLevel,
    int     AuroraProbabilityPct,
    // NWS summary
    string  NwsForecast,
    // Observatory location
    double  Latitude,
    double  Longitude
);

public static class StellarVisionClient
{
    private static readonly HttpClient Http = new();
    private const string DataUrl = "https://data.stellarvision.space/stellarvision_data.json";

    public static async Task<StellarVisionData?> FetchAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var json = await Http.GetStringAsync(DataUrl, cts.Token);
        var n = JsonNode.Parse(json);
        if (n == null) return null;

        var forecast = n["astropheric"]?["forecast"]?["current"];
        var weather  = n["weather"];
        var safety   = weather?["safety"];
        var moon     = n["astropheric"]?["sky"]?["moon"];
        var swpc     = n["swpc"];
        var obs      = n["observatory"];

        var issues = safety?["issues"]?.AsArray()
            .Select(x => x?.GetValue<string>() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray() ?? Array.Empty<string>();

        var nwsPeriods = n["nws"]?["periods"]?.AsArray();
        var nwsSummary = nwsPeriods?.FirstOrDefault()?["short_forecast"]?.GetValue<string>() ?? string.Empty;

        return new StellarVisionData(
            ImagingScore:         forecast?["imaging_score"]?.GetValue<int>()        ?? 0,
            SeeingLabel:          forecast?["seeing_label"]?.GetValue<string>()       ?? string.Empty,
            TransparencyRaw:      forecast?["transparency_raw"]?.GetValue<double>()   ?? 0,
            CloudCoverPct:        forecast?["cloud_cover"]?.GetValue<double>()        ?? 0,
            IsSafe:               safety?["status"]?.GetValue<string>() == "SAFE",
            SafetyIssues:         issues,
            SkyCondition:         weather?["sky_condition"]?.GetValue<string>()       ?? string.Empty,
            DewSafe:              weather?["dew_safe"]?.GetValue<bool>()              ?? true,
            MoonIlluminationPct:  moon?["illumination"]?.GetValue<double>()           ?? 0,
            MoonPhaseLabel:       moon?["phase_label"]?.GetValue<string>()            ?? string.Empty,
            MoonAboveHorizon:     moon?["above_horizon"]?.GetValue<bool>()            ?? false,
            KpIndex:              swpc?["kp_index"]?["value"]?.GetValue<double>()     ?? 0,
            KpLevel:              swpc?["kp_index"]?["level"]?.GetValue<string>()     ?? string.Empty,
            AuroraProbabilityPct: swpc?["aurora"]?["probability_percent"]?.GetValue<int>() ?? 0,
            NwsForecast:          nwsSummary,
            Latitude:             obs?["latitude"]?.GetValue<double>()                ?? 0,
            Longitude:            obs?["longitude"]?.GetValue<double>()               ?? 0
        );
    }
}
