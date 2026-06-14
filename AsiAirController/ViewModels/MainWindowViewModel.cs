using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
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

    // Kasa dew heater
    [ObservableProperty] private string _kasaEmail    = string.Empty;
    [ObservableProperty] private string _kasaPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KasaConnected))]
    [NotifyPropertyChangedFor(nameof(HasKasaDevices))]
    [NotifyCanExecuteChangedFor(nameof(ToggleDewHeaterCommand))]
    private IReadOnlyList<KasaDevice> _kasaDevices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KasaConnected))]
    [NotifyCanExecuteChangedFor(nameof(ToggleDewHeaterCommand))]
    private KasaDevice? _selectedKasaDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DewHeaterButtonText))]
    private bool _isDewHeaterOn;

    [ObservableProperty] private bool   _isDewHeaterStateKnown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleDewHeaterCommand))]
    private bool _isKasaBusy;

    [ObservableProperty] private string _kasaStatusMessage = string.Empty;
    [ObservableProperty] private bool   _isSettingsOpen;

    // Weather monitoring
    [ObservableProperty] private string _weatherFilePath    = string.Empty;
    [ObservableProperty] private string _dewMarginDisplay   = "3";
    [ObservableProperty] private bool   _isWeatherMonitoring;
    [ObservableProperty] private string _weatherMonitorStatus = string.Empty;
    [ObservableProperty] private string _weatherCurrentText    = "Checking weather…";
    [ObservableProperty] private string _weatherUpdatedText   = string.Empty;
    [ObservableProperty] private string _weatherNextCheckText = string.Empty;
    [ObservableProperty] private bool   _hasWeatherData;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DewMarginUnitText))]
    private bool _useFahrenheit;

    public string DewMarginUnitText => UseFahrenheit ? "°F of dew point" : "°C of dew point";

    // Notifications
    [ObservableProperty] private string _discordWebhookUrl = string.Empty;

    // AutoFocus status
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private bool   _isAutoFocusActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private string _autoFocusStatus = string.Empty;

    // Guiding status
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private bool   _isGuiding;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private string _guideStatus = string.Empty;
    private DateTime _lastGuideStepAt = DateTime.MinValue;

    public bool HasTrackingData =>
        IsAutoFocusActive || !string.IsNullOrEmpty(AutoFocusStatus) ||
        IsGuiding        || !string.IsNullOrEmpty(GuideStatus);

    // Auto Run
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoRunWaiting))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunRunning))]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopAutoRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private bool _isAutoRunActive;

    [ObservableProperty] private string _autoRunStatus        = string.Empty;
    [ObservableProperty] private string _autoRunNextCheckText = string.Empty;
    private CancellationTokenSource? _autoRunCts;

    public bool IsAutoRunWaiting => IsAutoRunActive && !IsPlanRunning;
    public bool IsAutoRunRunning => IsAutoRunActive && IsPlanRunning;

    // Session log
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogChevron))]
    private bool _isLogExpanded = true;

    public string LogChevron => IsLogExpanded ? "▼" : "▶";

    [RelayCommand]
    private void ToggleLog() => IsLogExpanded = !IsLogExpanded;

    private bool    _updatingMarginDisplay;
    private string? _kasaToken;
    private bool    _kasaCredentialsChanged;
    private bool    _dewHeaterAutoControlled;
    private CancellationTokenSource? _weatherPollCts;
    private WeatherData? _lastWeatherData;
    private string? _lastRoofPollStatus;

    public bool   KasaConnected      => _kasaToken != null && SelectedKasaDevice != null;
    public bool   HasKasaDevices     => KasaDevices.Count > 0;
    public string DewHeaterButtonText => IsDewHeaterOn ? "Turn Off" : "Turn On";

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
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    private PlanDetail? _activePlanDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlansEmptyState))]
    private bool _isLoadingPlans;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanRunning))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunWaiting))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunRunning))]
    [NotifyPropertyChangedFor(nameof(AutoRunButtonText))]
    [NotifyCanExecuteChangedFor(nameof(SetActivePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    private bool _isImagingActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanRunning))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunWaiting))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunRunning))]
    [NotifyPropertyChangedFor(nameof(AutoRunButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
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
    [NotifyPropertyChangedFor(nameof(ExposureCountdownText))]
    private string _captureState = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuskCountdownText))]
    [NotifyPropertyChangedFor(nameof(ExposureCountdownText))]
    private long _captureLapseMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuskCountdownText))]
    [NotifyPropertyChangedFor(nameof(ExposureCountdownText))]
    private long _captureTotalMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TogglePreviewButtonText))]
    private bool _isPreviewActive;
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private string _previewStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgressValue;
    private CancellationTokenSource? _previewCts;
    private DateTime _lastDiscordImageAt = DateTime.MinValue;
    private CancellationTokenSource? _exposureCountdownCts;
    // Set by Sequence:frame_complete push event so the preview loop can trigger a download in plan mode
    // (is_working never goes false in plan mode, so the normal wasWorking→!isWorking trigger never fires)
    private volatile bool _pendingImageDownload;

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

    public bool   IsPlanRunning        => IsImagingActive && ExposureMode == "autosave";
    public string AutoRunButtonText    => IsPlanRunning ? "▶  Resume Monitoring" : "▶  Auto Run";
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

    public string ExposureCountdownText
    {
        get
        {
            if (CaptureState != "expose" || CaptureTotalMs <= 0) return string.Empty;
            var remainingSec = Math.Max(0, (CaptureTotalMs - CaptureLapseMs) / 1000);
            return $"Exposing — {remainingSec}s remaining";
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
        KasaEmail       = _settings.KasaEmail;
        KasaPassword    = _settings.KasaPassword;
        WeatherFilePath    = _settings.WeatherFilePath;
        UseFahrenheit      = _settings.UseFahrenheit;
        DiscordWebhookUrl  = _settings.DiscordWebhookUrl;
        _updatingMarginDisplay = true;
        DewMarginDisplay = MarginToDisplay(_settings.DewMarginC);
        _updatingMarginDisplay = false;
        // RoofKey is intentionally set after the list arrives in LoadRoofKeysAsync,
        // so the ComboBox sees a real "" → "roof5" change and selects the item.
        _ = LoadRoofKeysAsync();
        if (!string.IsNullOrEmpty(_settings.IpAddress))
            _ = StartupConnectionsAsync();
        if (!string.IsNullOrEmpty(_settings.KasaEmail) && !string.IsNullOrEmpty(_settings.KasaPassword))
            _ = ConnectKasaAsync();

        // Weather polling runs from launch — always shows current conditions
        _weatherPollCts = new CancellationTokenSource();
        _ = Task.Run(() => WeatherPollLoopAsync(_weatherPollCts.Token));

        SessionLog.EntryAdded += entry =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(entry);
                if (LogEntries.Count > 500) LogEntries.RemoveAt(0);
            });
            if (!string.IsNullOrEmpty(_settings.DiscordWebhookUrl))
                _ = DiscordClient.PostAsync(_settings.DiscordWebhookUrl, entry);
        };

        AsiAirClient.AsiAirEvent += OnAsiAirEvent;

        SessionLog.Add(LogLevel.Info, "App started");
    }

    private async Task StartupConnectionsAsync()
    {
        var host = _settings.IpAddress.Trim();
        // Wait for the app to finish rendering and the VPN/network to be ready
        await Task.Delay(500);
        Dispatcher.UIThread.Post(() => _ = TogglePreviewAsync());
        // Wait for the TCP connection + test_connection handshake to complete
        // before sending plan list commands on the same socket
        await Task.Delay(750);
        await LoadPlansAsync();
        // Open the mount (port 4400) connection so GuideStep push events start flowing
        try { await AsiAirClient.EnsureMountConnectedAsync(host); } catch { }
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

    partial void OnIpAddressChanged(string value)          { _settings.IpAddress          = value; _settings.Save(); }
    partial void OnRoofStatusFilePathChanged(string value)  { _settings.RoofStatusFilePath  = value; _settings.Save(); }

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

    partial void OnKasaEmailChanged(string value)    { _settings.KasaEmail    = value; _settings.Save(); _kasaCredentialsChanged = true; }
    partial void OnKasaPasswordChanged(string value) { _settings.KasaPassword = value; _settings.Save(); _kasaCredentialsChanged = true; }

    partial void OnUseFahrenheitChanged(bool value)
    {
        _settings.UseFahrenheit = value;
        _settings.Save();
        _updatingMarginDisplay = true;
        DewMarginDisplay = MarginToDisplay(_settings.DewMarginC);
        _updatingMarginDisplay = false;
        RefreshWeatherDisplay();
    }

    partial void OnWeatherFilePathChanged(string value)   { _settings.WeatherFilePath   = value; _settings.Save(); }
    partial void OnDiscordWebhookUrlChanged(string value) { _settings.DiscordWebhookUrl = value; _settings.Save(); }

    partial void OnDewMarginDisplayChanged(string value)
    {
        if (_updatingMarginDisplay) return;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return;
        // Convert display unit back to °C for storage (margin is ratio only, no +32 offset)
        _settings.DewMarginC = UseFahrenheit ? d * 5.0 / 9.0 : d;
        _settings.Save();
    }

    // Converts stored °C margin to the current display unit
    private string MarginToDisplay(double marginC) => UseFahrenheit
        ? (marginC * 9.0 / 5.0).ToString("F1", CultureInfo.InvariantCulture)
        : marginC.ToString("F1", CultureInfo.InvariantCulture);

    // Start/stop weather monitoring whenever plan-running state changes
    partial void OnIsImagingActiveChanged(bool value) => UpdateDewMonitoring();
    partial void OnExposureModeChanged(string value)  => UpdateDewMonitoring();

    partial void OnSelectedKasaDeviceChanged(KasaDevice? value)
    {
        if (value == null) return;
        _settings.KasaDeviceId     = value.DeviceId;
        _settings.KasaDeviceAlias  = value.Alias;
        _settings.KasaAppServerUrl = value.AppServerUrl;
        _settings.KasaChildId      = value.ChildId ?? string.Empty;
        _settings.Save();
        OnPropertyChanged(nameof(KasaConnected));
        UpdateDewMonitoring();
        _ = RefreshDewHeaterStateAsync();
    }

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private void OpenLogFolder()
    {
        var folder = SessionLog.LogFolder;
        Directory.CreateDirectory(folder);
        try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); }
        catch { /* non-fatal */ }
    }

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;

        if (!string.IsNullOrWhiteSpace(KasaEmail) && !string.IsNullOrWhiteSpace(KasaPassword))
        {
            if (_kasaCredentialsChanged || _kasaToken == null)
            {
                _kasaCredentialsChanged = false;
                _ = ConnectKasaAsync();
            }
        }

        // Start preview and load plans when IP is entered for the first time
        if (!string.IsNullOrWhiteSpace(IpAddress))
        {
            if (!IsPreviewActive) _ = TogglePreviewAsync();
            if (Plans.Count == 0 && !IsLoadingPlans) _ = LoadPlansAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectKasaAsync()
    {
        if (string.IsNullOrWhiteSpace(KasaEmail) || string.IsNullOrWhiteSpace(KasaPassword))
        {
            KasaStatusMessage = "Enter Kasa email and password.";
            return;
        }
        IsKasaBusy = true;
        KasaStatusMessage = "Connecting to Kasa…";
        try
        {
            _kasaToken = await KasaCloudClient.LoginAsync(KasaEmail, KasaPassword);
            var devices = await KasaCloudClient.GetDevicesAsync(_kasaToken);
            Dispatcher.UIThread.Post(() =>
            {
                KasaDevices = devices;
                // Restore previously selected device (or plug) if it's still in the list
                var savedChildId = string.IsNullOrEmpty(_settings.KasaChildId) ? null : _settings.KasaChildId;
                var saved = devices.FirstOrDefault(d => d.DeviceId == _settings.KasaDeviceId && d.ChildId == savedChildId);
                if (saved != null)
                    SelectedKasaDevice = saved;
                OnPropertyChanged(nameof(KasaConnected));
                KasaStatusMessage = $"Connected — {devices.Count} device(s) found.";
                UpdateDewMonitoring();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _kasaToken = null;
                OnPropertyChanged(nameof(KasaConnected));
                KasaStatusMessage = $"Kasa connection failed: {ex.Message}";
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsKasaBusy = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleDewHeater))]
    private async Task ToggleDewHeaterAsync()
    {
        if (_kasaToken == null || SelectedKasaDevice == null) return;
        IsKasaBusy = true;
        var target = !IsDewHeaterOn;
        KasaStatusMessage = target ? "Turning dew heater on…" : "Turning dew heater off…";
        try
        {
            await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, target);
            SessionLog.Add(LogLevel.Info, $"Dew heater {(target ? "ON" : "OFF")} (manual)");
            Dispatcher.UIThread.Post(() =>
            {
                IsDewHeaterOn          = target;
                IsDewHeaterStateKnown  = true;
                KasaStatusMessage      = target ? "Dew heater on." : "Dew heater off.";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => KasaStatusMessage = $"Toggle failed: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsKasaBusy = false);
        }
    }

    private bool CanToggleDewHeater() => _kasaToken != null && SelectedKasaDevice != null && !IsKasaBusy;

    private async Task RefreshDewHeaterStateAsync()
    {
        if (_kasaToken == null || SelectedKasaDevice == null) return;
        try
        {
            var state = await KasaCloudClient.GetRelayStateAsync(_kasaToken, SelectedKasaDevice);
            if (state.HasValue)
                Dispatcher.UIThread.Post(() =>
                {
                    IsDewHeaterOn         = state.Value;
                    IsDewHeaterStateKnown = true;
                });
        }
        catch { /* non-fatal — state badge just stays unknown */ }
    }

    // ── Temperature formatting ──────────────────────────────────────────────

    private string FormatTemp(double celsius) => UseFahrenheit
        ? $"{celsius * 9.0 / 5.0 + 32:F1}°F"
        : $"{celsius:F1}°C";

    // Temperature differences (e.g. margin) scale by 9/5 only — no +32 offset
    private string FormatMargin(double deltaC) => UseFahrenheit
        ? $"{deltaC * 9.0 / 5.0:F1}°F"
        : $"{deltaC:F1}°C";

    private void RefreshWeatherDisplay()
    {
        var w = _lastWeatherData;
        if (w?.TemperatureC == null || w.DewPointC == null) return;
        var margin = w.TemperatureC.Value - w.DewPointC.Value;

        WeatherCurrentText   = BuildConditionLine(w, margin);
        WeatherMonitorStatus = $"{FormatTemp(w.TemperatureC.Value)}  dew {FormatTemp(w.DewPointC.Value)}  Δ{FormatMargin(margin)}  [{w.Source}]";
    }

    private string BuildConditionLine(WeatherData w, double margin)
    {
        var parts = new List<string>
        {
            $"Temp  {FormatTemp(w.TemperatureC!.Value)}",
            $"Dew Point  {FormatTemp(w.DewPointC!.Value)}",
            $"Dew Margin  {FormatMargin(margin)}"
        };
        if (w.HumidityPct.HasValue)
            parts.Add($"Humidity  {w.HumidityPct.Value:F0}%");

        var line = string.Join("  ·  ", parts);

        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(w.CloudText)) conditions.Add($"Sky  {w.CloudText}");
        if (!string.IsNullOrEmpty(w.WindText))  conditions.Add($"Wind  {w.WindText}");
        var condLine = string.Join("  ·  ", conditions);

        return string.IsNullOrEmpty(condLine) ? line : $"{line}\n{condLine}";
    }

    // ── Weather polling (always running) ───────────────────────────────────

    private void UpdateDewMonitoring()
    {
        // Update the auto-control badge
        IsWeatherMonitoring = IsPlanRunning && KasaConnected;

        // When plan ends, immediately turn off heater if we auto-controlled it on
        if (!IsPlanRunning && _dewHeaterAutoControlled)
        {
            _dewHeaterAutoControlled = false;
            if (_kasaToken != null && SelectedKasaDevice != null && IsDewHeaterOn)
            {
                SessionLog.Add(LogLevel.Info, "Dew heater OFF — plan ended (auto)");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, false);
                        Dispatcher.UIThread.Post(() => { IsDewHeaterOn = false; IsDewHeaterStateKnown = true; });
                    }
                    catch { /* non-fatal */ }
                });
            }
        }
    }

    // Counts down `seconds` ticking onTick every second. Returns false if cancelled.
    private static async Task<bool> CountdownAsync(int seconds, Action<string> onTick, CancellationToken ct)
    {
        for (var s = seconds; s > 0; s--)
        {
            onTick($"Next check in {s / 60}:{s % 60:D2}");
            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { onTick(string.Empty); return false; }
        }
        onTick(string.Empty);
        return true;
    }

    private async Task WeatherPollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Dispatcher.UIThread.Post(() => WeatherNextCheckText = string.Empty);
            await CheckWeatherAsync(ct);
            if (!await CountdownAsync(120,
                    t => Dispatcher.UIThread.Post(() => WeatherNextCheckText = t), ct))
                break;
        }
    }

    private async Task CheckWeatherAsync(CancellationToken ct)
    {
        try
        {
            var weather = await WeatherClient.GetBestAsync(WeatherFilePath.Trim(), ct);
            if (weather?.TemperatureC == null || weather.DewPointC == null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WeatherCurrentText = "No weather data available";
                    WeatherUpdatedText = $"Last attempt: {DateTime.Now:HH:mm:ss}";
                    HasWeatherData     = false;
                });
                return;
            }

            var margin    = weather.TemperatureC.Value - weather.DewPointC.Value;
            var threshold = _settings.DewMarginC;
            var shouldOn  = margin <= threshold;

            _lastWeatherData = weather;
            var condLine    = BuildConditionLine(weather, margin);
            var dataTime    = weather.Timestamp.HasValue ? weather.Timestamp.Value.ToString("HH:mm:ss") : DateTime.Now.ToString("HH:mm:ss");
            var updatedLine = $"[{weather.Source}]  {dataTime}";
            var monitorLine = $"{FormatTemp(weather.TemperatureC.Value)}  dew {FormatTemp(weather.DewPointC.Value)}  Δ{FormatMargin(margin)}  [{weather.Source}]";

            Dispatcher.UIThread.Post(() =>
            {
                WeatherCurrentText   = condLine;
                WeatherUpdatedText   = updatedLine;
                WeatherMonitorStatus = monitorLine;
                HasWeatherData       = true;
            });

            // Auto-control heater only while a plan is actively running
            if (!IsPlanRunning || _kasaToken == null || SelectedKasaDevice == null || IsKasaBusy) return;
            if (!IsDewHeaterStateKnown || shouldOn != IsDewHeaterOn)
            {
                await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, shouldOn);
                _dewHeaterAutoControlled = shouldOn;
                SessionLog.Add(LogLevel.Info, $"Dew heater {(shouldOn ? "ON" : "OFF")} — margin Δ{FormatMargin(margin)} (auto)");
                Dispatcher.UIThread.Post(() => { IsDewHeaterOn = shouldOn; IsDewHeaterStateKnown = true; });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                WeatherCurrentText = $"Error: {ex.Message}";
                WeatherUpdatedText = DateTime.Now.ToString("HH:mm:ss");
                HasWeatherData     = false;
            });
        }
    }

    // ── Auto Run ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartAutoRun))]
    private void StartAutoRun()
    {
        _autoRunCts?.Cancel();
        _autoRunCts = new CancellationTokenSource();
        IsAutoRunActive = true;
        var resuming = IsPlanRunning;
        AutoRunStatus = resuming ? "Resuming monitoring of running plan…" : "Checking roof status…";
        SessionLog.Add(LogLevel.Info, resuming ? "Auto Run resumed — plan already running" : "Auto Run started");
        _ = Task.Run(() => AutoRunLoopAsync(_autoRunCts.Token, resuming));
    }

    private bool CanStartAutoRun() =>
        !IsAutoRunActive && !IsBusy &&
        !string.IsNullOrEmpty(IpAddress) &&
        (!string.IsNullOrEmpty(RoofKey) || !string.IsNullOrEmpty(RoofStatusFilePath)) &&
        (IsPlanRunning || HasActivePlan);

    [RelayCommand(CanExecute = nameof(CanStopAutoRun))]
    private async Task StopAutoRunAsync()
    {
        _autoRunCts?.Cancel();
        var wasRunning = IsPlanRunning;
        if (wasRunning)
        {
            AutoRunStatus = "Cancelling — shutting down…";
            await PerformAutoRunShutdownAsync();
        }
        IsAutoRunActive = false;
        AutoRunStatus = wasRunning ? "Cancelled — mount parked, heater off." : "Cancelled.";
        SessionLog.Add(LogLevel.Info, wasRunning ? "Auto Run cancelled — shutdown complete" : "Auto Run cancelled");
    }

    private bool CanStopAutoRun() => IsAutoRunActive;

    private async Task AutoRunLoopAsync(CancellationToken ct, bool resumeFromRunning = false)
    {
        var planStarted    = resumeFromRunning;
        var planWasRunning = resumeFromRunning;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => AutoRunNextCheckText = string.Empty);

                // ── Roof check ────────────────────────────────────────────
                IReadOnlyList<RoofStatusResult> roofResults;
                try
                {
                    roofResults = await GetAllRoofResultsAsync(
                        RoofStatusFilePath.Trim(), RoofKey, ct);
                    if (roofResults.Count == 0)
                        throw new Exception("No roof status source available (file unreachable, API failed).");
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        AutoRunStatus = $"Roof check failed: {ex.Message}  (retrying…)");
                    try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch { break; }
                    continue;
                }

                var bestResult = roofResults.OrderByDescending(r => r.Timestamp ?? DateTime.MinValue).First();
                var roofStatus = bestResult.Status;
                var isOpen     = roofStatus == "OPEN";
                var checkedAt  = DateTime.Now.ToString("HH:mm");

                // ── State transitions ────────────────────────────────────
                if (!planStarted && isOpen)
                {
                    // Roof just opened — start the plan
                    SessionLog.Add(LogLevel.Info, "Roof open — Auto Run starting plan");
                    Dispatcher.UIThread.Post(() => AutoRunStatus = "Roof open — starting plan…");
                    try
                    {
                        await LaunchActivePlanAsync();
                        planStarted = true;
                        Dispatcher.UIThread.Post(() =>
                            AutoRunStatus = $"Plan running  ·  roof checked {checkedAt}");
                    }
                    catch (Exception ex)
                    {
                        SessionLog.Add(LogLevel.Error, $"Auto Run failed to start plan: {ex.Message}");
                        Dispatcher.UIThread.Post(() =>
                        {
                            IsAutoRunActive = false;
                            AutoRunStatus = $"Failed to start plan: {ex.Message}";
                        });
                        return;
                    }
                }
                else if (planStarted && !isOpen)
                {
                    // Any source reporting closed is enough — abort immediately
                    SessionLog.Add(LogLevel.Warning, $"Roof {roofStatus} at {checkedAt} [{bestResult.Source}] — Auto Run shutdown triggered");
                    Dispatcher.UIThread.Post(() =>
                        AutoRunStatus = $"Roof {roofStatus} — shutting down…");
                    await PerformAutoRunShutdownAsync();
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsAutoRunActive = false;
                        AutoRunStatus = $"Stopped — roof {roofStatus} at {checkedAt}  ·  mount parked, heater off";
                    });
                    return;
                }
                else if (planStarted && isOpen && planWasRunning && !IsPlanRunning)
                {
                    // IsPlanRunning went false — could be plan done OR a meridian flip/autofocus/goto
                    // pause. Query get_app_state to confirm before declaring completion.
                    bool planStillActive;
                    bool isMeridFlip = false;
                    bool isFocusing  = false;
                    try
                    {
                        (planStillActive, isMeridFlip, isFocusing) =
                            await AsiAirClient.QueryPlanSubstateAsync(IpAddress.Trim(), ct);
                    }
                    catch
                    {
                        planStillActive = true; // if query fails, assume still active (safe default)
                    }

                    if (planStillActive)
                    {
                        var pauseReason = isMeridFlip ? "Meridian flip"
                                        : isFocusing  ? "Focusing"
                                        : "Activity pause";
                        SessionLog.Add(LogLevel.Info, $"{pauseReason} in progress — continuing to monitor");
                        Dispatcher.UIThread.Post(() =>
                            AutoRunStatus = $"{pauseReason} in progress  ·  roof {roofStatus}  ·  checked {checkedAt}");
                    }
                    else
                    {
                        // Confirmed done — heater off, no park (roof is still open)
                        _dewHeaterAutoControlled = false;
                        if (_kasaToken != null && SelectedKasaDevice != null && IsDewHeaterOn)
                        {
                            try
                            {
                                await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, false);
                                Dispatcher.UIThread.Post(() => { IsDewHeaterOn = false; IsDewHeaterStateKnown = true; });
                            }
                            catch { }
                        }
                        SessionLog.Add(LogLevel.Info, $"Plan completed — Auto Run ended at {checkedAt}");
                        Dispatcher.UIThread.Post(() =>
                        {
                            IsAutoRunActive = false;
                            AutoRunStatus = $"Plan completed at {checkedAt}  ·  heater off";
                        });
                        return;
                    }
                }
                else
                {
                    if (planStarted && IsPlanRunning) planWasRunning = true;
                    Dispatcher.UIThread.Post(() => AutoRunStatus = planStarted
                        ? IsPlanRunning
                            ? $"Plan running  ·  roof {roofStatus}  ·  checked {checkedAt}"
                            : $"Plan waiting to image  ·  roof {roofStatus}  ·  checked {checkedAt}"
                        : $"Waiting for roof  ·  currently {roofStatus}  ·  checked {checkedAt}");
                }

                if (!await CountdownAsync(60,
                        t => Dispatcher.UIThread.Post(() => AutoRunNextCheckText = t), ct))
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsAutoRunActive) IsAutoRunActive = false;
                AutoRunNextCheckText = string.Empty;
            });
        }
    }

    private async Task LaunchActivePlanAsync()
    {
        var host       = IpAddress.Trim();
        var activePlan = Plans.FirstOrDefault(p => p.IsEnabled)
            ?? throw new InvalidOperationException("No active plan selected.");

        await AsiAirClient.SetPageAsync(host, "plan");
        await AsiAirClient.ResetPlanAsync(host, activePlan.Id);
        Dispatcher.UIThread.Post(() => StatusMessage = $"Starting {activePlan.Name}…");
        await AsiAirClient.StartPlanAsync(host);
        SessionLog.Add(LogLevel.Info, $"Plan started: {activePlan.Name}");
        await LoadPlansAsync();
    }

    private async Task PerformAutoRunShutdownAsync()
    {
        _dewHeaterAutoControlled = false;
        if (_kasaToken != null && SelectedKasaDevice != null && IsDewHeaterOn)
        {
            try
            {
                await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, false);
                Dispatcher.UIThread.Post(() => { IsDewHeaterOn = false; IsDewHeaterStateKnown = true; });
            }
            catch { }
        }
        var host = IpAddress.Trim();
        try { await AsiAirClient.CallAsync(host, new Capture.StopExposure()); } catch { }
        try { await AsiAirClient.CallAsync(host, new Mount.ScopePark()); } catch { }
    }

    private bool CanAct() => !IsBusy && !string.IsNullOrWhiteSpace(IpAddress);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task StopExposureAsync()
    {
        await FireAsync(new Capture.StopExposure(), "Stopping exposure…", "Exposure stopped.");
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ParkMountAsync()
    {
        await FireAsync(new Mount.ScopePark(), "Parking mount…", "Park command sent.");
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SafeShutdownAsync()
    {
        // Turn off dew heater before shutdown
        _dewHeaterAutoControlled = false;
        if (_kasaToken != null && SelectedKasaDevice != null && IsDewHeaterOn)
        {
            try
            {
                await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedKasaDevice, false);
                Dispatcher.UIThread.Post(() => { IsDewHeaterOn = false; IsDewHeaterStateKnown = true; });
            }
            catch { /* non-fatal */ }
        }

        SessionLog.Add(LogLevel.Warning, "Manual safe shutdown initiated");
        IsBusy = true;
        var host = IpAddress.Trim();
        try
        {
            StatusMessage = "Safe shutdown: stopping exposure…";
            try { await AsiAirClient.CallAsync(host, new Capture.StopExposure()); }
            catch { /* non-fatal if nothing is exposing */ }

            StatusMessage = "Safe shutdown: parking mount…";
            await AsiAirClient.CallAsync(host, new Mount.ScopePark());
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

                if (status != _lastRoofPollStatus)
                {
                    SessionLog.Add(status == "OPEN" ? LogLevel.Info : LogLevel.Warning,
                        $"Roof status: {status} [{result.Source}]");
                    _lastRoofPollStatus = status;
                }

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

    private static async Task<IReadOnlyList<RoofStatusResult>> GetAllRoofResultsAsync(string filePath, string roofKey, CancellationToken ct)
    {
        var tasks = new List<Task<RoofStatusResult>>();
        if (!string.IsNullOrEmpty(filePath))
        {
            // 5-second timeout: unmounted SMB drives can hang for 30+ seconds before the OS gives up
            tasks.Add(AsiAirClient.ReadRoofStatusAsync(filePath).WaitAsync(TimeSpan.FromSeconds(5), ct));
        }
        if (!string.IsNullOrEmpty(roofKey))
            tasks.Add(AsiAirClient.FetchRoofStatusFromApiAsync(roofKey, ct));

        var rawResults = await Task.WhenAll(tasks.Select(async t =>
        {
            try { return (RoofStatusResult?)await t; }
            catch { return null; }
        }));

        return rawResults.OfType<RoofStatusResult>().ToList();
    }

    private static async Task<RoofStatusResult> GetBestRoofStatusAsync(string filePath, string roofKey, CancellationToken ct)
    {
        var results = await GetAllRoofResultsAsync(filePath, roofKey, ct);
        if (results.Count == 0)
            throw new Exception("No roof status source available (file unreachable, API failed).");
        return results.OrderByDescending(r => r.Timestamp ?? DateTime.MinValue).First();
    }

    // Returns true only when all reachable sources say closed AND at least one reading is < 5 min old.
    // Prevents aborting a session on a single stale or split-brain reading.
    private async Task TriggerSafeShutdownAsync(string roofStatus)
    {
        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host))
        {
            StatusMessage = $"Roof is {roofStatus} but no IP set — cannot auto-shutdown!";
            return;
        }

        SessionLog.Add(LogLevel.Warning, $"Auto-shutdown triggered — roof was {roofStatus}");
        StatusMessage = $"⚠ Roof is {roofStatus} — stopping exposure…";
        try { await AsiAirClient.CallAsync(host, new Capture.StopExposure()); }
        catch { /* non-fatal */ }

        StatusMessage = $"⚠ Roof is {roofStatus} — parking mount…";
        try
        {
            await AsiAirClient.CallAsync(host, new Mount.ScopePark());
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
        !IsConfirmingStartPlan && !string.IsNullOrEmpty(IpAddress) &&
        !IsAutoRunActive;

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
            SessionLog.Add(LogLevel.Info, $"Plan started manually: {activePlan.Name}");
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
                var (isWorking, captureState, exposureMode, completedFrames, totalFrames, lapseMs, totalMs, lastAfHfr) = await AsiAirClient.QueryCaptureStateAsync(host, ct);
                Dispatcher.UIThread.Post(() =>
                {
                    IsImagingActive     = isWorking;
                    CaptureState        = captureState;
                    ExposureMode        = exposureMode;
                    LiveCompletedFrames = completedFrames;
                    LiveTotalFrames     = totalFrames;
                    CaptureLapseMs      = lapseMs;
                    CaptureTotalMs      = totalMs;
                    // Seed AF status from device on connect (only if not set by a live push event)
                    if (lastAfHfr.HasValue && !IsAutoFocusActive && string.IsNullOrEmpty(AutoFocusStatus))
                        AutoFocusStatus = $"HFR {lastAfHfr.Value:F2}";
                });

                // Single-shot: is_working goes false when exposure finishes.
                // Plan mode: is_working never goes false; use the Sequence:frame_complete push event flag instead.
                bool downloadNow = (wasWorking && !isWorking) || _pendingImageDownload;
                if (downloadNow) _pendingImageDownload = false;

                if (downloadNow)
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

                        var webhookUrl = _settings.DiscordWebhookUrl;
                        if (!string.IsNullOrEmpty(webhookUrl) && (now - _lastDiscordImageAt).TotalHours >= 1)
                        {
                            _lastDiscordImageAt = now;
                            _ = DiscordClient.PostImageAsync(webhookUrl, bitmap, $"[{now:HH:mm}] Preview image");
                        }
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

            // Clear guiding active if no GuideStep received in last 15 seconds (covers download gap)
            if (IsGuiding && (DateTime.Now - _lastGuideStepAt).TotalSeconds > 15)
                Dispatcher.UIThread.Post(() => IsGuiding = false);

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

    private void OnAsiAirEvent(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            switch (node?["Event"]?.GetValue<string>())
            {
                case "AutoFocus":
                {
                    var state = node["state"]?.GetValue<string>();
                    if (state == "working")
                    {
                        Dispatcher.UIThread.Post(() => { IsAutoFocusActive = true; AutoFocusStatus = "Running…"; });
                    }
                    else if (state == "complete")
                    {
                        var hfr = node["result"]?["last_point"]?.AsArray()?[1]?.GetValue<double>();
                        var ts  = DateTime.Now.ToString("HH:mm");
                        var status = hfr.HasValue ? $"HFR {hfr.Value:F2}  ·  {ts}" : $"Done  ·  {ts}";
                        SessionLog.Add(LogLevel.Info, $"Autofocus complete — {status}");
                        Dispatcher.UIThread.Post(() => { IsAutoFocusActive = false; AutoFocusStatus = status; });
                    }
                    break;
                }
                case "GuideStep":
                {
                    var avgDist = node["AvgDist"]?.GetValue<double>();
                    _lastGuideStepAt = DateTime.Now;
                    if (avgDist.HasValue)
                    {
                        var status = $"{avgDist.Value:F2}″ avg";
                        Dispatcher.UIThread.Post(() => { IsGuiding = true; GuideStatus = status; });
                    }
                    break;
                }
                case "Exposure":
                {
                    var state = node["state"]?.GetValue<string>();
                    if (state == "start")
                    {
                        var expUs   = node["exp_us"]?.GetValue<long>() ?? 0;
                        var totalSec = (int)Math.Ceiling(expUs / 1_000_000.0);
                        _exposureCountdownCts?.Cancel();
                        _exposureCountdownCts = new CancellationTokenSource();
                        var countdownCt = _exposureCountdownCts.Token;
                        _ = Task.Run(async () =>
                        {
                            for (int r = totalSec; r > 0 && !countdownCt.IsCancellationRequested; r--)
                            {
                                int rem = r;
                                Dispatcher.UIThread.Post(() => StatusMessage = $"Exposing… {rem}s remaining");
                                try { await Task.Delay(1000, countdownCt); }
                                catch (OperationCanceledException) { return; }
                            }
                            if (!countdownCt.IsCancellationRequested)
                                Dispatcher.UIThread.Post(() => StatusMessage = string.Empty);
                        });
                    }
                    else if (state == "downloading")
                    {
                        _exposureCountdownCts?.Cancel();
                        Dispatcher.UIThread.Post(() => StatusMessage = "Downloading…");
                    }
                    else if (state == "complete")
                    {
                        _exposureCountdownCts?.Cancel();
                        Dispatcher.UIThread.Post(() => StatusMessage = string.Empty);
                    }
                    break;
                }
                case "Sequence":
                {
                    var seqState = node["state"]?.GetValue<string>();
                    if (seqState == "frame_complete" || seqState == "frame_start")
                    {
                        var plan  = node["progress"]?["cur_plan"];
                        var total = plan?["total"]?.GetValue<int>() ?? 0;
                        var lapse = plan?["lapse"]?.GetValue<int>() ?? 0;
                        if (total > 0)
                            Dispatcher.UIThread.Post(() => { LiveCompletedFrames = lapse; LiveTotalFrames = total; });
                    }
                    // Trigger preview download for plan mode — is_working never goes false in a plan,
                    // so the normal wasWorking→!isWorking transition never fires.
                    if (seqState == "frame_complete")
                        _pendingImageDownload = true;
                    break;
                }
            }
        }
        catch { }
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
            var response = await AsiAirClient.CallAsync(host, new Mount.ScopeGetInfo());
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

    private async Task FireAsync(AsiAirCommand cmd, string startMsg, string doneMsg)
    {
        var host = IpAddress.Trim();
        IsBusy = true;
        StatusMessage = startMsg;
        try
        {
            _settings.IpAddress = host;
            _settings.Save();
            await AsiAirClient.CallAsync(host, cmd);
            StatusMessage = doneMsg;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            SessionLog.Add(LogLevel.Error, $"Command failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
