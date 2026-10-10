using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// ソース エディタのローカル ワークスペース。
/// ドラフト（baseline = 取得時の組織内容 / working = 編集中の内容）と、反映用の一時 sfdx プロジェクト
/// （data/source-editor/work）を管理する。
/// </summary>
public sealed class SourceEditorWorkspace
{
    private readonly AppLog _log;

    public SourceEditorWorkspace(AppPaths paths, AppLog log)
    {
        Root = Path.Combine(paths.DataRoot, "source-editor");
        _log = log;
    }

    /// <summary>data/source-editor ルート。</summary>
    public string Root { get; }

    /// <summary>反映用の一時 sfdx プロジェクト（sfdx-project.json + force-app）。</summary>
    public string WorkProjectRoot => Path.Combine(Root, "work");

    /// <summary>組織キー（ユーザー名から安全なフォルダー名を作る）。</summary>
    public static string KeyFor(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "org";
        }

        var key = new string(username.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-')
            .ToArray()).Trim('-');
        return key.Length == 0 ? "org" : key;
    }

    /// <summary>メンバーのドラフト フォルダー（&lt;root&gt;/&lt;orgKey&gt;/&lt;kind&gt;/&lt;name&gt;）。</summary>
    public string MemberFolder(string orgKey, SourceMemberInfo member)
        => Path.Combine(Root, orgKey, member.Kind.ToString(), SafeName(member.Name));

    /// <summary>反映用プロジェクトを用意する（sfdx-project.json を必要時に作成）。</summary>
    public async Task EnsureWorkProjectAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(WorkProjectRoot);
        var projectFile = Path.Combine(WorkProjectRoot, "sfdx-project.json");
        if (!File.Exists(projectFile))
        {
            await File.WriteAllTextAsync(
                projectFile,
                "{\"packageDirectories\":[{\"path\":\"force-app\",\"default\":true}],\"namespace\":\"\",\"sourceApiVersion\":\"62.0\"}",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            _log.Info($"ソース エディタ: 反映用プロジェクトを作成しました（{WorkProjectRoot}）。");
        }
    }

    /// <summary>ドラフトを読み込む（無ければ null）。</summary>
    public async Task<SourceDraft?> LoadDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        var folder = MemberFolder(orgKey, member);
        var baseline = await ReadFolderAsync(Path.Combine(folder, "baseline"), cancellationToken).ConfigureAwait(false);
        var working = await ReadFolderAsync(Path.Combine(folder, "working"), cancellationToken).ConfigureAwait(false);
        if (baseline.Count == 0 || working.Count == 0)
        {
            return null;
        }

        return new SourceDraft(baseline, working);
    }

    /// <summary>ドラフト（baseline + working）を保存する。</summary>
    public async Task SaveDraftAsync(
        string orgKey,
        SourceMemberInfo member,
        IReadOnlyList<SourceFileInfo> baseline,
        IReadOnlyList<SourceFileInfo> working,
        CancellationToken cancellationToken = default)
    {
        var folder = MemberFolder(orgKey, member);
        await WriteFolderAsync(Path.Combine(folder, "baseline"), baseline, cancellationToken).ConfigureAwait(false);
        await WriteFolderAsync(Path.Combine(folder, "working"), working, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ドラフトを削除する（削除・破棄用）。</summary>
    public Task ClearDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        var folder = MemberFolder(orgKey, member);
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"ソース エディタ: ドラフトを削除できませんでした（{folder}）: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    // ---- ローカル履歴（Phase 7: 反映成功時のスナップショット）----

    /// <summary>ローカル履歴の上限（これより古い版は破棄）。</summary>
    public const int MaxHistoryEntries = 20;

    /// <summary>ローカル履歴（新しい順）を読み込む。</summary>
    public async Task<IReadOnlyList<SourceHistoryEntry>> LoadHistoryAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        var path = HistoryPath(orgKey, member);
        if (!File.Exists(path))
        {
            return Array.Empty<SourceHistoryEntry>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<SourceHistoryEntry>>(json);
            return entries is null
                ? Array.Empty<SourceHistoryEntry>()
                : entries.OrderByDescending(e => e.DeployedAt).ToList();
        }
        catch (Exception ex)
        {
            _log.Warn($"ソース エディタ: 履歴を読み込めませんでした（{path}）: {ex.Message}");
            return Array.Empty<SourceHistoryEntry>();
        }
    }

    /// <summary>反映成功時のスナップショットを履歴の先頭へ追加する（上限 20 版）。</summary>
    public async Task SaveHistoryAsync(string orgKey, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return;
        }

        var existing = await LoadHistoryAsync(orgKey, member, cancellationToken).ConfigureAwait(false);
        var entries = new List<SourceHistoryEntry> { new(DateTime.Now, files) };
        entries.AddRange(existing.Take(MaxHistoryEntries - 1));

        var path = HistoryPath(orgKey, member);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(entries);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ローカル履歴を消去する。</summary>
    public Task ClearHistoryAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        var path = HistoryPath(orgKey, member);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"ソース エディタ: 履歴を削除できませんでした（{path}）: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private string HistoryPath(string orgKey, SourceMemberInfo member)
        => Path.Combine(MemberFolder(orgKey, member), "history.json");

    private async Task<IReadOnlyList<SourceFileInfo>> ReadFolderAsync(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<SourceFileInfo>();
        }

        var files = new List<SourceFileInfo>();
        foreach (var path in Directory.GetFiles(folder)
                     .OrderBy(p => SourceEditorService.FileOrder(Path.GetFileName(p)))
                     .ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            var text = await File.ReadAllTextAsync(path, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            files.Add(new SourceFileInfo(name, SourceEditorService.LanguageForFileName(name), text));
        }

        return files;
    }

    private async Task WriteFolderAsync(string folder, IReadOnlyList<SourceFileInfo> files, CancellationToken cancellationToken)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            var path = Path.Combine(folder, SafeName(file.FileName));
            await File.WriteAllTextAsync(path, file.Text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string SafeName(string name)
    {
        var safe = name;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(safe) ? "_" : safe;
    }
}
