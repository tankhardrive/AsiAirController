using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AsiAirController.ViewModels;

namespace AsiAirController.Views;

public partial class GuideGraphControl : UserControl
{
    private static readonly IBrush RaBrush   = new SolidColorBrush(Color.Parse("#3A7BD5"));
    private static readonly IBrush DecBrush  = new SolidColorBrush(Color.Parse("#E8A030"));
    private static readonly IBrush ZeroBrush = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush DimBrush  = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly Pen    RaPen     = new(RaBrush,  1.0);
    private static readonly Pen    DecPen    = new(DecBrush, 1.0);
    private static readonly Pen    ZeroPen   = new(ZeroBrush, 1.0);

    public GuideGraphControl()
    {
        InitializeComponent();
        GraphCanvas.SizeChanged += (_, _) => Redraw();
    }

    // Called by MainWindow code-behind when GuidePoints changes
    public void Redraw()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var pts = vm.GuidePoints;
        var canvas = GraphCanvas;
        canvas.Children.Clear();

        var w = canvas.Bounds.Width;
        var h = canvas.Bounds.Height;
        if (w < 10 || h < 10 || pts.Count == 0) return;

        // Y axis: ±3 arcsec range, auto-expand if data exceeds it
        var maxErr = Math.Max(3.0, pts.Max(p => Math.Max(Math.Abs(p.Ra), Math.Abs(p.Dec))) * 1.15);
        var midY   = h / 2.0;
        var scaleY = midY / maxErr;
        var scaleX = w / (double)Math.Max(pts.Count - 1, 1);

        // Zero line
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0,   midY),
            EndPoint   = new Point(w,   midY),
            Stroke     = ZeroPen.Brush,
            StrokeThickness = 1
        });

        // ±1" reference lines
        foreach (var sign in new[] { 1.0, -1.0 })
        {
            var yRef = midY - sign * scaleY;
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(0, yRef),
                EndPoint   = new Point(w, yRef),
                Stroke     = DimBrush,
                StrokeThickness = 0.5
            });
        }

        // Build polylines — skip settle/dither points (draw gaps instead)
        var raPoints  = new List<Point>();
        var decPoints = new List<Point>();

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var x = i * scaleX;

            if (p.IsSettle || p.IsDither)
            {
                FlushPolyline(canvas, raPoints,  RaPen);
                FlushPolyline(canvas, decPoints, DecPen);
                raPoints.Clear();
                decPoints.Clear();
                continue;
            }

            raPoints.Add( new Point(x, midY - p.Ra  * scaleY));
            decPoints.Add(new Point(x, midY - p.Dec * scaleY));
        }

        FlushPolyline(canvas, raPoints,  RaPen);
        FlushPolyline(canvas, decPoints, DecPen);
    }

    private static void FlushPolyline(Canvas canvas, List<Point> points, Pen pen)
    {
        if (points.Count < 2) return;
        canvas.Children.Add(new Polyline
        {
            Points          = new Avalonia.Collections.AvaloniaList<Point>(points),
            Stroke          = pen.Brush,
            StrokeThickness = pen.Thickness
        });
    }
}
