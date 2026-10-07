using CoreP = LanPE.Core;

namespace LanPE.Build;

/// <summary>
/// 用 grub-mkimage 生成 GRUB 引导镜像（BIOS 的 El Torito 映像与 UEFI 的 BOOTX64.EFI）。
///
/// 工具链来源：a1ive/grub 的 Windows 构建（grub2-latest.tar.gz），
/// 内含 grub-mkimage.exe 与 i386-pc / x86_64-efi 模块目录。
/// 该包不含 grub-mkrescue/xorriso，因此文件系统由 <see cref="Iso9660Writer"/> 直接写出。
/// </summary>
public sealed class GrubImageBuilder
{
    private readonly Toolchain _tools;

    public GrubImageBuilder(Toolchain tools) => _tools = tools;

    /// <summary>生成 BIOS El Torito 引导镜像（i386-pc）。</summary>
    public async Task<byte[]?> BuildBiosImageAsync(string prefix, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        string? mkimage = _tools.GrubMkimagePath;
        string? modDir = _tools.GrubModuleDir("i386-pc");
        if (mkimage == null || modDir == null) return null;

        string outFile = Path.Combine(Path.GetTempPath(), "lanpe_grub_bios_" + Guid.NewGuid().ToString("N") + ".img");
        string args = $"-d \"{modDir}\" -o \"{outFile}\" -O i386-pc -p \"{prefix}\" " +
                      "biosdisk part_msdos part_gpt fat ntfs exfat iso9660 udf " +
                      "search search_fs_file search_label search_fs_uuid " +
                      "normal linux linux16 chain loopback echo configfile ls cat " +
                      "gfxterm all_video bitmap font png jpeg";

        var r = await CoreP.ProcessRunner.RunAsync(mkimage, args, null, progress, CoreP.ProgressPhase.BuildingIso, ct)
            .ConfigureAwait(false);

        if (!r.Success || !File.Exists(outFile)) return null;

        var bytes = await File.ReadAllBytesAsync(outFile, ct).ConfigureAwait(false);
        try { File.Delete(outFile); } catch { /* 忽略 */ }
        return bytes;
    }

    /// <summary>生成 UEFI 引导镜像（x86_64-efi），即 EFI/BOOT/BOOTX64.EFI。</summary>
    public async Task<byte[]?> BuildUefiImageAsync(string prefix, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        string? mkimage = _tools.GrubMkimagePath;
        string? modDir = _tools.GrubModuleDir("x86_64-efi");
        if (mkimage == null || modDir == null) return null;

        string outFile = Path.Combine(Path.GetTempPath(), "lanpe_grub_efi_" + Guid.NewGuid().ToString("N") + ".efi");
        string args = $"-d \"{modDir}\" -o \"{outFile}\" -O x86_64-efi -p \"{prefix}\" " +
                      "part_msdos part_gpt fat ntfs exfat iso9660 udf " +
                      "search search_fs_file search_label search_fs_uuid " +
                      "normal linux linuxefi chain loopback echo configfile ls cat " +
                      "gfxterm all_video bitmap font png jpeg efi_gop efi_uga";

        var r = await CoreP.ProcessRunner.RunAsync(mkimage, args, null, progress, CoreP.ProgressPhase.BuildingIso, ct)
            .ConfigureAwait(false);

        if (!r.Success || !File.Exists(outFile)) return null;

        var bytes = await File.ReadAllBytesAsync(outFile, ct).ConfigureAwait(false);
        try { File.Delete(outFile); } catch { /* 忽略 */ }
        return bytes;
    }
}
