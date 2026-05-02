namespace S3Browser.Models;

public sealed class ConnectionProfile
{
    public CredentialMode Mode { get; set; } = CredentialMode.AwsLogin;
    public string Region { get; set; } = "ap-northeast-1";
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }

    // CredentialMode.AccessKey
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? SessionToken { get; set; }

    // CredentialMode.AwsProfile
    public string? AwsProfileName { get; set; }

    // CredentialMode.AwsLogin
    public string? AwsLoginCacheFile { get; set; }
    public string? AwsLoginProfile { get; set; }
}
