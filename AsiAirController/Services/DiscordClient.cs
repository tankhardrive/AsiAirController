using System.Net.Http;
using System.Net.Http.Json;
using AsiAirController.Models;

namespace AsiAirController.Services;

internal static class DiscordClient
{
    private static readonly HttpClient _http = new();

    public static async Task PostAsync(string webhookUrl, LogEntry entry)
    {
        try
        {
            var level   = entry.Level == LogLevel.Warning ? "[WARN]" : entry.Level == LogLevel.Error ? "[ERR]" : "[INFO]";
            var content = $"{level} {entry.Timestamp:HH:mm:ss}  {entry.Message}";
            await _http.PostAsJsonAsync(webhookUrl, new { content });
        }
        catch { /* non-fatal */ }
    }
}
