using System.Diagnostics;
using System.Text;
using System.IO.Compression;

namespace LanPE.Core;

/// <summary>归档解包（zip / 7z / 直拷）。</summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// 解包归档到目标目录。archive 取值：zip | 7z | none。
    /// 7z 需要外部 7za.exe（sevenZipPath 指向其路径）。
    /// </summary>
    public static async Task ExtractAsync(
        string archivePath,
        string targetDir,
        string? archiveKind,
        string? sevenZipPath,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        switch ((archiveKind ?? "none").ToLowerInvariant())
        {
            case "none":
                await CopyFileAsync(archivePath, Path.Combine(targetDir, Path.GetFileName(archivePath)), progress, ct)
                    .ConfigureAwait(false);
                break;
            case "zip":
                ExtractZip(archivePath, targetDir, progress, ct);
                break;
            case "7z":
                if (string.IsNullOrWhiteSpace(sevenZipPath) || !File.Exists(sevenZipPath))
                    throw new FileNotFoundException("需要 7za.exe 才能解压 7z 归档。", sevenZipPath);
                await RunSevenZipAsync(sevenZipPath, archivePath, targetDir, progress, ct).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"不支持的归档类型：{archiveKind}");
        }
    }

    private static void ExtractZip(string archivePath, string targetDir, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        string root = Path.GetFullPath(targetDir);

        long total = 0;
        foreach (var e in zip.Entries) total += e.Length;

        long done = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("归档包含非法路径（疑似 Zip Slip）：" + entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            done += entry.Length;

            progress?.Report(new ProgressInfo(ProgressPhase.Extracting, "解包：" + entry.FullName)
            {
                Current = done,
                Total = total
            });
        }
    }

    private static async Task RunSevenZipAsync(
        string sevenZip, string archivePath, string targetDir,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x \"{archivePath}\" -o\"{targetDir}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 7za。");
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) progress?.Report(ProgressInfo.Log(ProgressPhase.Extracting, e.Data));
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"7za 解压失败，退出码 {p.ExitCode}。");
    }

    private static async Task CopyFileAsync(string source, string dest, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        const int buf = 1 << 20;
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, buf, true);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, buf, true);

        var buffer = new byte[buf];
        long total = src.Length, done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buf), ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            progress?.Report(new ProgressInfo(ProgressPhase.Staging, "复制：" + Path.GetFileName(source))
            {
                Current = done,
                Total = total
            });
        }
    }
}
