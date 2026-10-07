using System.Text.Json.Serialization;

namespace LanPE.Core.Manifest;

/// <summary>发布清单根对象。</summary>
public sealed class Manifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("releaseTag")]
    public string? ReleaseTag { get; set; }

    [JsonPropertyName("generatedAt")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("toolchain")]
    public AssetRef? Toolchain { get; set; }

    [JsonPropertyName("components")]
    public List<Component> Components { get; set; } = new();

    [JsonPropertyName("bootFiles")]
    public List<AssetRef> BootFiles { get; set; } = new();

    /// <summary>清单来源 URL（运行时填写，不参与序列化）。</summary>
    [JsonIgnore]
    public string? SourceUrl { get; set; }

    public Component? FindComponent(string? id)
    {
        if (string.IsNullOrEmpty(id) || Components.Count == 0) return null;
        foreach (var c in Components)
            if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    public AssetRef? FindBootFile(string? id)
    {
        if (string.IsNullOrEmpty(id) || BootFiles.Count == 0) return null;
        foreach (var b in BootFiles)
            if (string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }
}

/// <summary>一个可下载资产。</summary>
public sealed class AssetRef
{
    /// <summary>逻辑标识（如 wimboot、win11pe）；BootFiles 用此字段识别用途。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    public override string ToString() => File ?? Url ?? Id ?? "(asset)";
}

public sealed class Component
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>winpe | linux</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("defaultSelected")]
    public bool DefaultSelected { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, AssetRef> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("boot")]
    public BootInfo Boot { get; set; } = new();

    [JsonPropertyName("options")]
    public List<ComponentOption> Options { get; set; } = new();

    [JsonPropertyName("software")]
    public List<SoftwarePackage> Software { get; set; } = new();

    public bool IsWindowsPe => string.Equals(Kind, "winpe", StringComparison.OrdinalIgnoreCase);
    public bool IsLinux => string.Equals(Kind, "linux", StringComparison.OrdinalIgnoreCase);
}

public sealed class BootInfo
{
    /// <summary>wimboot | loopback</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "wimboot";

    /// <summary>wimboot：wim 相对介质根的路径；loopback：iso 路径。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>wimboot：WIM 内部镜像索引。</summary>
    [JsonPropertyName("wimIndex")]
    public int WimIndex { get; set; } = 2;
}

public sealed class ComponentOption
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>bool | choice | string</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "bool";

    [JsonPropertyName("default")]
    public string? Default { get; set; }

    [JsonPropertyName("choices")]
    public List<string> Choices { get; set; } = new();
}

public sealed class SoftwarePackage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("defaultSelected")]
    public bool DefaultSelected { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>zip | 7z | none</summary>
    [JsonPropertyName("archive")]
    public string Archive { get; set; } = "zip";

    [JsonPropertyName("extractTo")]
    public string? ExtractTo { get; set; }

    public AssetRef ToAssetRef() => new()
    {
        Id = Id,
        File = File,
        Url = Url,
        Sha256 = Sha256,
        SizeBytes = SizeBytes
    };
}
