namespace AsiAirController.Models;

public record PlanSummary(int Id, string Name, bool IsEnabled, int TargetCount);

public record PlanSlot(string FrameType, int Filter, double ExpSec, int Gain, int Bin, int Repeat, int Lapsed);

public record PlanDetail(
    string Name,
    long TotalTimeSec,
    long LeftTimeSec,
    double TotalSizeMb,
    double LeftSizeMb,
    string StartTimeType,
    string EndTimeType,
    IReadOnlyList<string> TargetNames,
    IReadOnlyList<PlanSlot> Slots);
