using Avalonia.Media.Imaging;

namespace AsiAirController.Services;

public static class ObservatoryCameraService
{
    private static readonly HttpClient Http = new();

    // building-0005 pattern: zero-padded 4-digit building number
    public static string BuildingCamUrl(int buildingId) =>
        $"https://files-api.tx.starfront.space/status-assets-public/building-{buildingId:D4}/current.jpg";

    // Shared site all-sky cam (building 9) — used by StellarVision for the sky/cloud view.
    // The per-building /allsky/ path shows the dome interior, not the sky.
    public static string AllSkyCamUrl() =>
        "https://files-api.tx.starfront.space/status-assets-public/building-0009/allsky/images/image.jpg";

    // Returns (bitmap, captureTime). captureTime from x-amz-meta-capture-time header if present.
    // Retries up to 3 times with a short back-off on transient stream errors.
    public static async Task<(Bitmap? Bitmap, DateTime? CaptureTime)> FetchSnapshotAsync(
        string url, CancellationToken ct = default)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 0) await Task.Delay(2000 * attempt, ct);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(45));

                // Fetch headers first to get capture-time metadata
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                DateTime? captureTime = null;
                if (response.Headers.TryGetValues("x-amz-meta-capture-time", out var vals))
                    _ = DateTime.TryParse(vals.FirstOrDefault(), out var dt) ? captureTime = dt : captureTime = null;

                // Buffer entire body before creating bitmap
                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                var bitmap = new Bitmap(new MemoryStream(bytes));
                return (bitmap, captureTime);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; }
        }
        throw last!;
    }
}
