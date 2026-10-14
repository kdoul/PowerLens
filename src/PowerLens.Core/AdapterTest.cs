namespace PowerLens.Core;

public enum AdapterTestStage
{
    NotStarted,
    WaitingForUnplug,
    MeasuringSystemDraw,
    WaitingForPlugIn,
    Settling,
    MeasuringCharge,
    Completed,
    Failed,
}

public sealed record AdapterTestResult(
    double SystemDrawWatts,
    double BatteryRateWatts,
    double InputWatts,
    ChargerClass? LikelyChargerClass,
    IReadOnlyList<string> Notes);

/// <summary>
/// Guided measurement: unplug → measure system load on battery → plug in → measure charge power.
/// Charger output ≈ (system load + charge power) / conversion efficiency.
/// Feed it a sample every second or so.
/// </summary>
public sealed class AdapterTest
{
    public TimeSpan DrawSettle { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan DrawWindow { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan ChargeWindow { get; init; } = TimeSpan.FromSeconds(25);

    private readonly List<double> _draw = [];
    private readonly List<double> _charge = [];
    private DateTimeOffset _stageStart;
    private DateTimeOffset _now;
    private double? _percentAtCharge;

    public AdapterTestStage Stage { get; private set; } = AdapterTestStage.NotStarted;
    public AdapterTestResult? Result { get; private set; }
    public string? FailureReason { get; private set; }

    public bool IsRunning => Stage is not (AdapterTestStage.NotStarted or AdapterTestStage.Completed or AdapterTestStage.Failed);

    public void Start()
    {
        _draw.Clear();
        _charge.Clear();
        _percentAtCharge = null;
        Result = null;
        FailureReason = null;
        Stage = AdapterTestStage.WaitingForUnplug;
    }

    public void Cancel()
    {
        if (IsRunning) Fail("Cancelled.");
    }

    public void Feed(PowerSample s)
    {
        _now = s.Time;
        TimeSpan elapsed = s.Time - _stageStart;

        switch (Stage)
        {
            case AdapterTestStage.WaitingForUnplug:
                if (!s.AcOnline) Enter(AdapterTestStage.MeasuringSystemDraw, s.Time);
                break;

            case AdapterTestStage.MeasuringSystemDraw:
                if (s.AcOnline)
                {
                    // Plugged back in early: keep going if we already have a usable reading.
                    if (_draw.Count >= 3) Enter(AdapterTestStage.Settling, s.Time);
                    else { _draw.Clear(); Enter(AdapterTestStage.WaitingForUnplug, s.Time); }
                    break;
                }
                if (elapsed >= DrawSettle && s.BatteryRateMw is int r && r < 0)
                    _draw.Add(-r / 1000.0);
                if (elapsed >= DrawWindow)
                {
                    if (_draw.Count == 0) Fail("The battery did not report a discharge rate while unplugged.");
                    else Enter(AdapterTestStage.WaitingForPlugIn, s.Time);
                }
                break;

            case AdapterTestStage.WaitingForPlugIn:
                if (s.AcOnline) Enter(AdapterTestStage.Settling, s.Time);
                break;

            case AdapterTestStage.Settling:
                if (!s.AcOnline) Enter(AdapterTestStage.WaitingForPlugIn, s.Time);
                else if (elapsed >= SettleTime) Enter(AdapterTestStage.MeasuringCharge, s.Time);
                break;

            case AdapterTestStage.MeasuringCharge:
                if (!s.AcOnline)
                {
                    Fail("The charger was unplugged during the measurement.");
                    break;
                }
                _percentAtCharge ??= s.ChargePercent;
                if (s.BatteryRateMw is int c) _charge.Add(c / 1000.0);
                if (elapsed >= ChargeWindow)
                {
                    if (_charge.Count == 0) Fail("The battery did not report a charge rate while plugged in.");
                    else Complete();
                }
                break;
        }
    }

    /// <summary>0..1 overall progress for a progress bar.</summary>
    public double Progress => Stage switch
    {
        AdapterTestStage.NotStarted or AdapterTestStage.WaitingForUnplug => 0,
        AdapterTestStage.MeasuringSystemDraw => 0.40 * Fraction(DrawWindow),
        AdapterTestStage.WaitingForPlugIn => 0.40,
        AdapterTestStage.Settling => 0.40 + 0.15 * Fraction(SettleTime),
        AdapterTestStage.MeasuringCharge => 0.55 + 0.45 * Fraction(ChargeWindow),
        AdapterTestStage.Completed => 1,
        _ => 0,
    };

    public string Instruction => Stage switch
    {
        AdapterTestStage.NotStarted =>
            "Measures your charger by comparing the system load on battery with the power flowing in when plugged in. " +
            "Works best below 80 % battery with a steady workload (leave the laptop idle during the test).",
        AdapterTestStage.WaitingForUnplug => "Step 1 of 3 — Unplug the charger now.",
        AdapterTestStage.MeasuringSystemDraw => $"Step 1 of 3 — Measuring system load on battery. Keep the charger unplugged… {Remaining(DrawWindow)} s",
        AdapterTestStage.WaitingForPlugIn => "Step 2 of 3 — Plug the USB-C charger back in.",
        AdapterTestStage.Settling => $"Step 3 of 3 — Waiting for charging to settle… {Remaining(SettleTime)} s",
        AdapterTestStage.MeasuringCharge => $"Step 3 of 3 — Measuring charging power. Keep the charger plugged in… {Remaining(ChargeWindow)} s",
        AdapterTestStage.Completed => "Done.",
        AdapterTestStage.Failed => $"Test stopped: {FailureReason}",
        _ => "",
    };

    private void Complete()
    {
        double draw = Median(_draw);
        double charge = Median(_charge);
        double input = (draw + charge) / AdapterEstimator.ConversionEfficiency;
        if (input <= 0)
        {
            Fail("Readings were inconsistent (the system load changed a lot). Try again with the laptop idle.");
            return;
        }

        var notes = new List<string>();
        if (charge <= AdapterEstimator.IdleThresholdWatts)
            notes.Add("The battery did not charge during the test (full, or a charge limit is active), so the result only reflects system load — a lower bound.");
        else if (_percentAtCharge >= 80)
            notes.Add("Battery was above 80 %, where charging tapers. The charger is probably stronger than measured.");
        notes.Add("Laptops limit their own intake, so this is the power actually used. The charger's rating is at least this.");

        Result = new AdapterTestResult(draw, charge, input, UsbPd.SmallestAtLeast(input), notes);
        Stage = AdapterTestStage.Completed;
    }

    private void Enter(AdapterTestStage stage, DateTimeOffset time)
    {
        Stage = stage;
        _stageStart = time;
    }

    private void Fail(string reason)
    {
        FailureReason = reason;
        Stage = AdapterTestStage.Failed;
    }

    private double Fraction(TimeSpan window) =>
        window <= TimeSpan.Zero ? 1 : Math.Clamp((_now - _stageStart) / window, 0, 1);

    private int Remaining(TimeSpan window) =>
        (int)Math.Ceiling(Math.Max(0, (window - (_now - _stageStart)).TotalSeconds));

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
