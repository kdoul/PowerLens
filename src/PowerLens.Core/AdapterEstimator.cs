namespace PowerLens.Core;

public enum ChargerVerdict
{
    Unknown,
    OnBattery,
    Charging,
    SlowCharging,
    InsufficientPower,
    PluggedInNotCharging,
    Full,
}

public enum EstimateConfidence
{
    None,
    Low,
    Medium,
    High,
}

public sealed record AdapterAssessment
{
    public static readonly AdapterAssessment Empty = new() { Notes = ["Waiting for the first battery reading…"] };

    public ChargerVerdict Verdict { get; init; } = ChargerVerdict.Unknown;
    public bool AcOnline { get; init; }
    public double? ChargePercent { get; init; }

    /// <summary>Net battery power, W (+ charging, − discharging), smoothed over a few seconds.</summary>
    public double? BatteryRateWatts { get; init; }

    /// <summary>System load measured while on battery; used as the baseline when plugged in.</summary>
    public double? SystemDrawWatts { get; init; }

    /// <summary>Estimated power flowing in from the charger right now.</summary>
    public double? EstimatedInputWatts { get; init; }

    /// <summary>True when the input estimate lacks a system-load baseline (so it is charge power only).</summary>
    public bool InputIsLowerBound { get; init; }

    /// <summary>Highest input estimate seen since the charger was connected.</summary>
    public double? PeakInputWatts { get; init; }

    /// <summary>Smallest standard charger rating consistent with the peak.</summary>
    public ChargerClass? LikelyChargerClass { get; init; }

    /// <summary>A rating the driver/firmware reports directly (most trustworthy when present).</summary>
    public ReportedCharger? Reported { get; init; }

    public EstimateConfidence Confidence { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Estimates how much power the USB-C charger is delivering from what Windows does expose:
/// the battery's charge/discharge rate. While unplugged, the discharge rate is the system load.
/// While plugged in, the charger supplies system load + charge power (+ conversion losses).
/// </summary>
public sealed class AdapterEstimator
{
    /// <summary>Typical efficiency of the laptop's input/charging circuitry.</summary>
    public const double ConversionEfficiency = 0.90;

    /// <summary>Charge rates are this close to zero count as "not charging".</summary>
    public const double IdleThresholdWatts = 0.3;

    private static readonly TimeSpan SmoothingWindow = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan BaselineMaxAge = TimeSpan.FromMinutes(30);

    private readonly Queue<PowerSample> _recent = new();
    private double? _baselineWatts;
    private DateTimeOffset _baselineTime;
    private double? _peakInputWatts;

    public PowerSample? Last { get; private set; }

    public void Reset()
    {
        _recent.Clear();
        _baselineWatts = null;
        _peakInputWatts = null;
        Last = null;
    }

    public void Add(PowerSample sample)
    {
        if (Last is { } previous && previous.AcOnline != sample.AcOnline)
        {
            _recent.Clear(); // Never average across a plug/unplug.
            if (sample.AcOnline) _peakInputWatts = null; // Possibly a different charger now.
        }
        Last = sample;

        _recent.Enqueue(sample);
        while (_recent.Count > 0 && sample.Time - _recent.Peek().Time > SmoothingWindow)
            _recent.Dequeue();

        if (!sample.AcOnline && sample.BatteryRateMw is int rate && rate < 0)
        {
            double draw = -rate / 1000.0;
            bool fresh = _baselineWatts is not null && sample.Time - _baselineTime < BaselineMaxAge;
            _baselineWatts = fresh ? _baselineWatts * 0.8 + draw * 0.2 : draw;
            _baselineTime = sample.Time;
        }

        if (sample.AcOnline && EstimateInput(sample.Time, SmoothedRateWatts()) is { Watts: var input } && input > (_peakInputWatts ?? 0))
            _peakInputWatts = input;
    }

    public AdapterAssessment Assess(ReportedCharger? reported = null)
    {
        if (Last is not PowerSample s) return AdapterAssessment.Empty with { Reported = reported };

        var notes = new List<string>();
        double? rate = SmoothedRateWatts();
        double? baseline = FreshBaseline(s.Time);

        if (!s.AcOnline)
        {
            notes.Add("Running on battery. Plug in your charger to measure it — the current system load is remembered and used as the baseline.");
            return new AdapterAssessment
            {
                Verdict = ChargerVerdict.OnBattery,
                AcOnline = false,
                ChargePercent = s.ChargePercent,
                BatteryRateWatts = rate,
                SystemDrawWatts = rate is < 0 ? -rate : baseline,
                Confidence = EstimateConfidence.None,
                Notes = notes,
            };
        }

        var verdict = ChargerVerdict.Unknown;
        var input = EstimateInput(s.Time, rate);

        if (rate is null)
        {
            notes.Add("The battery firmware does not report a charge rate while plugged in, so the charger cannot be estimated from it.");
        }
        else if (rate < -IdleThresholdWatts)
        {
            verdict = ChargerVerdict.InsufficientPower;
            notes.Add($"Plugged in but the battery is still draining by {-rate:0.0} W: the charger delivers less than the laptop is using. " +
                      "This is typical for phone chargers, non-PD USB-C sources, or a cable/port that limits current.");
        }
        else if (rate <= IdleThresholdWatts)
        {
            verdict = s.ChargePercent >= 95 ? ChargerVerdict.Full : ChargerVerdict.PluggedInNotCharging;
            notes.Add(verdict == ChargerVerdict.Full
                ? "Battery is full, so it is not charging; only the system load comes from the charger."
                : "Plugged in but not charging. A charge limit (e.g. stop at 80 %) or battery-care mode may be active.");
        }
        else
        {
            // Above ~80 % laptops taper charging on purpose, so low power there isn't a slow charger.
            verdict = input is { Watts: < 20 } && s.ChargePercent is not >= 80 ? ChargerVerdict.SlowCharging : ChargerVerdict.Charging;
        }

        if (baseline is null && rate is not null)
            notes.Add("No system-load baseline yet: unplug for ~30 s and plug back in (or run the Adapter test) to include system load in the estimate.");
        if (verdict is ChargerVerdict.Charging or ChargerVerdict.SlowCharging && s.ChargePercent >= 80)
            notes.Add("Above ~80 % the laptop tapers charging, so the charger looks weaker than it is. Measure below 80 % for the best estimate.");
        if (input is not null)
            notes.Add("The estimate is what the laptop is pulling right now. Laptops throttle their own intake, so a charger can be rated higher than this — never lower.");

        var confidence = reported is not null ? EstimateConfidence.High
            : input is null ? EstimateConfidence.None
            : baseline is not null && verdict == ChargerVerdict.Charging && s.ChargePercent < 80 ? EstimateConfidence.Medium
            : EstimateConfidence.Low;

        return new AdapterAssessment
        {
            Verdict = verdict,
            AcOnline = true,
            ChargePercent = s.ChargePercent,
            BatteryRateWatts = rate,
            SystemDrawWatts = baseline,
            EstimatedInputWatts = input?.Watts,
            InputIsLowerBound = input?.LowerBound ?? false,
            PeakInputWatts = _peakInputWatts,
            LikelyChargerClass = _peakInputWatts is double peak ? UsbPd.SmallestAtLeast(peak) : null,
            Reported = reported,
            Confidence = confidence,
            Notes = notes,
        };
    }

    private (double Watts, bool LowerBound)? EstimateInput(DateTimeOffset now, double? rateWatts)
    {
        if (rateWatts is not double rate) return null;
        double? baseline = FreshBaseline(now);
        if (baseline is double b)
        {
            double total = (b + rate) / ConversionEfficiency;
            return total > 0 ? (total, false) : null;
        }
        return rate > IdleThresholdWatts ? (rate / ConversionEfficiency, true) : null;
    }

    private double? SmoothedRateWatts()
    {
        var known = _recent.Where(x => x.BatteryRateMw is not null).Select(x => x.BatteryRateMw!.Value).ToList();
        return known.Count == 0 ? null : known.Average() / 1000.0;
    }

    private double? FreshBaseline(DateTimeOffset now) =>
        _baselineWatts is double b && now - _baselineTime < BaselineMaxAge ? b : null;
}
