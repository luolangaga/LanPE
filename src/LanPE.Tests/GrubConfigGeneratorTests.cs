using LanPE.Build;
using LanPE.Core;
using LanPE.Core.Manifest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

[TestClass]
public class GrubConfigGeneratorTests
{
    private static Manifest BuildManifest() => new()
    {
        SchemaVersion = 1,
        Name = "LanPE",
        Version = "1.0.0",
        Components =
        {
            new Component
            {
                Id = "win11pe", Name = "Windows 11 PE", Kind = "winpe",
                Boot = new BootInfo { Mode = "wimboot", Path = "pe/win11pe/win11pe.wim", WimIndex = 2 }
            },
            new Component
            {
                Id = "win10pe", Name = "Windows 10 PE", Kind = "winpe",
                Boot = new BootInfo { Mode = "wimboot", Path = "pe/win10pe/win10pe.wim", WimIndex = 2 }
            },
            new Component
            {
                Id = "linux-rescue", Name = "SystemRescue", Kind = "linux",
                Boot = new BootInfo { Mode = "loopback", Path = "iso/systemrescue.iso" }
            }
        }
    };

    private static Selection SelectAll(Manifest m)
    {
        var s = new Selection();
        foreach (var c in m.Components) s.For(c.Id).Selected = true;
        return s;
    }

    [TestMethod]
    public void Generate_AllSelected_ContainsAllEntries()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()));

        StringAssert.Contains(cfg, "Windows 11 PE");
        StringAssert.Contains(cfg, "Windows 10 PE");
        StringAssert.Contains(cfg, "SystemRescue");
        StringAssert.Contains(cfg, "pe/win11pe/win11pe.wim");
        StringAssert.Contains(cfg, "pe/win10pe/win10pe.wim");
        StringAssert.Contains(cfg, "iso/systemrescue.iso");
    }

    [TestMethod]
    public void Generate_WimBoot_UsesLinuxefiAndInitrdefi()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()));

        // UEFI 分支
        StringAssert.Contains(cfg, "linuxefi /boot/wimboot index=2");
        StringAssert.Contains(cfg, "initrdefi newc:bcd:(/boot/bcd)");
        StringAssert.Contains(cfg, "newc:boot.sdi:(/boot/boot.sdi)");
        StringAssert.Contains(cfg, "newc:boot.wim:(/pe/win11pe/win11pe.wim)");
        // BIOS 分支
        StringAssert.Contains(cfg, "linux16 /boot/wimboot index=2");
        StringAssert.Contains(cfg, "initrd16 newc:bcd:(/boot/bcd)");
        StringAssert.Contains(cfg, "newc:bootmgr:(/boot/bootmgr)");
    }

    [TestMethod]
    public void Generate_WimBoot_RespectsProvidedBootFilePaths()
    {
        var bootFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wimboot"] = "custom/wimboot",
            ["bcd"] = "custom/my.bcd"
        };

        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()), bootFiles);
        StringAssert.Contains(cfg, "linuxefi /custom/wimboot index=2");
        StringAssert.Contains(cfg, "newc:bcd:(/custom/my.bcd)");
    }

    [TestMethod]
    public void Generate_SearchMarkerPresent()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()));
        StringAssert.Contains(cfg, "/LanPE/marker");
        StringAssert.Contains(cfg, "search --no-floppy --set=root --file");
    }

    [TestMethod]
    public void Generate_Linux_UsesLoopback()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()));
        StringAssert.Contains(cfg, "loopback loop /iso/systemrescue.iso");
        StringAssert.Contains(cfg, "img_loop=/iso/systemrescue.iso");
    }

    [TestMethod]
    public void Generate_OnlyWin11_ExcludesOthers()
    {
        var m = BuildManifest();
        var sel = new Selection();
        sel.For("win11pe").Selected = true;

        var cfg = GrubConfigGenerator.Generate(m, sel);
        StringAssert.Contains(cfg, "Windows 11 PE");
        Assert.IsFalse(cfg.Contains("Windows 10 PE"), "未选中的组件不应出现。");
        Assert.IsFalse(cfg.Contains("SystemRescue"), "未选中的组件不应出现。");
    }

    [TestMethod]
    public void Generate_NoSelection_EmitsPlaceholder()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), new Selection());
        StringAssert.Contains(cfg, "未选择任何组件");
    }

    [TestMethod]
    public void Generate_UsesGrubPlatformBranch()
    {
        var cfg = GrubConfigGenerator.Generate(BuildManifest(), SelectAll(BuildManifest()));
        StringAssert.Contains(cfg, "$grub_platform");
    }
}
