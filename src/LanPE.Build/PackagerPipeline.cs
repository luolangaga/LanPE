using CoreP = LanPE.Core;
using LanPE.Core.Net;

namespace LanPE.Build;

/// <summary>
/// 端到端流程编排：拉清单 → 下载所需资产 → 组装暂存 → 生成 ISO。
/// </summary>
public sealed class PackagerPipeline : IDisposable
{
    private readonly CoreP.AppSettings _settings;
    private readonly DownloadService _download;

    public PackagerPipeline(CoreP.AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _download = new DownloadService(settings.GitHubToken);
    }

    public string StagingDir => CoreP.AppPaths.StagingDir;

    public async Task<CoreP.Manifest.Manifest> FetchManifestAsync(
        IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
    {
        using var source = ManifestSourceFactory.Create(_settings);
        var m = await source.FetchAsync(progress, ct).ConfigureAwait(false);
        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done,
            $"清单已加载：{m.Name} {m.Version}（{m.Components.Count} 个组件）"));
        return m;
    }

    /// <summary>下载清单 + 所有被选中的资产（工具链、共享引导文件、组件载荷、所选软件）。</summary>
    public async Task<Dictionary<string, string>> DownloadAllAsync(
        CoreP.Manifest.Manifest manifest,
        CoreP.Selection selection,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<KeyValuePair<string, CoreP.Manifest.AssetRef>>();

        if (manifest.Toolchain != null)
            queue.Add(new("__tools__", manifest.Toolchain));

        // 共享引导文件（wimboot / bcd / boot.sdi / bootmgr…）
        foreach (var bf in manifest.BootFiles)
            queue.Add(new(bf.Id ?? bf.File ?? "boot", bf));

        foreach (var comp in manifest.Components)
        {
            var sel = selection.For(comp.Id);
            if (!sel.Selected) continue;

            foreach (var kv in comp.Payload)
                queue.Add(new(kv.Value.File ?? kv.Key, kv.Value));

            foreach (var sw in comp.Software)
                if (sel.IsSoftwareSelected(sw.Id))
                    queue.Add(new(sw.File ?? sw.Id ?? "sw", sw.ToAssetRef()));
        }

        int index = 0;
        foreach (var item in queue)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            string label = item.Key;

            var wrap = new Progress<CoreP.ProgressInfo>(p =>
            {
                double frac = p.CurrentPercent >= 0 ? p.CurrentPercent / 100.0 : 0;
                double overall = ((index - 1) + frac) / queue.Count * 100.0;
                progress?.Report(new CoreP.ProgressInfo(p.Phase, $"[{index}/{queue.Count}] {p.Message}")
                {
                    Current = p.Current,
                    Total = p.Total,
                    OverallPercent = overall
                });
            });

            var result = await _download.GetAsync(item.Value, wrap, ct, _settings.PreferCachedAssets)
                .ConfigureAwait(false);

            resolved[item.Key] = result.Path;
            if (item.Value.File != null) resolved[item.Value.File!] = result.Path;
            if (item.Value.Id != null) resolved[item.Value.Id!] = result.Path;
        }

        progress?.Report(CoreP.ProgressInfo.Log(CoreP.ProgressPhase.Done, $"全部资产就绪，共 {resolved.Count} 项。"));
        return resolved;
    }

    /// <summary>组装暂存目录。</summary>
    public async Task<string> StageAsync(
        CoreP.Manifest.Manifest manifest,
        CoreP.Selection selection,
        Dictionary<string, string> resolvedAssets,
        Toolchain tools,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        CoreP.AppPaths.ResetStaging();

        var ctx = new StagingContext { StagingDir = CoreP.AppPaths.StagingDir, Tools = tools };
        foreach (var kv in resolvedAssets) ctx.ResolvedAssets[kv.Key] = kv.Value;

        return await new StagingBuilder(ctx).BuildAsync(manifest, selection, progress, ct).ConfigureAwait(false);
    }

    /// <summary>生成 ISO。</summary>
    public Task<string> BuildIsoAsync(
        string stagingDir, string outputIso, string? volumeLabel,
        Toolchain tools, IProgress<CoreP.ProgressInfo>? progress, CancellationToken ct)
        => new IsoBuilder(tools).BuildIsoAsync(stagingDir, outputIso, volumeLabel, progress, ct);

    /// <summary>全流程：下载 + 组装 + 出 ISO。返回 ISO 路径。</summary>
    public async Task<string> RunAllAsync(
        CoreP.Manifest.Manifest manifest,
        CoreP.Selection selection,
        string outputIso,
        IProgress<CoreP.ProgressInfo>? progress,
        CancellationToken ct)
    {
        var resolved = await DownloadAllAsync(manifest, selection, progress, ct).ConfigureAwait(false);

        var tools = new Toolchain(CoreP.AppPaths.ToolsDir);
        if (resolved.TryGetValue("__tools__", out var toolsZip)) tools.Prepare(toolsZip);
        else tools.Locate();

        string staging = await StageAsync(manifest, selection, resolved, tools, progress, ct).ConfigureAwait(false);
        return await BuildIsoAsync(staging, outputIso, manifest.Name, tools, progress, ct).ConfigureAwait(false);
    }

    public void Dispose() => _download.Dispose();
}
