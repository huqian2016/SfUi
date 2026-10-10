using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>メタデータ エクスプローラーの 1 グループ（種別）。</summary>
public sealed partial class SourceMemberGroupViewModel : ObservableObject
{
    private readonly List<SourceMemberInfo> _all = new();

    public SourceMemberGroupViewModel(SourceMemberKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public SourceMemberKind Kind { get; }

    public string Title { get; }

    /// <summary>グループの展開状態（初期 = 展開。TreeView の ItemContainerStyle からバインド）。</summary>
    public bool IsExpanded { get; set; } = true;

    public int CountTotal => _all.Count;

    /// <summary>ツリー表示名（件数付き。例: Apex クラス (185)）。</summary>
    public string HeaderText => CountTotal == 0 ? Title : Title + " (" + CountTotal + ")";

    /// <summary>UIA 名（TreeViewItem の Name）にも使われる表示名。</summary>
    public override string ToString() => HeaderText;

    /// <summary>UIA 名の AutomationProperties.Name バインド用。</summary>
    public string DisplayNodeName => HeaderText;

    /// <summary>フィルタ後のメンバー（ツリーの子要素）。</summary>
    public ObservableCollection<SourceMemberInfo> Members { get; } = new();

    public void SetAll(IReadOnlyCollection<SourceMemberInfo> members, string filter)
    {
        _all.Clear();
        _all.AddRange(members);
        ApplyFilter(filter);
        OnPropertyChanged(nameof(CountTotal));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(DisplayNodeName));
    }

    public void ApplyFilter(string filter)
    {
        Members.Clear();
        foreach (var member in _all)
        {
            if (string.IsNullOrWhiteSpace(filter) || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                Members.Add(member);
            }
        }
    }
}

/// <summary>開いている 1 ファイル（タブ）。</summary>
public sealed partial class SourceFileViewModel : ObservableObject
{
    public SourceFileViewModel(string memberKey, string fileName, string languageId, string text)
    {
        MemberKey = memberKey;
        FileName = fileName;
        LanguageId = languageId;
        _text = text;
    }

    /// <summary>同一メンバー判定用キー（kind:name）。</summary>
    public string MemberKey { get; }

    public string FileName { get; }

    /// <summary>シンタックス ハイライト定義名（空 = なし）。</summary>
    public string LanguageId { get; }

    /// <summary>UIA 名（TabItem の Name）にも使われる表示名。</summary>
    public override string ToString() => FileName;

    [ObservableProperty]
    private string _text;
}

/// <summary>
/// ソース エディタ ウィンドウの ViewModel（Phase 1: 列挙 + 本文表示。編集・反映は Phase 2）。
/// </summary>
public sealed partial class SourceEditorViewModel : ObservableObject
{
    private readonly ISourceEditorService _service;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    public SourceEditorViewModel(ISourceEditorService service, IDialogService dialogs, AppLog log)
    {
        _service = service;
        _dialogs = dialogs;
        _log = log;

        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.ApexClass, UiText.T("SourceEditor_GroupApexClass")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.ApexTrigger, UiText.T("SourceEditor_GroupApexTrigger")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.VisualforcePage, UiText.T("SourceEditor_GroupVfPage")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.LightningComponentBundle, UiText.T("SourceEditor_GroupLwc")));
    }

    public string Title => UiText.T("SourceEditor_Title");

    public OrgInfo? Org { get; private set; }

    /// <summary>Tooling REST / sf CLI に渡す組織名（alias 優先）。</summary>
    public string TargetOrg => Org is null ? string.Empty : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias!);

    public string OrgLabel => Org is null ? string.Empty : Org.DisplayName;

    public ObservableCollection<SourceMemberGroupViewModel> Groups { get; } = new();

    public ObservableCollection<SourceFileViewModel> OpenFiles { get; } = new();

    [ObservableProperty]
    private SourceFileViewModel? _selectedFile;

    [ObservableProperty]
    private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value)
    {
        foreach (var group in Groups)
        {
            group.ApplyFilter(value);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMemberCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>エディタのカーソル位置（Ln, Col）。ウィンドウのコードビハインドから更新する。</summary>
    [ObservableProperty]
    private string _caretText = string.Empty;

    public void Initialize(OrgInfo? org)
    {
        Org = org;
        OnPropertyChanged(nameof(OrgLabel));
        OnPropertyChanged(nameof(TargetOrg));
        StatusMessage = org is null ? UiText.T("SourceEditor_NoOrg") : string.Empty;
    }

    /// <summary>4 種別のメンバー一覧を再読み込みする。</summary>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RefreshAsync()
    {
        if (Org is null)
        {
            StatusMessage = UiText.T("SourceEditor_NoOrg");
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("SourceEditor_LoadingFmt", OrgLabel);
        try
        {
            var counts = new int[Groups.Count];
            var errors = new List<string>();
            for (var i = 0; i < Groups.Count; i++)
            {
                var group = Groups[i];
                try
                {
                    var members = await _service.ListAsync(TargetOrg, group.Kind).ConfigureAwait(true);
                    group.SetAll(members, FilterText);
                    counts[i] = members.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error($"ソース エディタ: {group.Kind} の一覧取得に失敗しました", ex);
                    errors.Add(group.Title + ": " + ex.Message);
                }
            }

            StatusMessage = errors.Count == 0
                ? UiText.T("SourceEditor_LoadedFmt", counts[0], counts[1], counts[2], counts[3])
                : UiText.T("Common_FailedFmt", string.Join(" / ", errors));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>メンバーを選択してソースを開く（同一メンバーはタブを置き換え）。</summary>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task OpenMemberAsync(SourceMemberInfo? member)
    {
        if (member is null)
        {
            return;
        }

        if (Org is null)
        {
            StatusMessage = UiText.T("SourceEditor_NoOrg");
            return;
        }

        var key = member.Kind + ":" + member.Name;
        IsBusy = true;
        StatusMessage = UiText.T("SourceEditor_FetchingFmt", member.Name);
        try
        {
            var files = await _service.GetSourceAsync(TargetOrg, member).ConfigureAwait(true);
            if (files.Count == 0)
            {
                StatusMessage = UiText.T("Common_FailedFmt", UiText.T("SourceEditor_EmptyFmt", member.Name));
                return;
            }

            foreach (var old in OpenFiles.Where(f => f.MemberKey == key).ToList())
            {
                OpenFiles.Remove(old);
            }

            SourceFileViewModel? first = null;
            foreach (var file in files)
            {
                var tab = new SourceFileViewModel(key, file.FileName, file.LanguageId, file.Text);
                OpenFiles.Add(tab);
                first ??= tab;
            }

            SelectedFile = first;
            StatusMessage = UiText.T("SourceEditor_FetchedFmt", member.Name, CountLines(files[0].Text));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"ソース エディタ: {member.Name} の取得に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, UiText.T("SourceEditor_Title"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseFile(SourceFileViewModel? file)
    {
        if (file is null)
        {
            return;
        }

        var index = OpenFiles.IndexOf(file);
        if (index < 0)
        {
            return;
        }

        OpenFiles.Remove(file);
        if (ReferenceEquals(SelectedFile, file))
        {
            SelectedFile = OpenFiles.Count == 0 ? null : OpenFiles[Math.Min(index, OpenFiles.Count - 1)];
        }
    }

    /// <summary>行数（空文字は 0 行）。</summary>
    public static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
