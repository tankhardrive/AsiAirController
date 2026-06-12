using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AsiAirController.Imaging;
using AsiAirController.Models;
using AsiAirController.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsiAirController.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int RoofPollIntervalSeconds = 300;

    private readonly AppSettings _settings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopExposureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkMountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SafeShutdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetActivePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private string _ipAddress = string.Empty;

    [ObservableProperty] private string _roofStatusFilePath = string.Empty;
    [ObservableProperty] private string _roofKey = string.Empty;
    [ObservableProperty] private List<string> _availableRoofKeys = new();
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopExposureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkMountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SafeShutdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollRoofButtonText))]
    private bool _isPollingRoof;
    [ObservableProperty] private string _roofPollStatus = string.Empty;
    private CancellationTokenSource? _roofPollCts;

    [ObservableProperty] private string _exposureSeconds = "10";

    // Plans
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InactivePlans))]
    [NotifyPropertyChangedFor(nameof(ShowPlansEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(SetActivePlanCommand))]
    private IReadOnlyList<PlanSummary> _plans = new List<PlanSummary>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivePlan))]
    [NotifyPropertyChangedFor(nameof(ActivePlanProgressFraction))]
    [NotifyPropertyChangedFor(nameof(ActivePlanProgressText))]
    [NotifyPropertyChangedFor(nameof(ActivePlanTimeText))]
    [NotifyPropertyChangedFor(nameof(ActivePlanDataText))]
    [NotifyPropertyChangedFor(nameof(ActivePlanScheduleText))]
    [NotifyPropertyChangedFor(nameof(ActivePlanTargetsText))]
    [NotifyPropertyChangedFor(nameof(ActivePlanSlots))]
    [NotifyPropertyChangedFor(nameof(StartPlanConfirmText))]
    [NotifyPropertyChangedFor(nameof(LiveProgressFraction))]
    [NotifyPropertyChangedFor(nameof(LiveFrameText))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private PlanDetail? _activePlanDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlansEmptyState))]
    private bool _isLoadingPlans;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanRunning))]
    [NotifyCanExecuteChangedFor(nameof(SetActivePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private bool _isImagingActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanRunning))]
    private string _exposureMode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveProgressFraction))]
    [NotifyPropertyChangedFor(nameof(LiveFrameText))]
    private int _liveCompletedFrames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveProgressFraction))]
    [NotifyPropertyChangedFor(nameof(LiveFrameText))]
    private int _liveTotalFrames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForDusk))]
    [NotifyPropertyChangedFor(nameof(DuskCountdownText))]
    private string _captureState = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuskCountdownText))]
    private long _captureLapseMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuskCountdownText))]
    private long _captureTotalMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TogglePreviewButtonText))]
    private bool _isPreviewActive;
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private string _previewStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgressValue;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _exposureCountdownCts;

    // Expected compressed size of a full-res IMX571 raw ZIP (~35.7 MB from capture analysis)
    private const long ExpectedCompressedBytes = 36_000_000L;

    public string PollRoofButtonText      => IsPollingRoof  ? "Stop Polling" : "Poll Roof";
    public string TogglePreviewButtonText => IsPreviewActive ? "Stop Preview" : "Live Preview";

    public IReadOnlyList<PlanSummary> InactivePlans       => Plans.Where(p => !p.IsEnabled).ToList();
    public bool                       HasActivePlan       => ActivePlanDetail != null;
    public bool                       ShowPlansEmptyState => !IsLoadingPlans && Plans.Count == 0;

    public double LiveProgressFraction => LiveTotalFrames > 0
        ? LiveCompletedFrames / (double)LiveTotalFrames
        : ActivePlanProgressFraction;

    public string LiveFrameText => LiveTotalFrames > 0
        ? $"{LiveCompletedFrames} / {LiveTotalFrames} frames"
        : ActivePlanProgressText;

    public bool   IsPlanRunning     => IsImagingActive && ExposureMode == "autosave";
    public bool   IsWaitingForDusk  => CaptureState == "target_delay";

    public string DuskCountdownText
    {
        get
        {
            if (!IsWaitingForDusk || CaptureTotalMs <= 0) return string.Empty;
            var remainingSec = Math.Max(0, (CaptureTotalMs - CaptureLapseMs) / 1000);
            return $"Waiting for dusk — {FormatDuration(remainingSec)} remaining";
        }
    }

    public double ActivePlanProgressFraction
    {
        get
        {
            if (ActivePlanDetail == null || ActivePlanDetail.TotalTimeSec == 0) return 0;
            return 1.0 - ActivePlanDetail.LeftTimeSec / (double)ActivePlanDetail.TotalTimeSec;
        }
    }

    public string ActivePlanProgressText
    {
        get
        {
            if (ActivePlanDetail?.Slots.Count > 0)
            {
                int total = ActivePlanDetail.Slots.Sum(s => s.Repeat);
                int done  = ActivePlanDetail.Slots.Sum(s => s.Lapsed);
                if (total > 0) return $"{done} / {total} frames";
            }
            return string.Empty;
        }
    }

    public string ActivePlanTimeText => ActivePlanDetail == null ? string.Empty :
        $"Time:  {FormatDuration(ActivePlanDetail.LeftTimeSec)} remaining  ({FormatDuration(ActivePlanDetail.TotalTimeSec)} total)";

    public string ActivePlanDataText => ActivePlanDetail == null ? string.Empty :
        $"Data:  {FormatSize(ActivePlanDetail.LeftSizeMb)} remaining  ({FormatSize(ActivePlanDetail.TotalSizeMb)} total)";

    public string ActivePlanScheduleText
    {
        get
        {
            if (ActivePlanDetail == null) return string.Empty;
            var start = ActivePlanDetail.StartTimeType switch
            {
                "none" => "no start time",
                "dusk" => "at dusk",
                _      => ActivePlanDetail.StartTimeType
            };
            var end = ActivePlanDetail.EndTimeType switch
            {
                "dawn" => "at dawn",
                "none" => "no end time",
                _      => ActivePlanDetail.EndTimeType
            };
            return $"Schedule:  starts {start}  ·  ends {end}";
        }
    }

    public string ActivePlanTargetsText => ActivePlanDetail?.TargetNames.Count > 0
        ? $"Target{(ActivePlanDetail.TargetNames.Count > 1 ? "s" : "")}:  {string.Join(", ", ActivePlanDetail.TargetNames)}"
        : string.Empty;

    public IReadOnlyList<string> ActivePlanSlots
    {
        get
        {
            if (ActivePlanDetail == null) return [];
            return ActivePlanDetail.Slots.Select(s =>
            {
                var gain = s.Gain < 0 ? "auto gain" : $"gain {s.Gain}";
                var exp  = s.ExpSec >= 1 ? $"{s.ExpSec:0}s" : $"{s.ExpSec * 1000:0}ms";
                var type = s.FrameType.Length > 0
                    ? char.ToUpper(s.FrameType[0]) + s.FrameType[1..]
                    : s.FrameType;
                return $"· {s.Repeat}×  {type}  {exp}  {gain}  bin {s.Bin}  filter {s.Filter}";
            }).ToArray();
        }
    }

    private static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds <= 0) return "0m";
        var h = totalSeconds / 3600;
        var m = (totalSeconds % 3600) / 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string FormatSize(double mb) =>
        mb >= 1024 ? $"{mb / 1024:F1} GB" : $"{mb:F0} MB";

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        IpAddress = _settings.IpAddress;
        RoofStatusFilePath = _settings.RoofStatusFilePath;
        ExposureSeconds = _settings.ExposureSeconds;
        // RoofKey is intentionally set after the list arrives in LoadRoofKeysAsync,
        // so the ComboBox sees a real "" → "roof5" change and selects the item.
        _ = LoadRoofKeysAsync();
        if (!string.IsNullOrEmpty(_settings.IpAddress))
        {
            _ = TogglePreviewAsync();
            _ = LoadPlansAsync();
        }
    }

    private async Task LoadRoofKeysAsync()
    {
        try
        {
            var keys = await AsiAirClient.FetchRoofKeysAsync();
            Dispatcher.UIThread.Post(() =>
            {
                AvailableRoofKeys = keys.ToList();
                // Set RoofKey now that the list exists — goes from "" to the saved value,
                // which is a real change so the ComboBox will select the right item.
                var saved = _settings.RoofKey;
                if (!string.IsNullOrEmpty(saved) && keys.Contains(saved))
                    RoofKey = saved;
            });
        }
        catch { /* leave list empty if API unreachable at startup */ }
    }

    partial void OnRoofKeyChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _settings.RoofKey = value;
        _settings.Save();
    }

    partial void OnExposureSecondsChanged(string value)
    {
        _settings.ExposureSeconds = value;
        _settings.Save();
    }

    private bool CanAct() => !IsBusy && !string.IsNullOrWhiteSpace(IpAddress);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task StopExposureAsync()
    {
        await FireAsync(4700, "stop_exposure", "Stopping exposure…", "Exposure stopped.");
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ParkMountAsync()
    {
        await FireAsync(4400, "scope_park", "Parking mount…", "Park command sent.");
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SafeShutdownAsync()
    {
        IsBusy = true;
        var host = IpAddress.Trim();
        try
        {
            StatusMessage = "Safe shutdown: stopping exposure…";
            try { await AsiAirClient.SendCommandAsync(host, 4700, "stop_exposure"); }
            catch { /* non-fatal if nothing is exposing */ }

            StatusMessage = "Safe shutdown: parking mount…";
            await AsiAirClient.SendCommandAsync(host, 4400, "scope_park");
            StatusMessage = "Safe shutdown complete. Park command sent.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Shutdown failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PollRoofAsync()
    {
        if (IsPollingRoof)
        {
            _roofPollCts?.Cancel();
            IsPollingRoof = false;
            RoofPollStatus = string.Empty;
            StatusMessage = "Roof polling stopped.";
            return;
        }

        if (string.IsNullOrEmpty(RoofKey)) { StatusMessage = "Select a roof from the dropdown first."; return; }

        var path = RoofStatusFilePath.Trim();
        _settings.RoofStatusFilePath = path;
        _settings.Save();

        _roofPollCts = new CancellationTokenSource();
        IsPollingRoof = true;
        StatusMessage = "Roof polling started…";

        _ = Task.Run(() => RoofPollLoopAsync(path, RoofKey, _roofPollCts.Token));
    }

    [RelayCommand]
    private async Task CheckRoofStatusAsync()
    {
        if (string.IsNullOrEmpty(RoofKey)) { StatusMessage = "Select a roof first."; return; }
        IsBusy = true;
        StatusMessage = "Checking roof status…";
        try
        {
            var result = await GetBestRoofStatusAsync(RoofStatusFilePath.Trim(), RoofKey, default);
            var ts = result.Timestamp.HasValue ? result.Timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss") : "unknown time";
            StatusMessage = $"{RoofKey}: {result.Status}  —  {ts} [{result.Source}]";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Status check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RoofPollLoopAsync(string path, string roofKey, CancellationToken ct)
    {
        var shutdownTriggered = false;
        while (!ct.IsCancellationRequested)
        {
            Dispatcher.UIThread.Post(() => RoofPollStatus = "Checking roof…");
            try
            {
                var result = await GetBestRoofStatusAsync(path, roofKey, ct);
                var status = result.Status;

                if (!shutdownTriggered && status != "OPEN")
                {
                    shutdownTriggered = true;
                    await TriggerSafeShutdownAsync(status);
                }
                else
                {
                    var checkedAt = DateTime.Now;
                    Dispatcher.UIThread.Post(() =>
                        StatusMessage = $"Roof: {status}  —  last checked {checkedAt:HH:mm:ss} [{result.Source}]");
                }

                if (status == "OPEN") shutdownTriggered = false;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusMessage = $"Roof poll error: {ex.Message}");
            }

            // Countdown to next poll
            for (var secs = RoofPollIntervalSeconds; secs > 0 && !ct.IsCancellationRequested; secs--)
            {
                var s = secs;
                Dispatcher.UIThread.Post(() => RoofPollStatus = $"Next roof check in {s}s");
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsPollingRoof = false;
            RoofPollStatus = string.Empty;
        });
    }

    private static async Task<RoofStatusResult> GetBestRoofStatusAsync(string filePath, string roofKey, CancellationToken ct)
    {
        var tasks = new List<Task<RoofStatusResult>>();
        if (!string.IsNullOrEmpty(filePath))
            tasks.Add(AsiAirClient.ReadRoofStatusAsync(filePath));
        if (!string.IsNullOrEmpty(roofKey))
            tasks.Add(AsiAirClient.FetchRoofStatusFromApiAsync(roofKey, ct));

        var rawResults = await Task.WhenAll(tasks.Select(async t =>
        {
            try { return (RoofStatusResult?)await t; }
            catch { return null; }
        }));

        var results = rawResults.OfType<RoofStatusResult>().ToList();
        if (results.Count == 0)
            throw new Exception("No roof status source available (file unreachable, API failed).");

        return results.OrderByDescending(r => r.Timestamp ?? DateTime.MinValue).First();
    }

    private async Task TriggerSafeShutdownAsync(string roofStatus)
    {
        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host))
        {
            StatusMessage = $"Roof is {roofStatus} but no IP set — cannot auto-shutdown!";
            return;
        }

        StatusMessage = $"⚠ Roof is {roofStatus} — stopping exposure…";
        try { await AsiAirClient.SendCommandAsync(host, 4700, "stop_exposure"); }
        catch { /* non-fatal */ }

        StatusMessage = $"⚠ Roof is {roofStatus} — parking mount…";
        try
        {
            await AsiAirClient.SendCommandAsync(host, 4400, "scope_park");
            StatusMessage = $"Auto-shutdown complete. Roof was {roofStatus}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-shutdown failed during park: {ex.Message}";
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private bool _isConfirmingStartPlan;

    public string StartPlanConfirmText
    {
        get
        {
            if (ActivePlanDetail == null) return string.Empty;
            int done  = ActivePlanDetail.Slots.Sum(s => s.Lapsed);
            int total = ActivePlanDetail.Slots.Sum(s => s.Repeat);
            return done > 0
                ? $"Reset {ActivePlanDetail.Name} ({done}/{total} frames done) and start from the beginning?"
                : $"Start {ActivePlanDetail.Name}?";
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowStartPlanConfirm))]
    private void ShowStartPlanConfirm() => IsConfirmingStartPlan = true;

    private bool CanShowStartPlanConfirm() =>
        HasActivePlan && !IsImagingActive && !IsBusy &&
        !IsConfirmingStartPlan && !string.IsNullOrEmpty(IpAddress);

    [RelayCommand]
    private void CancelStartPlanConfirm() => IsConfirmingStartPlan = false;

    [RelayCommand]
    private async Task ConfirmStartPlanAsync()
    {
        IsConfirmingStartPlan = false;
        var host       = IpAddress.Trim();
        var activePlan = Plans.FirstOrDefault(p => p.IsEnabled);
        if (activePlan == null) return;

        IsBusy = true;
        StatusMessage = $"Resetting {activePlan.Name}…";
        try
        {
            await AsiAirClient.SetPageAsync(host, "plan");
            await AsiAirClient.ResetPlanAsync(host, activePlan.Id);
            Dispatcher.UIThread.Post(() => StatusMessage = $"Starting {activePlan.Name}…");
            await AsiAirClient.StartPlanAsync(host);
            await LoadPlansAsync();
            Dispatcher.UIThread.Post(() => StatusMessage = $"{activePlan.Name} started.");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Start failed: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsBusy = false);
        }
    }

    [RelayCommand]
    private async Task LoadPlansAsync()
    {
        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host)) { StatusMessage = "Enter an IP address first."; return; }

        Dispatcher.UIThread.Post(() => IsLoadingPlans = true);
        try
        {
            var plans = await AsiAirClient.ListPlansAsync(host);
            PlanDetail? detail = null;
            if (plans.Any(p => p.IsEnabled))
                detail = await AsiAirClient.GetActivePlanDetailAsync(host);

            Dispatcher.UIThread.Post(() =>
            {
                Plans = plans;
                ActivePlanDetail = detail;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Plan load failed: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoadingPlans = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetActivePlan))]
    private async Task SetActivePlanAsync(PlanSummary plan)
    {
        var host = IpAddress.Trim();
        Dispatcher.UIThread.Post(() => StatusMessage = $"Switching to {plan.Name}…");
        try
        {
            await AsiAirClient.SwapActivePlanAsync(host, Plans, plan.Id);
            await LoadPlansAsync();
            Dispatcher.UIThread.Post(() => StatusMessage = $"Active plan: {plan.Name}.");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Plan swap failed: {ex.Message}");
        }
    }

    private bool CanSetActivePlan(PlanSummary? plan) =>
        plan != null && !plan.IsEnabled && !IsImagingActive && !string.IsNullOrEmpty(IpAddress);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task TakeImageAsync()
    {
        if (!double.TryParse(ExposureSeconds, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var secs) || secs <= 0)
        {
            StatusMessage = "Enter a positive exposure time in seconds.";
            return;
        }

        var host = IpAddress.Trim();
        IsBusy = true;
        try
        {
            StatusMessage = $"Starting {secs}s exposure…";
            await AsiAirClient.SetPageAsync(host, "preview");
            await AsiAirClient.StartExposureAsync(host, (long)(secs * 1_000_000));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture failed: {ex.Message}";
            IsBusy = false;
            return;
        }
        IsBusy = false;

        // Countdown until the exposure finishes — cancelled by the preview loop when download starts
        _exposureCountdownCts?.Cancel();
        _exposureCountdownCts = new CancellationTokenSource();
        var countdownCt = _exposureCountdownCts.Token;
        _ = Task.Run(async () =>
        {
            for (int r = (int)Math.Ceiling(secs); r > 0 && !countdownCt.IsCancellationRequested; r--)
            {
                int remaining = r;
                Dispatcher.UIThread.Post(() => StatusMessage = $"Exposing… {remaining}s remaining");
                try { await Task.Delay(1000, countdownCt); }
                catch (OperationCanceledException) { return; }
            }
            // Loop finished naturally — clear the message
            if (!countdownCt.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => StatusMessage = string.Empty);
        });
    }

    [RelayCommand]
    private Task TogglePreviewAsync()
    {
        if (IsPreviewActive)
        {
            _previewCts?.Cancel();
            IsPreviewActive = false;
            PreviewStatus = string.Empty;
            return Task.CompletedTask;
        }

        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host)) { StatusMessage = "Enter an IP address first."; return Task.CompletedTask; }

        _previewCts = new CancellationTokenSource();
        IsPreviewActive = true;
        _ = Task.Run(() => PreviewLoopAsync(host, _previewCts.Token));
        return Task.CompletedTask;
    }

    private async Task PreviewLoopAsync(string host, CancellationToken ct)
    {
        bool wasWorking = false;
        Dispatcher.UIThread.Post(() => PreviewStatus = "Waiting for exposure...");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (isWorking, captureState, exposureMode, completedFrames, totalFrames, lapseMs, totalMs) = await AsiAirClient.QueryCaptureStateAsync(host, ct);
                Dispatcher.UIThread.Post(() =>
                {
                    IsImagingActive     = isWorking;
                    CaptureState        = captureState;
                    ExposureMode        = exposureMode;
                    LiveCompletedFrames = completedFrames;
                    LiveTotalFrames     = totalFrames;
                    CaptureLapseMs      = lapseMs;
                    CaptureTotalMs      = totalMs;
                });

                if (wasWorking && !isWorking)
                {
                    _exposureCountdownCts?.Cancel();
                    Dispatcher.UIThread.Post(() => { StatusMessage = string.Empty; PreviewStatus = "Downloading image..."; IsDownloading = true; });

                    var downloadProgress = new Progress<long>(bytes =>
                        Dispatcher.UIThread.Post(() =>
                            DownloadProgressValue = Math.Min(1.0, bytes / (double)ExpectedCompressedBytes)));
                    try
                    {
                        var rawData = await AsiAirClient.FetchRawImageAsync(host, downloadProgress, ct);
                        Dispatcher.UIThread.Post(() => PreviewStatus = "Processing...");
                        var bitmap = await Task.Run(() => RawDebayer.Debayer(rawData), ct);
                        var now = DateTime.Now;
                        Dispatcher.UIThread.Post(() =>
                        {
                            var old = PreviewBitmap;
                            PreviewBitmap = bitmap;
                            old?.Dispose();
                            PreviewStatus = $"Last preview: {now:HH:mm:ss}";
                        });
                    }
                    finally
                    {
                        Dispatcher.UIThread.Post(() => { IsDownloading = false; DownloadProgressValue = 0; });
                    }
                }

                wasWorking = isWorking;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { PreviewStatus = $"Error: {ex.Message}"; IsDownloading = false; DownloadProgressValue = 0; });
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsPreviewActive     = false;
            IsImagingActive     = false;
            CaptureState        = string.Empty;
            ExposureMode        = string.Empty;
            LiveCompletedFrames = 0;
            LiveTotalFrames     = 0;
            CaptureLapseMs      = 0;
            CaptureTotalMs      = 0;
            PreviewStatus       = string.Empty;
        });
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host)) { StatusMessage = "Enter an IP address first."; return; }

        IsBusy = true;
        StatusMessage = "Testing connection…";
        try
        {
            var response = await AsiAirClient.QueryAsync(host, 4400, "scope_get_info");
            StatusMessage = $"OK — {response}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FireAsync(int port, string method, string startMsg, string doneMsg)
    {
        var host = IpAddress.Trim();
        IsBusy = true;
        StatusMessage = startMsg;
        try
        {
            _settings.IpAddress = host;
            _settings.Save();
            await AsiAirClient.SendCommandAsync(host, port, method);
            StatusMessage = doneMsg;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
