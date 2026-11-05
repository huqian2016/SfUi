using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// 内部ブラウザー（WebView2）で Salesforce にサインインし、その組織を登録するタブの ViewModel。
/// ログイン後のセッション（sid Cookie）を受け取り、<c>sf org login access-token</c> で登録する。
/// </summary>
public partial class OrgSignInViewModel : ObservableObject
{
    private readonly OrgManageService _manage;
    private readonly AppLog _log;

    private string? _sessionId;

    public OrgSignInViewModel(OrgManageService manage, AppLog log)
    {
        _manage = manage;
        _log = log;
        _statusText = UiText.T("OrgSignIn_Hint");
    }

    /// <summary>組織の登録が完了したとき（メイン画面が組織一覧を再読み込みする）。</summary>
    public event EventHandler? OrgRegistered;

    public string Title => UiText.T("OrgSignIn_Title");

    public string LabelInstance => UiText.T("OrgSignIn_InstanceUrl");

    public string OpenText => UiText.T("OrgSignIn_Open");

    public string ReloadText => UiText.T("OrgSignIn_Reload");

    public string RegisterText => UiText.T("OrgSignIn_Register");

    public string ResetText => UiText.T("OrgSignIn_Reset");

    public string LabelAlias => UiText.T("OrgSignIn_Alias");

    public string HintText => UiText.T("OrgSignIn_Hint");

    /// <summary>サインイン先（ログイン URL または組織の URL）。</summary>
    [ObservableProperty]
    private string _instanceUrl = OrgSignInUrl.LoginUrl;

    /// <summary>登録時のエイリアス（任意）。</summary>
    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>ログイン後に検出した組織の URL。</summary>
    [ObservableProperty]
    private string? _detectedInstanceUrl;

    /// <summary>ログイン後のセッションを検出済みか（登録ボタンの活性）。</summary>
    public bool HasSession => !string.IsNullOrEmpty(_sessionId) && !string.IsNullOrEmpty(DetectedInstanceUrl);

    /// <summary>画面から渡す URL（正規化済み）。</summary>
    public string NormalizedInstanceUrl => OrgSignInUrl.Normalize(InstanceUrl);

    /// <summary>ログイン後のページから検出したセッションを取り込む。</summary>
    public void ApplySession(string instanceUrl, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (HasSession && string.Equals(DetectedInstanceUrl, instanceUrl, StringComparison.OrdinalIgnoreCase))
        {
            _sessionId = sessionId;
            return;
        }

        DetectedInstanceUrl = instanceUrl;
        _sessionId = sessionId;
        StatusText = UiText.T("OrgSignIn_DetectedFmt", instanceUrl);
        OnPropertyChanged(nameof(HasSession));
        RegisterCommand.NotifyCanExecuteChanged();
    }

    /// <summary>検出済みのセッションを破棄する。</summary>
    public void ClearSession()
    {
        _sessionId = null;
        DetectedInstanceUrl = null;
        OnPropertyChanged(nameof(HasSession));
        RegisterCommand.NotifyCanExecuteChanged();
    }

    /// <summary>入力と検出状態を初期化する。</summary>
    [RelayCommand]
    private void Reset()
    {
        ClearSession();
        Alias = string.Empty;
        StatusText = HintText;
    }

    /// <summary>検出したセッションで組織を登録する。</summary>
    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        if (!HasSession)
        {
            StatusText = UiText.T("OrgSignIn_NoSession");
            return;
        }

        IsBusy = true;
        StatusText = UiText.T("OrgSignIn_Registering");
        try
        {
            var instanceUrl = DetectedInstanceUrl!;
            var alias = Alias.Trim();
            var result = await _manage.LoginAccessTokenAsync(
                instanceUrl,
                _sessionId!,
                alias.Length == 0 ? null : alias,
                setDefault: false,
                CancellationToken.None);

            if (result.Success)
            {
                StatusText = UiText.T("OrgSignIn_RegisteredFmt", instanceUrl);
                _log.Info($"内部ブラウザーで組織を登録: {instanceUrl}");
                OrgRegistered?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusText = UiText.T("OrgSignIn_RegisterFailedFmt", result.Message);
                _log.Warn($"内部ブラウザーでの組織登録に失敗: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("OrgSignIn_RegisterFailedFmt", ex.Message);
            _log.Warn($"内部ブラウザーでの組織登録に失敗: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRegister() => HasSession && !IsBusy;

    partial void OnIsBusyChanged(bool value) => RegisterCommand.NotifyCanExecuteChanged();
}
