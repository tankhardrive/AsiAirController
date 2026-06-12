using System.Text.Json;

namespace AsiAirController.Models;

public class AppSettings
{
    public string IpAddress { get; set; } = string.Empty;
    public string RoofStatusFilePath { get; set; } = "/Volumes/sfro-customer/roof/building-5/RoofStatusFile.txt";
    public string RoofKey { get; set; } = "roof5";
    public string ExposureSeconds { get; set; } = "10";

    // Kasa cloud — password stored in plain text (personal tool, local settings file)
    public string KasaEmail        { get; set; } = string.Empty;
    public string KasaPassword     { get; set; } = string.Empty;
    public string KasaDeviceId     { get; set; } = string.Empty;
    public string KasaDeviceAlias  { get; set; } = string.Empty;
    public string KasaAppServerUrl { get; set; } = string.Empty;
    public string KasaChildId      { get; set; } = string.Empty;

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
