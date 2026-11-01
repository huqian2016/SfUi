using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>接続アプリの取得・デプロイ・削除（テスト差し替え用）。</summary>
public interface IConnectedAppService
{
    Task<IReadOnlyList<ConnectedAppSummary>> ListAsync(string targetOrg, string kind, CancellationToken cancellationToken);

    Task<ConnectedAppDocument?> GetAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken);

    Task<ConnectedAppDeployResult> DeployAsync(string targetOrg, IReadOnlyList<ConnectedAppFile> files, bool dryRun, CancellationToken cancellationToken);

    Task<ConnectedAppDeployResult> DeleteAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken);
}

/// <summary>
/// 接続アプリ（ConnectedApp / ExternalClientApplication）の一覧取得・参照・デプロイ・削除。
/// メタデータ操作は一時的な SFDX プロジェクトを作り、sf project retrieve / deploy で行う。
/// </summary>
public sealed class ConnectedAppService : IConnectedAppService
{
    private static readonly TimeSpan RetrieveTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DeployTimeout = TimeSpan.FromMinutes(20);
    private const int MaxOutputChars = 8000;

    private readonly SfCliRunner _runner;
    private readonly AppLog _log;
    private readonly AppPaths _paths;

    /// <summary>CLI の自動更新チェック（標準出力への注意書き）を抑止する。</summary>
    private static readonly Dictionary<string, string> CliEnvironment = new()
    {
        ["SF_DISABLE_AUTOUPDATE"] = "true",
        ["SF_SKIP_NEW_VERSION_CHECK"] = "true",
    };

    public ConnectedAppService(SfCliRunner runner, AppLog log, AppPaths paths)
    {
        _runner = runner;
        _log = log;
        _paths = paths;
    }

    // ---------------------------------------------------------------- 引数

    /// <summary>取得に使うメタデータ型（種別ごと）。</summary>
    public static IReadOnlyList<string> MetadataTypesFor(string kind) =>
        string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                "ExternalClientApplication",
                "ExtlClntAppGlobalOauthSettings",
                "ExtlClntAppOauthSettings",
                "ExtlClntAppOauthConfigurablePolicies",
                "ExtlClntAppConfigurablePolicies",
            }
            : new[] { "ConnectedApp" };

    public static IReadOnlyList<string> BuildRetrieveArgs(string kind, string? name, string outputDirectory, string targetOrg)
    {
        var args = new List<string> { "project", "retrieve", "start" };
        var types = MetadataTypesFor(kind);
        var single = string.Equals(kind, ConnectedAppKinds.ConnectedApp, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(name);
        foreach (var type in types)
        {
            args.Add("--metadata");
            args.Add(single ? $"{type}:{name}" : type);
        }

        args.AddRange(new[] { "--target-org", targetOrg, "--output-dir", outputDirectory, "--ignore-conflicts", "--json" });
        return args;
    }

    public static IReadOnlyList<string> BuildDeployArgs(string sourceDirectory, string targetOrg, bool dryRun)
    {
        var args = new List<string> { "project", "deploy", "start", "--source-dir", sourceDirectory, "--target-org", targetOrg };
        if (dryRun)
        {
            args.Add("--dry-run");
        }

        args.Add("--json");
        return args;
    }

    public static IReadOnlyList<string> BuildDeleteArgs(string manifestFile, string destructiveChangesFile, string targetOrg) =>
        new[]
        {
            "project", "deploy", "start",
            "--manifest", manifestFile,
            "--pre-destructive-changes", destructiveChangesFile,
            "--target-org", targetOrg,
            "--json",
        };

    /// <summary>削除用の package.xml / destructiveChanges.xml を作る。</summary>
    public static (string PackageXml, string DestructiveXml) BuildDeleteManifests(string kind, string name)
    {
        var type = string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? "ExternalClientApplication"
            : "ConnectedApp";
        const string header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
        var package = header
                      + "<Package xmlns=\"http://soap.sforce.com/2006/04/metadata\">\n"
                      + "    <version>62.0</version>\n"
                      + "</Package>\n";
        var destructive = header
                          + "<Package xmlns=\"http://soap.sforce.com/2006/04/metadata\">\n"
                          + "    <types>\n"
                          + $"        <members>{System.Security.SecurityElement.Escape(name)}</members>\n"
                          + $"        <name>{type}</name>\n"
                          + "    </types>\n"
                          + "    <version>62.0</version>\n"
                          + "</Package>\n";
        return (package, destructive);
    }

    // ---------------------------------------------------------------- 結果解析

    /// <summary>sf deploy の JSON 出力を解析する（純関数）。</summary>
    public static ConnectedAppDeployResult ParseDeployResult(SfCliResult result)
    {
        var output = Combine(result);
        var json = ExtractJson(result.StdOut);
        if (json is null)
        {
            return new ConnectedAppDeployResult
            {
                Success = result.Success,
                Status = result.Success ? "Succeeded" : "Failed",
                Problems = result.Success
                    ? Array.Empty<ConnectedAppDeployProblem>()
                    : new[] { new ConnectedAppDeployProblem(null, null, FirstLine(result.StdErr) ?? FirstLine(result.StdOut) ?? "") },
                Output = output,
            };
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var code) ? code : -1;
            if (!root.TryGetProperty("result", out var payload))
            {
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return new ConnectedAppDeployResult
                {
                    Success = false,
                    Status = "Failed",
                    Problems = new[] { new ConnectedAppDeployProblem(null, null, message ?? "") },
                    Output = output,
                };
            }

            var success = payload.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var statusText = payload.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var deployUrl = payload.TryGetProperty("deployUrl", out var u) ? u.GetString() : null;
            var problems = new List<ConnectedAppDeployProblem>();

            if (payload.TryGetProperty("details", out var details)
                && details.TryGetProperty("componentFailures", out var failures)
                && failures.ValueKind == JsonValueKind.Array)
            {
                foreach (var failure in failures.EnumerateArray())
                {
                    problems.Add(new ConnectedAppDeployProblem(
                        failure.TryGetProperty("fileName", out var f) ? f.GetString() : null,
                        failure.TryGetProperty("componentType", out var t) ? t.GetString() : null,
                        failure.TryGetProperty("problem", out var p) ? p.GetString() ?? "" : ""));
                }
            }

            if (!success && problems.Count == 0)
            {
                var message = payload.TryGetProperty("message", out var pm) ? pm.GetString() : null;
                problems.Add(new ConnectedAppDeployProblem(null, null, message ?? UiText.T("Conn_ErrorDeployFailed")));
            }

            return new ConnectedAppDeployResult
            {
                Success = success && status == 0,
                Status = string.IsNullOrWhiteSpace(statusText) ? (success ? "Succeeded" : "Failed") : statusText,
                Problems = problems,
                Output = output,
                DeployUrl = deployUrl,
            };
        }
        catch (JsonException ex)
        {
            return new ConnectedAppDeployResult
            {
                Success = false,
                Status = "Failed",
                Problems = new[] { new ConnectedAppDeployProblem(null, null, ex.Message) },
                Output = output,
            };
        }
    }

    private static string? ExtractJson(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        // CLI の更新通知などが前後に混ざっても JSON 部分だけを取り出す
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        return start < 0 || end <= start ? null : stdout[start..(end + 1)];
    }

    private static string Combine(SfCliResult result)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            builder.AppendLine(result.StdOut.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            builder.AppendLine(result.StdErr.Trim());
        }

        var text = builder.ToString().Trim();
        return text.Length <= MaxOutputChars ? text : "…" + text[^MaxOutputChars..];
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line;
    }

    private static string Shorten(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Length <= 400 ? text : text[..400] + "…";

    // ---------------------------------------------------------------- 操作

    /// <summary>対象組織の接続アプリ一覧を取得する。</summary>
    public async Task<IReadOnlyList<ConnectedAppSummary>> ListAsync(string targetOrg, string kind, CancellationToken cancellationToken)
    {
        var root = CreateProject();
        try
        {
            var retrieved = Path.Combine(root, "retrieved");
            var files = await RetrieveAsync(targetOrg, kind, null, retrieved, root, cancellationToken).ConfigureAwait(false);
            return ConnectedAppXml.ParseSummaries(kind, files);
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>1 件分のメタデータを取得する。</summary>
    public async Task<ConnectedAppDocument?> GetAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken)
    {
        var root = CreateProject();
        try
        {
            var retrieved = Path.Combine(root, "retrieved");
            var files = await RetrieveAsync(targetOrg, kind, name, retrieved, root, cancellationToken).ConfigureAwait(false);
            return ConnectedAppXml.ParseDocument(kind, name, files);
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>メタデータ ファイル一式をデプロイする。</summary>
    public async Task<ConnectedAppDeployResult> DeployAsync(
        string targetOrg,
        IReadOnlyList<ConnectedAppFile> files,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return ConnectedAppDeployResult.Fail(UiText.T("Conn_ErrorNothingToDeploy"));
        }

        var root = CreateProject();
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(root, "force-app", "main", "default", file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }

            var sourceDirectory = Path.Combine(root, "force-app");
            var result = await _runner.RunAsync(
                BuildDeployArgs(sourceDirectory, targetOrg, dryRun),
                root,
                DeployTimeout,
                cancellationToken,
                CliEnvironment).ConfigureAwait(false);

            var parsed = ParseDeployResult(result);
            _log.Info($"接続アプリ: デプロイ{(dryRun ? "（検証）" : "")} {targetOrg} / {files.Count} ファイル / 成功={parsed.Success}");
            if (!parsed.Success)
            {
                _log.Warn($"接続アプリ: デプロイ失敗 {parsed.ProblemText}");
                _log.Warn($"接続アプリ: stdout={Shorten(result.StdOut)} / stderr={Shorten(result.StdErr)}");
            }

            return parsed;
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>接続アプリを削除する（destructiveChanges）。</summary>
    public async Task<ConnectedAppDeployResult> DeleteAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken)
    {
        var root = CreateProject();
        try
        {
            var (packageXml, destructiveXml) = BuildDeleteManifests(kind, name);
            var packagePath = Path.Combine(root, "package.xml");
            var destructivePath = Path.Combine(root, "destructiveChanges.xml");
            await File.WriteAllTextAsync(packagePath, packageXml, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(destructivePath, destructiveXml, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            var result = await _runner.RunAsync(
                BuildDeleteArgs(packagePath, destructivePath, targetOrg),
                root,
                DeployTimeout,
                cancellationToken).ConfigureAwait(false);

            var parsed = ParseDeployResult(result);
            _log.Info($"接続アプリ: 削除 {targetOrg} / {kind} / {name} / 成功={parsed.Success}");
            if (!parsed.Success)
            {
                _log.Warn($"接続アプリ: 削除失敗 {parsed.ProblemText}");
            }

            return parsed;
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ---------------------------------------------------------------- 内部

    private async Task<IReadOnlyList<ConnectedAppFile>> RetrieveAsync(
        string targetOrg,
        string kind,
        string? name,
        string outputDirectory,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = await _runner.RunAsync(
            BuildRetrieveArgs(kind, name, outputDirectory, targetOrg),
            workingDirectory,
            RetrieveTimeout,
            cancellationToken,
            CliEnvironment).ConfigureAwait(false);

        if (!result.Success)
        {
            // CLI の注意書き（stderr）より、JSON のエラー メッセージを優先する
            var parsed = ParseDeployResult(result);
            var message = string.IsNullOrWhiteSpace(parsed.ProblemText)
                ? FirstLine(result.StdErr) ?? FirstLine(result.StdOut) ?? ""
                : parsed.ProblemText;
            _log.Warn($"接続アプリ: 取得に失敗（{targetOrg} / {kind}）: {message}");
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? UiText.T("Conn_ErrorRetrieveFailed") : message);
        }

        var files = new List<ConnectedAppFile>();
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*-meta.xml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(outputDirectory, path).Replace('\\', '/');
            var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            files.Add(new ConnectedAppFile(relative, content));
        }

        return files;
    }

    private string CreateProject()
    {
        var root = Path.Combine(_paths.TempDirectory, "connected-apps", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "force-app", "main", "default"));
        File.WriteAllText(
            Path.Combine(root, "sfdx-project.json"),
            """
            {
              "packageDirectories": [ { "path": "force-app", "default": true } ],
              "sourceApiVersion": "62.0",
              "sfdcLoginUrl": "https://login.salesforce.com"
            }
            """,
            new UTF8Encoding(false));
        return root;
    }

    private void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // 一時フォルダーの後始末失敗は処理結果に影響させない
            _log.Warn($"接続アプリ: 一時フォルダーを削除できませんでした（{root}）: {ex.Message}");
        }
    }
}
