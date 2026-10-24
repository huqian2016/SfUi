using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>REST コンソールタブの ViewModel（エクスプローラー ツリー付き）。</summary>
public partial class ApiConsoleViewModel : ObservableObject
{
    private const string BodySeparator = "\n---body---\n";
    private const string DefaultApiVersion = "67.0";
    private const string HistoryFolderKey = "group.history";

    private readonly SalesforceRestClient _client;
    private readonly SObjectDescribeService _describes;
    private readonly HistoryStore _history;
    private readonly FavoritesStore _favorites;
    private readonly AppSettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly Dictionary<string, IReadOnlyList<string>> _objectsByOrg = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _versionByOrg = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _instanceByOrg = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _customApisByOrg = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _objectNames = Array.Empty<string>();
    private IReadOnlyList<string> _customApiNames = Array.Empty<string>();

    /// <summary>直近にツリーから適用したエンドポイント（パラメータ検証に使う）。</summary>
    private RestApiNode? _appliedNode;

    /// <summary>ツリー再構築時の選択復元で、入力を上書きしないためのフラグ。</summary>
    private bool _suppressApply;

    /// <summary>初期表示のままのパス（組織の API バージョンに追従させる）。</summary>
    private string _lastDefaultPath = $"/services/data/v{DefaultApiVersion}/limits";

    [ObservableProperty]
    private string _method = "GET";

    [ObservableProperty]
    private string _path = $"/services/data/v{DefaultApiVersion}/limits";

    [ObservableProperty]
    private string? _requestBody;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private string? _responseText;

    /// <summary>整形前の生の応答（Workbench の Show Raw Response 用）。</summary>
    [ObservableProperty]
    private string? _rawResponseText;

    /// <summary>生の応答を表示するか（Workbench 風のトグル）。</summary>
    [ObservableProperty]
    private bool _isRawResponse;

    /// <summary>インスタンスの URL（絶対 URL の表示用）。</summary>
    [ObservableProperty]
    private string? _instanceUrl;

    /// <summary>レスポンス領域に表示する文字列。</summary>
    public string DisplayResponse => IsRawResponse ? RawResponseText ?? string.Empty : ResponseText ?? string.Empty;

    /// <summary>リクエスト本文を表示するか（GET / DELETE では隠す）。</summary>
    public bool IsBodyVisible => !string.Equals(Method, "GET", StringComparison.OrdinalIgnoreCase)
                                 && !string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase);

    /// <summary>現在のパスの絶対 URL（表示用。取得できていなければパスのみ）。</summary>
    public string AbsoluteUrl => string.IsNullOrWhiteSpace(InstanceUrl)
        ? Path
        : InstanceUrl!.TrimEnd('/') + (Path.StartsWith('/') ? Path : "/" + Path);

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    [ObservableProperty]
    private string? _currentOrg;

    // ---- エクスプローラー ツリー ----

    [ObservableProperty]
    private bool _isTreeVisible = true;

    [ObservableProperty]
    private bool _isTreeLoading;

    [ObservableProperty]
    private string _treeFilter = string.Empty;

    [ObservableProperty]
    private RestTreeNodeViewModel? _selectedTreeNode;

    /// <summary>REST の API バージョン（組織から取得。取得できるまでは既定値）。</summary>
    [ObservableProperty]
    private string _apiVersion = DefaultApiVersion;

    public ObservableCollection<string> Methods { get; } = new() { "GET", "POST", "PATCH", "DELETE" };

    // Workbench 風のメソッド ボタン用（選択中のものだけ色を変える）
    public bool IsGetMethod => IsMethod("GET");

    public bool IsPostMethod => IsMethod("POST");

    public bool IsPatchMethod => IsMethod("PATCH");

    public bool IsDeleteMethod => IsMethod("DELETE");

    private bool IsMethod(string method) => string.Equals(Method, method, StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<RestTreeNodeViewModel> TreeNodes { get; } = new();

    /// <summary>選択中エンドポイントの入力パラメータ。</summary>
    public ObservableCollection<RestParameterViewModel> Parameters { get; } = new();

    public bool HasParameters => Parameters.Count > 0;

    public ApiConsoleViewModel(
        SalesforceRestClient client,
        SObjectDescribeService describes,
        HistoryStore history,
        FavoritesStore favorites,
        AppSettingsStore settings,
        IDialogService dialogs,
        AppLog log)
    {
        _client = client;
        _describes = describes;
        _history = history;
        _favorites = favorites;
        _settings = settings;
        _dialogs = dialogs;
        _log = log;
        Parameters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasParameters));
        RebuildTree();
    }

    partial void OnCurrentOrgChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = LoadOrgDataAsync(value);
        }
    }

    partial void OnTreeFilterChanged(string value) => RebuildTree();

    partial void OnResponseTextChanged(string? value) => OnPropertyChanged(nameof(DisplayResponse));

    partial void OnRawResponseTextChanged(string? value) => OnPropertyChanged(nameof(DisplayResponse));

    partial void OnIsRawResponseChanged(bool value) => OnPropertyChanged(nameof(DisplayResponse));

    partial void OnMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsBodyVisible));
        OnPropertyChanged(nameof(IsGetMethod));
        OnPropertyChanged(nameof(IsPostMethod));
        OnPropertyChanged(nameof(IsPatchMethod));
        OnPropertyChanged(nameof(IsDeleteMethod));
    }

    partial void OnPathChanged(string value) => OnPropertyChanged(nameof(AbsoluteUrl));

    partial void OnApiVersionChanged(string value)
    {
        // 初期パスのまま（ユーザーが触っていない）なら組織の API バージョンに合わせる
        if (Path == _lastDefaultPath)
        {
            _lastDefaultPath = $"/services/data/v{value}/limits";
            Path = _lastDefaultPath;
        }
    }

    partial void OnInstanceUrlChanged(string? value) => OnPropertyChanged(nameof(AbsoluteUrl));

    /// <summary>メソッド ボタン（Workbench 風）から HTTP メソッドを切り替える。</summary>
    [RelayCommand]
    private void SetMethod(string? method)
    {
        if (!string.IsNullOrWhiteSpace(method))
        {
            Method = method.ToUpperInvariant();
        }
    }

    partial void OnSelectedTreeNodeChanged(RestTreeNodeViewModel? value)
    {
        if (_suppressApply)
        {
            return;
        }

        ApplyNode(value);
    }

    // ---- ツリー ----

    [RelayCommand]
    private void ToggleTree() => IsTreeVisible = !IsTreeVisible;

    /// <summary>ツリーを作り直す（言語切替や組織変更時）。</summary>
    public void RefreshTree() => RebuildTree();

    [RelayCommand]
    private async Task ReloadTreeAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentOrg))
        {
            await LoadOrgDataAsync(CurrentOrg, forceObjects: true);
        }

        RebuildTree();
    }

    /// <summary>API バージョンと SObject 一覧を取得してツリーを作り直す。</summary>
    private async Task LoadOrgDataAsync(string org, bool forceObjects = false)
    {
        IsTreeLoading = true;
        try
        {
            if (!_versionByOrg.TryGetValue(org, out var version))
            {
                version = await _client.GetApiVersionAsync(org);
                _versionByOrg[org] = string.IsNullOrWhiteSpace(version) ? DefaultApiVersion : version;
            }

            ApiVersion = _versionByOrg[org];

            if (!_instanceByOrg.TryGetValue(org, out var instance))
            {
                instance = await _client.GetInstanceUrlAsync(org) ?? string.Empty;
                _instanceByOrg[org] = instance;
            }

            InstanceUrl = string.IsNullOrWhiteSpace(instance) ? null : instance;

            if (forceObjects || !_objectsByOrg.TryGetValue(org, out var objects))
            {
                var list = await _describes.ListObjectsAsync(org, forceRefresh: forceObjects);
                objects = list.Select(item => item.Name).ToList();
                _objectsByOrg[org] = objects;
            }

            _objectNames = objects;

            if (forceObjects || !_customApisByOrg.TryGetValue(org, out var customApis))
            {
                customApis = await LoadCustomApisAsync(org, ApiVersion);
                _customApisByOrg[org] = customApis;
            }

            _customApiNames = customApis;
        }
        catch (Exception ex)
        {
            _log.Warn($"REST ツリー: 組織データの取得に失敗: {ex.Message}");
            _objectNames = Array.Empty<string>();
            _customApiNames = Array.Empty<string>();
        }
        finally
        {
            IsTreeLoading = false;
            RebuildTree();
        }
    }

    /// <summary>組織のカスタム API（/actions/custom の一覧）を取得する（失敗時は空）。</summary>
    private async Task<IReadOnlyList<string>> LoadCustomApisAsync(string org, string version)
    {
        try
        {
            var (status, body) = await _client.SendConsoleAsync(
                org,
                HttpMethod.Get,
                $"/services/data/v{version}/actions/custom");
            if ((int)status is < 200 or > 299)
            {
                _log.Warn($"REST ツリー: カスタム API の取得に失敗: HTTP {(int)status}");
                return Array.Empty<string>();
            }

            return RestCustomApiCatalog.ParseNames(body);
        }
        catch (Exception ex)
        {
            _log.Warn($"REST ツリー: カスタム API の取得に失敗: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private void RebuildTree()
    {
        var selectedKey = SelectedTreeNode?.Key;
        var expandedKeys = TreeNodes
            .SelectMany(Flatten)
            .Where(node => node.IsExpanded)
            .Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        TreeNodes.Clear();

        var filter = TreeFilter.Trim();
        if (filter.Length > 0)
        {
            AddFilteredNodes(filter);
        }
        else
        {
            AddFolder(RestTreeNodeViewModel.CreateFavorites(_favorites.GetByType(HistoryTypes.Api)));
            AddFolder(RestTreeNodeViewModel.CreateHistory(RecentHistory()));
            foreach (var group in RestApiCatalog.BuildStaticGroups())
            {
                TreeNodes.Add(new RestTreeNodeViewModel(group));
            }

            TreeNodes.Add(new RestTreeNodeViewModel(RestCustomApiCatalog.BuildGroup(_customApiNames)));
            TreeNodes.Add(new RestTreeNodeViewModel(RestApiCatalog.BuildObjectGroup(() => _objectNames)));
        }

        RestoreExpansion(expandedKeys);

        var restored = selectedKey is null
            ? null
            : TreeNodes.SelectMany(Flatten).FirstOrDefault(node => node.Key == selectedKey);
        if (restored is not null)
        {
            _suppressApply = true;
            SelectedTreeNode = restored;
            _suppressApply = false;
        }
    }

    /// <summary>再構築前の展開状態を戻す（遅延ノードは展開して子を作る）。</summary>
    private void RestoreExpansion(HashSet<string> expandedKeys)
    {
        if (expandedKeys.Count == 0)
        {
            return;
        }

        for (var pass = 0; pass < 3; pass++)
        {
            var pending = false;
            foreach (var node in TreeNodes.SelectMany(Flatten).ToList())
            {
                if (expandedKeys.Contains(node.Key) && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    pending = true;
                }
            }

            if (!pending)
            {
                break;
            }
        }
    }

    private void AddFolder(RestTreeNodeViewModel folder)
    {
        folder.IsExpanded = true;
        TreeNodes.Add(folder);
    }

    private void AddFilteredNodes(string filter)
    {
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        var favorites = _favorites.GetByType(HistoryTypes.Api)
            .Where(item => item.Label.Contains(filter, comparison))
            .ToList();
        if (favorites.Count > 0)
        {
            AddFolder(RestTreeNodeViewModel.CreateFavorites(favorites));
        }

        var history = RecentHistory()
            .Where(entry => (entry.Summary ?? string.Empty).Contains(filter, comparison))
            .ToList();
        if (history.Count > 0)
        {
            AddFolder(RestTreeNodeViewModel.CreateHistory(history));
        }

        foreach (var group in RestApiCatalog.BuildStaticGroups())
        {
            var matches = FilterRequests(group, filter);
            if (matches.Count > 0)
            {
                var node = new RestTreeNodeViewModel(new RestApiNode(group.Id, group.Title, Children: matches));
                node.IsExpanded = true;
                TreeNodes.Add(node);
            }
        }

        var custom = RestCustomApiCatalog.BuildGroup(_customApiNames);
        var customMatches = FilterRequests(custom, filter);
        if (customMatches.Count > 0)
        {
            var node = new RestTreeNodeViewModel(new RestApiNode(custom.Id, custom.Title, Children: customMatches));
            node.IsExpanded = true;
            TreeNodes.Add(node);
        }

        var objects = _objectNames
            .Where(name => name.Contains(filter, comparison))
            .Take(200)
            .Select(RestApiCatalog.BuildObjectNode)
            .ToList();
        if (objects.Count > 0)
        {
            var node = new RestTreeNodeViewModel(
                new RestApiNode("group.objects.search", UiText.T("ApiTree_GroupObjects"), Children: objects));
            node.IsExpanded = true;
            TreeNodes.Add(node);
        }
    }

    private static List<RestApiNode> FilterRequests(RestApiNode folder, string filter)
    {
        var result = new List<RestApiNode>();
        foreach (var child in folder.Children ?? Array.Empty<RestApiNode>())
        {
            if (child.Kind == RestApiNodeKind.Request)
            {
                if (child.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (child.PathTemplate?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    result.Add(child);
                }
            }
            else
            {
                var nested = FilterRequests(child, filter);
                if (nested.Count > 0)
                {
                    result.Add(new RestApiNode(child.Id, child.Title, Children: nested));
                }
            }
        }

        return result;
    }

    private IEnumerable<HistoryEntry> RecentHistory() =>
        _history.Query(new HistoryQuery(Type: HistoryTypes.Api)).Take(30);

    private static IEnumerable<RestTreeNodeViewModel> Flatten(RestTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    /// <summary>ノード選択 → Method / パス / ボディ / パラメータを反映する。</summary>
    private void ApplyNode(RestTreeNodeViewModel? node)
    {
        if (node is null || node.IsPlaceholder)
        {
            return;
        }

        if (node.Node is { Kind: RestApiNodeKind.Request } request)
        {
            ApplyRequest(request);
            return;
        }

        if (node.Favorite is { } favorite)
        {
            _appliedNode = null;
            Parameters.Clear();
            LoadFavorite(favorite, autoRun: false);
            return;
        }

        if (node.History is { } history)
        {
            _appliedNode = null;
            Parameters.Clear();
            LoadFromHistory(history, autoRun: false);
        }
    }

    private void ApplyRequest(RestApiNode request)
    {
        _appliedNode = request;
        Method = request.Method?.ToUpperInvariant() ?? "GET";
        RequestBody = RestApiCatalog.BuildBody(request, ApiVersion);

        Parameters.Clear();
        foreach (var parameter in request.Parameters ?? Array.Empty<RestApiParameter>())
        {
            Parameters.Add(new RestParameterViewModel(parameter, RebuildPath));
        }

        RebuildPath();
    }

    private void RebuildPath()
    {
        if (_appliedNode is null)
        {
            return;
        }

        var values = Parameters.ToDictionary(
            parameter => parameter.Parameter.Name,
            parameter => parameter.Value,
            StringComparer.OrdinalIgnoreCase);
        Path = RestApiCatalog.BuildPath(_appliedNode, ApiVersion, values);
    }

    private IReadOnlyList<string> MissingParameters()
    {
        if (_appliedNode is null)
        {
            return Array.Empty<string>();
        }

        var values = Parameters.ToDictionary(
            parameter => parameter.Parameter.Name,
            parameter => parameter.Value,
            StringComparer.OrdinalIgnoreCase);
        return RestApiCatalog.MissingParameters(_appliedNode, values);
    }

    private void RefreshHistoryNode()
    {
        var node = TreeNodes.FirstOrDefault(item => item.Key == HistoryFolderKey);
        if (node is null)
        {
            return;
        }

        var index = TreeNodes.IndexOf(node);
        var replacement = RestTreeNodeViewModel.CreateHistory(RecentHistory());
        replacement.IsExpanded = true;
        TreeNodes[index] = replacement;
    }

    // ---- 送信 ----

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentOrg))
        {
            StatusText = UiText.T("Msg_SelectOrg");
            return;
        }

        var missing = MissingParameters();
        if (missing.Count > 0)
        {
            StatusText = UiText.T("ApiTree_MissingParamsFmt", string.Join(", ", missing));
            return;
        }

        RebuildPath();
        var path = Path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = UiText.T("Api_EnterPath");
            return;
        }

        var method = Method.Trim().ToUpperInvariant();
        var body = string.IsNullOrWhiteSpace(RequestBody) ? null : RequestBody;

        if (method != "GET" && ConfirmPolicies.ShouldConfirm(_settings.Current.ConfirmPolicy, isDangerous: true))
        {
            var answer = _dialogs.Confirm(
                UiText.T("Api_SendConfirmFmt", method, Environment.NewLine, path),
                "SfUi");
            if (!answer)
            {
                StatusText = UiText.T("Common_Canceled");
                return;
            }
        }

        IsRunning = true;
        StatusText = UiText.T("Api_Sending");
        ResponseText = null;
        RawResponseText = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (statusCode, responseBody) = await _client.SendConsoleAsync(CurrentOrg, new HttpMethod(method), path, body);
            var bytes = Encoding.UTF8.GetByteCount(responseBody);
            var success = (int)statusCode is >= 200 and <= 299;
            StatusText = $"HTTP {(int)statusCode} ({statusCode}) / {bytes:N0} bytes / {stopwatch.Elapsed.TotalMilliseconds:F0} ms";
            RawResponseText = responseBody;
            ResponseText = JsonPretty.Prettify(responseBody);
            AppendHistory(method, path, body, success ? "success" : "error", (int)stopwatch.Elapsed.TotalMilliseconds, responseBody);
            _log.Info($"REST: {method} {path} → HTTP {(int)statusCode}");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            ResponseText = ex.Message;
            RawResponseText = ex.Message;
            AppendHistory(method, path, body, "error", (int)stopwatch.Elapsed.TotalMilliseconds, ex.Message);
            _log.Error("REST リクエストに失敗", ex);
        }
        finally
        {
            IsRunning = false;
            RefreshHistoryNode();
        }
    }

    [RelayCommand]
    private void AddFavorite()
    {
        var path = Path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = UiText.T("Api_NoPathFavorite");
            return;
        }

        var label = Summarize($"{Method.Trim().ToUpperInvariant()} {path}");
        _favorites.Add(HistoryTypes.Api, label, FormatParams(Method.Trim().ToUpperInvariant(), path, string.IsNullOrWhiteSpace(RequestBody) ? null : RequestBody));
        StatusText = UiText.T("Common_FavoriteAddedFmt", label);
        RebuildTree();
    }

    /// <summary>履歴から読み込む（autoRun=true ならそのまま送信）。</summary>
    public void LoadFromHistory(HistoryEntry entry, bool autoRun)
    {
        if (entry.Params is { Length: > 0 } text)
        {
            ApplyParams(text);
        }

        if (autoRun)
        {
            _ = SendAsync();
        }
    }

    /// <summary>お気に入りから読み込む（autoRun=true ならそのまま送信）。</summary>
    public void LoadFavorite(FavoriteItem item, bool autoRun)
    {
        if (item.Payload is { Length: > 0 } text)
        {
            ApplyParams(text);
        }

        if (autoRun)
        {
            _ = SendAsync();
        }
    }

    private void ApplyParams(string text)
    {
        var parts = text.Split(BodySeparator, 2, StringSplitOptions.None);
        var firstLine = parts[0].Trim();
        var spaceIndex = firstLine.IndexOf(' ');
        if (spaceIndex > 0)
        {
            Method = firstLine[..spaceIndex].ToUpperInvariant();
            Path = firstLine[(spaceIndex + 1)..].Trim();
        }

        RequestBody = parts.Length > 1 ? parts[1] : null;
    }

    private void AppendHistory(string method, string path, string? body, string status, int durationMs, string? result)
    {
        _history.Append(
            new HistoryEntry
            {
                Type = HistoryTypes.Api,
                Org = CurrentOrg,
                Params = FormatParams(method, path, body),
                Summary = Summarize($"{method} {path}"),
                Status = status,
                DurationMs = durationMs,
            },
            result: result);
    }

    private static string FormatParams(string method, string path, string? body)
    {
        var text = $"{method} {path}";
        return body is null ? text : text + BodySeparator + body;
    }

    private static string Summarize(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
