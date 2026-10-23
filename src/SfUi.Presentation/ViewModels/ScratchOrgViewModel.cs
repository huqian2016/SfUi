using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

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
    private readonly ScratchRunHistoryStore _historyStore;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly ToolLauncherService _launcher;
    private readonly AppLog _log;
    private readonly StringBuilder _logBuilder = new();

    private ScratchProfile _profile = new();
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _applyingProfile;
    private string? _preferredDevHub;

    public ScratchOrgViewModel(
        OrgService orgs,
        ScratchProfileStore profiles,
        ScratchOrgService service,
        ScratchRunHistoryStore historyStore,
        IDialogService dialogs,
        IFilePickerService filePicker,
        ToolLauncherService launcher,
        AppLog log)
    {
        _orgs = orgs;
        _profiles = profiles;
        _service = service;
        _historyStore = historyStore;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _launcher = launcher;
        _log = log;
    }

    public string Title => UiText.T("Scratch_Title");

    public ObservableCollection<OrgInfo> Orgs { get; } = new();

    public ObservableCollection<string> ProfileNames { get; } = new();

    public ObservableCollection<ScratchRunRecord> History { get; } = new();

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

    [ObservableProperty]
    private bool _useMetadata = true;

    [ObservableProperty]
    private bool _useCustomMetadata = true;

    [ObservableProperty]
    private bool _usePermissionSets = true;

    [ObservableProperty]
    private bool _useSeedData = true;

    [ObservableProperty]
    private string _permissionSetsText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
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
        ProfileNames.Clear();
        foreach (var name in _profiles.ListProfiles())
        {
            ProfileNames.Add(name);
        }

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

    private void LoadProfile(string name)
    {
        _profile = _profiles.Ensure(name);
        ApplyProfileValues(_profile);
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
            UseMetadata = profile.UseMetadata;
            UseCustomMetadata = profile.UseCustomMetadata;
            UsePermissionSets = profile.UsePermissionSets;
            UseSeedData = profile.UseSeedData;
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
        profile.UseMetadata = UseMetadata;
        profile.UseCustomMetadata = UseCustomMetadata;
        profile.UsePermissionSets = UsePermissionSets;
        profile.UseSeedData = UseSeedData;
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

        if (profile.UseSeedData && !ScratchOrgService.HasSeedObjects(files.SeedPlanFile))
        {
            warnings.Add(UiText.T("Scratch_WarnNoSeedPlan"));
        }

        if (profile.UsePermissionSets && profile.PermissionSets.Count == 0)
        {
            warnings.Add(UiText.T("Scratch_WarnNoPermissionSets"));
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
        var profile = CaptureProfile();
        if (!ScratchProfileNaming.IsValidName(profile.Name))
        {
            _dialogs.Warning(UiText.T("Scratch_InvalidProfileName"), Title);
            return;
        }

        _profiles.Save(profile);
        _profile = profile;
        ApplyProfileValues(profile);
        StatusMessage = UiText.T("Scratch_SavedFmt", profile.Name);
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

    // ---- 実行 ----

    private bool CanRun() => !IsRunning && !IsLoadingOrgs && SelectedDevHub is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var profile = CaptureProfile();
        var devHub = SelectedDevHub?.Alias ?? SelectedDevHub?.Username;
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

        var steps = ScratchOrgService.BuildPlan(profile, devHub!, alias, files);
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ResetLog();
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
            return;
        }

        var record = new ScratchRunRecord
        {
            StartedAt = started,
            FinishedAt = DateTimeOffset.Now,
            ProfileName = profile.Name,
            Alias = alias,
            DevHub = devHub!,
            Success = runResult.Success,
            Message = runResult.Success ? string.Empty : (runResult.ErrorText ?? string.Empty),
            Steps = runResult.Steps.ToList(),
        };
        _historyStore.Add(record);
        ReloadHistory();

        if (runResult.Success)
        {
            StatusMessage = UiText.T("Scratch_RunDoneFmt", alias);
            _dialogs.Info(
                UiText.T("Scratch_RunDoneFmt", alias) + Environment.NewLine + Environment.NewLine +
                UiText.T("Scratch_RunDoneHintFmt", alias),
                Title);
            await LoadOrgsAsync(alias);
        }
        else
        {
            StatusMessage = UiText.T("Scratch_RunFailedFmt", runResult.FailedStepLabel ?? string.Empty);
            _dialogs.Warning(
                $"{runResult.FailedStepLabel}: {runResult.ErrorText}" + Environment.NewLine +
                UiText.T("Scratch_RunFailedHint"),
                Title);
        }
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
