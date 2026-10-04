using System.IO;
using Microsoft.Win32;
using SfUi.Presentation;

namespace SfUi.App.Services;

/// <summary>WPF（Microsoft.Win32）のファイル ダイアログを使う IFilePickerService 実装。UI スレッドから呼ばれる前提。</summary>
public sealed class WpfFilePickerService : IFilePickerService
{
    public Task<string?> OpenFileAsync(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> SaveFileAsync(string title, string suggestedFileName, string filter, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedFileName,
            Filter = filter,
            AddExtension = true,
        };
        var extension = Path.GetExtension(suggestedFileName);
        if (!string.IsNullOrEmpty(extension))
        {
            dialog.DefaultExt = extension.TrimStart('.');
        }

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }
}
