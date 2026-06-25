namespace AsiAirController.Planning;

/// <summary>
/// Computes horizon-aware visibility windows for DSOs over a single night.
/// Caches the twilight calculation so batch computations against the same date/site are fast.
/// Ported from AstroPlannerWeb.Services.VisibilityService (DSO subset only).
/// </summary>
public class VisibilityService
{
    private (DateOnly Date, string SiteKey, DateTime DarkStart, DateTime DarkEnd)? _twilightCache;

    private (DateTime DarkStart, DateTime DarkEnd) GetDarkness(DateOnly date, ObservationSite site)
    {
        string key = $"{site.LatitudeDegrees:F4},{site.LongitudeDegrees:F4},{site.TimeZoneId}";
        if (_twilightCache.HasValue
            && _twilightCache.Value.Date == date
            && _twilightCache.Value.SiteKey == key)
        {
            return (_twilightCache.Value.DarkStart, _twilightCache.Value.DarkEnd);
        }
        var result = AstronomyService.GetAstronomicalDarkness(date, site);
        _twilightCache = (date, key, result.DarkStart, result.DarkEnd);
        return result;
    }

    public VisibilityWindow ComputeDso(
        DeepSkyObject obj,
        DateOnly observingDate,
        ObservationSite site,
        HorizonProfile horizon,
        int stepMinutes,
        (double RaDeg, double DecDeg, double IllumPct) moonInfo)
    {
        var (darkStart, darkEnd) = GetDarkness(observingDate, site);
        return ComputeCore(obj.RaDegrees, obj.DecDegrees, darkStart, darkEnd,
            site, horizon, stepMinutes, moonInfo);
    }

    private static VisibilityWindow ComputeCore(
        double raDeg, double decDeg,
        DateTime darkStart, DateTime darkEnd,
        ObservationSite site, HorizonProfile horizon, int stepMinutes,
        (double RaDeg, double DecDeg, double IllumPct) moonInfo)
    {
        if (darkEnd <= darkStart)
            return VisibilityWindow.NeverVisible;

        double totalDarkMinutes = (darkEnd - darkStart).TotalMinutes;
        double visibleMinutes   = 0;
        double peakAlt          = double.MinValue;
        double peakAz           = 0;
        double peakClearance    = double.MinValue;
        DateTime peakTime       = darkStart;
        double visibleAltSum    = 0;
        int    visibleAltCount  = 0;
        bool   riseDetected     = false;
        DateTime? riseTime = null, setTime = null;

        var steps = new List<(DateTime Time, double Alt, double HorizAlt)>();
        var t = darkStart;

        while (t <= darkEnd)
        {
            var (alt, az)    = AstronomyService.EquatorialToHorizontal(raDeg, decDeg, t,
                site.LatitudeDegrees, site.LongitudeDegrees);
            double horizAlt  = horizon.GetAltitudeAt(az);
            steps.Add((t, alt, horizAlt));

            if (alt > horizAlt)
            {
                visibleMinutes += stepMinutes;
                visibleAltSum  += alt;
                visibleAltCount++;
                double clearance = alt - horizAlt;
                if (alt > peakAlt) { peakAlt = alt; peakAz = az; peakTime = t; peakClearance = clearance; }
                else if (clearance > peakClearance) peakClearance = clearance;

                if (!riseDetected) { riseDetected = true; riseTime = t == darkStart ? null : t; }
                setTime = t == darkEnd ? null : t;
            }
            t = t.AddMinutes(stepMinutes);
        }

        if (visibleMinutes <= 0)
            return VisibilityWindow.NeverVisible;

        double moonSep = AstronomyService.AngularSeparationDeg(
            raDeg, decDeg, moonInfo.RaDeg, moonInfo.DecDeg);

        return new VisibilityWindow
        {
            IsComputed             = true,
            DarkWindowStart        = darkStart,
            DarkWindowEnd          = darkEnd,
            RiseTime               = riseTime,
            SetTime                = setTime,
            Duration               = TimeSpan.FromMinutes(visibleMinutes),
            AverageAltitudeDegrees = visibleAltCount > 0 ? visibleAltSum / visibleAltCount : 0,
            PeakAltitudeDegrees    = peakAlt,
            PeakAzimuthDegrees     = peakAz,
            PeakClearanceDegrees   = Math.Max(0, peakClearance),
            PeakTime               = peakTime,
            MoonSeparationDegrees  = moonSep,
            VisibilityFraction     = visibleMinutes / totalDarkMinutes,
        };
    }

    /// <summary>
    /// Fast batch path: uses pre-computed time steps and LST values to avoid
    /// recomputing GAST for each object. Call ComputeLstHours once per night/site,
    /// then pass the result here for every object in the catalog.
    /// </summary>
    public static VisibilityWindow ComputeDsoFast(
        DeepSkyObject obj,
        DateTime darkStart,
        DateTime darkEnd,
        DateTime[] timeSteps,
        double[] lstHours,
        double latDeg,
        HorizonProfile horizon,
        int stepMinutes,
        (double RaDeg, double DecDeg, double IllumPct) moonInfo)
    {
        if (darkEnd <= darkStart || timeSteps.Length == 0)
            return VisibilityWindow.NeverVisible;

        double totalDarkMinutes = (darkEnd - darkStart).TotalMinutes;
        double visibleMinutes   = 0;
        double peakAlt          = double.MinValue;
        double peakAz           = 0;
        double peakClearance    = double.MinValue;
        DateTime peakTime       = darkStart;
        double visibleAltSum    = 0;
        int    visibleAltCount  = 0;
        bool   riseDetected     = false;
        DateTime? riseTime = null, setTime = null;

        for (int i = 0; i < timeSteps.Length; i++)
        {
            var (alt, az)   = AstronomyService.EquatorialToHorizontalFromLst(
                obj.RaDegrees, obj.DecDegrees, lstHours[i], latDeg);
            double horizAlt = horizon.GetAltitudeAt(az);

            if (alt > horizAlt)
            {
                visibleMinutes += stepMinutes;
                visibleAltSum  += alt;
                visibleAltCount++;
                double clearance = alt - horizAlt;
                if (alt > peakAlt) { peakAlt = alt; peakAz = az; peakTime = timeSteps[i]; peakClearance = clearance; }
                else if (clearance > peakClearance) peakClearance = clearance;

                if (!riseDetected)
                {
                    riseDetected = true;
                    riseTime = timeSteps[i] == darkStart ? null : timeSteps[i];
                }
                setTime = timeSteps[i] == darkEnd ? null : timeSteps[i];
            }
        }

        if (visibleMinutes <= 0)
            return VisibilityWindow.NeverVisible;

        double moonSep = AstronomyService.AngularSeparationDeg(
            obj.RaDegrees, obj.DecDegrees, moonInfo.RaDeg, moonInfo.DecDeg);

        return new VisibilityWindow
        {
            IsComputed             = true,
            DarkWindowStart        = darkStart,
            DarkWindowEnd          = darkEnd,
            RiseTime               = riseTime,
            SetTime                = setTime,
            Duration               = TimeSpan.FromMinutes(visibleMinutes),
            AverageAltitudeDegrees = visibleAltCount > 0 ? visibleAltSum / visibleAltCount : 0,
            PeakAltitudeDegrees    = peakAlt,
            PeakAzimuthDegrees     = peakAz,
            PeakClearanceDegrees   = Math.Max(0, peakClearance),
            PeakTime               = peakTime,
            MoonSeparationDegrees  = moonSep,
            VisibilityFraction     = visibleMinutes / totalDarkMinutes,
        };
    }
}
