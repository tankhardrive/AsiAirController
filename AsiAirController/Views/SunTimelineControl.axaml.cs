using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using AsiAirController.Services;

namespace AsiAirController.Views;

public partial class SunTimelineControl : UserControl
{
    private static readonly IBrush DayBrush    = new SolidColorBrush(Color.Parse("#1F5B9E"));
    private static readonly IBrush CivilBrush  = new SolidColorBrush(Color.Parse("#C06818"));
    private static readonly IBrush NautBrush   = new SolidColorBrush(Color.Parse("#5A1870"));
    private static readonly IBrush AstroBrush  = new SolidColorBrush(Color.Parse("#1C0840"));
    private static readonly IBrush NightBrush  = new SolidColorBrush(Color.Parse("#07090E"));
    private static readonly IBrush NowBrush    = new SolidColorBrush(Color.Parse("#F0A030"));
    private static readonly IBrush LabelBrush  = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    // All stored in UTC — eliminates DateTimeKind.Unspecified comparison issues.
    private record UtcTimes(
        DateTime Sunset, DateTime CivilDusk, DateTime NautDusk, DateTime AstroDusk,
        DateTime AstroDawn, DateTime NautDawn, DateTime CivilDawn, DateTime Sunrise,
        string SunsetLabel, string SunriseLabel, TimeZoneInfo Tz);

    private UtcTimes? _utc;
    private readonly DispatcherTimer _timer;

    // Kept only for the "now" label formatting in the observatory timezone
    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Local;

    public SunTimelineControl()
    {
        InitializeComponent();
        TimelineCanvas.SizeChanged += (_, _) => Redraw();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Redraw();

        AttachedToVisualTree   += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void SetTimes(SunTimes times)
    {
        var tz = times.TimeZone;
        TimeZone = tz;

        // Convert local (Unspecified) datetimes from the observatory TZ to UTC.
        // This is the only safe way to compare them with DateTime.UtcNow.
        static DateTime ToUtc(DateTime local, TimeZoneInfo tz) =>
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);

        // Apply day correction in local time first (dawn times are same calendar date
        // as dusk but represent the following morning).
        var astroDawn = times.AstroDawn;
        var nautDawn  = times.NauticalDawn;
        var civilDawn = times.CivilDawn;
        var sunrise   = times.Sunrise;
        if (astroDawn <= times.AstroDusk)
        {
            astroDawn = astroDawn.AddDays(1);
            nautDawn  = nautDawn.AddDays(1);
            civilDawn = civilDawn.AddDays(1);
            sunrise   = sunrise.AddDays(1);
        }

        _utc = new UtcTimes(
            Sunset:      ToUtc(times.Sunset, tz),
            CivilDusk:   ToUtc(times.CivilDusk, tz),
            NautDusk:    ToUtc(times.NauticalDusk, tz),
            AstroDusk:   ToUtc(times.AstroDusk, tz),
            AstroDawn:   ToUtc(astroDawn, tz),
            NautDawn:    ToUtc(nautDawn, tz),
            CivilDawn:   ToUtc(civilDawn, tz),
            Sunrise:     ToUtc(sunrise, tz),
            SunsetLabel:  times.Sunset.ToString("HH:mm"),
            SunriseLabel: sunrise.ToString("HH:mm"),
            Tz: tz
        );

        Redraw();
    }

    public void Redraw()
    {
        var canvas = TimelineCanvas;
        canvas.Children.Clear();

        var w = canvas.Bounds.Width;
        var h = canvas.Bounds.Height;
        if (w < 10 || _utc == null) return;

        var u = _utc;
        var nowUtc      = DateTime.UtcNow;
        var spanStartUtc = u.Sunset.AddHours(-3);
        var spanEndUtc   = u.Sunrise.AddHours(3);
        var totalSec     = (spanEndUtc - spanStartUtc).TotalSeconds;
        if (totalSec <= 0) return;

        double X(DateTime utc) =>
            Math.Clamp((utc - spanStartUtc).TotalSeconds / totalSec * w, 0, w);

        const double bandH   = 28.0;
        const double tickH   = 4.0;
        const double labelY  = bandH + tickH + 1;
        const double fontSize = 9.0;

        var segments = new (DateTime Start, DateTime End, IBrush Fill)[]
        {
            (spanStartUtc, u.Sunset,   DayBrush),
            (u.Sunset,     u.CivilDusk, CivilBrush),
            (u.CivilDusk,  u.NautDusk,  NautBrush),
            (u.NautDusk,   u.AstroDusk, AstroBrush),
            (u.AstroDusk,  u.AstroDawn, NightBrush),
            (u.AstroDawn,  u.NautDawn,  AstroBrush),
            (u.NautDawn,   u.CivilDawn, NautBrush),
            (u.CivilDawn,  u.Sunrise,   CivilBrush),
            (u.Sunrise,    spanEndUtc,  DayBrush),
        };

        foreach (var (start, end, brush) in segments)
        {
            var x1 = X(start);
            var x2 = X(end);
            if (x2 <= x1) continue;
            canvas.Children.Add(new Rectangle
            {
                Width  = x2 - x1,
                Height = bandH,
                Fill   = brush,
                [Canvas.LeftProperty] = x1,
                [Canvas.TopProperty]  = 0,
            });
        }

        void Label(DateTime utc, string text)
        {
            var x = X(utc);
            canvas.Children.Add(new Line
            {
                StartPoint      = new Point(x, bandH),
                EndPoint        = new Point(x, bandH + tickH),
                Stroke          = LabelBrush,
                StrokeThickness = 0.5,
            });
            var ft = new FormattedText(text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelFace, fontSize, LabelBrush);
            var tx = Math.Clamp(x - ft.Width / 2, 0, w - ft.Width);
            canvas.Children.Add(new TextBlock
            {
                Text       = text,
                FontSize   = fontSize,
                Foreground = LabelBrush,
                [Canvas.LeftProperty] = tx,
                [Canvas.TopProperty]  = labelY,
            });
        }

        Label(u.Sunset,   u.SunsetLabel);
        Label(u.AstroDusk, "Night");
        Label(u.AstroDawn, "Dawn");
        Label(u.Sunrise,  u.SunriseLabel);

        // "Now" marker — pure UTC arithmetic, no timezone mismatch possible
        var nowX    = X(nowUtc);
        var nowText = TimeZoneInfo.ConvertTime(nowUtc, u.Tz).ToString("HH:mm");

        canvas.Children.Add(new Line
        {
            StartPoint      = new Point(nowX, 0),
            EndPoint        = new Point(nowX, bandH),
            Stroke          = NowBrush,
            StrokeThickness = 1.5,
        });

        var nowFt = new FormattedText(nowText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, LabelFace, fontSize, NowBrush);
        var nowTx = Math.Clamp(nowX - nowFt.Width / 2, 0, w - nowFt.Width);
        canvas.Children.Add(new TextBlock
        {
            Text       = nowText,
            FontSize   = fontSize,
            Foreground = NowBrush,
            [Canvas.LeftProperty] = nowTx,
            [Canvas.TopProperty]  = 2,
        });
    }
}
