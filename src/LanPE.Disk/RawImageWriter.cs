using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CoreP = LanPE.Core;

namespace LanPE.Disk;

/// <summary>把 ISO 镜像 raw（DD）写入物理盘。</summary>
public sealed class RawImageWriter
{
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const int FSCTL_LOCK_VOLUME = 0x00090018;
    private const int FSCTL_UNLOCK_VOLUME = 0x0009001C;
    private const int FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const int IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    /// <summary>把镜像写入物理盘。需要管理员权限；调用方须已通过安全确认。</summary>
    public async Task WriteAsync(
        string imagePath,
        PhysicalDrive target,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!File.Exists(imagePath)) throw new FileNotFoundException("镜像不存在。", imagePath);

        if (target.IsSystem)
            throw new InvalidOperationException("拒绝对系统盘写入！");

        long imageSize = new FileInfo(imagePath).Length;
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.WritingDisk, $"打开 {target.DevicePath} …"));

        using var handle = CreateFile(
            target.DevicePath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开物理盘（请以管理员身份运行）。");

        TryIoControl(handle, FSCTL_LOCK_VOLUME);
        TryIoControl(handle, FSCTL_DISMOUNT_VOLUME);

        const int bufferSize = 4 << 20; // 4 MiB
        var buffer = new byte[bufferSize];
        long written = 0;

        await using var src = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var dev = new FileStream(handle, FileAccess.Write, bufferSize, isAsync: false);

        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            // 尾部补齐到扇区对齐（512B），避免设备写入对齐错误
            int aligned = AlignUp(read, 512);
            if (aligned != read) Array.Clear(buffer, read, aligned - read);

            await dev.WriteAsync(buffer.AsMemory(0, aligned), ct).ConfigureAwait(false);
            written += read;

            progress?.Report(new CoreP.ProgressInfo(CoreP.ProgressPhase.WritingDisk,
                $"写入中… {written / (1024 * 1024)} MB") { Current = written, Total = imageSize });
        }

        await dev.FlushAsync(ct).ConfigureAwait(false);

        FlushFileBuffers(handle);
        TryIoControl(handle, IOCTL_DISK_UPDATE_PROPERTIES);
        TryIoControl(handle, FSCTL_UNLOCK_VOLUME);

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done, $"写入完成：{target.DevicePath}"));
    }

    private static int AlignUp(int value, int align) => (value + align - 1) / align * align;

    private static void TryIoControl(SafeFileHandle h, int code)
    {
        try { DeviceIoControl(h, code, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero); }
        catch { /* 尽力而为 */ }
    }
}
