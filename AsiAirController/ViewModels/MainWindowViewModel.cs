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
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollRoofButtonText))]
    private bool _isPollingRoof;
    [ObservableProperty] private string _roofPollStatus = string.Empty;
    private CancellationTokenSource? _roofPollCts;

    [ObservableProperty] private string _exposureSeconds = "10";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TogglePreviewButtonText))]
    private bool _isPreviewActive;
    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private string _previewStatus = string.Empty;
    private CancellationTokenSource? _previewCts;

    public string PollRoofButtonText => IsPollingRoof ? "Stop Polling" : "Poll Roof";
    public string TogglePreviewButtonText => IsPreviewActive ? "Stop Preview" : "Live Preview";

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        IpAddress = _settings.IpAddress;
        RoofStatusFilePath = _settings.RoofStatusFilePath;
        ExposureSeconds = _settings.ExposureSeconds;
        // RoofKey is intentionally set after the list arrives in LoadRoofKeysAsync,
        // so the ComboBox sees a real "" → "roof5" change and selects the item.
        _ = LoadRoofKeysAsync();
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
        StatusMessage = $"Starting {secs}s exposure…";
        try
        {
            await AsiAirClient.StartExposureAsync(host, (long)(secs * 1_000_000));
            StatusMessage = $"{secs}s exposure started.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
                var (isWorking, _) = await AsiAirClient.QueryCaptureStateAsync(host, ct);

                if (wasWorking && !isWorking)
                {
                    Dispatcher.UIThread.Post(() => PreviewStatus = "Downloading image...");
                    var rawData = await AsiAirClient.FetchRawImageAsync(host, ct);
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

                wasWorking = isWorking;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => PreviewStatus = $"Error: {ex.Message}");
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsPreviewActive = false;
            PreviewStatus = string.Empty;
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
