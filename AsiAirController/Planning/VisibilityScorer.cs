namespace AsiAirController.Planning;

/// <summary>
/// Composite 0–100 score for a DSO on a given night.
/// Higher = better candidate for tonight's imaging session.
/// Ported from AstroPlannerWeb.Services.VisibilityScorer.
/// </summary>
public static class VisibilityScorer
{
    /// <summary>
    /// Sky quality factor from Bortle class: 1.0 (Bortle 1, pristine) → 0.5 (Bortle 9, inner city).
    /// Applied to the brightness component so faint objects score lower in light-polluted skies.
    /// </summary>
    private static double SkyFactor(int? bortle) =>
        bortle.HasValue
            ? Math.Clamp(1.0 - (bortle.Value - 1) * 0.056, 0.5, 1.0)
            : 0.75;

    /// <summary>
    /// Compute a 0–100 score.
    /// Components: visibility fraction (15), duration (10), avg altitude (20),
    /// moon separation (20), brightness (15), angular size (10), peak altitude (10).
    /// </summary>
    public static double ComputeScore(DeepSkyObject obj, VisibilityWindow vis, int? bortleClass = null)
    {
        if (!vis.IsVisible) return 0;

        double frac   = vis.VisibilityFraction * 15;
        double dur    = Math.Clamp(vis.Duration.TotalHours / 5.0, 0, 1) * 10;
        double alt    = Math.Min(vis.AverageAltitudeDegrees / 45.0, 1.0) * 20;
        double moon   = Math.Min(vis.MoonSeparationDegrees / 90.0, 1.0) * 20;
        double? mag   = obj.DisplayMagnitude < 99 ? obj.DisplayMagnitude : null;
        double sky    = SkyFactor(bortleClass);
        double bright = mag.HasValue ? Math.Clamp((15.0 - mag.Value) / 15.0, 0, 1) * 15 * sky : 7.5 * sky;
        double? arcmin = obj.MajorAxisArcmin;
        double size   = arcmin is double s && s > 0
            ? Math.Clamp(Math.Log10(Math.Max(s, 1)) / Math.Log10(30), 0, 1) * 10 : 0;
        double peakDeg = vis.PeakAltitudeDegrees;
        double peak   = peakDeg < 10 ? 0
            : peakDeg < 20 ? (peakDeg - 10) / 10.0 * 3
            : Math.Min((peakDeg - 20) / 70.0, 1.0) * 7 + 3;

        return Math.Round(Math.Max(frac + dur + alt + moon + bright + size + peak, 0), 1);
    }
}
