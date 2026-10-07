using System.Text.Json;
using LanPE.Core;
using LanPE.Core.Manifest;
using LanPE.Core.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

/// <summary>
/// 端到端验证：从真实 GitHub Release 拉取 manifest.json 并解析校验。
/// 默认跳过（需网络）；用 --filter 或环境变量 LANPE_E2E=1 显式启用。
/// </summary>
[TestClass]
public class LiveReleaseTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("LANPE_E2E") == "1";

    [TestMethod]
    public async Task Fetch_LiveManifest_FromGitHubRelease()
    {
        if (!Enabled)
        {
            Assert.Inconclusive("跳过（需网络）。设置 LANPE_E2E=1 以启用。");
            return;
        }

        var settings = new AppSettings { RepoOwner = "luolangaga", RepoName = "LanPE" };
        using var source = ManifestSourceFactory.Create(settings);

        var progress = new Progress<ProgressInfo>(p => Console.WriteLine(p.Message));
        var m = await source.FetchAsync(progress, CancellationToken.None);

        Assert.IsNotNull(m);
        Assert.AreEqual("LanPE", m.Name);
        Assert.IsTrue(m.Components.Count >= 3, "应至少含 3 个组件");

        Console.WriteLine($"=== {m.Name} {m.Version} ({m.ReleaseTag}) ===");
        Console.WriteLine($"bootFiles: {m.BootFiles.Count}");
        foreach (var c in m.Components)
            Console.WriteLine($"  - {c.Id} [{c.Kind}] {c.Boot.Mode} -> {c.Boot.Path}");

        Assert.IsNotNull(m.FindComponent("win11pe"));
        Assert.IsNotNull(m.FindComponent("win10pe"));
        Assert.IsNotNull(m.FindComponent("linux-rescue"));
        Assert.IsNotNull(m.FindBootFile("wimboot"));
    }

    [TestMethod]
    public async Task Verify_AllAssetUrlsAreReachable()
    {
        if (!Enabled)
        {
            Assert.Inconclusive("跳过（需网络）。设置 LANPE_E2E=1 以启用。");
            return;
        }

        var settings = new AppSettings { RepoOwner = "luolangaga", RepoName = "LanPE" };
        using var source = ManifestSourceFactory.Create(settings);
        var m = await source.FetchAsync(null, CancellationToken.None);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LanPE-Packer/1.0");

        var urls = new List<(string what, string url)>();
        foreach (var bf in m.BootFiles) urls.Add(($"boot:{bf.Id}", bf.Url!));
        foreach (var c in m.Components)
        {
            foreach (var kv in c.Payload) urls.Add(($"{c.Id}.payload.{kv.Key}", kv.Value.Url!));
            foreach (var sw in c.Software) urls.Add(($"{c.Id}.sw.{sw.Id}", sw.Url!));
        }

        int ok = 0;
        var bad = new List<string>();
        foreach (var (what, url) in urls)
        {
            try
            {
                using var resp = await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, url), CancellationToken.None);
                if (resp.IsSuccessStatusCode) ok++;
                else bad.Add($"{what}: HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                bad.Add($"{what}: {ex.Message}");
            }
        }

        Console.WriteLine($"可达 {ok}/{urls.Count}");
        foreach (var b in bad) Console.WriteLine("  BAD " + b);

        Assert.AreEqual(urls.Count, ok, "存在不可达资产：" + string.Join("; ", bad));
    }
}
