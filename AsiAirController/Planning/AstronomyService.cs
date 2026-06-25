using AASharp;

namespace AsiAirController.Planning;

/// <summary>
/// Core astronomical calculations: coordinate transforms, twilight, moon.
/// All angles in degrees, DateTimes in UTC unless noted.
/// Ported from AstroPlannerWeb.Services.AstronomyService (DSO subset only).
/// </summary>
public static class AstronomyService
{
    // ── Julian Day ────────────────────────────────────────────────────────────

    public static double ToJulianDay(DateTime utc)
    {
        double dayFraction = utc.Day + utc.TimeOfDay.TotalDays;
        return AASDate.DateToJD(utc.Year, utc.Month, dayFraction, true);
    }

    // ── Coordinate transform ──────────────────────────────────────────────────

    /// <summary>
    /// RA/Dec (J2000 degrees) → Alt/Az (degrees).
    /// Az is [0,360) measured North through East.
    /// </summary>
    public static (double Alt, double Az) EquatorialToHorizontal(
        double raDeg, double decDeg, DateTime utc, double latDeg, double lonDeg)
    {
        double jd   = ToJulianDay(utc);
        double gast = AASSidereal.ApparentGreenwichSiderealTime(jd);
        double lst  = ((gast + lonDeg / 15.0) % 24 + 24) % 24;
        double haHours = lst - raDeg / 15.0;
        haHours = ((haHours % 24) + 24) % 24;
        if (haHours > 12) haHours -= 24;

        var    horiz = AASCoordinateTransformation.Equatorial2Horizontal(haHours, decDeg, latDeg);
        double alt   = horiz.Y;
        double az    = (horiz.X + 180.0) % 360.0;
        if (az < 0) az += 360;
        return (alt, az);
    }

    /// <summary>
    /// Faster variant using a pre-computed LST value (see ComputeLstHours).
    /// Avoids recomputing GAST when processing many objects at the same time steps.
    /// </summary>
    public static (double Alt, double Az) EquatorialToHorizontalFromLst(
        double raDeg, double decDeg, double lstHours, double latDeg)
    {
        double haHours = lstHours - raDeg / 15.0;
        haHours = ((haHours % 24) + 24) % 24;
        if (haHours > 12) haHours -= 24;

        var    horiz = AASCoordinateTransformation.Equatorial2Horizontal(haHours, decDeg, latDeg);
        double alt   = horiz.Y;
        double az    = (horiz.X + 180.0) % 360.0;
        if (az < 0) az += 360;
        return (alt, az);
    }

    /// <summary>
    /// Pre-compute LST for an array of UTC times. Pass the result to
    /// EquatorialToHorizontalFromLst to skip the GAST calculation per object.
    /// </summary>
    public static double[] ComputeLstHours(DateTime[] times, double lonDeg)
    {
        var lst = new double[times.Length];
        for (int i = 0; i < times.Length; i++)
        {
            double jd   = ToJulianDay(times[i]);
            double gast = AASSidereal.ApparentGreenwichSiderealTime(jd);
            lst[i] = ((gast + lonDeg / 15.0) % 24 + 24) % 24;
        }
        return lst;
    }

    // ── Sun ───────────────────────────────────────────────────────────────────

    public static (double RaDeg, double DecDeg) GetSunPosition(DateTime utc)
    {
        double jd  = ToJulianDay(utc);
        double lon = AASSun.ApparentEclipticLongitude(jd, false);
        double lat = AASSun.ApparentEclipticLatitude(jd, false);
        double eps = AASNutation.TrueObliquityOfEcliptic(jd);
        var    eq  = AASCoordinateTransformation.Ecliptic2Equatorial(lon, lat, eps);
        return (eq.X * 15.0, eq.Y);
    }

    public static double GetSunAltitude(DateTime utc, double latDeg, double lonDeg)
    {
        var (ra, dec) = GetSunPosition(utc);
        var (alt, _)  = EquatorialToHorizontal(ra, dec, utc, latDeg, lonDeg);
        return alt;
    }

    /// <summary>
    /// Binary-search the start and end of astronomical darkness (sun below -18°) for a given local date.
    /// Returns UTC times.
    /// </summary>
    public static (DateTime DarkStart, DateTime DarkEnd) GetAstronomicalDarkness(
        DateOnly localDate, ObservationSite site)
    {
        var tz  = site.GetTimeZone();
        var lat = site.LatitudeDegrees;
        var lon = site.LongitudeDegrees;

        var localNoon  = new DateTime(localDate.Year, localDate.Month, localDate.Day, 12, 0, 0);
        var eveningHi  = localNoon.AddHours(15);

        var darkStart = BinarySearchTwilight(
            TimeZoneInfo.ConvertTimeToUtc(localNoon, tz),
            TimeZoneInfo.ConvertTimeToUtc(eveningHi, tz),
            lat, lon, crossingDown: true);

        var morningLo = localNoon.AddHours(13);
        var morningHi = localNoon.AddHours(24);

        var darkEnd = BinarySearchTwilight(
            TimeZoneInfo.ConvertTimeToUtc(morningLo, tz),
            TimeZoneInfo.ConvertTimeToUtc(morningHi, tz),
            lat, lon, crossingDown: false);

        return (darkStart, darkEnd);
    }

    private static DateTime BinarySearchTwilight(
        DateTime lo, DateTime hi, double lat, double lon, bool crossingDown)
    {
        const double threshold = -18.0;
        for (int i = 0; i < 40; i++)
        {
            var    mid = lo + (hi - lo) / 2;
            double alt = GetSunAltitude(mid, lat, lon);
            if (crossingDown) { if (alt > threshold) lo = mid; else hi = mid; }
            else              { if (alt < threshold) lo = mid; else hi = mid; }
        }
        return lo + (hi - lo) / 2;
    }

    // ── Moon ─────────────────────────────────────────────────────────────────

    /// <summary>Returns moon RA/Dec (degrees) and illumination percentage.</summary>
    public static (double RaDeg, double DecDeg, double IlluminationPct) GetMoonPosition(DateTime utc)
    {
        double jd  = ToJulianDay(utc);
        double lon = AASMoon.EclipticLongitude(jd);
        double lat = AASMoon.EclipticLatitude(jd);
        double eps = AASNutation.TrueObliquityOfEcliptic(jd);
        var    eq  = AASCoordinateTransformation.Ecliptic2Equatorial(lon, lat, eps);
        double ra  = eq.X * 15.0;
        double dec = eq.Y;

        var (sunRa, sunDec) = GetSunPosition(utc);
        double illum = ComputeIllumination(ra, dec, sunRa, sunDec);
        return (ra, dec, illum * 100.0);
    }

    private static double ComputeIllumination(double moonRa, double moonDec, double sunRa, double sunDec)
    {
        double d2r = Math.PI / 180.0;
        double cos =
            Math.Sin(moonDec * d2r) * Math.Sin(sunDec * d2r) +
            Math.Cos(moonDec * d2r) * Math.Cos(sunDec * d2r) * Math.Cos((moonRa - sunRa) * d2r);
        double elongation = Math.Acos(Math.Clamp(cos, -1, 1));
        return (1.0 - Math.Cos(elongation)) / 2.0;
    }

    // ── Angular separation ────────────────────────────────────────────────────

    public static double AngularSeparationDeg(double ra1, double dec1, double ra2, double dec2)
    {
        double d2r = Math.PI / 180.0;
        double cos =
            Math.Sin(dec1 * d2r) * Math.Sin(dec2 * d2r) +
            Math.Cos(dec1 * d2r) * Math.Cos(dec2 * d2r) * Math.Cos((ra1 - ra2) * d2r);
        return Math.Acos(Math.Clamp(cos, -1, 1)) / d2r;
    }
}
