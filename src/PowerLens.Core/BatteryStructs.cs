using System.Buffers.Binary;
using System.Text;

namespace PowerLens.Core;

/// <summary>
/// Decodes the raw buffers returned by the battery IOCTLs and CallNtPowerInformation.
/// Kept free of P/Invoke so it can be unit tested on any OS.
/// </summary>
public static class BatteryStructs
{
    public const uint UnknownCapacity = 0xFFFFFFFF;
    public const uint UnknownVoltage = 0xFFFFFFFF;
    public const int UnknownRate = unchecked((int)0x80000000);
    public const uint UnknownTime = 0xFFFFFFFF;

    public const int BatteryInformationSize = 36;
    public const int BatteryStatusSize = 16;
    public const int SystemBatteryStateSize = 32;

    public static BatteryStaticInfo ParseBatteryInformation(ReadOnlySpan<byte> b)
    {
        if (b.Length < BatteryInformationSize)
            throw new ArgumentException($"BATTERY_INFORMATION needs {BatteryInformationSize} bytes, got {b.Length}.", nameof(b));

        byte technology = b[4];
        return new BatteryStaticInfo(
            Capabilities: (BatteryCapabilities)U32(b, 0),
            Technology: technology switch
            {
                0 => "Primary (non-rechargeable)",
                1 => "Rechargeable",
                _ => $"Unknown ({technology})",
            },
            Chemistry: DescribeChemistry(b.Slice(8, 4)),
            DesignedCapacity: U32(b, 12),
            FullChargedCapacity: U32(b, 16),
            DefaultAlert1: U32(b, 20),
            DefaultAlert2: U32(b, 24),
            CriticalBias: U32(b, 28),
            CycleCount: U32(b, 32));
    }

    public static BatteryStatusInfo ParseBatteryStatus(ReadOnlySpan<byte> b)
    {
        if (b.Length < BatteryStatusSize)
            throw new ArgumentException($"BATTERY_STATUS needs {BatteryStatusSize} bytes, got {b.Length}.", nameof(b));

        var state = (BatteryPowerState)U32(b, 0);
        uint capacity = U32(b, 4);
        uint voltage = U32(b, 8);
        int rate = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(12, 4));

        int? normalizedRate = rate == UnknownRate ? null : rate;
        // Some firmware reports the discharge rate as a positive number; the flags are authoritative.
        if (normalizedRate is int r && r > 0 && state.HasFlag(BatteryPowerState.Discharging) && !state.HasFlag(BatteryPowerState.Charging))
            normalizedRate = -r;

        return new BatteryStatusInfo(
            state,
            capacity == UnknownCapacity ? null : capacity,
            voltage == UnknownVoltage ? null : voltage,
            normalizedRate);
    }

    /// <summary>BatteryTemperature is reported in tenths of a degree Kelvin.</summary>
    public static double? ParseTemperatureC(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return null;
        uint tenthsKelvin = U32(b, 0);
        if (tenthsKelvin == 0 || tenthsKelvin == 0xFFFFFFFF) return null;
        return Math.Round(tenthsKelvin / 10.0 - 273.15, 1);
    }

    public static TimeSpan? ParseEstimatedTime(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return null;
        uint seconds = U32(b, 0);
        return seconds == UnknownTime ? null : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>BATTERY_MANUFACTURE_DATE { UCHAR Day; UCHAR Month; USHORT Year; }.</summary>
    public static DateOnly? ParseManufactureDate(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return null;
        int day = b[0], month = b[1], year = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(2, 2));
        if (year is > 0 and < 100) year += year < 80 ? 2000 : 1900;
        if (year < 1990 || year > 2100 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return null;
        return new DateOnly(year, month, day);
    }

    public static string? ParseString(ReadOnlySpan<byte> b)
    {
        int chars = b.Length / 2;
        int end = 0;
        while (end < chars && (b[end * 2] != 0 || b[end * 2 + 1] != 0)) end++;
        string s = Encoding.Unicode.GetString(b[..(end * 2)]).Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>BATTERY_CHARGING_SOURCE_INFORMATION { BATTERY_CHARGING_SOURCE_TYPE Type; BOOLEAN SourceOnline; }.</summary>
    public static ChargingSourceInfo? ParseChargingSource(ReadOnlySpan<byte> b)
    {
        if (b.Length < 5) return null;
        return new ChargingSourceInfo(ToSourceType(U32(b, 0)), b[4] != 0);
    }

    /// <summary>
    /// BATTERY_USB_CHARGER_STATUS:
    /// Type, Reserved, Flags, MaxCurrent (mA), Voltage (mV), PortType, PortId (ULONG64),
    /// PowerSourceInformation (pointer), OemCharger (GUID).
    /// </summary>
    public static UsbChargerStatus? ParseUsbChargerStatus(ReadOnlySpan<byte> b, int pointerSize = 8)
    {
        if (b.Length < 20) return null;

        uint portType = b.Length >= 24 ? U32(b, 20) : 0;
        ulong portId = b.Length >= 32 ? BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(24, 8)) : 0;
        int guidOffset = 32 + pointerSize;
        Guid oem = b.Length >= guidOffset + 16 ? new Guid(b.Slice(guidOffset, 16)) : Guid.Empty;

        return new UsbChargerStatus(
            ToSourceType(U32(b, 0)),
            Flags: U32(b, 8),
            MaxCurrentMa: U32(b, 12),
            VoltageMv: U32(b, 16),
            PortType: portType == 1 ? UsbChargerPort.TypeC : UsbChargerPort.Legacy,
            PortId: portId,
            OemCharger: oem);
    }

    /// <summary>
    /// SYSTEM_BATTERY_STATE: 4 BOOLEANs, 3 spare bytes, Tag, then MaxCapacity, RemainingCapacity,
    /// Rate, EstimatedTime, DefaultAlert1, DefaultAlert2 (all 32-bit).
    /// </summary>
    public static SystemPowerState ParseSystemBatteryState(ReadOnlySpan<byte> b)
    {
        if (b.Length < SystemBatteryStateSize)
            throw new ArgumentException($"SYSTEM_BATTERY_STATE needs {SystemBatteryStateSize} bytes, got {b.Length}.", nameof(b));

        int rate = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(16, 4));
        uint time = U32(b, 20);
        bool batteryPresent = b[1] != 0;
        return new SystemPowerState(
            AcOnline: b[0] != 0,
            BatteryPresent: batteryPresent,
            Charging: b[2] != 0,
            Discharging: b[3] != 0,
            MaxCapacityMwh: U32(b, 8),
            RemainingCapacityMwh: U32(b, 12),
            RateMw: rate == UnknownRate || !batteryPresent ? null : rate,
            EstimatedTime: time == UnknownTime || time == 0 ? null : TimeSpan.FromSeconds(time));
    }

    public static string DescribeChemistry(ReadOnlySpan<byte> raw)
    {
        string code = Encoding.ASCII.GetString(raw).TrimEnd('\0', ' ');
        string friendly = code.ToUpperInvariant().Replace("-", "").Replace(" ", "") switch
        {
            "PBAC" => "Lead acid",
            "LION" or "LII" or "LIION" => "Lithium-ion",
            "LIP" or "LIPO" or "LPOL" => "Lithium polymer",
            "NICD" => "Nickel cadmium",
            "NIMH" => "Nickel metal hydride",
            "NIZN" => "Nickel zinc",
            "RAM" => "Rechargeable alkaline-manganese",
            "" => "Unknown",
            _ => code,
        };
        return friendly == code || code.Length == 0 ? friendly : $"{friendly} ({code})";
    }

    private static ChargingSourceType ToSourceType(uint v) =>
        v is >= 1 and <= 3 ? (ChargingSourceType)v : ChargingSourceType.Unknown;

    private static uint U32(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));
}
