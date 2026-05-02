using System.IO;

namespace S3Browser.Services;

public sealed record AwsConfigProfile(string Name, IReadOnlyDictionary<string, string> Properties)
{
    public bool HasLoginSession => Properties.ContainsKey("login_session");
    public string? LoginSession => Properties.TryGetValue("login_session", out var v) ? v : null;
    public string? Region => Properties.TryGetValue("region", out var v) ? v : null;
}

/// <summary>
/// `~/.aws/config` の INI 形式を読み取り、プロファイル一覧を返す。
/// </summary>
public static class AwsConfigReader
{
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws", "config");

    public static IReadOnlyList<AwsConfigProfile> ReadProfiles(string? path = null)
    {
        var configPath = path ?? DefaultConfigPath;
        if (!File.Exists(configPath)) return [];

        var profiles = new List<AwsConfigProfile>();
        string? currentName = null;
        Dictionary<string, string>? currentProps = null;

        foreach (var raw in File.ReadAllLines(configPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                FlushCurrent(profiles, currentName, currentProps);
                currentName = null;
                currentProps = null;

                var header = line[1..^1].Trim();
                if (string.Equals(header, "default", StringComparison.Ordinal))
                {
                    currentName = "default";
                }
                else if (header.StartsWith("profile ", StringComparison.Ordinal))
                {
                    currentName = header[8..].Trim();
                }
                // Skip [sso-session ...], [services ...], etc.
                if (currentName is not null) currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (currentProps is null) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            currentProps[key] = value;
        }

        FlushCurrent(profiles, currentName, currentProps);
        return profiles;
    }

    public static IReadOnlyList<AwsConfigProfile> ReadLoginProfiles(string? path = null) =>
        ReadProfiles(path).Where(p => p.HasLoginSession).ToList();

    private static void FlushCurrent(List<AwsConfigProfile> profiles, string? name, Dictionary<string, string>? props)
    {
        if (name is not null && props is not null)
            profiles.Add(new AwsConfigProfile(name, props));
    }
}
