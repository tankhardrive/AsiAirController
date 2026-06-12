using Avalonia.Media;

namespace AsiAirController.Models;

public enum LogLevel { Info, Warning, Error }

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    private static readonly IBrush InfoBadge    = new SolidColorBrush(Color.Parse("#3A3A5A"));
    private static readonly IBrush WarnBadge    = new SolidColorBrush(Color.Parse("#D4943A"));
    private static readonly IBrush ErrorBadge   = new SolidColorBrush(Color.Parse("#E05555"));
    private static readonly IBrush InfoText     = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush WarnText     = new SolidColorBrush(Color.Parse("#D4B070"));
    private static readonly IBrush ErrorText    = new SolidColorBrush(Color.Parse("#DD8888"));

    public string  LevelText       => Level == LogLevel.Warning ? "WARN" : Level == LogLevel.Error ? "ERR" : "INFO";
    public IBrush  LevelBadgeBrush => Level == LogLevel.Warning ? WarnBadge : Level == LogLevel.Error ? ErrorBadge : InfoBadge;
    public IBrush  MessageBrush    => Level == LogLevel.Warning ? WarnText  : Level == LogLevel.Error ? ErrorText  : InfoText;
}
