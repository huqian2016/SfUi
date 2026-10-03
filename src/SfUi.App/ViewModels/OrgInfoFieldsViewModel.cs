using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>オブジェクト項目タブ（オブジェクト選択時に遅延取得 + キャッシュ）。</summary>
public partial class OrgInfoFieldsViewModel : ObservableObject, IOrgInfoTab, IDisposable
{
    private readonly OrgInfo _org;
    private readonly string _orgKey;
    private readonly OrgInfoService _service;
    private readonly OrgInfoCacheStore _cache;
    private readonly AppLog _log;
    private readonly ToolLauncherService _toolLauncher;

    [ObservableProperty]
    private string _title = UiText.T(OrgInfoSections.FieldsTitleKey);

    [ObservableProperty]
    private FieldTarget? _selectedObject;

    [ObservableProperty]
    private OrgInfoSectionViewModel? _section;

    [ObservableProperty]
    private string _hint = UiText.T("OrgInfo_Fields_NoSelection");

    public ObservableCollection<FieldTarget> Objects { get; } = new();

    /// <summary>選択候補（表示は「ラベル (API 名)」）。</summary>
    public sealed record FieldTarget(string ApiName, string Display)
    {
        public override string ToString() => Display;
    }

    public string? CurrentSectionId => Section?.Id;

    public bool HasData => Section?.HasData == true;

    public DateTimeOffset? FetchedAt => Section?.FetchedAt;

    /// <summary>AI パネルへ添付するテキスト（選択オブジェクトの項目。未選択は null）。</summary>
    public string? BuildAttachmentText(int maxChars) => Section?.BuildAttachmentText(maxChars);

    public OrgInfoFieldsViewModel(
        OrgInfo org,
        string orgKey,
        OrgInfoService service,
        OrgInfoCacheStore cache,
        AppLog log,
        ToolLauncherService toolLauncher)
    {
        _org = org;
        _orgKey = orgKey;
        _service = service;
        _cache = cache;
        _log = log;
        _toolLauncher = toolLauncher;
        ReloadObjectCandidates();
    }

    /// <summary>オブジェクト一覧（Objects セクションのキャッシュ）から候補を作り直す。</summary>
    public void ReloadObjectCandidates()
    {
        var previous = SelectedObject?.ApiName;
        var objectsSection = _cache.GetSection(_orgKey, OrgInfoSections.Objects);
        Objects.Clear();
        if (objectsSection is not null)
        {
            foreach (var row in objectsSection.Rows)
            {
                var apiName = row.Get("apiName") ?? row.Id;
                if (string.IsNullOrWhiteSpace(apiName))
                {
                    continue;
                }

                var label = row.Get("label");
                Objects.Add(new FieldTarget(apiName, string.IsNullOrWhiteSpace(label) ? apiName : $"{label} ({apiName})"));
            }
        }

        if (previous is not null)
        {
            SelectedObject = Objects.FirstOrDefault(o => string.Equals(o.ApiName, previous, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>検索結果から該当セクションへ切り替える（キャッシュ済みなら取得しない）。</summary>
    public void ActivateSection(string sectionId)
    {
        if (!OrgInfoSections.IsFieldsSection(sectionId))
        {
            return;
        }

        var apiName = sectionId[OrgInfoSections.FieldsPrefix.Length..];
        var target = Objects.FirstOrDefault(o => string.Equals(o.ApiName, apiName, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            SelectedObject = target;
            return;
        }

        LoadSection(apiName, fetchIfMissing: false);
    }

    /// <summary>他ウィンドウが同じセクションを更新したときに表示へ反映する（自分で取得中は除外）。</summary>
    public void HandleSectionUpdated(string sectionId)
    {
        if (Section is null
            || !string.Equals(Section.Id, sectionId, StringComparison.OrdinalIgnoreCase)
            || Section.IsLoading)
        {
            return;
        }

        var data = _cache.GetSection(_orgKey, sectionId);
        if (data is not null)
        {
            Section.Apply(data);
        }
    }

    public void Relocalize()
    {
        Title = UiText.T(OrgInfoSections.FieldsTitleKey);
        Section?.Relocalize();
        if (SelectedObject is null)
        {
            Hint = UiText.T("OrgInfo_Fields_NoSelection");
        }
    }

    public void Dispose()
    {
        Section?.Dispose();
        Section = null;
    }

    /// <summary>タブの UI オートメーション名などに使われる表示名。</summary>
    public override string ToString() => Title;

    partial void OnSelectedObjectChanged(FieldTarget? value)
    {
        if (value is null)
        {
            Section?.Dispose();
            Section = null;
            Hint = UiText.T("OrgInfo_Fields_NoSelection");
            return;
        }

        LoadSection(value.ApiName, fetchIfMissing: true);
    }

    private void LoadSection(string apiName, bool fetchIfMissing)
    {
        var sectionId = OrgInfoSections.Fields(apiName);
        if (Section is not null && string.Equals(Section.Id, sectionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Section?.Dispose();
        var section = new OrgInfoSectionViewModel(
            sectionId,
            OrgInfoSections.FieldsTitleKey,
            OrgInfoSections.FieldsColumns,
            _org,
            _orgKey,
            _service,
            _cache,
            _log,
            _toolLauncher,
            OrgInfoUrlBuilder.ObjectFieldsOrNull(_org.InstanceUrl, apiName));

        var cached = _cache.GetSection(_orgKey, sectionId);
        if (cached is not null)
        {
            section.Apply(cached);
        }

        Section = section;
        Hint = string.Empty;

        if (cached is null && fetchIfMissing)
        {
            _ = FetchSectionAsync(section, apiName);
        }
    }

    private async Task FetchSectionAsync(OrgInfoSectionViewModel section, string apiName)
    {
        Hint = UiText.T("OrgInfo_Fields_FetchingFmt", apiName);
        var success = await section.FetchAsync();
        Hint = success ? string.Empty : section.ErrorText ?? string.Empty;
    }
}
