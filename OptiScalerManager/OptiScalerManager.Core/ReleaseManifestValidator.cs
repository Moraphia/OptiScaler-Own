using System.Text.Json;

namespace OptiScalerManager.Core;

public static class ReleaseManifestValidator
{
    public static void Validate(string manifestText, ReleaseInfo release, string actualSha256)
    {
        using var document = JsonDocument.Parse(manifestText);
        var root = document.RootElement;
        var channel = root.GetProperty("releaseChannel").GetString();
        var type = root.GetProperty("packageType").GetString();
        var expected = root.GetProperty("packageSha256").GetString();
        if (!string.Equals(channel, "own", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release channel must be own.");
        if (!string.Equals(type, release.PackageType, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release package type does not match the selected download.");
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64 || !expected.All(Uri.IsHexDigit) ||
            !string.Equals(expected, actualSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package SHA256 does not match the release manifest.");
    }
}
