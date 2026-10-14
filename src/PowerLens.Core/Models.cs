namespace PowerLens.Core;

/// <summary>BATTERY_STATUS.PowerState flags (poclass.h).</summary>
[Flags]
public enum BatteryPowerState : uint
{
    None = 0,
    PowerOnLine = 0x1,
    Discharging = 0x2,
    Charging = 0x4,
    Critical = 0x8,
}

/// <summary>BATTERY_INFORMATION.Capabilities flags (poclass.h).</summary>
[Flags]
public enum BatteryCapabilities : uint
{
    None = 0,
    SetChargeSupported = 0x1,
    SetDischargeSupported = 0x2,
    SetChargingSourceSupported = 0x4,
    SetChargerIdSupported = 0x8,
    IsShortTerm = 0x20000000,
    CapacityRelative = 0x40000000,
    SystemBattery = 0x80000000,
}

/// <summary>BATTERY_QUERY_INFORMATION_LEVEL (poclass.h / batclass.h).</summary>
public enum BatteryQueryInformationLevel
{
    BatteryInformation = 0,
    BatteryGranularityInformation = 1,
    BatteryTemperature = 2,
    BatteryEstimatedTime = 3,
    BatteryDeviceName = 4,
    BatteryManufactureDate = 5,
    BatteryManufactureName = 6,
    BatteryUniqueID = 7,
    BatterySerialNumber = 8,
    // The next three are only implemented by battery miniports that sit on top of the
    // Windows charging stack (UcmCx / charge arbitration). Most ACPI laptops return
    // ERROR_INVALID_FUNCTION, but when they work they expose the negotiated USB contract.
    BatteryChargingSource = 9,
    BatteryChargerId = 10,
    BatteryChargerStatus = 11,
}

/// <summary>BATTERY_CHARGING_SOURCE_TYPE (batclass.h).</summary>
public enum ChargingSourceType
{
    Unknown = 0,
    AC = 1,
    Usb = 2,
    Wireless = 3,
}

/// <summary>USB_CHARGER_PORT (batclass.h).</summary>
public enum UsbChargerPort
{
    Legacy = 0,
    TypeC = 1,
}

/// <summary>Static battery data from BATTERY_INFORMATION.</summary>
public sealed record BatteryStaticInfo(
    BatteryCapabilities Capabilities,
    string Technology,
    string Chemistry,
    uint DesignedCapacity,
    uint FullChargedCapacity,
    uint DefaultAlert1,
    uint DefaultAlert2,
    uint CriticalBias,
    uint CycleCount)
{
    public bool IsRelative => Capabilities.HasFlag(BatteryCapabilities.CapacityRelative);

    /// <summary>"mWh" for absolute batteries, "%" style units for relative ones.</summary>
    public string CapacityUnit => IsRelative ? "units" : "mWh";
}

/// <summary>Live battery data from BATTERY_STATUS, with "unknown" sentinels mapped to null.</summary>
public sealed record BatteryStatusInfo(
    BatteryPowerState PowerState,
    uint? Capacity,
    uint? VoltageMv,
    // mW; positive while charging, negative while discharging.
    int? RateMw);

public sealed record ChargingSourceInfo(ChargingSourceType Type, bool Online);

/// <summary>BATTERY_USB_CHARGER_STATUS: the charger contract as seen by the Windows charging stack.</summary>
public sealed record UsbChargerStatus(
    ChargingSourceType Type,
    uint Flags,
    uint MaxCurrentMa,
    uint VoltageMv,
    UsbChargerPort PortType,
    ulong PortId,
    Guid OemCharger)
{
    public const uint FlagDefaultUsb = 0x1;
    public const uint FlagUcmPd = 0x2;

    public bool IsPowerDelivery => (Flags & FlagUcmPd) != 0;

    public double? Watts => MaxCurrentMa > 0 && VoltageMv > 0 ? MaxCurrentMa * (double)VoltageMv / 1_000_000.0 : null;
}

/// <summary>One battery slot as read through the battery class IOCTLs.</summary>
public sealed record BatteryDevice
{
    public required int Index { get; init; }
    public required string DevicePath { get; init; }
    public uint Tag { get; init; }
    public string? Error { get; init; }

    public BatteryStaticInfo? Info { get; init; }
    public BatteryStatusInfo? Status { get; init; }
    public double? TemperatureC { get; init; }
    public string? DeviceName { get; init; }
    public string? ManufactureName { get; init; }
    public string? SerialNumber { get; init; }
    public string? UniqueId { get; init; }
    public DateOnly? ManufactureDate { get; init; }
    public TimeSpan? EstimatedTime { get; init; }

    public ChargingSourceInfo? ChargingSource { get; init; }
    public Guid? ChargerId { get; init; }
    public UsbChargerStatus? ChargerStatus { get; init; }

    /// <summary>Cycle count from root\WMI BatteryCycleCount, used when the IOCTL reports 0.</summary>
    public uint? WmiCycleCount { get; init; }

    /// <summary>Raw per-IOCTL outcome, for the diagnostics view.</summary>
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();

    public bool IsRelative => Info?.IsRelative == true;

    public double? ChargePercent =>
        Status?.Capacity is uint cap && Info is { FullChargedCapacity: > 0 and not BatteryStructs.UnknownCapacity } info
            ? Math.Clamp(100.0 * cap / info.FullChargedCapacity, 0, 100)
            : null;

    public double? HealthPercent =>
        Info is { DesignedCapacity: > 0 and not BatteryStructs.UnknownCapacity, FullChargedCapacity: > 0 and not BatteryStructs.UnknownCapacity } info
            ? 100.0 * info.FullChargedCapacity / info.DesignedCapacity
            : null;

    public uint? CycleCount => Info is { CycleCount: > 0 } info ? info.CycleCount : WmiCycleCount;
}

/// <summary>SYSTEM_BATTERY_STATE from CallNtPowerInformation — the aggregate across all batteries.</summary>
public sealed record SystemPowerState(
    bool AcOnline,
    bool BatteryPresent,
    bool Charging,
    bool Discharging,
    uint MaxCapacityMwh,
    uint RemainingCapacityMwh,
    // mW; positive while charging, negative while discharging.
    int? RateMw,
    TimeSpan? EstimatedTime);

/// <summary>A WMI class instance from root\WMI battery classes, flattened to strings.</summary>
public sealed record WmiBatteryInfo(string InstanceName, IReadOnlyDictionary<string, string> Properties)
{
    public uint? CycleCount =>
        Properties.TryGetValue("CycleCount", out var v) && uint.TryParse(v, out var n) && n > 0 ? n : null;
}

/// <summary>An OEM/vendor WMI class that looks charger related (e.g. adapter wattage).</summary>
public sealed record OemPowerFinding(
    string Namespace,
    string ClassName,
    string Instance,
    IReadOnlyList<KeyValuePair<string, string>> Properties);

/// <summary>Everything captured in one polling pass.</summary>
public sealed record PowerSnapshot(
    DateTimeOffset Time,
    SystemPowerState? System,
    IReadOnlyList<BatteryDevice> Batteries,
    IReadOnlyList<WmiBatteryInfo> WmiBatteries,
    IReadOnlyList<OemPowerFinding> OemFindings,
    PowerSample Sample,
    AdapterAssessment Adapter);
