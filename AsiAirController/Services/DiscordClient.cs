using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using AsiAirController.Models;

namespace AsiAirController.Services;

internal static class DiscordClient
{
    private static readonly HttpClient _http = new();

    // Creates a thread in a Discord forum channel via webhook.
    // Returns the thread (channel) ID to pass to subsequent PostAsync/PostImageAsync calls.
    public static async Task<string?> CreateForumThreadAsync(string webhookUrl, string threadName, string content)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { thread_name = threadName, content });
            var response = await _http.PostAsync(
                webhookUrl + "?wait=true",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(json)?["channel_id"]?.GetValue<string>();
        }
        catch { return null; }
    }

    public static async Task PostAsync(string webhookUrl, LogEntry entry, string? threadId = null)
    {
        try
        {
            var url     = threadId != null ? $"{webhookUrl}?thread_id={threadId}" : webhookUrl;
            var level   = entry.Level == LogLevel.Warning ? "[WARN]" : entry.Level == LogLevel.Error ? "[ERR]" : "[INFO]";
            var content = $"{level} {entry.Timestamp:HH:mm:ss}  {entry.Message}";
            await _http.PostAsJsonAsync(url, new { content });
        }
        catch { /* non-fatal */ }
    }

    public static async Task PostImageAsync(string webhookUrl, Bitmap bitmap, string caption, string? threadId = null)
    {
        try
        {
            var url = threadId != null ? $"{webhookUrl}?thread_id={threadId}" : webhookUrl;

            using var ms = new MemoryStream();
            bitmap.Save(ms);
            ms.Position = 0;

            using var form = new MultipartFormDataContent();
            var json = JsonSerializer.Serialize(new { content = caption });
            form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
            var imageContent = new StreamContent(ms);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "files[0]", "preview.png");
            await _http.PostAsync(url, form);
        }
        catch { /* non-fatal */ }
    }
}
