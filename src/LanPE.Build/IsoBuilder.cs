using CoreP = LanPE.Core;

namespace LanPE.Build;

/// <summary>
/// 用工具链把暂存目录生成为 BIOS+UEFI 双引导、可 DD 直写的 hybrid ISO。
/// 优先 grub-mkrescue（自动产出 hybrid 镜像）；缺失时回退到直接调用 xorriso。
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

        if (!string.IsNullOrWhiteSpace(_tools.GrubMkrescuePath))
            await BuildWithGrubMkrescueAsync(stagingDir, outputIso, volumeLabel, progress, ct).ConfigureAwait(false);
        else if (!string.IsNullOrWhiteSpace(_tools.XorrisoPath))
            await BuildWithXorrisoAsync(stagingDir, outputIso, volumeLabel, progress, ct).ConfigureAwait(false);
        else
            throw new FileNotFoundException("工具链中既没有 grub-mkrescue 也没有 xorriso，无法生成 ISO。");

        if (!File.Exists(outputIso))
            throw new InvalidOperationException("ISO 生成失败：输出文件不存在。");

        var fi = new FileInfo(outputIso);
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done,
            $"ISO 已生成：{outputIso}（{fi.Length / 1024.0 / 1024.0:F1} MB）"));
        return outputIso;
    }

    private async Task BuildWithGrubMkrescueAsync(
        string stagingDir, string outputIso, string? label,
        IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.BuildingIso, "开始生成 ISO（grub-mkrescue）…"));

        string? toolDir = Path.GetDirectoryName(_tools.GrubMkrescuePath);
        string args = $"-o \"{outputIso}\" -volid \"{Sanitize(label, "LanPE")}\" \"{stagingDir}\"";

        var result = await RunToolAsync(_tools.GrubMkrescuePath!, args, toolDir, stagingDir, progress, ct)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException("grub-mkrescue 失败。" + Environment.NewLine + result.StdErr);
    }

    private async Task BuildWithXorrisoAsync(
        string stagingDir, string outputIso, string? label,
        IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.BuildingIso, "开始生成 ISO（xorriso 直调）…"));

        string efiImg = "EFI/BOOT/BOOTX64.EFI";
        string biosImg = "boot/grub/i386-pc/eltorito.img";

        if (!File.Exists(Path.Combine(stagingDir, efiImg.Replace('/', Path.DirectorySeparatorChar))))
            throw new FileNotFoundException($"暂存目录缺少 UEFI 引导镜像 {efiImg}，无法直调 xorriso。");

        var sb = new System.Text.StringBuilder();
        sb.Append($"-as mkisofs -iso-level 3 -full-iso9660-filenames -volid \"{Sanitize(label, "LanPE")}\" ");
        sb.Append($"-eltorito-alt-boot -e \"{efiImg}\" -no-emul-boot -isohybrid-gpt-basdat ");

        if (File.Exists(Path.Combine(stagingDir, biosImg.Replace('/', Path.DirectorySeparatorChar))))
        {
            sb.Append($"-b \"{biosImg}\" -no-emul-boot -boot-load-size 4 -boot-info-table ");
            string mbr = Path.Combine(_tools.RootDir, "grub", "isohdpfx.bin");
            if (File.Exists(mbr)) sb.Append($"-isohybrid-mbr \"{mbr}\" ");
        }

        sb.Append($"-o \"{outputIso}\" \"{stagingDir}\"");

        string? toolDir = Path.GetDirectoryName(_tools.XorrisoPath);
        var result = await RunToolAsync(_tools.XorrisoPath!, sb.ToString(), toolDir, stagingDir, progress, ct)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException("xorriso 失败。" + Environment.NewLine + result.StdErr);
    }

    private static async Task<CoreP.ProcessResult> RunToolAsync(
        string exe, string args, string? toolDir, string workDir,
        IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        string oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!string.IsNullOrEmpty(toolDir))
            Environment.SetEnvironmentVariable("PATH", toolDir + Path.PathSeparator + oldPath);

        try
        {
            return await CoreP.ProcessRunner.RunAsync(exe, args, workDir, progress, CoreP.ProgressPhase.BuildingIso, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    private static string Sanitize(string? label, string fallback)
    {
        if (string.IsNullOrWhiteSpace(label)) return fallback;
        var sb = new System.Text.StringBuilder();
        foreach (var c in label)
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        var s = sb.ToString();
        return s.Length == 0 ? fallback : (s.Length > 32 ? s[..32] : s);
    }
}
