namespace AsiAirController.Planning;

public class ObservationSite
{
    public string Name { get; set; } = "My Location";
    public double LatitudeDegrees { get; set; }
    public double LongitudeDegrees { get; set; }
    public double ElevationMeters { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int? BortleClass { get; set; }

    public TimeZoneInfo GetTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch { return TimeZoneInfo.Utc; }
    }
}
