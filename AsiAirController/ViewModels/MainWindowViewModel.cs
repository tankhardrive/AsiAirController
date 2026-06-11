using System.Text.RegularExpressions;
using Avalonia.Threading;
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
    private string _ipAddress = string.Empty;

    [ObservableProperty] private string _roofStatusFilePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopExposureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParkMountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SafeShutdownCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollRoofButtonText))]
    private bool _isPollingRoof;
    [ObservableProperty] private string _roofPollStatus = string.Empty;
    private CancellationTokenSource? _roofPollCts;

    public string PollRoofButtonText => IsPollingRoof ? "Stop Polling" : "Poll Roof";

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        IpAddress = _settings.IpAddress;
        RoofStatusFilePath = _settings.RoofStatusFilePath;
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

        var path = RoofStatusFilePath.Trim();
        if (string.IsNullOrEmpty(path)) { StatusMessage = "Enter a roof status file path first."; return; }

        _settings.RoofStatusFilePath = path;
        _settings.Save();

        _roofPollCts = new CancellationTokenSource();
        IsPollingRoof = true;
        StatusMessage = "Roof polling started…";

        _ = Task.Run(() => RoofPollLoopAsync(path, _roofPollCts.Token));
    }

    private async Task RoofPollLoopAsync(string path, CancellationToken ct)
    {
        var shutdownTriggered = false;
        while (!ct.IsCancellationRequested)
        {
            Dispatcher.UIThread.Post(() => RoofPollStatus = "Checking roof…");
            try
            {
                var result = await GetBestRoofStatusAsync(path, ct);
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

    private static async Task<RoofStatusResult> GetBestRoofStatusAsync(string filePath, CancellationToken ct)
    {
        var roofKey = RoofKeyFromFilePath(filePath);

        var tasks = new List<Task<RoofStatusResult>>();
        if (!string.IsNullOrEmpty(filePath))
            tasks.Add(AsiAirClient.ReadRoofStatusAsync(filePath));
        if (roofKey != null)
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

    private static string? RoofKeyFromFilePath(string path)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(path));
        if (dir == null) return null;
        var m = Regex.Match(dir, @"building-(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? $"roof{m.Groups[1].Value}" : null;
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
