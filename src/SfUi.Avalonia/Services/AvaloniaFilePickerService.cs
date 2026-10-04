using System.IO;
using Avalonia.Platform.Storage;
using SfUi.Presentation;

namespace SfUi.Avalonia.Services;

/// <summary>Avalonia の StorageProvider を使う IFilePickerService 実装。</summary>
public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private readonly TopLevelAccessor _top;

    public AvaloniaFilePickerService(TopLevelAccessor top) => _top = top;

    public async Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null)
    {
        var storage = _top.Current?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(filter),
            SuggestedStartLocation = await ResolveStartAsync(storage, initialDirectory),
        };
        var files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null)
    {
        var storage = _top.Current?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = ParseFilter(filter),
            SuggestedStartLocation = await ResolveStartAsync(storage, initialDirectory),
        };
        var extension = Path.GetExtension(suggestedFileName);
        if (!string.IsNullOrEmpty(extension))
        {
            options.DefaultExtension = extension.TrimStart('.');
        }

        var file = await storage.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
    {
        var storage = _top.Current?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await ResolveStartAsync(storage, initialDirectory),
        };
        var folders = await storage.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>WPF 形式（"CSV file (*.csv)|*.csv|All files (*.*)|*.*"）のフィルタを Avalonia 形式へ変換する。</summary>
    private static List<FilePickerFileType> ParseFilter(string filter)
    {
        var result = new List<FilePickerFileType>();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return result;
        }

        var parts = filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var label = parts[i].Trim();
            var patterns = parts[i + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => string.Equals(p, "*.*", StringComparison.Ordinal) ? "*" : p)
                .ToList();
            if (patterns.Count == 0)
            {
                continue;
            }

            result.Add(new FilePickerFileType(label) { Patterns = patterns });
        }

        return result;
    }

    private static async Task<IStorageFolder?> ResolveStartAsync(IStorageProvider storage, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return await storage.TryGetFolderFromPathAsync(directory);
        }
        catch
        {
            return null;
        }
    }
}
