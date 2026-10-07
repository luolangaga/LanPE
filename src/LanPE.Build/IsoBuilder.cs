using CoreP = LanPE.Core;

namespace LanPE.Build;

/// <summary>
/// 生成 BIOS+UEFI 双引导的 hybrid ISO。
///
/// 实现路径：grub-mkimage 产出引导镜像 + 纯 C# 的 <see cref="Iso9660Writer"/> 写出文件系统。
/// 不依赖 xorriso / grub-mkrescue —— 这两个在 Windows 上不易获取，
/// 而 Native AOT 产物应当零外部工具依赖。
/// </summary>
public sealed class IsoBuilder
{
    private readonly Toolchain _tools;

    public IsoBuilder(Toolchain tools) => _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public async Task<string> BuildIsoAsync(
        string stagingDir,
        string outputIso,
        string? volumeLabel,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(stagingDir))
            throw new DirectoryNotFoundException("暂存目录不存在：" + stagingDir);

        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outputIso));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.BuildingIso, "生成 GRUB 引导镜像…"));

        var grub = new GrubImageBuilder(_tools);
        byte[]? biosImg = await grub.BuildBiosImageAsync("/boot/grub", progress, ct).ConfigureAwait(false);
        byte[]? uefiImg = await grub.BuildUefiImageAsync("/boot/grub", progress, ct).ConfigureAwait(false);

        if (biosImg == null && uefiImg == null)
            throw new FileNotFoundException(
                "工具链中未找到可用的 grub-mkimage（或其模块目录）。请先准备 lanpe-tools.zip：\n" +
                "  .\\scripts\\fetch-tools.ps1");

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.BuildingIso, "写入 ISO9660 文件系统…"));

        var writer = new Iso9660Writer(volumeLabel ?? "LanPE");
        int biosIdx = biosImg != null ? writer.AddBootImage(biosImg) : -1;
        int uefiIdx = uefiImg != null ? writer.AddBootImage(uefiImg) : -1;

        writer.AddDirectoryTree(stagingDir);

        await Task.Run(() => writer.Write(outputIso, biosIdx, uefiIdx), ct).ConfigureAwait(false);

        if (!File.Exists(outputIso))
            throw new InvalidOperationException("ISO 生成失败：输出文件不存在。");

        var fi = new FileInfo(outputIso);
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done,
            $"ISO 已生成：{outputIso}（{fi.Length / 1024.0 / 1024.0:F1} MB）"));
        return outputIso;
    }
}
