using AsiAirController.Models;
using AsiAirController.Services;

namespace AsiAirController.Planning;

/// <summary>
/// Autonomously selects DSO targets for a given night, creates an ASI Air plan,
/// and records session progress to the TargetProgressStore.
/// Designed to run as an optional mode inside AutopilotLoopAsync.
/// </summary>
public class AutoTargetPlanner
{
    private readonly CatalogService      _catalog;
    private readonly TargetProgressStore _progress;
    private readonly VisibilityService   _visibility;

    // Configuration constants
    private const int   StepMinutes   = 10;
    private const int   MaxTargets    = 3;   // multi-target mode ceiling

    public AutoTargetPlanner(
        CatalogService catalog,
        TargetProgressStore progress)
    {
        _catalog    = catalog;
        _progress   = progress;
        _visibility = new VisibilityService();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Score all catalog objects for tonight, apply filters, and return the
    /// ranked candidate list. Does NOT create any ASI Air plan.
    /// </summary>
    public async Task<IReadOnlyList<TargetCandidate>> RankCandidatesAsync(
        DateOnly observingDate,
        AppSettings settings,
        HorizonProfile horizon,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        await _catalog.EnsureLoadedAsync();
        ct.ThrowIfCancellationRequested();

        var site  = settings.ToObservationSite();
        var setup = settings.ToImagingSetup();
        var snr   = new SnrSettings();
        var allowedTypes = settings.GetPlannerObjectTypes();

        // ── Twilight + moon (computed once for the night) ─────────────────────
        var (darkStart, darkEnd) = AstronomyService.GetAstronomicalDarkness(observingDate, site);
        if (darkEnd <= darkStart)
        {
            log?.Invoke("AutoTargetPlanner: no astronomical darkness tonight");
            return [];
        }

        var midNight = darkStart + (darkEnd - darkStart) / 2;
        var (moonRa, moonDec, moonIllum) = AstronomyService.GetMoonPosition(midNight);
        log?.Invoke($"AutoTargetPlanner: dark {darkStart:HH:mm}–{darkEnd:HH:mm} UTC, moon {moonIllum:F0}%");

        // ── Pre-compute LST for batch visibility ───────────────────────────────
        var steps   = BuildTimeSteps(darkStart, darkEnd, StepMinutes);
        var lstHrs  = AstronomyService.ComputeLstHours(steps, site.LongitudeDegrees);
        var moonInfo = (moonRa, moonDec, moonIllum);

        // ── Filter catalog ────────────────────────────────────────────────────
        var objects = _catalog.GetAll()
            .Where(o => allowedTypes.Count == 0 || allowedTypes.Contains(o.Type))
            .Where(o => o.Type.IsImagingTarget())
            .ToList();

        log?.Invoke($"AutoTargetPlanner: scoring {objects.Count} candidates");

        // ── Batch score ────────────────────────────────────────────────────────
        var candidates = new List<TargetCandidate>(objects.Count);

        foreach (var obj in objects)
        {
            ct.ThrowIfCancellationRequested();

            var vis = VisibilityService.ComputeDsoFast(
                obj, darkStart, darkEnd, steps, lstHrs,
                site.LatitudeDegrees, horizon, StepMinutes, moonInfo);

            if (!vis.IsVisible) continue;
            if (vis.Duration.TotalHours < 2.0) continue;  // not worth imaging a short window
            if (vis.PeakAltitudeDegrees < settings.PlannerMinAltitudeDeg) continue;
            if (vis.MoonSeparationDegrees < settings.PlannerMinMoonSeparationDeg) continue;

            // FOV check — skip objects that are too large or tiny for the setup
            if (FovCalculator.IsUsable(setup) && obj.MajorAxisArcmin.HasValue)
                if (!FovCalculator.FitsInFov(setup, obj.MajorAxisArcmin.Value)) continue;

            double score       = VisibilityScorer.ComputeScore(obj, vis, site.BortleClass);
            double hoursImaged = _progress.GetTotalHours(obj.Name);
            var    snrEst      = SnrCalculator.Compute(setup, obj, site.BortleClass, snr);

            // Progress-weight: once a target has its SNR goal, de-prioritize it
            if (settings.PlannerSnrGoalHours > 0 && snrEst != null)
            {
                double goalHours = snrEst.Good;
                if (hoursImaged >= goalHours * 0.9)
                    continue;   // target essentially complete
                double completionFrac = Math.Clamp(hoursImaged / Math.Max(goalHours, 0.1), 0, 1);
                score *= (1.0 - completionFrac * 0.7);   // reduce score as target nears goal
            }

            candidates.Add(new TargetCandidate(obj, vis, score, hoursImaged, snrEst));
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        log?.Invoke($"AutoTargetPlanner: {candidates.Count} viable candidates, top: {candidates.FirstOrDefault()?.Object.DisplayName}");
        return candidates.AsReadOnly();
    }

    /// <summary>
    /// Select target(s) and create an ASI Air plan. Returns the new plan ID.
    /// </summary>
    public async Task<(IReadOnlyList<TargetCandidate> Selected, int PlanId)> SelectAndCreatePlanAsync(
        string host,
        DateOnly observingDate,
        AppSettings settings,
        HorizonProfile horizon,
        IReadOnlyList<PlanSummary> existingPlans,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var ranked = await RankCandidatesAsync(observingDate, settings, horizon, log, ct);

        if (ranked.Count == 0)
            throw new InvalidOperationException("No suitable targets found for tonight.");

        // Pick top target(s)
        int maxTargets = settings.PlannerMultiTarget ? MaxTargets : 1;
        var selected = ranked.Take(maxTargets).ToList();

        int planId = await CreatePlanAsync(host, observingDate, selected, settings, existingPlans, log, ct);
        return (selected.AsReadOnly(), planId);
    }

    /// <summary>
    /// Call after a night finishes to record how much was imaged.
    /// </summary>
    public void RecordSessionProgress(IReadOnlyList<TargetCandidate> selected, DateOnly date, double hoursImaged)
    {
        double hoursEach = hoursImaged / Math.Max(selected.Count, 1);
        foreach (var c in selected)
            _progress.RecordSession(c.Object.Name, date, hoursEach);
    }

    /// <summary>
    /// Create a plan from a library target with known RA/Dec (bypasses catalog lookup).
    /// </summary>
    public async Task<int> CreateLibraryPlanAsync(
        string host,
        string targetName,
        double raHours,
        double decDegrees,
        AppSettings settings,
        IReadOnlyList<PlanSummary> existingPlans,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        int planId = (existingPlans.Count > 0 ? existingPlans.Max(p => p.Id) : 0) + 1;
        string planName = $"{targetName} — {DateOnly.FromDateTime(DateTime.Now):MMM d}";

        var createCmd = new Plan.CreateOrUpdatePlan(
            PlanId: planId, Name: planName,
            StartTime: new PlanStartTime("dusk"), EndTime: new PlanEndTime("dawn"),
            Enable: false, MeridianFlip: true, MountGoHome: true,
            StartGuiding: true, CloseCooler: true, WaitForCooling: true);
        await AsiAirClient.CallAsync(host, createCmd, ct);
        log?.Invoke($"Library plan: created id={planId} '{planName}'");

        var target = new PlanTarget(
            Id: 1, Name: targetName,
            RaHours: raHours, DecDegrees: decDegrees,
            Sequences: [],
            Enable: true, OriginalName: targetName, CaptureIndex: 1);
        await AsiAirClient.CallAsync(host, new Plan.ImportPlanTarget(planId, target), ct);

        var filter = Enum.TryParse<FilterType>(settings.PlannerFilterType, out var ft) ? ft : FilterType.Broadband;
        var seqs = new[]
        {
            new PlanSequence(
                Id: 1, FrameType: "light", Filter: FilterTypeToSlot(filter),
                ExpSec: settings.PlannerSubExposureSec,
                Gain: -10000, Bin: settings.PlannerBinning,
                Repeat: 9999,   // bounded by plan's dawn end-time
                AutoExp: false, Enable: true, CaptureIndex: 1)
        };
        await AsiAirClient.CallAsync(host, new Plan.ImportPlanSequences(planId, target.Id, seqs), ct);
        log?.Invoke($"Library plan: added '{targetName}' ×{settings.PlannerSubExposureSec}s (runs to dawn)");

        return planId;
    }

    // ── Plan creation ─────────────────────────────────────────────────────────

    private static async Task<int> CreatePlanAsync(
        string host,
        DateOnly observingDate,
        IReadOnlyList<TargetCandidate> selected,
        AppSettings settings,
        IReadOnlyList<PlanSummary> existingPlans,
        Action<string>? log,
        CancellationToken ct)
    {
        int planId  = (existingPlans.Count > 0 ? existingPlans.Max(p => p.Id) : 0) + 1;
        string name = selected.Count == 1
            ? $"Auto — {selected[0].Object.DisplayName} — {observingDate:MMM d}"
            : $"Auto — Multi — {observingDate:MMM d}";

        // Step 1: create plan metadata
        var createCmd = new Plan.CreateOrUpdatePlan(
            PlanId: planId,
            Name: name,
            StartTime: new PlanStartTime("dusk"),
            EndTime:   new PlanEndTime("dawn"),
            Enable:         false,
            MeridianFlip:   true,
            MountGoHome:    true,
            StartGuiding:   true,
            CloseCooler:    true,
            WaitForCooling: true);
        await AsiAirClient.CallAsync(host, createCmd, ct);
        log?.Invoke($"AutoTargetPlanner: created plan id={planId} '{name}'");

        // Steps 2+3: add each target + sequences
        var filter  = Enum.TryParse<FilterType>(settings.PlannerFilterType, out var ft) ? ft : FilterType.Broadband;
        int filterSlot = FilterTypeToSlot(filter);

        for (int i = 0; i < selected.Count; i++)
        {
            var obj = selected[i].Object;
            var target = new PlanTarget(
                Id:           i + 1,
                Name:         obj.DisplayName,
                RaHours:      obj.RaHours,
                DecDegrees:   obj.DecDegrees,
                Sequences:    [],
                Enable:       true,
                OriginalName: obj.Name,
                CaptureIndex: i + 1);

            await AsiAirClient.CallAsync(host, new Plan.ImportPlanTarget(planId, target), ct);

            int repeat = (int)Math.Max(1, Math.Floor(
                selected[i].Visibility.Duration.TotalSeconds / settings.PlannerSubExposureSec));
            var seqs = new[]
            {
                new PlanSequence(
                    Id: 1, FrameType: "light", Filter: filterSlot,
                    ExpSec: settings.PlannerSubExposureSec,
                    Gain: -10000, Bin: settings.PlannerBinning,
                    Repeat: repeat,
                    AutoExp: false, Enable: true, CaptureIndex: 1)
            };
            await AsiAirClient.CallAsync(host, new Plan.ImportPlanSequences(planId, target.Id, seqs), ct);
            log?.Invoke($"AutoTargetPlanner: added target '{obj.DisplayName}' {repeat}×{settings.PlannerSubExposureSec}s ({selected[i].Visibility.Duration.TotalHours:F1}h window)");
        }

        return planId;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime[] BuildTimeSteps(DateTime darkStart, DateTime darkEnd, int stepMinutes)
    {
        var steps = new List<DateTime>();
        var t = darkStart;
        while (t <= darkEnd)
        {
            steps.Add(t);
            t = t.AddMinutes(stepMinutes);
        }
        return steps.ToArray();
    }

    private static int FilterTypeToSlot(FilterType filter) => filter switch
    {
        FilterType.Broadband => 0,
        FilterType.Ha        => 1,
        FilterType.Oiii      => 2,
        FilterType.Sii       => 3,
        _                    => 0,
    };
}

// ── Result types ──────────────────────────────────────────────────────────────

public class TargetCandidate
{
    public DeepSkyObject   Object        { get; }
    public VisibilityWindow Visibility   { get; }
    public double          Score         { get; }
    public double          HoursImaged   { get; }
    public SnrTimeEstimate? SnrEstimate  { get; }

    public TargetCandidate(
        DeepSkyObject obj, VisibilityWindow vis,
        double score, double hoursImaged, SnrTimeEstimate? snr)
    {
        Object      = obj;
        Visibility  = vis;
        Score       = score;
        HoursImaged = hoursImaged;
        SnrEstimate = snr;
    }

    public string Summary =>
        $"{Object.DisplayName} ({Object.Type.ToShortString()}) — score {Score:F0}" +
        $" | vis {Visibility.Duration.TotalHours:F1}h | peak {Visibility.PeakAltitudeDegrees:F0}°" +
        $" | moon {Visibility.MoonSeparationDegrees:F0}°" +
        (HoursImaged > 0 ? $" | {HoursImaged:F1}h imaged" : "");
}
