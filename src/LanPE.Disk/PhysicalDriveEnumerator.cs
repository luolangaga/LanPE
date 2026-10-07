using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using CoreP = LanPE.Core;

namespace LanPE.Disk;

public sealed class PhysicalDrive
{
    public int Index { get; set; }
    public string Model { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsRemovable { get; set; }
    public bool IsSystem { get; set; }

    public string DevicePath => $@"\\.\PhysicalDrive{Index}";

    public string DisplayName =>
        $"PhysicalDrive{Index} · {Model} · {SizeBytes / 1024.0 / 1024.0 / 1024.0:F1} GB" +
        (IsSystem ? " · [系统盘]" : "") + (IsRemovable ? " · [可移动]" : "");
}

/// <summary>
/// 物理盘枚举。使用原生 SetupAPI / DeviceIoControl 而非 WMI ——
/// System.Management 在 NativeAOT 下依赖 COM 互操作，会在运行时抛异常。
/// </summary>
public static class PhysicalDriveEnumerator
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const int SPDRP_FRIENDLYNAME = 0x0000000C;
    private const int SPDRP_DEVICEDESC = 0x00000000;

    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const int IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    private const int IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public uint cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GET_LENGTH_INFORMATION
    {
        public long Length;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        int property, out uint propertyRegDataType, byte[]? propertyBuffer,
        uint propertyBufferSize, out uint requiredSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, int dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    private static readonly Guid DiskClassGuid = new("{53f56307-b6bf-11d0-94f2-00a0c91efb8b}");
    private static readonly Guid GuidDevinterfaceDisk = new("{53f56307-b6bf-11d0-94f2-00a0c91efb8b}");

    public static IReadOnlyList<PhysicalDrive> List()
    {
        var drives = new List<PhysicalDrive>();
        int systemDiskIndex = GetSystemDiskIndex();

        Guid diskGuid = GuidDevinterfaceDisk;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref diskGuid, null, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
            return drives;

        try
        {
            uint index = 0;
            while (true)
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

                if (!SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref diskGuid, index, ref iface))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_NO_MORE_ITEMS || err == ERROR_INSUFFICIENT_BUFFER) break;
                    break;
                }

                string? path = GetDevicePath(hDevInfo, ref iface);
                if (path != null)
                {
                    var info = QueryDrive(path);
                    if (info != null)
                    {
                        info.IsSystem = info.Index == systemDiskIndex;
                        drives.Add(info);
                    }
                }

                index++;
                if (index > 64) break; // 安全阀
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        drives.Sort((a, b) => a.Index.CompareTo(b.Index));
        return drives;
    }

    private static string? GetDevicePath(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA iface)
    {
        // 先取所需大小
        SetupDiGetDeviceInterfaceDetail(hDevInfo, ref iface, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (required == 0) return null;

        IntPtr detail = Marshal.AllocHGlobal((int)required);
        try
        {
            // cbSize 依平台为 8(x64) 或 6(x86)
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

            if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ref iface, detail, required, out _, IntPtr.Zero))
                return null;

            IntPtr pPath = detail + IntPtr.Size;
            return Marshal.PtrToStringUni(pPath);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static PhysicalDrive? QueryDrive(string devicePath)
    {
        // 打开设备（仅查询，不需写权限）
        using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        // 设备号
        int size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
        IntPtr pNum = Marshal.AllocHGlobal(size);
        int number = -1;
        try
        {
            if (DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                    pNum, size, out _, IntPtr.Zero))
                number = (int)Marshal.PtrToStructure<STORAGE_DEVICE_NUMBER>(pNum).DeviceNumber;
        }
        finally { Marshal.FreeHGlobal(pNum); }

        if (number < 0) return null;

        // 容量
        long length = 0;
        int lenSize = Marshal.SizeOf<GET_LENGTH_INFORMATION>();
        IntPtr pLen = Marshal.AllocHGlobal(lenSize);
        try
        {
            if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0,
                    pLen, lenSize, out _, IntPtr.Zero))
                length = Marshal.PtrToStructure<GET_LENGTH_INFORMATION>(pLen).Length;
        }
        finally { Marshal.FreeHGlobal(pLen); }

        // 型号：取设备路径尾部无法得到型号，改用友好名不可得时退回通用名
        string model = QueryModel(devicePath) ?? $"磁盘 {number}";

        return new PhysicalDrive
        {
            Index = number,
            Model = model,
            SizeBytes = length,
            IsRemovable = IsRemovableDrive(devicePath)
        };
    }

    private static string? QueryModel(string devicePath)
    {
        // STORAGE_PROPERTY_QUERY + STORAGE_DEVICE_DESCRIPTOR
        // PropertyId=0 (DeviceProperty), QueryType=0 (Standard)
        int qSize = 12; // 4(PropertyId) + 4(QueryType) + 8(AdditionalParameters)
        IntPtr pQuery = Marshal.AllocHGlobal(qSize);
        IntPtr pDesc = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt32(pQuery, 0);
            Marshal.WriteInt32(pQuery + 4, 0);

            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            if (!DeviceIoControl(handle, (int)IOCTL_STORAGE_QUERY_PROPERTY, pQuery, qSize,
                    pDesc, 1024, out _, IntPtr.Zero))
                return null;

            // STORAGE_DEVICE_DESCRIPTOR: Version(4) Size(4) DeviceType(1) DeviceTypeModifier(1)
            // RemovableMedia(1) CommandQueueing(1) VendorIdOffset(4) ProductIdOffset(4)...
            int vendorOff = Marshal.ReadInt32(pDesc + 12);
            int productOff = Marshal.ReadInt32(pDesc + 16);
            bool removable = Marshal.ReadByte(pDesc + 10) != 0;

            string vendor = vendorOff > 0 ? (Marshal.PtrToStringAnsi(pDesc + vendorOff) ?? "") : "";
            string product = productOff > 0 ? (Marshal.PtrToStringAnsi(pDesc + productOff) ?? "") : "";

            string model = (vendor.Trim() + " " + product.Trim()).Trim();
            return string.IsNullOrEmpty(model) ? null : model;
        }
        catch { return null; }
        finally
        {
            Marshal.FreeHGlobal(pQuery);
            Marshal.FreeHGlobal(pDesc);
        }
    }

    private static bool IsRemovableDrive(string devicePath)
    {
        int qSize = 12;
        IntPtr pQuery = Marshal.AllocHGlobal(qSize);
        IntPtr pDesc = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt32(pQuery, 0);
            Marshal.WriteInt32(pQuery + 4, 0);

            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return false;

            if (!DeviceIoControl(handle, (int)IOCTL_STORAGE_QUERY_PROPERTY, pQuery, qSize,
                    pDesc, 1024, out _, IntPtr.Zero))
                return false;

            return Marshal.ReadByte(pDesc + 10) != 0; // RemovableMedia
        }
        catch { return false; }
        finally
        {
            Marshal.FreeHGlobal(pQuery);
            Marshal.FreeHGlobal(pDesc);
        }
    }

    /// <summary>
    /// 系统盘物理索引。优先用 \\.\C: 的设备号（系统分区所在盘）。
    /// </summary>
    public static int GetSystemDiskIndex()
    {
        string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        string volume = $@"\\.\{sysDrive.TrimEnd('\\')}";

        using var handle = CreateFile(volume, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return -1;

        int size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
        IntPtr pNum = Marshal.AllocHGlobal(size);
        try
        {
            if (DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                    pNum, size, out _, IntPtr.Zero))
                return (int)Marshal.PtrToStructure<STORAGE_DEVICE_NUMBER>(pNum).DeviceNumber;
        }
        catch { /* 忽略 */ }
        finally { Marshal.FreeHGlobal(pNum); }

        return -1;
    }
}
