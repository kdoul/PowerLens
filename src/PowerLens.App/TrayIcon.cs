using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PowerLens.Core;
using Forms = System.Windows.Forms;

namespace PowerLens.App;

/// <summary>
/// Notification-area icon. Optionally draws the live battery wattage into the icon
/// (green = charging, slate = discharging, red = charger too weak), with a menu for quick settings.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _appIcon;
    private readonly AppSettings _settings;
    private readonly Forms.ToolStripMenuItem _statusRate = new() { Enabled = false };
    private readonly Forms.ToolStripMenuItem _statusBattery = new() { Enabled = false };
    private readonly Forms.ToolStripMenuItem _statusCharger = new() { Enabled = false };
    private readonly Forms.ToolStripMenuItem _showWatts;
    private readonly Forms.ToolStripMenuItem _closeToTray;
    private readonly Forms.ToolStripMenuItem _startWithWindows;
    private IntPtr _drawnIcon;
    private string? _drawnKey;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? SettingsChanged;
    public event Action? UpdateRequested;
    public event Action? CheckForUpdatesRequested;

    private readonly Forms.ToolStripMenuItem _update = new() { Visible = false };
    private string? _announcedVersion;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayIcon(AppSettings settings)
    {
        _settings = settings;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PowerLens.ico"))!.Stream)
            _appIcon = new Icon(stream, Forms.SystemInformation.SmallIconSize);

        _showWatts = new Forms.ToolStripMenuItem("Show watts in tray icon") { CheckOnClick = true };
        _showWatts.CheckedChanged += (_, _) => { _settings.TrayShowsWatts = _showWatts.Checked; Changed(); };
        _closeToTray = new Forms.ToolStripMenuItem("Close button hides to tray") { CheckOnClick = true };
        _closeToTray.CheckedChanged += (_, _) => { _settings.CloseToTray = _closeToTray.Checked; Changed(); };
        _startWithWindows = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startWithWindows.CheckedChanged += (_, _) => { StartupRegistration.Set(_startWithWindows.Checked); SettingsChanged?.Invoke(); };

        var open = new Forms.ToolStripMenuItem("Open PowerLens") { Font = new Font(Forms.SystemInformation.MenuFont, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        _update.Click += (_, _) => UpdateRequested?.Invoke();
        var checkUpdates = new Forms.ToolStripMenuItem("Check for updates");
        checkUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke();

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([
            open,
            _update,
            new Forms.ToolStripSeparator(),
            _statusRate, _statusBattery, _statusCharger,
            new Forms.ToolStripSeparator(),
            _showWatts, _closeToTray, _startWithWindows, checkUpdates,
            new Forms.ToolStripSeparator(),
            exit,
        ]);
        menu.Opening += (_, _) => SyncChecks();

        _icon = new Forms.NotifyIcon { Icon = _appIcon, Text = "PowerLens", ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        SyncChecks();
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    /// <summary>Shows the "Update to …" menu item and, once per version, a notification.</summary>
    public void ShowUpdate(ReleaseInfo? release)
    {
        _update.Visible = release is not null;
        if (release is null) return;
        string version = UpdateChecker.Display(release.Version);
        _update.Text = $"Update to {version}…";
        if (_announcedVersion == version || !_icon.Visible) return;
        _announcedVersion = version;
        _icon.ShowBalloonTip(8000, "PowerLens update available",
            $"Version {version} is ready. Open PowerLens to install it.", Forms.ToolTipIcon.Info);
    }

    public void Update(PowerSnapshot s)
    {
        var a = s.Adapter;
        double? rate = s.Sample.BatteryRateWatts;

        _statusRate.Text = rate switch
        {
            double w when w > AdapterEstimator.IdleThresholdWatts => $"Charging at {w:0.0} W",
            double w when w < -AdapterEstimator.IdleThresholdWatts => $"Discharging at {-w:0.0} W",
            double => "Battery idle",
            _ => "Battery rate not reported",
        };
        _statusBattery.Text = s.Sample.ChargePercent is double p ? $"Battery {p:0} %" : "Battery —";
        _statusCharger.Text = ChargerLine(a);

        string tip = $"PowerLens · {_statusRate.Text} · {_statusBattery.Text}";
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;

        if (_settings.TrayShowsWatts && rate is double r) SetWattsIcon(r, a.Verdict);
        else SetAppIcon();
    }

    private static string ChargerLine(AdapterAssessment a)
    {
        if (!a.AcOnline) return "Charger not connected";
        if (a.Reported is { } r) return $"Charger {ReportFormatter.Watts(r.Watts, "0.#")} (reported)";
        if (a.EstimatedInputWatts is double w)
            return $"Charger delivering ~{w:0} W" + (a.LikelyChargerClass is { } c ? $" ({c.Label}+ class)" : "");
        return $"Charger: {ReportFormatter.VerdictText(a.Verdict).ToLowerInvariant()}";
    }

    private void SetAppIcon()
    {
        if (_drawnKey is null) return;
        _icon.Icon = _appIcon;
        ReleaseDrawn();
    }

    private void SetWattsIcon(double watts, ChargerVerdict verdict)
    {
        int value = (int)Math.Round(Math.Abs(watts));
        string text = value > 999 ? "999" : value.ToString();
        Color back = verdict switch
        {
            ChargerVerdict.InsufficientPower => Color.FromArgb(220, 38, 38),
            _ when watts > AdapterEstimator.IdleThresholdWatts => Color.FromArgb(22, 163, 74),
            _ when watts < -AdapterEstimator.IdleThresholdWatts => Color.FromArgb(71, 85, 105),
            _ => Color.FromArgb(37, 99, 235),
        };

        string key = $"{text}|{back.ToArgb()}";
        if (key == _drawnKey) return;

        var size = Forms.SystemInformation.SmallIconSize;
        using var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float radius = size.Width / 4f;
            using (var path = RoundedRect(new RectangleF(0, 0, size.Width, size.Height), radius))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);

            // Largest bold font that fits the icon.
            float em = size.Height * 0.82f;
            Font font;
            while (true)
            {
                font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
                var measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                if (measured.Width <= size.Width - 1 || em <= 6) break;
                font.Dispose();
                em -= 0.5f;
            }
            using (font)
            {
                var format = new StringFormat(StringFormat.GenericTypographic)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString(text, font, Brushes.White, new RectangleF(0, 0.5f, size.Width, size.Height), format);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        _icon.Icon = Icon.FromHandle(handle);
        ReleaseDrawn();
        _drawnIcon = handle;
        _drawnKey = key;
    }

    private void ReleaseDrawn()
    {
        if (_drawnIcon != IntPtr.Zero) DestroyIcon(_drawnIcon);
        _drawnIcon = IntPtr.Zero;
        if (_icon.Icon == _appIcon) _drawnKey = null;
    }

    private void SyncChecks()
    {
        _showWatts.Checked = _settings.TrayShowsWatts;
        _closeToTray.Checked = _settings.CloseToTray;
        _startWithWindows.Checked = StartupRegistration.IsEnabled;
    }

    private void Changed()
    {
        _settings.Save();
        _drawnKey = null; // Force a redraw on the next update.
        SettingsChanged?.Invoke();
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_drawnIcon != IntPtr.Zero) DestroyIcon(_drawnIcon);
        _appIcon.Dispose();
    }
}
