using System.Text.RegularExpressions;
using CoreP = LanPE.Core;

namespace LanPE.Disk;

public sealed class InstallTarget
{
    /// <summary>数据分区根（如 D:\），用于放置 LanPE 数据。</summary>
    public string DataRoot { get; set; } = "";

    /// <summary>ESP 盘符（如 S:\）。为空则仅安装数据（不注册引导）。</summary>
    public string EspRoot { get; set; } = "";

    /// <summary>EFI 内的引导子目录名。</summary>
    public string EfiDirName { get; set; } = "LanPE";

    /// <summary>启动项显示名。</summary>
    public string BootEntryName { get; set; } = "LanPE";
}

/// <summary>
/// 把已组装内容安装到本地磁盘：数据分区释放 + ESP 写入 GRUB + bcdedit 注册固件启动项。
/// </summary>
public sealed class LocalInstaller
{
    private const string EfiGrubName = "grubx64.efi";

    public async Task InstallAsync(
        string stagingDir,
        string grubEfiSource,
        InstallTarget target,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.DataRoot))
            throw new InvalidOperationException("未指定数据目录。");

        string dataDir = Path.Combine(target.DataRoot, "LanPE");
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, $"复制数据到 {dataDir} …"));
        await CopyDirectoryAsync(stagingDir, dataDir, progress, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(target.EspRoot))
        {
            progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing,
                "未指定 ESP，跳过引导注册（仅数据已安装）。"));
            return;
        }

        string efiDir = Path.Combine(target.EspRoot, "EFI", target.EfiDirName);
        Directory.CreateDirectory(efiDir);

        string destEfi = Path.Combine(efiDir, EfiGrubName);
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, $"写入 EFI 引导：{destEfi}"));
        File.Copy(grubEfiSource, destEfi, overwrite: true);

        string cfgSrc = Path.Combine(stagingDir, "boot", "grub", "grub.cfg");
        if (File.Exists(cfgSrc))
            File.Copy(cfgSrc, Path.Combine(efiDir, "grub.cfg"), overwrite: true);

        await RegisterBootEntryAsync(target, progress, ct).ConfigureAwait(false);
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done, "本地安装完成。"));
    }

    private static async Task RegisterBootEntryAsync(
        InstallTarget target, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, "注册 UEFI 启动项（bcdedit）…"));

        var create = await CoreP.ProcessRunner.RunCapturedAsync(
            "bcdedit.exe", $"/create /d \"{target.BootEntryName}\" /application bootsector", null, ct)
            .ConfigureAwait(false);

        string? guid = ExtractGuid(create.StdOut);
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException("bcdedit 创建启动项失败：" + create.StdOut + create.StdErr);

        string espLetter = target.EspRoot.TrimEnd('\\');
        string efiPath = $"\\EFI\\{target.EfiDirName}\\{EfiGrubName}";

        await RunBcdedit($" /set {guid} device partition={espLetter}", ct).ConfigureAwait(false);
        await RunBcdedit($" /set {guid} path {efiPath}", ct).ConfigureAwait(false);
        await RunBcdedit($" /set {guid} description \"{target.BootEntryName}\"", ct).ConfigureAwait(false);
        await RunBcdedit($" /set {{fwbootmgr}} displayorder {guid} /addfirst", ct).ConfigureAwait(false);

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, $"启动项已注册：{guid}"));
    }

    /// <summary>卸载：删除数据目录并从固件启动项移除。</summary>
    public async Task UninstallAsync(
        InstallTarget target, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(target.DataRoot))
        {
            string dataDir = Path.Combine(target.DataRoot, "LanPE");
            if (Directory.Exists(dataDir))
            {
                progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, $"删除 {dataDir} …"));
                try { Directory.Delete(dataDir, true); } catch { /* 忽略 */ }
            }
        }

        if (!string.IsNullOrWhiteSpace(target.EspRoot))
        {
            string efiDir = Path.Combine(target.EspRoot, "EFI", target.EfiDirName);
            if (Directory.Exists(efiDir))
            {
                try { Directory.Delete(efiDir, true); } catch { /* 忽略 */ }
            }

            string? guid = await FindBootEntryAsync(target.BootEntryName, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(guid))
            {
                await RunBcdedit($" /set {{fwbootmgr}} displayorder {guid} /remove", ct).ConfigureAwait(false);
                await RunBcdedit($" /delete {guid} /f", ct).ConfigureAwait(false);
                progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Installing, "已移除启动项。"));
            }
        }

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done, "卸载完成。"));
    }

    private static async Task<string?> FindBootEntryAsync(string name, CancellationToken ct)
    {
        var r = await CoreP.ProcessRunner.RunCapturedAsync("bcdedit.exe", "/enum all /v", null, ct).ConfigureAwait(false);
        string? currentId = null;
        foreach (var raw in r.StdOut.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("identifier", StringComparison.OrdinalIgnoreCase))
                currentId = ExtractGuid(line);
            else if (line.StartsWith("description", StringComparison.OrdinalIgnoreCase) &&
                     line.Contains(name, StringComparison.OrdinalIgnoreCase))
                return currentId;
        }
        return null;
    }

    private static async Task RunBcdedit(string args, CancellationToken ct)
    {
        var r = await CoreP.ProcessRunner.RunCapturedAsync("bcdedit.exe", args, null, ct).ConfigureAwait(false);
        if (!r.Success)
            throw new InvalidOperationException($"bcdedit {args} 失败：{r.StdErr}{r.StdOut}");
    }

    private static string? ExtractGuid(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = Regex.Match(text, @"\{[0-9a-fA-F\-]{36}\}");
        return m.Success ? m.Value : null;
    }

    private static async Task CopyDirectoryAsync(
        string source, string dest, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);

        long total = 0;
        foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            total += new FileInfo(f).Length;

        long done = 0;
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(dest, dir[source.Length..].TrimStart('\\', '/')));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string rel = file[source.Length..].TrimStart('\\', '/');
            string dst = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            await using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            await using var d = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            await s.CopyToAsync(d, 1 << 20, ct).ConfigureAwait(false);

            done += new FileInfo(file).Length;
            progress?.Report(new CoreP.ProgressInfo(CoreP.ProgressPhase.Installing, "复制：" + rel)
            {
                Current = done,
                Total = total
            });
        }
    }
}
