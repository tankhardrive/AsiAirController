using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
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

    public static async Task PostImageAsync(string webhookUrl, Bitmap bitmap, string caption)
    {
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms);
            ms.Position = 0;

            using var form = new MultipartFormDataContent();
            var json = JsonSerializer.Serialize(new { content = caption });
            form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
            var imageContent = new StreamContent(ms);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "files[0]", "preview.png");
            await _http.PostAsync(webhookUrl, form);
        }
        catch { /* non-fatal */ }
    }
}
