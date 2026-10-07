using System.Text.Json;
using LanPE.Core.Manifest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

[TestClass]
public class ManifestParserTests
{
    private const string ValidJson = """
    {
      "schemaVersion": 1,
      "name": "LanPE",
      "version": "1.0.0",
      "releaseTag": "v1.0.0",
      "toolchain": { "file": "lanpe-tools.zip", "url": "https://x/lanpe-tools.zip", "sha256": "aa", "sizeBytes": 10 },
      "bootFiles": [
        { "id": "wimboot", "file": "wimboot", "url": "https://x/wimboot", "sha256": "ff", "sizeBytes": 1 }
      ],
      "components": [
        {
          "id": "win11pe", "name": "Windows 11 PE", "kind": "winpe", "version": "24H2", "arch": "x64",
          "defaultSelected": true,
          "payload": { "wim": { "file": "win11pe.wim", "url": "https://x/win11pe.wim", "sha256": "bb", "sizeBytes": 100 } },
          "boot": { "mode": "wimboot", "path": "pe/win11pe/win11pe.wim", "wimIndex": 2 },
          "options": [ { "id": "ramdisk", "label": "RAMDisk", "type": "bool", "default": "true" } ],
          "software": [ { "id": "disk-tools", "name": "硬盘工具", "defaultSelected": true, "file": "apps/disk-tools.zip", "url": "https://x/apps/disk-tools.zip", "sha256": "cc", "sizeBytes": 50, "archive": "zip", "extractTo": "Apps/disk-tools" } ]
        },
        {
          "id": "linux-rescue", "name": "SystemRescue", "kind": "linux", "version": "11.03",
          "defaultSelected": true,
          "payload": { "iso": { "file": "systemrescue.iso", "url": "https://x/systemrescue.iso", "sha256": "dd", "sizeBytes": 900 } },
          "boot": { "mode": "loopback", "path": "iso/systemrescue.iso" }
        }
      ]
    }
    """;

    [TestMethod]
    public void Parse_ValidManifest_Succeeds()
    {
        var m = ManifestParser.Parse(ValidJson);
        Assert.AreEqual(1, m.SchemaVersion);
        Assert.AreEqual("LanPE", m.Name);
        Assert.AreEqual(2, m.Components.Count);
        Assert.IsNotNull(m.Toolchain);
        Assert.AreEqual(1, m.BootFiles.Count);
    }

    [TestMethod]
    public void Parse_BootFiles_LookupById()
    {
        var m = ManifestParser.Parse(ValidJson);
        Assert.IsNotNull(m.FindBootFile("wimboot"));
        Assert.IsNull(m.FindBootFile("nope"));
    }

    [TestMethod]
    public void Parse_ComponentLookup_IsCaseInsensitive()
    {
        var m = ManifestParser.Parse(ValidJson);
        Assert.IsNotNull(m.FindComponent("WIN11PE"));
        Assert.IsNotNull(m.FindComponent("linux-rescue"));
    }

    [TestMethod]
    public void Parse_WinPe_FlagsAndBoot()
    {
        var m = ManifestParser.Parse(ValidJson);
        var c = m.FindComponent("win11pe")!;
        Assert.IsTrue(c.IsWindowsPe);
        Assert.AreEqual("wimboot", c.Boot.Mode);
        Assert.AreEqual(2, c.Boot.WimIndex);
        Assert.AreEqual(1, c.Software.Count);
    }

    [TestMethod]
    public void Parse_Linux_IsLinux()
    {
        var m = ManifestParser.Parse(ValidJson);
        var c = m.FindComponent("linux-rescue")!;
        Assert.IsTrue(c.IsLinux);
        Assert.AreEqual("loopback", c.Boot.Mode);
    }

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_Empty_Throws() => ManifestParser.Parse("   ");

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_MissingComponents_Throws() =>
        ManifestParser.Parse(@"{ ""schemaVersion"": 1, ""name"": ""X"" }");

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_DuplicateComponentId_Throws() =>
        ManifestParser.Parse("""
        { "schemaVersion": 1, "name": "X", "components": [
          { "id": "a", "name": "A", "kind": "winpe", "payload": { "p": { "url": "u" } } },
          { "id": "A", "name": "B", "kind": "winpe", "payload": { "p": { "url": "u" } } } ] }
        """);

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_InvalidKind_Throws() =>
        ManifestParser.Parse("""
        { "schemaVersion": 1, "name": "X", "components": [
          { "id": "a", "name": "A", "kind": "weird", "payload": { "p": { "url": "u" } } } ] }
        """);

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_HigherSchemaVersion_Throws() =>
        ManifestParser.Parse("""
        { "schemaVersion": 999, "name": "X", "components": [
          { "id": "a", "name": "A", "kind": "winpe", "payload": { "p": { "url": "u" } } } ] }
        """);

    [TestMethod]
    [ExpectedException(typeof(ManifestValidationException))]
    public void Parse_MalformedJson_Throws() => ManifestParser.Parse("{ not json");
}
