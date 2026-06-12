using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AsiAirController.Models;

namespace AsiAirController.Services;

public record RoofStatusResult(string Status, DateTime? Timestamp, string Source);

public static class AsiAirClient
{
    private static readonly HttpClient Http = new();
    private const string RoofApiUrl = "https://api.bortle.org/api/sfro/roof_status";

    /// <summary>
    /// Sends a command and returns the JSON-RPC response. All ASI Air TCP calls go through here.
    /// </summary>
    public static async Task<string> CallAsync(string host, AsiAirCommand cmd, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(host, cmd.Port, cts.Token);
        var stream  = tcp.GetStream();
        var payload = Encoding.UTF8.GetBytes(cmd.ToRequestJson());
        await stream.WriteAsync(payload, cts.Token);
        await stream.FlushAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // Skip unsolicited push events (no "id") until we get our response.
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) throw new Exception("Connection closed before response.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(line); } catch { continue; }
            if (node?["id"]?.GetValue<int>() == cmd.Id) return line;
        }
    }

    // ── Domain methods ──────────────────────────────────────────────────────

    /// <summary>Sets exposure time (µs) then fires the shutter.</summary>
    public static async Task StartExposureAsync(string host, long exposureUs, CancellationToken ct = default)
    {
        await CallAsync(host, new Capture.SetControlValue("Exposure", exposureUs), ct);
        await CallAsync(host, new Capture.StartExposure(), ct);
    }

    public static async Task<(bool IsWorking, string State, string ExposureMode, int CompletedFrames, int TotalFrames, long LapseMs, long TotalMs)>
        QueryCaptureStateAsync(string host, CancellationToken ct = default)
    {
        var json    = await CallAsync(host, new Capture.GetAppState(), ct);
        var node    = JsonNode.Parse(json);
        var capture = node?["result"]?["capture"];
        var curPlan = capture?["progress"]?["cur_plan"];
        return (
            capture?["is_working"]?.GetValue<bool>()      ?? false,
            capture?["state"]?.GetValue<string>()         ?? string.Empty,
            capture?["exposure_mode"]?.GetValue<string>() ?? string.Empty,
            curPlan?["lapse"]?.GetValue<int>()            ?? 0,
            curPlan?["total"]?.GetValue<int>()            ?? 0,
            capture?["lapse_ms"]?.GetValue<long>()        ?? 0,
            capture?["total_ms"]?.GetValue<long>()        ?? 0
        );
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

        // Sequences live inside targets[].seqs[], not in get_target_sequences
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
    {
        await CallAsync(host, new Plan.ResetPlan(planId), ct);
    }

    public static async Task StartPlanAsync(string host, CancellationToken ct = default)
    {
        await CallAsync(host, new Capture.StartExposure(), ct);
    }

    public static async Task SetPageAsync(string host, string page, CancellationToken ct = default)
    {
        await CallAsync(host, new Capture.SetPage(page), ct);
    }

    // ── Image download ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the last raw image from port 4800 as a streaming ZIP and returns decompressed raw_data bytes.
    /// Uses a custom read loop because the response is binary, not JSON-RPC.
    /// </summary>
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

            // Scan only the last 1 KB to keep this O(chunk) rather than O(total)
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

    // ── Roof status ─────────────────────────────────────────────────────────

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
        // API time format: "2026-06-11 04:43:14AM"
        if (timeStr != null && DateTime.TryParseExact(timeStr, "yyyy-MM-dd hh:mm:sstt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            timestamp = dt;
        return new RoofStatusResult(status, timestamp, "API");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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

        // File format: "2026-06-09 05:16:34AM CST Roof Status: CLOSED"
        var parts = content.Split(' ');
        DateTime? timestamp = null;
        if (parts.Length >= 2 && DateTime.TryParseExact(
                parts[0] + " " + parts[1], "yyyy-MM-dd hh:mm:sstt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            timestamp = dt;

        return new RoofStatusResult(status, timestamp, "File");
    }
}
