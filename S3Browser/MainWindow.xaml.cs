using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using S3Browser.Models;
using S3Browser.Services;
using S3Browser.ViewModels;

namespace S3Browser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IS3Service _service;

    public MainWindow()
    {
        InitializeComponent();
        _service = new S3Service();
        _service.SetReauthHandler(ReauthAsync);
        _vm = new MainViewModel(_service);
        DataContext = _vm;
        Closed += (_, _) => _service.Dispose();
        Loaded += (_, _) => _vm.ConnectCommand.Execute(null);
    }

    private async Task<bool> ReauthAsync(string? profileName, CancellationToken ct)
    {
        try
        {
            await Dispatcher.InvokeAsync(() => _vm.StatusText = $"認証情報の有効期限が切れました。aws login を再実行中... (profile: {profileName ?? "default"})");
            var exitCode = await AwsLoginRunner.RunAsync(profileName, ct).ConfigureAwait(false);
            if (exitCode != 0)
            {
                await Dispatcher.InvokeAsync(() => _vm.StatusText = $"aws login が終了コード {exitCode} で終了しました。");
                return false;
            }
            await Dispatcher.InvokeAsync(() => _vm.StatusText = "再認証が完了しました。");
            return true;
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => _vm.StatusText = $"再認証に失敗: {ex.Message}");
            return false;
        }
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.NavigateToAddressCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is S3Item item)
            _vm.OpenItemCommand.Execute(item);
    }

    private void OnContextOpen(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is S3Item item)
            _vm.OpenItemCommand.Execute(item);
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedItems.Clear();
        foreach (var item in ItemsList.SelectedItems)
        {
            if (item is S3Item s) _vm.SelectedItems.Add(s);
        }
    }

    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNodeViewModel node)
        {
            await _vm.NavigateToAsync(node.Location);
        }
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm.IsConnected && !_vm.CurrentLocation.IsRoot
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (System.IO.File.Exists(p)) files.Add(p);
        }
        if (files.Count > 0)
            await _vm.UploadFilesAsync(files);
    }
}
