using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PiSwitch.Models;

namespace PiSwitch;

public class PieMenuView : Canvas
{
    private const double InnerRadius = 15;
    private const double OuterRadius = 100;
    private const double CenterXY = 200;

    private readonly List<AppConfig> _apps;
    private readonly List<Path> _slicePaths = [];
    private int? _currentIndex;

    public event Action<int>? OnSelect;
    public event Action? OnCancel;

    public PieMenuView(List<AppConfig> apps)
    {
        _apps = apps;
        Width = 400;
        Height = 400;
        Background = Brushes.Transparent;
        Setup();
    }

    private void Setup()
    {
        // Background circle so the pie is visible on any desktop
        var bg = new Ellipse
        {
            Width = OuterRadius * 2 + 20,
            Height = OuterRadius * 2 + 20,
            Fill = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30))
        };
        SetLeft(bg, CenterXY - OuterRadius - 10);
        SetTop(bg, CenterXY - OuterRadius - 10);
        Children.Add(bg);

        // Create pie slices
        for (var i = 0; i < _apps.Count; i++)
        {
            var slice = CreateSlice(_apps[i]);
            _slicePaths.Add(slice);
            Children.Add(slice);
        }

        // Add labels
        for (var i = 0; i < _apps.Count; i++)
            CreateLabel(_apps[i]);

        // Center hole
        var centerHole = new Ellipse
        {
            Width = InnerRadius * 2,
            Height = InnerRadius * 2,
            Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
        };
        SetLeft(centerHole, CenterXY - InnerRadius);
        SetTop(centerHole, CenterXY - InnerRadius);
        Children.Add(centerHole);

        // Center dot
        var centerDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Brushes.White
        };
        SetLeft(centerDot, CenterXY - 3);
        SetTop(centerDot, CenterXY - 3);
        Children.Add(centerDot);
    }

    private Path CreateSlice(AppConfig app)
    {
        var startRad = app.StartAngle * Math.PI / 180;
        var endRad = app.EndAngle * Math.PI / 180;

        // WPF: Y increases downward, so negate sin for standard math angles
        var innerStart = new Point(
            CenterXY + Math.Cos(startRad) * InnerRadius,
            CenterXY - Math.Sin(startRad) * InnerRadius);
        var innerEnd = new Point(
            CenterXY + Math.Cos(endRad) * InnerRadius,
            CenterXY - Math.Sin(endRad) * InnerRadius);
        var outerStart = new Point(
            CenterXY + Math.Cos(startRad) * OuterRadius,
            CenterXY - Math.Sin(startRad) * OuterRadius);
        var outerEnd = new Point(
            CenterXY + Math.Cos(endRad) * OuterRadius,
            CenterXY - Math.Sin(endRad) * OuterRadius);

        var sliceAngle = app.EndAngle - app.StartAngle;
        var isLargeArc = Math.Abs(sliceAngle) > 180;

        var figure = new PathFigure { StartPoint = innerStart, IsClosed = true };

        // Line from inner start to outer start
        figure.Segments.Add(new LineSegment(outerStart, true));

        // Outer arc (start to end) — counterclockwise in WPF because Y is flipped
        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(OuterRadius, OuterRadius),
            0, isLargeArc,
            SweepDirection.Counterclockwise, true));

        // Line from outer end to inner end
        figure.Segments.Add(new LineSegment(innerEnd, true));

        // Inner arc (end to start) — clockwise in WPF because Y is flipped
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(InnerRadius, InnerRadius),
            0, isLargeArc,
            SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(app.Color) { Opacity = 0.34 },
            Stroke = new SolidColorBrush(Colors.White) { Opacity = 0.1 },
            StrokeThickness = 1,
            Tag = geometry // Store for hit testing
        };
    }

    private void CreateLabel(AppConfig app)
    {
        var midAngle = (app.StartAngle + app.EndAngle) / 2;
        var midRad = midAngle * Math.PI / 180;
        var labelRadius = (InnerRadius + OuterRadius) / 2;

        var x = CenterXY + Math.Cos(midRad) * labelRadius;
        var y = CenterXY - Math.Sin(midRad) * labelRadius; // WPF Y-flip

        // Number badge
        var numBorder = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Brushes.White,
            Child = new TextBlock
            {
                Text = app.Number.ToString(),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center
            }
        };
        SetLeft(numBorder, x - 13);
        SetTop(numBorder, y - 28);
        Children.Add(numBorder);

        // App name label
        var nameLabel = new TextBlock
        {
            Text = app.DisplayName,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = System.Windows.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 70,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.8,
                ShadowDepth = 0.5,
                BlurRadius = 1.5,
                Direction = 270
            }
        };
        SetLeft(nameLabel, x - 35);
        SetTop(nameLabel, y + 2);
        Children.Add(nameLabel);
    }

    public void Highlight(int? index)
    {
        if (_currentIndex == index) return;
        _currentIndex = index;

        for (var i = 0; i < _slicePaths.Count; i++)
        {
            var isSelected = i == index;
            var app = _apps[i];
            _slicePaths[i].Fill = new SolidColorBrush(app.Color) { Opacity = isSelected ? 1.0 : 0.34 };
            _slicePaths[i].Stroke = isSelected
                ? Brushes.White
                : new SolidColorBrush(Colors.White) { Opacity = 0.1 };
            _slicePaths[i].StrokeThickness = isSelected ? 2 : 1;
        }
    }

    private int? GetSliceIndexAtPoint(Point point)
    {
        var dx = point.X - CenterXY;
        var dy = -(point.Y - CenterXY); // Invert Y for standard math
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < InnerRadius || dist > OuterRadius + 30) return null;

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;

        for (var i = 0; i < _apps.Count; i++)
        {
            if (AngleInRange(angle, _apps[i].StartAngle, _apps[i].EndAngle))
                return i;
        }

        return null;
    }

    private static bool AngleInRange(double angle, double start, double end)
    {
        // Normalize to handle wrap-around
        static double Normalize(double a) => ((a % 360) + 360) % 360;

        var normAngle = Normalize(angle);
        var normStart = Normalize(start);
        var normEnd = Normalize(end);

        if (normStart <= normEnd)
            return normAngle >= normStart && normAngle <= normEnd;
        // Wraps around 0
        return normAngle >= normStart || normAngle <= normEnd;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        Highlight(GetSliceIndexAtPoint(pos));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Highlight(null);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        Highlight(GetSliceIndexAtPoint(pos));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_currentIndex.HasValue)
            OnSelect?.Invoke(_currentIndex.Value);
        else
            OnCancel?.Invoke();
    }
}
