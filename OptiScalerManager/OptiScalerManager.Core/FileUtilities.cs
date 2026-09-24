using System.Security.Cryptography;
using System.Text.Json;

namespace OptiScalerManager.Core;

public static class FileUtilities
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static bool IsGameDirectory(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).Any();

    public static string SafeGameId(string path) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..16];

    public static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "package.zip" : name;
    }

    public static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temp, path, true);
    }
}
