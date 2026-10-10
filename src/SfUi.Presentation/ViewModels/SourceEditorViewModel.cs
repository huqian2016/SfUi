using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;
using SfUi.Presentation;

namespace SfUi.App.ViewModels;

/// <summary>メタデータ エクスプローラーの 1 グループ（種別）。</summary>
public sealed partial class SourceMemberGroupViewModel : ObservableObject
{
    private readonly List<SourceMemberInfo> _all = new();
    private string _filter = string.Empty;

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
        _filter = filter;
        Members.Clear();
        foreach (var member in _all)
        {
            if (string.IsNullOrWhiteSpace(filter) || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                Members.Add(member);
            }
        }
    }

    /// <summary>削除後の一覧更新（現在のフィルタを維持）。</summary>
    public void RemoveMember(string name)
    {
        _all.RemoveAll(m => string.Equals(m.Name, name, StringComparison.Ordinal));
        ApplyFilter(_filter);
        OnPropertyChanged(nameof(CountTotal));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(DisplayNodeName));
    }

    /// <summary>全メンバー名（新規作成の重複チェック用。フィルタの影響を受けない）。</summary>
    public IReadOnlyCollection<string> AllNames => _all.Select(m => m.Name).ToArray();
}

/// <summary>開いている 1 ファイル（タブ）。baseline = 組織と一致した内容、Text = 編集中の内容。</summary>
public sealed partial class SourceFileViewModel : ObservableObject
{
    public SourceFileViewModel(SourceMemberInfo member, string fileName, string languageId, string baselineText, string text)
    {
        Member = member;
        MemberKey = member.Kind + ":" + member.Name;
        FileName = fileName;
        LanguageId = languageId;
        _baselineText = baselineText;
        _text = text;
        RecomputeDiff();
    }

    public SourceMemberInfo Member { get; }

    /// <summary>同一メンバー判定用キー（kind:name）。</summary>
    public string MemberKey { get; }

    public string FileName { get; }

    /// <summary>シンタックス ハイライト定義名（空 = なし）。</summary>
    public string LanguageId { get; }

    /// <summary>UIA 名（TabItem の Name）にも使われる表示名。</summary>
    public override string ToString() => FileName;

    [ObservableProperty]
    private string _text;

    /// <summary>組織と一致している内容（反映成功で Text に追従）。</summary>
    [ObservableProperty]
    private string _baselineText;

    partial void OnTextChanged(string value) => RecomputeDiff();

    partial void OnBaselineTextChanged(string value) => RecomputeDiff();

    /// <summary>未反映行（working の 1 始まり行番号 → 種別）。</summary>
    public IReadOnlyDictionary<int, LineChangeKind> ChangedLines { get; private set; } = new Dictionary<int, LineChangeKind>();

    public int ChangedLineCount => ChangedLines.Count;

    public bool IsDirty => ChangedLineCount > 0;

    /// <summary>反映成功時に呼ぶ（baseline を現在の内容へ更新）。</summary>
    public void MarkDeployed() => BaselineText = Text;

    private void RecomputeDiff()
    {
        ChangedLines = LineDiff.Compute(BaselineText, Text);
        OnPropertyChanged(nameof(ChangedLines));
        OnPropertyChanged(nameof(ChangedLineCount));
        OnPropertyChanged(nameof(IsDirty));
    }
}

/// <summary>反映エラー パネルの 1 行（表示用の整形付き）。</summary>
public sealed class SourceDeployErrorRow
{
    public SourceDeployErrorRow(SourceDeployError error)
    {
        Error = error;
        var fileName = error.FileName.Replace('\\', '/');
        var shortName = fileName.Contains('/') ? fileName[(fileName.LastIndexOf('/') + 1)..] : fileName;
        DisplayText = error.Line > 0
            ? UiText.T("SourceEditor_ErrorRowFmt", shortName, error.Line, error.Problem)
            : UiText.T("SourceEditor_ErrorRowNoLineFmt", shortName, error.Problem);
    }

    public SourceDeployError Error { get; }

    public string DisplayText { get; }

    public override string ToString() => DisplayText;
}

/// <summary>エディタへ行ジャンプする要求（ウィンドウのコードビハインドが処理）。</summary>
public sealed record SourceNavigation(string MemberKey, string FileName, int Line);

/// <summary>
/// ソース エディタ ウィンドウの ViewModel（Phase 2: 編集 + 未反映ハイライト + 組織反映 + 検証 + 削除）。
/// </summary>
public sealed partial class SourceEditorViewModel : ObservableObject
{
    private readonly ISourceEditorService _service;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly ApexSuggestionProvider _suggestions;
    private readonly AiChatViewModel _ai;
    private CancellationTokenSource? _draftCts;
    private CancellationTokenSource? _suggestCts;

    public SourceEditorViewModel(ISourceEditorService service, IDialogService dialogs, AppLog log, ApexSuggestionProvider suggestions, AiChatViewModel ai)
    {
        _service = service;
        _dialogs = dialogs;
        _log = log;
        _suggestions = suggestions;
        _ai = ai;
        _ai.ApplyRequested += ApplyAiSnippet;

        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.ApexClass, UiText.T("SourceEditor_GroupApexClass")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.ApexTrigger, UiText.T("SourceEditor_GroupApexTrigger")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.VisualforcePage, UiText.T("SourceEditor_GroupVfPage")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.LightningComponentBundle, UiText.T("SourceEditor_GroupLwc")));
        Groups.Add(new SourceMemberGroupViewModel(SourceMemberKind.Flow, UiText.T("SourceEditor_GroupFlow")));
    }

    public string Title => UiText.T("SourceEditor_Title");

    public OrgInfo? Org { get; private set; }

    /// <summary>Tooling REST / sf CLI に渡す組織名（alias 優先）。</summary>
    public string TargetOrg => Org is null ? string.Empty : (string.IsNullOrWhiteSpace(Org.Alias) ? Org.Username : Org.Alias!);

    public string OrgLabel => Org is null ? string.Empty : Org.DisplayName;

    /// <summary>ドラフト保存用の組織キー（username から生成）。</summary>
    public string OrgKey => SourceEditorWorkspace.KeyFor(Org?.Username);

    public ObservableCollection<SourceMemberGroupViewModel> Groups { get; } = new();

    public ObservableCollection<SourceFileViewModel> OpenFiles { get; } = new();

    [ObservableProperty]
    private SourceFileViewModel? _selectedFile;

    partial void OnSelectedFileChanged(SourceFileViewModel? oldValue, SourceFileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= File_PropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += File_PropertyChanged;
        }

        HideSuggestions();
        UpdateFileStatus();
    }

    private void File_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SourceFileViewModel.ChangedLineCount) or nameof(SourceFileViewModel.IsDirty))
        {
            UpdateFileStatus();
            QueueDraftSave(sender as SourceFileViewModel);
        }
    }

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
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeployCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMemberCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>反映系の操作が可能か（ビジー中とグラフ表示中は不可）。</summary>
    public bool CanEdit => !IsBusy && CurrentGraph is null;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>エディタのカーソル位置（Ln, Col）。ウィンドウのコードビハインドから更新する。</summary>
    [ObservableProperty]
    private string _caretText = string.Empty;

    /// <summary>選択中ファイルの反映状態（未反映 N 行 / 組織と一致）。</summary>
    [ObservableProperty]
    private string _fileStatusText = string.Empty;

    /// <summary>開いているフロー グラフ（null = ファイル タブ表示）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGraph))]
    [NotifyPropertyChangedFor(nameof(HasNoGraph))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(DeployCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMemberCommand))]
    private FlowGraphViewModel? _currentGraph;

    public bool HasGraph => CurrentGraph is not null;

    public bool HasNoGraph => CurrentGraph is null;

    /// <summary>反映エラー（パネル表示用）。</summary>
    public ObservableCollection<SourceDeployErrorRow> DeployErrors { get; } = new();

    public bool HasDeployErrors => DeployErrors.Count > 0;

    public string ErrorsHeaderText => UiText.T("SourceEditor_ErrorsHeaderFmt", DeployErrors.Count);

    [ObservableProperty]
    private SourceDeployErrorRow? _selectedDeployError;

    partial void OnSelectedDeployErrorChanged(SourceDeployErrorRow? value) => GoToError(value);

    /// <summary>エディタの行ジャンプ要求（コードビハインドが処理して消費する）。</summary>
    [ObservableProperty]
    private SourceNavigation? _pendingNavigation;

    public void Initialize(OrgInfo? org)
    {
        Org = org;
        OnPropertyChanged(nameof(OrgLabel));
        OnPropertyChanged(nameof(TargetOrg));
        OnPropertyChanged(nameof(OrgKey));
        StatusMessage = org is null ? UiText.T("SourceEditor_NoOrg") : string.Empty;
        ConfigureAi();
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
                    var members = group.Kind == SourceMemberKind.Flow
                        ? await _service.ListFlowsAsync(TargetOrg).ConfigureAwait(true)
                        : await _service.ListAsync(TargetOrg, group.Kind).ConfigureAwait(true);
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
                ? UiText.T("SourceEditor_LoadedFmt", counts[0], counts[1], counts[2], counts[3], counts[4])
                : UiText.T("Common_FailedFmt", string.Join(" / ", errors));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>メンバーを選択してソースを開く（同一メンバーはタブを置き換え。ローカル下書きを復元）。</summary>
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

        if (member.Kind == SourceMemberKind.Flow)
        {
            await OpenFlowGraphAsync(member).ConfigureAwait(true);
            return;
        }

        // ファイルを開くときはグラフ表示を解除する
        CurrentGraph = null;
        var key = member.Kind + ":" + member.Name;
        IsBusy = true;
        StatusMessage = UiText.T("SourceEditor_FetchingFmt", member.Name);
        try
        {
            var orgFiles = await _service.GetSourceAsync(TargetOrg, member).ConfigureAwait(true);
            if (orgFiles.Count == 0)
            {
                StatusMessage = UiText.T("Common_FailedFmt", UiText.T("SourceEditor_EmptyFmt", member.Name));
                return;
            }

            IReadOnlyList<SourceFileInfo> baseline = orgFiles;
            IReadOnlyList<SourceFileInfo> working = orgFiles;
            var restored = false;
            try
            {
                var draft = await _service.LoadDraftAsync(OrgKey, member).ConfigureAwait(true);
                if (draft is not null && SameContent(draft.Baseline, orgFiles))
                {
                    working = draft.Working;
                    restored = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"ソース エディタ: 下書きの読み込みに失敗しました（{member.Name}）: {ex.Message}");
            }

            await _service.SaveDraftAsync(OrgKey, member, baseline, working).ConfigureAwait(true);

            foreach (var old in OpenFiles.Where(f => f.MemberKey == key).ToList())
            {
                OpenFiles.Remove(old);
            }

            SourceFileViewModel? first = null;
            foreach (var file in working)
            {
                var baseText = baseline.FirstOrDefault(b => string.Equals(b.FileName, file.FileName, StringComparison.OrdinalIgnoreCase))?.Text ?? file.Text;
                var tab = new SourceFileViewModel(member, file.FileName, file.LanguageId, baseText, file.Text);
                OpenFiles.Add(tab);
                first ??= tab;
            }

            if (first is null)
            {
                StatusMessage = UiText.T("Common_FailedFmt", UiText.T("SourceEditor_EmptyFmt", member.Name));
                return;
            }

            SelectedFile = first;
            StatusMessage = restored
                ? UiText.T("SourceEditor_DraftRestoredFmt", member.Name)
                : UiText.T("SourceEditor_FetchedFmt", member.Name, CountLines(first.Text));
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

    /// <summary>フロー 1 件のグラフを読み込んで表示する（Phase 4）。</summary>
    private async Task OpenFlowGraphAsync(SourceMemberInfo member)
    {
        IsBusy = true;
        HideSuggestions();
        StatusMessage = UiText.T("SourceEditor_FlowLoadingFmt", member.Name);
        try
        {
            var graph = await _service.GetFlowGraphAsync(TargetOrg, member).ConfigureAwait(true);
            var graphViewModel = new FlowGraphViewModel(graph);
            graphViewModel.CloseRequested += CloseGraph;
            CurrentGraph = graphViewModel;
            StatusMessage = UiText.T("SourceEditor_FlowLoadedFmt", graph.Label, graph.VersionLabel, graph.Nodes.Count, graph.Edges.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"ソース エディタ: フロー {member.Name} の取得に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
            _dialogs.Warning(ex.Message, UiText.T("SourceEditor_Title"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>グラフ表示を閉じてファイル タブに戻る。</summary>
    public void CloseGraph() => CurrentGraph = null;

    // ---- 自動補完（Phase 5: Apex / LWC JS / VF・HTML の候補チップ）----

    /// <summary>候補チップの一覧。</summary>
    public ObservableCollection<SoqlCompletionItem> Suggestions { get; } = new();

    /// <summary>候補エリアの見出し（例: 「System のメンバー:」）。</summary>
    [ObservableProperty]
    private string _suggestionsHeader = string.Empty;

    /// <summary>候補エリアを表示するか。</summary>
    [ObservableProperty]
    private bool _isSuggestionsVisible;

    /// <summary>候補を取得中か。</summary>
    [ObservableProperty]
    private bool _isFetchingSuggestions;

    /// <summary>
    /// カーソル位置の候補を計算して候補エリアへ反映する（ウィンドウのコードビハインドから呼ばれる）。
    /// 言語はファイル名で判定する（.cls/.trigger = Apex、.js = JavaScript、.html/.page = HTML）。
    /// </summary>
    public async Task UpdateSuggestionsAsync(SourceFileViewModel? file, int caret)
    {
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = null;

        if (file is null || CurrentGraph is not null)
        {
            HideSuggestions();
            return;
        }

        var language = SourceCompletionLanguages.ForFileName(file.FileName);
        if (language == SourceCompletionLanguage.None)
        {
            HideSuggestions();
            return;
        }

        caret = Math.Clamp(caret, 0, file.Text.Length);
        var cts = new CancellationTokenSource();
        _suggestCts = cts;
        IsSuggestionsVisible = true;
        IsFetchingSuggestions = true;

        try
        {
            string header;
            IReadOnlyList<SoqlCompletionItem> items;

            switch (language)
            {
                case SourceCompletionLanguage.Apex:
                {
                    var context = ApexCompletionParser.Parse(file.Text, caret);
                    if (context.Kind == ApexCompletionKind.None)
                    {
                        HideSuggestions();
                        return;
                    }

                    var result = await _suggestions
                        .GetAsync(TargetOrg, file.Text, caret, context, cts.Token)
                        .ConfigureAwait(true);
                    if (result is null)
                    {
                        HideSuggestions();
                        return;
                    }

                    (header, items) = result.Value;
                    break;
                }

                case SourceCompletionLanguage.JavaScript:
                {
                    var context = JsCompletion.Parse(file.Text, caret);
                    if (!context.InCode || context.Prefix.Length == 0)
                    {
                        HideSuggestions();
                        return;
                    }

                    header = UiText.T("SourceEditor_SuggestJs");
                    items = JsCompletion.Items(context.Prefix);
                    break;
                }

                case SourceCompletionLanguage.Html:
                {
                    var context = HtmlCompletion.Parse(file.Text, caret);
                    if (!context.InTag)
                    {
                        HideSuggestions();
                        return;
                    }

                    var isVf = SourceCompletionLanguages.IsVisualforce(file.FileName);
                    header = UiText.T(isVf ? "SourceEditor_SuggestVf" : "SourceEditor_SuggestHtml");
                    items = HtmlCompletion.Items(context.Prefix, isVf);
                    break;
                }

                default:
                    HideSuggestions();
                    return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (items.Count == 0)
            {
                HideSuggestions();
                return;
            }

            SuggestionsHeader = header;
            Suggestions.Clear();
            foreach (var item in items)
            {
                Suggestions.Add(item);
            }

            IsFetchingSuggestions = false;
            IsSuggestionsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // 次の入力・カーソル移動で破棄された
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                HideSuggestions();
                _log.Warn($"ソース エディタ: 補完候補の取得に失敗しました（{file.FileName}）: {ex.Message}");
            }
        }
    }

    /// <summary>候補チップのクリック時に、カーソル位置の語の置換開始位置を返す（言語別のパーサーを使用）。</summary>
    public int GetSuggestionSegmentStart(SourceFileViewModel? file, string text, int caret)
    {
        if (file is null)
        {
            return caret;
        }

        caret = Math.Clamp(caret, 0, text.Length);
        return SourceCompletionLanguages.ForFileName(file.FileName) switch
        {
            SourceCompletionLanguage.Apex => ApexCompletionParser.Parse(text, caret).SegmentStart,
            SourceCompletionLanguage.JavaScript => JsCompletion.Parse(text, caret).Start,
            SourceCompletionLanguage.Html => HtmlCompletion.Parse(text, caret).Start,
            _ => caret,
        };
    }

    /// <summary>候補エリアを消す。</summary>
    public void HideSuggestions()
    {
        IsFetchingSuggestions = false;
        IsSuggestionsVisible = false;
        Suggestions.Clear();
    }

    // ---- AI 支援（Phase 6: 質問 / 説明 / 改善提案 / エラー修正 + コードのエディタ挿入）----

    /// <summary>このウィンドウ専用の AI チャット（メインウィンドウとは独立した会話）。</summary>
    public AiChatViewModel Ai => _ai;

    /// <summary>AI パネルの表示状態。</summary>
    [ObservableProperty]
    private bool _isAiPanelOpen;

    /// <summary>エディタのカーソル位置（AI コード挿入に使う。ウィンドウのコードビハインドが更新する）。</summary>
    public int CaretOffset { get; set; }

    [RelayCommand]
    private void ToggleAiPanel() => IsAiPanelOpen = !IsAiPanelOpen;

    /// <summary>AI へ渡すソース エディタ固有のコンテキスト（組織 / 開いているファイル / 反映エラー）を設定する。</summary>
    private void ConfigureAi()
    {
        _ai.CurrentOrg = TargetOrg;
        _ai.ExtraSystemContextProvider = BuildAiContext;
        _ai.TabDataProvider = BuildCurrentFileAttachment;
        _ai.ExtraApplyLanguages = new[] { "apex", "javascript", "html", "visualforce", "css" };
        _ai.ExtraApplyLabel = UiText.T("SourceEditor_Ai_Apply");
        _ai.ShowTabDataButton = true;

        _ai.QuickPrompts.Clear();
        _ai.QuickPrompts.Add(UiText.T("SourceEditor_Ai_ExplainPrompt"));
        _ai.QuickPrompts.Add(UiText.T("SourceEditor_Ai_ImprovePrompt"));
        _ai.QuickPrompts.Add(UiText.T("SourceEditor_Ai_FixErrorsPrompt"));
        _ai.HasQuickPrompts = _ai.QuickPrompts.Count > 0;
    }

    private string? BuildAiContext()
    {
        var builder = new StringBuilder();
        builder.AppendLine(UiText.T("SourceEditor_Ai_CtxHeader"));
        if (SelectedFile is { } file)
        {
            builder.AppendLine(UiText.T("SourceEditor_Ai_CtxFileFmt", file.FileName, file.LanguageId));
        }

        if (DeployErrors.Count > 0)
        {
            var errors = string.Join("\n", DeployErrors.Take(10).Select(e => e.DisplayText));
            builder.AppendLine(UiText.T("SourceEditor_Ai_CtxErrorsFmt", DeployErrors.Count, errors));
        }

        builder.Append(UiText.T("SourceEditor_Ai_CtxFence"));
        return builder.ToString();
    }

    /// <summary>「現在のタブのデータを添付」= 選択中ファイルの本文（長すぎる場合は切り詰め）。</summary>
    private (string Name, string Text)? BuildCurrentFileAttachment()
    {
        var file = SelectedFile;
        if (file is null || file.Text.Length == 0)
        {
            return null;
        }

        const int maxChars = 12000;
        var text = file.Text.Length > maxChars ? file.Text[..maxChars] + "\n…(truncated)" : file.Text;
        return (file.FileName, text);
    }

    /// <summary>AI のコード片を選択中ファイルのカーソル位置へ挿入する（エディタ表示は本文変更に追従する）。</summary>
    private void ApplyAiSnippet(AiSnippet snippet)
    {
        var file = SelectedFile;
        if (file is null || !CanEdit)
        {
            StatusMessage = UiText.T("SourceEditor_NoSelection");
            return;
        }

        var caret = Math.Clamp(CaretOffset, 0, file.Text.Length);
        file.Text = file.Text.Insert(caret, snippet.Code);
        CaretOffset = caret + snippet.Code.Length;
        StatusMessage = UiText.T("SourceEditor_Ai_AppliedFmt", file.FileName, snippet.Code.Length);
    }

    /// <summary>選択中ファイルのメンバーを組織へ反映する（Ctrl+S）。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task DeployAsync() => DeployCoreAsync(dryRun: false);

    /// <summary>検証のみ（dry-run deploy）。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ValidateAsync() => DeployCoreAsync(dryRun: true);

    private async Task DeployCoreAsync(bool dryRun)
    {
        var file = SelectedFile;
        if (file is null || Org is null)
        {
            StatusMessage = Org is null ? UiText.T("SourceEditor_NoOrg") : UiText.T("SourceEditor_NoSelection");
            return;
        }

        var member = file.Member;
        var tabs = OpenFiles.Where(f => f.MemberKey == file.MemberKey).ToList();
        var sourceFiles = tabs.Select(f => new SourceFileInfo(f.FileName, f.LanguageId, f.Text)).ToList();

        IsBusy = true;
        DeployErrors.Clear();
        OnPropertyChanged(nameof(HasDeployErrors));
        OnPropertyChanged(nameof(ErrorsHeaderText));
        StatusMessage = UiText.T("SourceEditor_DeployingFmt", member.Name);
        try
        {
            var result = await _service.DeployAsync(TargetOrg, member, sourceFiles, dryRun).ConfigureAwait(true);
            if (result.Success)
            {
                if (!dryRun)
                {
                    foreach (var tab in tabs)
                    {
                        tab.MarkDeployed();
                    }

                    await SaveDraftForAsync(member, CancellationToken.None).ConfigureAwait(true);
                }

                StatusMessage = dryRun
                    ? UiText.T("SourceEditor_ValidateOkFmt", member.Name)
                    : UiText.T("SourceEditor_DeployOkFmt", member.Name);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    DeployErrors.Add(new SourceDeployErrorRow(error));
                }

                OnPropertyChanged(nameof(HasDeployErrors));
                OnPropertyChanged(nameof(ErrorsHeaderText));
                StatusMessage = result.Errors.Count > 0
                    ? UiText.T("SourceEditor_DeployFailedFmt", result.Errors.Count)
                    : UiText.T("Common_FailedFmt", result.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>選択中ファイルのメンバーを組織から削除する。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task DeleteMemberAsync()
    {
        var file = SelectedFile;
        if (file is null || Org is null)
        {
            StatusMessage = Org is null ? UiText.T("SourceEditor_NoOrg") : UiText.T("SourceEditor_NoSelection");
            return;
        }

        var member = file.Member;
        if (!_dialogs.ConfirmDestructive(UiText.T("SourceEditor_DeleteConfirmFmt", member.Name), UiText.T("SourceEditor_Title")))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = UiText.T("SourceEditor_DeletingFmt", member.Name);
        try
        {
            var result = await _service.DeleteAsync(TargetOrg, member).ConfigureAwait(true);
            if (result.Success)
            {
                foreach (var tab in OpenFiles.Where(f => f.MemberKey == file.MemberKey).ToList())
                {
                    OpenFiles.Remove(tab);
                }

                SelectedFile = OpenFiles.Count == 0 ? null : OpenFiles[^1];
                Groups.First(g => g.Kind == member.Kind).RemoveMember(member.Name);
                await _service.ClearDraftAsync(OrgKey, member).ConfigureAwait(true);
                DeployErrors.Clear();
                OnPropertyChanged(nameof(HasDeployErrors));
                OnPropertyChanged(nameof(ErrorsHeaderText));
                StatusMessage = UiText.T("SourceEditor_DeletedFmt", member.Name);
            }
            else
            {
                StatusMessage = UiText.T("Common_FailedFmt", result.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>グループ内の既存メンバー名（新規作成ダイアログの重複チェック用）。</summary>
    public IReadOnlyCollection<string> GetExistingNames(SourceMemberKind kind)
        => Groups.FirstOrDefault(g => g.Kind == kind)?.AllNames ?? Array.Empty<string>();

    /// <summary>テンプレートからメンバーを新規作成して組織へ反映し、一覧を更新して開く（Phase 3）。</summary>
    public async Task CreateMemberAsync(SourceMemberKind kind, string name, string? sobjectName)
    {
        if (Org is null)
        {
            StatusMessage = UiText.T("SourceEditor_NoOrg");
            return;
        }

        var trimmed = name.Trim();
        IsBusy = true;
        DeployErrors.Clear();
        OnPropertyChanged(nameof(HasDeployErrors));
        OnPropertyChanged(nameof(ErrorsHeaderText));
        StatusMessage = UiText.T("SourceEditor_NewCreatingFmt", trimmed);
        try
        {
            // 組織の最新一覧で重複を確認する（ローカルの一覧が古い場合の上書き事故を防ぐ）
            var current = await _service.ListAsync(TargetOrg, kind).ConfigureAwait(true);
            if (current.Any(m => string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = UiText.T("Common_FailedFmt", UiText.T("SourceEditor_NameExistsFmt", trimmed));
                return;
            }

            var files = SourceEditorTemplates.BuildFiles(kind, trimmed, sobjectName);
            var placeholder = new SourceMemberInfo(kind, trimmed, string.Empty);
            var result = await _service.DeployAsync(TargetOrg, placeholder, files, dryRun: false).ConfigureAwait(true);
            if (!result.Success)
            {
                foreach (var error in result.Errors)
                {
                    DeployErrors.Add(new SourceDeployErrorRow(error));
                }

                OnPropertyChanged(nameof(HasDeployErrors));
                OnPropertyChanged(nameof(ErrorsHeaderText));
                StatusMessage = result.Errors.Count > 0
                    ? UiText.T("SourceEditor_DeployFailedFmt", result.Errors.Count)
                    : UiText.T("Common_FailedFmt", result.Message);
                return;
            }

            _log.Info($"ソース エディタ: {kind} {trimmed} を新規作成しました。");

            var members = await _service.ListAsync(TargetOrg, kind).ConfigureAwait(true);
            Groups.FirstOrDefault(g => g.Kind == kind)?.SetAll(members, FilterText);

            var created = members.FirstOrDefault(m => string.Equals(m.Name, trimmed, StringComparison.Ordinal));
            if (created is not null)
            {
                await OpenMemberAsync(created).ConfigureAwait(true);
            }

            StatusMessage = UiText.T("SourceEditor_NewCreatedFmt", trimmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"ソース エディタ: {trimmed} の新規作成に失敗しました", ex);
            StatusMessage = UiText.T("Common_FailedFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>反映エラーの行へジャンプする（SelectedDeployError → PendingNavigation）。</summary>
    public void GoToError(SourceDeployErrorRow? row)
    {
        if (row is null)
        {
            return;
        }

        var fileName = row.Error.FileName.Replace('\\', '/');
        var shortName = fileName.Contains('/') ? fileName[(fileName.LastIndexOf('/') + 1)..] : fileName;
        var file = OpenFiles.FirstOrDefault(f => string.Equals(f.FileName, shortName, StringComparison.OrdinalIgnoreCase))
                   ?? OpenFiles.FirstOrDefault(f => f.MemberKey == SelectedFile?.MemberKey);
        if (file is null)
        {
            return;
        }

        SelectedFile = file;
        PendingNavigation = new SourceNavigation(file.MemberKey, file.FileName, Math.Max(row.Error.Line, 1));
    }

    /// <summary>コードビハインドがジャンプを実行した後に呼ぶ。</summary>
    public void ClearPendingNavigation() => PendingNavigation = null;

    /// <summary>ファイル名の集合と本文が一致するか（下書きが現在の組織内容に対応するか）。</summary>
    public static bool SameContent(IReadOnlyList<SourceFileInfo> a, IReadOnlyList<SourceFileInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var file in a)
        {
            var other = b.FirstOrDefault(x => string.Equals(x.FileName, file.FileName, StringComparison.OrdinalIgnoreCase));
            if (other is null || !string.Equals(file.Text, other.Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateFileStatus()
    {
        var file = SelectedFile;
        FileStatusText = file is null
            ? string.Empty
            : file.IsDirty
                ? UiText.T("SourceEditor_StatusDirtyFmt", file.ChangedLineCount)
                : UiText.T("SourceEditor_StatusClean");
    }

    /// <summary>編集のたびにドラフトを自動保存する（800ms デバウンス）。</summary>
    private void QueueDraftSave(SourceFileViewModel? file)
    {
        if (file is null || Org is null)
        {
            return;
        }

        var member = file.Member;
        _draftCts?.Cancel();
        _draftCts = new CancellationTokenSource();
        var token = _draftCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, token).ConfigureAwait(false);
                await SaveDraftForAsync(member, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Warn($"ソース エディタ: 下書きの保存に失敗しました（{member.Name}）: {ex.Message}");
            }
        });
    }

    private async Task SaveDraftForAsync(SourceMemberInfo member, CancellationToken cancellationToken)
    {
        if (Org is null)
        {
            return;
        }

        var key = member.Kind + ":" + member.Name;
        var files = OpenFiles.Where(f => f.MemberKey == key).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var baseline = files.Select(f => new SourceFileInfo(f.FileName, f.LanguageId, f.BaselineText)).ToList();
        var working = files.Select(f => new SourceFileInfo(f.FileName, f.LanguageId, f.Text)).ToList();
        await _service.SaveDraftAsync(OrgKey, member, baseline, working, cancellationToken).ConfigureAwait(false);
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
        HideSuggestions();
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
