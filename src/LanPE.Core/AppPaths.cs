namespace LanPE.Core;

/// <summary>应用路径与临时目录管理。</summary>
public static class AppPaths
{
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanPE");

    public static string CacheDir => Path.Combine(DataRoot, "cache");
    public static string ToolsDir => Path.Combine(DataRoot, "tools");
    public static string StagingDir => Path.Combine(DataRoot, "staging");
    public static string LogsDir => Path.Combine(DataRoot, "logs");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ToolsDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>清理暂存目录（每次组装前重置）。</summary>
    public static void ResetStaging()
    {
        if (Directory.Exists(StagingDir))
        {
            try { Directory.Delete(StagingDir, true); }
            catch { /* 忽略被占用的文件 */ }
        }
        Directory.CreateDirectory(StagingDir);
    }

    /// <summary>把文件名安全化为可作单目录名的字符串。</summary>
    public static string SafeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "_";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace('\\', '_').Replace('/', '_');
    }
}
