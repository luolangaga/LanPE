using System.IO.Compression;

namespace LanPE.Build;

/// <summary>
/// 定位和管理外部工具链（从 tools.zip 解包到 %LocalAppData%\LanPE\tools）。
/// </summary>
public sealed class Toolchain
{
    public string RootDir { get; }
    public string? SevenZipPath { get; private set; }
    public string? XorrisoPath { get; private set; }
    public string? GrubMkrescuePath { get; private set; }
    public string? WimlibPath { get; private set; }

    public Toolchain(string rootDir) => RootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));

    public bool IsReady => XorrisoPath != null || GrubMkrescuePath != null;

    /// <summary>从 tools.zip 解包并定位各工具。</summary>
    public void Prepare(string? toolsZipPath)
    {
        if (!string.IsNullOrWhiteSpace(toolsZipPath))
        {
            if (!File.Exists(toolsZipPath))
                throw new FileNotFoundException("找不到工具链压缩包。", toolsZipPath);

            if (Directory.Exists(RootDir))
            {
                try { Directory.Delete(RootDir, true); } catch { /* 忽略 */ }
            }
            Directory.CreateDirectory(RootDir);
            ZipFile.ExtractToDirectory(toolsZipPath, RootDir);
        }

        Locate();
    }

    /// <summary>在已解包的目录中定位各工具。</summary>
    public void Locate()
    {
        SevenZipPath = Find("7za.exe", "7z.exe");
        XorrisoPath = Find("xorriso.exe", "xorriso");
        GrubMkrescuePath = Find("grub-mkrescue", "grub-mkrescue.exe", "grub-mkrescue.bat", "grub-mkrescue.sh");
        WimlibPath = Find("wimlib-imagex.exe", "wimlib-imagex");
    }

    private string? Find(params string[] names)
    {
        if (!Directory.Exists(RootDir)) return null;
        foreach (var name in names)
        {
            var hit = Directory.EnumerateFiles(RootDir, name, SearchOption.AllDirectories).FirstOrDefault();
            if (hit != null) return hit;
        }
        return null;
    }
}
