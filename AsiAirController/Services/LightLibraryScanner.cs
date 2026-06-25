using System.Globalization;
using System.Text.RegularExpressions;

namespace AsiAirController.Services;

public static partial class LightLibraryScanner
{
    private static readonly HashSet<string> FitsExts =
        new(StringComparer.OrdinalIgnoreCase) { ".fit", ".fits", ".fts" };

    // Detects mosaic panels: name ends with _1, _2, _1-1, _2-3, etc.
    [GeneratedRegex(@"^(.+)_(\d+(?:-\d+)?)$")]
    private static partial Regex MosaicPanelRegex();

    // Parses exposure from ASI Air filename: ..._300.0s_...
    [GeneratedRegex(@"_(\d+(?:\.\d+)?)s(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExposureRegex();

    public static async Task<IReadOnlyList<LightTarget>> ScanAsync(
        string rootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return [];

        var mosaics = new Dictionary<string, LightTarget>(StringComparer.OrdinalIgnoreCase);
        var singles = new Dictionary<string, LightTarget>(StringComparer.OrdinalIgnoreCase);

        var dirs = await Task.Run(
            () => Directory.GetDirectories(rootPath)
                           .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                           .ToArray(), ct);

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();
            var folderName = Path.GetFileName(dir);

            var files = await Task.Run(
                () => Directory.GetFiles(dir)
                               .Where(f => FitsExts.Contains(Path.GetExtension(f)))
                               .OrderBy(f => f)
                               .ToArray(), ct);

            if (files.Length == 0) continue;

            var info = await Task.Run(() => FitsHeaderReader.Read(files[0]), ct);
            double expSec = info.ExpTimeSec ?? ParseExposureFromFilename(files[0]);

            var m = MosaicPanelRegex().Match(folderName);
            if (m.Success)
            {
                string baseName = m.Groups[1].Value;
                if (!mosaics.TryGetValue(baseName, out var mosaic))
                {
                    mosaic = new LightTarget(baseName, isMosaic: true);
                    mosaics[baseName] = mosaic;
                }
                mosaic.Panels.Add(new MosaicPanel(folderName, files.Length, expSec, info.RaHours, info.DecDegrees));
            }
            else
            {
                singles[folderName] = new LightTarget(folderName, isMosaic: false)
                {
                    FrameCount      = files.Length,
                    ExposureSeconds = expSec,
                    RaHours         = info.RaHours,
                    DecDegrees      = info.DecDegrees,
                };
            }
        }

        foreach (var mosaic in mosaics.Values)
            mosaic.Panels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var result = new List<LightTarget>(singles.Values);
        result.AddRange(mosaics.Values);
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static double ParseExposureFromFilename(string path)
    {
        var m = ExposureRegex().Match(Path.GetFileNameWithoutExtension(path));
        return m.Success && double.TryParse(m.Groups[1].Value,
            NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

// ── Models ────────────────────────────────────────────────────────────────────

public class LightTarget
{
    public string Name { get; }
    public bool IsMosaic { get; }
    public List<MosaicPanel> Panels { get; } = new();

    // Populated for non-mosaic targets
    public int FrameCount { get; set; }
    public double ExposureSeconds { get; set; }
    public double? RaHours { get; set; }
    public double? DecDegrees { get; set; }

    public int TotalFrameCount =>
        IsMosaic ? Panels.Sum(p => p.FrameCount) : FrameCount;

    public double TotalHoursImaged =>
        IsMosaic ? Panels.Sum(p => p.TotalHours) : FrameCount * ExposureSeconds / 3600.0;

    public string DisplayName => IsMosaic ? $"{Name} Mosaic" : Name;

    public string Summary => IsMosaic
        ? $"{Panels.Count} panels · {TotalFrameCount:N0} frames · {TotalHoursImaged:F1}h total"
        : ExposureSeconds > 0
            ? $"{FrameCount:N0} × {ExposureSeconds:F0}s = {TotalHoursImaged:F1}h"
            : $"{FrameCount:N0} frames";

    public LightTarget(string name, bool isMosaic) { Name = name; IsMosaic = isMosaic; }
}

public class MosaicPanel
{
    public string Name { get; }
    public int FrameCount { get; }
    public double ExposureSeconds { get; }
    public double? RaHours { get; }
    public double? DecDegrees { get; }

    public double TotalHours => FrameCount * ExposureSeconds / 3600.0;

    public string Summary => ExposureSeconds > 0
        ? $"{FrameCount:N0} × {ExposureSeconds:F0}s = {TotalHours:F1}h"
        : $"{FrameCount:N0} frames";

    public MosaicPanel(string name, int frameCount, double expSec, double? ra, double? dec)
    {
        Name = name; FrameCount = frameCount; ExposureSeconds = expSec;
        RaHours = ra; DecDegrees = dec;
    }
}
