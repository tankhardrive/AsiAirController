using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace AsiAirController.Services;

public static class AsiAirClient
{
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
        // Skip event messages (no "id") and return the response to our command
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

    public static async Task<string> ReadRoofStatusAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync()).Trim();
    }
}
