using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AsiAirController.Models;

namespace AsiAirController.Services;

public record RoofStatusResult(string Status, DateTime? Timestamp, string Source);

// ── Persistent JSON-RPC TCP connection ────────────────────────────────────────
// One instance per port. Keeps the socket open, dispatches responses by id,
// and sends test_connection every 5 s on port 4700 to satisfy the server's
// heartbeat expectation (observed in Wireshark: ASI Air app sends it every 5 s).

internal sealed class AsiAirConnection : IAsyncDisposable
{
    private TcpClient?     _tcp;
    private NetworkStream? _stream;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    private readonly CancellationTokenSource _cts       = new();
    private readonly SemaphoreSlim           _writeLock = new(1, 1);
    private volatile bool _alive;

    public bool IsAlive => _alive;

    // Called for lines that have an "Event" field (unsolicited push events).
    internal Action<string>? EventReceived { get; set; }

    public async Task ConnectAsync(string host, int port, bool heartbeat, CancellationToken ct)
    {
        _tcp    = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(host, port, ct);
        _stream = _tcp.GetStream();
        _alive  = true;

        var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        _ = Task.Run(() => ReadLoopAsync(reader));
        if (heartbeat)
            _ = Task.Run(() => HeartbeatLoopAsync());
    }

    public async Task<string> SendAsync(AsiAirCommand cmd, CancellationToken ct)
    {
        if (!_alive) throw new InvalidOperationException("Connection is not alive.");

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[cmd.Id] = tcs;

        using var reg = ct.Register(() =>
        {
            if (_pending.TryRemove(cmd.Id, out _)) tcs.TrySetCanceled();
        });

        try
        {
            await WriteAsync(cmd.ToRequestJson(), ct);
        }
        catch
        {
            _pending.TryRemove(cmd.Id, out _);
            throw;
        }

        return await tcs.Task;
    }

    private async Task WriteAsync(string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream!.WriteAsync(bytes, ct);
            await _stream.FlushAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(StreamReader reader)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line == null) break; // server closed
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var node = JsonNode.Parse(line);
                    if (node?["id"] is { } idNode)
                    {
                        var id = idNode.GetValue<int>();
                        if (_pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(line);
                        // Unregistered IDs (e.g. heartbeat responses) are silently dropped.
                    }
                    else if (node?["Event"] is not null)
                    {
                        EventReceived?.Invoke(line);
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _alive = false;
            // Fail any callers still waiting so they get an exception rather than hanging.
            foreach (var (_, tcs) in _pending)
                tcs.TrySetException(new Exception("ASI Air connection lost."));
            _pending.Clear();
        }
    }

    // Sends test_connection every 5 s without registering a TCS — the response
    // arrives with an ID not in _pending and is silently dropped by the read loop.
    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(5000, _cts.Token);
                try { await WriteAsync(new Capture.TestConnection().ToRequestJson(), _cts.Token); }
                catch { /* connection loss is handled by the read loop */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _alive = false;
        try { _cts.Cancel(); } catch { }
        _tcp?.Dispose();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}

// ── AsiAirClient ──────────────────────────────────────────────────────────────

public static class AsiAirClient
{
    private static readonly HttpClient    Http     = new();
    private static readonly SemaphoreSlim ConnLock = new(1, 1);
    private const string RoofApiUrl = "https://api.bortle.org/api/sfro/roof_status";

    private static AsiAirConnection? _conn4700;
    private static AsiAirConnection? _conn4400;
    private static string?           _connectedHost;

    // Fired for all unsolicited Event messages received on port 4700.
    public static event Action<string>? AsiAirEvent;

    // Returns (or creates) the persistent connection for the given port.
    // Thread-safe: only one connection per port is kept alive at a time.
    private static async Task<AsiAirConnection> GetConnectionAsync(
        string host, int port, CancellationToken ct)
    {
        await ConnLock.WaitAsync(ct);
        try
        {
            // Host change → drop both connections and start fresh
            if (_connectedHost != host)
            {
                await TryDisposeAsync(_conn4700); _conn4700 = null;
                await TryDisposeAsync(_conn4400); _conn4400 = null;
                _connectedHost = host;
            }

            if (port == 4700)
            {
                if (_conn4700 is { IsAlive: true }) return _conn4700;
                await TryDisposeAsync(_conn4700);

                var conn = new AsiAirConnection();
                conn.EventReceived = line => AsiAirEvent?.Invoke(line);
                try
                {
                    await conn.ConnectAsync(host, 4700, heartbeat: true, ct);
                    // Initial handshake — matches what the official app does on connect
                    await conn.SendAsync(new Capture.TestConnection(), ct);
                }
                catch { await conn.DisposeAsync(); throw; }

                return _conn4700 = conn;
            }
            else // 4400 (mount + guide push events)
            {
                if (_conn4400 is { IsAlive: true }) return _conn4400;
                await TryDisposeAsync(_conn4400);

                var conn = new AsiAirConnection();
                conn.EventReceived = line => AsiAirEvent?.Invoke(line);
                try
                {
                    await conn.ConnectAsync(host, 4400, heartbeat: true, ct);
                    await conn.SendAsync(new Mount.TestConnection(), ct);
                }
                catch { await conn.DisposeAsync(); throw; }

                return _conn4400 = conn;
            }
        }
        finally { ConnLock.Release(); }
    }

    private static async Task TryDisposeAsync(AsiAirConnection? conn)
    {
        if (conn != null) await conn.DisposeAsync();
    }

    /// <summary>Sends a JSON-RPC command over the persistent connection and returns the raw response.</summary>
    public static async Task<string> CallAsync(string host, AsiAirCommand cmd, CancellationToken ct = default)
    {
        if (cmd.Port == 4800) throw new InvalidOperationException("Use FetchRawImageAsync for port 4800.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var conn = await GetConnectionAsync(host, cmd.Port, cts.Token);
        return await conn.SendAsync(cmd, cts.Token);
    }

    /// <summary>Opens the port-4400 (mount) connection so GuideStep push events begin flowing immediately.</summary>
    public static async Task EnsureMountConnectedAsync(string host, CancellationToken ct = default)
    {
        await GetConnectionAsync(host, 4400, ct);
    }

    // ── Domain methods ──────────────────────────────────────────────────────────

    public static async Task StartExposureAsync(string host, long exposureUs, CancellationToken ct = default)
    {
        await CallAsync(host, new Capture.SetControlValue("Exposure", exposureUs), ct);
        await CallAsync(host, new Capture.StartExposure(), ct);
    }

    public static async Task<(bool IsWorking, string State, string ExposureMode, int CompletedFrames, int TotalFrames, long LapseMs, long TotalMs, double? LastAfHfr)>
        QueryCaptureStateAsync(string host, CancellationToken ct = default)
    {
        var json    = await CallAsync(host, new Capture.GetAppState(), ct);
        var node    = JsonNode.Parse(json);
        var result  = node?["result"];
        var capture = result?["capture"];
        var curPlan = capture?["progress"]?["cur_plan"];
        var afPt    = result?["auto_focus"]?["result"]?["last_point"]?.AsArray();
        var lastAfHfr = afPt?.Count >= 2 ? afPt[1]?.GetValue<double>() : (double?)null;
        return (
            capture?["is_working"]?.GetValue<bool>()      ?? false,
            capture?["state"]?.GetValue<string>()         ?? string.Empty,
            capture?["exposure_mode"]?.GetValue<string>() ?? string.Empty,
            curPlan?["lapse"]?.GetValue<int>()            ?? 0,
            curPlan?["total"]?.GetValue<int>()            ?? 0,
            capture?["lapse_ms"]?.GetValue<long>()        ?? 0,
            capture?["total_ms"]?.GetValue<long>()        ?? 0,
            lastAfHfr
        );
    }

    /// <summary>
    /// Checks whether any plan-related activity is still in progress (capture, meridian flip,
    /// autofocus, or goto). Returns (Active, IsMeridFlip, IsFocusing).
    /// Used to distinguish a true plan completion from a transient pause.
    /// </summary>
    public static async Task<(bool Active, bool IsMeridFlip, bool IsFocusing)>
        QueryPlanSubstateAsync(string host, CancellationToken ct = default)
    {
        var json   = await CallAsync(host, new Capture.GetAppState(), ct);
        var result = JsonNode.Parse(json)?["result"];
        var captureWorking = result?["capture"]?["is_working"]?.GetValue<bool>()    ?? false;
        var meridFlip      = result?["merid_flip"]?["is_working"]?.GetValue<bool>() ?? false;
        var autoFocus      = result?["auto_focus"]?["is_working"]?.GetValue<bool>() ?? false;
        var autoGoto       = result?["auto_goto"]?["is_working"]?.GetValue<bool>()  ?? false;
        return (captureWorking || meridFlip || autoFocus || autoGoto, meridFlip, autoFocus);
    }

    // Returns camera sensor temperature in °C (value from device is tenths of a degree).
    // Also returns cooling power percentage if the camera has a cooler (null otherwise).
    public static async Task<(double? TempC, int? CoolPowerPerc)>
        QueryCameraTemperatureAsync(string host, CancellationToken ct = default)
    {
        try
        {
            var json   = await CallAsync(host, new Capture.GetControlValue("Temperature"), ct);
            var raw    = JsonNode.Parse(json)?["result"]?["value"]?.GetValue<double>();
            var tempC  = raw.HasValue ? raw.Value / 10.0 : (double?)null;

            int? coolPower = null;
            try
            {
                var cj = await CallAsync(host, new Capture.GetControlValue("CoolPowerPerc"), ct);
                coolPower = JsonNode.Parse(cj)?["result"]?["value"]?.GetValue<int>();
            }
            catch { }

            return (tempC, coolPower);
        }
        catch { return (null, null); }
    }

    public static async Task<IReadOnlyList<PlanSummary>> ListPlansAsync(string host, CancellationToken ct = default)
    {
        var json = await CallAsync(host, new Plan.ListPlan(), ct);
        var arr  = JsonNode.Parse(json)?["result"]?.AsArray();
        if (arr == null) return [];
        return arr.Select(p => new PlanSummary(
            p!["id"]!.GetValue<int>(),
            p["plan_name"]!.GetValue<string>(),
            p["enable"]!.GetValue<bool>(),
            p["target_cnt"]?.GetValue<int>() ?? 0
        )).ToList();
    }

    public static async Task<PlanDetail?> GetActivePlanDetailAsync(string host, CancellationToken ct = default)
    {
        var json     = await CallAsync(host, new Plan.GetEnabledPlan(), ct);
        var planNode = JsonNode.Parse(json)?["result"]?.AsArray()?.FirstOrDefault();
        if (planNode == null) return null;

        var targets     = planNode["targets"]?.AsArray() ?? new JsonArray();
        var targetNames = targets
            .Select(t => t?["target_name"]?.GetValue<string>() ?? string.Empty)
            .ToList();

        var slots = targets
            .SelectMany(t => t?["seqs"]?.AsArray()?.Select(s => new PlanSlot(
                s!["type"]?.GetValue<string>()   ?? "light",
                s["filter"]?.GetValue<int>()     ?? 0,
                s["exp"]?.GetValue<double>()     ?? 0,
                s["gain"]?.GetValue<int>()       ?? 0,
                s["bin"]?.GetValue<int>()        ?? 1,
                s["repeat"]?.GetValue<int>()     ?? 0,
                s["lapsed"]?.GetValue<int>()     ?? 0
            )) ?? Enumerable.Empty<PlanSlot>())
            .ToList();

        return new PlanDetail(
            planNode["plan_name"]!.GetValue<string>(),
            planNode["total_time_sec"]?.GetValue<long>()   ?? 0,
            planNode["left_time_sec"]?.GetValue<long>()    ?? 0,
            planNode["total_size_m"]?.GetValue<double>()   ?? 0,
            planNode["left_size_m"]?.GetValue<double>()    ?? 0,
            planNode["start_time"]?["type"]?.GetValue<string>() ?? "none",
            planNode["end_time"]?["type"]?.GetValue<string>()   ?? "none",
            targetNames,
            slots);
    }

    public static async Task SwapActivePlanAsync(string host, IReadOnlyList<PlanSummary> allPlans, int targetId, CancellationToken ct = default)
    {
        var assignments = allPlans.Select(p => (p.Id, p.Id == targetId)).ToList();
        await CallAsync(host, new Plan.ImportPlan(assignments), ct);
    }

    public static async Task ResetPlanAsync(string host, int planId, CancellationToken ct = default)
        => await CallAsync(host, new Plan.ResetPlan(planId), ct);

    public static async Task StartPlanAsync(string host, CancellationToken ct = default)
        => await CallAsync(host, new Capture.StartExposure(), ct);

    public static async Task SetPageAsync(string host, string page, CancellationToken ct = default)
        => await CallAsync(host, new Capture.SetPage(page), ct);

    // ── Image download (port 4800 — binary protocol, one-off connection) ────────

    public static async Task<byte[]> FetchRawImageAsync(string host, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var cmd = new Image.GetCurrentImage();
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        await tcp.ConnectAsync(host, cmd.Port, cts.Token);
        var stream = tcp.GetStream();

        await stream.WriteAsync(Encoding.UTF8.GetBytes(cmd.ToRequestJson()), cts.Token);
        await stream.FlushAsync(cts.Token);

        using var ms = new MemoryStream();
        var buf     = new byte[65536];
        byte[] eocdSig = { 0x50, 0x4B, 0x05, 0x06 };

        while (true)
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
            if (n == 0) break;
            ms.Write(buf, 0, n);
            progress?.Report(ms.Length);

            int tailLen = (int)Math.Min(1024, ms.Length);
            var tail    = new byte[tailLen];
            ms.Position = ms.Length - tailLen;
            _ = ms.Read(tail, 0, tailLen);
            ms.Seek(0, SeekOrigin.End);

            int eocdIdx = ByteIndexOf(tail, eocdSig);
            if (eocdIdx >= 0 && eocdIdx + 22 <= tailLen) break;
        }

        var allBytes = ms.ToArray();
        int zipStart = ByteIndexOf(allBytes, new byte[] { 0x50, 0x4B, 0x03, 0x04 }, maxLen: 512);
        if (zipStart < 0) throw new Exception("ZIP signature not found in port-4800 response.");

        using var zipMs       = new MemoryStream(allBytes, zipStart, allBytes.Length - zipStart, writable: false);
        using var zip         = new ZipArchive(zipMs, ZipArchiveMode.Read, leaveOpen: false);
        var entry             = zip.GetEntry("raw_data") ?? throw new Exception("raw_data not found in ZIP.");
        using var entryStream = entry.Open();
        using var rawMs       = new MemoryStream();
        await entryStream.CopyToAsync(rawMs, cts.Token);
        return rawMs.ToArray();
    }

    // ── Roof status ─────────────────────────────────────────────────────────────

    public static async Task<IReadOnlyList<string>> FetchRoofKeysAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var json = await Http.GetStringAsync(RoofApiUrl, cts.Token);
        var node = JsonNode.Parse(json) ?? throw new Exception("Invalid JSON.");
        return node.AsObject()
            .Select(kvp => kvp.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<RoofStatusResult> ReadRoofStatusAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        var content = (await reader.ReadToEndAsync()).Trim();
        return ParseFileContent(content);
    }

    public static async Task<RoofStatusResult> FetchRoofStatusFromApiAsync(string roofKey, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var json = await Http.GetStringAsync(RoofApiUrl, cts.Token);
        var node = JsonNode.Parse(json) ?? throw new Exception("Invalid JSON from roof API.");
        var roof = node[roofKey] ?? throw new Exception($"Roof key '{roofKey}' not found in API response.");
        var status  = roof["status"]!.GetValue<string>();
        var timeStr = roof["time"]?.GetValue<string>();
        DateTime? timestamp = null;
        if (timeStr != null && DateTime.TryParseExact(timeStr, "yyyy-MM-dd hh:mm:sstt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            timestamp = dt;
        return new RoofStatusResult(status, timestamp, "API");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static int ByteIndexOf(byte[] data, byte[] pattern, int maxLen = -1)
    {
        int limit = (maxLen > 0 ? Math.Min(data.Length, maxLen) : data.Length) - pattern.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static RoofStatusResult ParseFileContent(string content)
    {
        var marker = "Roof Status: ";
        var idx    = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var status = idx >= 0
            ? content[(idx + marker.Length)..].Trim().Split('\n')[0].Trim()
            : content;

        var parts = content.Split(' ');
        DateTime? timestamp = null;
        if (parts.Length >= 2 && DateTime.TryParseExact(
                parts[0] + " " + parts[1], "yyyy-MM-dd hh:mm:sstt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            timestamp = dt;

        return new RoofStatusResult(status, timestamp, "File");
    }
}
