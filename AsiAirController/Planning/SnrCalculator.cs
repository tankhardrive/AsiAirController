namespace AsiAirController.Planning;

/// <summary>SNR target values for the four quality tiers used by SnrCalculator.</summary>
public record SnrSettings(
    double Minimum   = 5,
    double Decent    = 15,
    double Good      = 30,
    double Excellent = 50);

/// <summary>
/// Estimates integration time needed to reach four SNR quality tiers for a DSO.
/// Uses the full CCD noise equation: SNR = S·√T / √(S + B + R²/t_sub)
/// → T = SNR² · (S + B + R²/t_sub) / S²
/// Ported from AstroPlannerWeb.Services.SnrCalculator.
/// </summary>
public record SnrTimeEstimate(
    double Minimum,
    double Decent,
    double Good,
    double Excellent,
    bool   IsBrightCore);

public static class SnrCalculator
{
    // V-band zero-point: ~8.93e9 photons/s/m² over ~89 nm bandwidth.
    private const double F0 = 8.93e9;

    private static double BortleToSkyBrightness(int bortle) => bortle switch
    {
        1 => 22.0, 2 => 21.5, 3 => 21.0, 4 => 20.5,
        5 => 19.5, 6 => 18.5, 7 => 18.0, 8 => 17.5,
        _ => 17.0,
    };

    private static (double Sb, bool IsBrightCore)? EffectiveSb(DeepSkyObject obj)
    {
        if (obj.Type == ObjectType.OpenCluster) return null;

        double? sb = obj.SurfaceBrightness;

        if (sb == null)
        {
            double? mag = obj.MagnitudeV ?? obj.MagnitudeB;
            if (mag == null || mag >= 99) return null;
            if (!obj.MajorAxisArcmin.HasValue || obj.MajorAxisArcmin.Value <= 0) return null;

            double aArcsec    = obj.MajorAxisArcmin.Value * 60.0;
            double bArcsec    = (obj.MinorAxisArcmin ?? obj.MajorAxisArcmin.Value) * 60.0;
            double areaArcsec = Math.PI * (aArcsec / 2.0) * (bArcsec / 2.0);
            if (areaArcsec <= 0) return null;

            sb = mag.Value + 2.5 * Math.Log10(areaArcsec);
        }

        if (sb < 10) return null;
        return (sb.Value, sb < 18);
    }

    /// <summary>
    /// Returns hours needed for each SNR tier, or null if the object can't be estimated
    /// (open clusters, objects without size/magnitude data).
    /// </summary>
    public static SnrTimeEstimate? Compute(
        ImagingSetup setup, DeepSkyObject obj, int? bortle, SnrSettings? snr = null)
    {
        if (!bortle.HasValue) return null;

        var sbResult = EffectiveSb(obj);
        if (sbResult == null) return null;
        var (sbObj, isBrightCore) = sbResult.Value;

        if (setup.ApertureMm <= 0 || setup.FocalLengthMm <= 0 || setup.PixelSizeMicrons <= 0)
            return null;

        snr ??= new SnrSettings();

        double plateScale      = 206.265 * setup.PixelSizeMicrons / setup.FocalLengthMm;
        double pixelSolidAngle = plateScale * plateScale;
        double apertureM2      = Math.PI * Math.Pow(setup.ApertureMm / 2000.0, 2);
        double qe              = Math.Clamp(setup.QePercent, 1, 100) / 100.0;
        double R               = Math.Max(setup.ReadNoiseElectrons, 0);
        double tSub            = setup.SubExposureSeconds > 0 ? setup.SubExposureSeconds : 300.0;

        double sbSky        = BortleToSkyBrightness(bortle.Value);
        double signalRate   = F0 * Math.Pow(10, -sbObj / 2.5) * apertureM2 * qe * pixelSolidAngle;
        double skyRejection = setup.FilterBandwidthNm / 300.0;
        double skyRate      = F0 * Math.Pow(10, -sbSky / 2.5) * apertureM2 * qe * pixelSolidAngle * skyRejection;

        if (signalRate <= 0) return null;

        double noiseFactor = signalRate + skyRate + (R * R) / tSub;
        double Solve(double targetSnr) =>
            targetSnr * targetSnr * noiseFactor / (signalRate * signalRate) / 3600.0;

        double minimum = Solve(snr.Minimum);

        return new SnrTimeEstimate(
            Minimum:     minimum,
            Decent:      Solve(snr.Decent),
            Good:        Solve(snr.Good),
            Excellent:   Solve(snr.Excellent),
            IsBrightCore: isBrightCore || minimum < 0.5);
    }

    public static string FormatHours(double hours)
    {
        if (hours < 1.0 / 60.0) return "<1m";
        if (hours < 1.0)        return $"{(int)Math.Round(hours * 60)}m";
        if (hours > 500)        return ">500h";
        return hours < 10 ? $"{hours:F1}h" : $"{(int)Math.Round(hours)}h";
    }
}
