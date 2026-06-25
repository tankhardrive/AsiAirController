namespace AsiAirController.Planning;

public class VisibilityWindow
{
    public static readonly VisibilityWindow NotComputed  = new() { IsComputed = false };
    public static readonly VisibilityWindow NeverVisible = new() { IsComputed = true, Duration = TimeSpan.Zero };

    public bool IsComputed { get; init; }
    public bool IsVisible  => IsComputed && Duration > TimeSpan.Zero;

    public DateTime  DarkWindowStart          { get; init; }
    public DateTime  DarkWindowEnd            { get; init; }
    public DateTime? RiseTime                 { get; init; }
    public DateTime? SetTime                  { get; init; }
    public TimeSpan  Duration                 { get; init; }
    public double    AverageAltitudeDegrees   { get; init; }
    public double    PeakAltitudeDegrees      { get; init; }
    public double    PeakAzimuthDegrees       { get; init; }
    public double    PeakClearanceDegrees     { get; init; }
    public DateTime  PeakTime                 { get; init; }
    public double    MoonSeparationDegrees    { get; init; }
    public double    VisibilityFraction       { get; init; }
}
