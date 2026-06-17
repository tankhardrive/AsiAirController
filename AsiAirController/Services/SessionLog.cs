using System.Threading.Channels;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class SessionLog
{
    public static event Action<LogEntry>? EntryAdded;

    private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    private static string? _currentDate;
    private static string? _currentPath;

    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AsiAirController", "logs");

    static SessionLog() => _ = Task.Run(WriteLoopAsync);

    public static void Add(LogLevel level, string message, bool discord = true)
    {
        var entry = new LogEntry(DateTime.Now, level, message) { Discord = discord };
        EntryAdded?.Invoke(entry);
        _channel.Writer.TryWrite($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}  {entry.LevelText,-4}  {message}");
    }

    private static async Task WriteLoopAsync()
    {
        await foreach (var line in _channel.Reader.ReadAllAsync())
        {
            try
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (today != _currentDate)
                {
                    _currentDate = today;
                    Directory.CreateDirectory(LogFolder);
                    _currentPath = Path.Combine(LogFolder, $"session-{today}.log");
                }
                await File.AppendAllTextAsync(_currentPath!, line + "\n");
            }
            catch { /* non-fatal */ }
        }
    }
}
