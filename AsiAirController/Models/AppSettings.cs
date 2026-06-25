using System.Text.Json;
using AsiAirController.Planning;

namespace AsiAirController.Models;

public class AppSettings
{
    public string IpAddress { get; set; } = string.Empty;
    public int    StarfrontBuildingId { get; set; } = 0;
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
    public double WindowWidth          { get; set; } = 1300;
    public double WindowHeight         { get; set; } = 1000;
    public int    PreviewImageMaxHeight { get; set; } = 375;

    // Weather monitoring
    public double DewMarginC       { get; set; } = 3.0;
    public bool   UseFahrenheit    { get; set; } = false;

    // Camera cooling
    public int    CoolerPreCoolMinutes { get; set; } = 20;
    public double CoolerTargetTempC    { get; set; } = -10.0;

    // Image sync
    public bool   ImageSyncEnabled        { get; set; } = false;
    public bool   ImageSyncAppendDateTime { get; set; } = false;
    public string ImageSyncSourcePath     { get; set; } = string.Empty;
    public string ImageSyncDestPath       { get; set; } = string.Empty;

    // Observatory location
    public string ObservatoryName          { get; set; } = "My Observatory";
    public string ObservatoryTimeZoneId    { get; set; } = "America/Chicago";
    public double ObservatoryLatitudeDeg   { get; set; } = 30.0;
    public double ObservatoryLongitudeDeg  { get; set; } = -97.0;
    public double ObservatoryElevationM    { get; set; } = 200.0;
    public int    ObservatoryBortleClass   { get; set; } = 4;

    // Equipment — telescope
    public string TelescopeName      { get; set; } = "";
    public double TelescopeApertureMm   { get; set; } = 100.0;
    public double TelescopeFocalLengthMm { get; set; } = 500.0;

    // Equipment — camera
    public string CameraName               { get; set; } = "";
    public double CameraPixelSizeMicrons   { get; set; } = 3.76;
    public int    CameraSensorWidthPixels  { get; set; } = 4144;
    public int    CameraSensorHeightPixels { get; set; } = 2822;
    public double CameraQePercent          { get; set; } = 65.0;
    public double CameraReadNoiseE         { get; set; } = 3.0;

    // Auto-planner — imaging defaults
    public double PlannerSubExposureSec    { get; set; } = 300.0;
    public string PlannerFilterType        { get; set; } = "Broadband";
    public int    PlannerBinning           { get; set; } = 1;
    public int    PlannerRepeatCount       { get; set; } = 10;

    // Auto-planner — target selection preferences
    public bool   PlannerEnabled                 { get; set; } = false;
    public bool   PlannerMultiTarget             { get; set; } = false;
    public double PlannerMinAltitudeDeg          { get; set; } = 20.0;
    public double PlannerMinMoonSeparationDeg    { get; set; } = 30.0;
    public double PlannerSnrGoalHours            { get; set; } = 0.0;   // 0 = no goal
    public List<string> PlannerObjectTypes       { get; set; } = DefaultObjectTypes();

    // Notifications
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    // Autopilot
    public int        AutopilotNightCount          { get; set; } = 0;
    public int        AutopilotPowerOnOffsetMinutes { get; set; } = 60;
    public List<int>  AutopilotPlanIds             { get; set; } = new();

    // ── Planning model helpers ────────────────────────────────────────────────

    public ObservationSite ToObservationSite() => new()
    {
        Name             = ObservatoryName,
        TimeZoneId       = ObservatoryTimeZoneId,
        LatitudeDegrees  = ObservatoryLatitudeDeg,
        LongitudeDegrees = ObservatoryLongitudeDeg,
        ElevationMeters  = ObservatoryElevationM,
        BortleClass      = ObservatoryBortleClass > 0 ? ObservatoryBortleClass : null,
    };

    public ImagingSetup ToImagingSetup() => new()
    {
        Name                = $"{TelescopeName} + {CameraName}".Trim(' ', '+', ' '),
        ApertureMm          = TelescopeApertureMm,
        FocalLengthMm       = TelescopeFocalLengthMm,
        PixelSizeMicrons    = CameraPixelSizeMicrons,
        SensorWidthPixels   = CameraSensorWidthPixels,
        SensorHeightPixels  = CameraSensorHeightPixels,
        QePercent           = CameraQePercent,
        ReadNoiseElectrons  = CameraReadNoiseE,
        SubExposureSeconds  = PlannerSubExposureSec,
        Filter              = Enum.TryParse<FilterType>(PlannerFilterType, out var ft) ? ft : FilterType.Broadband,
        CameraName          = CameraName,
        TelescopeName       = TelescopeName,
    };

    public HashSet<ObjectType> GetPlannerObjectTypes()
    {
        var result = new HashSet<ObjectType>();
        foreach (var name in PlannerObjectTypes)
            if (Enum.TryParse<ObjectType>(name, out var t))
                result.Add(t);
        return result;
    }

    private static List<string> DefaultObjectTypes() =>
    [
        nameof(ObjectType.Galaxy),
        nameof(ObjectType.GalaxyPair),
        nameof(ObjectType.GalaxyTriplet),
        nameof(ObjectType.GalaxyGroup),
        nameof(ObjectType.OpenCluster),
        nameof(ObjectType.GlobularCluster),
        nameof(ObjectType.ClusterNebula),
        nameof(ObjectType.PlanetaryNebula),
        nameof(ObjectType.EmissionNebula),
        nameof(ObjectType.ReflectionNebula),
        nameof(ObjectType.SupernovaRemnant),
        nameof(ObjectType.HiiRegion),
        nameof(ObjectType.BrightNebula),
        nameof(ObjectType.Nebula),
    ];

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
