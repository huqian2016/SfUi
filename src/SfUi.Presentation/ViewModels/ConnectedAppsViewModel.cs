using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>
/// 接続アプリ ウィンドウ。接続アプリ（ConnectedApp）と外部クライアント アプリ
/// （ExternalClientApplication）の一覧・詳細・作成・編集・削除を行う。
/// </summary>
public sealed partial class ConnectedAppsViewModel : ObservableObject
{
    private readonly IConnectedAppService _service;
    private readonly IConnectedAppEditorDialog _editor;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    private List<ConnectedAppSummary> _all = new();
    private int _listVersion;
    private int _busyCount;

    public ConnectedAppsViewModel(
        IConnectedAppService service,
        IConnectedAppEditorDialog editor,
        IDialogService dialogs,
        AppLog log)
    {
        _service = service;
        _editor = editor;
        _dialogs = dialogs;
        _log = log;
        Kinds = new[]
        {
            new ReportChoice(ConnectedAppKinds.ConnectedApp, UiText.T("Conn_KindConnectedApp")),
            new ReportChoice(ConnectedAppKinds.ExternalClientApp, UiText.T("Conn_KindEca")),
        };
        _selectedKind = Kinds[0];
    }

    /// <summary>種別の選択肢。</summary>
    public IReadOnlyList<ReportChoice> Kinds { get; }

    /// <summary>組織の選択肢。</summary>
    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    /// <summary>表示中の一覧（フィルタ適用後）。</summary>
    public ObservableCollection<ConnectedAppSummary> Apps { get; } = new();

    public string LabelOrg => UiText.T("Conn_LabelOrg");

    public string Title => UiText.T("Conn_Title");

    public string LabelKind => UiText.T("Conn_LabelKind");

    public string LabelSearch => UiText.T("Conn_LabelSearch");

    public string RefreshText => UiText.T("Conn_Refresh");

    public string NewText => UiText.T("Conn_New");

    public string EditText => UiText.T("Conn_Edit");

    public string DeleteText => UiText.T("Conn_Delete");

    public string OpenSetupText => UiText.T("Conn_OpenSetup");

    public string EmptyHint => UiText.T("Conn_EmptyHint");

    public string DetailTitle => UiText.T("Conn_DetailTitle");

    public string LabelCallbackUrlText => UiText.T("Conn_LabelCallbackUrl");

    public string LabelScopesText => UiText.T("Conn_LabelScopes");

    public string LabelConsumerKeyText => UiText.T("Conn_LabelConsumerKey");

    public string LabelIpRelaxationText => UiText.T("Conn_LabelIpRelaxation");

    public string LabelRefreshTokenPolicyText => UiText.T("Conn_LabelRefreshTokenPolicy");

    public string CreationRestrictedHint => UiText.T("Conn_CreationRestrictedHint");

    public string EcaRecommendHint => UiText.T("Conn_EcaRecommendHint");

    [ObservableProperty]
    private ReportChoice _selectedKind;

    [ObservableProperty]
    private OrgInfo? _selectedOrg;

    [ObservableProperty]
    private ConnectedAppSummary? _selectedApp;

    [ObservableProperty]
    private ConnectedAppDocument? _document;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Salesforce の作成制限エラーが発生した（案内を表示する）。</summary>
    [ObservableProperty]
    private bool _isCreationRestricted;

    public bool HasSelection => SelectedApp is not null;

    public bool HasDocument => Document is not null;

    public bool IsEca => string.Equals(SelectedKind?.Key, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase);

    /// <summary>従来の接続アプリを選択中か（ECA 推奨の案内を出す）。</summary>
    public bool IsConnectedAppKind => !IsEca;

    /// <summary>ウィンドウを開いたときの初期化。</summary>
    public void Initialize(IReadOnlyList<OrgInfo> orgs, OrgInfo? currentOrg)
    {
        Orgs.Clear();
        foreach (var org in orgs)
        {
            Orgs.Add(org);
        }

        SelectedOrg = currentOrg ?? orgs.FirstOrDefault();
        _ = RefreshAsync();
    }

    partial void OnSelectedOrgChanged(OrgInfo? value)
    {
        _ = RefreshAsync();
    }

    partial void OnSelectedKindChanged(ReportChoice value)
    {
        OnPropertyChanged(nameof(IsEca));
        OnPropertyChanged(nameof(IsConnectedAppKind));
        IsCreationRestricted = false;
        _ = RefreshAsync();
    }

    partial void OnSelectedAppChanged(ConnectedAppSummary? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        Document = null;
        OnPropertyChanged(nameof(HasDocument));
        if (value is not null)
        {
            _ = LoadDocumentAsync(value);
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedOrg is null)
        {
            return;
        }

        var org = SelectedOrg;
        var kind = SelectedKind?.Key ?? ConnectedAppKinds.ConnectedApp;
        var version = ++_listVersion;
        BeginBusy();
        StatusMessage = UiText.T("Conn_StatusLoading");
        try
        {
            var list = await _service.ListAsync(org.Username, kind, CancellationToken.None).ConfigureAwait(true);
            if (version != _listVersion)
            {
                // 古い取得結果は破棄する（デプロイ後の再読み込みが優先）
                return;
            }

            _all = list.ToList();
            ApplyFilter();
            SelectedApp = Apps.FirstOrDefault();
            StatusMessage = UiText.T("Conn_StatusLoadedFmt", _all.Count);
        }
        catch (Exception ex)
        {
            if (version != _listVersion)
            {
                return;
            }

            _all = new List<ConnectedAppSummary>();
            ApplyFilter();
            SelectedApp = null;
            StatusMessage = UiText.T("Conn_StatusFailedFmt", ex.Message);
            _log.Warn($"接続アプリ: 一覧の取得に失敗（{org.Username} / {kind}）: {ex.Message}");
        }
        finally
        {
            EndBusy();
        }
    }

    private void BeginBusy()
    {
        _busyCount++;
        IsBusy = true;
    }

    private void EndBusy()
    {
        _busyCount = Math.Max(0, _busyCount - 1);
        IsBusy = _busyCount > 0;
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (SelectedOrg is null)
        {
            StatusMessage = UiText.T("Conn_ErrorOrgRequired");
            return;
        }

        var kind = SelectedKind?.Key ?? ConnectedAppKinds.ConnectedApp;
        var result = await _editor.EditAsync(new ConnectedAppEditorRequest
        {
            Kind = kind,
            OrgDisplayName = SelectedOrg.DisplayName,
        }).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var files = ConnectedAppXml.BuildFiles(result.Draft);
        await ApplyDeployAsync(SelectedOrg, result, files).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedOrg is null || Document is null)
        {
            return;
        }

        var document = Document;
        var result = await _editor.EditAsync(new ConnectedAppEditorRequest
        {
            Kind = document.Kind,
            OrgDisplayName = SelectedOrg.DisplayName,
            Document = document,
        }).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var files = ConnectedAppXml.ApplyDraft(document, result.Draft);
        await ApplyDeployAsync(SelectedOrg, result, files).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedOrg is null || SelectedApp is null)
        {
            return;
        }

        var app = SelectedApp;
        if (!_dialogs.ConfirmDestructive(UiText.T("Conn_DeleteConfirmFmt", app.Name), UiText.T("Conn_Delete")))
        {
            return;
        }

        BeginBusy();
        StatusMessage = UiText.T("Conn_StatusDeleting");
        try
        {
            var result = await _service.DeleteAsync(SelectedOrg.Username, app.Kind, app.Name, CancellationToken.None).ConfigureAwait(true);
            if (result.Success)
            {
                StatusMessage = UiText.T("Conn_StatusDeletedFmt", app.Name);
                _log.Info($"接続アプリ: 削除しました（{SelectedOrg.Username} / {app.Kind} / {app.Name}）");
                EndBusy();
                await RefreshAsync().ConfigureAwait(true);
                return;
            }

            StatusMessage = UiText.T("Conn_StatusFailedFmt", result.ProblemText);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Conn_StatusFailedFmt", ex.Message);
            _log.Warn($"接続アプリ: 削除に失敗（{app.Name}）: {ex.Message}");
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private void OpenSetup()
    {
        if (SelectedOrg?.InstanceUrl is null)
        {
            return;
        }

        var path = IsEca
            ? "/lightning/setup/ExternalClientApplication/home"
            : "/lightning/setup/ConnectedApplication/home";
        try
        {
            Process.Start(new ProcessStartInfo(SelectedOrg.InstanceUrl.TrimEnd('/') + path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Conn_StatusFailedFmt", ex.Message);
        }
    }

    private async Task ApplyDeployAsync(OrgInfo org, ConnectedAppEditorResult result, IReadOnlyList<ConnectedAppFile> files)
    {
        BeginBusy();
        IsCreationRestricted = false;
        StatusMessage = result.DryRun ? UiText.T("Conn_StatusValidating") : UiText.T("Conn_StatusSaving");
        try
        {
            var deploy = await _service.DeployAsync(org.Username, files, result.DryRun, CancellationToken.None).ConfigureAwait(true);
            if (deploy.Success)
            {
                StatusMessage = result.DryRun
                    ? UiText.T("Conn_StatusValidatedFmt", result.Draft.Name)
                    : UiText.T("Conn_StatusSavedFmt", result.Draft.Name);
                if (!result.DryRun)
                {
                    EndBusy();
                    await RefreshAsync().ConfigureAwait(true);
                    return;
                }
            }
            else
            {
                StatusMessage = UiText.T("Conn_StatusFailedFmt", deploy.ProblemText);
                IsCreationRestricted = result.Draft.Name.Length > 0 && deploy.IsCreationRestricted && !result.DryRun;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Conn_StatusFailedFmt", ex.Message);
            _log.Warn($"接続アプリ: デプロイに失敗（{result.Draft.Name}）: {ex.Message}");
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task LoadDocumentAsync(ConnectedAppSummary summary)
    {
        if (SelectedOrg is null)
        {
            return;
        }

        try
        {
            var document = await _service.GetAsync(SelectedOrg.Username, summary.Kind, summary.Name, CancellationToken.None).ConfigureAwait(true);
            Document = document;
            OnPropertyChanged(nameof(HasDocument));
        }
        catch (Exception ex)
        {
            Document = null;
            OnPropertyChanged(nameof(HasDocument));
            StatusMessage = UiText.T("Conn_StatusFailedFmt", ex.Message);
            _log.Warn($"接続アプリ: 詳細の取得に失敗（{summary.Name}）: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        var text = Filter.Trim();
        var filtered = string.IsNullOrWhiteSpace(text)
            ? _all
            : _all.Where(a => a.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                              || a.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
                              || (a.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        Apps.Clear();
        foreach (var app in filtered)
        {
            Apps.Add(app);
        }

        if (SelectedApp is not null && !Apps.Contains(SelectedApp))
        {
            SelectedApp = null;
        }
    }
}
