namespace PowerLens.Core;

public sealed record ChargerClass(double Watts, string Description)
{
    public string Label => Watts % 1 == 0 ? $"{Watts:0} W" : $"{Watts:0.0} W";
}

/// <summary>Common USB-C charger power levels, used to turn a measured wattage into a likely charger rating.</summary>
public static class UsbPd
{
    public static readonly IReadOnlyList<ChargerClass> CommonClasses =
    [
        new(7.5, "USB Type-C current, 5 V / 1.5 A (no PD)"),
        new(15, "USB Type-C current, 5 V / 3 A (no PD)"),
        new(18, "USB-PD 9 V / 2 A"),
        new(20, "USB-PD 9 V / 2.22 A"),
        new(25, "USB-PD 9 V / 2.78 A (phone fast charger)"),
        new(30, "USB-PD 15 V / 2 A or 20 V / 1.5 A"),
        new(35, "USB-PD 20 V / 1.75 A"),
        new(45, "USB-PD 15 V / 3 A or 20 V / 2.25 A"),
        new(60, "USB-PD 20 V / 3 A"),
        new(65, "USB-PD 20 V / 3.25 A"),
        new(70, "USB-PD 20 V / 3.5 A"),
        new(90, "USB-PD 20 V / 4.5 A (needs a 5 A cable)"),
        new(100, "USB-PD 20 V / 5 A (needs a 5 A e-marked cable)"),
        new(140, "USB-PD 3.1 EPR 28 V / 5 A"),
        new(180, "USB-PD 3.1 EPR 36 V / 5 A"),
        new(240, "USB-PD 3.1 EPR 48 V / 5 A"),
    ];

    /// <summary>
    /// The smallest common charger rating that can deliver <paramref name="watts"/>.
    /// A 2 % tolerance absorbs measurement noise so 65.5 W still reads as a 65 W charger.
    /// </summary>
    public static ChargerClass? SmallestAtLeast(double watts)
    {
        if (watts <= 0) return null;
        return CommonClasses.FirstOrDefault(c => c.Watts >= watts * 0.98);
    }

    /// <summary>The class matching a reported contract exactly (±3 %), if it is a standard one.</summary>
    public static ChargerClass? Closest(double watts) =>
        CommonClasses.MinBy(c => Math.Abs(c.Watts - watts)) is { } c && Math.Abs(c.Watts - watts) <= c.Watts * 0.03 ? c : null;
}
