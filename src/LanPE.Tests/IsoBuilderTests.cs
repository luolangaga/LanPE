using LanPE.Build;
using LanPE.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

/// <summary>
/// 用真实工具链生成一张小 ISO，验证 grub-mkimage + 纯 C# Iso9660Writer 的完整链路。
/// 需要 release/assets/lanpe-tools.zip 存在；否则跳过（Inconclusive）。
/// </summary>
[TestClass]
public class IsoBuilderTests
{
    private static string? FindToolsZip()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "release", "assets", "lanpe-tools.zip");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [TestMethod]
    public async Task BuildIso_WithRealToolchain_ProducesValidIso()
    {
        string? toolsZip = FindToolsZip();
        if (toolsZip == null)
            Assert.Inconclusive("未找到 release/assets/lanpe-tools.zip（先运行 scripts\\fetch-tools.ps1）。");

        var toolsRoot = Path.Combine(Path.GetTempPath(), "lanpe_tools_test_" + Guid.NewGuid().ToString("N"));
        var tools = new Toolchain(toolsRoot);
        tools.Prepare(toolsZip);

        Assert.IsNotNull(tools.GrubMkimagePath, "工具链中应有 grub-mkimage");
        Assert.IsNotNull(tools.GrubModuleDir("i386-pc"), "应有 i386-pc 模块目录");
        Assert.IsNotNull(tools.GrubModuleDir("x86_64-efi"), "应有 x86_64-efi 模块目录");

        // 构造一个最小 staging
        var staging = Path.Combine(Path.GetTempPath(), "lanpe_stage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "boot", "grub"));
        Directory.CreateDirectory(Path.Combine(staging, "LanPE"));
        await File.WriteAllTextAsync(
            Path.Combine(staging, "boot", "grub", "grub.cfg"),
            "set timeout=5\nmenuentry \"Test\" { echo hello }");
        await File.WriteAllTextAsync(Path.Combine(staging, "LanPE", "marker"), "LanPE test\n");

        var outIso = Path.Combine(Path.GetTempPath(), "lanpe_test_" + Guid.NewGuid().ToString("N") + ".iso");

        try
        {
            var progress = new Progress<ProgressInfo>(p => Console.WriteLine(p.Message));
            var builder = new IsoBuilder(tools);
            string result = await builder.BuildIsoAsync(staging, outIso, "LANPETEST", progress, CancellationToken.None);

            Assert.AreEqual(outIso, result);
            Assert.IsTrue(File.Exists(outIso), "ISO 应已生成");

            var fi = new FileInfo(outIso);
            Console.WriteLine($"ISO size: {fi.Length} bytes");
            Assert.IsTrue(fi.Length > 2048 * 32, "ISO 体积过小，疑似未写入内容");
            Assert.AreEqual(0, fi.Length % 2048, "ISO 长度应为 2048 扇区对齐");

            // 校验 ISO9660 主卷描述符（扇区 16，偏移 0x8000）
            using var fs = File.OpenRead(outIso);
            fs.Seek(16 * 2048, SeekOrigin.Begin);
            var header = new byte[7];
            fs.ReadExactly(header);
            Assert.AreEqual(1, header[0], "PVD 类型应为 1");
            Assert.AreEqual("CD001", System.Text.Encoding.ASCII.GetString(header, 1, 5), "PVD 标准标识");

            // 卷标应出现在 PVD 偏移 40
            fs.Seek(16 * 2048 + 40, SeekOrigin.Begin);
            var label = new byte[9];
            fs.ReadExactly(label);
            Assert.AreEqual("LANPETEST", System.Text.Encoding.ASCII.GetString(label).TrimEnd('\0', ' '));
        }
        finally
        {
            try { File.Delete(outIso); } catch { }
            try { Directory.Delete(staging, true); } catch { }
            try { Directory.Delete(toolsRoot, true); } catch { }
        }
    }
}
