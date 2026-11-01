using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>OAuth スコープ 1 件分のチェックボックス。</summary>
public sealed partial class ConnectedAppScopeOption : ObservableObject
{
    public ConnectedAppScopeOption(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public string Label => Name;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>接続アプリの作成 / 編集フォーム。</summary>
public sealed partial class ConnectedAppEditorViewModel : ObservableObject
{
    private readonly ConnectedAppDocument? _document;
    private readonly string _initialXml;

    public ConnectedAppEditorViewModel(ConnectedAppEditorRequest request)
    {
        _document = request.Document;
        Kind = request.Kind;
        OrgDisplayName = request.OrgDisplayName;

        var document = request.Document;
        if (document is not null)
        {
            Name = document.Name;
            Label = document.Label;
            ContactEmail = document.ContactEmail ?? "";
            Description = document.Description ?? "";
            CallbackUrl = (document.CallbackUrl ?? "").Replace('\r', '\n');
            ConsumerKey = document.ConsumerKey ?? "";
            IpRelaxation = MapIpRelaxationFromSalesforce(document.IpRelaxation);
            RefreshTokenPolicy = MapRefreshTokenFromSalesforce(document.RefreshTokenPolicy);
            IsAdminApproved = string.Equals(document.PermittedUsersPolicy, "AdminApprovedPreAuthorized", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Label = "";
            ContactEmail = "";
            Description = "";
            CallbackUrl = "";
            ConsumerKey = "";
            IpRelaxation = "ENFORCE";
            RefreshTokenPolicy = "infinite";
        }

        foreach (var scope in ConnectedAppOauthScopes.All)
        {
            var selected = document?.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase) ?? false;
            Scopes.Add(new ConnectedAppScopeOption(scope, selected));
        }

        _initialXml = document?.MainXml ?? "";
        _xml = _initialXml;
    }

    public string Kind { get; }

    public string OrgDisplayName { get; }

    public bool IsNew => _document is null;

    public bool IsEca => string.Equals(Kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase);

    public bool IsConnectedApp => !IsEca;

    public string Title => UiText.T(IsNew ? "Conn_NewTitleFmt" : "Conn_EditTitleFmt", UiText.T(IsEca ? "Conn_KindEca" : "Conn_KindConnectedApp"));

    public string LabelName => UiText.T("Conn_LabelName");

    public string LabelDisplayName => UiText.T("Conn_LabelLabel");

    public string LabelContactEmail => UiText.T("Conn_LabelContactEmail");

    public string LabelDescription => UiText.T("Conn_LabelDescription");

    public string LabelCallbackUrl => UiText.T("Conn_LabelCallbackUrl");

    public string LabelScopes => UiText.T("Conn_LabelScopes");

    public string LabelIpRelaxation => UiText.T("Conn_LabelIpRelaxation");

    public string LabelRefreshTokenPolicy => UiText.T("Conn_LabelRefreshTokenPolicy");

    public string LabelConsumerKey => UiText.T("Conn_LabelConsumerKey");

    public string LabelAdminApproved => UiText.T("Conn_LabelPermittedUsers");

    public string HintConsumerKey => UiText.T("Conn_HintConsumerKey");

    public string HintCallbackUrl => UiText.T("Conn_HintCallbackUrl");

    public string TabForm => UiText.T("Conn_TabForm");

    public string TabXml => UiText.T("Conn_TabXml");

    public string SaveText => UiText.T("Conn_Save");

    public string CancelText => UiText.T("Conn_Cancel");

    public string DryRunText => UiText.T("Conn_DryRun");

    public string HintXml => UiText.T("Conn_HintXml");

    /// <summary>IP 緩和の選択肢（ConnectedApp の値）。</summary>
    public IReadOnlyList<ReportChoice> IpRelaxations { get; } = new[]
    {
        new ReportChoice("ENFORCE", UiText.T("Conn_IpEnforce")),
        new ReportChoice("BYPASS_2FACTOR", UiText.T("Conn_IpBypass2Factor")),
        new ReportChoice("BYPASS", UiText.T("Conn_IpBypass")),
        new ReportChoice("ENFORCE_RELAXREFRESH", UiText.T("Conn_IpEnforceRelaxRefresh")),
    };

    /// <summary>リフレッシュ トークン ポリシーの選択肢。</summary>
    public IReadOnlyList<ReportChoice> RefreshTokenPolicies { get; } = new[]
    {
        new ReportChoice("infinite", UiText.T("Conn_RefreshInfinite")),
        new ReportChoice("zero", UiText.T("Conn_RefreshZero")),
        new ReportChoice("SpecificLifetime", UiText.T("Conn_RefreshLifetime")),
        new ReportChoice("SpecificInactivity", UiText.T("Conn_RefreshInactivity")),
    };

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _contactEmail = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _callbackUrl = "";

    [ObservableProperty]
    private string _consumerKey = "";

    [ObservableProperty]
    private string _ipRelaxation = "ENFORCE";

    [ObservableProperty]
    private string _refreshTokenPolicy = "infinite";

    [ObservableProperty]
    private bool _isAdminApproved;

    [ObservableProperty]
    private bool _isXmlMode;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _xml = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _dryRun;

    public ObservableCollection<ConnectedAppScopeOption> Scopes { get; } = new();

    /// <summary>XML タブへ切り替えたときにフォーム内容から XML を生成する。</summary>
    partial void OnIsXmlModeChanged(bool value)
    {
        if (SelectedTabIndex != (value ? 1 : 0))
        {
            SelectedTabIndex = value ? 1 : 0;
        }

        if (value && string.IsNullOrWhiteSpace(Xml))
        {
            Xml = ConnectedAppXml.BuildFiles(ToDraft(null))[0].Content;
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => IsXmlMode = value == 1;

    /// <summary>フォーム内容からドラフトを作る。</summary>
    public ConnectedAppDraft ToDraft(string? rawXml)
    {
        return new ConnectedAppDraft
        {
            Kind = Kind,
            Name = Name.Trim(),
            Label = Label.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            CallbackUrl = string.IsNullOrWhiteSpace(CallbackUrl) ? null : CallbackUrl.Trim(),
            Scopes = Scopes.Where(s => s.IsSelected).Select(s => s.Name).ToList(),
            IpRelaxation = IpRelaxation,
            RefreshTokenPolicy = RefreshTokenPolicy,
            IsAdminApproved = IsAdminApproved,
            ConsumerKey = string.IsNullOrWhiteSpace(ConsumerKey) ? null : ConsumerKey.Trim(),
            RawXml = rawXml,
        };
    }

    /// <summary>入力を検証し、問題なければ結果を返す。</summary>
    public ConnectedAppEditorResult? BuildResult()
    {
        var rawXml = IsXmlMode && !string.IsNullOrWhiteSpace(Xml) && Xml != _initialXml ? Xml : null;
        var draft = ToDraft(rawXml);
        if (rawXml is null)
        {
            var error = draft.Validate();
            if (error is not null)
            {
                ErrorMessage = error;
                return null;
            }
        }
        else if (!rawXml.Contains("<", StringComparison.Ordinal))
        {
            ErrorMessage = UiText.T("Conn_ErrorXmlInvalid");
            return null;
        }

        ErrorMessage = "";
        return new ConnectedAppEditorResult(draft, DryRun);
    }

    private static string MapIpRelaxationFromSalesforce(string? value) => (value ?? "").ToUpperInvariant() switch
    {
        "ENFORCE" => "ENFORCE",
        "BYPASS" => "BYPASS",
        "BYPASS_2FACTOR" => "BYPASS_2FACTOR",
        "ENFORCE_RELAXREFRESH" => "ENFORCE_RELAXREFRESH",
        _ => "ENFORCE",
    };

    private static string MapRefreshTokenFromSalesforce(string? value) => (value ?? "").ToLowerInvariant() switch
    {
        "" => "infinite",
        "infinite" => "infinite",
        "zero" => "zero",
        "specificlifetime" => "SpecificLifetime",
        "specificinactivity" => "SpecificInactivity",
        _ => "infinite",
    };
}
