using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using S3Browser.Models;
using S3Browser.Services;
using S3Browser.Views;

namespace S3Browser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IS3Service _service;
    private readonly Stack<S3Location> _backStack = new();
    private readonly Stack<S3Location> _forwardStack = new();

    public MainViewModel(IS3Service service)
    {
        _service = service;
        Items = new ObservableCollection<S3Item>();
        TreeRoots = new ObservableCollection<TreeNodeViewModel>();
        SelectedItems = new ObservableCollection<S3Item>();
        CurrentLocation = S3Location.Root;
        AddressBarText = CurrentLocation.DisplayPath;
        StatusText = "未接続";
    }

    public ObservableCollection<S3Item> Items { get; }
    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; }
    public ObservableCollection<S3Item> SelectedItems { get; }

    [ObservableProperty]
    private S3Location currentLocation;

    [ObservableProperty]
    private string addressBarText;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string currentUserDisplay = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private string progressText = string.Empty;

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp => !CurrentLocation.IsRoot;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var dialog = new ConnectionDialog(_service.CurrentProfile)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } profile) return;

        try
        {
            _service.Connect(profile);
            IsConnected = true;
            CurrentUserDisplay = "認証情報を確認中...";
            StatusText = $"接続済み ({profile.Region})";
            _backStack.Clear();
            _forwardStack.Clear();

            _ = LoadCallerIdentityAsync();
            await NavigateToAsync(S3Location.Root, addToHistory: false);
            // ルート遷移で取得済みのバケット一覧を再利用してツリーを構築する。
            await LoadTreeRootsAsync(Items.ToList());
        }
        catch (Exception ex)
        {
            ShowError($"接続に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _service.Disconnect();
        IsConnected = false;
        CurrentUserDisplay = string.Empty;
        Items.Clear();
        TreeRoots.Clear();
        _backStack.Clear();
        _forwardStack.Clear();
        CurrentLocation = S3Location.Root;
        AddressBarText = CurrentLocation.DisplayPath;
        StatusText = "未接続";
        NotifyNav();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_service.IsConnected) return;
        await NavigateToAsync(CurrentLocation, addToHistory: false);
    }

    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        if (!CanGoUp) return;
        await NavigateToAsync(CurrentLocation.Parent());
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (!CanGoBack) return;
        var prev = _backStack.Pop();
        _forwardStack.Push(CurrentLocation);
        await NavigateToAsync(prev, addToHistory: false);
        NotifyNav();
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (!CanGoForward) return;
        var next = _forwardStack.Pop();
        _backStack.Push(CurrentLocation);
        await NavigateToAsync(next, addToHistory: false);
        NotifyNav();
    }

    [RelayCommand]
    private async Task NavigateToAddressAsync()
    {
        if (!_service.IsConnected) return;
        if (S3Location.TryParse(AddressBarText ?? string.Empty, out var loc))
            await NavigateToAsync(loc);
    }

    [RelayCommand]
    private async Task OpenItemAsync(S3Item? item)
    {
        if (item is null) return;
        switch (item.ItemType)
        {
            case S3ItemType.Bucket:
                await NavigateToAsync(new S3Location(item.Name, string.Empty));
                break;
            case S3ItemType.Folder:
                await NavigateToAsync(new S3Location(item.BucketName, item.Key));
                break;
            case S3ItemType.ParentFolder:
                await NavigateUpAsync();
                break;
            case S3ItemType.File:
                await DownloadItemAsync(item);
                break;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedItems.Count == 0) return;
        var files = SelectedItems.Where(i => i.ItemType == S3ItemType.File).ToList();
        if (files.Count == 0) return;

        var dlg = new OpenFolderDialog { Title = "保存先フォルダーを選択" };
        if (dlg.ShowDialog() != true) return;
        var dir = dlg.FolderName;

        IsProgressVisible = true;
        try
        {
            int done = 0;
            foreach (var f in files)
            {
                ProgressText = $"ダウンロード中 ({++done}/{files.Count}): {f.Name}";
                ProgressValue = 0;
                var local = Path.Combine(dir, f.Name);
                var size = (double)Math.Max(1, f.Size);
                var progress = new Progress<long>(b => ProgressValue = b / size * 100);
                await _service.DownloadFileAsync(f.BucketName, f.Key, local, progress);
            }
            StatusText = $"{files.Count}件をダウンロードしました";
        }
        catch (Exception ex)
        {
            ShowError($"ダウンロードに失敗しました: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
            ProgressValue = 0;
        }
    }

    private async Task DownloadItemAsync(S3Item item)
    {
        var dlg = new SaveFileDialog
        {
            FileName = item.Name,
            Title = "ダウンロード先を選択",
        };
        if (dlg.ShowDialog() != true) return;

        IsProgressVisible = true;
        ProgressText = $"ダウンロード中: {item.Name}";
        try
        {
            var size = (double)Math.Max(1, item.Size);
            var progress = new Progress<long>(b => ProgressValue = b / size * 100);
            await _service.DownloadFileAsync(item.BucketName, item.Key, dlg.FileName, progress);
            StatusText = $"ダウンロード完了: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowError($"ダウンロードに失敗しました: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
            ProgressValue = 0;
        }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (CurrentLocation.IsRoot)
        {
            ShowError("バケットを選択してからアップロードしてください。");
            return;
        }

        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "アップロードするファイルを選択",
        };
        if (dlg.ShowDialog() != true) return;

        await UploadFilesAsync(dlg.FileNames);
    }

    public async Task UploadFilesAsync(IEnumerable<string> paths)
    {
        if (CurrentLocation.IsRoot || CurrentLocation.Bucket is null)
        {
            ShowError("バケット内に移動してからアップロードしてください。");
            return;
        }

        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;

        IsProgressVisible = true;
        try
        {
            int done = 0;
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                var key = CurrentLocation.Prefix + name;
                ProgressText = $"アップロード中 ({++done}/{files.Count}): {name}";
                ProgressValue = 0;
                var info = new FileInfo(path);
                var size = (double)Math.Max(1, info.Length);
                var progress = new Progress<long>(b => ProgressValue = b / size * 100);
                await _service.UploadFileAsync(CurrentLocation.Bucket, key, path, progress);
            }
            StatusText = $"{files.Count}件をアップロードしました";
            await NavigateToAsync(CurrentLocation, addToHistory: false);
        }
        catch (Exception ex)
        {
            ShowError($"アップロードに失敗しました: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
            ProgressValue = 0;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedItems.Count == 0) return;
        var targets = SelectedItems.Where(i => i.ItemType != S3ItemType.ParentFolder).ToList();
        if (targets.Count == 0) return;

        var msg = targets.Count == 1
            ? $"'{targets[0].Name}' を削除します。よろしいですか?"
            : $"選択した {targets.Count} 件を削除します。よろしいですか?";
        var confirm = MessageBox.Show(msg, "削除の確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        try
        {
            foreach (var item in targets)
            {
                switch (item.ItemType)
                {
                    case S3ItemType.File:
                        await _service.DeleteObjectAsync(item.BucketName, item.Key);
                        break;
                    case S3ItemType.Folder:
                        await _service.DeleteFolderAsync(item.BucketName, item.Key);
                        break;
                    case S3ItemType.Bucket:
                        await _service.DeleteBucketAsync(item.Name);
                        break;
                }
            }
            StatusText = $"{targets.Count}件を削除しました";
            await NavigateToAsync(CurrentLocation, addToHistory: false);
            if (CurrentLocation.IsRoot) await LoadTreeRootsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"削除に失敗しました: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (CurrentLocation.IsRoot || CurrentLocation.Bucket is null)
        {
            ShowError("バケット内に移動してからフォルダーを作成してください。");
            return;
        }

        var name = InputBox.Show("新しいフォルダー名:", "新規フォルダー", "新しいフォルダー");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim().Trim('/');

        try
        {
            await _service.CreateFolderAsync(CurrentLocation.Bucket, CurrentLocation.Prefix + name);
            StatusText = $"フォルダーを作成しました: {name}";
            await NavigateToAsync(CurrentLocation, addToHistory: false);
        }
        catch (Exception ex)
        {
            ShowError($"フォルダー作成に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NewBucketAsync()
    {
        if (!_service.IsConnected) return;
        var name = InputBox.Show("バケット名 (グローバルにユニーク):", "新規バケット", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _service.CreateBucketAsync(name.Trim());
            StatusText = $"バケットを作成しました: {name}";
            await LoadTreeRootsAsync();
            if (CurrentLocation.IsRoot)
                await NavigateToAsync(CurrentLocation, addToHistory: false);
        }
        catch (Exception ex)
        {
            ShowError($"バケット作成に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedItems.Count != 1) return;
        var item = SelectedItems[0];
        if (item.ItemType != S3ItemType.File) return;

        var newName = InputBox.Show("新しい名前:", "名前の変更", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        var idx = item.Key.LastIndexOf('/');
        var newKey = idx < 0 ? newName : item.Key[..(idx + 1)] + newName;

        try
        {
            await _service.RenameAsync(item.BucketName, item.Key, newKey);
            StatusText = $"名前を変更しました: {item.Name} → {newName}";
            await NavigateToAsync(CurrentLocation, addToHistory: false);
        }
        catch (Exception ex)
        {
            ShowError($"名前の変更に失敗しました: {ex.Message}");
        }
    }

    public async Task NavigateToAsync(S3Location location, bool addToHistory = true)
    {
        if (!_service.IsConnected) return;

        if (addToHistory && !CurrentLocation.Equals(location))
        {
            _backStack.Push(CurrentLocation);
            _forwardStack.Clear();
        }

        CurrentLocation = location;
        AddressBarText = location.DisplayPath;
        IsBusy = true;
        StatusText = "読み込み中...";
        try
        {
            var items = await _service.ListAsync(location);
            Items.Clear();
            if (!location.IsRoot)
            {
                Items.Add(new S3Item
                {
                    Name = "..",
                    ItemType = S3ItemType.ParentFolder,
                    BucketName = location.Bucket ?? string.Empty,
                });
            }
            foreach (var item in items.OrderByDescending(i => i.IsDirectoryLike).ThenBy(i => i.Name))
                Items.Add(item);

            var fileCount = items.Count(i => i.ItemType == S3ItemType.File);
            var folderCount = items.Count(i => i.ItemType is S3ItemType.Folder or S3ItemType.Bucket);
            StatusText = $"{folderCount} フォルダー, {fileCount} ファイル";
        }
        catch (Exception ex)
        {
            ShowError($"一覧の取得に失敗しました: {ex.Message}");
            StatusText = "エラー";
        }
        finally
        {
            IsBusy = false;
            NotifyNav();
        }
    }

    private async Task LoadTreeRootsAsync(IReadOnlyList<S3Item>? prefetchedBuckets = null)
    {
        TreeRoots.Clear();
        try
        {
            var buckets = prefetchedBuckets ?? await _service.ListBucketsAsync();
            foreach (var b in buckets)
            {
                TreeRoots.Add(new TreeNodeViewModel(
                    _service,
                    b.Name,
                    new S3Location(b.Name, string.Empty),
                    S3ItemType.Bucket,
                    msg => { ShowError(msg); return Task.CompletedTask; }));
            }
        }
        catch (Exception ex)
        {
            ShowError($"バケット一覧の取得に失敗しました: {ex.Message}");
        }
    }

    private async Task LoadCallerIdentityAsync()
    {
        try
        {
            var identity = await _service.GetCallerIdentityAsync();
            var name = ExtractFriendlyName(identity.Arn);
            CurrentUserDisplay = string.IsNullOrEmpty(name)
                ? $"acct {identity.Account}"
                : $"{name} (acct {identity.Account})";
        }
        catch (Exception ex)
        {
            CurrentUserDisplay = $"認証情報の取得に失敗: {ex.Message}";
        }
    }

    private static string ExtractFriendlyName(string arn)
    {
        if (string.IsNullOrEmpty(arn)) return string.Empty;
        var slash = arn.LastIndexOf('/');
        return slash >= 0 ? arn[(slash + 1)..] : arn;
    }

    private void NotifyNav()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    private static void ShowError(string msg)
    {
        MessageBox.Show(Application.Current.MainWindow!, msg, "S3 Browser", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
