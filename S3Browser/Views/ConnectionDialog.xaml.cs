using System.Windows;
using S3Browser.Models;
using S3Browser.ViewModels;

namespace S3Browser.Views;


public partial class ConnectionDialog : Window
{
    private readonly ConnectionDialogViewModel _vm;

    public ConnectionProfile? Result { get; private set; }

    public ConnectionDialog(ConnectionProfile? initial = null)
    {
        InitializeComponent();
        _vm = new ConnectionDialogViewModel();
        if (initial is not null)
        {
            _vm.LoadFrom(initial);
            SecretBox.Password = initial.SecretKey ?? string.Empty;
        }
        DataContext = _vm;
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        switch (_vm.Mode)
        {
            case CredentialMode.AccessKey:
                _vm.SecretKey = SecretBox.Password;
                if (string.IsNullOrWhiteSpace(_vm.AccessKey) || string.IsNullOrWhiteSpace(_vm.SecretKey))
                {
                    MessageBox.Show(this, "アクセスキーIDとシークレットアクセスキーを入力してください。",
                        "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                break;

            case CredentialMode.AwsProfile:
                if (string.IsNullOrWhiteSpace(_vm.AwsProfileName))
                {
                    MessageBox.Show(this, "AWSプロファイル名を入力してください。",
                        "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                break;

            case CredentialMode.AwsLogin:
                if (!await EnsureAwsLoginCredentialsAsync()) return;
                break;
        }

        Result = _vm.ToProfile();
        DialogResult = true;
        Close();
    }

    private async Task<bool> EnsureAwsLoginCredentialsAsync()
    {
        var entry = _vm.SelectedAwsLoginEntry;
        if (entry is not null && !entry.IsExpired) return true;

        IsEnabled = false;
        try
        {
            await _vm.RunAwsLoginCommand.ExecuteAsync(null);
        }
        finally
        {
            IsEnabled = true;
        }

        entry = _vm.SelectedAwsLoginEntry;
        if (entry is null || entry.IsExpired)
        {
            MessageBox.Show(this,
                "aws login を実行しましたが、有効な認証情報を取得できませんでした。",
                "aws login", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
