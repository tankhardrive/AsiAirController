using System.Threading.Channels;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class SessionLog
{
    public static event Action<LogEntry>? EntryAdded;

    // File-only log channel — also receives UI-level entries, plus Trace entries.
    private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    // One file per app launch so crashes produce a distinct file.
    private static readonly string _sessionStart = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
    private static string? _currentPath;

    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AsiAirController", "logs");

    static SessionLog()
    {
        _ = Task.Run(WriteLoopAsync);
        // Write session banner immediately so the file exists and is identifiable.
        _channel.Writer.TryWrite($"{'=',60}");
        _channel.Writer.TryWrite($"SESSION START  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _channel.Writer.TryWrite($"{'=',60}");
    }

    // Writes to the UI log, the file, and (optionally) Discord.
    public static void Add(LogLevel level, string message, bool discord = true)
    {
        var entry = new LogEntry(DateTime.Now, level, message) { Discord = discord };
        EntryAdded?.Invoke(entry);
        _channel.Writer.TryWrite($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}  {entry.LevelText,-4}  {message}");
    }

    // Writes to the file only — not shown in the UI log or sent to Discord.
    // Use for verbose diagnostic traces (connection events, command TX/RX, poll results).
    public static void Trace(string message)
    {
        _channel.Writer.TryWrite($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  TRAC  {message}");
    }

    private static async Task WriteLoopAsync()
    {
        await foreach (var line in _channel.Reader.ReadAllAsync())
        {
            try
            {
                if (_currentPath == null)
                {
                    Directory.CreateDirectory(LogFolder);
                    _currentPath = Path.Combine(LogFolder, $"session-{_sessionStart}.log");
                }
                await File.AppendAllTextAsync(_currentPath, line + "\n");
            }
            catch { /* non-fatal */ }
        }
    }
}
