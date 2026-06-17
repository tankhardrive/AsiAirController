using System.Text.Json;

namespace AsiAirController.Models;

public class AppSettings
{
    public string IpAddress { get; set; } = string.Empty;
    public string RoofStatusFilePath { get; set; } = "/Volumes/sfro-customer/roof/building-5/RoofStatusFile.txt";
    public int    StarfrontBuildingId { get; set; } = 5;
    public string ExposureSeconds { get; set; } = "10";

    // Kasa cloud — password stored in plain text (personal tool, local settings file)
    public string KasaEmail        { get; set; } = string.Empty;
    public string KasaPassword     { get; set; } = string.Empty;
    public string KasaDeviceId     { get; set; } = string.Empty;
    public string KasaDeviceAlias  { get; set; } = string.Empty;
    public string KasaAppServerUrl { get; set; } = string.Empty;
    public string KasaChildId      { get; set; } = string.Empty;
    public string KasaImagingDeviceId { get; set; } = string.Empty;
    public string KasaImagingChildId  { get; set; } = string.Empty;
    public string KasaAsiAirDeviceId  { get; set; } = string.Empty;
    public string KasaAsiAirChildId   { get; set; } = string.Empty;

    // Window state
    public double WindowWidth  { get; set; } = 1300;
    public double WindowHeight { get; set; } = 1000;

    // Weather monitoring
    public string WeatherFilePath  { get; set; } = string.Empty;
    public double DewMarginC       { get; set; } = 3.0;
    public bool   UseFahrenheit    { get; set; } = false;

    // Camera cooling
    public int    CoolerPreCoolMinutes { get; set; } = 20;
    public double CoolerTargetTempC    { get; set; } = -10.0;

    // Notifications
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AsiAirController");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }
}
