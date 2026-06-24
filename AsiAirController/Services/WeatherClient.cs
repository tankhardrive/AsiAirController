using System.Text.Json.Nodes;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class WeatherClient
{
    private static readonly HttpClient Http = new();
    private const string AlpacaBase = "https://alpaca-api.tx.starfront.space/api/v1/observingconditions";

    public static async Task<WeatherData?> FetchAlpacaAsync(int buildingId, CancellationToken ct = default)
    {
        if (buildingId <= 0) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var tempTask    = GetDoubleAsync(buildingId, "temperature", cts.Token);
        var dewTask     = GetDoubleAsync(buildingId, "dewpoint", cts.Token);
        var humTask     = GetDoubleAsync(buildingId, "humidity", cts.Token);
        var skyTempTask = GetDoubleAsync(buildingId, "skytemperature", cts.Token);
        var windTask    = GetDoubleAsync(buildingId, "windspeed", cts.Token);

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

    private static async Task<double?> GetDoubleAsync(int deviceNumber, string property, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync($"{AlpacaBase}/{deviceNumber}/{property}", ct);
            return JsonNode.Parse(json)?["Value"]?.GetValue<double>();
        }
        catch { return null; }
    }
}
