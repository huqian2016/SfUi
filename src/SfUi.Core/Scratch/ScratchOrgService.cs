using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// スクラッチ組織の作成（sf org create scratch 以降の一連のコマンド）を組み立てて実行する。
/// コマンドは「作成 → メタデータ → カスタム メタデータ → 権限セット → シードデータ → 確認」の順。
/// </summary>
public sealed class ScratchOrgService
{
    private const int MaxOutputChars = 8000;

    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public ScratchOrgService(SfCliRunner runner, AppLog log)
    {
        _runner = runner;
        _log = log;
    }

    // ---- コマンド組み立て（テスト対象） ----

    public static IReadOnlyList<string> BuildCreateArgs(
        string devHub,
        string definitionFile,
        string alias,
        int durationDays,
        int waitMinutes,
        bool setDefault,
        string? orgShapeId = null)
    {
        var args = new List<string>
        {
            "org", "create", "scratch",
            "--target-dev-hub", devHub,
            "--definition-file", definitionFile,
            "--alias", alias,
            "--duration-days", durationDays.ToString(),
            "--wait", waitMinutes.ToString(),
        };

        if (setDefault)
        {
            args.Add("--set-default");
        }

        // Org Shape を使う場合は --source-org（15 桁の shape ID）。edition は定義ファイル側の値が上書きされる。
        if (!string.IsNullOrWhiteSpace(orgShapeId))
        {
            args.Add("--source-org");
            args.Add(orgShapeId.Trim());
        }

        args.Add("--json");
        return args;
    }

    public static IReadOnlyList<string> BuildDeployArgs(string manifestFile, string target, int waitMinutes = 30) =>
        new[]
        {
            "project", "deploy", "start",
            "--manifest", manifestFile,
            "--target-org", target,
            "--wait", waitMinutes.ToString(),
            "--ignore-conflicts",
            "--json",
        };

    public static IReadOnlyList<string> BuildAssignPermissionSetArgs(string permissionSet, string target) =>
        new[]
        {
            "org", "assign", "permset",
            "--name", permissionSet,
            "--target-org", target,
            "--json",
        };

    public static IReadOnlyList<string> BuildApexRunArgs(string scriptFile, string target) =>
        new[]
        {
            "apex", "run",
            "--file", scriptFile,
            "--target-org", target,
            "--json",
        };

    public static IReadOnlyList<string> BuildImportTreeArgs(string planFile, string target) =>
        new[]
        {
            "data", "import", "tree",
            "--plan", planFile,
            "--target-org", target,
            "--json",
        };

    public static IReadOnlyList<string> BuildDisplayArgs(string target) =>
        new[] { "org", "display", "--target-org", target, "--json" };

    /// <summary>
    /// プロファイルとファイル一式から実行ステップを組み立てる。
    /// チェック OFF の資源はコマンド自体を作らない（リポジトリのファイルは削除しない）。
    /// </summary>
    public static IReadOnlyList<ScratchOrgStep> BuildPlan(
        ScratchProfile profile,
        string devHub,
        string alias,
        ScratchProfileFiles files)
    {
        var steps = new List<ScratchOrgStep>();
        var profileDirectory = Path.GetDirectoryName(files.ProjectFile) ?? string.Empty;
        var hasSources = HasSourceFiles(files.PackageDirectory);
        steps.Add(new ScratchOrgStep(
            "create",
            UiText.T("Scratch_StepCreate"),
            BuildCreateArgs(
                devHub,
                files.DefinitionFile,
                alias,
                profile.DurationDays,
                profile.WaitMinutes,
                profile.SetDefault,
                profile.OrgShapeId),
            TimeSpan.FromMinutes(profile.WaitMinutes + 20)));

        if (profile.UseMetadata && hasSources && File.Exists(files.ManifestFile))
        {
            steps.Add(new ScratchOrgStep(
                "metadata",
                UiText.T("Scratch_StepMetadata"),
                BuildDeployArgs(files.ManifestFile, alias),
                TimeSpan.FromMinutes(60),
                profileDirectory));
        }

        if (profile.UseCustomMetadata && hasSources && File.Exists(files.CustomMetadataManifestFile))
        {
            steps.Add(new ScratchOrgStep(
                "custom-metadata",
                UiText.T("Scratch_StepCustomMetadata"),
                BuildDeployArgs(files.CustomMetadataManifestFile, alias),
                TimeSpan.FromMinutes(30),
                profileDirectory));
        }

        if (profile.UseStaticResources && hasSources && File.Exists(files.StaticResourcesManifestFile))
        {
            steps.Add(new ScratchOrgStep(
                "static-resources",
                UiText.T("Scratch_StepStaticResources"),
                BuildDeployArgs(files.StaticResourcesManifestFile, alias),
                TimeSpan.FromMinutes(30),
                profileDirectory));
        }

        if (profile.UseConnectedApps && hasSources && File.Exists(files.ConnectedAppsManifestFile))
        {
            steps.Add(new ScratchOrgStep(
                "connected-apps",
                UiText.T("Scratch_StepConnectedApps"),
                BuildDeployArgs(files.ConnectedAppsManifestFile, alias),
                TimeSpan.FromMinutes(30),
                profileDirectory));
        }

        if (profile.UseReports && hasSources && File.Exists(files.ReportsManifestFile))
        {
            steps.Add(new ScratchOrgStep(
                "reports",
                UiText.T("Scratch_StepReports"),
                BuildDeployArgs(files.ReportsManifestFile, alias),
                TimeSpan.FromMinutes(30),
                profileDirectory));
        }

        if (profile.UseApexScripts)
        {
            foreach (var script in ListApexScripts(files.ApexScriptsDirectory))
            {
                steps.Add(new ScratchOrgStep(
                    "apex",
                    UiText.T("Scratch_StepApexFmt", Path.GetFileName(script)),
                    BuildApexRunArgs(script, alias),
                    TimeSpan.FromMinutes(30),
                    profileDirectory));
            }
        }

        if (profile.UsePermissionSets)
        {
            foreach (var permissionSet in profile.PermissionSets)
            {
                steps.Add(new ScratchOrgStep(
                    "permset",
                    UiText.T("Scratch_StepPermSetFmt", permissionSet),
                    BuildAssignPermissionSetArgs(permissionSet, alias),
                    TimeSpan.FromMinutes(5)));
            }
        }

        if (profile.UseSeedData && HasSeedObjects(files.SeedPlanFile))
        {
            steps.Add(new ScratchOrgStep(
                "seed",
                UiText.T("Scratch_StepSeed"),
                BuildImportTreeArgs(files.SeedPlanFile, alias),
                TimeSpan.FromMinutes(30)));
        }

        steps.Add(new ScratchOrgStep(
            "verify",
            UiText.T("Scratch_StepVerify"),
            BuildDisplayArgs(alias),
            TimeSpan.FromMinutes(3)));

        return steps;
    }

    /// <summary>plan.json にインポート対象（objects が 1 件以上）があるか。</summary>
    public static bool HasSeedObjects(string planFile)
    {
        try
        {
            if (!File.Exists(planFile))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(planFile));
            return document.RootElement.TryGetProperty("objects", out var objects)
                   && objects.ValueKind == JsonValueKind.Array
                   && objects.GetArrayLength() > 0;
        }
        catch
        {
            // 壊れた plan.json は「対象なし」として扱う（作成自体は続行）
            return false;
        }
    }

    /// <summary>force-app/main/default にソースファイルがあるか（無い状態で deploy すると必ず失敗するため）。</summary>
    public static bool HasSourceFiles(string packageDirectory)
    {
        try
        {
            return Directory.Exists(packageDirectory)
                   && Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>scripts/apex の *.apex を名前順で返す。</summary>
    public static IReadOnlyList<string> ListApexScripts(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.apex", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ---- 実行 ----

    /// <summary>
    /// ステップを順に実行する。失敗した時点で中断し、成功したステップまでの結果を返す。
    /// </summary>
    public async Task<ScratchOrgRunResult> RunAsync(
        IReadOnlyList<ScratchOrgStep> steps,
        IProgress<ScratchOrgProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ScratchOrgStepResult>();
        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[index];
            progress?.Report(new ScratchOrgProgress(step, index + 1, steps.Count, "start", null));

            SfCliResult result;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                result = await _runner.RunAsync(step.Arguments, step.WorkingDirectory, step.Timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var failed = new ScratchOrgStepResult
                {
                    Kind = step.Kind,
                    Label = step.Label,
                    CommandLine = step.CommandLine,
                    ExitCode = -1,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Output = TrimOutput(ex.Message),
                };
                results.Add(failed);
                progress?.Report(new ScratchOrgProgress(step, index + 1, steps.Count, "done", failed));
                _log.Warn($"スクラッチ組織: {step.Label} に失敗: {ex.Message}");
                return new ScratchOrgRunResult(false, step.Label, ex.Message, results);
            }

            stopwatch.Stop();
            var output = TrimOutput(CombineOutput(result));
            var stepResult = new ScratchOrgStepResult
            {
                Kind = step.Kind,
                Label = step.Label,
                CommandLine = step.CommandLine,
                ExitCode = result.ExitCode,
                TimedOut = result.TimedOut,
                DurationMs = (long)result.Duration.TotalMilliseconds,
                Output = output,
            };
            results.Add(stepResult);
            progress?.Report(new ScratchOrgProgress(step, index + 1, steps.Count, "done", stepResult));

            if (!result.Success)
            {
                var error = ExtractError(result);
                _log.Warn($"スクラッチ組織: {step.Label} に失敗（exit {result.ExitCode}）: {error}");
                return new ScratchOrgRunResult(false, step.Label, error, results);
            }

            _log.Info($"スクラッチ組織: {step.Label} 完了（{result.Duration.TotalMilliseconds:0} ms）");
        }

        return new ScratchOrgRunResult(true, null, null, results);
    }

    /// <summary>失敗理由の短い表示（JSON の message → stderr の末尾行 → 終了コード）。</summary>
    public static string ExtractError(SfCliResult result)
    {
        if (result.TimedOut)
        {
            return UiText.T("Scratch_ErrorTimeout");
        }

        if (!string.IsNullOrWhiteSpace(result.StdOut) && TryReadMessage(result.StdOut, out var message))
        {
            return message;
        }

        var stderrLines = (result.StdErr ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Warning", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (stderrLines.Count > 0)
        {
            return TrimLine(stderrLines[^1]);
        }

        return UiText.T("Scratch_ErrorExitFmt", result.ExitCode);
    }

    private static bool TryReadMessage(string stdout, out string message)
    {
        message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                message = TrimLine(value.GetString()!);
                return true;
            }
        }
        catch (JsonException)
        {
            // JSON でない出力は無視
        }

        return false;
    }

    private static string TrimLine(string value)
    {
        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length > 400 ? single[..400] : single;
    }

    private static string CombineOutput(SfCliResult result)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            builder.Append(result.StdOut.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(result.StdErr.Trim());
        }

        return builder.ToString();
    }

    private static string TrimOutput(string output) =>
        output.Length <= MaxOutputChars ? output : "…" + output[^MaxOutputChars..];
}
