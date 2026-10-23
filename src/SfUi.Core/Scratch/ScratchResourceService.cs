namespace SfUi.Core;

/// <summary>取り込み時のファイル変化。</summary>
public enum ScratchRetrieveChangeKind
{
    Added,
    Changed,
    Same,
}

/// <summary>取り込んだファイル 1 件の差分。</summary>
public sealed record ScratchRetrieveChange(string RelativePath, ScratchRetrieveChangeKind Kind)
{
    /// <summary>表示用の種別（追加 / 変更 / 同一）。</summary>
    public string KindText => UiText.T(Kind switch
    {
        ScratchRetrieveChangeKind.Added => "Scratch_ChangeAdded",
        ScratchRetrieveChangeKind.Changed => "Scratch_ChangeChanged",
        _ => "Scratch_ChangeSame",
    });
}

/// <summary>取り込み結果の差分一覧。</summary>
public sealed class ScratchRetrieveDiff
{
    public ScratchRetrieveDiff(string retrievedDirectory, IReadOnlyList<ScratchRetrieveChange> changes)
    {
        RetrievedDirectory = retrievedDirectory;
        Changes = changes;
    }

    public string RetrievedDirectory { get; }

    public IReadOnlyList<ScratchRetrieveChange> Changes { get; }

    public int AddedCount => Changes.Count(c => c.Kind == ScratchRetrieveChangeKind.Added);

    public int ChangedCount => Changes.Count(c => c.Kind == ScratchRetrieveChangeKind.Changed);

    public int SameCount => Changes.Count(c => c.Kind == ScratchRetrieveChangeKind.Same);

    /// <summary>プロファイルへ反映する対象（追加 + 変更）の件数。</summary>
    public int PendingCount => AddedCount + ChangedCount;
}

/// <summary>sf project retrieve start の結果。</summary>
public sealed record ScratchRetrieveResult(bool Success, string? ErrorText, string Output);

/// <summary>
/// 別組織からのメタデータ取り込み（sf project retrieve start）と、
/// 取り込み結果とプロファイルの差分計算 / 反映を行う。
/// </summary>
public sealed class ScratchResourceService
{
    private static readonly TimeSpan RetrieveTimeout = TimeSpan.FromMinutes(60);
    private const int MaxOutputChars = 8000;

    private readonly SfCliRunner _runner;
    private readonly AppLog _log;

    public ScratchResourceService(SfCliRunner runner, AppLog log)
    {
        _runner = runner;
        _log = log;
    }

    public static IReadOnlyList<string> BuildRetrieveArgs(string manifestFile, string target, string outputDirectory) =>
        new[]
        {
            "project", "retrieve", "start",
            "--manifest", manifestFile,
            "--target-org", target,
            "--output-dir", outputDirectory,
            "--ignore-conflicts",
            "--json",
        };

    /// <summary>
    /// 指定した組織からマニフェストの内容を取得する（プロファイル フォルダーの SFDX プロジェクトを cwd に使う）。
    /// </summary>
    public async Task<ScratchRetrieveResult> RetrieveAsync(
        string manifestFile,
        string target,
        string profileDirectory,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = await _runner.RunAsync(
            BuildRetrieveArgs(manifestFile, target, outputDirectory),
            profileDirectory,
            RetrieveTimeout,
            cancellationToken).ConfigureAwait(false);

        var output = CombineOutput(result);
        if (output.Length > MaxOutputChars)
        {
            output = "…" + output[^MaxOutputChars..];
        }

        if (!result.Success)
        {
            var error = ScratchOrgService.ExtractError(result);
            _log.Warn($"スクラッチ組織: 取り込みに失敗（{target} / {Path.GetFileName(manifestFile)}）: {error}");
            return new ScratchRetrieveResult(false, error, output);
        }

        _log.Info($"スクラッチ組織: 取り込み完了（{target} / {Path.GetFileName(manifestFile)} / {result.Duration.TotalMilliseconds:0} ms）");
        return new ScratchRetrieveResult(true, null, output);
    }

    /// <summary>取り込んだファイルとプロファイルの既存ファイルを比較する（削除は扱わない）。</summary>
    public static ScratchRetrieveDiff Compare(string retrievedDirectory, string targetDirectory)
    {
        var changes = new List<ScratchRetrieveChange>();
        if (!Directory.Exists(retrievedDirectory))
        {
            return new ScratchRetrieveDiff(retrievedDirectory, changes);
        }

        foreach (var file in Directory
                     .EnumerateFiles(retrievedDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(retrievedDirectory, file).Replace('\\', '/');
            if (string.Equals(relativePath, "package.xml", StringComparison.OrdinalIgnoreCase))
            {
                // マニフェスト自体はプロファイルの manifest/ を使うため対象外
                continue;
            }

            var target = Path.Combine(targetDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var kind = !File.Exists(target)
                ? ScratchRetrieveChangeKind.Added
                : FilesEqual(file, target)
                    ? ScratchRetrieveChangeKind.Same
                    : ScratchRetrieveChangeKind.Changed;
            changes.Add(new ScratchRetrieveChange(relativePath, kind));
        }

        return new ScratchRetrieveDiff(retrievedDirectory, changes);
    }

    /// <summary>追加 / 変更のファイルをプロファイルへコピーする（上書き前の内容は .bak に退避）。</summary>
    public static int Apply(ScratchRetrieveDiff diff, string targetDirectory)
    {
        var applied = 0;
        foreach (var change in diff.Changes)
        {
            if (change.Kind == ScratchRetrieveChangeKind.Same)
            {
                continue;
            }

            var source = Path.Combine(
                diff.RetrievedDirectory, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(
                targetDirectory, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(target))
            {
                File.Copy(target, target + ".bak", overwrite: true);
            }

            File.Copy(source, target, overwrite: true);
            applied++;
        }

        return applied;
    }

    private static bool FilesEqual(string left, string right)
    {
        try
        {
            var leftInfo = new FileInfo(left);
            var rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
        }
        catch
        {
            return false;
        }
    }

    private static string CombineOutput(SfCliResult result)
    {
        var text = result.StdOut?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            text = text.Length > 0 ? text + Environment.NewLine + result.StdErr.Trim() : result.StdErr.Trim();
        }

        return text;
    }
}
