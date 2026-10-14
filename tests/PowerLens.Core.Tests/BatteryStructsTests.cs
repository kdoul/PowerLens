using System.Buffers.Binary;
using System.Text;
using PowerLens.Core;

namespace PowerLens.Core.Tests;

public class BatteryStructsTests
{
    [Fact]
    public void ParsesBatteryInformation()
    {
        var b = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)BatteryCapabilities.SystemBattery);
        b[4] = 1;
        Encoding.ASCII.GetBytes("LION").CopyTo(b, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 57_000);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 51_300);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 312);

        var info = BatteryStructs.ParseBatteryInformation(b);

        Assert.Equal("Rechargeable", info.Technology);
        Assert.Equal("Lithium-ion (LION)", info.Chemistry);
        Assert.Equal(57_000u, info.DesignedCapacity);
        Assert.Equal(51_300u, info.FullChargedCapacity);
        Assert.Equal(312u, info.CycleCount);
        Assert.False(info.IsRelative);
    }

    [Fact]
    public void StatusMapsUnknownsToNullAndFixesPositiveDischargeRate()
    {
        var b = Status(BatteryPowerState.Discharging, 0xFFFFFFFF, 0xFFFFFFFF, 9_500);
        var status = BatteryStructs.ParseBatteryStatus(b);
        Assert.Null(status.Capacity);
        Assert.Null(status.VoltageMv);
        Assert.Equal(-9_500, status.RateMw);

        var unknownRate = BatteryStructs.ParseBatteryStatus(Status(BatteryPowerState.PowerOnLine, 30_000, 12_600, unchecked((int)0x80000000)));
        Assert.Null(unknownRate.RateMw);
        Assert.Equal(30_000u, unknownRate.Capacity);
    }

    [Fact]
    public void ParsesTemperatureAndManufactureDate()
    {
        var t = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(t, 3032); // 303.2 K
        Assert.Equal(30.1, BatteryStructs.ParseTemperatureC(t)!.Value, 1);

        var d = new byte[] { 14, 3, 0, 0 };
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(2), 2023);
        Assert.Equal(new DateOnly(2023, 3, 14), BatteryStructs.ParseManufactureDate(d));

        Assert.Null(BatteryStructs.ParseManufactureDate([0, 0, 0, 0]));
    }

    [Fact]
    public void ParsesNullTerminatedUnicode()
    {
        var bytes = new byte[64];
        Encoding.Unicode.GetBytes("DELL 7FHHV").CopyTo(bytes, 0);
        Assert.Equal("DELL 7FHHV", BatteryStructs.ParseString(bytes));
        Assert.Null(BatteryStructs.ParseString(new byte[8]));
    }

    [Fact]
    public void ParsesUsbChargerStatusAsPdContract()
    {
        var b = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(b, 2);                 // USB
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), 0x2);     // UCM PD
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 3_250);  // mA
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 20_000); // mV
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 1);      // Type-C

        var status = BatteryStructs.ParseUsbChargerStatus(b)!;

        Assert.Equal(ChargingSourceType.Usb, status.Type);
        Assert.True(status.IsPowerDelivery);
        Assert.Equal(UsbChargerPort.TypeC, status.PortType);
        Assert.Equal(65.0, status.Watts!.Value, 3);
    }

    [Fact]
    public void ParsesSystemBatteryState()
    {
        var b = new byte[32];
        b[0] = 1; b[1] = 1; b[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), 50_000);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 25_000);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16), 31_000);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 0xFFFFFFFF);

        var s = BatteryStructs.ParseSystemBatteryState(b);

        Assert.True(s.AcOnline);
        Assert.True(s.Charging);
        Assert.Equal(31_000, s.RateMw);
        Assert.Null(s.EstimatedTime);
    }

    private static byte[] Status(BatteryPowerState state, uint capacity, uint voltage, int rate)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)state);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), capacity);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), voltage);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), rate);
        return b;
    }
}
