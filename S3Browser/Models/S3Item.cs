using CommunityToolkit.Mvvm.ComponentModel;

namespace S3Browser.Models;

public partial class S3Item : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string key = string.Empty;

    [ObservableProperty]
    private string bucketName = string.Empty;

    [ObservableProperty]
    private long size;

    [ObservableProperty]
    private DateTime? lastModified;

    [ObservableProperty]
    private S3ItemType itemType;

    [ObservableProperty]
    private string? storageClass;

    public bool IsDirectoryLike => ItemType is S3ItemType.Bucket or S3ItemType.Folder or S3ItemType.ParentFolder;

    public string TypeDisplay => ItemType switch
    {
        S3ItemType.Bucket => "バケット",
        S3ItemType.Folder => "フォルダー",
        S3ItemType.ParentFolder => "上の階層",
        S3ItemType.File => System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? $"{ext} ファイル" : "ファイル",
        _ => string.Empty
    };

    public string SizeDisplay => ItemType == S3ItemType.File ? FormatSize(Size) : string.Empty;

    public string ModifiedDisplay => LastModified?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? string.Empty;

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N2} {units[unit]}";
    }
}
