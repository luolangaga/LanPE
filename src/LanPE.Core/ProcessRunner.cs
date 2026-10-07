using System.Diagnostics;
using System.Text;

namespace LanPE.Core;

public sealed class ProcessResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public bool Success => ExitCode == 0;
}

/// <summary>外部进程运行封装（捕获输出并可回报进度）。</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDir,
        IProgress<ProgressInfo>? progress,
        ProgressPhase phase,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdout.AppendLine(e.Data);
            progress?.Report(ProgressInfo.Log(phase, e.Data));
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            progress?.Report(ProgressInfo.Log(phase, e.Data));
        };

        if (!p.Start())
            throw new InvalidOperationException("无法启动进程：" + fileName);

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch { /* 忽略 */ }
            throw;
        }

        return new ProcessResult
        {
            ExitCode = p.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString()
        };
    }

    public static Task<ProcessResult> RunCapturedAsync(string fileName, string arguments, string? workingDir, CancellationToken ct)
        => RunAsync(fileName, arguments, workingDir, null, ProgressPhase.Idle, ct);
}
