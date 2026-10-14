using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PowerLens.Core;

namespace PowerLens.App.Controls;

/// <summary>Live chart: battery power (+/−) and estimated charger output, with plugged-in periods shaded.</summary>
internal sealed class PowerChartView : FrameworkElement
{
    private IReadOnlyList<HistoryPoint> _points = [];

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);

    public void SetData(IReadOnlyList<HistoryPoint> points)
    {
        _points = points;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = Res("TextMuted", Brushes.Gray);
        var text = Res("TextPrimary", Brushes.Black);
        var battery = Res("ChartBattery", Brushes.Green);
        var input = Res("ChartInput", Brushes.Blue);
        var shade = Res("ChartAcShade", Brushes.AliceBlue);
        var grid = new Pen(Res("ChartGrid", Brushes.LightGray), 1);
        var zero = new Pen(Res("ChartZero", Brushes.Gray), 1.2);

        var plot = new Rect(44, 30, Math.Max(0, ActualWidth - 52), Math.Max(0, ActualHeight - 52));
        // Transparent hit surface so the whole area belongs to the chart.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (plot.Width < 50 || plot.Height < 30) return;

        DateTimeOffset end = _points.Count > 0 ? _points[^1].Time : DateTimeOffset.Now;
        DateTimeOffset start = end - Window;
        double X(DateTimeOffset t) => plot.Left + (t - start) / Window * plot.Width;

        var values = _points.SelectMany(p => new[] { p.BatteryWatts, p.InputWatts }).OfType<double>().ToList();
        double min = Math.Min(0, values.DefaultIfEmpty(0).Min());
        double max = Math.Max(10, values.DefaultIfEmpty(0).Max());
        double step = NiceStep((max - min) / 5);
        min = Math.Floor(min / step) * step;
        max = Math.Ceiling(max / step) * step;
        double Y(double w) => plot.Bottom - (w - min) / (max - min) * plot.Height;

        // Plugged-in shading.
        for (int i = 0; i < _points.Count - 1; i++)
        {
            if (!_points[i].AcOnline) continue;
            double x0 = Math.Max(plot.Left, X(_points[i].Time));
            double x1 = Math.Min(plot.Right, X(_points[i + 1].Time));
            if (x1 > x0) dc.DrawRectangle(shade, null, new Rect(x0, plot.Top, x1 - x0, plot.Height));
        }

        // Grid and labels.
        for (double v = min; v <= max + step / 2; v += step)
        {
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(Math.Abs(v) < 1e-9 ? zero : grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text($"{v:0} W", 11, muted, ppd);
            dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        int minutes = Math.Max(1, (int)Window.TotalMinutes);
        for (int m = 0; m <= minutes; m += Math.Max(1, minutes / 5))
        {
            double x = X(end - TimeSpan.FromMinutes(minutes - m));
            var label = Text(m == minutes ? "now" : $"−{minutes - m} min", 11, muted, ppd);
            dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, plot.Left - 10, plot.Right - label.Width), plot.Bottom + 6));
        }

        DrawSeries(dc, p => p.BatteryWatts, battery, X, Y);
        DrawSeries(dc, p => p.InputWatts, input, X, Y);

        // Legend.
        double lx = plot.Left;
        foreach (var (brush, label, box) in new (Brush, string, bool)[]
                 {
                     (battery, "Battery power (+ charging / − discharging)", false),
                     (input, "Estimated charger output", false),
                     (shade, "Plugged in", true),
                 })
        {
            if (box) dc.DrawRoundedRectangle(brush, new Pen(Res("CardBorder", Brushes.LightGray), 1), new Rect(lx, 6, 14, 12), 3, 3);
            else dc.DrawRoundedRectangle(brush, null, new Rect(lx, 10, 14, 4), 2, 2);
            var ft = Text(label, 12, text, ppd);
            dc.DrawText(ft, new Point(lx + 20, 4));
            lx += 20 + ft.Width + 18;
        }

        if (_points.Count == 0)
        {
            var ft = Text("Collecting data…", 13, muted, ppd);
            dc.DrawText(ft, new Point(plot.Left + (plot.Width - ft.Width) / 2, plot.Top + (plot.Height - ft.Height) / 2));
        }
    }

    private void DrawSeries(DrawingContext dc, Func<HistoryPoint, double?> value, Brush brush,
        Func<DateTimeOffset, double> x, Func<double, double> y)
    {
        var pen = new Pen(brush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var segment = new List<Point>();
        HistoryPoint? previous = null;

        void Flush()
        {
            if (segment.Count >= 2)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(segment[0], false, false);
                    ctx.PolyLineTo(segment.Skip(1).ToList(), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
            else if (segment.Count == 1)
            {
                dc.DrawEllipse(brush, null, segment[0], 2.5, 2.5);
            }
            segment.Clear();
        }

        foreach (var p in _points)
        {
            if (previous is { } prev && (p.Time - prev.Time > TimeSpan.FromSeconds(15) || prev.AcOnline != p.AcOnline)) Flush();
            if (value(p) is double v) segment.Add(new Point(x(p.Time), y(v)));
            else Flush();
            previous = p;
        }
        Flush();
    }

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static FormattedText Text(string s, double size, Brush brush, double ppd) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush, ppd);

    private static double NiceStep(double raw)
    {
        foreach (double candidate in new[] { 1, 2, 5, 10, 20, 25, 50, 100 })
            if (candidate >= raw) return candidate;
        return Math.Ceiling(raw / 100) * 100;
    }
}
