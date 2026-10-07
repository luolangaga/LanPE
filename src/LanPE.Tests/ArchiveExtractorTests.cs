using System.IO.Compression;
using LanPE.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests;

[TestClass]
public class ArchiveExtractorTests
{
    [TestMethod]
    public async Task ExtractZip_ExtractsFiles()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        string outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("sub/hello.txt");
                using var w = new StreamWriter(e.Open());
                w.Write("hi");
            }

            await ArchiveExtractor.ExtractAsync(zipPath, outDir, "zip", null, null, default);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "sub", "hello.txt")));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    [TestMethod]
    public async Task ExtractZip_RejectsZipSlip()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        string outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("../evil.txt");
                using var w = new StreamWriter(e.Open());
                w.Write("bad");
            }

            await Assert.ThrowsExceptionAsync<IOException>(
                () => ArchiveExtractor.ExtractAsync(zipPath, outDir, "zip", null, null, default));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    [TestMethod]
    public async Task ExtractNone_CopiesSingleFile()
    {
        string src = Path.GetTempFileName();
        string outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            File.WriteAllText(src, "payload");
            await ArchiveExtractor.ExtractAsync(src, outDir, "none", null, null, default);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, Path.GetFileName(src))));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }
}
