using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S3Browser.Models;
using S3Browser.Services;

namespace S3Browser.ViewModels;

public partial class ConnectionDialogViewModel : ObservableObject
{
    public const string DefaultProfileName = "default";

    public ConnectionDialogViewModel()
    {
        AwsLoginCacheEntries = new ObservableCollection<AwsLoginCacheEntry>();
        LoginProfiles = new ObservableCollection<AwsConfigProfile>();
        RefreshAwsLoginCacheCommand = new RelayCommand(RefreshAwsLoginCache);
        RefreshLoginProfilesCommand = new RelayCommand(RefreshLoginProfiles);
        RunAwsLoginCommand = new AsyncRelayCommand(RunAwsLoginAsync, () => !IsAwsLoginRunning);
        RefreshAwsLoginCache();
        RefreshLoginProfiles();
    }

    [ObservableProperty]
    private CredentialMode mode = CredentialMode.AwsLogin;

    [ObservableProperty]
    private string region = "ap-northeast-1";

    [ObservableProperty]
    private string awsProfileName = string.Empty;

    public ObservableCollection<AwsLoginCacheEntry> AwsLoginCacheEntries { get; }

    [ObservableProperty]
    private AwsLoginCacheEntry? selectedAwsLoginEntry;

    [ObservableProperty]
    private string awsLoginCacheStatus = string.Empty;

    [ObservableProperty]
    private string awsLoginProfile = string.Empty;

    partial void OnAwsLoginProfileChanged(string value) => ApplyProfileSelection();

    [ObservableProperty]
    private bool isAwsLoginRunning;

    partial void OnIsAwsLoginRunningChanged(bool value) => RunAwsLoginCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    private string accessKey = string.Empty;

    [ObservableProperty]
    private string secretKey = string.Empty;

    [ObservableProperty]
    private string sessionToken = string.Empty;

    [ObservableProperty]
    private string serviceUrl = string.Empty;

    [ObservableProperty]
    private bool forcePathStyle;

    public ObservableCollection<AwsConfigProfile> LoginProfiles { get; }

    public IRelayCommand RefreshAwsLoginCacheCommand { get; }
    public IRelayCommand RefreshLoginProfilesCommand { get; }
    public IAsyncRelayCommand RunAwsLoginCommand { get; }

    public IReadOnlyList<string> Regions { get; } =
    [
        "us-east-1", "us-east-2", "us-west-1", "us-west-2",
        "ap-northeast-1", "ap-northeast-2", "ap-northeast-3",
        "ap-south-1", "ap-southeast-1", "ap-southeast-2",
        "ca-central-1",
        "eu-central-1", "eu-west-1", "eu-west-2", "eu-west-3", "eu-north-1",
        "sa-east-1",
    ];

    private async Task RunAwsLoginAsync()
    {
        IsAwsLoginRunning = true;
        AwsLoginCacheStatus = "aws login を起動中... コンソールウィンドウでサインインを完了してください。";
        try
        {
            var exitCode = await AwsLoginRunner.RunAsync(NormalizedAwsLoginProfile()).ConfigureAwait(true);
            AwsLoginCacheStatus = exitCode == 0
                ? "aws login が正常に完了しました。"
                : $"aws login が終了コード {exitCode} で終了しました。";
            RefreshAwsLoginCache();
        }
        catch (Exception ex)
        {
            AwsLoginCacheStatus = $"aws login の実行に失敗: {ex.Message}";
            MessageBox.Show(ex.Message, "aws login", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsAwsLoginRunning = false;
        }
    }

    private void RefreshLoginProfiles()
    {
        var prev = AwsLoginProfile;
        LoginProfiles.Clear();
        var profiles = AwsConfigReader.ReadLoginProfiles();
        foreach (var p in profiles) LoginProfiles.Add(p);

        if (string.IsNullOrEmpty(prev))
        {
            if (profiles.Any(p => p.Name == DefaultProfileName))
                AwsLoginProfile = DefaultProfileName;
            else if (profiles.Count > 0)
                AwsLoginProfile = profiles[0].Name;
        }
    }

    private void RefreshAwsLoginCache()
    {
        AwsLoginCacheEntries.Clear();
        var entries = AwsLoginCacheReader.ScanCache();
        foreach (var e in entries) AwsLoginCacheEntries.Add(e);

        AwsLoginCacheStatus = entries.Count == 0
            ? $"キャッシュなし: {AwsLoginCacheReader.DefaultCacheDir}"
            : $"{entries.Count} 件のキャッシュを検出";

        ApplyProfileSelection();
    }

    /// <summary>
    /// 選択中のプロファイル名を ~/.aws/config の login_session に解決し、
    /// それと一致する LoginSession を持つキャッシュを SelectedAwsLoginEntry に設定する。
    /// </summary>
    private void ApplyProfileSelection()
    {
        var loginSession = LoginProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, NormalizedAwsLoginProfile(), StringComparison.Ordinal))?.LoginSession;

        SelectedAwsLoginEntry = loginSession is not null
            ? AwsLoginCacheEntries.FirstOrDefault(e => string.Equals(e.LoginSession, loginSession, StringComparison.Ordinal))
            : null;
    }

    private string NormalizedAwsLoginProfile() =>
        string.IsNullOrWhiteSpace(AwsLoginProfile) ? DefaultProfileName : AwsLoginProfile.Trim();

    public ConnectionProfile ToProfile() => new()
    {
        Mode = Mode,
        Region = string.IsNullOrWhiteSpace(Region) ? "ap-northeast-1" : Region,
        AwsProfileName = Mode == CredentialMode.AwsProfile && !string.IsNullOrWhiteSpace(AwsProfileName) ? AwsProfileName : null,
        AwsLoginCacheFile = Mode == CredentialMode.AwsLogin ? SelectedAwsLoginEntry?.FilePath : null,
        AwsLoginProfile = Mode == CredentialMode.AwsLogin ? NormalizedAwsLoginProfile() : null,
        AccessKey = Mode == CredentialMode.AccessKey ? AccessKey : null,
        SecretKey = Mode == CredentialMode.AccessKey ? SecretKey : null,
        SessionToken = Mode == CredentialMode.AccessKey && !string.IsNullOrWhiteSpace(SessionToken) ? SessionToken : null,
        ServiceUrl = string.IsNullOrWhiteSpace(ServiceUrl) ? null : ServiceUrl,
        ForcePathStyle = ForcePathStyle,
    };

    public void LoadFrom(ConnectionProfile profile)
    {
        Mode = profile.Mode;
        Region = profile.Region;
        AwsProfileName = profile.AwsProfileName ?? string.Empty;
        AccessKey = profile.AccessKey ?? string.Empty;
        SecretKey = profile.SecretKey ?? string.Empty;
        SessionToken = profile.SessionToken ?? string.Empty;
        ServiceUrl = profile.ServiceUrl ?? string.Empty;
        ForcePathStyle = profile.ForcePathStyle;

        if (Mode == CredentialMode.AwsLogin && profile.AwsLoginCacheFile is not null)
        {
            SelectedAwsLoginEntry = AwsLoginCacheEntries
                .FirstOrDefault(e => e.FilePath == profile.AwsLoginCacheFile);
        }
    }
}
