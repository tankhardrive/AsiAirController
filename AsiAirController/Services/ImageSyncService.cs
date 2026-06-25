using System.Diagnostics;

namespace AsiAirController.Services;

public static class ImageSyncService
{
    public record SyncResult(
        int  FilesScanned,
        int  FilesCopied,
        long BytesCopied,
        TimeSpan Duration,
        IReadOnlyList<(string RelativePath, string Error)> PersistentFailures);

    private static readonly HashSet<string> FitsExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".fits", ".fit", ".fts" };

    private static bool IsFitsFile(string path) =>
        FitsExtensions.Contains(Path.GetExtension(path));

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024              => $"{bytes} B",
        < 1_048_576          => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824      => $"{bytes / 1_048_576.0:F1} MB",
        _                    => $"{bytes / 1_073_741_824.0:F2} GB"
    };

    private static string BuildDestPath(string destRoot, string sourceRoot, string srcFile, bool appendDateTime)
    {
        var relative = Path.GetRelativePath(sourceRoot, srcFile);
        if (!appendDateTime)
            return Path.Combine(destRoot, relative);

        var dir       = Path.GetDirectoryName(relative) ?? string.Empty;
        var stem      = Path.GetFileNameWithoutExtension(relative);
        var ext       = Path.GetExtension(relative);
        var timestamp = File.GetLastWriteTime(srcFile).ToString("yyyyMMdd_HHmmss");
        return Path.Combine(destRoot, dir, $"{stem}_{timestamp}{ext}");
    }

    public static async Task<SyncResult> SyncAsync(
        string sourcePath,
        string destPath,
        bool appendDateTime,
        Action<string> onStatus,
        CancellationToken ct,
        IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source folder not found: {sourcePath}");

        try { Directory.CreateDirectory(destPath); }
        catch (Exception ex)
        {
            throw new IOException($"Cannot access destination folder: {ex.Message}", ex);
        }

        // Enumerate all FITS files under source
        onStatus("Sync — scanning source…");
        var allSourceFiles = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Where(IsFitsFile)
            .ToList();

        // Build the list of files that need copying (missing or wrong size at dest)
        var toCopy     = new List<string>();
        long totalBytes = 0;
        foreach (var srcFile in allSourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            var dstFile = BuildDestPath(destPath, sourcePath, srcFile, appendDateTime);
            var srcLen  = new FileInfo(srcFile).Length;
            if (!File.Exists(dstFile) || new FileInfo(dstFile).Length != srcLen)
            {
                toCopy.Add(srcFile);
                totalBytes += srcLen;
            }
        }

        if (toCopy.Count == 0)
            return new SyncResult(allSourceFiles.Count, 0, 0, sw.Elapsed, []);

        // Up to 3 passes: first full copy, then up to 2 retry passes on failures
        long bytesCopied  = 0;
        int  filesCopied  = 0;
        var  pending      = new List<string>(toCopy);
        var  lastErrors   = new Dictionary<string, string>();

        for (int pass = 1; pass <= 3 && pending.Count > 0; pass++)
        {
            if (pass > 1)
            {
                var delaySec = pass == 2 ? 5 : 10;
                onStatus($"Retry {pass - 1}/2 — waiting {delaySec}s before {pending.Count} file(s)…");
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
            }

            var stillFailed = new List<string>();

            for (int i = 0; i < pending.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var srcFile  = pending[i];
                var dstFile  = BuildDestPath(destPath, sourcePath, srcFile, appendDateTime);
                var fileName = Path.GetFileName(dstFile);

                var passLabel = pass == 1
                    ? $"Sync — {filesCopied + 1}/{toCopy.Count}  ·  {fileName}"
                    : $"Retry {pass - 1}/2 — {i + 1}/{pending.Count}  ·  {fileName}";
                onStatus(passLabel);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                    var srcLen = new FileInfo(srcFile).Length;

                    await using var src = new FileStream(srcFile, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var dst = new FileStream(dstFile, FileMode.Create, FileAccess.Write,
                        FileShare.None, 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await src.CopyToAsync(dst, ct);

                    bytesCopied += srcLen;
                    filesCopied++;
                    lastErrors.Remove(srcFile);
                    progress?.Report(filesCopied / (double)toCopy.Count);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    stillFailed.Add(srcFile);
                    lastErrors[srcFile] = ex.Message;
                    // Remove a partial dest file so next pass starts clean
                    try
                    {
                        if (File.Exists(dstFile)) File.Delete(dstFile);
                    }
                    catch { /* best-effort */ }
                }
            }

            pending = stillFailed;
        }

        var persistent = lastErrors
            .Select(kv => (Path.GetRelativePath(destPath, BuildDestPath(destPath, sourcePath, kv.Key, appendDateTime)), kv.Value))
            .ToList();

        onStatus($"Sync complete — {filesCopied} files · {FormatBytes(bytesCopied)}");
        return new SyncResult(allSourceFiles.Count, filesCopied, bytesCopied, sw.Elapsed, persistent);
    }
}
