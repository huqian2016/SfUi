using System.Text;
using System.Text.Json.Serialization;

namespace SfUi.Core;

/// <summary>
/// スクラッチ組織の作成プロファイル。
/// 「何を作るか（資源の選択）」と「どう作るか（パラメータ）」を保持し、
/// data/scratch/profiles/&lt;name&gt;/profile.json に保存する。
/// </summary>
public sealed class ScratchProfile
{
    public const int MinDurationDays = 1;
    public const int MaxDurationDays = 30;
    public const int MinWaitMinutes = 1;
    public const int MaxWaitMinutes = 120;
    public const int DefaultDurationDays = 7;
    public const int DefaultWaitMinutes = 10;
    public const string DefaultAliasPattern = "scratch-{profile}-{date}";
    public const int MaxNameLength = 60;

    /// <summary>プロファイル名（フォルダー名にもなる）。</summary>
    public string Name { get; set; } = "default";

    /// <summary>Dev Hub のユーザー名またはエイリアス。</summary>
    public string? DevHub { get; set; }

    /// <summary>Org Shape ID（15 桁）。指定時は sf org create scratch --source-org に渡す。</summary>
    public string? OrgShapeId { get; set; }

    public int DurationDays { get; set; } = DefaultDurationDays;

    public int WaitMinutes { get; set; } = DefaultWaitMinutes;

    /// <summary>エイリアス パターン（{profile} / {date} / {time} / {datetime} が使える）。</summary>
    public string AliasPattern { get; set; } = DefaultAliasPattern;

    /// <summary>作成後に既定組織（target-org）へ設定するか。</summary>
    public bool SetDefault { get; set; }

    public bool UseMetadata { get; set; } = true;

    public bool UseCustomMetadata { get; set; } = true;

    public bool UsePermissionSets { get; set; } = true;

    public bool UseSeedData { get; set; } = true;

    /// <summary>作成後に割り当てる権限セット（API 名）。</summary>
    public List<string> PermissionSets { get; set; } = new();

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>値域の補正（範囲外は丸める）と空白の除去。</summary>
    public void Normalize()
    {
        Name = ScratchProfileNaming.SanitizeName(Name);
        DurationDays = Math.Clamp(DurationDays, MinDurationDays, MaxDurationDays);
        WaitMinutes = Math.Clamp(WaitMinutes, MinWaitMinutes, MaxWaitMinutes);
        AliasPattern = string.IsNullOrWhiteSpace(AliasPattern) ? DefaultAliasPattern : AliasPattern.Trim();
        OrgShapeId = string.IsNullOrWhiteSpace(OrgShapeId) ? null : OrgShapeId.Trim();
        DevHub = string.IsNullOrWhiteSpace(DevHub) ? null : DevHub.Trim();
        PermissionSets = PermissionSets
            .Select(p => p?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>実行前の検証。エラーが無ければ空。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!ScratchProfileNaming.IsValidName(Name))
        {
            errors.Add(UiText.T("Scratch_InvalidProfileName"));
        }

        if (DurationDays is < MinDurationDays or > MaxDurationDays)
        {
            errors.Add(UiText.T("Scratch_InvalidDays", MinDurationDays, MaxDurationDays));
        }

        if (WaitMinutes is < MinWaitMinutes or > MaxWaitMinutes)
        {
            errors.Add(UiText.T("Scratch_InvalidWait", MinWaitMinutes, MaxWaitMinutes));
        }

        if (string.IsNullOrWhiteSpace(AliasPattern))
        {
            errors.Add(UiText.T("Scratch_InvalidAlias"));
        }

        var alias = ScratchProfileNaming.ExpandAlias(AliasPattern, Name, DateTimeOffset.Now);
        if (alias.Length == 0)
        {
            errors.Add(UiText.T("Scratch_InvalidAlias"));
        }

        return errors;
    }

    public ScratchProfile Clone() => new()
    {
        Name = Name,
        DevHub = DevHub,
        OrgShapeId = OrgShapeId,
        DurationDays = DurationDays,
        WaitMinutes = WaitMinutes,
        AliasPattern = AliasPattern,
        SetDefault = SetDefault,
        UseMetadata = UseMetadata,
        UseCustomMetadata = UseCustomMetadata,
        UsePermissionSets = UsePermissionSets,
        UseSeedData = UseSeedData,
        PermissionSets = PermissionSets.ToList(),
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}

/// <summary>プロファイル名・エイリアスの正規化と展開。</summary>
public static class ScratchProfileNaming
{
    /// <summary>フォルダー名に使える名前か（英数字と - _ . のみ、60 文字以内）。</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= ScratchProfile.MaxNameLength
        && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>フォルダー名として安全な名前に変換する（不正文字は - に置換）。</summary>
    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "default";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        }

        var sanitized = builder.ToString().Trim('-', '.', ' ');
        if (sanitized.Length == 0)
        {
            sanitized = "profile";
        }

        return sanitized.Length > ScratchProfile.MaxNameLength
            ? sanitized[..ScratchProfile.MaxNameLength]
            : sanitized;
    }

    /// <summary>
    /// エイリアス パターンを展開する。
    /// {profile} = プロファイル名 / {date} = yyyyMMdd / {time} = HHmmss / {datetime} = yyyyMMdd-HHmmss。
    /// </summary>
    public static string ExpandAlias(string? pattern, string profileName, DateTimeOffset now)
    {
        var value = string.IsNullOrWhiteSpace(pattern) ? ScratchProfile.DefaultAliasPattern : pattern.Trim();
        var expanded = value
            .Replace("{profile}", SanitizeName(profileName), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{datetime}", now.ToString("yyyyMMdd-HHmmss"), StringComparison.OrdinalIgnoreCase);

        // sf のエイリアスに使える文字だけ残す
        var builder = new StringBuilder(expanded.Length);
        foreach (var c in expanded)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        }

        var alias = builder.ToString();
        // 連続する - を 1 つにまとめ、端の記号を落とす
        while (alias.Contains("--", StringComparison.Ordinal))
        {
            alias = alias.Replace("--", "-", StringComparison.Ordinal);
        }

        alias = alias.Trim('-', '.', '_');
        return alias.Length > 60 ? alias[..60] : alias;
    }
}

/// <summary>プロファイルが使うファイル一式のフルパス。</summary>
public sealed record ScratchProfileFiles(
    string DefinitionFile,
    string ManifestFile,
    string CustomMetadataManifestFile,
    string SeedPlanFile);

/// <summary>スクラッチ組織作成の 1 ステップ（sf コマンド 1 回分）。</summary>
public sealed record ScratchOrgStep(
    string Kind,
    string Label,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout)
{
    /// <summary>表示用コマンドライン（例: sf org create scratch ...）。</summary>
    public string CommandLine => "sf " + string.Join(' ', Arguments);
}

/// <summary>1 ステップの実行結果（履歴にも保存する）。</summary>
public sealed class ScratchOrgStepResult
{
    public string Kind { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string CommandLine { get; set; } = string.Empty;

    public int ExitCode { get; set; }

    public bool TimedOut { get; set; }

    public long DurationMs { get; set; }

    /// <summary>標準出力 + 標準エラー（末尾のみ保持）。</summary>
    public string Output { get; set; } = string.Empty;

    public bool Success => ExitCode == 0 && !TimedOut;
}

/// <summary>実行中の進捗（Phase は "start" / "done"）。</summary>
public sealed record ScratchOrgProgress(
    ScratchOrgStep Step,
    int Index,
    int Total,
    string Phase,
    ScratchOrgStepResult? Result);

/// <summary>作成処理の結果。</summary>
public sealed record ScratchOrgRunResult(
    bool Success,
    string? FailedStepLabel,
    string? ErrorText,
    IReadOnlyList<ScratchOrgStepResult> Steps);

/// <summary>実行履歴の 1 件。</summary>
public sealed class ScratchRunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public string ProfileName { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public string DevHub { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public List<ScratchOrgStepResult> Steps { get; set; } = new();

    [JsonIgnore]
    public string StartedAtText => StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            var duration = FinishedAt - StartedAt;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            return duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{duration.TotalSeconds:0.0}s";
        }
    }

    [JsonIgnore]
    public string ResultText => UiText.T(Success ? "Scratch_ResultOk" : "Scratch_ResultNg");

    /// <summary>詳細ペイン用のテキスト（各ステップのコマンドと出力）。</summary>
    public string ToDetailText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{StartedAtText} / {ProfileName} / {Alias}");
        if (!string.IsNullOrWhiteSpace(Message))
        {
            builder.AppendLine(Message);
        }

        foreach (var step in Steps)
        {
            builder.AppendLine();
            builder.AppendLine($"{(step.Success ? "[OK]" : "[NG]")} {step.CommandLine}");
            builder.AppendLine($"    exit {step.ExitCode}{(step.TimedOut ? " (timeout)" : string.Empty)} / {step.DurationMs} ms");
            if (!string.IsNullOrWhiteSpace(step.Output))
            {
                builder.AppendLine(step.Output);
            }
        }

        return builder.ToString();
    }
}

/// <summary>history.json の内容。</summary>
public sealed class ScratchRunHistoryFile
{
    public List<ScratchRunRecord> Runs { get; set; } = new();
}
