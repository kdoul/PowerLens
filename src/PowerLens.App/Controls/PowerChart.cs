using System.Drawing.Drawing2D;
using PowerLens.Core;

namespace PowerLens.App.Controls;

/// <summary>Live line chart: battery power (+/−) and estimated charger output, with plugged-in periods shaded.</summary>
internal sealed class PowerChart : Control
{
    private static readonly Color BatteryColor = Color.FromArgb(22, 163, 74);
    private static readonly Color InputColor = Color.FromArgb(37, 99, 235);
    private static readonly Color AcShade = Color.FromArgb(235, 243, 255);
    private static readonly Color GridColor = Color.FromArgb(229, 231, 235);
    private static readonly Color AxisText = Color.FromArgb(107, 114, 128);

    private IReadOnlyList<HistoryPoint> _points = [];

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);

    public PowerChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
    }

    public void SetData(IReadOnlyList<HistoryPoint> points)
    {
        _points = points;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        float s = DeviceDpi / 96f;
        using var font = new Font(Font.FontFamily, 8.25f);
        var plot = new RectangleF(52 * s, 30 * s, Width - 64 * s, Height - 56 * s);
        if (plot.Width < 50 || plot.Height < 30) return;

        DateTimeOffset end = _points.Count > 0 ? _points[^1].Time : DateTimeOffset.Now;
        DateTimeOffset start = end - Window;
        float X(DateTimeOffset t) => plot.Left + (float)((t - start) / Window) * plot.Width;

        // Y range: always include 0 and at least ±10 W.
        var values = _points.SelectMany(p => new[] { p.BatteryWatts, p.InputWatts }).OfType<double>().ToList();
        double min = Math.Min(0, values.DefaultIfEmpty(0).Min());
        double max = Math.Max(10, values.DefaultIfEmpty(0).Max());
        double step = NiceStep((max - min) / 5);
        min = Math.Floor(min / step) * step;
        max = Math.Ceiling(max / step) * step;
        float Y(double w) => plot.Bottom - (float)((w - min) / (max - min)) * plot.Height;

        // Plugged-in shading.
        using (var shade = new SolidBrush(AcShade))
        {
            for (int i = 0; i < _points.Count - 1; i++)
            {
                if (!_points[i].AcOnline) continue;
                float x0 = Math.Max(plot.Left, X(_points[i].Time));
                float x1 = Math.Min(plot.Right, X(_points[i + 1].Time));
                if (x1 > x0) g.FillRectangle(shade, x0, plot.Top, x1 - x0, plot.Height);
            }
        }

        // Grid and Y labels.
        using (var grid = new Pen(GridColor))
        using (var zero = new Pen(Color.FromArgb(156, 163, 175), 1.2f * s))
        using (var text = new SolidBrush(AxisText))
        {
            var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            for (double v = min; v <= max + step / 2; v += step)
            {
                float y = Y(v);
                g.DrawLine(Math.Abs(v) < 1e-9 ? zero : grid, plot.Left, y, plot.Right, y);
                g.DrawString($"{v:0} W", font, text, new RectangleF(0, y - 10 * s, plot.Left - 6 * s, 20 * s), right);
            }

            var center = new StringFormat { Alignment = StringAlignment.Center };
            int minutes = (int)Window.TotalMinutes;
            for (int m = 0; m <= minutes; m += Math.Max(1, minutes / 5))
            {
                float x = X(end - TimeSpan.FromMinutes(minutes - m));
                string label = m == minutes ? "now" : $"−{minutes - m} min";
                g.DrawString(label, font, text, new RectangleF(x - 40 * s, plot.Bottom + 4 * s, 80 * s, 18 * s), center);
            }
        }

        DrawSeries(g, p => p.BatteryWatts, BatteryColor, X, Y, s);
        DrawSeries(g, p => p.InputWatts, InputColor, X, Y, s);
        DrawLegend(g, font, s);

        if (_points.Count == 0)
        {
            using var text = new SolidBrush(AxisText);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("Collecting data…", Font, text, plot, center);
        }
    }

    private void DrawSeries(Graphics g, Func<HistoryPoint, double?> value, Color color,
        Func<DateTimeOffset, float> x, Func<double, float> y, float s)
    {
        using var pen = new Pen(color, 2f * s) { LineJoin = LineJoin.Round };
        var segment = new List<PointF>();
        HistoryPoint? previous = null;

        void Flush()
        {
            if (segment.Count >= 2) g.DrawLines(pen, segment.ToArray());
            else if (segment.Count == 1)
            {
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, segment[0].X - 2 * s, segment[0].Y - 2 * s, 4 * s, 4 * s);
            }
            segment.Clear();
        }

        foreach (var p in _points)
        {
            bool gap = previous is { } prev && (p.Time - prev.Time > TimeSpan.FromSeconds(15) || prev.AcOnline != p.AcOnline);
            if (gap) Flush();
            if (value(p) is double v) segment.Add(new PointF(x(p.Time), y(v)));
            else Flush();
            previous = p;
        }
        Flush();
    }

    private void DrawLegend(Graphics g, Font font, float s)
    {
        (Color color, string label, bool box)[] items =
        [
            (BatteryColor, "Battery power (+ charging / − discharging)", false),
            (InputColor, "Estimated charger output", false),
            (AcShade, "Plugged in", true),
        ];
        float x = 52 * s;
        using var text = new SolidBrush(Color.FromArgb(55, 65, 81));
        foreach (var (color, label, box) in items)
        {
            using var brush = new SolidBrush(color);
            if (box)
            {
                g.FillRectangle(brush, x, 9 * s, 14 * s, 12 * s);
                g.DrawRectangle(Pens.LightGray, x, 9 * s, 14 * s, 12 * s);
            }
            else
            {
                g.FillRectangle(brush, x, 13 * s, 14 * s, 3 * s);
            }
            x += 18 * s;
            g.DrawString(label, font, text, x, 7 * s);
            x += g.MeasureString(label, font).Width + 14 * s;
        }
    }

    private static double NiceStep(double raw)
    {
        foreach (double candidate in new[] { 1, 2, 5, 10, 20, 25, 50, 100 })
            if (candidate >= raw) return candidate;
        return Math.Ceiling(raw / 100) * 100;
    }
}
