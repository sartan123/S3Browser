using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace S3Browser.Services;

/// <summary>
/// `aws login` (AWS Sign-In) が ~/.aws/login/cache/ に保存する一時認証キャッシュを読み込む。
/// </summary>
public static class AwsLoginCacheReader
{
    public static string DefaultCacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws", "login", "cache");

    public static IReadOnlyList<AwsLoginCacheEntry> ScanCache(string? cacheDir = null)
    {
        var dir = cacheDir ?? DefaultCacheDir;
        if (!Directory.Exists(dir)) return [];

        var results = new List<AwsLoginCacheEntry>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var entry = TryRead(path);
            if (entry is not null) results.Add(entry);
        }
        return results
            .OrderByDescending(e => e.ExpiresAt ?? DateTime.MinValue)
            .ThenByDescending(e => File.GetLastWriteTimeUtc(e.FilePath))
            .ToList();
    }

    public static AwsLoginCacheEntry? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonSerializer.Deserialize<LoginCacheDocument>(stream, JsonOpts);
            var token = doc?.AccessToken;
            if (token is null
                || string.IsNullOrEmpty(token.AccessKeyId)
                || string.IsNullOrEmpty(token.SecretAccessKey))
                return null;

            return new AwsLoginCacheEntry
            {
                FilePath = path,
                AccessKey = token.AccessKeyId,
                SecretKey = token.SecretAccessKey,
                SessionToken = token.SessionToken,
                AccountId = token.AccountId,
                ExpiresAt = token.ExpiresAt,
                LoginSession = ExtractLoginSession(doc?.IdToken),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractLoginSession(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String)
                return sub.GetString();
        }
        catch
        {
            // not a parseable JWT — ignore
        }
        return null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class LoginCacheDocument
    {
        [JsonPropertyName("accessToken")]
        public LoginCacheAccessToken? AccessToken { get; set; }

        [JsonPropertyName("idToken")]
        public string? IdToken { get; set; }
    }

    private sealed class LoginCacheAccessToken
    {
        [JsonPropertyName("accessKeyId")]
        public string? AccessKeyId { get; set; }

        [JsonPropertyName("secretAccessKey")]
        public string? SecretAccessKey { get; set; }

        [JsonPropertyName("sessionToken")]
        public string? SessionToken { get; set; }

        [JsonPropertyName("accountId")]
        public string? AccountId { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }
}

public sealed class AwsLoginCacheEntry
{
    public required string FilePath { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public string? SessionToken { get; init; }
    public string? AccountId { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>idToken の sub クレーム — ~/.aws/config の login_session と突き合わせるためのキー。</summary>
    public string? LoginSession { get; init; }

    public bool IsExpired => ExpiresAt is { } e && e <= DateTime.UtcNow.AddSeconds(30);

    public string DisplayName
    {
        get
        {
            var fileName = Path.GetFileNameWithoutExtension(FilePath);
            var shortHash = fileName.Length > 12 ? fileName[..12] + "..." : fileName;
            var account = string.IsNullOrEmpty(AccountId) ? "(account 不明)" : $"acct {AccountId}";
            var expiry = ExpiresAt is { } e
                ? (IsExpired ? "期限切れ" : $"残り {(e - DateTime.UtcNow).TotalMinutes:N0} 分")
                : "有効期限不明";
            return $"{account} — {expiry} [{shortHash}]";
        }
    }
}
