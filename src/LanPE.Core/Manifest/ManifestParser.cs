using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanPE.Core.Manifest;

/// <summary>
/// System.Text.Json 源生成上下文。AOT 必需——禁用反射序列化后，
/// 必须由此上下文生成类型元数据。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(AssetRef))]
[JsonSerializable(typeof(Component))]
[JsonSerializable(typeof(BootInfo))]
[JsonSerializable(typeof(ComponentOption))]
[JsonSerializable(typeof(SoftwarePackage))]
internal partial class ManifestJsonContext : JsonSerializerContext;

public sealed class ManifestValidationException : Exception
{
    public ManifestValidationException(string message) : base(message) { }
}

/// <summary>清单解析与校验。</summary>
public static class ManifestParser
{
    public const int SupportedSchemaVersion = 1;

    public static Manifest Parse(string json, string? sourceUrl = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ManifestValidationException("清单内容为空。");

        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest);
        }
        catch (JsonException ex)
        {
            throw new ManifestValidationException("清单 JSON 解析失败：" + ex.Message);
        }

        if (manifest == null)
            throw new ManifestValidationException("清单 JSON 解析结果为空。");

        manifest.SourceUrl = sourceUrl;
        Validate(manifest);
        return manifest;
    }

    public static Manifest ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到清单文件。", path);
        return Parse(File.ReadAllText(path), path);
    }

    public static void Validate(Manifest m)
    {
        var errors = new List<string>();

        if (m.SchemaVersion <= 0)
            errors.Add("缺少或非法的 schemaVersion。");
        else if (m.SchemaVersion > SupportedSchemaVersion)
            errors.Add($"清单 schemaVersion={m.SchemaVersion} 高于本程序支持的 {SupportedSchemaVersion}。");

        if (string.IsNullOrWhiteSpace(m.Name))
            errors.Add("缺少 name。");

        if (m.Components.Count == 0)
            errors.Add("components 为空。");
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < m.Components.Count; i++)
            {
                var c = m.Components[i];
                string tag = $"components[{i}]";

                if (string.IsNullOrWhiteSpace(c.Id))
                    errors.Add($"{tag} 缺少 id。");
                else if (!seen.Add(c.Id!))
                    errors.Add($"{tag} 的 id 重复：{c.Id}");

                if (string.IsNullOrWhiteSpace(c.Name))
                    errors.Add($"{tag} 缺少 name。");

                if (!c.IsWindowsPe && !c.IsLinux)
                    errors.Add($"{tag} 的 kind 非法（应为 winpe 或 linux）：{c.Kind}");

                if (c.Payload.Count == 0)
                    errors.Add($"{tag} 缺少 payload。");
                else
                {
                    foreach (var kv in c.Payload)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Value.Url))
                            errors.Add($"{tag}.payload[{kv.Key}] 缺少 url。");
                    }
                }

                if (c.Boot is null)
                    errors.Add($"{tag} 缺少 boot。");

                for (int j = 0; j < c.Software.Count; j++)
                {
                    var s = c.Software[j];
                    if (string.IsNullOrWhiteSpace(s.Id))
                        errors.Add($"{tag}.software[{j}] 缺少 id。");
                    if (string.IsNullOrWhiteSpace(s.Url))
                        errors.Add($"{tag}.software[{j}] 缺少 url。");
                }
            }
        }

        if (errors.Count > 0)
            throw new ManifestValidationException(
                "清单校验未通过：" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
