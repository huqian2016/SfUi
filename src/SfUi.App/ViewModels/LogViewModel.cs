using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>デバッグログタブの ViewModel。</summary>
public partial class LogViewModel : ObservableObject
{
    private readonly ApexService _service;
    private readonly AppLog _log;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "「⟳ 一覧更新」でログ一覧を取得します";

    [ObservableProperty]
    private ApexLogInfo? _selectedLog;

    [ObservableProperty]
    private string? _logContent;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    public ObservableCollection<ApexLogInfo> Logs { get; } = new();

    public LogViewModel(ApexService service, AppLog log)
    {
        _service = service;
        _log = log;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        IsBusy = true;
        StatusText = "ログ一覧を取得中…";
        try
        {
            var logs = await _service.ListLogsAsync(CurrentOrg);

            Logs.Clear();
            foreach (var item in logs)
            {
                Logs.Add(item);
            }

            SelectedLog = Logs.FirstOrDefault();
            StatusText = Logs.Count == 0
                ? "org に保存されたデバッグログがありません（Setup の Debug Logs / sf apex tail log で作成できます）"
                : $"ログ {Logs.Count} 件";
            _log.Info($"ログ一覧を取得: {Logs.Count} 件");
        }
        catch (Exception ex)
        {
            StatusText = $"ログ一覧の取得に失敗: {ex.Message}";
            _log.Error("ログ一覧の取得に失敗", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FetchAsync()
    {
        if (SelectedLog is not { } selected)
        {
            StatusText = "取得するログを選択してください";
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = "上部バーで組織を選択してください";
            return;
        }

        IsBusy = true;
        StatusText = $"ログ {selected.Id} を取得中…";
        try
        {
            var text = await _service.GetLogAsync(CurrentOrg, selected.Id);
            LogContent = text ?? "(内容を取得できませんでした)";
            StatusText = $"取得完了（{LogContent.Length:N0} 文字）";
        }
        catch (Exception ex)
        {
            StatusText = $"ログの取得に失敗: {ex.Message}";
            _log.Error("ログの取得に失敗", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveLog()
    {
        if (string.IsNullOrEmpty(LogContent))
        {
            StatusText = "保存するログがありません";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "デバッグログを保存",
            FileName = $"{SelectedLog?.Id ?? "apex"}-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "ログ ファイル (*.log)|*.log|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, LogContent, new UTF8Encoding(false));
            StatusText = $"保存: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"保存に失敗: {ex.Message}";
        }
    }
}
