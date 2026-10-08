using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>デバッグログタブの ViewModel。</summary>
public partial class LogViewModel : ObservableObject
{
    private readonly ApexService _service;
    private readonly IFilePickerService _filePicker;
    private readonly IAppWindowService _windowService;
    private readonly AppLog _log;

    /// <summary>現在表示中のログの出所（ログ Id or ファイル名）。</summary>
    private string? _contentSourceLabel;

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

    public LogViewModel(ApexService service, IFilePickerService filePicker, IAppWindowService windowService, AppLog log)
    {
        _service = service;
        _filePicker = filePicker;
        _windowService = windowService;
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
            _contentSourceLabel = selected.Id;
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

    /// <summary>ローカルの .log ファイルを読み込み、本文表示 + 解析してウィンドウを開く。</summary>
    [RelayCommand]
    private async Task OpenLogFileAsync()
    {
        var fileName = await _filePicker.OpenFileAsync(UiText.T("LogAnalyze_OpenTitle"), UiText.T("LogAnalyze_FileFilter"));
        if (fileName is null)
        {
            return;
        }

        try
        {
            LogContent = await File.ReadAllTextAsync(fileName);
            _contentSourceLabel = Path.GetFileName(fileName);
            StatusText = UiText.T("LogAnalyze_OpenedFmt", fileName, LogContent.Length);
            await AnalyzeCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("LogAnalyze_ParseFailedFmt", ex.Message);
            _log.Error("ログファイルの読み込みに失敗", ex);
        }
    }

    /// <summary>現在表示中のログ本文を解析して解析ウィンドウを開く。</summary>
    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrEmpty(LogContent))
        {
            StatusText = UiText.T("LogAnalyze_NoContent");
            return;
        }

        await AnalyzeCoreAsync();
    }

    private async Task AnalyzeCoreAsync()
    {
        if (string.IsNullOrEmpty(LogContent))
        {
            StatusText = UiText.T("LogAnalyze_NoContent");
            return;
        }

        IsBusy = true;
        StatusText = UiText.T("LogAnalyze_ParsingFmt", LogContent.Length);
        try
        {
            var text = LogContent;
            var analysis = await Task.Run(() => DebugLogParser.Parse(text));
            _windowService.OpenLogAnalyzer(analysis, CurrentOrg ?? "-", _contentSourceLabel ?? "-");
            StatusText = UiText.T("LogAnalyze_DoneFmt", analysis.Summary.EventCount);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("LogAnalyze_ParseFailedFmt", ex.Message);
            _log.Error("ログの解析に失敗", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        if (string.IsNullOrEmpty(LogContent))
        {
            StatusText = UiText.T("Log_NoLogToSave");
            return;
        }

        var fileName = await _filePicker.SaveFileAsync(
            UiText.T("Log_SaveTitle"),
            $"{SelectedLog?.Id ?? "apex"}-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            UiText.T("Log_SaveFilter"));
        if (fileName is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(fileName, LogContent, new UTF8Encoding(false));
            StatusText = UiText.T("Common_SavedFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_SaveFailedFmt", ex.Message);
        }
    }
}
