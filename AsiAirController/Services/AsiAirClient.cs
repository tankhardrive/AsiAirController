using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AsiAirController.Services;

public record RoofStatusResult(string Status, DateTime? Timestamp, string Source);

public static class AsiAirClient
{
    private static readonly HttpClient Http = new();
    private const string RoofApiUrl = "https://api.bortle.org/api/sfro/roof_status";

    public static async Task SendCommandAsync(string host, int port, string method,
        string? paramsJson = null, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(host, port, cts.Token);
        var paramsStr = paramsJson != null ? $",\"params\":{paramsJson}" : string.Empty;
        var payload = Encoding.UTF8.GetBytes($"{{\"id\":1,\"method\":\"{method}\"{paramsStr}}}\n");
        await tcp.GetStream().WriteAsync(payload, cts.Token);
        await tcp.GetStream().FlushAsync(cts.Token);
        tcp.Client.Shutdown(SocketShutdown.Both);
    }

    // Sets exposure time then fires the shutter. exposureUs is in microseconds.
    public static async Task StartExposureAsync(string host, long exposureUs, CancellationToken ct = default)
    {
        await SendCommandAsync(host, 4700, "set_control_value",
            $"[\"Exposure\",{exposureUs},false]", ct);
        await SendCommandAsync(host, 4700, "start_exposure", "[\"light\"]", ct);
    }

    public static async Task<string> QueryAsync(string host, int port, string method, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(host, port, cts.Token);
        var payload = Encoding.UTF8.GetBytes($"{{\"id\": 1, \"method\": \"{method}\"}}\n");
        var stream = tcp.GetStream();
        await stream.WriteAsync(payload, cts.Token);
        await stream.FlushAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // Skip unsolicited events (no "id") and return the response to our command
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) throw new Exception("Connection closed before response.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(line); } catch { continue; }
            if (node?["id"] != null) return line;
        }
    }

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
        var status = roof["status"]!.GetValue<string>();
        var timeStr = roof["time"]?.GetValue<string>();
        DateTime? timestamp = null;
        // API time format: "2026-06-11 04:43:14AM"
        if (timeStr != null && DateTime.TryParseExact(timeStr, "yyyy-MM-dd hh:mm:sstt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            timestamp = dt;
        return new RoofStatusResult(status, timestamp, "API");
    }

    public static async Task<(bool IsWorking, string State)> QueryCaptureStateAsync(string host, CancellationToken ct = default)
    {
        var json = await QueryAsync(host, 4700, "get_app_state", ct);
        var node = JsonNode.Parse(json);
        var capture = node?["result"]?["capture"];
        return (
            capture?["is_working"]?.GetValue<bool>() ?? false,
            capture?["state"]?.GetValue<string>() ?? string.Empty
        );
    }

    // Connects to port 4800, sends get_current_img, reads the streaming ZIP reply,
    // and returns the decompressed raw_data bytes (16-bit RGGB, 6248×4176).
    public static async Task<byte[]> FetchRawImageAsync(string host, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        await tcp.ConnectAsync(host, 4800, cts.Token);
        var stream = tcp.GetStream();

        var cmd = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"get_current_img\"}\n");
        await stream.WriteAsync(cmd, cts.Token);
        await stream.FlushAsync(cts.Token);

        // Read until we have the complete ZIP: stop when EOCD (PK\x05\x06 + 18 bytes) is present.
        using var ms = new MemoryStream();
        var buf  = new byte[65536];
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

        // Skip any preamble (e.g. the 80-byte keepalive the device may send); ZIP starts at PK\x03\x04.
        int zipStart = ByteIndexOf(allBytes, new byte[] { 0x50, 0x4B, 0x03, 0x04 }, maxLen: 512);
        if (zipStart < 0) throw new Exception("ZIP signature not found in port-4800 response.");

        using var zipMs      = new MemoryStream(allBytes, zipStart, allBytes.Length - zipStart, writable: false);
        using var zip        = new ZipArchive(zipMs, ZipArchiveMode.Read, leaveOpen: false);
        var entry            = zip.GetEntry("raw_data") ?? throw new Exception("raw_data not found in ZIP.");
        using var entryStream = entry.Open();
        using var rawMs      = new MemoryStream();
        await entryStream.CopyToAsync(rawMs, cts.Token);
        return rawMs.ToArray();
    }

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
        var idx = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
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
