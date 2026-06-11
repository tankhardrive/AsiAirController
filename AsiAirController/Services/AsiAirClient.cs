using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AsiAirController.Services;

public record RoofStatusResult(string Status, DateTime? Timestamp, string Source);

public static class AsiAirClient
{
    private static readonly HttpClient Http = new();
    private const string RoofApiUrl = "https://api.bortle.org/api/sfro/roof_status";

    public static async Task SendCommandAsync(string host, int port, string method, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(host, port, cts.Token);
        var payload = Encoding.UTF8.GetBytes($"{{\"id\": 1, \"method\": \"{method}\"}}\n");
        await tcp.GetStream().WriteAsync(payload, cts.Token);
        await tcp.GetStream().FlushAsync(cts.Token);
        // Close immediately — device processes the command once it receives the data + FIN
        tcp.Client.Shutdown(SocketShutdown.Both);
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
        tcp.Client.Shutdown(SocketShutdown.Send);
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
