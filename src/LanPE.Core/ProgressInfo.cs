namespace LanPE.Core;

public enum ProgressPhase
{
    Idle,
    FetchingManifest,
    Downloading,
    Verifying,
    Extracting,
    Staging,
    BuildingIso,
    WritingDisk,
    Installing,
    Done,
    Failed
}

/// <summary>统一进度回报。</summary>
public sealed class ProgressInfo
{
    public ProgressPhase Phase { get; set; }
    public string? Message { get; set; }

    /// <summary>当前任务已完成量。</summary>
    public long Current { get; set; }

    /// <summary>当前任务总量（未知为 0）。</summary>
    public long Total { get; set; }

    /// <summary>整体进度百分比（0-100，未知为 -1）。</summary>
    public double OverallPercent { get; set; } = -1;

    public ProgressInfo(ProgressPhase phase, string? message)
    {
        Phase = phase;
        Message = message;
    }

    public static ProgressInfo Log(ProgressPhase phase, string? message) => new(phase, message);

    public double CurrentPercent => Total > 0 ? (double)Current / Total * 100.0 : -1.0;
}
