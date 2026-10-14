using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerLens.App.Interop;
using PowerLens.Core;

namespace PowerLens.App;

public partial class MainWindow : Window
{
    private readonly PowerMonitor _monitor = new();
    private readonly AdapterTest _test = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly RowsModel _charger = new();
    private readonly RowsModel _battery = new();
    private readonly RowsModel _raw = new();
    private readonly AppSettings _settings;
    private TrayIcon? _tray;
    private PowerSnapshot? _last;
    private bool _refreshing;
    private bool _acrylic;
    private bool _exiting;

    internal MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ChargerRows.ItemsSource = _charger.Groups;
        BatteryRows.ItemsSource = _battery.Groups;
        RawRows.ItemsSource = _raw.Groups;

        _timer.Tick += async (_, _) => await RefreshAsync();
        SourceInitialized += (_, _) => ApplyTheme();
        Closing += OnClosing;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        RenderTest();
        ApplySettings();

        // Monitoring starts right away, even when launched hidden in the tray.
        _ = StartMonitoringAsync();
    }

    /// <summary>Shows the window (from the tray or a second launch) and brings it to the front.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; // Reliable bring-to-front across focus-stealing rules.
        Topmost = false;
        Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _settings.CloseToTray && _tray?.Visible == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        ExitApp();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    // ---- Settings & tray ---------------------------------------------------------------------

    private void ApplySettings()
    {
        if (_settings.ShowTrayIcon && _tray is null)
        {
            _tray = new TrayIcon(_settings);
            _tray.OpenRequested += () => Dispatcher.BeginInvoke(ShowFromTray);
            _tray.ExitRequested += () => Dispatcher.BeginInvoke(ExitApp);
            _tray.SettingsChanged += () => Dispatcher.BeginInvoke(SyncSettingsUi);
            if (_last is { } s) _tray.Update(s);
        }
        if (_tray is not null) _tray.Visible = _settings.ShowTrayIcon;
        SyncSettingsUi();
    }

    private void SyncSettingsUi()
    {
        SettingTray.IsChecked = _settings.ShowTrayIcon;
        SettingTrayWatts.IsChecked = _settings.TrayShowsWatts;
        SettingTrayWatts.IsEnabled = _settings.ShowTrayIcon;
        SettingCloseToTray.IsChecked = _settings.CloseToTray;
        SettingCloseToTray.IsEnabled = _settings.ShowTrayIcon;
        SettingStartup.IsChecked = StartupRegistration.IsEnabled;
        if (_tray is not null && _last is { } s) _tray.Update(s);
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        _settings.ShowTrayIcon = SettingTray.IsChecked == true;
        _settings.TrayShowsWatts = SettingTrayWatts.IsChecked == true;
        _settings.CloseToTray = SettingCloseToTray.IsChecked == true;
        if (ReferenceEquals(sender, SettingStartup))
            StartupRegistration.Set(SettingStartup.IsChecked == true);
        _settings.Save();
        ApplySettings();
    }

    private async Task StartMonitoringAsync()
    {
        await RefreshAsync();
        _timer.Start();

        // WMI is slow; load it in the background and refresh when it arrives.
        try { await Task.Run(_monitor.LoadSlowSources); }
        catch (Exception ex) { AdapterDetail.Text = $"WMI unavailable: {ex.Message}"; }
        await RefreshAsync();
    }

    private void ApplyTheme()
    {
        bool dark = Theme.SystemUsesDarkMode();
        if (!_acrylic) _acrylic = Backdrop.TryApplyAcrylic(this, dark);
        else Backdrop.SetDarkMode(this, dark);
        Theme.Apply(Application.Current.Resources, dark, _acrylic);
        if (!_acrylic) Backdrop.SetDarkMode(this, dark);
        Chart.InvalidateVisual();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
            Dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await Task.Run(_monitor.Capture);
            _last = snapshot;
            if (_test.IsRunning) _test.Feed(snapshot.Sample);
            Render(snapshot);
        }
        catch (Exception ex)
        {
            AdapterCaption.Text = "Could not read power information";
            AdapterDetail.Text = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    // ---- Rendering --------------------------------------------------------------------------

    private void Render(PowerSnapshot s)
    {
        RenderAdapter(s.Adapter);
        RenderBattery(s);
        Chart.SetData(_monitor.History.Snapshot());
        _charger.Update(ReportFormatter.ChargerRows(s));
        _battery.Update(ReportFormatter.BatteryRows(s));
        _raw.Update(ReportFormatter.RawRows(s));
        _tray?.Update(s);
        RenderTest();
    }

    private void RenderAdapter(AdapterAssessment a)
    {
        StatusText.Text = ReportFormatter.VerdictText(a.Verdict);
        StatusPill.Background = Theme.VerdictBrush(a.Verdict);

        if (!a.AcOnline)
        {
            AdapterWatts.Text = "—";
            AdapterCaption.Text = "Not plugged in";
            AdapterDetail.Text = a.SystemDrawWatts is double d
                ? $"System is using {d:0.0} W from the battery. Plug in your USB-C charger to measure it."
                : "Plug in your USB-C charger to measure it.";
            return;
        }

        if (a.Reported is { } r)
        {
            AdapterWatts.Text = ReportFormatter.Watts(r.Watts, "0.#");
            AdapterCaption.Text = UsbPd.Closest(r.Watts) is { } cls ? $"{cls.Label} charger — {cls.Description}" : "Charger rating reported by Windows";
            AdapterDetail.Text = r.Detail +
                (a.EstimatedInputWatts is double now ? $"  ·  using ~{now:0.0} W now" : "");
            return;
        }

        if (a.EstimatedInputWatts is double input)
        {
            AdapterWatts.Text = $"{input:0.0} W";
            AdapterCaption.Text = a.LikelyChargerClass is { } c
                ? $"Being delivered now — consistent with a {c.Label} charger or larger"
                : "Being delivered by the charger now (estimated)";
            AdapterDetail.Text =
                $"Peak {ReportFormatter.Watts(a.PeakInputWatts)}  ·  {a.Confidence} confidence" +
                (a.InputIsLowerBound ? "  ·  charge power only (lower bound)" : "");
            return;
        }

        AdapterWatts.Text = "?";
        AdapterCaption.Text = "Plugged in — wattage not measurable right now";
        AdapterDetail.Text = a.Notes.FirstOrDefault() ?? "";
    }

    private void RenderBattery(PowerSnapshot s)
    {
        var sample = s.Sample;
        BatteryPercent.Text = sample.ChargePercent is double p ? $"{p:0} %" : "—";
        BatteryLevel.Value = sample.ChargePercent ?? 0;

        BatteryCaption.Text = sample.BatteryRateWatts switch
        {
            double w when w > AdapterEstimator.IdleThresholdWatts => $"Charging at {w:0.0} W",
            double w when w < -AdapterEstimator.IdleThresholdWatts => $"Discharging at {-w:0.0} W",
            double => "Idle (not charging or discharging)",
            _ => s.Batteries.Count == 0 ? "No battery found" : "Rate not reported",
        };

        var first = s.Batteries.FirstOrDefault(b => b.Error is null);
        var parts = new List<string>();
        if (first?.HealthPercent is double h) parts.Add($"Health {h:0} %");
        if (first?.CycleCount is uint cycles) parts.Add($"{cycles:N0} cycles");
        if (first?.Info is { } info) parts.Add($"{info.FullChargedCapacity / 1000.0:0.0} of {info.DesignedCapacity / 1000.0:0.0} Wh");
        if (s.System?.EstimatedTime is TimeSpan t && !sample.AcOnline) parts.Add($"{ReportFormatter.Duration(t)} left");
        BatteryDetail.Text = string.Join("  ·  ", parts);
    }

    // ---- Adapter test ------------------------------------------------------------------------

    private void OnStartTest(object sender, RoutedEventArgs e)
    {
        _test.Start();
        if (_last is { } s) _test.Feed(s.Sample);
        RenderTest();
    }

    private void OnCancelTest(object sender, RoutedEventArgs e)
    {
        _test.Cancel();
        RenderTest();
    }

    private void RenderTest()
    {
        _timer.Interval = TimeSpan.FromSeconds(_test.IsRunning ? 1 : 2);
        TestInstruction.Text = _test.Instruction;
        TestProgress.Value = _test.Progress;
        TestStart.IsEnabled = !_test.IsRunning;
        TestStart.Content = _test.Stage is AdapterTestStage.Completed or AdapterTestStage.Failed ? "Run again" : "Start test";
        TestCancel.IsEnabled = _test.IsRunning;

        if (_test.Result is { } r)
        {
            TestHeadline.Text = $"≈ {r.InputWatts:0.0} W" +
                (r.LikelyChargerClass is { } c ? $"  →  {c.Label} charger or larger" : "");
            TestResult.Text =
                (r.LikelyChargerClass is { } c2 ? $"{c2.Description}\n\n" : "") +
                $"System load on battery: {r.SystemDrawWatts:0.0} W    ·    Battery charge power: {r.BatteryRateWatts:+0.0;-0.0} W    ·    " +
                $"Efficiency assumed: {AdapterEstimator.ConversionEfficiency:P0}\n\n" +
                string.Join("\n", r.Notes.Select(n => "• " + n));
        }
        else
        {
            TestHeadline.Text = "";
            TestResult.Text = "";
        }
    }

    // ---- Raw data actions --------------------------------------------------------------------

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        if (_last is { } s) Clipboard.SetText(ReportFormatter.ToText(s));
    }

    private void OnSaveReport(object sender, RoutedEventArgs e)
    {
        if (_last is not { } s) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"PowerLens-{s.Time:yyyyMMdd-HHmmss}.txt",
        };
        if (dialog.ShowDialog(this) == true)
            File.WriteAllText(dialog.FileName, ReportFormatter.ToText(s));
    }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        RescanButton.IsEnabled = false;
        try
        {
            await Task.Run(_monitor.LoadSlowSources);
            await RefreshAsync();
        }
        finally
        {
            RescanButton.IsEnabled = true;
        }
    }
}
