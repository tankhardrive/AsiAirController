namespace AsiAirController.Planning;

/// <summary>
/// Field of view and object fit calculations.
/// Ported from AstroPlannerWeb.Services.FovCalculator.
/// </summary>
public static class FovCalculator
{
    public static double PlateScaleArcsecPx(double focalLengthMm, double pixelSizeMicrons)
        => focalLengthMm > 0 ? 206.265 * pixelSizeMicrons / focalLengthMm : 0;

    public static (double WidthArcmin, double HeightArcmin) FovArcmin(ImagingSetup s)
    {
        double ps = PlateScaleArcsecPx(s.FocalLengthMm, s.PixelSizeMicrons);
        return (ps * s.SensorWidthPixels / 60.0, ps * s.SensorHeightPixels / 60.0);
    }

    /// <summary>
    /// How much of the long FOV axis the object fills (%).
    /// 100% = object exactly fills the frame. >100% = object larger than FOV.
    /// </summary>
    public static double FillPercent(ImagingSetup s, double objectMajorArcmin)
    {
        var (w, h) = FovArcmin(s);
        double longSide = Math.Max(w, h);
        return longSide > 0 ? objectMajorArcmin / longSide * 100 : 0;
    }

    /// <summary>Ideal fill % target for an object of a given angular size.</summary>
    public static double TargetFillPct(double majorArcmin) => majorArcmin switch
    {
        < 5   => 40,
        < 20  => 50,
        < 60  => 65,
        < 120 => 80,
        _     => 90,
    };

    public static bool IsUsable(ImagingSetup s)
        => s.FocalLengthMm > 0 && s.PixelSizeMicrons > 0
        && s.SensorWidthPixels > 0 && s.SensorHeightPixels > 0;

    /// <summary>
    /// True if an object fits reasonably in the FOV.
    /// Rejects if object is more than 3× the frame's long axis (too large to capture meaningfully).
    /// Rejects if the object is so small it fills less than 5% (would be tiny/featureless).
    /// </summary>
    public static bool FitsInFov(ImagingSetup s, double objectMajorArcmin)
    {
        if (!IsUsable(s) || objectMajorArcmin <= 0) return true; // no setup configured → don't filter
        double fill = FillPercent(s, objectMajorArcmin);
        return fill is >= 5 and <= 300;
    }

    public static string FormatArcmin(double arcmin)
        => arcmin >= 60 ? $"{arcmin / 60.0:F1}°" : $"{arcmin:F0}′";
}
