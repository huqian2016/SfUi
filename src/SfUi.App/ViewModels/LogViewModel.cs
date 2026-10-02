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
    private string _statusText = UiText.T("Log_Initial");

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
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        IsBusy = true;
        StatusText = UiText.T("Log_LoadingList");
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
                ? UiText.T("Apex_NoOrgLogs")
                : UiText.T("Log_CountFmt", Logs.Count);
            _log.Info($"ログ一覧を取得: {Logs.Count} 件");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Log_ListFailedFmt", ex.Message);
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
            StatusText = UiText.T("Log_SelectToFetch");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        IsBusy = true;
        StatusText = UiText.T("Log_FetchingFmt", selected.Id);
        try
        {
            var text = await _service.GetLogAsync(CurrentOrg, selected.Id);
            LogContent = text ?? UiText.T("Log_Unavailable");
            StatusText = UiText.T("Log_FetchedFmt", LogContent.Length);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Log_FetchFailedFmt", ex.Message);
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
            StatusText = UiText.T("Log_NoLogToSave");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = UiText.T("Log_SaveTitle"),
            FileName = $"{SelectedLog?.Id ?? "apex"}-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = UiText.T("Log_SaveFilter"),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, LogContent, new UTF8Encoding(false));
            StatusText = UiText.T("Common_SavedFmt", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_SaveFailedFmt", ex.Message);
        }
    }
}
