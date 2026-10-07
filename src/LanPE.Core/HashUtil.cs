using System.Security.Cryptography;

namespace LanPE.Core;

public static class HashUtil
{
    public static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexStringLower(sha.ComputeHash(fs));
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        const int bufferSize = 1 << 20;
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

        var buffer = new byte[bufferSize];
        int read;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            sha.TransformBlock(buffer, 0, read, null, 0);

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    public static bool Equals(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>校验文件 sha256；期望值为空视为跳过校验，返回 true。</summary>
    public static async Task<bool> VerifyAsync(string path, string? expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        if (!File.Exists(path)) return false;
        var actual = await Sha256FileAsync(path, ct).ConfigureAwait(false);
        return Equals(actual, expectedSha256);
    }
}
