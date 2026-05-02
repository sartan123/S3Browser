using S3Browser.Models;

namespace S3Browser.Services;

public sealed record CallerIdentity(string Account, string UserId, string Arn);

public interface IS3Service : IDisposable
{
    bool IsConnected { get; }
    ConnectionProfile? CurrentProfile { get; }

    void Connect(ConnectionProfile profile);
    void Disconnect();
    void SetReauthHandler(ReauthHandler? handler);

    Task<CallerIdentity> GetCallerIdentityAsync(CancellationToken ct = default);
    Task<IReadOnlyList<S3Item>> ListBucketsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<S3Item>> ListAsync(S3Location location, CancellationToken ct = default);

    Task DownloadFileAsync(string bucket, string key, string localPath, IProgress<long>? progress = null, CancellationToken ct = default);
    Task UploadFileAsync(string bucket, string key, string localPath, IProgress<long>? progress = null, CancellationToken ct = default);
    Task DeleteObjectAsync(string bucket, string key, CancellationToken ct = default);
    Task DeleteFolderAsync(string bucket, string prefix, CancellationToken ct = default);
    Task CreateFolderAsync(string bucket, string prefix, CancellationToken ct = default);
    Task CreateBucketAsync(string bucket, CancellationToken ct = default);
    Task DeleteBucketAsync(string bucket, CancellationToken ct = default);
    Task RenameAsync(string bucket, string oldKey, string newKey, CancellationToken ct = default);
}
