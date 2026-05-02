using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using S3Browser.Models;
using S3Browser.Services;

namespace S3Browser.ViewModels;

public partial class TreeNodeViewModel : ObservableObject
{
    private readonly IS3Service _service;
    private readonly Func<string, Task> _onError;
    private bool _childrenLoaded;

    public TreeNodeViewModel(
        IS3Service service,
        string name,
        S3Location location,
        S3ItemType type,
        Func<string, Task> onError)
    {
        _service = service;
        _onError = onError;
        Name = name;
        Location = location;
        ItemType = type;
        Children = new ObservableCollection<TreeNodeViewModel>();
        if (CanExpand)
            Children.Add(CreatePlaceholder());
    }

    private TreeNodeViewModel()
    {
        _service = null!;
        _onError = _ => Task.CompletedTask;
        Name = "読み込み中...";
        Location = S3Location.Root;
        ItemType = S3ItemType.Folder;
        Children = new ObservableCollection<TreeNodeViewModel>();
    }

    private static TreeNodeViewModel CreatePlaceholder() => new();

    public string Name { get; }
    public S3Location Location { get; }
    public S3ItemType ItemType { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }

    public bool CanExpand => ItemType is S3ItemType.Bucket or S3ItemType.Folder;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
        {
            _ = LoadChildrenAsync();
        }
    }

    public async Task LoadChildrenAsync()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;

        try
        {
            var items = await _service.ListAsync(Location).ConfigureAwait(true);
            Children.Clear();
            foreach (var item in items.Where(i => i.ItemType == S3ItemType.Folder))
            {
                Children.Add(new TreeNodeViewModel(
                    _service,
                    item.Name,
                    new S3Location(item.BucketName, item.Key),
                    S3ItemType.Folder,
                    _onError));
            }
        }
        catch (Exception ex)
        {
            Children.Clear();
            _childrenLoaded = false;
            await _onError($"フォルダーの読み込みに失敗しました: {ex.Message}");
        }
    }

}
