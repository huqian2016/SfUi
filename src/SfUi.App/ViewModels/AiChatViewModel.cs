using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>AI 応答から抽出したコード片（タブへ適用する候補）。</summary>
public sealed class AiSnippet
{
    public AiSnippet(string language, string code)
    {
        Language = language;
        Code = code;
        ApplyLabel = language switch
        {
            "soql" => UiText.T("Ai_ApplySoql"),
            "apex" => UiText.T("Ai_ApplyApex"),
            "command" => UiText.T("Ai_ApplyCommand"),
            _ => string.Empty,
        };
    }

    /// <summary>soql / apex / command / text。</summary>
    public string Language { get; }

    public string Code { get; }

    /// <summary>適用ボタンのラベル（text は空 = ボタン非表示）。</summary>
    public string ApplyLabel { get; }

    public bool CanApply => Language is "soql" or "apex" or "command";
}

/// <summary>チャット 1 通分の表示用メッセージ。</summary>
public sealed class AiChatMessage
{
    public required bool IsUser { get; init; }

    public required string Role { get; init; }

    public required string Content { get; init; }

    public IReadOnlyList<AiSnippet> Snippets { get; init; } = Array.Empty<AiSnippet>();

    public string HeaderText => IsUser ? UiText.T("Ai_You") : UiText.T("Ai_Assistant");
}

/// <summary>AI チャットタブの ViewModel（DeepSeek API）。</summary>
public partial class AiChatViewModel : ObservableObject
{
    private const int MaxHistoryMessages = 12;
    private const int MaxAttachedResultChars = 12000;

    private static readonly Regex FenceRegex = new(@"```([A-Za-z0-9_+-]*)\r?\n(.*?)```", RegexOptions.Singleline);

    private readonly DeepSeekClient _client;
    private readonly HistoryStore _history;
    private readonly AppLog _log;

    private CancellationTokenSource? _cts;
    private string? _tabAttachment;
    private string? _tabAttachmentName;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = UiText.T("Common_Ready");

    [ObservableProperty]
    private HistoryEntry? _selectedContextEntry;

    [ObservableProperty]
    private bool _attachContext = true;

    /// <summary>現在の対象組織（MainViewModel から設定される）。</summary>
    public string? CurrentOrg { get; set; }

    /// <summary>表示中タブのデータ添付（組織情報ウィンドウ用）: 呼ぶと (タブ名, テキスト) を返す。</summary>
    public Func<(string Name, string Text)?>? TabDataProvider { get; set; }

    /// <summary>システムプロンプトへ追記する追加コンテキスト（組織情報ウィンドウ用）。</summary>
    public Func<string?>? ExtraSystemContextProvider { get; set; }

    /// <summary>クイックプロンプト（組織情報ウィンドウ用。空なら表示しない）。</summary>
    public ObservableCollection<string> QuickPrompts { get; } = new();

    [ObservableProperty]
    private bool _showTabDataButton;

    [ObservableProperty]
    private bool _hasQuickPrompts;

    public ObservableCollection<AiChatMessage> Messages { get; } = new();

    /// <summary>コンテキスト添付の候補（結果付きの最近の履歴）。</summary>
    public ObservableCollection<HistoryEntry> ContextEntries { get; } = new();

    /// <summary>コード片の適用が要求されたときに発火する（MainViewModel が処理）。</summary>
    public event Action<AiSnippet>? ApplyRequested;

    public AiChatViewModel(DeepSeekClient client, HistoryStore history, AppLog log)
    {
        _client = client;
        _history = history;
        _log = log;
        RefreshContext();
    }

    /// <summary>コンテキスト候補（結果付き履歴の直近 20 件）を再読込する。</summary>
    [RelayCommand]
    private void RefreshContext()
    {
        var previousId = SelectedContextEntry?.Id;
        ContextEntries.Clear();
        foreach (var entry in _history.Query()
                     .Where(e => e.ResultInline is { Length: > 0 } || e.ResultRef is { Length: > 0 })
                     .Take(20))
        {
            ContextEntries.Add(entry);
        }

        SelectedContextEntry = ContextEntries.FirstOrDefault(e => e.Id == previousId) ?? ContextEntries.FirstOrDefault();
    }

    /// <summary>直前の結果を分析: 入力欄にプロンプトを入れ、その履歴を添付対象にする。</summary>
    [RelayCommand]
    private void AnalyzeLast()
    {
        RefreshContext();
        var entry = ContextEntries.FirstOrDefault();
        if (entry is null)
        {
            StatusText = UiText.T("Ai_NoResult");
            return;
        }

        SelectedContextEntry = entry;
        AttachContext = true;
        if (string.IsNullOrWhiteSpace(InputText))
        {
            InputText = UiText.T("Ai_AnalyzePrompt");
        }

        StatusText = UiText.T("Ai_AttachedHintFmt", entry.TypeLabel, entry.Summary);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var text = InputText.Trim();
        if (text.Length == 0)
        {
            StatusText = UiText.T("Ai_EmptyInput");
            return;
        }

        var userContent = text;
        if (_tabAttachment is { } tabData)
        {
            // 組織情報ウィンドウの「表示中タブのデータを添付」分を優先して含める
            userContent = UiText.T("OrgInfo_Ai_AttachHeaderFmt", _tabAttachmentName ?? string.Empty)
                          + "\n```\n" + tabData + "\n```\n\n" + text;
            _tabAttachment = null;
            _tabAttachmentName = null;
        }
        else if (AttachContext && SelectedContextEntry is { } context)
        {
            var result = _history.ReadResult(context);
            if (!string.IsNullOrEmpty(result))
            {
                var trimmed = result.Length > MaxAttachedResultChars ? result[..MaxAttachedResultChars] + "\n…(truncated)" : result;
                userContent = UiText.T("Ai_AttachHeaderFmt", context.TypeLabel, context.Summary)
                              + "\n```\n" + trimmed + "\n```\n\n" + text;
            }
        }

        Messages.Add(new AiChatMessage { IsUser = true, Role = "user", Content = userContent });
        InputText = string.Empty;
        AttachContext = false;

        IsBusy = true;
        StatusText = UiText.T("Ai_Thinking");
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _client.ChatAsync(BuildRequestMessages(), _cts.Token);
            if (result.Success && result.Content is { } content)
            {
                Messages.Add(new AiChatMessage
                {
                    IsUser = false,
                    Role = "assistant",
                    Content = content,
                    Snippets = ParseSnippets(content),
                });
                StatusText = UiText.T("Ai_DoneFmt", result.Duration.TotalSeconds, result.PromptTokens ?? 0, result.CompletionTokens ?? 0);
            }
            else
            {
                StatusText = result.Error ?? UiText.T("Common_FailedFmt", string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = UiText.T("Common_Canceled");
        }
        catch (Exception ex)
        {
            StatusText = UiText.T("Common_FailedFmt", ex.Message);
            _log.Error("AI 送信に失敗", ex);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>表示中タブのデータを次の送信に添付する（組織情報ウィンドウ用）。</summary>
    [RelayCommand]
    private void AttachTabData()
    {
        var data = TabDataProvider?.Invoke();
        if (data is null || data.Value.Text.Length == 0)
        {
            _tabAttachment = null;
            _tabAttachmentName = null;
            StatusText = UiText.T("OrgInfo_Ai_NoTabData");
            return;
        }

        var (name, text) = data.Value;
        _tabAttachment = text;
        _tabAttachmentName = name;
        StatusText = UiText.T("OrgInfo_Ai_AttachDoneFmt", name, text.Length);
    }

    /// <summary>クイックプロンプトを入力欄へ入れる。</summary>
    [RelayCommand]
    private void UseQuickPrompt(string? prompt)
    {
        if (!string.IsNullOrEmpty(prompt))
        {
            InputText = prompt;
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        StatusText = UiText.T("Ai_Cleared");
    }

    /// <summary>コード片の適用を要求する（View のボタンから）。</summary>
    [RelayCommand]
    private void ApplySnippet(AiSnippet? snippet)
    {
        if (snippet is { CanApply: true })
        {
            ApplyRequested?.Invoke(snippet);
        }
    }

    /// <summary>API キーとモデルの設定状況を文字列化する（スモークログ用）。</summary>
    public string DescribeConfiguration()
        => $"apiKey={_client.ApiKeySource} / model={_client.Model}";

    private List<DeepSeekClient.ChatMessage> BuildRequestMessages()
    {
        var messages = new List<DeepSeekClient.ChatMessage>
        {
            new("system", BuildSystemPrompt()),
        };

        foreach (var message in Messages.TakeLast(MaxHistoryMessages))
        {
            messages.Add(new DeepSeekClient.ChatMessage(message.Role, message.Content));
        }

        return messages;
    }

    private string BuildSystemPrompt()
    {
        var japanese = UiText.Language == UiText.Japanese;
        var org = string.IsNullOrWhiteSpace(CurrentOrg)
            ? (japanese ? "（未選択）" : "(none)")
            : CurrentOrg;

        var prompt = japanese
            ? "あなたは Salesforce のエキスパートアシスタントです。ユーザーは Windows のデスクトップツール SfUi から Salesforce CLI (sf) を操作しています。"
              + $"現在の対象組織: {org}。\n"
              + "・SOQL は ```soql、匿名Apex は ```apex、sf コマンドは ```bash（先頭に sf を付ける）のコードブロックで出力してください。\n"
              + "・回答は日本語で簡潔に。"
            : "You are a Salesforce expert assistant. The user operates the Salesforce CLI (sf) from a Windows desktop tool called SfUi. "
              + $"Current target org: {org}.\n"
              + "- Output SOQL in ```soql, anonymous Apex in ```apex, and sf commands in ```bash (prefix with sf).\n"
              + "- Be concise.";

        var extra = ExtraSystemContextProvider?.Invoke();
        return string.IsNullOrWhiteSpace(extra) ? prompt : prompt + "\n" + extra;
    }

    /// <summary>応答からコードブロックを抽出して適用候補にする。</summary>
    internal static List<AiSnippet> ParseSnippets(string content)
    {
        var snippets = new List<AiSnippet>();
        foreach (Match match in FenceRegex.Matches(content))
        {
            var language = match.Groups[1].Value.ToLowerInvariant();
            var code = match.Groups[2].Value.Trim();
            if (code.Length == 0)
            {
                continue;
            }

            var mapped = language switch
            {
                "soql" => "soql",
                "apex" or "java" => "apex",
                "bash" or "sh" or "shell" or "powershell" or "ps" or "cmd" or "console" => "command",
                _ => code.StartsWith("sf ", StringComparison.OrdinalIgnoreCase) ? "command" : "text",
            };

            snippets.Add(new AiSnippet(mapped, code));
        }

        return snippets;
    }
}
