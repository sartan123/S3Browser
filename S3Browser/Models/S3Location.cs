namespace S3Browser.Models;

public sealed record S3Location(string? Bucket, string Prefix)
{
    public static readonly S3Location Root = new(null, string.Empty);

    public bool IsRoot => Bucket is null;

    public string DisplayPath => IsRoot
        ? "S3://"
        : string.IsNullOrEmpty(Prefix) ? $"S3://{Bucket}/" : $"S3://{Bucket}/{Prefix}";

    public S3Location Parent()
    {
        if (IsRoot) return this;
        if (string.IsNullOrEmpty(Prefix)) return Root;
        var trimmed = Prefix.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx < 0
            ? new S3Location(Bucket, string.Empty)
            : new S3Location(Bucket, trimmed[..(idx + 1)]);
    }

    public S3Location Child(string folderName) =>
        new(Bucket, string.IsNullOrEmpty(Prefix) ? folderName + "/" : Prefix + folderName + "/");

    public static bool TryParse(string text, out S3Location location)
    {
        location = Root;
        if (string.IsNullOrWhiteSpace(text))
        {
            location = Root;
            return true;
        }

        var s = text.Trim();
        if (s.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            s = s[5..];
        else if (s.StartsWith("S3://", StringComparison.Ordinal))
            s = s[5..];

        s = s.TrimStart('/');
        if (string.IsNullOrEmpty(s))
        {
            location = Root;
            return true;
        }

        var slashIdx = s.IndexOf('/');
        if (slashIdx < 0)
        {
            location = new S3Location(s, string.Empty);
            return true;
        }

        var bucket = s[..slashIdx];
        var prefix = s[(slashIdx + 1)..];
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        location = new S3Location(bucket, prefix);
        return true;
    }
}
