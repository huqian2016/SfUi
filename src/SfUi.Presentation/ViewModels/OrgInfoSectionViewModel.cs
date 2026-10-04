using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>組織情報の 1 セクション（タブ）の ViewModel。</summary>
public partial class OrgInfoSectionViewModel : ObservableObject, IOrgInfoTab, IDisposable
{
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(200);

    private readonly OrgInfoService _service;
    private readonly OrgInfoCacheStore _cache;
    private readonly AppLog _log;
    private readonly ToolLauncherService _toolLauncher;
    private string? _setupUrl;
    private readonly OrgInfo _org;
    private readonly string _orgKey;
    private readonly string _titleKey;
    private readonly UiDebouncer _filterDebounce;
    private readonly List<OrgInfoRowView> _allRows = new();

    private OrgInfoSection? _source;

    public OrgInfoSectionViewModel(
        string id,
        string titleKey,
        IReadOnlyList<OrgInfoColumn> columns,
        OrgInfo org,
        string orgKey,
        OrgInfoService service,
        OrgInfoCacheStore cache,
        AppLog log,
        ToolLauncherService toolLauncher,
        IUiDispatcher ui,
        string? setupUrl = null)
    {
        Id = id;
        _titleKey = titleKey;
        _org = org;
        _orgKey = orgKey;
        _service = service;
        _cache = cache;
        _log = log;
        _toolLauncher = toolLauncher;
        Columns = columns;
        _title = UiText.T(titleKey);
        _emptyMessage = UiText.T("OrgInfo_NotFetched");
        SetSetupUrl(setupUrl);

        _filterDebounce = new UiDebouncer((int)FilterDebounce.TotalMilliseconds, ui);
    }

    public string Id { get; }

    public IReadOnlyList<OrgInfoColumn> Columns { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string _fetchedAtText = UiText.T("OrgInfo_NotFetched");

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _emptyMessage;

    public ObservableCollection<OrgInfoRowView> FilteredRows { get; } = new();

    public bool HasData => _source?.HasData == true;

    public DateTimeOffset? FetchedAt => _source?.FetchedAt;

    public bool CanRefresh => !IsLoading;

    /// <summary>取得済みデータ（検索用）。</summary>
    public OrgInfoSection? Source => _source;

    /// <summary>行リンク（ユーザー/プロファイル/オブジェクト詳細）を持つ可能性があるセクションか。</summary>
    public bool CanHaveLinks => Id is OrgInfoSections.Users or OrgInfoSections.Profiles or OrgInfoSections.Objects;

    /// <summary>オブジェクトタブ限定: データ入出力ウィンドウを開くボタンを表示する。</summary>
    public bool IsObjectsSection => Id == OrgInfoSections.Objects;

    /// <summary>データ入出力ウィンドウの起動要求（選択中オブジェクトの API 名を渡す。未選択は null）。</summary>
    public event Action<string?>? DataIoRequested;

    [RelayCommand]
    private void OpenDataIo() => DataIoRequested?.Invoke(SelectedRow?.Id);

    /// <summary>セクションに対応する Setup ページ URL（null はリンクなし）。</summary>
    public string? SetupUrl => _setupUrl;

    public bool HasSetupLink => !string.IsNullOrEmpty(_setupUrl);

    [ObservableProperty]
    private OrgInfoRowView? _selectedRow;

    /// <summary>セクションの取得が成功したときに発火する（ウィンドウのステータス更新用）。</summary>
    public event Action<OrgInfoSectionViewModel>? Fetched;

    /// <summary>検索結果などから該当行を選択する（絞り込みは解除）。</summary>
    public void SelectRow(string rowId)
    {
        if (!string.IsNullOrEmpty(FilterText))
        {
            FilterText = string.Empty;
        }

        _filterDebounce.Cancel(); // デバウンス経由の再適用は不要（この後すぐ ApplyFilter する）
        ApplyFilter();
        SelectedRow = FilteredRows.FirstOrDefault(r => string.Equals(r.Id, rowId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Setup URL を設定する（オブジェクト項目は選択オブジェクトごとに変わる）。</summary>
    public void SetSetupUrl(string? url)
    {
        if (string.Equals(_setupUrl, url, StringComparison.Ordinal))
        {
            return;
        }

        _setupUrl = url;
        OnPropertyChanged(nameof(SetupUrl));
        OnPropertyChanged(nameof(HasSetupLink));
    }

    [RelayCommand]
    private async Task RefreshAsync() => await FetchAsync().ConfigureAwait(true);

    [RelayCommand]
    private void OpenSetup()
    {
        if (string.IsNullOrEmpty(_setupUrl))
        {
            return;
        }

        var result = _toolLauncher.LaunchBrowser(_setupUrl);
        if (!result.Success)
        {
            ErrorText = result.Message;
        }
    }

    [RelayCommand]
    private void OpenRowLink(OrgInfoRowView? row)
    {
        if (row?.Link is not { Length: > 0 } url)
        {
            return;
        }

        var result = _toolLauncher.LaunchBrowser(url);
        if (!result.Success)
        {
            ErrorText = result.Message;
        }
    }

    /// <summary>キャッシュまたは取得結果を表示へ反映する。</summary>
    public void Apply(OrgInfoSection section)
    {
        _source = section;
        _allRows.Clear();
        foreach (var row in section.Rows)
        {
            _allRows.Add(CreateRowView(row));
        }

        FetchedAtText = section.FetchedAt is { } fetchedAt
            ? UiText.T("OrgInfo_FetchedAtFmt", FormatTimestamp(fetchedAt))
            : UiText.T("OrgInfo_NotFetched");

        ApplyFilter();
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(FetchedAt));
    }

    /// <summary>現在言語で表示値を組み直す（言語切替時）。</summary>
    public void Relocalize()
    {
        Title = UiText.T(_titleKey);
        if (_source is not null)
        {
            Apply(_source);
        }
        else
        {
            EmptyMessage = UiText.T("OrgInfo_NotFetched");
        }
    }

    /// <summary>Salesforce から取得してキャッシュへ保存する。戻り値は成功可否。</summary>
    public async Task<bool> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return false;
        }

        IsLoading = true;
        ErrorText = null;
        try
        {
            var section = await _service.FetchSectionAsync(_org, Id, cancellationToken);
            _cache.UpsertSection(_orgKey, section);
            Apply(section);
            Fetched?.Invoke(this);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SalesforceApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden
            || string.Equals(ex.ErrorCode, "INSUFFICIENT_ACCESS", StringComparison.OrdinalIgnoreCase))
        {
            ErrorText = UiText.T("OrgInfo_PermissionDeniedFmt", ex.Message);
            _log.Error($"組織情報の取得権限が不足: {_orgKey}/{Id}", ex);
            return false;
        }
        catch (Exception ex)
        {
            ErrorText = UiText.T("OrgInfo_LoadFailedFmt", ex.Message);
            _log.Error($"組織情報の取得に失敗: {_orgKey}/{Id}", ex);
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>タブの UI オートメーション名などに使われる表示名。</summary>
    public override string ToString() => Title;

    /// <summary>AI パネルへ添付するテキスト（タイトル + 絞り込み前の全行）。</summary>
    public string? BuildAttachmentText(int maxChars)
    {
        if (_source is null)
        {
            return null;
        }

        var headers = Columns.Select(c => c.Label).ToList();
        var rows = new List<IReadOnlyList<string>>(_allRows.Count);
        foreach (var row in _allRows)
        {
            var values = new List<string>(Columns.Count);
            foreach (var column in Columns)
            {
                values.Add(row.Cells.TryGetValue(column.Key, out var value) ? value : string.Empty);
            }

            rows.Add(values);
        }

        var title = UiText.T("OrgInfo_Ai_TabHeaderFmt", Title, _allRows.Count, FetchedAtText);
        return OrgInfoAttachment.Build(title, headers, rows, maxChars);
    }

    public void Dispose()
    {
        _filterDebounce.Cancel();
        FilteredRows.Clear();
        _allRows.Clear();
    }

    internal static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    partial void OnFilterTextChanged(string value)
    {
        _filterDebounce.Debounce(ApplyFilter);
    }

    private void ApplyFilter()
    {
        var terms = FilterText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        FilteredRows.Clear();
        foreach (var row in _allRows)
        {
            if (terms.Length == 0 || terms.All(t => row.SearchText.Contains(t, StringComparison.Ordinal)))
            {
                FilteredRows.Add(row);
            }
        }

        CountText = HasData ? UiText.T("OrgInfo_CountFmt", FilteredRows.Count, _allRows.Count) : string.Empty;
        EmptyMessage = !HasData
            ? UiText.T("OrgInfo_NotFetched")
            : FilteredRows.Count == 0 && terms.Length > 0
                ? UiText.T("OrgInfo_NoMatch")
                : string.Empty;
    }

    private OrgInfoRowView CreateRowView(OrgInfoRow row)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in Columns)
        {
            cells[column.Key] = OrgInfoDisplay.FormatCell(Id, row, column.Key, row.Get(column.Key)) ?? string.Empty;
        }

        return new OrgInfoRowView
        {
            Id = row.Id,
            Summary = row.Summary,
            Link = row.Link,
            Cells = cells,
            SearchText = OrgInfoDisplay.BuildSearchText(Id, Columns, row),
        };
    }
}
