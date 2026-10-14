using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PowerLens.Core.Native;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    /// <summary>GUID_DEVCLASS_BATTERY (also the battery device interface class).</summary>
    public static readonly Guid GuidDevClassBattery = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // CTL_CODE(FILE_DEVICE_BATTERY = 0x29, function, METHOD_BUFFERED, FILE_READ_ACCESS)
    public const uint IOCTL_BATTERY_QUERY_TAG = 0x00294040;
    public const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x00294044;
    public const uint IOCTL_BATTERY_QUERY_STATUS = 0x0029404C;

    /// <summary>POWER_INFORMATION_LEVEL.SystemBatteryState.</summary>
    public const int SystemBatteryState = 5;

    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, int inBufferSize,
        byte[]? outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("powrprof.dll")]
    public static extern uint CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, int inputBufferLength, byte[] outputBuffer, int outputBufferLength);

    public static string DescribeError(int error) => error switch
    {
        1 => "not supported by this battery driver",
        2 or 433 => "battery changed or removed",
        5 => "access denied",
        _ => new Win32Exception(error).Message,
    };
}
