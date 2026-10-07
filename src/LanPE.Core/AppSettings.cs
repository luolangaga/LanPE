using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanPE.Core;

public enum ManifestSourceKind
{
    LatestRelease,
    CustomUrl,
    LocalFile
}

/// <summary>应用设置（持久化到 %LocalAppData%\LanPE\settings.json）。</summary>
public sealed class AppSettings
{
    public string RepoOwner { get; set; } = "luolangaga";
    public string RepoName { get; set; } = "LanPE";
    public ManifestSourceKind SourceKind { get; set; } = ManifestSourceKind.LatestRelease;
    public string CustomManifestUrl { get; set; } = "";
    public string LocalManifestPath { get; set; } = "";
    public string GitHubToken { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public bool PreferCachedAssets { get; set; } = true;

    public string RepoSlug => $"{RepoOwner}/{RepoName}";

    [JsonIgnore]
    public bool HasToken => !string.IsNullOrWhiteSpace(GitHubToken);

    public static AppSettings Load()
    {
        try
        {
            var path = AppPaths.SettingsFile;
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings);
                if (s != null) return s;
            }
        }
        catch { /* 回退到默认 */ }
        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile,
            JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
