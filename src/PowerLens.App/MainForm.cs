using Microsoft.Win32;
using PowerLens.App.Controls;
using PowerLens.Core;

namespace PowerLens.App;

internal sealed class MainForm : Form
{
    private static readonly Color PageBack = Color.FromArgb(243, 244, 246);
    private static readonly Color CardBack = Color.White;
    private static readonly Color Muted = Color.FromArgb(107, 114, 128);
    private static readonly Color Ink = Color.FromArgb(17, 24, 39);

    private readonly PowerMonitor _monitor = new();
    private readonly AdapterTest _test = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private PowerSnapshot? _last;
    private bool _refreshing;

    // Header cards.
    private readonly Label _adapterTitle = MakeLabel("POWER ADAPTER", 8.5f, FontStyle.Bold, Muted);
    private readonly Label _adapterWatts = MakeLabel("—", 30f, FontStyle.Bold, Ink);
    private readonly Label _adapterCaption = MakeLabel("Reading…", 10f, FontStyle.Regular, Ink);
    private readonly Label _adapterDetail = MakeLabel("", 9f, FontStyle.Regular, Muted);
    private readonly Label _statusPill = MakeLabel("", 9f, FontStyle.Bold, Color.White);
    private readonly Label _batteryTitle = MakeLabel("BATTERY", 8.5f, FontStyle.Bold, Muted);
    private readonly Label _batteryPercent = MakeLabel("—", 30f, FontStyle.Bold, Ink);
    private readonly Label _batteryCaption = MakeLabel("", 10f, FontStyle.Regular, Ink);
    private readonly Label _batteryDetail = MakeLabel("", 9f, FontStyle.Regular, Muted);

    private readonly PowerChart _chart = new() { Dock = DockStyle.Fill };
    private readonly KeyValueList _chargerList = new() { Dock = DockStyle.Fill };
    private readonly KeyValueList _batteryList = new() { Dock = DockStyle.Fill };
    private readonly KeyValueList _rawList = new() { Dock = DockStyle.Fill };

    // Adapter test tab.
    private readonly Label _testInstruction = MakeLabel("", 11f, FontStyle.Regular, Ink);
    private readonly ProgressBar _testProgress = new() { Maximum = 1000, Width = 420, Height = 14 };
    private readonly Button _testStart = new() { Text = "Start test", AutoSize = true };
    private readonly Button _testCancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Label _testResult = MakeLabel("", 10f, FontStyle.Regular, Ink);

    public MainForm()
    {
        Text = "PowerLens — battery & charger info";
        MinimumSize = new Size(820, 640);
        Size = new Size(1040, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PageBack;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* Default icon. */ }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(WrapCard(_chart), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        Controls.Add(root);

        _timer.Tick += async (_, _) => await RefreshAsync();
        _testStart.Click += (_, _) => StartTest();
        _testCancel.Click += (_, _) => { _test.Cancel(); RenderTest(); SetPollingInterval(); };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshAsync();
        _timer.Start();

        // WMI is slow; load it in the background and refresh when it arrives.
        try { await Task.Run(_monitor.LoadSlowSources); } catch (Exception ex) { _adapterDetail.Text = $"WMI unavailable: {ex.Message}"; }
        await RefreshAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange && IsHandleCreated)
            BeginInvoke(async () => await RefreshAsync());
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
            _adapterCaption.Text = "Could not read power information";
            _adapterDetail.Text = ex.Message;
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
        _chart.SetData(_monitor.History.Snapshot());
        _chargerList.SetRows(ReportFormatter.ChargerRows(s));
        _batteryList.SetRows(ReportFormatter.BatteryRows(s));
        _rawList.SetRows(ReportFormatter.RawRows(s));
        RenderTest();
    }

    private void RenderAdapter(AdapterAssessment a)
    {
        SetPill(a.Verdict);

        if (!a.AcOnline)
        {
            _adapterWatts.Text = "—";
            _adapterCaption.Text = "Not plugged in";
            _adapterDetail.Text = a.SystemDrawWatts is double d
                ? $"System is using {d:0.0} W from the battery. Plug in your USB-C charger to measure it."
                : "Plug in your USB-C charger to measure it.";
            return;
        }

        if (a.Reported is { } r)
        {
            _adapterWatts.Text = ReportFormatter.Watts(r.Watts, "0.#");
            _adapterCaption.Text = UsbPd.Closest(r.Watts) is { } cls ? $"{cls.Label} charger — {cls.Description}" : "Charger rating reported by Windows";
            _adapterDetail.Text = $"{r.Detail}  ·  source: {r.Source}" +
                (a.EstimatedInputWatts is double now ? $"  ·  using ~{now:0.0} W now" : "");
            return;
        }

        if (a.EstimatedInputWatts is double input)
        {
            _adapterWatts.Text = $"{input:0.0} W";
            _adapterCaption.Text = a.LikelyChargerClass is { } c
                ? $"Being delivered now — consistent with a {c.Label} charger or larger"
                : "Being delivered by the charger now (estimated)";
            _adapterDetail.Text =
                (a.InputIsLowerBound ? "Charge power only (no system-load baseline yet) — lower bound.  " : "") +
                $"Peak since plugged in: {ReportFormatter.Watts(a.PeakInputWatts)}  ·  Confidence: {a.Confidence}.  " +
                "Run the Adapter test for a better measurement.";
            return;
        }

        _adapterWatts.Text = "?";
        _adapterCaption.Text = "Plugged in — wattage not measurable right now";
        _adapterDetail.Text = a.Notes.FirstOrDefault() ?? "";
    }

    private void RenderBattery(PowerSnapshot s)
    {
        var sample = s.Sample;
        _batteryPercent.Text = sample.ChargePercent is double p ? $"{p:0} %" : "—";

        _batteryCaption.Text = sample.BatteryRateWatts switch
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
        _batteryDetail.Text = string.Join("  ·  ", parts);
    }

    private void SetPill(ChargerVerdict verdict)
    {
        _statusPill.Text = "  " + ReportFormatter.VerdictText(verdict) + "  ";
        _statusPill.BackColor = verdict switch
        {
            ChargerVerdict.Charging or ChargerVerdict.Full => Color.FromArgb(22, 163, 74),
            ChargerVerdict.SlowCharging or ChargerVerdict.PluggedInNotCharging => Color.FromArgb(217, 119, 6),
            ChargerVerdict.InsufficientPower => Color.FromArgb(220, 38, 38),
            ChargerVerdict.OnBattery => Color.FromArgb(75, 85, 99),
            _ => Color.FromArgb(156, 163, 175),
        };
    }

    // ---- Adapter test ----------------------------------------------------------------------------

    private void StartTest()
    {
        _test.Start();
        if (_last is { } s) _test.Feed(s.Sample);
        SetPollingInterval();
        RenderTest();
    }

    private void SetPollingInterval() => _timer.Interval = _test.IsRunning ? 1000 : 2000;

    private void RenderTest()
    {
        _testInstruction.Text = _test.Instruction;
        _testProgress.Value = (int)(_test.Progress * 1000);
        _testStart.Enabled = !_test.IsRunning;
        _testStart.Text = _test.Stage is AdapterTestStage.Completed or AdapterTestStage.Failed ? "Run again" : "Start test";
        _testCancel.Enabled = _test.IsRunning;
        if (!_test.IsRunning) SetPollingInterval();

        _testResult.Text = _test.Result is { } r
            ? $"Charger output ≈ {r.InputWatts:0.0} W" +
              (r.LikelyChargerClass is { } c ? $"   →   consistent with a {c.Label} charger or larger ({c.Description})" : "") +
              $"\n\nSystem load on battery: {r.SystemDrawWatts:0.0} W     Battery charge power: {r.BatteryRateWatts:+0.0;-0.0} W     " +
              $"Efficiency assumed: {AdapterEstimator.ConversionEfficiency:P0}\n\n" +
              string.Join("\n", r.Notes.Select(n => "• " + n))
            : "";
    }

    // ---- Layout ----------------------------------------------------------------------------------

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _statusPill.Margin = new Padding(0, 6, 0, 0);
        var adapter = Stack(_adapterTitle, _adapterWatts, _adapterCaption, _adapterDetail, _statusPill);
        var battery = Stack(_batteryTitle, _batteryPercent, _batteryCaption, _batteryDetail);

        var adapterCard = WrapCard(adapter);
        adapterCard.Margin = new Padding(0, 0, 10, 0);
        header.Controls.Add(adapterCard, 0, 0);
        header.Controls.Add(WrapCard(battery), 1, 0);
        return header;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5), Margin = new Padding(0, 10, 0, 0) };
        tabs.TabPages.Add(MakeTab("Charger", _chargerList));
        tabs.TabPages.Add(MakeTab("Battery", _batteryList));
        tabs.TabPages.Add(MakeTab("Adapter test", BuildTestPanel()));
        tabs.TabPages.Add(MakeTab("Raw data", BuildRawPanel()));
        return tabs;
    }

    private Control BuildTestPanel()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 12) };
        buttons.Controls.AddRange([_testStart, _testCancel]);

        _testInstruction.MaximumSize = new Size(760, 0);
        _testResult.MaximumSize = new Size(760, 0);
        var stack = Stack(_testInstruction, buttons, _testProgress, _testResult);
        stack.Padding = new Padding(16);
        stack.AutoScroll = true;
        stack.Dock = DockStyle.Fill;
        _testResult.Margin = new Padding(0, 16, 0, 0);
        return stack;
    }

    private Control BuildRawPanel()
    {
        var copy = new Button { Text = "Copy report", AutoSize = true };
        var save = new Button { Text = "Save report…", AutoSize = true };
        var rescan = new Button { Text = "Rescan WMI", AutoSize = true };
        copy.Click += (_, _) => { if (_last is { } s) Clipboard.SetText(ReportFormatter.ToText(s)); };
        save.Click += (_, _) => SaveReport();
        rescan.Click += async (_, _) =>
        {
            rescan.Enabled = false;
            try { await Task.Run(_monitor.LoadSlowSources); await RefreshAsync(); }
            finally { rescan.Enabled = true; }
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        bar.Controls.AddRange([copy, save, rescan]);

        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(_rawList);
        panel.Controls.Add(bar);
        return panel;
    }

    private void SaveReport()
    {
        if (_last is not { } s) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"PowerLens-{s.Time:yyyyMMdd-HHmmss}.txt",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, ReportFormatter.ToText(s));
    }

    private static TabPage MakeTab(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = CardBack, Padding = new Padding(2) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Panel WrapCard(Control content)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBack,
            Padding = new Padding(16, 12, 16, 12),
            AutoSize = content is FlowLayoutPanel,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        content.Dock = content is FlowLayoutPanel ? DockStyle.Top : DockStyle.Fill;
        card.Controls.Add(content);
        return card;
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        BackColor = Color.Transparent,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 2, 0, 2),
    };
}
