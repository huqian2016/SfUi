using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>連続作成用のプロファイル 1 行（チェックボックス）。</summary>
public sealed partial class ScratchProfileRowViewModel : ObservableObject
{
    public ScratchProfileRowViewModel(string name) => Name = name;

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// スクラッチ組織ウィンドウの ViewModel。
/// プロファイル（作成する資源の選択）を編集し、sf コマンドのプレビューと実行ログを表示する。
/// </summary>
public sealed partial class ScratchOrgViewModel : ObservableObject, IDisposable
{
    private const int MaxLogChars = 200_000;
    private const string ZipFilter = "ZIP (*.zip)|*.zip";

    private readonly OrgService _orgs;
    private readonly ScratchProfileStore _profiles;
    private readonly ScratchOrgService _service;
    private readonly ScratchResourceService _resources;
    private readonly ScratchSignupService _signup;
    private readonly ScratchRunHistoryStore _historyStore;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly ToolLauncherService _launcher;
    private readonly AppLog _log;
    private readonly StringBuilder _logBuilder = new();
    private readonly List<string> _batchSelection = new();

    private ScratchProfile _profile = new();
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _applyingProfile;
    private string? _preferredDevHub;
    private ScratchRetrieveDiff? _retrieveDiff;
    private string? _retrieveDirectory;

    public ScratchOrgViewModel(
        OrgService orgs,
        ScratchProfileStore profiles,
        ScratchOrgService service,
        ScratchResourceService resources,
        ScratchSignupService signup,
        ScratchRunHistoryStore historyStore,
        AppPaths paths,
        IDialogService dialogs,
        IFilePickerService filePicker,
        ToolLauncherService launcher,
        AppLog log)
    {
        _orgs = orgs;
        _profiles = profiles;
        _service = service;
        _resources = resources;
        _signup = signup;
        _historyStore = historyStore;
        _paths = paths;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _launcher = launcher;
        _log = log;
    }

    public string Title => UiText.T("Scratch_Title");

    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    public ObservableCollection<string> ProfileNames { get; } = new();

    /// <summary>連続作成用のプロファイル一覧（チェックボックス）。</summary>
    public ObservableCollection<ScratchProfileRowViewModel> ProfileRows { get; } = new();

    public ObservableCollection<ScratchRunRecord> History { get; } = new();

    /// <summary>取り込み差分の一覧。</summary>
    public ObservableCollection<ScratchRetrieveChange> RetrieveChanges { get; } = new();

    /// <summary>Org Shape 一覧。</summary>
    public ObservableCollection<ScratchShapeInfo> Shapes { get; } = new();

    /// <summary>スナップショット一覧。</summary>
    public ObservableCollection<ScratchSnapshotInfo> Snapshots { get; } = new();

    /// <summary>Named Credential プリセットの表示名。</summary>
    public IReadOnlyList<string> NamedCredentialPresetNames { get; } = new[] { "SAP", "ServiceNow", "MuleSoft", "Generic" };

    /// <summary>現在のプロファイル名。</summary>
    public string ProfileName => SelectedProfileName ?? _profile.Name;

    /// <summary>プロファイル フォルダーのフルパス。</summary>
    public string ProfileFolder => _profiles.GetProfileDirectory(ProfileName);

    /// <summary>作成されるエイリアス（パターン展開後）。</summary>
    public string AliasPreview => ScratchProfileNaming.ExpandAlias(AliasPattern, ProfileName, DateTimeOffset.Now);

    public string AliasPreviewText => UiText.T("Scratch_AliasPreviewFmt", AliasPreview);

    public string FilesHint => UiText.T("Scratch_ProfileFilesHint");

    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    public bool HasHistory => History.Count > 0;

    public bool IsHistoryEmpty => History.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(BatchCreateCommand))]
    private OrgInfo? _selectedDevHub;

    [ObservableProperty]
    private string? _selectedProfileName;

    [ObservableProperty]
    private int _durationDays = ScratchProfile.DefaultDurationDays;

    [ObservableProperty]
    private int _waitMinutes = ScratchProfile.DefaultWaitMinutes;

    [ObservableProperty]
    private string _aliasPattern = ScratchProfile.DefaultAliasPattern;

    [ObservableProperty]
    private bool _setDefault;

    [ObservableProperty]
    private string _orgShapeId = string.Empty;

    /// <summary>作成元のスナップショット名（shape とは排他）。</summary>
    [ObservableProperty]
    private string _snapshotName = string.Empty;

    [ObservableProperty]
    private bool _useMetadata = true;

    [ObservableProperty]
    private bool _useCustomMetadata = true;

    [ObservableProperty]
    private bool _usePermissionSets = true;

    [ObservableProperty]
    private bool _useSeedData = true;

    [ObservableProperty]
    private bool _useStaticResources;

    [ObservableProperty]
    private bool _useConnectedApps;

    [ObservableProperty]
    private bool _useReports;

    [ObservableProperty]
    private bool _useApexScripts;

    [ObservableProperty]
    private bool _useExperienceCloud;

    [ObservableProperty]
    private bool _useNamedCredentials;

    /// <summary>選択中の Named Credential プリセット表示名。</summary>
    [ObservableProperty]
    private string _selectedNamedCredentialPreset = "SAP";

    [ObservableProperty]
    private string _permissionSetsText = string.Empty;

    // ---- 別 Org からの取り込み ----

    [ObservableProperty]
    private OrgInfo? _sourceOrg;

    [ObservableProperty]
    private bool _retrieveUseMetadata = true;

    [ObservableProperty]
    private bool _retrieveUseCustomMetadata = true;

    [ObservableProperty]
    private bool _retrieveUseStaticResources;

    [ObservableProperty]
    private bool _retrieveUseConnectedApps;

    [ObservableProperty]
    private bool _retrieveUseReports;

    [ObservableProperty]
    private bool _retrieveUseExperienceCloud;

    [ObservableProperty]
    private bool _retrieveUseNamedCredentials;

    // ---- Org Shape / スナップショット ----

    [ObservableProperty]
    private ScratchShapeInfo? _selectedShape;

    [ObservableProperty]
    private ScratchSnapshotInfo? _selectedSnapshot;

    [ObservableProperty]
    private OrgInfo? _shapeSourceOrg;

    [ObservableProperty]
    private string _snapshotSourceOrg = string.Empty;

    [ObservableProperty]
    private string _newSnapshotName = string.Empty;

    [ObservableProperty]
    private string _newSnapshotDescription = string.Empty;

    [ObservableProperty]
    private string _shapeStatus = string.Empty;

    [ObservableProperty]
    private string _snapshotStatus = string.Empty;

    [ObservableProperty]
    private bool _isShapeBusy;

    public bool IsShapeListEmpty => Shapes.Count == 0;

    public bool IsSnapshotListEmpty => Snapshots.Count == 0;

    [ObservableProperty]
    private string _retrieveStatus = string.Empty;

    [ObservableProperty]
    private string _retrieveLog = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetrieveCommand))]
    private bool _isRetrieving;

    /// <summary>反映できる差分があるか。</summary>
    public bool HasPendingRetrieveChanges => _retrieveDiff is { PendingCount: > 0 };

    public bool IsRetrieveEmpty => RetrieveChanges.Count == 0;

    /// <summary>連続作成で選んだプロファイルの表示。</summary>
    public string BatchSelectionText => UiText.T("Scratch_BatchSelectedFmt", _batchSelection.Count);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(BatchCreateCommand))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(BatchCreateCommand))]
    private bool _isLoadingOrgs;

    [ObservableProperty]
    private string _statusMessage = UiText.T("Common_Ready");

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _commandPreview = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string _warningText = string.Empty;

    [ObservableProperty]
    private string _historyDetailText = string.Empty;

    [ObservableProperty]
    private ScratchRunRecord? _selectedHistory;

    public bool CanCancel => IsRunning;

    public bool CanEdit => !IsRunning;

    // ---- 初期化 ----

    /// <summary>メイン ウィンドウで選択中の組織を Dev Hub の初期値にする。</summary>
    public void Initialize(OrgInfo? initial) =>
        _preferredDevHub = initial?.Alias ?? initial?.Username;

    /// <summary>ウィンドウ表示時の初期化（プロファイル一覧・組織一覧・履歴）。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _profiles.EnsureDefaultProfile();

        ReloadProfileNames(null);
        await LoadOrgsAsync();
        ReloadHistory();
    }

    [RelayCommand]
    private async Task RefreshOrgsAsync() => await LoadOrgsAsync();

    private async Task LoadOrgsAsync(string? selectAlias = null)
    {
        IsLoadingOrgs = true;
        try
        {
            var orgs = await _orgs.ListOrgsAsync();
            Orgs.Clear();
            foreach (var org in orgs
                         .OrderByDescending(o => o.IsDefault)
                         .ThenBy(o => o.DisplayName, StringComparer.CurrentCulture))
            {
                Orgs.Add(org);
            }

            var preferred = selectAlias ?? _profile.DevHub ?? _preferredDevHub;
            SelectedDevHub = Orgs.FirstOrDefault(o => Matches(o, preferred))
                             ?? Orgs.FirstOrDefault(o => o.IsDefault)
                             ?? Orgs.FirstOrDefault();
            SourceOrg ??= Orgs.FirstOrDefault(o => o.IsDefault) ?? Orgs.FirstOrDefault();
            ShapeSourceOrg ??= Orgs.FirstOrDefault(o => o.IsDefault) ?? Orgs.FirstOrDefault();
            StatusMessage = UiText.T("Scratch_OrgsLoadedFmt", Orgs.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _log.Warn($"スクラッチ組織: 組織一覧の取得に失敗: {ex.Message}");
        }
        finally
        {
            IsLoadingOrgs = false;
        }
    }

    private static bool Matches(OrgInfo org, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (string.Equals(org.Alias, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(org.Username, value, StringComparison.OrdinalIgnoreCase));

    private void ReloadProfileNames(string? select)
    {
        var selectedNames = _batchSelection.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProfileNames.Clear();
        foreach (var row in ProfileRows)
        {
            row.PropertyChanged -= OnProfileRowPropertyChanged;
        }

        ProfileRows.Clear();
        foreach (var name in _profiles.ListProfiles())
        {
            ProfileNames.Add(name);
            var row = new ScratchProfileRowViewModel(name) { IsSelected = selectedNames.Contains(name) };
            row.PropertyChanged += OnProfileRowPropertyChanged;
            ProfileRows.Add(row);
        }

        UpdateBatchSelection();

        var target = ProfileNames.FirstOrDefault(n => string.Equals(n, select, StringComparison.OrdinalIgnoreCase))
                     ?? ProfileNames.FirstOrDefault();
        if (target is null)
        {
            return;
        }

        if (string.Equals(SelectedProfileName, target, StringComparison.Ordinal))
        {
            LoadProfile(target);
        }
        else
        {
            SelectedProfileName = target;
        }
    }

    private void OnProfileRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScratchProfileRowViewModel.IsSelected))
        {
            UpdateBatchSelection();
        }
    }

    private void UpdateBatchSelection()
    {
        _batchSelection.Clear();
        _batchSelection.AddRange(ProfileRows.Where(row => row.IsSelected).Select(row => row.Name));
        OnPropertyChanged(nameof(BatchSelectionText));
        BatchCreateCommand.NotifyCanExecuteChanged();
    }

    private void LoadProfile(string name)
    {
        _profile = _profiles.Ensure(name);
        ClearRetrieveState();
        ApplyProfileValues(_profile);
    }

    /// <summary>取り込み結果（差分とログ）をクリアする。</summary>
    private void ClearRetrieveState()
    {
        _retrieveDiff = null;
        _retrieveDirectory = null;
        RetrieveChanges.Clear();
        RetrieveStatus = string.Empty;
        RetrieveLog = string.Empty;
        OnPropertyChanged(nameof(HasPendingRetrieveChanges));
        OnPropertyChanged(nameof(IsRetrieveEmpty));
        ApplyRetrievedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>プロファイルの値を画面へ反映する（プロファイル読み込み時）。</summary>
    private void ApplyProfileValues(ScratchProfile profile)
    {
        _applyingProfile = true;
        try
        {
            DurationDays = profile.DurationDays;
            WaitMinutes = profile.WaitMinutes;
            AliasPattern = profile.AliasPattern;
            SetDefault = profile.SetDefault;
            OrgShapeId = profile.OrgShapeId ?? string.Empty;
            SnapshotName = profile.SnapshotName ?? string.Empty;
            UseMetadata = profile.UseMetadata;
            UseCustomMetadata = profile.UseCustomMetadata;
            UsePermissionSets = profile.UsePermissionSets;
            UseSeedData = profile.UseSeedData;
            UseStaticResources = profile.UseStaticResources;
            UseConnectedApps = profile.UseConnectedApps;
            UseReports = profile.UseReports;
            UseApexScripts = profile.UseApexScripts;
            UseExperienceCloud = profile.UseExperienceCloud;
            UseNamedCredentials = profile.UseNamedCredentials;
            PermissionSetsText = string.Join(Environment.NewLine, profile.PermissionSets);

            if (!string.IsNullOrWhiteSpace(profile.DevHub))
            {
                var match = Orgs.FirstOrDefault(o => Matches(o, profile.DevHub));
                if (match is not null)
                {
                    SelectedDevHub = match;
                }
            }
        }
        finally
        {
            _applyingProfile = false;
        }

        RefreshPreview();
    }

    /// <summary>画面の値をプロファイルへ取り込む。</summary>
    private ScratchProfile CaptureProfile()
    {
        var profile = _profile.Clone();
        profile.Name = ScratchProfileNaming.SanitizeName(ProfileName);
        profile.DevHub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username ?? profile.DevHub;
        profile.DurationDays = DurationDays;
        profile.WaitMinutes = WaitMinutes;
        profile.AliasPattern = AliasPattern;
        profile.SetDefault = SetDefault;
        profile.OrgShapeId = string.IsNullOrWhiteSpace(OrgShapeId) ? null : OrgShapeId.Trim();
        profile.SnapshotName = string.IsNullOrWhiteSpace(SnapshotName) ? null : SnapshotName.Trim();
        profile.UseMetadata = UseMetadata;
        profile.UseCustomMetadata = UseCustomMetadata;
        profile.UsePermissionSets = UsePermissionSets;
        profile.UseSeedData = UseSeedData;
        profile.UseStaticResources = UseStaticResources;
        profile.UseConnectedApps = UseConnectedApps;
        profile.UseReports = UseReports;
        profile.UseApexScripts = UseApexScripts;
        profile.UseExperienceCloud = UseExperienceCloud;
        profile.UseNamedCredentials = UseNamedCredentials;
        profile.PermissionSets = PermissionSetsText
            .Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.Normalize();
        return profile;
    }

    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(ProfileFolder));
        OnPropertyChanged(nameof(AliasPreview));
        OnPropertyChanged(nameof(AliasPreviewText));

        var profile = CaptureProfile();
        var devHub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username ?? "<DevHub>";
        var alias = ScratchProfileNaming.ExpandAlias(profile.AliasPattern, profile.Name, DateTimeOffset.Now);
        var files = _profiles.GetFiles(profile.Name);
        var hasSources = ScratchOrgService.HasSourceFiles(files.PackageDirectory);
        var usesDeploy = profile.UseMetadata || profile.UseCustomMetadata || profile.UseStaticResources
                         || profile.UseConnectedApps || profile.UseReports;

        var warnings = new List<string>();
        if (!File.Exists(files.DefinitionFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingDefinitionFmt", files.DefinitionFile));
        }

        if (profile.UseMetadata && !File.Exists(files.ManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingManifestFmt", files.ManifestFile));
        }

        if (profile.UseCustomMetadata && !File.Exists(files.CustomMetadataManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingCustomMetadataFmt", files.CustomMetadataManifestFile));
        }

        if (profile.UseStaticResources && !File.Exists(files.StaticResourcesManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingStaticResourcesFmt", files.StaticResourcesManifestFile));
        }

        if (profile.UseConnectedApps && !File.Exists(files.ConnectedAppsManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingConnectedAppsFmt", files.ConnectedAppsManifestFile));
        }

        if (profile.UseReports && !File.Exists(files.ReportsManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingReportsFmt", files.ReportsManifestFile));
        }

        if (profile.UseExperienceCloud && !File.Exists(files.ExperienceManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingExperienceFmt", files.ExperienceManifestFile));
        }

        if (profile.UseNamedCredentials && !File.Exists(files.NamedCredentialsManifestFile))
        {
            warnings.Add(UiText.T("Scratch_WarnMissingNamedCredentialsFmt", files.NamedCredentialsManifestFile));
        }

        if (profile.UseExperienceCloud && !ScratchDefinitionEditor.HasExperienceSettings(files.DefinitionFile))
        {
            warnings.Add(UiText.T("Scratch_WarnExperienceWithoutSettings"));
        }

        if (!string.IsNullOrWhiteSpace(profile.OrgShapeId) && !string.IsNullOrWhiteSpace(profile.SnapshotName))
        {
            warnings.Add(UiText.T("Scratch_InvalidShapeAndSnapshot"));
        }

        if (usesDeploy && !hasSources)
        {
            warnings.Add(UiText.T("Scratch_WarnNoSources"));
        }

        if (profile.UseCustomMetadata && !profile.UseMetadata)
        {
            warnings.Add(UiText.T("Scratch_WarnCustomMetadataWithoutMetadata"));
        }

        if (profile.UsePermissionSets && !profile.UseMetadata)
        {
            warnings.Add(UiText.T("Scratch_WarnPermissionSetsWithoutMetadata"));
        }

        if (profile.UseSeedData && !ScratchOrgService.HasSeedObjects(files.SeedPlanFile))
        {
            warnings.Add(UiText.T("Scratch_WarnNoSeedPlan"));
        }

        if (profile.UsePermissionSets && profile.PermissionSets.Count == 0)
        {
            warnings.Add(UiText.T("Scratch_WarnNoPermissionSets"));
        }

        if (profile.UseApexScripts && ScratchOrgService.ListApexScripts(files.ApexScriptsDirectory).Count == 0)
        {
            warnings.Add(UiText.T("Scratch_WarnNoApexScripts"));
        }

        WarningText = string.Join(Environment.NewLine, warnings);

        var steps = ScratchOrgService.BuildPlan(profile, devHub, alias, files);
        var builder = new StringBuilder();
        for (var index = 0; index < steps.Count; index++)
        {
            builder.AppendLine($"{index + 1}. {steps[index].CommandLine}");
        }

        CommandPreview = builder.ToString().TrimEnd();
    }

    // ---- プロファイル操作 ----

    [RelayCommand]
    private void SaveProfile()
    {
        if (!TrySaveProfile())
        {
            return;
        }

        StatusMessage = UiText.T("Scratch_SavedFmt", ProfileName);
    }

    /// <summary>画面の値を検証してプロファイルへ保存する（成功したら true）。</summary>
    private bool TrySaveProfile()
    {
        var profile = CaptureProfile();
        if (!ScratchProfileNaming.IsValidName(profile.Name))
        {
            _dialogs.Warning(UiText.T("Scratch_InvalidProfileName"), Title);
            return false;
        }

        _profiles.Save(profile);
        _profile = profile;
        ApplyProfileValues(profile);
        return true;
    }

    [RelayCommand]
    private void NewProfile()
    {
        var name = _dialogs.Prompt(
            UiText.T("Scratch_NewProfileTitle"),
            UiText.T("Scratch_NewProfilePrompt"),
            $"profile-{DateTime.Now:yyyyMMdd}");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var sanitized = ScratchProfileNaming.SanitizeName(name);
        if (Directory.Exists(_profiles.GetProfileDirectory(sanitized)))
        {
            _dialogs.Warning(UiText.T("Scratch_ProfileExistsFmt", sanitized), Title);
            return;
        }

        _profiles.Ensure(sanitized);
        ReloadProfileNames(sanitized);
        StatusMessage = UiText.T("Scratch_ProfileCreatedFmt", sanitized);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        var name = ProfileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = UiText.T("Scratch_NeedProfile");
            return;
        }

        if (!_dialogs.ConfirmDestructive(UiText.T("Scratch_DeleteConfirmFmt", name), Title))
        {
            return;
        }

        if (_profiles.ListProfiles().Count <= 1)
        {
            _dialogs.Warning(UiText.T("Scratch_CannotDeleteLastProfile"), Title);
            return;
        }

        _profiles.Delete(name);
        ReloadProfileNames(_profiles.ListProfiles().FirstOrDefault());
        StatusMessage = UiText.T("Scratch_DeletedFmt", name);
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        var path = await _filePicker.SaveFileAsync(
            UiText.T("Scratch_ExportTitle"), $"{ProfileName}.zip", ZipFilter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _profiles.Export(ProfileName, path);
            StatusMessage = UiText.T("Scratch_ExportedFmt", path);
            _dialogs.Info(UiText.T("Scratch_ExportedFmt", path), Title);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, Title);
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        var path = await _filePicker.OpenFileAsync(UiText.T("Scratch_ImportTitle"), ZipFilter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var name = _dialogs.Prompt(
            UiText.T("Scratch_ImportNameTitle"),
            UiText.T("Scratch_ImportNamePrompt"),
            Path.GetFileNameWithoutExtension(path));
        try
        {
            var imported = _profiles.Import(path, name);
            ReloadProfileNames(imported);
            StatusMessage = UiText.T("Scratch_ImportedFmt", imported);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, Title);
        }
    }

    [RelayCommand]
    private void OpenProfileFolder()
    {
        try
        {
            var directory = _profiles.GetProfileDirectory(ProfileName);
            Directory.CreateDirectory(directory);
            var result = _launcher.LaunchExplorer(directory);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
    }

    // ---- Org Shape / スナップショット ----

    [RelayCommand]
    private async Task RefreshShapesAsync()
    {
        if (IsShapeBusy)
        {
            return;
        }

        IsShapeBusy = true;
        ShapeStatus = UiText.T("Scratch_ShapeLoading");
        try
        {
            await RefreshShapesCoreAsync();
        }
        finally
        {
            IsShapeBusy = false;
        }
    }

    private async Task RefreshShapesCoreAsync()
    {
        var result = await _signup.ListShapesAsync(CancellationToken.None);
        if (!result.Success)
        {
            ShapeStatus = UiText.T("Scratch_ShapeFailedFmt", result.ErrorText);
            return;
        }

        var shapes = ScratchSignupService.ParseShapes(result.Output);
        Shapes.Clear();
        foreach (var shape in shapes)
        {
            Shapes.Add(shape);
        }

        ShapeStatus = UiText.T("Scratch_ShapeLoadedFmt", Shapes.Count);
        OnPropertyChanged(nameof(IsShapeListEmpty));
    }

    [RelayCommand]
    private async Task CreateShapeAsync()
    {
        var source = ShapeSourceOrg?.Alias ?? ShapeSourceOrg?.Username;
        if (string.IsNullOrWhiteSpace(source))
        {
            ShapeStatus = UiText.T("Scratch_ShapeNeedSource");
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Scratch_ShapeCreateConfirmFmt", source), Title))
        {
            return;
        }

        await RunSignupOperationAsync(
            token => _signup.CreateShapeAsync(source, token),
            UiText.T("Scratch_ShapeCreatedHint"),
            refreshShapes: true);
    }

    [RelayCommand]
    private async Task DeleteShapeAsync()
    {
        var source = ShapeSourceOrg?.Alias ?? ShapeSourceOrg?.Username;
        if (string.IsNullOrWhiteSpace(source))
        {
            ShapeStatus = UiText.T("Scratch_ShapeNeedSource");
            return;
        }

        if (!_dialogs.ConfirmDestructive(UiText.T("Scratch_ShapeDeleteConfirmFmt", source), Title))
        {
            return;
        }

        await RunSignupOperationAsync(
            token => _signup.DeleteShapeAsync(source, token),
            UiText.T("Scratch_ShapeDeletedFmt", source),
            refreshShapes: true);
    }

    private async Task RunSignupOperationAsync(
        Func<CancellationToken, Task<ScratchSignupResult>> operation, string successMessage, bool refreshShapes)
    {
        if (IsShapeBusy)
        {
            return;
        }

        IsShapeBusy = true;
        try
        {
            var result = await operation(CancellationToken.None);
            ShapeStatus = result.Success
                ? successMessage
                : UiText.T("Scratch_ShapeFailedFmt", result.ErrorText);
            if (result.Success && refreshShapes)
            {
                await RefreshShapesCoreAsync();
            }
        }
        finally
        {
            IsShapeBusy = false;
        }
    }

    [RelayCommand]
    private void UseShapeInProfile()
    {
        var shape = SelectedShape;
        if (shape is null || string.IsNullOrWhiteSpace(shape.Id))
        {
            ShapeStatus = UiText.T("Scratch_ShapeSelect");
            return;
        }

        OrgShapeId = shape.Id;
        SnapshotName = string.Empty;
        if (!TrySaveProfile())
        {
            return;
        }

        ShapeStatus = UiText.T("Scratch_ShapeUseDoneFmt", shape.Id);
    }

    [RelayCommand]
    private async Task RefreshSnapshotsAsync()
    {
        if (IsShapeBusy)
        {
            return;
        }

        IsShapeBusy = true;
        SnapshotStatus = UiText.T("Scratch_SnapshotLoading");
        try
        {
            await RefreshSnapshotsCoreAsync();
        }
        finally
        {
            IsShapeBusy = false;
        }
    }

    private async Task RefreshSnapshotsCoreAsync()
    {
        var hub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
        if (string.IsNullOrWhiteSpace(hub))
        {
            SnapshotStatus = UiText.T("Scratch_NoDevHub");
            return;
        }

        var result = await _signup.ListSnapshotsAsync(hub, CancellationToken.None);
        if (!result.Success)
        {
            SnapshotStatus = UiText.T("Scratch_SnapshotFailedFmt", result.ErrorText);
            return;
        }

        var snapshots = ScratchSignupService.ParseSnapshots(result.Output);
        Snapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            Snapshots.Add(snapshot);
        }

        SnapshotStatus = UiText.T("Scratch_SnapshotLoadedFmt", Snapshots.Count);
        OnPropertyChanged(nameof(IsSnapshotListEmpty));
    }

    private static bool IsValidSnapshotName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 80
        && char.IsLetter(name[0])
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    [RelayCommand]
    private async Task CreateSnapshotAsync()
    {
        var hub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
        var source = SnapshotSourceOrg?.Trim() ?? string.Empty;
        var name = NewSnapshotName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hub))
        {
            SnapshotStatus = UiText.T("Scratch_NoDevHub");
            return;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            SnapshotStatus = UiText.T("Scratch_SnapshotNeedSource");
            return;
        }

        if (!IsValidSnapshotName(name))
        {
            SnapshotStatus = UiText.T("Scratch_InvalidSnapshotName");
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Scratch_SnapshotCreateConfirmFmt", hub, source, name), Title))
        {
            return;
        }

        await RunSnapshotOperationAsync(
            token => _signup.CreateSnapshotAsync(hub, source, name, NewSnapshotDescription, token),
            UiText.T("Scratch_SnapshotCreatedFmt", name),
            refreshSnapshots: true);
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync()
    {
        var hub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
        var snapshot = SelectedSnapshot;
        if (string.IsNullOrWhiteSpace(hub))
        {
            SnapshotStatus = UiText.T("Scratch_NoDevHub");
            return;
        }

        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Name))
        {
            SnapshotStatus = UiText.T("Scratch_SnapshotSelect");
            return;
        }

        if (!_dialogs.ConfirmDestructive(UiText.T("Scratch_SnapshotDeleteConfirmFmt", snapshot.Name), Title))
        {
            return;
        }

        await RunSnapshotOperationAsync(
            token => _signup.DeleteSnapshotAsync(hub, snapshot.Name, token),
            UiText.T("Scratch_SnapshotDeletedFmt", snapshot.Name),
            refreshSnapshots: true);
    }

    private async Task RunSnapshotOperationAsync(
        Func<CancellationToken, Task<ScratchSignupResult>> operation, string successMessage, bool refreshSnapshots)
    {
        if (IsShapeBusy)
        {
            return;
        }

        IsShapeBusy = true;
        try
        {
            var result = await operation(CancellationToken.None);
            SnapshotStatus = result.Success
                ? successMessage
                : UiText.T("Scratch_SnapshotFailedFmt", result.ErrorText);
            if (result.Success && refreshSnapshots)
            {
                await RefreshSnapshotsCoreAsync();
            }
        }
        finally
        {
            IsShapeBusy = false;
        }
    }

    [RelayCommand]
    private void UseSnapshotInProfile()
    {
        var snapshot = SelectedSnapshot;
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Name))
        {
            SnapshotStatus = UiText.T("Scratch_SnapshotSelect");
            return;
        }

        SnapshotName = snapshot.Name;
        OrgShapeId = string.Empty;
        if (!TrySaveProfile())
        {
            return;
        }

        SnapshotStatus = UiText.T("Scratch_SnapshotUseDoneFmt", snapshot.Name);
    }

    // ---- Experience Cloud / Named Credential の補助 ----

    [RelayCommand]
    private void AddExperienceSettings()
    {
        var files = _profiles.GetFiles(ProfileName);
        if (!File.Exists(files.DefinitionFile))
        {
            StatusMessage = UiText.T("Scratch_MissingDefinitionFmt", files.DefinitionFile);
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Scratch_ExperienceAddConfirm"), Title))
        {
            return;
        }

        try
        {
            var changed = ScratchDefinitionEditor.AddExperienceSettings(files.DefinitionFile);
            UseExperienceCloud = true;
            if (!TrySaveProfile())
            {
                return;
            }

            StatusMessage = UiText.T(changed ? "Scratch_ExperienceAddedFmt" : "Scratch_ExperienceAlreadyFmt");
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, Title);
        }
    }

    [RelayCommand]
    private void AddNamedCredentialPreset()
    {
        var apiName = _dialogs.Prompt(
            UiText.T("Scratch_PresetNameTitle"),
            UiText.T("Scratch_PresetNamePrompt"),
            "SapApi");
        if (string.IsNullOrWhiteSpace(apiName))
        {
            return;
        }

        if (!ScratchNamedCredentialPresets.IsValidApiName(apiName))
        {
            _dialogs.Warning(UiText.T("Scratch_InvalidApiName"), Title);
            return;
        }

        var preset = SelectedNamedCredentialPreset switch
        {
            "ServiceNow" => ScratchNamedCredentialPreset.ServiceNow,
            "MuleSoft" => ScratchNamedCredentialPreset.MuleSoft,
            "Generic" => ScratchNamedCredentialPreset.Generic,
            _ => ScratchNamedCredentialPreset.Sap,
        };

        try
        {
            var path = ScratchNamedCredentialPresets.Write(
                _profiles.GetProfileDirectory(ProfileName), apiName.Trim(), preset);
            if (path is null)
            {
                StatusMessage = UiText.T("Scratch_PresetExistsFmt", apiName.Trim());
                _dialogs.Warning(StatusMessage, Title);
                return;
            }

            UseNamedCredentials = true;
            TrySaveProfile();
            StatusMessage = UiText.T("Scratch_PresetAddedFmt", path);
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, Title);
        }
    }

    // ---- 実行 ----

    private bool CanRun() => !IsRunning && !IsLoadingOrgs && SelectedDevHub is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var profile = CaptureProfile();
        var devHub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
        var errors = ValidateForRun(profile, devHub);
        if (errors.Count > 0)
        {
            StatusMessage = errors[0];
            _dialogs.Warning(string.Join(Environment.NewLine, errors), Title);
            return;
        }

        profile.DevHub = devHub;
        _profiles.Save(profile);
        _profile = profile;
        ApplyProfileValues(profile);

        var alias = ScratchProfileNaming.ExpandAlias(profile.AliasPattern, profile.Name, DateTimeOffset.Now);
        if (!_dialogs.Confirm(UiText.T("Scratch_RunConfirmFmt", devHub!, alias), Title))
        {
            return;
        }

        var (success, resultAlias) = await RunProfileCoreAsync(profile, devHub!, showDialogs: true, resetLog: true);
        if (success)
        {
            await LoadOrgsAsync(resultAlias);
        }
    }

    /// <summary>実行前の検証（Dev Hub / プロファイル / 必須ファイル）。</summary>
    private List<string> ValidateForRun(ScratchProfile profile, string? devHub)
    {
        var files = _profiles.GetFiles(profile.Name);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(devHub))
        {
            errors.Add(UiText.T("Scratch_NoDevHub"));
        }

        errors.AddRange(profile.Validate());
        if (!File.Exists(files.DefinitionFile))
        {
            errors.Add(UiText.T("Scratch_MissingDefinitionFmt", files.DefinitionFile));
        }

        if (profile.UseMetadata && !File.Exists(files.ManifestFile))
        {
            errors.Add(UiText.T("Scratch_MissingManifestFmt", files.ManifestFile));
        }

        if (profile.UseCustomMetadata && !File.Exists(files.CustomMetadataManifestFile))
        {
            errors.Add(UiText.T("Scratch_MissingCustomMetadataFmt", files.CustomMetadataManifestFile));
        }

        return errors;
    }

    /// <summary>1 プロファイル分の作成を実行する（成功・失敗を問わず履歴に記録する）。</summary>
    private async Task<(bool Success, string Alias)> RunProfileCoreAsync(
        ScratchProfile profile,
        string devHub,
        bool showDialogs,
        bool resetLog)
    {
        if (resetLog)
        {
            ResetLog();
        }

        var files = _profiles.GetFiles(profile.Name);
        var alias = ScratchProfileNaming.ExpandAlias(profile.AliasPattern, profile.Name, DateTimeOffset.Now);
        var steps = ScratchOrgService.BuildPlan(profile, devHub, alias, files);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        var started = DateTimeOffset.Now;
        ScratchOrgRunResult? runResult = null;
        try
        {
            var progress = new Progress<ScratchOrgProgress>(OnProgress);
            runResult = await _service.RunAsync(steps, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiText.T("Scratch_Cancelled");
            AppendLog(UiText.T("Scratch_Cancelled"));
        }
        catch (Exception ex)
        {
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            AppendLog(ex.Message);
            _log.Error("スクラッチ組織の作成に失敗", ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (runResult is null)
        {
            return (false, alias);
        }

        var record = new ScratchRunRecord
        {
            StartedAt = started,
            FinishedAt = DateTimeOffset.Now,
            ProfileName = profile.Name,
            Alias = alias,
            DevHub = devHub,
            Success = runResult.Success,
            Message = runResult.Success ? string.Empty : (runResult.ErrorText ?? string.Empty),
            Steps = runResult.Steps.ToList(),
        };
        _historyStore.Add(record);
        ReloadHistory();

        if (runResult.Success)
        {
            StatusMessage = UiText.T("Scratch_RunDoneFmt", alias);
            if (showDialogs)
            {
                _dialogs.Info(
                    UiText.T("Scratch_RunDoneFmt", alias) + Environment.NewLine + Environment.NewLine +
                    UiText.T("Scratch_RunDoneHintFmt", alias),
                    Title);
            }

            return (true, alias);
        }

        StatusMessage = UiText.T("Scratch_RunFailedFmt", runResult.FailedStepLabel ?? string.Empty);
        if (showDialogs)
        {
            _dialogs.Warning(
                $"{runResult.FailedStepLabel}: {runResult.ErrorText}" + Environment.NewLine +
                UiText.T("Scratch_RunFailedHint"),
                Title);
        }

        return (false, alias);
    }

    // ---- 複数プロファイルの連続作成 ----

    private bool CanBatchCreate() => !IsRunning && !IsLoadingOrgs && SelectedDevHub is not null && _batchSelection.Count > 0;

    [RelayCommand(CanExecute = nameof(CanBatchCreate))]
    private async Task BatchCreateAsync()
    {
        var devHub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
        var profiles = _batchSelection.Select(name => _profiles.Ensure(name)).ToList();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(devHub))
        {
            errors.Add(UiText.T("Scratch_NoDevHub"));
        }

        if (profiles.Count == 0)
        {
            errors.Add(UiText.T("Scratch_BatchNoSelection"));
        }

        var aliases = profiles
            .Select(p => ScratchProfileNaming.ExpandAlias(p.AliasPattern, p.Name, DateTimeOffset.Now))
            .ToList();
        var duplicate = aliases
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            errors.Add(UiText.T("Scratch_BatchDuplicateAliasFmt", duplicate.Key));
        }

        foreach (var profile in profiles)
        {
            errors.AddRange(ValidateForRun(profile, devHub));
        }

        if (errors.Count > 0)
        {
            StatusMessage = errors[0];
            _dialogs.Warning(string.Join(Environment.NewLine, errors.Distinct()), Title);
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Scratch_BatchConfirmFmt", profiles.Count), Title))
        {
            return;
        }

        ResetLog();
        var succeeded = 0;
        foreach (var profile in profiles)
        {
            AppendLog(UiText.T("Scratch_BatchProfileFmt", profile.Name));
            var (success, _) = await RunProfileCoreAsync(profile, devHub!, showDialogs: false, resetLog: false);
            if (!success)
            {
                break;
            }

            succeeded++;
        }

        ReloadHistory();
        var summary = UiText.T("Scratch_BatchSummaryFmt", succeeded, profiles.Count);
        StatusMessage = summary;
        _dialogs.Info(summary, Title);
    }

    // ---- 別 Org からの取り込み ----

    private bool CanRetrieve() => !IsRetrieving && SourceOrg is not null;

    [RelayCommand(CanExecute = nameof(CanRetrieve))]
    private async Task RetrieveAsync()
    {
        var profile = CaptureProfile();
        var source = SourceOrg?.Alias ?? SourceOrg?.Username;
        if (string.IsNullOrWhiteSpace(source))
        {
            RetrieveStatus = UiText.T("Scratch_ImportNoSource");
            return;
        }

        var files = _profiles.GetFiles(profile.Name);
        var manifests = new List<(string Path, string Label)>();
        if (RetrieveUseMetadata)
        {
            manifests.Add((files.ManifestFile, UiText.T("Scratch_UseMetadata")));
        }

        if (RetrieveUseCustomMetadata)
        {
            manifests.Add((files.CustomMetadataManifestFile, UiText.T("Scratch_UseCustomMetadata")));
        }

        if (RetrieveUseStaticResources)
        {
            manifests.Add((files.StaticResourcesManifestFile, UiText.T("Scratch_UseStaticResources")));
        }

        if (RetrieveUseConnectedApps)
        {
            manifests.Add((files.ConnectedAppsManifestFile, UiText.T("Scratch_UseConnectedApps")));
        }

        if (RetrieveUseReports)
        {
            manifests.Add((files.ReportsManifestFile, UiText.T("Scratch_UseReports")));
        }

        if (RetrieveUseExperienceCloud)
        {
            manifests.Add((files.ExperienceManifestFile, UiText.T("Scratch_UseExperienceCloud")));
        }

        if (RetrieveUseNamedCredentials)
        {
            manifests.Add((files.NamedCredentialsManifestFile, UiText.T("Scratch_UseNamedCredentials")));
        }

        if (manifests.Count == 0)
        {
            RetrieveStatus = UiText.T("Scratch_ImportNoGroup");
            return;
        }

        var missing = manifests.FirstOrDefault(item => !File.Exists(item.Path));
        if (!string.IsNullOrWhiteSpace(missing.Path))
        {
            RetrieveStatus = UiText.T("Scratch_RetrieveNoManifestFmt", missing.Label, missing.Path);
            return;
        }

        IsRetrieving = true;
        RetrieveStatus = UiText.T("Scratch_Retrieving", source);
        RetrieveLog = string.Empty;
        RetrieveChanges.Clear();
        _retrieveDiff = null;
        OnPropertyChanged(nameof(IsRetrieveEmpty));
        OnPropertyChanged(nameof(HasPendingRetrieveChanges));
        ApplyRetrievedCommand.NotifyCanExecuteChanged();

        var retrieveDirectory = Path.Combine(_paths.TempDirectory, $"scratch-retrieve-{Guid.NewGuid():N}");
        _retrieveDirectory = retrieveDirectory;
        try
        {
            var profileDirectory = _profiles.GetProfileDirectory(profile.Name);
            foreach (var manifest in manifests)
            {
                AppendRetrieveLog($"{manifest.Label}: {manifest.Path}");
                var result = await _resources.RetrieveAsync(
                    manifest.Path, source, profileDirectory, retrieveDirectory, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    AppendRetrieveLog(result.Output);
                }

                if (!result.Success)
                {
                    RetrieveStatus = UiText.T("Scratch_RetrieveFailedFmt", result.ErrorText);
                    return;
                }
            }

            var diff = ScratchResourceService.Compare(retrieveDirectory, files.PackageDirectory);
            _retrieveDiff = diff;
            foreach (var change in diff.Changes)
            {
                RetrieveChanges.Add(change);
            }

            RetrieveStatus = UiText.T("Scratch_RetrieveDoneFmt", diff.AddedCount, diff.ChangedCount, diff.SameCount);
        }
        catch (Exception ex)
        {
            RetrieveStatus = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("スクラッチ組織: 取り込みに失敗", ex);
        }
        finally
        {
            IsRetrieving = false;
            OnPropertyChanged(nameof(IsRetrieveEmpty));
            OnPropertyChanged(nameof(HasPendingRetrieveChanges));
            ApplyRetrievedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApplyRetrieved() => !IsRetrieving && _retrieveDiff is { PendingCount: > 0 };

    [RelayCommand(CanExecute = nameof(CanApplyRetrieved))]
    private void ApplyRetrieved()
    {
        if (_retrieveDiff is null || string.IsNullOrWhiteSpace(_retrieveDirectory))
        {
            return;
        }

        if (!_dialogs.Confirm(UiText.T("Scratch_ApplyConfirmFmt", _retrieveDiff.PendingCount), Title))
        {
            return;
        }

        var files = _profiles.GetFiles(ProfileName);
        var applied = ScratchResourceService.Apply(_retrieveDiff, files.PackageDirectory);
        var remaining = ScratchResourceService.Compare(_retrieveDirectory, files.PackageDirectory);
        _retrieveDiff = remaining;
        RetrieveChanges.Clear();
        foreach (var change in remaining.Changes)
        {
            RetrieveChanges.Add(change);
        }

        RetrieveStatus = UiText.T("Scratch_AppliedFmt", applied);
        StatusMessage = UiText.T("Scratch_AppliedFmt", applied);
        RefreshPreview();
        OnPropertyChanged(nameof(IsRetrieveEmpty));
        OnPropertyChanged(nameof(HasPendingRetrieveChanges));
        ApplyRetrievedCommand.NotifyCanExecuteChanged();
    }

    private void AppendRetrieveLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        RetrieveLog = string.IsNullOrEmpty(RetrieveLog) ? line : RetrieveLog + Environment.NewLine + line;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = UiText.T("Scratch_Cancelling");
    }

    private void OnProgress(ScratchOrgProgress progress)
    {
        if (string.Equals(progress.Phase, "start", StringComparison.Ordinal))
        {
            StatusMessage = UiText.T("Scratch_RunningFmt", progress.Step.Label);
            AppendLog(UiText.T("Scratch_StepStartFmt", progress.Index, progress.Total, progress.Step.Label));
            return;
        }

        var result = progress.Result;
        if (result is null)
        {
            return;
        }

        AppendLog(result.Success
            ? UiText.T("Scratch_StepDoneFmt", progress.Index, progress.Total, progress.Step.Label, result.DurationMs)
            : UiText.T("Scratch_StepFailedFmt", progress.Index, progress.Total, progress.Step.Label, result.ExitCode));

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            AppendLog(result.Output);
        }
    }

    // ---- 履歴 ----

    private void ReloadHistory()
    {
        var previousId = SelectedHistory?.Id;
        History.Clear();
        foreach (var record in _historyStore.Load())
        {
            History.Add(record);
        }

        SelectedHistory = History.FirstOrDefault(r => r.Id == previousId) ?? History.FirstOrDefault();
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    partial void OnSelectedHistoryChanged(ScratchRunRecord? value) =>
        HistoryDetailText = value?.ToDetailText() ?? string.Empty;

    [RelayCommand]
    private void ClearHistory()
    {
        if (History.Count == 0)
        {
            return;
        }

        if (!_dialogs.ConfirmDestructive(UiText.T("Scratch_HistoryClearConfirm"), Title))
        {
            return;
        }

        _historyStore.Clear();
        ReloadHistory();
        StatusMessage = UiText.T("Scratch_HistoryCleared");
    }

    // ---- 変更の反映 ----

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        LoadProfile(value);
    }

    partial void OnSelectedDevHubChanged(OrgInfo? value) => RefreshPreview();

    partial void OnDurationDaysChanged(int value)
    {
        if (!_applyingProfile)
        {
            _profile.DurationDays = value;
        }

        RefreshPreview();
    }

    partial void OnWaitMinutesChanged(int value)
    {
        if (!_applyingProfile)
        {
            _profile.WaitMinutes = value;
        }

        RefreshPreview();
    }

    partial void OnAliasPatternChanged(string value)
    {
        if (!_applyingProfile)
        {
            _profile.AliasPattern = value;
        }

        RefreshPreview();
    }

    partial void OnSetDefaultChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.SetDefault = value;
        }

        RefreshPreview();
    }

    partial void OnOrgShapeIdChanged(string value)
    {
        if (!_applyingProfile)
        {
            _profile.OrgShapeId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        RefreshPreview();
    }

    partial void OnUseMetadataChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseMetadata = value;
        }

        RefreshPreview();
    }

    partial void OnUseCustomMetadataChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseCustomMetadata = value;
        }

        RefreshPreview();
    }

    partial void OnUsePermissionSetsChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UsePermissionSets = value;
        }

        RefreshPreview();
    }

    partial void OnUseSeedDataChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseSeedData = value;
        }

        RefreshPreview();
    }

    partial void OnUseStaticResourcesChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseStaticResources = value;
        }

        RefreshPreview();
    }

    partial void OnUseConnectedAppsChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseConnectedApps = value;
        }

        RefreshPreview();
    }

    partial void OnUseReportsChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseReports = value;
        }

        RefreshPreview();
    }

    partial void OnUseApexScriptsChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseApexScripts = value;
        }

        RefreshPreview();
    }

    partial void OnUseExperienceCloudChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseExperienceCloud = value;
        }

        RefreshPreview();
    }

    partial void OnUseNamedCredentialsChanged(bool value)
    {
        if (!_applyingProfile)
        {
            _profile.UseNamedCredentials = value;
        }

        RefreshPreview();
    }

    partial void OnSnapshotNameChanged(string value)
    {
        if (!_applyingProfile)
        {
            _profile.SnapshotName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        RefreshPreview();
    }

    partial void OnSourceOrgChanged(OrgInfo? value) => RetrieveCommand.NotifyCanExecuteChanged();

    partial void OnPermissionSetsTextChanged(string value) => RefreshPreview();

    // ---- ログ ----

    private void ResetLog()
    {
        _logBuilder.Clear();
        LogText = string.Empty;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _logBuilder.AppendLine(line);
        if (_logBuilder.Length > MaxLogChars)
        {
            _logBuilder.Remove(0, _logBuilder.Length - MaxLogChars);
        }

        LogText = _logBuilder.ToString();
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
            // 終了時のキャンセル失敗は無視
        }

        _cts = null;
    }
}
