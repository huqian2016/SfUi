using System.Text;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// ソース エディタのデータ サービス。
/// 読み取りは Tooling REST、書き込み（反映・削除）は sf CLI（deploy / delete source / retrieve）で行う。
/// パース部は純関数として切り出し、ユニットテスト対象にする。
/// </summary>
public sealed class SourceEditorService : ISourceEditorService
{
    private readonly SalesforceRestClient _rest;
    private readonly SfCliRunner _sfCli;
    private readonly SourceEditorWorkspace _workspace;
    private readonly AppLog _log;

    public SourceEditorService(SalesforceRestClient rest, SfCliRunner sfCli, AppPaths paths, AppLog log)
    {
        _rest = rest;
        _sfCli = sfCli;
        _workspace = new SourceEditorWorkspace(paths, log);
        _log = log;
    }

    // ---- 一覧 ----

    /// <summary>種別ごとの一覧 SOQL（Tooling API）。</summary>
    public static string BuildListSoql(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass =>
            "SELECT Id, Name, ApiVersion, LastModifiedDate, LengthWithoutComments FROM ApexClass WHERE NamespacePrefix = null ORDER BY Name",
        SourceMemberKind.ApexTrigger =>
            "SELECT Id, Name, ApiVersion, LastModifiedDate FROM ApexTrigger ORDER BY Name",
        SourceMemberKind.VisualforcePage =>
            "SELECT Id, Name, LastModifiedDate FROM ApexPage WHERE NamespacePrefix = null ORDER BY Name",
        SourceMemberKind.LightningComponentBundle =>
            "SELECT Id, DeveloperName FROM LightningComponentBundle ORDER BY DeveloperName",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Tooling クエリ結果（records）からメンバー一覧を組み立てる。</summary>
    public static IReadOnlyList<SourceMemberInfo> ParseMembers(JsonDocument document, SourceMemberKind kind)
    {
        var list = new List<SourceMemberInfo>();
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var record in records.EnumerateArray())
        {
            var name = kind == SourceMemberKind.LightningComponentBundle
                ? GetString(record, "DeveloperName")
                : GetString(record, "Name");
            var id = GetString(record, "Id");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            DateTime? modified = null;
            var raw = GetString(record, "LastModifiedDate");
            if (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                modified = parsed;
            }

            int? size = null;
            if (record.TryGetProperty("LengthWithoutComments", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
            {
                size = sizeElement.GetInt32();
            }

            list.Add(new SourceMemberInfo(kind, name, id, modified, size));
        }

        return list;
    }

    public async Task<IReadOnlyList<SourceMemberInfo>> ListAsync(string targetOrg, SourceMemberKind kind, CancellationToken cancellationToken = default)
    {
        var document = await _rest.QueryAsync(targetOrg, BuildListSoql(kind), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var members = ParseMembers(document, kind);
        _log.Info($"ソース エディタ: {kind} を {members.Count} 件取得しました（{targetOrg}）。");
        return members;
    }

    // ---- 本文 ----

    /// <summary>LWC バンドルのリソース（ファイル）一覧 SOQL。</summary>
    public static string BuildLwcResourceSoql(string developerName)
        => "SELECT Id, FilePath, Format, Source FROM LightningComponentResource WHERE LightningComponentBundle.DeveloperName = '"
           + EscapeSoql(developerName) + "'";

    /// <summary>SOQL 文字列リテラル エスケープ（' → \'、\ → \\）。</summary>
    public static string EscapeSoql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>Apex/VF の GET 応答から本文を取り出す（ApexClass / ApexTrigger = Body、ApexPage = Markup）。</summary>
    public static string? ParseSourceText(JsonDocument document, SourceMemberKind kind)
    {
        var field = kind == SourceMemberKind.VisualforcePage ? "Markup" : "Body";
        return GetString(document.RootElement, field);
    }

    /// <summary>LWC リソース クエリ結果からファイル一覧（表示順 = js → html → css → meta）を組み立てる。</summary>
    public static IReadOnlyList<SourceFileInfo> ParseLwcResources(JsonDocument document)
    {
        var list = new List<(int Order, SourceFileInfo File)>();
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SourceFileInfo>();
        }

        foreach (var record in records.EnumerateArray())
        {
            var filePath = GetString(record, "FilePath");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var fileName = filePath.Replace('\\', '/').Split('/')[^1];
            var text = GetString(record, "Source") ?? string.Empty;
            list.Add((FileOrder(fileName), new SourceFileInfo(fileName, LanguageForFileName(fileName), text)));
        }

        return list.OrderBy(entry => entry.Order).ThenBy(entry => entry.File.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.File).ToList();
    }

    /// <summary>LWC ファイルの表示順（js → html → css → その他 → meta.xml）。</summary>
    public static int FileOrder(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".js-meta.xml", StringComparison.Ordinal)) return 9;
        if (lower.EndsWith(".js", StringComparison.Ordinal)) return 0;
        if (lower.EndsWith(".html", StringComparison.Ordinal)) return 1;
        if (lower.EndsWith(".css", StringComparison.Ordinal)) return 2;
        if (lower.EndsWith(".svg", StringComparison.Ordinal)) return 3;
        return 5;
    }

    /// <summary>ファイル名からシンタックス ハイライト定義名を決める（表示のみ。空 = ハイライトなし）。</summary>
    public static string LanguageForFileName(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".cls", StringComparison.Ordinal) || lower.EndsWith(".trigger", StringComparison.Ordinal)) return "C#";
        if (lower.EndsWith(".js", StringComparison.Ordinal)) return "JavaScript";
        if (lower.EndsWith(".html", StringComparison.Ordinal)) return "HTML";
        if (lower.EndsWith(".css", StringComparison.Ordinal)) return "CSS";
        if (lower.EndsWith(".xml", StringComparison.Ordinal) || lower.EndsWith(".page", StringComparison.Ordinal)) return "XML";
        return string.Empty;
    }

    /// <summary>Apex/VF メンバーのファイル名（表示用）。</summary>
    public static string FileNameForMember(SourceMemberInfo member) => member.Kind switch
    {
        SourceMemberKind.ApexClass => member.Name + ".cls",
        SourceMemberKind.ApexTrigger => member.Name + ".trigger",
        SourceMemberKind.VisualforcePage => member.Name + ".page",
        _ => member.Name,
    };

    /// <summary>Tooling の種別名（REST パスに使用）。</summary>
    public static string ToolingTypeName(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass => "ApexClass",
        SourceMemberKind.ApexTrigger => "ApexTrigger",
        SourceMemberKind.VisualforcePage => "ApexPage",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public async Task<IReadOnlyList<SourceFileInfo>> GetSourceAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        if (member.Kind == SourceMemberKind.LightningComponentBundle)
        {
            var lwcDocument = await _rest
                .QueryAsync(targetOrg, BuildLwcResourceSoql(member.Name), useToolingApi: true, cancellationToken)
                .ConfigureAwait(false);
            var files = ParseLwcResources(lwcDocument);
            _log.Info($"ソース エディタ: LWC {member.Name} の {files.Count} ファイルを取得しました。");
            return files;
        }

        var apiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var body = await _rest
            .SendRawAsync(targetOrg, HttpMethod.Get, $"/services/data/v{apiVersion}/tooling/sobjects/{ToolingTypeName(member.Kind)}/{member.Id}", null, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var text = ParseSourceText(document, member.Kind)
            ?? throw new SalesforceApiException(UiText.T("SourceEditor_NoBodyFmt", member.Name));
        var fileName = FileNameForMember(member);
        _log.Info($"ソース エディタ: {member.Name} を取得しました（{text.Length} 文字）。");
        return new[] { new SourceFileInfo(fileName, LanguageForFileName(fileName), text) };
    }

    // ---- 反映 / 削除（sf CLI） ----

    /// <summary>種別ごとの force-app フォルダー名。</summary>
    public static string AreaFor(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass => "classes",
        SourceMemberKind.ApexTrigger => "triggers",
        SourceMemberKind.VisualforcePage => "pages",
        SourceMemberKind.LightningComponentBundle => "lwc",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>種別ごとのファイル拡張子（LWC はバンドルなので空）。</summary>
    public static string ExtensionFor(SourceMemberKind kind) => kind switch
    {
        SourceMemberKind.ApexClass => ".cls",
        SourceMemberKind.ApexTrigger => ".trigger",
        SourceMemberKind.VisualforcePage => ".page",
        _ => string.Empty,
    };

    /// <summary>deploy 結果 JSON を解析する（status 0 = 成功、componentFailures に行・列付きエラー）。</summary>
    public static SourceDeployResult ParseDeployResult(string stdout, bool dryRun)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Number
                ? statusElement.GetInt32()
                : (int?)null;
            if (status is null)
            {
                var plainMessage = GetString(root, "message");
                return SourceDeployResult.Fail(plainMessage ?? "deploy failed");
            }

            if (status.Value == 0)
            {
                return SourceDeployResult.Ok(dryRun, "Succeeded");
            }

            var errors = new List<SourceDeployError>();
            if (root.TryGetProperty("result", out var result)
                && result.TryGetProperty("details", out var details)
                && details.TryGetProperty("componentFailures", out var failures)
                && failures.ValueKind == JsonValueKind.Array)
            {
                foreach (var failure in failures.EnumerateArray())
                {
                    errors.Add(new SourceDeployError(
                        GetString(failure, "fileName") ?? string.Empty,
                        GetInt(failure, "lineNumber"),
                        GetInt(failure, "columnNumber"),
                        GetString(failure, "problem") ?? string.Empty));
                }
            }

            var message = GetString(root, "message")
                ?? (errors.Count > 0 ? errors.Count + " error(s)" : "deploy failed");
            return SourceDeployResult.Fail(message, errors);
        }
        catch (JsonException)
        {
            return SourceDeployResult.Fail("deploy の結果を解析できませんでした");
        }
    }

    /// <summary>sf コマンドの JSON 応答の status を判定する（0 = 成功）。</summary>
    public static (bool Success, string Message) ParseSfStatus(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number)
            {
                return status.GetInt32() == 0
                    ? (true, "Succeeded")
                    : (false, GetString(root, "message") ?? "failed");
            }

            return (false, GetString(root, "message") ?? "failed");
        }
        catch (JsonException)
        {
            return (false, "sf の結果を解析できませんでした");
        }
    }

    /// <summary>
    /// 失敗したデプロイ結果について、組織の実際の内容で成否を再判定すべきか。
    /// コンパイル エラー（componentFailures）が無い CLI 側の失敗のみを対象にする。
    /// sf CLI はまれに、反映が完了した後にメッセージ解決で失敗し
    /// （例: Missing message metadata.transfer:Finalizing）、偽の失敗を返すため。
    /// </summary>
    public static bool ShouldVerifyAgainstOrg(SourceDeployResult parsed, bool dryRun)
        => !parsed.Success && !dryRun && parsed.Errors.Count == 0;

    /// <summary>意図したファイルと組織のファイルが一致するか（ファイル名は大文字小文字を無視、改行コードと末尾改行の差は無視）。</summary>
    public static bool ContentMatches(IReadOnlyList<SourceFileInfo> intended, IReadOnlyList<SourceFileInfo> actual)
    {
        if (intended.Count == 0 || intended.Count != actual.Count)
        {
            return false;
        }

        var byName = new Dictionary<string, SourceFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in actual)
        {
            byName[file.FileName] = file;
        }

        foreach (var file in intended)
        {
            if (!byName.TryGetValue(file.FileName, out var orgFile)
                || NormalizeNewlines(file.Text).TrimEnd('\n') != NormalizeNewlines(orgFile.Text).TrimEnd('\n'))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>失敗時: 組織の実際の内容が意図と一致していれば成功とみなす（一致しなければ null）。</summary>
    private async Task<SourceDeployResult?> VerifyDeployAgainstOrgAsync(
        string targetOrg,
        SourceMemberInfo member,
        IReadOnlyList<SourceFileInfo> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var target = member;
            if (string.IsNullOrWhiteSpace(member.Id) && member.Kind != SourceMemberKind.LightningComponentBundle)
            {
                // 新規メンバーは Id が無いため、一覧から Id を解決してから本文を取得する
                var listed = (await ListAsync(targetOrg, member.Kind, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(m => string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                if (listed is null)
                {
                    return null;
                }

                target = listed;
            }

            var orgFiles = await GetSourceAsync(targetOrg, target, cancellationToken).ConfigureAwait(false);
            if (ContentMatches(files, orgFiles))
            {
                return SourceDeployResult.Ok(false, "Succeeded (verified in org)");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn($"ソース エディタ: {member.Name} の反映結果を確認できませんでした: {ex.Message}");
        }

        return null;
    }

    public async Task<SourceDeployResult> DeployAsync(
        string targetOrg,
        SourceMemberInfo member,
        IReadOnlyList<SourceFileInfo> files,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _workspace.EnsureWorkProjectAsync(cancellationToken).ConfigureAwait(false);
            var area = AreaFor(member.Kind);
            var isBundle = member.Kind == SourceMemberKind.LightningComponentBundle;
            var areaDir = Path.Combine(_workspace.WorkProjectRoot, "force-app", "main", "default", area);
            var memberDir = isBundle ? Path.Combine(areaDir, member.Name) : areaDir;

            if (isBundle)
            {
                if (Directory.Exists(memberDir))
                {
                    Directory.Delete(memberDir, recursive: true);
                }

                Directory.CreateDirectory(memberDir);
            }
            else
            {
                Directory.CreateDirectory(areaDir);

                // メタデータ（-meta.xml）が無ければ補う:
                // 既存メンバーは retrieve で取得（apiVersion / status / label 等の属性を保つ）、
                // 新規メンバー（未作成）は既定のメタデータを生成する。
                var metaPath = Path.Combine(areaDir, member.Name + ExtensionFor(member.Kind) + "-meta.xml");
                if (!File.Exists(metaPath) && !string.IsNullOrWhiteSpace(member.Id))
                {
                    var retrieve = await RetrieveMemberAsync(targetOrg, member, cancellationToken).ConfigureAwait(false);
                    if (!retrieve.Success && !File.Exists(metaPath))
                    {
                        // retrieve が失敗を報告してもファイルが取得できている場合は続行する
                        // （sf CLI が完了後にメッセージ解決で失敗する既知の問題に備える）。
                        return retrieve;
                    }
                }

                if (!File.Exists(metaPath))
                {
                    var apiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllTextAsync(
                        metaPath,
                        BuildDefaultMeta(member.Kind, member.Name, apiVersion),
                        new UTF8Encoding(false),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                await File.WriteAllTextAsync(
                    Path.Combine(memberDir, fileName),
                    file.Text,
                    new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);
            }

            if (isBundle)
            {
                // 新規 LWC バンドル: メタデータ（.js-meta.xml）が無ければ生成する（既存編集時は working に含まれる）。
                var bundleMetaPath = Path.Combine(memberDir, member.Name + ".js-meta.xml");
                if (!File.Exists(bundleMetaPath))
                {
                    var bundleApiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllTextAsync(
                        bundleMetaPath,
                        BuildDefaultMeta(SourceMemberKind.LightningComponentBundle, member.Name, bundleApiVersion),
                        new UTF8Encoding(false),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var sourceDir = isBundle
                ? memberDir
                : Path.Combine(memberDir, member.Name + ExtensionFor(member.Kind));
            var arguments = new List<string>
            {
                "project", "deploy", "start",
                "--source-dir", sourceDir,
                "--ignore-conflicts",
                "--target-org", targetOrg,
            };
            if (dryRun)
            {
                arguments.Add("--dry-run");
            }

            arguments.Add("--json");
            var result = await _sfCli.RunAsync(arguments, _workspace.WorkProjectRoot, TimeSpan.FromMinutes(5), cancellationToken)
                .ConfigureAwait(false);
            var parsed = ParseDeployResult(result.StdOut, dryRun);
            if (ShouldVerifyAgainstOrg(parsed, dryRun))
            {
                // sf CLI が偽の失敗を返しても組織には反映済みのことがある。組織の実際の内容で成否を再判定する。
                var verified = await VerifyDeployAgainstOrgAsync(targetOrg, member, files, cancellationToken).ConfigureAwait(false);
                if (verified is not null)
                {
                    _log.Info($"ソース エディタ: {member.Name} の反映 = 成功（CLI は失敗を報告しましたが、組織への反映を確認しました）");
                    return verified;
                }
            }

            _log.Info($"ソース エディタ: {member.Name} の{(dryRun ? "検証" : "反映")} = {(parsed.Success ? "成功" : "失敗")}"
                      + (parsed.Errors.Count > 0 ? $"（{parsed.Errors.Count} エラー）" : ""));
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"ソース エディタ: {member.Name} の反映に失敗しました", ex);
            return SourceDeployResult.Fail(ex.Message);
        }
    }

    /// <summary>メタデータ属性を保つため、対象メンバーを一時プロジェクトへ retrieve する。</summary>
    public async Task<SourceDeployResult> RetrieveMemberAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        await _workspace.EnsureWorkProjectAsync(cancellationToken).ConfigureAwait(false);
        var arguments = new[]
        {
            "project", "retrieve", "start",
            "--metadata", ToolingTypeName(member.Kind) + ":" + member.Name,
            "--ignore-conflicts",
            "--target-org", targetOrg,
            "--json",
        };
        var result = await _sfCli.RunAsync(arguments, _workspace.WorkProjectRoot, TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);
        var (success, message) = ParseSfStatus(result.StdOut);
        return success ? SourceDeployResult.Ok(false, message) : SourceDeployResult.Fail(message);
    }

    public async Task<SourceDeployResult> DeleteAsync(string targetOrg, SourceMemberInfo member, CancellationToken cancellationToken = default)
    {
        try
        {
            if (member.Kind == SourceMemberKind.LightningComponentBundle)
            {
                await _workspace.EnsureWorkProjectAsync(cancellationToken).ConfigureAwait(false);
                var arguments = new[]
                {
                    "project", "delete", "source",
                    "--no-prompt",
                    "--metadata", "LightningComponentBundle:" + member.Name,
                    "--target-org", targetOrg,
                    "--json",
                };
                var result = await _sfCli.RunAsync(arguments, _workspace.WorkProjectRoot, TimeSpan.FromMinutes(5), cancellationToken)
                    .ConfigureAwait(false);
                var (success, message) = ParseSfStatus(result.StdOut);
                if (!success)
                {
                    // 反映と同じく CLI 側の偽の失敗に備え、実際に削除されたかを組織で確認する。
                    try
                    {
                        var bundles = await ListAsync(targetOrg, SourceMemberKind.LightningComponentBundle, cancellationToken).ConfigureAwait(false);
                        if (!bundles.Any(b => string.Equals(b.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _log.Info($"ソース エディタ: LWC {member.Name} の削除 = 成功（CLI は失敗を報告しましたが、組織で削除を確認しました）");
                            return SourceDeployResult.Ok(false, "Deleted");
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"ソース エディタ: LWC {member.Name} の削除結果を確認できませんでした: {ex.Message}");
                    }
                }

                _log.Info($"ソース エディタ: LWC {member.Name} の削除 = {(success ? "成功" : "失敗")}");
                return success ? SourceDeployResult.Ok(false, message) : SourceDeployResult.Fail(message);
            }

            var apiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
            await _rest.SendRawAsync(
                targetOrg,
                HttpMethod.Delete,
                $"/services/data/v{apiVersion}/tooling/sobjects/{ToolingTypeName(member.Kind)}/{member.Id}",
                null,
                cancellationToken).ConfigureAwait(false);
            _log.Info($"ソース エディタ: {member.Name} を組織から削除しました。");
            return SourceDeployResult.Ok(false, "Deleted");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"ソース エディタ: {member.Name} の削除に失敗しました", ex);
            return SourceDeployResult.Fail(ex.Message);
        }
    }

    // ---- ローカル ドラフト ----

    public Task<SourceDraft?> LoadDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
        => _workspace.LoadDraftAsync(orgKey, member, cancellationToken);

    public Task SaveDraftAsync(string orgKey, SourceMemberInfo member, IReadOnlyList<SourceFileInfo> baseline, IReadOnlyList<SourceFileInfo> working, CancellationToken cancellationToken = default)
        => _workspace.SaveDraftAsync(orgKey, member, baseline, working, cancellationToken);

    public Task ClearDraftAsync(string orgKey, SourceMemberInfo member, CancellationToken cancellationToken = default)
        => _workspace.ClearDraftAsync(orgKey, member, cancellationToken);

    /// <summary>新規メンバー用の既定メタデータ（-meta.xml）を生成する。</summary>
    public static string BuildDefaultMeta(SourceMemberKind kind, string name, string apiVersion)
    {
        var ns = "http://soap.sforce.com/2006/04/metadata";
        return kind switch
        {
            SourceMemberKind.ApexClass =>
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ApexClass xmlns=\"{ns}\"><apiVersion>{apiVersion}</apiVersion><status>Active</status></ApexClass>",
            SourceMemberKind.ApexTrigger =>
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ApexTrigger xmlns=\"{ns}\"><apiVersion>{apiVersion}</apiVersion><status>Active</status></ApexTrigger>",
            SourceMemberKind.VisualforcePage =>
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ApexPage xmlns=\"{ns}\"><apiVersion>{apiVersion}</apiVersion><label>{name}</label></ApexPage>",
            SourceMemberKind.LightningComponentBundle =>
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><LightningComponentBundle xmlns=\"{ns}\"><apiVersion>{apiVersion}</apiVersion><isExposed>false</isExposed></LightningComponentBundle>",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return 0;
    }
}
