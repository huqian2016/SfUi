using System.Text.Json;

namespace SfUi.Core;

/// <summary>Dev Hub に登録された Org Shape の 1 件。</summary>
public sealed class ScratchShapeInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string CreatedDate { get; set; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}

/// <summary>Dev Hub に登録されたスクラッチ組織スナップショットの 1 件。</summary>
public sealed class ScratchSnapshotInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string CreatedDate { get; set; } = string.Empty;

    public string ExpirationDate { get; set; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

/// <summary>Org Shape / スナップショット操作の結果。</summary>
public sealed record ScratchSignupResult(bool Success, string? ErrorText, string Output);

/// <summary>
/// Org Shape（sf org create/list/delete shape）と
/// スクラッチ組織スナップショット（sf org create/list/delete snapshot）の操作。
/// </summary>
public sealed class ScratchSignupService
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CreateShapeTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CreateSnapshotTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromMinutes(3);
    private const int MaxOutputChars = 8000;

    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public ScratchSignupService(SfCliRunner runner, AppLog log)
    {
        _runner = runner;
        _log = log;
    }

    // ---- コマンド組み立て（テスト対象） ----

    public static IReadOnlyList<string> BuildListShapesArgs() =>
        new[] { "org", "list", "shape", "--json" };

    public static IReadOnlyList<string> BuildCreateShapeArgs(string sourceOrg) =>
        new[] { "org", "create", "shape", "--target-org", sourceOrg, "--json" };

    public static IReadOnlyList<string> BuildDeleteShapeArgs(string sourceOrg) =>
        new[] { "org", "delete", "shape", "--target-org", sourceOrg, "--no-prompt", "--json" };

    public static IReadOnlyList<string> BuildListSnapshotsArgs(string devHub) =>
        new[] { "org", "list", "snapshot", "--target-dev-hub", devHub, "--json" };

    public static IReadOnlyList<string> BuildCreateSnapshotArgs(
        string devHub, string sourceOrg, string name, string? description = null)
    {
        var args = new List<string>
        {
            "org", "create", "snapshot",
            "--target-dev-hub", devHub,
            "--source-org", sourceOrg,
            "--name", name,
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            args.Add("--description");
            args.Add(description.Trim());
        }

        args.Add("--json");
        return args;
    }

    public static IReadOnlyList<string> BuildDeleteSnapshotArgs(string devHub, string snapshot) =>
        new[]
        {
            "org", "delete", "snapshot",
            "--target-dev-hub", devHub,
            "--snapshot", snapshot,
            "--no-prompt",
            "--json",
        };

    // ---- 解析（テスト対象） ----

    /// <summary>org list shape --json の出力を解析する。</summary>
    public static IReadOnlyList<ScratchShapeInfo> ParseShapes(string json)
    {
        var items = ReadResultArray(json);
        return items.Select(element => new ScratchShapeInfo
        {
            Id = ReadString(element, "Id", "ShapeId", "shapeId"),
            Name = ReadString(element, "Name", "ShapeName", "shapeName", "DeveloperName", "developerName"),
            Status = ReadString(element, "Status", "State"),
            CreatedDate = ReadString(element, "CreatedDate", "Created", "createdDate"),
        }).ToList();
    }

    /// <summary>org list snapshot --json の出力を解析する。</summary>
    public static IReadOnlyList<ScratchSnapshotInfo> ParseSnapshots(string json)
    {
        var items = ReadResultArray(json);
        return items.Select(element => new ScratchSnapshotInfo
        {
            Id = ReadString(element, "Id", "SnapshotId", "snapshotId"),
            Name = ReadString(element, "SnapshotName", "snapshotName", "Name", "DeveloperName", "developerName"),
            Status = ReadString(element, "Status", "State"),
            CreatedDate = ReadString(element, "CreatedDate", "Created", "createdDate"),
            ExpirationDate = ReadString(element, "ExpirationDate", "expirationDate", "ExpiresAt", "expiresAt"),
        }).ToList();
    }

    private static List<JsonElement> ReadResultArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<JsonElement>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray().Select(element => element.Clone()).ToList();
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Array)
            {
                return result.EnumerateArray().Select(element => element.Clone()).ToList();
            }
        }
        catch (JsonException)
        {
            // 解析できない出力は空として扱う
        }

        return new List<JsonElement>();
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    // ---- 実行 ----

    public Task<ScratchSignupResult> ListShapesAsync(CancellationToken cancellationToken) =>
        RunAsync(BuildListShapesArgs(), ListTimeout, "shape 一覧", cancellationToken);

    public Task<ScratchSignupResult> CreateShapeAsync(string sourceOrg, CancellationToken cancellationToken) =>
        RunAsync(BuildCreateShapeArgs(sourceOrg), CreateShapeTimeout, "shape 作成", cancellationToken);

    public Task<ScratchSignupResult> DeleteShapeAsync(string sourceOrg, CancellationToken cancellationToken) =>
        RunAsync(BuildDeleteShapeArgs(sourceOrg), DeleteTimeout, "shape 削除", cancellationToken);

    public Task<ScratchSignupResult> ListSnapshotsAsync(string devHub, CancellationToken cancellationToken) =>
        RunAsync(BuildListSnapshotsArgs(devHub), ListTimeout, "スナップショット一覧", cancellationToken);

    public Task<ScratchSignupResult> CreateSnapshotAsync(
        string devHub, string sourceOrg, string name, string? description, CancellationToken cancellationToken) =>
        RunAsync(
            BuildCreateSnapshotArgs(devHub, sourceOrg, name, description),
            CreateSnapshotTimeout,
            $"スナップショット作成 {name}",
            cancellationToken);

    public Task<ScratchSignupResult> DeleteSnapshotAsync(string devHub, string snapshot, CancellationToken cancellationToken) =>
        RunAsync(BuildDeleteSnapshotArgs(devHub, snapshot), DeleteTimeout, $"スナップショット削除 {snapshot}", cancellationToken);

    private async Task<ScratchSignupResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, string operation, CancellationToken cancellationToken)
    {
        SfCliResult result;
        try
        {
            result = await _runner.RunAsync(arguments, null, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"スクラッチ組織: {operation} に失敗: {ex.Message}");
            return new ScratchSignupResult(false, ex.Message, ex.Message);
        }

        var output = CombineOutput(result);
        if (!result.Success)
        {
            var error = ScratchOrgService.ExtractError(result);
            _log.Warn($"スクラッチ組織: {operation} に失敗: {error}");
            return new ScratchSignupResult(false, error, output);
        }

        _log.Info($"スクラッチ組織: {operation} 完了（{result.Duration.TotalMilliseconds:0} ms）");
        return new ScratchSignupResult(true, null, output);
    }

    private static string CombineOutput(SfCliResult result)
    {
        var text = result.StdOut?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            text = text.Length > 0 ? text + Environment.NewLine + result.StdErr.Trim() : result.StdErr.Trim();
        }

        return text.Length <= MaxOutputChars ? text : "…" + text[^MaxOutputChars..];
    }
}
