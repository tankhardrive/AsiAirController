using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AsiAirController.Services;

namespace AsiAirController.Views;

public partial class SunTimelineControl : UserControl
{
    private static readonly IBrush DayBrush     = new SolidColorBrush(Color.Parse("#1F5B9E"));
    private static readonly IBrush CivilBrush   = new SolidColorBrush(Color.Parse("#C06818"));
    private static readonly IBrush NautBrush    = new SolidColorBrush(Color.Parse("#5A1870"));
    private static readonly IBrush AstroBrush   = new SolidColorBrush(Color.Parse("#1C0840"));
    private static readonly IBrush NightBrush   = new SolidColorBrush(Color.Parse("#07090E"));
    private static readonly IBrush NowBrush     = new SolidColorBrush(Color.Parse("#F0A030"));
    private static readonly IBrush LabelBrush   = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly Typeface LabelFace  = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private SunTimes? _times;

    public SunTimelineControl()
    {
        InitializeComponent();
        TimelineCanvas.SizeChanged += (_, _) => Redraw();
    }

    public void SetTimes(SunTimes times)
    {
        _times = times;
        Redraw();
    }

    public void Redraw()
    {
        var canvas = TimelineCanvas;
        canvas.Children.Clear();

        var w = canvas.Bounds.Width;
        var h = canvas.Bounds.Height;
        if (w < 10 || _times == null) return;

        var now = DateTime.Now;

        // sunrise-sunset.org returns same-calendar-date times, so morning dawn
        // values (04:xx) are numerically before evening dusk values (22:xx).
        // Shift morning times to the next calendar day so segments are in order.
        var sunrise   = _times.Sunrise;
        var civilDawn = _times.CivilDawn;
        var nautDawn  = _times.NauticalDawn;
        var astroDawn = _times.AstroDawn;
        if (astroDawn <= _times.AstroDusk)
        {
            astroDawn  = astroDawn.AddDays(1);
            nautDawn   = nautDawn.AddDays(1);
            civilDawn  = civilDawn.AddDays(1);
            sunrise    = sunrise.AddDays(1);
        }

        // Window: 3h before sunset → 3h after sunrise (always shows the full night)
        var spanStart = _times.Sunset.AddHours(-3);
        var spanEnd   = sunrise.AddHours(3);
        var totalSec  = (spanEnd - spanStart).TotalSeconds;

        double X(DateTime t) => Math.Clamp((t - spanStart).TotalSeconds / totalSec * w, 0, w);

        const double bandH  = 28.0;
        const double tickH  = 4.0;
        const double labelY = bandH + tickH + 1;
        const double fontSize = 9.0;

        var segments = new (DateTime Start, DateTime End, IBrush Fill)[]
        {
            (spanStart,            _times.Sunset,      DayBrush),
            (_times.Sunset,        _times.CivilDusk,   CivilBrush),
            (_times.CivilDusk,     _times.NauticalDusk, NautBrush),
            (_times.NauticalDusk,  _times.AstroDusk,   AstroBrush),
            (_times.AstroDusk,     astroDawn,           NightBrush),
            (astroDawn,            nautDawn,            AstroBrush),
            (nautDawn,             civilDawn,           NautBrush),
            (civilDawn,            sunrise,             CivilBrush),
            (sunrise,              spanEnd,             DayBrush),
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

        // Tick + label helper
        void Label(DateTime time, string text, bool anchorRight = false)
        {
            var x = X(time);
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
            var tx = anchorRight
                ? Math.Clamp(x - ft.Width, 0, w - ft.Width)
                : Math.Clamp(x - ft.Width / 2, 0, w - ft.Width);
            canvas.Children.Add(new TextBlock
            {
                Text       = text,
                FontSize   = fontSize,
                Foreground = LabelBrush,
                [Canvas.LeftProperty] = tx,
                [Canvas.TopProperty]  = labelY,
            });
        }

        Label(_times.Sunset,  _times.Sunset.ToString("HH:mm"));
        Label(_times.AstroDusk, "Dusk");
        Label(astroDawn,        "Dawn");
        Label(sunrise,          sunrise.ToString("HH:mm"));

        // "Now" marker — only drawn if inside the window
        if (now > spanStart && now < spanEnd)
        {
            var nowX = X(now);
            canvas.Children.Add(new Line
            {
                StartPoint      = new Point(nowX, 0),
                EndPoint        = new Point(nowX, bandH),
                Stroke          = NowBrush,
                StrokeThickness = 1.5,
            });
        }
    }
}
