namespace SfUi.Core;

/// <summary>デプロイ操作の種別。</summary>
public static class DeployOperations
{
    public const string Deploy = "deploy";
    public const string Validate = "validate";
    public const string Quick = "quick";
    public const string Report = "report";
    public const string Retrieve = "retrieve";
}

/// <summary>デプロイ / 取得リクエスト（フォーム入力）。</summary>
public sealed record DeployRequest(
    string Operation,
    string? TargetOrg,
    string? SourceDir = null,
    string? Manifest = null,
    string TestLevel = "NoTestRun",
    string? Tests = null,
    int WaitMinutes = 30,
    string? JobId = null,
    bool UseMostRecent = false);

/// <summary>デプロイ系コマンドの引数組み立てと実行。</summary>
public sealed class DeployService
{
    private readonly SfCliRunner _runner;

    public DeployService(SfCliRunner runner)
    {
        _runner = runner;
    }

    public async Task<SfCliResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await _runner.RunAsync(arguments, workingDirectory, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>フォーム入力から sf 引数を組み立てる。</summary>
    public static IReadOnlyList<string> BuildArgs(DeployRequest request)
    {
        var args = new List<string>();
        switch (request.Operation)
        {
            case DeployOperations.Retrieve:
                args.AddRange(new[] { "project", "retrieve", "start" });
                break;
            case DeployOperations.Validate:
                args.AddRange(new[] { "project", "deploy", "validate" });
                break;
            case DeployOperations.Quick:
                args.AddRange(new[] { "project", "deploy", "quick" });
                break;
            case DeployOperations.Report:
                args.AddRange(new[] { "project", "deploy", "report" });
                break;
            default:
                args.AddRange(new[] { "project", "deploy", "start" });
                break;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetOrg))
        {
            args.Add("--target-org");
            args.Add(request.TargetOrg);
        }

        args.Add("--json");

        switch (request.Operation)
        {
            case DeployOperations.Quick:
            case DeployOperations.Report:
                if (!string.IsNullOrWhiteSpace(request.JobId))
                {
                    args.Add("--job-id");
                    args.Add(request.JobId);
                }
                else
                {
                    args.Add("--use-most-recent");
                }

                break;

            default:
                if (!string.IsNullOrWhiteSpace(request.SourceDir))
                {
                    args.Add("--source-dir");
                    args.Add(request.SourceDir);
                }

                if (!string.IsNullOrWhiteSpace(request.Manifest))
                {
                    args.Add("--manifest");
                    args.Add(request.Manifest);
                }

                if (request.Operation is DeployOperations.Deploy or DeployOperations.Validate)
                {
                    if (!string.IsNullOrWhiteSpace(request.TestLevel))
                    {
                        args.Add("--test-level");
                        args.Add(request.TestLevel);
                    }

                    if (!string.IsNullOrWhiteSpace(request.Tests))
                    {
                        args.Add("--tests");
                        args.Add(request.Tests);
                    }

                    if (request.WaitMinutes > 0)
                    {
                        args.Add("--wait");
                        args.Add(request.WaitMinutes.ToString());
                    }
                }

                break;
        }

        return args;
    }

    /// <summary>表示 / コピー用のコマンド文字列（sf から始まり、必要な引用符を付与）。</summary>
    public static string ToDisplayCommand(IReadOnlyList<string> arguments) =>
        "sf " + string.Join(' ', arguments.Select(SfCliRunner.QuoteArgument));
}
