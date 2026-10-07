namespace LanPE.Build;

/// <summary>组装上下文：暂存目录 + 已下载资产路径 + 工具链。</summary>
public sealed class StagingContext
{
    public string StagingDir { get; set; } = "";
    public Toolchain? Tools { get; set; }

    /// <summary>资产 Key（file 名） -> 本地已下载路径。</summary>
    public Dictionary<string, string> ResolvedAssets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string PathInStaging(params string[] parts)
    {
        var all = new List<string> { StagingDir };
        all.AddRange(parts);
        return Path.Combine(all.ToArray());
    }
}

/// <summary>
/// 把选中的组件与软件组装到暂存目录，并生成 GRUB 配置。
/// PE 以裸 WIM 分发（wimboot 引导），软件旁挂到 LanPE/Apps/&lt;pe&gt;/ 供 PE 内启动器扫描。
/// </summary>
public sealed class StagingBuilder
{
    private readonly StagingContext _ctx;

    public StagingBuilder(StagingContext ctx) => _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));

    public async Task<string> BuildAsync(
        Core.Manifest.Manifest manifest,
        Core.Selection selection,
        IProgress<Core.ProgressInfo>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_ctx.StagingDir);

        // 标记文件：GRUB search --file 定位介质根
        string marker = _ctx.PathInStaging("LanPE", "marker");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        await File.WriteAllTextAsync(marker, $"{manifest.Name} {manifest.Version}{Environment.NewLine}", ct)
            .ConfigureAwait(false);

        var selected = new List<Core.Manifest.Component>();
        foreach (var c in manifest.Components)
        {
            var s = selection.For(c.Id);
            if (s.Selected) selected.Add(c);
        }

        if (selected.Count == 0)
            throw new InvalidOperationException("请至少选择一个组件。");

        // 1) 共享引导文件（wimboot / bcd / boot.sdi / bootmgr / bootmgr.efi）放到 boot/
        var bootFiles = await StageBootFilesAsync(manifest, progress, ct).ConfigureAwait(false);

        // 2) 各组件
        foreach (var comp in selected)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, $"组装组件：{comp.Name}"));

            if (comp.IsWindowsPe) await StageWinPeAsync(comp, selection.For(comp.Id), progress, ct).ConfigureAwait(false);
            else if (comp.IsLinux) await StageLinuxAsync(comp, progress, ct).ConfigureAwait(false);
        }

        // 3) GRUB 主配置
        string cfg = GrubConfigGenerator.Generate(manifest, selection, bootFiles);
        string cfgPath = _ctx.PathInStaging("boot", "grub", "grub.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        await File.WriteAllTextAsync(cfgPath, cfg, ct).ConfigureAwait(false);
        progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, "已生成 GRUB 菜单。"));

        return _ctx.StagingDir;
    }

    /// <summary>放置共享引导文件，返回其在介质内的相对路径映射。</summary>
    private async Task<Dictionary<string, string>> StageBootFilesAsync(
        Core.Manifest.Manifest manifest,
        IProgress<Core.ProgressInfo>? progress,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string bootDir = _ctx.PathInStaging("boot");
        Directory.CreateDirectory(bootDir);

        foreach (var bf in manifest.BootFiles)
        {
            string? local = TryResolve(bf, bf.Id);
            if (local == null) continue;

            string fileName = bf.File ?? Path.GetFileName(local);
            string dst = Path.Combine(bootDir, fileName);
            File.Copy(local, dst, overwrite: true);

            string rel = "boot/" + fileName;
            if (bf.Id != null) map[bf.Id] = rel;
            map[fileName] = rel;

            progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, $"放置引导文件：{fileName}"));
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return map;
    }

    private async Task StageWinPeAsync(
        Core.Manifest.Component comp,
        Core.Selection.ComponentSelection sel,
        IProgress<Core.ProgressInfo>? progress,
        CancellationToken ct)
    {
        // PE 主体：裸 WIM 直接放到 pe/<id>/ 下
        string peDir = _ctx.PathInStaging("pe", comp.Id ?? "pe");
        Directory.CreateDirectory(peDir);

        foreach (var kv in comp.Payload)
        {
            string local = Resolve(kv.Value, kv.Key);
            string dst = Path.Combine(peDir, kv.Value.File ?? Path.GetFileName(local));
            progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, $"放置 PE：{comp.Name}"));
            File.Copy(local, dst, overwrite: true);
        }

        // 旁挂软件：LanPE/Apps/<peId>/<swId>/
        foreach (var sw in comp.Software)
        {
            if (!sel.IsSoftwareSelected(sw.Id)) continue;

            string local = Resolve(sw.ToAssetRef(), sw.Id);
            string extractTo = (sw.ExtractTo ?? ("Apps/" + sw.Id)).Trim('/').Replace('\\', '/');
            string target = _ctx.PathInStaging("LanPE", "Apps", comp.Id ?? "pe",
                extractTo.Replace('/', Path.DirectorySeparatorChar));

            progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, $"安装软件：{sw.Name}"));
            await Core.ArchiveExtractor.ExtractAsync(local, target, sw.Archive, _ctx.Tools?.SevenZipPath, progress, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task StageLinuxAsync(
        Core.Manifest.Component comp,
        IProgress<Core.ProgressInfo>? progress,
        CancellationToken ct)
    {
        string dir = _ctx.PathInStaging("iso");
        Directory.CreateDirectory(dir);

        foreach (var kv in comp.Payload)
        {
            string local = Resolve(kv.Value, kv.Key);
            string dst = Path.Combine(dir, kv.Value.File ?? Path.GetFileName(local));
            progress?.Report(Core.ProgressInfo.Log(Core.ProgressPhase.Staging, $"放置 ISO：{comp.Name}"));
            File.Copy(local, dst, overwrite: true);
            break; // Linux 组件只有一个 ISO 载荷
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private string Resolve(Core.Manifest.AssetRef asset, string? key)
    {
        string? local = TryResolve(asset, key);
        return local ?? throw new FileNotFoundException($"资产尚未下载：{asset.File ?? key}");
    }

    private string? TryResolve(Core.Manifest.AssetRef? asset, string? key)
    {
        if (asset == null) return null;

        if (!string.IsNullOrEmpty(asset.File) && _ctx.ResolvedAssets.TryGetValue(asset.File!, out var p))
            return p;

        if (key != null && _ctx.ResolvedAssets.TryGetValue(key, out var p2))
            return p2;

        if (asset.Id != null && _ctx.ResolvedAssets.TryGetValue(asset.Id!, out var p3))
            return p3;

        if (!string.IsNullOrEmpty(asset.Url))
        {
            string tail = Path.GetFileName(asset.Url.Split('?')[0]);
            if (_ctx.ResolvedAssets.TryGetValue(tail, out var p4)) return p4;
        }

        return null;
    }
}
