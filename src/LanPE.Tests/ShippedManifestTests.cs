using System;
using System.IO;
using LanPE.Core.Manifest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LanPE.Tests
{
    /// <summary>
    /// 保证仓库中真实发布的清单与生成模板始终符合 schema（防止 CI 发布坏清单）。
    /// </summary>
    [TestClass]
    public class ShippedManifestTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "release")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "未能定位仓库根（含 release/ 的目录）。");
            return dir.FullName;
        }

        [TestMethod]
        public void ShippedManifest_ParsesAndValidates()
        {
            string path = Path.Combine(RepoRoot(), "release", "manifest.json");
            if (!File.Exists(path))
                Assert.Inconclusive("尚未生成 release/manifest.json，跳过。");

            var m = ManifestParser.ParseFile(path);
            Assert.IsTrue(m.Components.Count >= 1);
            Assert.IsNotNull(m.FindComponent("win11pe"));
            Assert.IsNotNull(m.FindComponent("linux-rescue"));
        }
    }
}
