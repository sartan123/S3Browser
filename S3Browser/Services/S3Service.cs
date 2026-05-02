using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using S3Browser.Models;
using S3Location = S3Browser.Models.S3Location;

namespace S3Browser.Services;

public delegate Task<bool> ReauthHandler(string? profileName, CancellationToken ct);

public sealed class S3Service : IS3Service
{
    private static readonly HashSet<string> AuthErrorCodes = new(StringComparer.Ordinal)
    {
        "ExpiredToken", "ExpiredTokenException", "InvalidToken",
        "TokenRefreshRequired", "RequestExpired", "InvalidClientTokenId",
        "UnrecognizedClientException", "SignatureDoesNotMatch",
    };

    private AmazonS3Client? _client;
    private TransferUtility? _transfer;
    private AmazonSecurityTokenServiceClient? _sts;
    private AWSCredentials? _credentials;
    private RegionEndpoint? _region;
    private DateTime? _credentialsExpiresAt;
    private ReauthHandler? _reauthHandler;
    private readonly SemaphoreSlim _reauthLock = new(1, 1);

    public bool IsConnected => _client is not null;
    public ConnectionProfile? CurrentProfile { get; private set; }

    public void SetReauthHandler(ReauthHandler? handler) => _reauthHandler = handler;

    public void Connect(ConnectionProfile profile)
    {
        DisposeClients();

        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(profile.Region),
        };
        if (!string.IsNullOrWhiteSpace(profile.ServiceUrl))
        {
            config.ServiceURL = profile.ServiceUrl;
            config.ForcePathStyle = profile.ForcePathStyle;
        }

        var (credentials, expiresAt) = ResolveCredentials(profile);

        _client = credentials is null
            ? new AmazonS3Client(config)
            : new AmazonS3Client(credentials, config);
        _transfer = new TransferUtility(_client);
        _credentials = credentials;
        _region = config.RegionEndpoint;
        _credentialsExpiresAt = expiresAt;
        CurrentProfile = profile;
    }

    private static (AWSCredentials? Credentials, DateTime? ExpiresAt) ResolveCredentials(ConnectionProfile profile)
    {
        switch (profile.Mode)
        {
            case CredentialMode.AwsLogin:
                if (string.IsNullOrWhiteSpace(profile.AwsLoginCacheFile))
                    throw new InvalidOperationException("aws login のキャッシュファイルが指定されていません。");
                var entry = AwsLoginCacheReader.TryRead(profile.AwsLoginCacheFile)
                    ?? throw new InvalidOperationException(
                        $"aws login キャッシュを読み込めませんでした: {profile.AwsLoginCacheFile}");
                AWSCredentials loginCreds = string.IsNullOrEmpty(entry.SessionToken)
                    ? new BasicAWSCredentials(entry.AccessKey, entry.SecretKey)
                    : new SessionAWSCredentials(entry.AccessKey, entry.SecretKey, entry.SessionToken);
                return (loginCreds, entry.ExpiresAt);

            case CredentialMode.AccessKey:
                if (string.IsNullOrWhiteSpace(profile.AccessKey) || string.IsNullOrWhiteSpace(profile.SecretKey))
                    throw new InvalidOperationException("アクセスキーまたはシークレットキーが指定されていません。");
                AWSCredentials akCreds = string.IsNullOrWhiteSpace(profile.SessionToken)
                    ? new BasicAWSCredentials(profile.AccessKey, profile.SecretKey)
                    : new SessionAWSCredentials(profile.AccessKey, profile.SecretKey, profile.SessionToken);
                return (akCreds, null);

            case CredentialMode.AwsProfile:
                if (string.IsNullOrWhiteSpace(profile.AwsProfileName))
                    throw new InvalidOperationException("AWSプロファイル名が指定されていません。");
                var chain = new CredentialProfileStoreChain();
                if (chain.TryGetAWSCredentials(profile.AwsProfileName, out var profileCreds))
                    return (profileCreds, null);
                throw new InvalidOperationException($"AWSプロファイル '{profile.AwsProfileName}' が見つかりません。");

            case CredentialMode.DefaultChain:
            default:
                return (null, null);
        }
    }

    public void Disconnect()
    {
        DisposeClients();
        CurrentProfile = null;
    }

    private void DisposeClients()
    {
        _transfer?.Dispose();
        _client?.Dispose();
        _sts?.Dispose();
        _transfer = null;
        _client = null;
        _sts = null;
        _credentials = null;
        _region = null;
        _credentialsExpiresAt = null;
    }

    public void Dispose()
    {
        Disconnect();
        _reauthLock.Dispose();
    }

    private AmazonS3Client Client => _client ?? throw new InvalidOperationException("S3に接続されていません。");
    private TransferUtility Transfer => _transfer ?? throw new InvalidOperationException("S3に接続されていません。");

    private AmazonSecurityTokenServiceClient Sts
    {
        get
        {
            if (_client is null) throw new InvalidOperationException("S3に接続されていません。");
            if (_sts is null)
            {
                var stsConfig = new AmazonSecurityTokenServiceConfig
                {
                    RegionEndpoint = _region ?? RegionEndpoint.USEast1,
                };
                _sts = _credentials is null
                    ? new AmazonSecurityTokenServiceClient(stsConfig)
                    : new AmazonSecurityTokenServiceClient(_credentials, stsConfig);
            }
            return _sts;
        }
    }

    /// <summary>
    /// 操作を実行する。事前にキャッシュ期限を確認し、期限切れなら再認証を要求する。
    /// 操作中に認証エラーが発生した場合も一度だけ再認証を試行して再実行する。
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        if (IsCachedCredentialsExpired())
            await TryReauthAsync(ct).ConfigureAwait(false);

        try
        {
            return await op(ct).ConfigureAwait(false);
        }
        catch (AmazonServiceException ex) when (IsAuthError(ex))
        {
            if (await TryReauthAsync(ct).ConfigureAwait(false))
                return await op(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> op, CancellationToken ct)
        => await ExecuteAsync<int>(async c => { await op(c).ConfigureAwait(false); return 0; }, ct).ConfigureAwait(false);

    private bool IsCachedCredentialsExpired() =>
        _credentialsExpiresAt is { } e && e <= DateTime.UtcNow.AddSeconds(30);

    private async Task<bool> TryReauthAsync(CancellationToken ct)
    {
        if (_reauthHandler is null || CurrentProfile is null || string.IsNullOrEmpty(CurrentProfile.AwsLoginCacheFile))
            return false;

        await _reauthLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 別の呼び出しが既に再認証を済ませているかもしれない。Connect 後の expiry がまだ有効ならスキップ。
            if (!IsCachedCredentialsExpired()) return true;

            var success = await _reauthHandler(CurrentProfile.AwsLoginProfile, ct).ConfigureAwait(false);
            if (success)
            {
                Connect(CurrentProfile);
                return true;
            }
            return false;
        }
        finally
        {
            _reauthLock.Release();
        }
    }

    private static bool IsAuthError(AmazonServiceException ex) =>
        ex.ErrorCode is not null && AuthErrorCodes.Contains(ex.ErrorCode);

    public Task<CallerIdentity> GetCallerIdentityAsync(CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            var resp = await Sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), c).ConfigureAwait(false);
            return new CallerIdentity(resp.Account ?? string.Empty, resp.UserId ?? string.Empty, resp.Arn ?? string.Empty);
        }, ct);

    public Task<IReadOnlyList<S3Item>> ListBucketsAsync(CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            var response = await Client.ListBucketsAsync(c).ConfigureAwait(false);
            IReadOnlyList<S3Item> items = response.Buckets?.Select(b => new S3Item
            {
                Name = b.BucketName,
                Key = string.Empty,
                BucketName = b.BucketName,
                ItemType = S3ItemType.Bucket,
                LastModified = b.CreationDate,
            }).ToList() ?? [];
            return items;
        }, ct);

    public Task<IReadOnlyList<S3Item>> ListAsync(S3Location location, CancellationToken ct = default) =>
        location.IsRoot
            ? ListBucketsAsync(ct)
            : ExecuteAsync<IReadOnlyList<S3Item>>(async c =>
            {
                var items = new List<S3Item>();
                string? continuationToken = null;
                do
                {
                    var request = new ListObjectsV2Request
                    {
                        BucketName = location.Bucket,
                        Prefix = location.Prefix,
                        Delimiter = "/",
                        ContinuationToken = continuationToken,
                        MaxKeys = 1000,
                    };
                    var response = await Client.ListObjectsV2Async(request, c).ConfigureAwait(false);

                    if (response.CommonPrefixes is not null)
                    {
                        foreach (var cp in response.CommonPrefixes)
                        {
                            var name = cp.TrimEnd('/');
                            var lastSlash = name.LastIndexOf('/');
                            if (lastSlash >= 0) name = name[(lastSlash + 1)..];
                            items.Add(new S3Item
                            {
                                Name = name,
                                Key = cp,
                                BucketName = location.Bucket!,
                                ItemType = S3ItemType.Folder,
                            });
                        }
                    }

                    if (response.S3Objects is not null)
                    {
                        foreach (var obj in response.S3Objects)
                        {
                            if (obj.Key == location.Prefix) continue;
                            if (obj.Key.EndsWith('/') && obj.Size == 0) continue;

                            var name = obj.Key;
                            if (!string.IsNullOrEmpty(location.Prefix) && name.StartsWith(location.Prefix))
                                name = name[location.Prefix.Length..];

                            items.Add(new S3Item
                            {
                                Name = name,
                                Key = obj.Key,
                                BucketName = location.Bucket!,
                                ItemType = S3ItemType.File,
                                Size = obj.Size ?? 0,
                                LastModified = obj.LastModified,
                                StorageClass = obj.StorageClass?.Value,
                            });
                        }
                    }

                    continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
                } while (continuationToken is not null);

                return items;
            }, ct);

    public Task DownloadFileAsync(string bucket, string key, string localPath, IProgress<long>? progress = null, CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            var request = new TransferUtilityDownloadRequest
            {
                BucketName = bucket,
                Key = key,
                FilePath = localPath,
            };
            if (progress is not null)
            {
                request.WriteObjectProgressEvent += (_, e) => progress.Report(e.TransferredBytes);
            }
            await Transfer.DownloadAsync(request, c).ConfigureAwait(false);
        }, ct);

    public Task UploadFileAsync(string bucket, string key, string localPath, IProgress<long>? progress = null, CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            var request = new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                Key = key,
                FilePath = localPath,
            };
            if (progress is not null)
            {
                request.UploadProgressEvent += (_, e) => progress.Report(e.TransferredBytes);
            }
            await Transfer.UploadAsync(request, c).ConfigureAwait(false);
        }, ct);

    public Task DeleteObjectAsync(string bucket, string key, CancellationToken ct = default) =>
        ExecuteAsync(c => Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucket,
            Key = key,
        }, c), ct);

    public Task DeleteFolderAsync(string bucket, string prefix, CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            string? continuationToken = null;
            do
            {
                var listRequest = new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = prefix,
                    ContinuationToken = continuationToken,
                    MaxKeys = 1000,
                };
                var listResponse = await Client.ListObjectsV2Async(listRequest, c).ConfigureAwait(false);
                if (listResponse.S3Objects is { Count: > 0 } objs)
                {
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = bucket,
                        Objects = objs.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                        Quiet = true,
                    };
                    await Client.DeleteObjectsAsync(deleteRequest, c).ConfigureAwait(false);
                }
                continuationToken = listResponse.IsTruncated == true ? listResponse.NextContinuationToken : null;
            } while (continuationToken is not null);
        }, ct);

    public Task CreateFolderAsync(string bucket, string prefix, CancellationToken ct = default) =>
        ExecuteAsync(c => Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = prefix.EndsWith('/') ? prefix : prefix + "/",
            ContentBody = string.Empty,
        }, c), ct);

    public Task CreateBucketAsync(string bucket, CancellationToken ct = default) =>
        ExecuteAsync(c => Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = bucket,
        }, c), ct);

    public Task DeleteBucketAsync(string bucket, CancellationToken ct = default) =>
        ExecuteAsync(c => Client.DeleteBucketAsync(new DeleteBucketRequest
        {
            BucketName = bucket,
        }, c), ct);

    public Task RenameAsync(string bucket, string oldKey, string newKey, CancellationToken ct = default) =>
        ExecuteAsync(async c =>
        {
            await Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucket,
                SourceKey = oldKey,
                DestinationBucket = bucket,
                DestinationKey = newKey,
            }, c).ConfigureAwait(false);

            await Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = oldKey,
            }, c).ConfigureAwait(false);
        }, ct);
}
