using System.Net.Http.Headers;
using LanPE.Core.Manifest;

namespace LanPE.Core.Net;

public sealed class DownloadResult
{
    public string Path { get; set; } = "";
    public bool FromCache { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>
/// 资产下载：带进度、sha256 校验、按哈希命名缓存去重。
/// </summary>
public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _http;

    public DownloadService(string? githubToken = null)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _http.Timeout = TimeSpan.FromMinutes(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LanPE-Packer/1.0");
        if (!string.IsNullOrWhiteSpace(githubToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", githubToken.Trim());
    }

    /// <summary>下载资产到缓存。若缓存已存在且哈希匹配，直接复用。</summary>
    public async Task<DownloadResult> GetAsync(
        AssetRef asset,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct,
        bool preferCache = true)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.Url))
            throw new InvalidOperationException("资产缺少下载地址。");

        AppPaths.EnsureCreated();
        string cachePath = BuildCachePath(asset);

        if (preferCache && File.Exists(cachePath))
        {
            progress?.Report(ProgressInfo.Log(ProgressPhase.Verifying, $"校验缓存：{asset.File}"));
            if (await HashUtil.VerifyAsync(cachePath, asset.Sha256, ct).ConfigureAwait(false))
            {
                var fi = new FileInfo(cachePath);
                progress?.Report(ProgressInfo.Log(ProgressPhase.Done, $"命中缓存：{asset.File}"));
                return new DownloadResult { Path = cachePath, FromCache = true, SizeBytes = fi.Length };
            }
            try { File.Delete(cachePath); } catch { /* 忽略 */ }
        }

        string tempPath = cachePath + ".part";
        await DownloadToFileAsync(asset, tempPath, progress, ct).ConfigureAwait(false);

        progress?.Report(ProgressInfo.Log(ProgressPhase.Verifying, $"校验 sha256：{asset.File}"));
        if (!await HashUtil.VerifyAsync(tempPath, asset.Sha256, ct).ConfigureAwait(false))
        {
            try { File.Delete(tempPath); } catch { /* 忽略 */ }
            throw new InvalidDataException($"哈希校验失败：{asset.File}（文件可能损坏或被篡改）。");
        }

        if (File.Exists(cachePath)) File.Delete(cachePath);
        File.Move(tempPath, cachePath);

        return new DownloadResult { Path = cachePath, FromCache = false, SizeBytes = new FileInfo(cachePath).Length };
    }

    private async Task DownloadToFileAsync(
        AssetRef asset, string targetPath,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        const int bufferSize = 1 << 20;

        using var resp = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? asset.SizeBytes;

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        var buffer = new byte[bufferSize];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;

            progress?.Report(new ProgressInfo(ProgressPhase.Downloading, $"下载中：{asset.File}")
            {
                Current = done,
                Total = total
            });
        }
    }

    private static string BuildCachePath(AssetRef asset)
    {
        string name = AppPaths.SafeName(asset.File);
        string key = !string.IsNullOrWhiteSpace(asset.Sha256)
            ? asset.Sha256[..Math.Min(16, asset.Sha256.Length)]
            : AppPaths.SafeName(asset.Url);
        return Path.Combine(AppPaths.CacheDir, key + "_" + name);
    }

    public void Dispose() => _http.Dispose();
}
