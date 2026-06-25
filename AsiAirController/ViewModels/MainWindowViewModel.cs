using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AsiAirController.Imaging;
using AsiAirController.Models;
using AsiAirController.Planning;
using AsiAirController.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsiAirController.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopExposureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkMountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SafeShutdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetActivePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    [NotifyPropertyChangedFor(nameof(ShowAutoRunSetupHint))]
    private string _starfrontBuildingIdText = "5";
    [ObservableProperty] private string _observatoryTimeZoneId  = "America/Chicago";
    [ObservableProperty] private string _observatoryTimeText    = "--:--";
    [ObservableProperty] private string _observatoryTime12Text  = "";
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopExposureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkMountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SafeShutdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    private bool _isBusy;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoofBadgeColor))]
    private bool _roofIsOpen = false;
    [ObservableProperty] private string _roofBadgeText = string.Empty;

    public string RoofBadgeColor   => RoofIsOpen ? "#2ECC71" : "#C0392B";
    public bool   RoofBadgeVisible => int.TryParse(StarfrontBuildingIdText, out var id) && id > 0;

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

    [ObservableProperty] private bool _isDewHeaterStateKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImagingPowerConnected))]
    [NotifyCanExecuteChangedFor(nameof(ToggleImagingPowerCommand))]
    private KasaDevice? _selectedImagingDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImagingPowerButtonText))]
    private bool _isImagingPowerOn;

    [ObservableProperty] private bool _isImagingPowerKnown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsiAirPowerConnected))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAsiAirPowerCommand))]
    private KasaDevice? _selectedAsiAirDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsiAirPowerButtonText))]
    private bool _isAsiAirPowerOn;

    [ObservableProperty] private bool _isAsiAirPowerKnown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleDewHeaterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleImagingPowerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAsiAirPowerCommand))]
    private bool _isKasaBusy;

    [ObservableProperty] private string _kasaStatusMessage = string.Empty;
    [ObservableProperty] private bool   _isSettingsOpen;

    // Weather monitoring
    [ObservableProperty] private string _dewMarginDisplay   = "3";
    [ObservableProperty] private bool   _isWeatherMonitoring;
    [ObservableProperty] private string _weatherMonitorStatus = string.Empty;
    [ObservableProperty] private string _weatherCurrentText    = "Checking weather…";
    [ObservableProperty] private string _weatherUpdatedText   = string.Empty;
    [ObservableProperty] private string _weatherNextCheckText = string.Empty;
    [ObservableProperty] private bool   _hasWeatherData;
    [ObservableProperty] private bool   _weatherAlertActive;

    // Sun times (refreshed once per day)
    [ObservableProperty] private SunTimes? _sunTimes;
    [ObservableProperty] private string _timeToNightText = string.Empty;

    // StellarVision
    [ObservableProperty] private string _svImagingScoreText  = string.Empty;
    [ObservableProperty] private string _svSeeingText        = string.Empty;
    [ObservableProperty] private string _svSafetyText        = string.Empty;
    [ObservableProperty] private bool   _svIsSafe            = true;
    [ObservableProperty] private string _svMoonText          = string.Empty;
    [ObservableProperty] private string _svAuroraText        = string.Empty;
    [ObservableProperty] private string _svNwsForecast       = string.Empty;
    [ObservableProperty] private bool   _hasStellarVisionData;
    private CancellationTokenSource? _svCts;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DewMarginUnitText))]
    private bool _useFahrenheit;

    public string DewMarginUnitText => UseFahrenheit ? "°F of dew point" : "°C of dew point";

    // Camera cooling
    [ObservableProperty] private string _coolerPreCoolMinutesText = "20";
    [ObservableProperty] private string _coolerTargetTempText     = "-10";
    public string CoolerTempUnitText => UseFahrenheit ? "°F" : "°C";

    // Image sync
    [ObservableProperty] private bool   _imageSyncEnabled        = false;
    [ObservableProperty] private bool   _imageSyncAppendDateTime = false;
    [ObservableProperty] private string _imageSyncSourcePath     = string.Empty;
    [ObservableProperty] private string _imageSyncDestPath       = string.Empty;
    [ObservableProperty] private int    _previewImageMaxHeight   = 375;

    // Mount location (read from device on connect)
    [ObservableProperty] private string _mountLocationText = "Not connected";

    // Notifications
    [ObservableProperty] private string _discordWebhookUrl = string.Empty;

    // Observatory settings (for planner)
    [ObservableProperty] private string _observatoryName         = "My Observatory";
    [ObservableProperty] private string _observatoryLatitudeText  = "30.0";
    [ObservableProperty] private string _observatoryLongitudeText = "-97.0";
    [ObservableProperty] private string _observatoryBortleText    = "4";

    // Equipment settings (telescope)
    [ObservableProperty] private string _telescopeName            = "";
    [ObservableProperty] private string _telescopeApertureText    = "100";
    [ObservableProperty] private string _telescopeFocalLengthText = "500";

    // Equipment settings (camera)
    [ObservableProperty] private string _cameraName               = "";
    [ObservableProperty] private string _cameraPixelSizeText      = "3.76";
    [ObservableProperty] private string _cameraSensorWidthText    = "4144";
    [ObservableProperty] private string _cameraSensorHeightText   = "2822";

    // Planner imaging defaults
    [ObservableProperty] private string _plannerSubExposureText   = "300";
    [ObservableProperty] private string _plannerRepeatText        = "10";
    [ObservableProperty] private string _plannerFilterType        = "Broadband";

    // Planner preferences
    [ObservableProperty] private string _plannerHorizonFlatText        = "15";
    [ObservableProperty] private string _plannerMinAltitudeText       = "20";
    [ObservableProperty] private string _plannerMinMoonSepText        = "30";

    // AutoFocus status
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    [NotifyPropertyChangedFor(nameof(PlanStatusText))]
    [NotifyPropertyChangedFor(nameof(PlanStatusColor))]
    [NotifyPropertyChangedFor(nameof(HasPlanStatus))]
    private bool   _isAutoFocusActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private string _autoFocusStatus = string.Empty;

    // Guiding status
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    [NotifyPropertyChangedFor(nameof(PlanStatusText))]
    [NotifyPropertyChangedFor(nameof(PlanStatusColor))]
    [NotifyPropertyChangedFor(nameof(HasPlanStatus))]
    private bool   _isGuiding;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackingData))]
    private string _guideStatus = string.Empty;
    private DateTime  _lastGuideStepAt       = DateTime.MinValue;

    // Guide graph — rolling 500-point history of RA/Dec errors in arcseconds
    private const int GuideHistoryCapacity = 500;
    public record GuidePoint(double Ra, double Dec, bool IsSettle, bool IsDither);
    private readonly Queue<GuidePoint> _guidePointQueue = new();
    [ObservableProperty] private IReadOnlyList<GuidePoint> _guidePoints = Array.Empty<GuidePoint>();
    [ObservableProperty] private double _guideRmsRa;
    [ObservableProperty] private double _guideRmsDec;
    private DateTime? _planScheduledStartTime = null;
    private DateTime? _sessionDawnUtc         = null;

    public bool HasTrackingData =>
        IsAutoFocusActive || !string.IsNullOrEmpty(AutoFocusStatus) ||
        IsGuiding        || !string.IsNullOrEmpty(GuideStatus);

    // Plan session status — computed from activity flags set by push events and poll loop
    private bool _isWaitingForNight;
    private bool _isFindingTarget;
    private bool _isPlateSolveActive;
    private bool _isRestartingGuide;
    private bool _isSettling;
    private bool _isMeridFlipActive;
    private bool _isFrameExposing;

    public string PlanStatusText
    {
        get
        {
            if (_isMeridFlipActive)  return "Meridian flip";
            if (IsAutoFocusActive)   return "Autofocusing";
            if (_isPlateSolveActive) return "Plate solving";
            if (_isFindingTarget)    return "Finding target";
            if (_isRestartingGuide)  return "Restarting guide";
            if (_isSettling)         return "Guide settling";
            if (_isFrameExposing)    return "Imaging";
            if (IsGuiding)           return "Guiding";
            if (_isWaitingForNight)  return "Waiting for night";
            return string.Empty;
        }
    }

    public string PlanStatusColor
    {
        get
        {
            if (_isMeridFlipActive)  return "#D4943A";
            if (IsAutoFocusActive)   return "#D4943A";
            if (_isPlateSolveActive) return "#5BA3D4";
            if (_isFindingTarget)    return "#5BA3D4";
            if (_isRestartingGuide)  return "#D4943A";
            if (_isSettling)         return "#D4943A";
            if (_isFrameExposing)    return "#2ECC71";
            if (IsGuiding)           return "#2ECC71";
            if (_isWaitingForNight)  return "#888888";
            return "#888888";
        }
    }

    public bool HasPlanStatus => !string.IsNullOrEmpty(PlanStatusText);

    private void NotifyPlanStatus()
    {
        OnPropertyChanged(nameof(PlanStatusText));
        OnPropertyChanged(nameof(PlanStatusColor));
        OnPropertyChanged(nameof(HasPlanStatus));
    }

    private void ClearSessionActivityFlags()
    {
        _isWaitingForNight = _isFindingTarget = _isPlateSolveActive =
            _isRestartingGuide = _isSettling = _isMeridFlipActive = _isFrameExposing = false;
        _guidePointQueue.Clear();
        GuidePoints = Array.Empty<GuidePoint>();
        GuideRmsRa  = 0;
        GuideRmsDec = 0;
        NotifyPlanStatus();
    }

    // Auto Run
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoRunWaiting))]
    [NotifyPropertyChangedFor(nameof(IsAutoRunRunning))]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopAutoRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowStartPlanConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetActivePlanCommand))]
    private bool _isAutoRunActive;

    [ObservableProperty] private string _autoRunStatus        = string.Empty;
    [ObservableProperty] private string _autoRunNextCheckText = string.Empty;
    private CancellationTokenSource? _autoRunCts;
    private volatile string?         _discordThreadId;

    // Autopilot
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutopilotBannerVisible))]
    [NotifyCanExecuteChangedFor(nameof(StartAutoRunCommand))]
    private bool _isAutopilotActive;

    [ObservableProperty] private string _autopilotStatus     = string.Empty;
    [ObservableProperty] private string _autopilotNightLabel = string.Empty;
    [ObservableProperty] private string _autopilotNightCountText       = "0";
    [ObservableProperty] private string _autopilotPowerOnOffsetText    = "60";

    public bool AutopilotBannerVisible => IsAutopilotActive;
    public ObservableCollection<AutopilotNightEntry> AutopilotNights { get; } = new();

    private CancellationTokenSource? _autopilotCts;
    private DateTime? _lastKnownDuskUtc;
    private int       _autopilotNightsCompleted;
    private int       _autopilotQueueIndex;

    // Auto-target planner
    [ObservableProperty] private bool   _autoPlannerEnabled = false;
    [ObservableProperty] private string _autoPlannerStatus  = string.Empty;

    private readonly CatalogService      _catalogService      = new();
    private readonly TargetProgressStore _targetProgressStore = new();
    private HorizonProfile               _horizonProfile      = HorizonProfile.Flat(15);
    private AutoTargetPlanner?           _autoTargetPlanner;
    private IReadOnlyList<TargetCandidate>? _lastSelectedTargets;

    public bool IsAutoRunWaiting => IsAutoRunActive && !IsPlanRunning;
    public bool IsAutoRunRunning => IsAutoRunActive && IsPlanRunning;

    // Session log
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogChevron))]
    private bool _isLogExpanded = false;

    public string LogChevron => IsLogExpanded ? "▼" : "▶";

    [RelayCommand]
    private void ToggleLog() => IsLogExpanded = !IsLogExpanded;

    private bool    _updatingMarginDisplay;
    private string? _kasaToken;
    private bool    _kasaCredentialsChanged;
    private bool    _dewHeaterAutoControlled;
    private CancellationTokenSource? _weatherPollCts;
    private CancellationTokenSource? _roofDisplayCts;
    private WeatherData? _lastWeatherData;

    public bool   KasaConnected         => _kasaToken != null && SelectedKasaDevice != null;
    public bool   ImagingPowerConnected  => _kasaToken != null && SelectedImagingDevice != null;
    public bool   AsiAirPowerConnected   => _kasaToken != null && SelectedAsiAirDevice != null;
    public bool   HasKasaDevices         => KasaDevices.Count > 0;
    public string DewHeaterButtonText    => IsDewHeaterOn    ? "Turn Off" : "Turn On";
    public string ImagingPowerButtonText => IsImagingPowerOn ? "Turn Off" : "Turn On";
    public string AsiAirPowerButtonText  => IsAsiAirPowerOn  ? "Turn Off" : "Turn On";

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
    [NotifyCanExecuteChangedFor(nameof(ResetActivePlanCommand))]
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
    [NotifyPropertyChangedFor(nameof(CameraTemperatureText))]
    private double? _cameraTemperatureC;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraTemperatureText))]
    private int? _cameraCoolPowerPerc;

    public string CameraTemperatureText
    {
        get
        {
            if (CameraTemperatureC == null) return string.Empty;
            var text = $"Camera  {FormatTemp(CameraTemperatureC.Value)}";
            if (CameraCoolPowerPerc is > 0)
                text += $"  ·  Cooling {CameraCoolPowerPerc}%";
            return text;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PiTemperatureText))]
    private double? _piTemperatureC;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PiTemperatureText))]
    private bool _piIsUndervolt;

    public string PiTemperatureText
    {
        get
        {
            if (PiTemperatureC == null) return string.Empty;
            var text = $"Pi  {FormatTemp(PiTemperatureC.Value)}";
            if (PiIsUndervolt) text += "  ·  Undervolt!";
            return text;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageText))]
    private long? _storageTotalMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageText))]
    private long? _storageFreeMb;

    public string StorageText
    {
        get
        {
            if (StorageFreeMb == null || StorageTotalMb == null) return string.Empty;
            return $"Storage  {StorageFreeMb.Value / 1024.0:F0} GB free  ·  {StorageTotalMb.Value / 1024.0:F0} GB";
        }
    }

    [ObservableProperty]
    private bool _isPreviewActive;
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private string  _previewStatus = string.Empty;

    // Observatory cameras (Starfront snapshot API)
    [ObservableProperty] private Bitmap?  _buildingCamBitmap;
    [ObservableProperty] private Bitmap?  _allSkyCamBitmap;
    [ObservableProperty] private string   _buildingCamTimestamp = string.Empty;
    [ObservableProperty] private string   _allSkyCamTimestamp   = string.Empty;
    private CancellationTokenSource? _cameraCts;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgressValue;
    [ObservableProperty] private bool   _isSyncingManually;
    [ObservableProperty] private double _manualSyncProgress;
    private CancellationTokenSource? _manualSyncCts;
    public bool HasSyncPaths    => !string.IsNullOrWhiteSpace(ImageSyncSourcePath)
                                && !string.IsNullOrWhiteSpace(ImageSyncDestPath);
    public bool CanSyncManually => HasSyncPaths && !IsSyncingManually;
    public string SyncButtonText => IsSyncingManually ? "Copying…" : "Copy Images";
    private CancellationTokenSource? _previewCts;
    private DateTime _lastDiscordImageAt = DateTime.MinValue;
    private CancellationTokenSource? _exposureCountdownCts;
    // Set by Sequence:frame_complete push event so the preview loop can trigger a download in plan mode
    // (is_working never goes false in plan mode, so the normal wasWorking→!isWorking trigger never fires)
    private volatile bool _pendingImageDownload;

    // Expected compressed size of a full-res IMX571 raw ZIP (~35.7 MB from capture analysis)
    private const long ExpectedCompressedBytes = 36_000_000L;

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
        ExposureSeconds = _settings.ExposureSeconds;
        KasaEmail       = _settings.KasaEmail;
        KasaPassword    = _settings.KasaPassword;
        UseFahrenheit         = _settings.UseFahrenheit;
        DiscordWebhookUrl     = _settings.DiscordWebhookUrl;
        StarfrontBuildingIdText  = _settings.StarfrontBuildingId.ToString();
        if (_settings.StarfrontBuildingId > 0)
            RoofBadgeText = $"Building {_settings.StarfrontBuildingId} : CLOSED";
        ObservatoryTimeZoneId    = _settings.ObservatoryTimeZoneId;
        CoolerPreCoolMinutesText = _settings.CoolerPreCoolMinutes.ToString();
        CoolerTargetTempText     = TempCToDisplay(_settings.CoolerTargetTempC);
        ImageSyncEnabled        = _settings.ImageSyncEnabled;
        ImageSyncAppendDateTime = _settings.ImageSyncAppendDateTime;
        PreviewImageMaxHeight   = _settings.PreviewImageMaxHeight;
        ImageSyncSourcePath     = _settings.ImageSyncSourcePath;
        ImageSyncDestPath       = _settings.ImageSyncDestPath;
        AutopilotNightCountText    = _settings.AutopilotNightCount.ToString();
        AutopilotPowerOnOffsetText = _settings.AutopilotPowerOnOffsetMinutes.ToString();
        AutoPlannerEnabled         = _settings.PlannerEnabled;

        // Observatory + equipment + planner settings
        ObservatoryName          = _settings.ObservatoryName;
        ObservatoryLatitudeText  = _settings.ObservatoryLatitudeDeg.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        ObservatoryLongitudeText = _settings.ObservatoryLongitudeDeg.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        ObservatoryBortleText    = _settings.ObservatoryBortleClass.ToString();
        TelescopeName            = _settings.TelescopeName;
        TelescopeApertureText    = _settings.TelescopeApertureMm.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        TelescopeFocalLengthText = _settings.TelescopeFocalLengthMm.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        CameraName               = _settings.CameraName;
        CameraPixelSizeText      = _settings.CameraPixelSizeMicrons.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        CameraSensorWidthText    = _settings.CameraSensorWidthPixels.ToString();
        CameraSensorHeightText   = _settings.CameraSensorHeightPixels.ToString();
        PlannerSubExposureText   = _settings.PlannerSubExposureSec.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        PlannerRepeatText        = _settings.PlannerRepeatCount.ToString();
        PlannerFilterType        = _settings.PlannerFilterType;
        PlannerHorizonFlatText   = _settings.PlannerHorizonFlatDeg.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        PlannerMinAltitudeText   = _settings.PlannerMinAltitudeDeg.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        PlannerMinMoonSepText    = _settings.PlannerMinMoonSeparationDeg.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        _horizonProfile          = HorizonProfile.Flat(_settings.PlannerHorizonFlatDeg);

        _targetProgressStore.Load();
        _autoTargetPlanner = new AutoTargetPlanner(_catalogService, _targetProgressStore);
        _ = Task.Run(() => _catalogService.EnsureLoadedAsync());
        _updatingMarginDisplay = true;
        DewMarginDisplay = MarginToDisplay(_settings.DewMarginC);
        _updatingMarginDisplay = false;
        // Weather polling runs from launch — always shows current conditions
        _weatherPollCts = new CancellationTokenSource();
        _ = Task.Run(() => WeatherPollLoopAsync(_weatherPollCts.Token));

        // Observatory clock — always ticking in the configured timezone
        StartObservatoryClock();

        if (!string.IsNullOrEmpty(_settings.IpAddress))
            _ = StartupConnectionsAsync(); // starts observatory services after ASI Air is ready
        else
            StartObservatoryServices(); // no ASI Air — start immediately

        if (!string.IsNullOrEmpty(_settings.KasaEmail) && !string.IsNullOrEmpty(_settings.KasaPassword))
            _ = ConnectKasaAsync();

        SessionLog.EntryAdded += entry =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(entry);
                if (LogEntries.Count > 500) LogEntries.RemoveAt(0);
            });
            if (entry.Discord && !string.IsNullOrEmpty(_settings.DiscordWebhookUrl))
                _ = DiscordClient.PostAsync(_settings.DiscordWebhookUrl, entry, _discordThreadId);
        };

        AsiAirClient.AsiAirEvent += OnAsiAirEvent;

        SessionLog.Add(LogLevel.Info, "App started");
    }

    private async Task StartupConnectionsAsync()
    {
        var host = _settings.IpAddress.Trim();
        // Wait for the app to finish rendering and the VPN/network to be ready
        await Task.Delay(500);
        Dispatcher.UIThread.Post(() => StartPreview());
        // Wait for the TCP connection + test_connection handshake to complete
        // before sending plan list commands on the same socket
        await Task.Delay(750);
        await LoadPlansAsync();
        // Open the mount (port 4400) connection so GuideStep push events start flowing
        try { await AsiAirClient.EnsureMountConnectedAsync(host); } catch { }

        // Read stored location from the mount and display in settings
        try
        {
            var (lat, lon) = await AsiAirClient.QueryMountLocationAsync(host);
            var text = (lat.HasValue && lon.HasValue)
                ? $"{lat.Value:F4}°, {lon.Value:F4}°"
                : "Not set";
            Dispatcher.UIThread.Post(() => MountLocationText = text);
            SessionLog.Add(LogLevel.Info, $"Mount location: {text}", discord: false);
        }
        catch { }

        // ASI Air setup complete — now safe to kick off observatory API services
        StartObservatoryServices();
    }

    private void StartObservatoryServices()
    {
        _svCts = new CancellationTokenSource();
        _ = Task.Run(() => StellarVisionPollLoopAsync(_svCts.Token));
        StartCameraPolling();
        _roofDisplayCts = new CancellationTokenSource();
        _ = Task.Run(() => RoofDisplayPollLoopAsync(_roofDisplayCts.Token));
    }

    partial void OnIpAddressChanged(string value)          { _settings.IpAddress          = value; _settings.Save(); }

    partial void OnStarfrontBuildingIdTextChanged(string value)
    {
        _settings.StarfrontBuildingId = int.TryParse(value, out var id) ? id : 0;
        _settings.Save();
    }

    partial void OnObservatoryTimeZoneIdChanged(string value)
    {
        _settings.ObservatoryTimeZoneId = value;
        _settings.Save();
        SunTimes = null; // invalidate so timeline re-fetches with the new TZ
        UpdateObservatoryTime();
    }

    public TimeZoneInfo GetObservatoryTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(ObservatoryTimeZoneId); }
        catch { return TimeZoneInfo.Local; }
    }

    private void UpdateObservatoryTime()
    {
        var t = TimeZoneInfo.ConvertTime(DateTime.UtcNow, GetObservatoryTz());
        ObservatoryTimeText   = t.ToString("HH:mm");
        ObservatoryTime12Text = "(" + t.ToString("h:mm tt") + ")";

        var st = SunTimes;
        if (st == null)
        {
            TimeToNightText = string.Empty;
            return;
        }

        if (t < st.AstroDawn || t >= st.AstroDusk)
        {
            TimeToNightText = "Currently Night";
        }
        else
        {
            var remaining = st.AstroDusk - t;
            var h = (int)remaining.TotalHours;
            var m = remaining.Minutes;
            TimeToNightText = h > 0 ? $"Time to Night: {h}:{m:D2}" : $"Time to Night: {m}m";
        }
    }

    partial void OnSunTimesChanged(SunTimes? value) => UpdateObservatoryTime();

    private void StartObservatoryClock()
    {
        UpdateObservatoryTime();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => UpdateObservatoryTime();
        timer.Start();
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
        CoolerTargetTempText = TempCToDisplay(_settings.CoolerTargetTempC);
        OnPropertyChanged(nameof(CoolerTempUnitText));
        RefreshWeatherDisplay();
        OnPropertyChanged(nameof(CameraTemperatureText));
    }

    partial void OnCoolerPreCoolMinutesTextChanged(string value)
    {
        if (int.TryParse(value, out var m) && m >= 0)
        {
            _settings.CoolerPreCoolMinutes = m;
            _settings.Save();
        }
    }

    partial void OnCoolerTargetTempTextChanged(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return;
        _settings.CoolerTargetTempC = UseFahrenheit ? (d - 32) * 5.0 / 9.0 : d;
        _settings.Save();
    }

    // Converts a Celsius value to the user's preferred display unit string.
    private string TempCToDisplay(double celsius) =>
        UseFahrenheit
            ? (celsius * 9.0 / 5.0 + 32).ToString("F0")
            : celsius.ToString("F0");

    partial void OnDiscordWebhookUrlChanged(string value)   { _settings.DiscordWebhookUrl   = value; _settings.Save(); }
    partial void OnPreviewImageMaxHeightChanged(int value)      { _settings.PreviewImageMaxHeight   = value; _settings.Save(); }
    partial void OnImageSyncEnabledChanged(bool value)          { _settings.ImageSyncEnabled        = value; _settings.Save(); }
    partial void OnImageSyncAppendDateTimeChanged(bool value)   { _settings.ImageSyncAppendDateTime = value; _settings.Save(); }
    partial void OnImageSyncSourcePathChanged(string value)     { _settings.ImageSyncSourcePath = value; _settings.Save(); OnPropertyChanged(nameof(HasSyncPaths)); OnPropertyChanged(nameof(CanSyncManually)); }
    partial void OnImageSyncDestPathChanged(string value)       { _settings.ImageSyncDestPath   = value; _settings.Save(); OnPropertyChanged(nameof(HasSyncPaths)); OnPropertyChanged(nameof(CanSyncManually)); }
    partial void OnIsSyncingManuallyChanged(bool value)         { OnPropertyChanged(nameof(CanSyncManually)); OnPropertyChanged(nameof(SyncButtonText)); }

    partial void OnAutopilotNightCountTextChanged(string value)
    {
        if (int.TryParse(value, out var n) && n >= 0) { _settings.AutopilotNightCount = n; _settings.Save(); }
    }

    partial void OnAutopilotPowerOnOffsetTextChanged(string value)
    {
        if (int.TryParse(value, out var n) && n > 0) { _settings.AutopilotPowerOnOffsetMinutes = n; _settings.Save(); }
    }

    partial void OnAutoPlannerEnabledChanged(bool value) { _settings.PlannerEnabled = value; _settings.Save(); }

    // Observatory
    partial void OnObservatoryNameChanged(string value)          { _settings.ObservatoryName        = value; _settings.Save(); }
    partial void OnObservatoryLatitudeTextChanged(string value)  { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) { _settings.ObservatoryLatitudeDeg  = d; _settings.Save(); } }
    partial void OnObservatoryLongitudeTextChanged(string value) { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) { _settings.ObservatoryLongitudeDeg = d; _settings.Save(); } }
    partial void OnObservatoryBortleTextChanged(string value)    { if (int.TryParse(value, out int n) && n is >= 1 and <= 9) { _settings.ObservatoryBortleClass = n; _settings.Save(); } }

    // Telescope
    partial void OnTelescopeNameChanged(string value)            { _settings.TelescopeName           = value; _settings.Save(); }
    partial void OnTelescopeApertureTextChanged(string value)    { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0) { _settings.TelescopeApertureMm    = d; _settings.Save(); } }
    partial void OnTelescopeFocalLengthTextChanged(string value) { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0) { _settings.TelescopeFocalLengthMm  = d; _settings.Save(); } }

    // Camera
    partial void OnCameraNameChanged(string value)              { _settings.CameraName              = value; _settings.Save(); }
    partial void OnCameraPixelSizeTextChanged(string value)     { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0) { _settings.CameraPixelSizeMicrons = d; _settings.Save(); } }
    partial void OnCameraSensorWidthTextChanged(string value)   { if (int.TryParse(value, out int n) && n > 0) { _settings.CameraSensorWidthPixels  = n; _settings.Save(); } }
    partial void OnCameraSensorHeightTextChanged(string value)  { if (int.TryParse(value, out int n) && n > 0) { _settings.CameraSensorHeightPixels = n; _settings.Save(); } }

    // Planner imaging defaults
    partial void OnPlannerSubExposureTextChanged(string value) { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0) { _settings.PlannerSubExposureSec = d; _settings.Save(); } }
    partial void OnPlannerRepeatTextChanged(string value)      { if (int.TryParse(value, out int n) && n > 0) { _settings.PlannerRepeatCount = n; _settings.Save(); } }
    partial void OnPlannerFilterTypeChanged(string value)      { _settings.PlannerFilterType = value; _settings.Save(); }

    // Planner preferences
    partial void OnPlannerHorizonFlatTextChanged(string value)  { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d is >= 0 and <= 89) { _settings.PlannerHorizonFlatDeg = d; _horizonProfile = HorizonProfile.Flat(d); _settings.Save(); } }
    partial void OnPlannerMinAltitudeTextChanged(string value)  { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) { _settings.PlannerMinAltitudeDeg       = d; _settings.Save(); } }
    partial void OnPlannerMinMoonSepTextChanged(string value)   { if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) { _settings.PlannerMinMoonSeparationDeg = d; _settings.Save(); } }

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

    partial void OnSelectedImagingDeviceChanged(KasaDevice? value)
    {
        if (value == null) return;
        _settings.KasaImagingDeviceId = value.DeviceId;
        _settings.KasaImagingChildId  = value.ChildId ?? string.Empty;
        _settings.Save();
        OnPropertyChanged(nameof(ImagingPowerConnected));
        _ = RefreshImagingPowerStateAsync();
    }

    partial void OnSelectedAsiAirDeviceChanged(KasaDevice? value)
    {
        if (value == null) return;
        _settings.KasaAsiAirDeviceId = value.DeviceId;
        _settings.KasaAsiAirChildId  = value.ChildId ?? string.Empty;
        _settings.Save();
        OnPropertyChanged(nameof(AsiAirPowerConnected));
        _ = RefreshAsiAirPowerStateAsync();
    }

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private void DeleteLogFiles()
    {
        try
        {
            foreach (var file in Directory.GetFiles(SessionLog.LogFolder, "*.log"))
                File.Delete(file);
        }
        catch { /* non-fatal */ }
        LogEntries.Clear();
    }

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

        // Start/restart preview and load plans when IP is set
        if (!string.IsNullOrWhiteSpace(IpAddress))
        {
            StartPreview();
            if (Plans.Count == 0 && !IsLoadingPlans) _ = LoadPlansAsync();
        }

        // Restart weather + StellarVision polls so any setting changes take effect immediately
        _weatherPollCts?.Cancel();
        _weatherPollCts = new CancellationTokenSource();
        _ = Task.Run(() => WeatherPollLoopAsync(_weatherPollCts.Token));

        _svCts?.Cancel();
        _svCts = new CancellationTokenSource();
        _ = Task.Run(() => StellarVisionPollLoopAsync(_svCts.Token));

        // Restart camera poll in case building ID changed
        StartCameraPolling();
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
                // Restore previously selected devices (dew heater + imaging power)
                var savedChildId = string.IsNullOrEmpty(_settings.KasaChildId) ? null : _settings.KasaChildId;
                var saved = devices.FirstOrDefault(d => d.DeviceId == _settings.KasaDeviceId && d.ChildId == savedChildId);
                if (saved != null)
                    SelectedKasaDevice = saved;
                var savedImagingChildId = string.IsNullOrEmpty(_settings.KasaImagingChildId) ? null : _settings.KasaImagingChildId;
                var savedImaging = devices.FirstOrDefault(d => d.DeviceId == _settings.KasaImagingDeviceId && d.ChildId == savedImagingChildId);
                if (savedImaging != null)
                    SelectedImagingDevice = savedImaging;
                var savedAsiAirChildId = string.IsNullOrEmpty(_settings.KasaAsiAirChildId) ? null : _settings.KasaAsiAirChildId;
                var savedAsiAir = devices.FirstOrDefault(d => d.DeviceId == _settings.KasaAsiAirDeviceId && d.ChildId == savedAsiAirChildId);
                if (savedAsiAir != null)
                    SelectedAsiAirDevice = savedAsiAir;
                OnPropertyChanged(nameof(KasaConnected));
                OnPropertyChanged(nameof(ImagingPowerConnected));
                OnPropertyChanged(nameof(AsiAirPowerConnected));
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
                OnPropertyChanged(nameof(ImagingPowerConnected));
                OnPropertyChanged(nameof(AsiAirPowerConnected));
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

    [RelayCommand(CanExecute = nameof(CanToggleImagingPower))]
    private async Task ToggleImagingPowerAsync()
    {
        if (_kasaToken == null || SelectedImagingDevice == null) return;
        IsKasaBusy = true;
        var target = !IsImagingPowerOn;
        KasaStatusMessage = target ? "Turning imaging power on…" : "Turning imaging power off…";
        try
        {
            await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedImagingDevice, target);
            SessionLog.Add(LogLevel.Info, $"Imaging power {(target ? "ON" : "OFF")} (manual)");
            Dispatcher.UIThread.Post(() =>
            {
                IsImagingPowerOn    = target;
                IsImagingPowerKnown = true;
                KasaStatusMessage   = target ? "Imaging power on." : "Imaging power off.";
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

    private bool CanToggleImagingPower() => _kasaToken != null && SelectedImagingDevice != null && !IsKasaBusy;

    private async Task RefreshImagingPowerStateAsync()
    {
        if (_kasaToken == null || SelectedImagingDevice == null) return;
        try
        {
            var state = await KasaCloudClient.GetRelayStateAsync(_kasaToken, SelectedImagingDevice);
            if (state.HasValue)
                Dispatcher.UIThread.Post(() =>
                {
                    IsImagingPowerOn    = state.Value;
                    IsImagingPowerKnown = true;
                });
        }
        catch { /* non-fatal */ }
    }

    [RelayCommand(CanExecute = nameof(CanToggleAsiAirPower))]
    private async Task ToggleAsiAirPowerAsync()
    {
        if (_kasaToken == null || SelectedAsiAirDevice == null) return;
        IsKasaBusy = true;
        var target = !IsAsiAirPowerOn;
        KasaStatusMessage = target ? "Turning ASI Air power on…" : "Turning ASI Air power off…";
        try
        {
            await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedAsiAirDevice, target);
            SessionLog.Add(LogLevel.Info, $"ASI Air power {(target ? "ON" : "OFF")} (manual)");
            Dispatcher.UIThread.Post(() =>
            {
                IsAsiAirPowerOn    = target;
                IsAsiAirPowerKnown = true;
                KasaStatusMessage  = target ? "ASI Air power on." : "ASI Air power off.";
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

    private bool CanToggleAsiAirPower() => _kasaToken != null && SelectedAsiAirDevice != null && !IsKasaBusy;

    private async Task RefreshAsiAirPowerStateAsync()
    {
        if (_kasaToken == null || SelectedAsiAirDevice == null) return;
        try
        {
            var state = await KasaCloudClient.GetRelayStateAsync(_kasaToken, SelectedAsiAirDevice);
            if (state.HasValue)
                Dispatcher.UIThread.Post(() =>
                {
                    IsAsiAirPowerOn    = state.Value;
                    IsAsiAirPowerKnown = true;
                });
        }
        catch { /* non-fatal */ }
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
            $"Dew  {FormatTemp(w.DewPointC!.Value)}",
            $"Margin  {FormatMargin(margin)}"
        };
        if (w.HumidityPct.HasValue)
            parts.Add($"Humidity  {w.HumidityPct.Value:F0}%");
        var line1 = string.Join("  ·  ", parts);

        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(w.CloudText))   conditions.Add($"Sky  {w.CloudText}");
        if (!string.IsNullOrEmpty(w.WindText))    conditions.Add($"Wind  {w.WindText}");
        if (!string.IsNullOrEmpty(w.RainText)
            && w.RainText != "Dry")               conditions.Add($"Rain  {w.RainText}");
        if (!string.IsNullOrEmpty(w.DarknessText)) conditions.Add($"Darkness  {w.DarknessText}");
        if (w.SkyTemperatureC.HasValue)           conditions.Add($"Sky Temp  {FormatTemp(w.SkyTemperatureC.Value)}");
        var line2 = string.Join("  ·  ", conditions);

        return string.IsNullOrEmpty(line2) ? line1 : $"{line1}\n{line2}";
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

    private async Task StellarVisionPollLoopAsync(CancellationToken ct)
    {
        DateTime? lastSunFetch = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var data = await StellarVisionClient.FetchAsync(ct);
                if (data != null)
                {
                    SessionLog.Trace($"StellarVision: score={data.ImagingScore} seeing={data.SeeingLabel} safe={data.IsSafe}");

                    // Fetch sun times once per day using lat/lon from StellarVision
                    if (data.Latitude != 0 && (lastSunFetch == null || lastSunFetch.Value.Date < DateTime.Today))
                    {
                        try
                        {
                            var sunTimes = await SunTimesClient.FetchAsync(data.Latitude, data.Longitude, GetObservatoryTz(), ct);
                            if (sunTimes != null)
                            {
                                lastSunFetch = DateTime.Now;
                                SessionLog.Trace($"Sun times: sunset={sunTimes.Sunset:HH:mm} astroDusk={sunTimes.AstroDusk:HH:mm}");
                                Dispatcher.UIThread.Post(() => SunTimes = sunTimes);
                            }
                        }
                        catch (Exception ex) { SessionLog.Trace($"Sun times fetch error: {ex.Message}"); }
                    }
                    var scoreText  = $"{data.ImagingScore}/100";
                    var seeingText = $"Seeing  {data.SeeingLabel}  ·  Transparency  {data.TransparencyRaw:F1}/10  ·  Cloud  {data.CloudCoverPct:F0}%";
                    var safetyText = data.IsSafe
                        ? string.Empty
                        : string.Join("  ·  ", data.SafetyIssues);
                    var moonText   = $"{data.MoonPhaseLabel}  {data.MoonIlluminationPct:F0}%{(data.MoonAboveHorizon ? "  ↑" : "  ↓")}";
                    var auroraText = data.AuroraProbabilityPct > 5
                        ? $"Kp {data.KpIndex:F1} ({data.KpLevel})  ·  Aurora {data.AuroraProbabilityPct}%"
                        : string.Empty;

                    Dispatcher.UIThread.Post(() =>
                    {
                        SvImagingScoreText  = scoreText;
                        SvSeeingText        = seeingText;
                        SvSafetyText        = safetyText;
                        SvIsSafe            = data.IsSafe;
                        SvMoonText          = moonText;
                        SvAuroraText        = auroraText;
                        SvNwsForecast       = data.NwsForecast;
                        HasStellarVisionData = true;
                    });
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { SessionLog.Trace($"StellarVision poll error: {ex.Message}"); }

            try { await Task.Delay(120_000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void StartCameraPolling()
    {
        if (!int.TryParse(StarfrontBuildingIdText, out var bid) || bid <= 0)
        {
            SessionLog.Trace("Camera poll skipped — no building ID configured");
            return;
        }
        _cameraCts?.Cancel();
        _cameraCts = new CancellationTokenSource();
        _ = Task.Run(() => CameraPollLoopAsync(bid, _cameraCts.Token));
    }

    private async Task CameraPollLoopAsync(int buildingId, CancellationToken ct)
    {
        SessionLog.Trace($"Camera poll started for building {buildingId}");
        while (!ct.IsCancellationRequested)
        {
            await FetchAndUpdateCameraAsync(
                ObservatoryCameraService.BuildingCamUrl(buildingId),
                bmp => { var old = BuildingCamBitmap; BuildingCamBitmap = bmp; old?.Dispose(); },
                ts  => BuildingCamTimestamp = ts, ct);

            await FetchAndUpdateCameraAsync(
                ObservatoryCameraService.AllSkyCamUrl(),
                bmp => { var old = AllSkyCamBitmap; AllSkyCamBitmap = bmp; old?.Dispose(); },
                ts  => AllSkyCamTimestamp = ts, ct);

            try { await Task.Delay(60_000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task FetchAndUpdateCameraAsync(
        string url,
        Action<Bitmap> setBitmap,
        Action<string> setTimestamp,
        CancellationToken ct)
    {
        try
        {
            var (bmp, captureTime) = await ObservatoryCameraService.FetchSnapshotAsync(url, ct);
            if (bmp == null) return;
            var ts = captureTime.HasValue ? captureTime.Value.ToLocalTime().ToString("HH:mm:ss") : DateTime.Now.ToString("HH:mm:ss");
            SessionLog.Trace($"Camera snapshot fetched: {url} at {ts}");
            Dispatcher.UIThread.Post(() => { setBitmap(bmp); setTimestamp(ts); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SessionLog.Add(LogLevel.Warning, $"Camera fetch failed ({url}): {ex.Message}"); }
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
            var weather = await WeatherClient.GetBestAsync(ct);
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
            var alert       = weather.AlertFlag;

            Dispatcher.UIThread.Post(() =>
            {
                WeatherCurrentText   = condLine;
                WeatherUpdatedText   = updatedLine;
                WeatherMonitorStatus = monitorLine;
                WeatherAlertActive   = alert;
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
        !IsAutoRunActive && !IsAutopilotActive && !IsBusy &&
        !string.IsNullOrEmpty(IpAddress) &&
        (int.TryParse(StarfrontBuildingIdText, out var sfBid) && sfBid > 0) &&
        (IsPlanRunning || HasActivePlan);

    public bool ShowAutoRunSetupHint =>
        !(int.TryParse(StarfrontBuildingIdText, out var id) && id > 0);

    // ── Autopilot ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartAutopilot))]
    private void StartAutopilot()
    {
        _autopilotCts = new CancellationTokenSource();
        _autopilotNightsCompleted = 0;
        _autopilotQueueIndex = 0;
        IsAutopilotActive = true;
        AutopilotStatus = "Starting…";
        SessionLog.Add(LogLevel.Info, "Autopilot started");
        _ = Task.Run(() => AutopilotLoopAsync(_autopilotCts.Token));
    }

    private bool CanStartAutopilot() => !IsAutopilotActive && !IsAutoRunActive && AutopilotNights.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStopAutopilot))]
    private async Task StopAutopilotAsync()
    {
        _autopilotCts?.Cancel();
        _autoRunCts?.Cancel();
        await Task.Delay(500);
        Dispatcher.UIThread.Post(() =>
        {
            IsAutopilotActive   = false;
            IsAutoRunActive     = false;
            AutopilotStatus     = "Stopped";
            AutopilotNightLabel = string.Empty;
        });
        SessionLog.Add(LogLevel.Info, "Autopilot stopped by user");
    }

    private bool CanStopAutopilot() => IsAutopilotActive;

    [RelayCommand]
    private void AddAutopilotNight(PlanSummary plan)
    {
        AutopilotNights.Add(new AutopilotNightEntry { PlanId = plan.Id, PlanName = plan.Name });
        SaveAutopilotQueue();
        StartAutopilotCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveAutopilotNight(AutopilotNightEntry entry)
    {
        AutopilotNights.Remove(entry);
        SaveAutopilotQueue();
        StartAutopilotCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void MoveAutopilotNightUp(AutopilotNightEntry entry)
    {
        var i = AutopilotNights.IndexOf(entry);
        if (i > 0) { AutopilotNights.Move(i, i - 1); SaveAutopilotQueue(); }
    }

    [RelayCommand]
    private void MoveAutopilotNightDown(AutopilotNightEntry entry)
    {
        var i = AutopilotNights.IndexOf(entry);
        if (i >= 0 && i < AutopilotNights.Count - 1) { AutopilotNights.Move(i, i + 1); SaveAutopilotQueue(); }
    }

    private void SaveAutopilotQueue()
    {
        _settings.AutopilotPlanIds = AutopilotNights.Select(n => n.PlanId).ToList();
        _settings.Save();
    }

    private async Task AutopilotLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var totalNights = int.TryParse(AutopilotNightCountText, out var n) ? n : 0;
                if (totalNights > 0 && _autopilotNightsCompleted >= totalNights)
                {
                    SessionLog.Add(LogLevel.Info, $"Autopilot complete — {_autopilotNightsCompleted} night(s) finished");
                    break;
                }

                var nightNum   = _autopilotNightsCompleted + 1;
                var nightLabel = totalNights > 0 ? $"Night {nightNum}/{totalNights}" : $"Night {nightNum}";
                AutopilotNightEntry? entry = AutopilotNights.Count > 0
                    ? AutopilotNights[_autopilotQueueIndex % AutopilotNights.Count]
                    : null;
                var plannerActive = AutoPlannerEnabled && _autoTargetPlanner != null;
                var nightDisplayName = plannerActive ? "Auto-Target" : (entry?.PlanName ?? "—");
                Dispatcher.UIThread.Post(() => AutopilotNightLabel = $"{nightLabel}  ·  {nightDisplayName}");

                // ── 1. Wait until power-on time ─────────────────────────────
                var offsetMin     = int.TryParse(AutopilotPowerOnOffsetText, out var o) ? o : 60;
                var estimatedDusk = _lastKnownDuskUtc?.AddDays(1)
                                 ?? DateTime.UtcNow.Date.AddHours(23);
                var powerOnUtc    = estimatedDusk.AddMinutes(-offsetMin);

                if (DateTime.UtcNow < powerOnUtc)
                {
                    var waitSec      = (int)(powerOnUtc - DateTime.UtcNow).TotalSeconds;
                    var localPowerOn = powerOnUtc.ToLocalTime();
                    SessionLog.Add(LogLevel.Info, $"Autopilot {nightLabel} — powering on at {localPowerOn:HH:mm} local ({nightDisplayName})");
                    Dispatcher.UIThread.Post(() => AutopilotStatus = $"Powering on at {localPowerOn:HH:mm}  ·  {nightLabel}");
                    if (!await CountdownAsync(waitSec, t => Dispatcher.UIThread.Post(() => AutoRunNextCheckText = t), ct))
                        break;
                }

                // ── 2. Power on imaging hardware ─────────────────────────────
                var host = IpAddress.Trim();
                Dispatcher.UIThread.Post(() => AutopilotStatus = $"Powering on  ·  {nightLabel}");
                SessionLog.Add(LogLevel.Info, "Autopilot — powering on camera and ASI Air");
                try
                {
                    if (_kasaToken != null && SelectedImagingDevice != null)
                    {
                        await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedImagingDevice, true);
                        Dispatcher.UIThread.Post(() => { IsImagingPowerOn = true; IsImagingPowerKnown = true; });
                        await Task.Delay(5_000, ct);
                    }
                    if (_kasaToken != null && SelectedAsiAirDevice != null)
                    {
                        await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedAsiAirDevice, true);
                        await Task.Delay(5_000, ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SessionLog.Add(LogLevel.Warning, $"Power-on issue: {ex.Message}"); }

                // ── 3. Wait for ASI Air to boot and connect ──────────────────
                Dispatcher.UIThread.Post(() => AutopilotStatus = $"Waiting for ASI Air  ·  {nightLabel}");
                SessionLog.Add(LogLevel.Info, "Autopilot — waiting for ASI Air connection");
                var connected = false;
                for (var attempt = 0; attempt < 60 && !ct.IsCancellationRequested; attempt++)
                {
                    try
                    {
                        await AsiAirClient.CallAsync(host, new Capture.TestConnection(), ct);
                        connected = true;
                        break;
                    }
                    catch { }
                    await Task.Delay(10_000, ct);
                }

                if (!connected)
                {
                    SessionLog.Add(LogLevel.Error, "Autopilot — ASI Air did not connect within 10 minutes, retrying next cycle");
                    Dispatcher.UIThread.Post(() => AutopilotStatus = $"Connection failed  ·  {nightLabel}");
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    continue;
                }
                SessionLog.Add(LogLevel.Info, "Autopilot — ASI Air connected");

                try { await AsiAirClient.EnsureMountConnectedAsync(host, ct); } catch { }
                await LoadPlansAsync();

                // ── 4. Fetch real dusk/dawn ───────────────────────────────────
                try
                {
                    var (dawnUtc, duskUtc) = await AsiAirClient.QueryDawnDuskAsync(host, ct);
                    if (duskUtc.HasValue) _lastKnownDuskUtc = duskUtc.Value;
                    if (dawnUtc.HasValue)
                        SessionLog.Add(LogLevel.Info, $"Autopilot — dawn at {dawnUtc.Value.ToLocalTime():HH:mm} local");
                }
                catch { }

                // ── 5. Set tonight's plan as active ───────────────────────────
                string activePlanName = nightDisplayName;
                bool planReadyToRun = true;
                try
                {
                    if (plannerActive)
                    {
                        // Auto-target mode: pick best object, create plan on ASI Air
                        Dispatcher.UIThread.Post(() => AutopilotStatus = $"Selecting target  ·  {nightLabel}");
                        var today = DateOnly.FromDateTime(DateTime.UtcNow);
                        var (selected, newPlanId) = await _autoTargetPlanner!.SelectAndCreatePlanAsync(
                            host, today, _settings, _horizonProfile, Plans,
                            msg => SessionLog.Add(LogLevel.Info, msg), ct);
                        _lastSelectedTargets = selected;
                        activePlanName = selected.Count == 1
                            ? selected[0].Object.DisplayName
                            : $"Multi ({selected.Count} targets)";
                        Dispatcher.UIThread.Post(() =>
                        {
                            AutopilotNightLabel  = $"{nightLabel}  ·  {activePlanName}";
                            AutoPlannerStatus    = string.Join(", ", selected.Select(t => t.Object.DisplayName));
                        });
                        await LoadPlansAsync();
                        await AsiAirClient.SwapActivePlanAsync(host, Plans, newPlanId, ct);
                        await LoadPlansAsync();
                        SessionLog.Add(LogLevel.Info, $"Autopilot — auto-target plan created: {activePlanName} (id={newPlanId})");
                    }
                    else if (entry != null)
                    {
                        var match = Plans.FirstOrDefault(p => p.Id == entry.PlanId);
                        if (match != null)
                        {
                            await AsiAirClient.SwapActivePlanAsync(host, Plans, match.Id, ct);
                            await LoadPlansAsync();
                        }
                        activePlanName = entry.PlanName;
                        SessionLog.Add(LogLevel.Info, $"Autopilot — plan set to {entry.PlanName}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    SessionLog.Add(LogLevel.Warning, $"Autopilot — could not set plan: {ex.Message}");
                    if (plannerActive)
                    {
                        // No valid target found or plan creation failed — skip this night rather than
                        // running with no active plan and wasting power-on time.
                        SessionLog.Add(LogLevel.Warning, "Autopilot — skipping night, no target could be selected");
                        Dispatcher.UIThread.Post(() => AutopilotStatus = $"No target — skipping  ·  {nightLabel}");
                        planReadyToRun = false;
                    }
                }

                // ── 6. Run the night ──────────────────────────────────────────
                if (planReadyToRun)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsAutoRunActive = true;
                        AutoRunStatus   = $"Autopilot — {activePlanName}";
                    });
                    _autoRunCts = new CancellationTokenSource();
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _autoRunCts.Token))
                    {
                        await AutoRunLoopAsync(linkedCts.Token);
                    }
                    Dispatcher.UIThread.Post(() => IsAutoRunActive = false);
                }

                // ── 7. Image sync ─────────────────────────────────────────────
                if (planReadyToRun && ImageSyncEnabled && !string.IsNullOrWhiteSpace(ImageSyncSourcePath) && !string.IsNullOrWhiteSpace(ImageSyncDestPath))
                {
                    Dispatcher.UIThread.Post(() => AutopilotStatus = $"Syncing images  ·  {nightLabel}");
                    await RunImageSyncAsync(ct);
                }

                // ── 8. Power down ─────────────────────────────────────────────
                Dispatcher.UIThread.Post(() => AutopilotStatus = $"Powering down  ·  {nightLabel}");
                SessionLog.Add(LogLevel.Info, "Autopilot — powering down imaging hardware");
                try
                {
                    if (_kasaToken != null && SelectedAsiAirDevice != null)
                    {
                        await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedAsiAirDevice, false);
                        await Task.Delay(5_000, ct);
                    }
                    if (_kasaToken != null && SelectedImagingDevice != null)
                    {
                        await KasaCloudClient.SetRelayStateAsync(_kasaToken, SelectedImagingDevice, false);
                        Dispatcher.UIThread.Post(() => { IsImagingPowerOn = false; IsImagingPowerKnown = true; });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SessionLog.Add(LogLevel.Warning, $"Power-down issue: {ex.Message}"); }

                // Record progress if planner ran this night
                if (plannerActive && _lastSelectedTargets != null && _lastSelectedTargets.Count > 0)
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var site  = _settings.ToObservationSite();
                    var (darkStart, darkEnd) = AstronomyService.GetAstronomicalDarkness(today, site);
                    double hoursImaged = (darkEnd - darkStart).TotalHours;
                    _autoTargetPlanner!.RecordSessionProgress(_lastSelectedTargets, today, hoursImaged);
                    _lastSelectedTargets = null;
                }

                SessionLog.Add(LogLevel.Info, $"Autopilot — {nightLabel} complete ({activePlanName})");
                Dispatcher.UIThread.Post(() => AutopilotStatus = $"{nightLabel} complete");

                _autopilotNightsCompleted++;
                if (!plannerActive) _autopilotQueueIndex++;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsAutopilotActive   = false;
                IsAutoRunActive     = false;
                AutopilotStatus     = _autopilotNightsCompleted > 0
                    ? $"Finished — {_autopilotNightsCompleted} night(s) completed"
                    : "Stopped";
                AutopilotNightLabel = string.Empty;
                AutoRunNextCheckText = string.Empty;
            });
        }
    }

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
        var cloudGapCount  = 0;
        if (resumeFromRunning)
            _ = PreCoolAsync(IpAddress.Trim(), ct);

        // Cache tonight's dawn time so we know when to stop monitoring after cloud gaps.
        // Primary: ASI Air's own dawn query. Fallback: our sun-times API (SunTimes.AstroDawn).
        _sessionDawnUtc = null;
        try
        {
            var (dawn, dusk) = await AsiAirClient.QueryDawnDuskAsync(IpAddress.Trim(), ct);
            _sessionDawnUtc = dawn;
            if (dawn.HasValue)
                SessionLog.Add(LogLevel.Info, $"Tonight's dawn (ASI Air): {dawn.Value.ToLocalTime():HH:mm} local — monitoring until then");
        }
        catch { /* non-fatal */ }

        if (!_sessionDawnUtc.HasValue && SunTimes != null)
        {
            _sessionDawnUtc = TimeZoneInfo.ConvertTimeToUtc(SunTimes.AstroDawn, GetObservatoryTz());
            SessionLog.Add(LogLevel.Info, $"Tonight's dawn (sun-times API): {SunTimes.AstroDawn:HH:mm} local — monitoring until then");
        }

        if (!_sessionDawnUtc.HasValue)
            SessionLog.Add(LogLevel.Warning, "Could not determine dawn time — session will run until manually stopped");

        // Create a per-night Discord forum thread (fire-and-forget, non-fatal)
        var webhookForThread = _settings.DiscordWebhookUrl;
        if (!string.IsNullOrEmpty(webhookForThread))
        {
            var date       = DateTime.Now.ToString("yyyy-MM-dd");
            var suffix     = resumeFromRunning ? "Resumed" : "Imaging Session";
            var threadName = $"{date} — {suffix}";
            var firstMsg   = resumeFromRunning
                ? $"Monitoring resumed at {DateTime.Now:HH:mm}"
                : $"Auto Run started at {DateTime.Now:HH:mm}";
            _discordThreadId = await DiscordClient.CreateForumThreadAsync(webhookForThread, threadName, firstMsg);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => AutoRunNextCheckText = string.Empty);

                // ── Dawn cutoff — end the session when dawn arrives ────────
                // Check both the cached UTC dawn and SunTimes.AstroDawn directly so a
                // failed QueryDawnDuskAsync at startup can't leave the loop running forever.
                var nowObs = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetObservatoryTz());
                var dawnReached = (_sessionDawnUtc.HasValue && DateTime.UtcNow >= _sessionDawnUtc.Value)
                               || (SunTimes != null && nowObs >= SunTimes.AstroDawn && nowObs < SunTimes.AstroDusk);
                if (dawnReached)
                {
                    SessionLog.Add(LogLevel.Info, "Dawn reached — ending session");
                    Dispatcher.UIThread.Post(() => AutoRunStatus = "Dawn — shutting down…");
                    await PerformAutoRunShutdownAsync();
                    if (ImageSyncEnabled && !string.IsNullOrWhiteSpace(ImageSyncSourcePath) && !string.IsNullOrWhiteSpace(ImageSyncDestPath))
                        await RunImageSyncAsync(ct);
                    Dispatcher.UIThread.Post(() => { IsAutoRunActive = false; AutoRunStatus = "Session ended at dawn"; });
                    return;
                }

                // ── Suspend roof checks during TargetDelay ────────────────
                // While the plan is waiting for its scheduled start time the roof may
                // legitimately close and reopen (cloud gap, brief closure, etc.).
                // Don't abort — sleep until we're just before the pre-cool window,
                // then do a final roof check and abort only if still closed then.
                if (planStarted && _planScheduledStartTime.HasValue)
                {
                    var secsToStart = (_planScheduledStartTime.Value - DateTime.Now).TotalSeconds;
                    var preCoolSecs = _settings.CoolerPreCoolMinutes * 60.0;
                    var sleepSecs   = (int)(secsToStart - preCoolSecs - 120); // wake 2 min before pre-cool
                    if (sleepSecs > 0)
                    {
                        var resumeAt = DateTime.Now.AddSeconds(sleepSecs);
                        SessionLog.Add(LogLevel.Info,
                            $"Roof checks suspended — imaging at {_planScheduledStartTime.Value:HH:mm}, will resume at {resumeAt:HH:mm}");
                        Dispatcher.UIThread.Post(() =>
                            AutoRunStatus = $"Waiting for dark  ·  imaging at {_planScheduledStartTime.Value:HH:mm}  ·  roof check at {resumeAt:HH:mm}");
                        if (!await CountdownAsync(sleepSecs, t => Dispatcher.UIThread.Post(() => AutoRunNextCheckText = t), ct))
                            break;
                        continue;
                    }
                }

                // ── Roof check ────────────────────────────────────────────
                RoofStatusResult bestResult;
                try
                {
                    bestResult = await AsiAirClient.FetchRoofStatusFromStarfrontAsync(_settings.StarfrontBuildingId, ct);
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        AutoRunStatus = $"Roof check failed: {ex.Message}  (retrying…)");
                    try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch { break; }
                    continue;
                }

                var roofStatus = bestResult.Status;
                var isOpen     = roofStatus == "OPEN";
                var checkedAt  = DateTime.Now.ToString("HH:mm");

                // Keep the header badge in sync during active monitoring
                Dispatcher.UIThread.Post(() =>
                {
                    RoofIsOpen    = isOpen;
                    RoofBadgeText = $"Building {_settings.StarfrontBuildingId} : {roofStatus}";
                });

                // ── State transitions ────────────────────────────────────
                if (!planStarted && isOpen)
                {
                    // Roof just opened (or re-opened after a cloud gap) — start the plan
                    var openMsg = cloudGapCount > 0
                        ? $"Roof re-opened after {cloudGapCount} closure(s) — restarting plan"
                        : "Roof open — Auto Run starting plan";
                    SessionLog.Add(LogLevel.Info, openMsg);
                    Dispatcher.UIThread.Post(() => AutoRunStatus = cloudGapCount > 0 ? "Roof re-opened — restarting plan…" : "Roof open — starting plan…");
                    try
                    {
                        await LaunchActivePlanAsync();
                        planStarted = true;
                        _ = PreCoolAsync(IpAddress.Trim(), ct);
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
                    var dawnLabel = _sessionDawnUtc.HasValue
                        ? $"  ·  monitoring until {_sessionDawnUtc.Value.ToLocalTime():HH:mm}"
                        : string.Empty;
                    SessionLog.Add(LogLevel.Warning, $"Roof {roofStatus} at {checkedAt} [{bestResult.Source}] — plan paused, monitoring for re-open{dawnLabel}");
                    Dispatcher.UIThread.Post(() =>
                        AutoRunStatus = $"Roof {roofStatus} — parked, watching for re-open{dawnLabel}");
                    await PerformCloudGapPauseAsync();
                    cloudGapCount++;
                    planStarted    = false;
                    planWasRunning = false;
                    _planScheduledStartTime = null;
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
                        try { await AsiAirClient.StopCoolingAsync(IpAddress.Trim()); } catch { }
                        SessionLog.Add(LogLevel.Info, $"Plan completed — Auto Run ended at {checkedAt}");

                        if (ImageSyncEnabled
                            && !string.IsNullOrWhiteSpace(ImageSyncSourcePath)
                            && !string.IsNullOrWhiteSpace(ImageSyncDestPath))
                        {
                            Dispatcher.UIThread.Post(() => AutoRunStatus = "Plan complete — starting image sync…");
                            await RunImageSyncAsync(ct);
                        }

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
                    var scheduledStart = _planScheduledStartTime;
                    Dispatcher.UIThread.Post(() => AutoRunStatus = !planStarted
                        ? $"Waiting for roof  ·  currently {roofStatus}  ·  checked {checkedAt}"
                        : scheduledStart.HasValue && scheduledStart.Value > DateTime.Now
                            ? $"Waiting for start  ·  imaging at {scheduledStart.Value:HH:mm}  ·  roof {roofStatus}"
                            : IsPlanRunning
                                ? $"Plan running  ·  roof {roofStatus}  ·  checked {checkedAt}"
                                : $"Plan waiting to image  ·  roof {roofStatus}  ·  checked {checkedAt}");
                }

                if (!await CountdownAsync(60,
                        t => Dispatcher.UIThread.Post(() => AutoRunNextCheckText = t), ct))
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _discordThreadId = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (IsAutoRunActive) IsAutoRunActive = false;
                AutoRunNextCheckText = string.Empty;
            });
        }
    }

    private async Task PreCoolAsync(string host, CancellationToken ct)
    {
        var preCoolMinutes = _settings.CoolerPreCoolMinutes;
        var targetTempC    = _settings.CoolerTargetTempC;
        if (preCoolMinutes <= 0) return;

        try
        {
            if (!await AsiAirClient.HasCoolerAsync(host, ct)) return;
        }
        catch (OperationCanceledException) { return; }
        catch { return; }

        var preCoolSeconds = preCoolMinutes * 60.0;

        // Wait up to 15 s for the TargetDelay push event to set _planScheduledStartTime.
        // Without this, the first loop iteration fires before ASI Air sends TargetDelay,
        // sees remainingSec == 0, and starts cooling immediately regardless of schedule.
        for (var i = 0; i < 15 && !_planScheduledStartTime.HasValue && !ct.IsCancellationRequested; i++)
        {
            try { await Task.Delay(1_000, ct); } catch { return; }
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Use scheduled start time if the plan is waiting for a future time;
                // otherwise 0 means the plan is running now — start cooling immediately.
                double remainingSec;
                if (_planScheduledStartTime.HasValue && _planScheduledStartTime.Value > DateTime.Now)
                    remainingSec = (_planScheduledStartTime.Value - DateTime.Now).TotalSeconds;
                else
                    remainingSec = 0;

                if (remainingSec <= preCoolSeconds)
                {
                    var tempDisplay = FormatTemp(targetTempC);
                    SessionLog.Add(LogLevel.Info, $"Pre-cool started — target {tempDisplay}");
                    await AsiAirClient.StartCoolingAsync(host, targetTempC, ct);
                    return;
                }

                // How long until we should fire — wake up then.
                var waitSec = (int)(remainingSec - preCoolSeconds) + 5;
                Dispatcher.UIThread.Post(() =>
                    AutoRunStatus = $"Pre-cool in {waitSec / 60}m {waitSec % 60:D2}s  ·  imaging in {(int)(remainingSec / 60)}m");
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(waitSec, 30)), ct); }
                catch (OperationCanceledException) { return; }
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                try { await Task.Delay(30_000, ct); } catch { return; }
            }
        }
    }

    private async Task RunImageSyncAsync(CancellationToken ct)
    {
        var source = ImageSyncSourcePath.Trim();
        var dest   = ImageSyncDestPath.Trim();

        SessionLog.Add(LogLevel.Info, $"Image sync started — {source} → {dest}");

        ImageSyncService.SyncResult result;
        try
        {
            result = await ImageSyncService.SyncAsync(
                source, dest,
                ImageSyncAppendDateTime,
                status => Dispatcher.UIThread.Post(() => AutoRunStatus = status),
                ct);
        }
        catch (OperationCanceledException)
        {
            SessionLog.Add(LogLevel.Warning, "Image sync cancelled");
            return;
        }
        catch (Exception ex)
        {
            SessionLog.Add(LogLevel.Error, $"Image sync failed — {ex.Message}");
            return;
        }

        var size     = FormatSyncBytes(result.BytesCopied);
        var duration = FormatSyncDuration(result.Duration);
        var summary  = $"Image sync complete — {result.FilesCopied} of {result.FilesScanned} files · {size} in {duration}";

        if (result.PersistentFailures.Count > 0)
        {
            var fileList = string.Join(", ", result.PersistentFailures.Select(f => Path.GetFileName(f.RelativePath)));
            SessionLog.Add(LogLevel.Warning,
                $"{summary}\n{result.PersistentFailures.Count} file(s) failed after 3 attempts: {fileList}");
        }
        else
        {
            SessionLog.Add(LogLevel.Info, summary);
        }
    }

    private static string FormatSyncBytes(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F2} GB"
    };

    private static string FormatSyncDuration(TimeSpan ts) =>
        ts.TotalHours >= 1  ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s" :
        ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s" :
                               $"{ts.Seconds}s";

    private async Task LaunchActivePlanAsync()
    {
        _planScheduledStartTime = null;
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
        try { await AsiAirClient.StopCoolingAsync(host); } catch { }
        try { await AsiAirClient.CallAsync(host, new Mount.ScopePark()); } catch { }
    }

    private async Task PerformCloudGapPauseAsync()
    {
        var host = IpAddress.Trim();
        try { await AsiAirClient.CallAsync(host, new Capture.StopExposure()); } catch { }
        try { await AsiAirClient.CallAsync(host, new Mount.ScopePark()); } catch { }
        // Camera cooling and dew heater deliberately left on
    }

    [RelayCommand(CanExecute = nameof(CanSyncManually))]
    private async Task SyncImagesManuallyAsync()
    {
        var source = ImageSyncSourcePath.Trim();
        var dest   = ImageSyncDestPath.Trim();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest)) return;

        _manualSyncCts?.Cancel();
        _manualSyncCts = new CancellationTokenSource();
        var ct = _manualSyncCts.Token;

        IsSyncingManually = true;
        ManualSyncProgress = 0;
        SessionLog.Add(LogLevel.Info, $"Image copy started — {source} → {dest}");

        var progressHandler = new Progress<double>(v =>
            Dispatcher.UIThread.Post(() => ManualSyncProgress = v));

        ImageSyncService.SyncResult result;
        try
        {
            result = await Task.Run(() => ImageSyncService.SyncAsync(
                source, dest,
                ImageSyncAppendDateTime,
                status => SessionLog.Trace(status),
                ct,
                progressHandler), ct);
        }
        catch (OperationCanceledException)
        {
            SessionLog.Add(LogLevel.Warning, "Image copy cancelled");
            IsSyncingManually = false;
            ManualSyncProgress = 0;
            return;
        }
        catch (Exception ex)
        {
            SessionLog.Add(LogLevel.Error, $"Image copy failed — {ex.Message}");
            IsSyncingManually = false;
            ManualSyncProgress = 0;
            return;
        }

        var size    = FormatSyncBytes(result.BytesCopied);
        var elapsed = FormatSyncDuration(result.Duration);
        var summary = $"Image copy complete — {result.FilesCopied} of {result.FilesScanned} files · {size} in {elapsed}";
        if (result.PersistentFailures.Count > 0)
        {
            var files = string.Join(", ", result.PersistentFailures.Select(f => Path.GetFileName(f.RelativePath)));
            SessionLog.Add(LogLevel.Warning, $"{summary}\n{result.PersistentFailures.Count} file(s) failed: {files}");
        }
        else
        {
            SessionLog.Add(LogLevel.Info, summary);
        }

        ManualSyncProgress = 1;
        IsSyncingManually = false;
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

    private async Task RoofDisplayPollLoopAsync(CancellationToken ct)
    {
        try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            var buildingId = _settings.StarfrontBuildingId;
            if (buildingId > 0)
            {
                try
                {
                    var result = await AsiAirClient.FetchRoofStatusFromStarfrontAsync(buildingId, ct);
                    var isOpen = result.Status == "OPEN";
                    Dispatcher.UIThread.Post(() =>
                    {
                        RoofIsOpen    = isOpen;
                        RoofBadgeText = $"Building {buildingId} : {result.Status}";
                    });
                }
                catch
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RoofIsOpen    = false;
                        RoofBadgeText = $"Building {buildingId} : CLOSED";
                    });
                }
            }
            try { await Task.Delay(600_000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    [RelayCommand]
    private async Task CheckRoofStatusAsync()
    {
        var buildingId = _settings.StarfrontBuildingId;
        IsBusy = true;
        StatusMessage = "Checking roof status…";
        try
        {
            var result = await AsiAirClient.FetchRoofStatusFromStarfrontAsync(buildingId, default);
            var isOpen = result.Status == "OPEN";
            RoofIsOpen    = isOpen;
            RoofBadgeText = $"Building {buildingId} : {result.Status}";
            var ts = result.Timestamp.HasValue ? result.Timestamp.Value.ToString("HH:mm:ss") : "unknown time";
            StatusMessage = $"Building {buildingId}: {result.Status}  —  checked {ts}";
        }
        catch (Exception ex)
        {
            RoofIsOpen    = false;
            RoofBadgeText = $"Building {buildingId} : CLOSED";
            StatusMessage = $"Roof check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
                RestoreAutopilotQueueFromSettings();
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

    private void RestoreAutopilotQueueFromSettings()
    {
        if (_settings.AutopilotPlanIds.Count == 0) return;
        AutopilotNights.Clear();
        foreach (var id in _settings.AutopilotPlanIds)
        {
            var plan = Plans.FirstOrDefault(p => p.Id == id);
            if (plan != null)
                AutopilotNights.Add(new AutopilotNightEntry { PlanId = plan.Id, PlanName = plan.Name });
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetActivePlan))]
    private async Task SetActivePlanAsync(PlanSummary plan)
    {
        var host = IpAddress.Trim();
        SessionLog.Trace($"plan swap: activating plan id={plan.Id} name='{plan.Name}'");
        Dispatcher.UIThread.Post(() => StatusMessage = $"Switching to {plan.Name}…");
        bool swapOk = false;
        try
        {
            await AsiAirClient.SwapActivePlanAsync(host, Plans, plan.Id);
            swapOk = true;
            SessionLog.Trace($"plan swap: import_plan succeeded for plan id={plan.Id}");
        }
        catch (Exception ex)
        {
            SessionLog.Trace($"plan swap: import_plan failed — {ex.GetType().Name}: {ex.Message}");
            Dispatcher.UIThread.Post(() => StatusMessage = $"Plan swap failed: {ex.Message}");
            // Device may have closed the connection while processing the swap.
            // Give it a moment to recover before we try to reload.
            await Task.Delay(3_000);
        }

        // Always refresh plan list — the swap may have succeeded even if we lost the response.
        try
        {
            await LoadPlansAsync();
            if (swapOk)
                Dispatcher.UIThread.Post(() => StatusMessage = $"Active plan: {plan.Name}.");
        }
        catch { /* best-effort refresh */ }
    }

    private bool CanSetActivePlan(PlanSummary? plan) =>
        plan != null && !plan.IsEnabled && !IsImagingActive && !string.IsNullOrEmpty(IpAddress);

    [RelayCommand(CanExecute = nameof(CanResetActivePlan))]
    private async Task ResetActivePlanAsync()
    {
        var host       = IpAddress.Trim();
        var activePlan = Plans.FirstOrDefault(p => p.IsEnabled);
        if (activePlan == null) return;
        SessionLog.Trace($"Resetting plan id={activePlan.Id} name='{activePlan.Name}'");
        Dispatcher.UIThread.Post(() => StatusMessage = $"Resetting {activePlan.Name}…");
        try
        {
            await AsiAirClient.ResetPlanAsync(host, activePlan.Id);
            SessionLog.Add(LogLevel.Info, $"Plan reset: {activePlan.Name}", discord: false);
            await LoadPlansAsync();
            Dispatcher.UIThread.Post(() => StatusMessage = $"{activePlan.Name} reset.");
        }
        catch (Exception ex)
        {
            SessionLog.Trace($"Plan reset failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => StatusMessage = $"Reset failed: {ex.Message}");
        }
    }

    private bool CanResetActivePlan() =>
        HasActivePlan && !IsAutoRunActive && !IsBusy && !string.IsNullOrWhiteSpace(IpAddress);

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
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                if (attempt == 2)
                {
                    StatusMessage = "Reconnecting, retrying…";
                    await Task.Delay(1500); // give the reconnect a moment to settle
                }
                StatusMessage = $"Starting {secs}s exposure…";
                await AsiAirClient.SetPageAsync(host, "preview");
                await AsiAirClient.StartExposureAsync(host, (long)(secs * 1_000_000));
                lastEx = null;
                break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                SessionLog.Trace($"capture attempt {attempt} failed: {ex.Message}");
            }
        }
        if (lastEx != null)
        {
            StatusMessage = $"Capture failed: {lastEx.Message}";
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
    private void StartPreview()
    {
        var host = IpAddress.Trim();
        if (string.IsNullOrEmpty(host)) return;
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        IsPreviewActive = true;
        _ = Task.Run(() => PreviewLoopAsync(host, _previewCts.Token));
    }

    private async Task PreviewLoopAsync(string host, CancellationToken ct)
    {
        // Capture the CTS that started this loop so the cleanup block can detect
        // whether a new loop has been launched to replace us (e.g. settings re-open).
        var myCts = _previewCts;

        bool wasWorking = false;
        Dispatcher.UIThread.Post(() => PreviewStatus = "Waiting for exposure...");
        _ = TemperaturePollLoopAsync(host, ct);
        _ = SystemStatsPollLoopAsync(host, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (isWorking, captureState, exposureMode, completedFrames, totalFrames, lapseMs, totalMs, lastAfHfr, isMeridFlip) = await AsiAirClient.QueryCaptureStateAsync(host, ct);
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

                    // Track waiting-for-night and meridian flip from poll (no push events for these)
                    var waitingForNight = captureState == "target_delay";
                    if (waitingForNight != _isWaitingForNight)
                    {
                        _isWaitingForNight = waitingForNight;
                        NotifyPlanStatus();
                    }
                    if (isMeridFlip != _isMeridFlipActive)
                    {
                        if (!isMeridFlip && _isMeridFlipActive)
                            SessionLog.Add(LogLevel.Info, "Meridian flip complete");
                        _isMeridFlipActive = isMeridFlip;
                        NotifyPlanStatus();
                    }
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
                            _ = DiscordClient.PostImageAsync(webhookUrl, bitmap, $"[{now:HH:mm}] Preview image", _discordThreadId);
                        }
                    }
                    finally
                    {
                        Dispatcher.UIThread.Post(() => { IsDownloading = false; DownloadProgressValue = 0; });
                    }
                }

                wasWorking = isWorking;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                bool isTimeout  = ex is OperationCanceledException;
                bool isConnLoss = !isTimeout && (ex.Message.Contains("connection lost", StringComparison.OrdinalIgnoreCase)
                                             ||  ex.Message.Contains("not alive",       StringComparison.OrdinalIgnoreCase));
                SessionLog.Trace($"preview loop error ({(isConnLoss ? "conn" : isTimeout ? "timeout" : "other")}): {ex.GetType().Name}: {ex.Message}");
                Dispatcher.UIThread.Post(() =>
                {
                    if (isConnLoss) PreviewStatus = "Reconnecting…";
                    IsDownloading = false;
                    DownloadProgressValue = 0;
                });
                try { await Task.Delay(1_000, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            // Clear guiding active if no GuideStep received in last 15 seconds (covers download gap)
            if (IsGuiding && (DateTime.Now - _lastGuideStepAt).TotalSeconds > 15)
            {
                SessionLog.Add(LogLevel.Warning, "Guiding stopped");
                _isSettling = false;
                Dispatcher.UIThread.Post(() => IsGuiding = false);
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }

        // Only clear persistent state (panel visibility, device stats) if no new
        // preview loop was started to replace this one. When CloseSettings() calls
        // StartPreview(), it cancels our CTS and creates a fresh one — ReferenceEquals
        // detects that so we don't wipe data the new loop is about to keep current.
        var replaced = !ReferenceEquals(_previewCts, myCts);
        Dispatcher.UIThread.Post(() =>
        {
            if (!replaced)
            {
                IsPreviewActive     = false;
                CameraTemperatureC  = null;
                CameraCoolPowerPerc = null;
                PiTemperatureC      = null;
                PiIsUndervolt       = false;
                StorageTotalMb      = null;
                StorageFreeMb       = null;
            }
            IsImagingActive     = false;
            CaptureState        = string.Empty;
            ExposureMode        = string.Empty;
            LiveCompletedFrames = 0;
            LiveTotalFrames     = 0;
            CaptureLapseMs      = 0;
            CaptureTotalMs      = 0;
            PreviewStatus       = string.Empty;
            ClearSessionActivityFlags();
        });
    }

    private async Task TemperaturePollLoopAsync(string host, CancellationToken ct)
    {
        try { await Task.Delay(20_000, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (tempC, coolPower) = await AsiAirClient.QueryCameraTemperatureAsync(host, ct);
                SessionLog.Trace($"camera poll: temp={(tempC.HasValue ? $"{tempC:F1}°C" : "null")} coolPower={coolPower}%");
                Dispatcher.UIThread.Post(() =>
                {
                    if (tempC.HasValue)     CameraTemperatureC  = tempC;
                    if (coolPower.HasValue) CameraCoolPowerPerc = coolPower;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { SessionLog.Trace($"camera temp poll error: {ex.Message}"); }
            try { await Task.Delay(30_000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task SystemStatsPollLoopAsync(string host, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (piTempC, undervolt) = await AsiAirClient.QueryPiInfoAsync(host, ct);
                var (totalMb, freeMb)    = await AsiAirClient.QueryDiskVolumeAsync(host, ct);
                SessionLog.Trace($"system poll: pi={(piTempC.HasValue ? $"{piTempC:F1}°C" : "null")} undervolt={undervolt} storage={freeMb}/{totalMb} MB");
                Dispatcher.UIThread.Post(() =>
                {
                    // Only overwrite on a valid reading — keep last known value on transient API failures
                    if (piTempC.HasValue)  { PiTemperatureC = piTempC; PiIsUndervolt = undervolt; }
                    if (totalMb.HasValue)  StorageTotalMb = totalMb;
                    if (freeMb.HasValue)   StorageFreeMb  = freeMb;
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { SessionLog.Trace($"system poll error: {ex.Message}"); }
            try { await Task.Delay(60_000, ct); } catch (OperationCanceledException) { return; }
        }
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
                        if (!IsAutoFocusActive)
                            SessionLog.Add(LogLevel.Info, "Autofocus started");
                        Dispatcher.UIThread.Post(() => { IsAutoFocusActive = true; AutoFocusStatus = "Running…"; });
                    }
                    else if (state == "complete")
                    {
                        var hfr    = node["result"]?["last_point"]?.AsArray()?[1]?.GetValue<double>();
                        var ts     = DateTime.Now.ToString("HH:mm");
                        var badge  = hfr.HasValue ? $"HFR {hfr.Value:F2}  ·  {ts}" : $"Done  ·  {ts}";
                        var logMsg = hfr.HasValue ? $"Autofocus complete — star size {hfr.Value:F2}" : "Autofocus complete";
                        SessionLog.Add(LogLevel.Info, logMsg);
                        Dispatcher.UIThread.Post(() => { IsAutoFocusActive = false; AutoFocusStatus = badge; });
                    }
                    else if (state is not null)
                    {
                        SessionLog.Add(LogLevel.Warning, $"Autofocus {state}");
                        Dispatcher.UIThread.Post(() => { IsAutoFocusActive = false; AutoFocusStatus = state; });
                    }
                    break;
                }
                case "Target":
                {
                    var state = node["state"]?.GetValue<string>();
                    if (state == "start")
                    {
                        var name  = node["target"]?["current_name"]?.GetValue<string>();
                        var total = node["frame_summary"]?["total"]?.GetValue<int>();
                        var msg = name != null
                            ? (total.HasValue ? $"Target: {name} — {total} frames" : $"Target: {name}")
                            : "Target started";
                        SessionLog.Add(LogLevel.Info, msg);
                        _isFindingTarget = true;
                        NotifyPlanStatus();
                    }
                    break;
                }
                case "TargetDelay":
                {
                    var state = node["state"]?.GetValue<string>();
                    if (state == "start")
                    {
                        var seconds = node["seconds"]?.GetValue<int>() ?? 0;
                        _planScheduledStartTime = DateTime.Now.AddSeconds(seconds);
                        _isFindingTarget    = false;
                        _isWaitingForNight  = true;
                        NotifyPlanStatus();
                        var span      = TimeSpan.FromSeconds(seconds);
                        var startTime = _planScheduledStartTime.Value;
                        var msg = span.TotalHours >= 1
                            ? $"Waiting {(int)span.TotalHours}h {span.Minutes}m for scheduled start"
                            : $"Waiting {(int)span.TotalMinutes}m for scheduled start";
                        SessionLog.Add(LogLevel.Info, msg);
                        if (IsAutoRunActive)
                            Dispatcher.UIThread.Post(() =>
                                AutoRunStatus = $"Waiting for start  ·  imaging at {startTime:HH:mm}");
                    }
                    else if (state == "end")
                    {
                        _planScheduledStartTime = null;
                        _isWaitingForNight      = false;
                        NotifyPlanStatus();
                    }
                    break;
                }
                case "PlateSolve":
                {
                    var state = node["state"]?.GetValue<string>();
                    var page  = node["page"]?.GetValue<string>();
                    if (page == "plan")
                    {
                        if (state == "start")
                        {
                            _isPlateSolveActive = true;
                            NotifyPlanStatus();
                        }
                        else if (state is "complete" or "success")
                        {
                            _isPlateSolveActive = false;
                            _isFindingTarget    = false;
                            NotifyPlanStatus();
                        }
                        else if (state is "failed" or "error")
                        {
                            SessionLog.Add(LogLevel.Warning, "Plate solve failed");
                            _isPlateSolveActive = false;
                            NotifyPlanStatus();
                        }
                    }
                    break;
                }
                case "RestartGuide":
                {
                    var state = node["state"]?.GetValue<string>();
                    if (state == "start")
                    {
                        SessionLog.Add(LogLevel.Info, "Restarting guide…");
                        _isRestartingGuide = true;
                        NotifyPlanStatus();
                    }
                    break;
                }
                case "StartGuiding":
                    _isRestartingGuide = false;
                    _isSettling        = false;
                    NotifyPlanStatus();
                    break;
                case "SettleBegin":
                    _isSettling = true;
                    NotifyPlanStatus();
                    break;
                case "GuideStep":
                {
                    var avgDist  = node["AvgDist"]?.GetValue<double>();
                    var ra       = node["RADistanceRaw"]?.GetValue<double>();
                    var dec      = node["DECDistanceRaw"]?.GetValue<double>();
                    var isSettle = (node["IsSettle"]?.GetValue<int>() ?? 0) != 0;
                    var isDither = (node["IsDither"]?.GetValue<int>() ?? 0) != 0;
                    _lastGuideStepAt = DateTime.Now;

                    if (ra.HasValue && dec.HasValue)
                    {
                        _guidePointQueue.Enqueue(new GuidePoint(ra.Value, dec.Value, isSettle, isDither));
                        if (_guidePointQueue.Count > GuideHistoryCapacity)
                            _guidePointQueue.Dequeue();

                        var pts     = _guidePointQueue.ToArray();
                        var active  = pts.Where(p => !p.IsSettle && !p.IsDither).ToArray();
                        var rmsRa   = active.Length > 0 ? Math.Sqrt(active.Average(p => p.Ra  * p.Ra))  : 0;
                        var rmsDec  = active.Length > 0 ? Math.Sqrt(active.Average(p => p.Dec * p.Dec)) : 0;

                        var status  = avgDist.HasValue ? $"RMS  RA {rmsRa:F2}″  Dec {rmsDec:F2}″" : string.Empty;
                        var wasSettling = _isSettling;
                        if (wasSettling) { _isSettling = false; NotifyPlanStatus(); }
                        Dispatcher.UIThread.Post(() =>
                        {
                            IsGuiding    = true;
                            GuideStatus  = status;
                            GuidePoints  = pts;
                            GuideRmsRa   = rmsRa;
                            GuideRmsDec  = rmsDec;
                        });
                    }
                    else if (avgDist.HasValue)
                    {
                        var wasSettling = _isSettling;
                        if (wasSettling) { _isSettling = false; NotifyPlanStatus(); }
                        Dispatcher.UIThread.Post(() => { IsGuiding = true; GuideStatus = $"{avgDist.Value:F2}″ avg"; });
                    }
                    break;
                }
                case "Exposure":
                {
                    var state   = node["state"]?.GetValue<string>();
                    var tag     = node["tag"]?.GetValue<string>();
                    var isScienceExp = tag != "AutoFocus";
                    if (state == "start")
                    {
                        if (isScienceExp)
                        {
                            _isFrameExposing = true;
                            _isFindingTarget = false;
                            NotifyPlanStatus();
                        }
                        var expUs    = node["exp_us"]?.GetValue<long>() ?? 0;
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
                        if (isScienceExp) { _isFrameExposing = false; NotifyPlanStatus(); }
                        _exposureCountdownCts?.Cancel();
                        Dispatcher.UIThread.Post(() => StatusMessage = "Downloading…");
                    }
                    else if (state == "complete")
                    {
                        if (isScienceExp) { _isFrameExposing = false; NotifyPlanStatus(); }
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
                    {
                        _pendingImageDownload = true;
                        _isFrameExposing = false;
                        NotifyPlanStatus();
                        var plan  = node["progress"]?["cur_plan"];
                        var total = plan?["total"]?.GetValue<int>() ?? 0;
                        var lapse = plan?["lapse"]?.GetValue<int>() ?? 0;
                        if (total > 0)
                            SessionLog.Add(LogLevel.Info, $"Frame {lapse}/{total}", discord: false);
                    }
                    break;
                }
            }
        }
        catch { }
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
