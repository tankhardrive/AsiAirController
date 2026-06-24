using Avalonia;
using AsiAirController;

// Avalonia + Tmds.DBus (Linux D-Bus) shutdown race: the D-Bus read loop is
// cancelled during app exit and tries to Dispatcher.Send after the dispatcher
// has stopped, producing an unhandled TaskCanceledException on a threadpool
// thread. Catch it here and exit cleanly instead of crashing with a trace.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is TaskCanceledException or OperationCanceledException)
        Environment.Exit(0);
};

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
