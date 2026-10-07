using System.Net.Http.Headers;
using System.Text.Json;

namespace LanPE.Core.Net;

public interface IManifestSource : IDisposable
{
    Task<Manifest.Manifest> FetchAsync(IProgress<ProgressInfo>? progress, CancellationToken ct);
}

/// <summary>从 GitHub Release 获取 manifest.json（latest 或指定 tag）。</summary>
public sealed class GitHubManifestSource : IManifestSource
{
    private const string ManifestAssetName = "manifest.json";
    private readonly HttpClient _http;
    private readonly string _owner, _repo, _tag;

    public GitHubManifestSource(string owner, string repo, string? tag = null, string? token = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _tag = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LanPE-Packer/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    public async Task<Manifest.Manifest> FetchAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(ProgressInfo.Log(ProgressPhase.FetchingManifest,
            string.IsNullOrEmpty(_tag) ? "获取最新 Release 清单…" : $"获取 Release {_tag} 清单…"));

        string api = string.IsNullOrEmpty(_tag)
            ? $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest"
            : $"https://api.github.com/repos/{_owner}/{_repo}/releases/tags/{Uri.EscapeDataString(_tag)}";

        string releaseJson = await _http.GetStringAsync(api, ct).ConfigureAwait(false);

        string manifestUrl = ExtractBrowserDownloadUrl(releaseJson)
            ?? throw new InvalidOperationException($"Release 中找不到 {ManifestAssetName} 资产。");

        progress?.Report(ProgressInfo.Log(ProgressPhase.FetchingManifest, "下载清单…"));
        string manifestJson = await _http.GetStringAsync(manifestUrl, ct).ConfigureAwait(false);
        return Manifest.ManifestParser.Parse(manifestJson, manifestUrl);
    }

    /// <summary>在 release JSON 中定位名为 manifest.json 的资产下载链接。</summary>
    private static string? ExtractBrowserDownloadUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var a in assets.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), ManifestAssetName, StringComparison.OrdinalIgnoreCase) &&
                a.TryGetProperty("browser_download_url", out var url))
            {
                return url.GetString();
            }
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>从任意 URL 拉取清单。</summary>
public sealed class UrlManifestSource : IManifestSource
{
    private readonly string _url;
    private readonly string? _token;

    public UrlManifestSource(string url, string? token = null)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _token = token;
    }

    public async Task<Manifest.Manifest> FetchAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(ProgressInfo.Log(ProgressPhase.FetchingManifest, $"从 URL 获取清单：{_url}"));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LanPE-Packer/1.0");
        if (!string.IsNullOrWhiteSpace(_token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token.Trim());

        string json = await http.GetStringAsync(_url, ct).ConfigureAwait(false);
        return Manifest.ManifestParser.Parse(json, _url);
    }

    public void Dispose() { }
}

/// <summary>从本地文件读取清单（离线 / 调试）。</summary>
public sealed class FileManifestSource : IManifestSource
{
    private readonly string _path;

    public FileManifestSource(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public Task<Manifest.Manifest> FetchAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(ProgressInfo.Log(ProgressPhase.FetchingManifest, $"读取本地清单：{_path}"));
        return Task.FromResult(Manifest.ManifestParser.Parse(File.ReadAllText(_path), _path));
    }

    public void Dispose() { }
}

public static class ManifestSourceFactory
{
    public static IManifestSource Create(AppSettings s) => s.SourceKind switch
    {
        ManifestSourceKind.CustomUrl => new UrlManifestSource(s.CustomManifestUrl, s.GitHubToken),
        ManifestSourceKind.LocalFile => new FileManifestSource(s.LocalManifestPath),
        _ => new GitHubManifestSource(s.RepoOwner, s.RepoName, null, s.GitHubToken)
    };
}
