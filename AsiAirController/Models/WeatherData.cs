namespace AsiAirController.Models;

public record WeatherData(
    double? TemperatureC,
    double? DewPointC,
    double? HumidityPct,
    DateTime? Timestamp,
    string Source,
    string? CloudText      = null,
    string? WindText       = null,
    double? SkyTemperatureC = null,
    string? DarknessText   = null,
    string? RainText       = null,
    bool    AlertFlag      = false
);
