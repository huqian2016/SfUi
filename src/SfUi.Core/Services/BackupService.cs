using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// レコードのバックアップと復元。
/// バックアップ = オブジェクト単位で全項目を取得（REST = JSON / 大量は Bulk API = CSV）し data\backups に保存。
/// 復元 = 同一組織は Id 照合（存在 → スキップ / 上書き、削除済み → SOAP undelete で Id 維持、無し → 新規挿入）、
///        別組織はキー項目（既定 Name）で照合。新規挿入では同一バックアップ内の参照（旧 Id → 新 Id）を自動で張り替える。
/// </summary>
public sealed class BackupService
{
    /// <summary>レコード詳細ウィンドウの読み込み上限。</summary>
    public const int MaxDetailRecords = 20_000;

    private const int MaxResultRows = 1_000;
    private const int BatchSize = 200;
    private const int KeyChunkSize = 100;

    private readonly AppPaths _paths;
    private readonly SalesforceRestClient _rest;
    private readonly SObjectDescribeService _describes;
    private readonly SfCliRunner _runner;
    private readonly OrgService _orgService;
    private readonly SalesforceSoapClient _soap;
    private readonly AppSettingsStore _settings;
    private readonly BackupStateStore _state;
    private readonly AppLog _log;

    public BackupService(
        AppPaths paths,
        SalesforceRestClient rest,
        SObjectDescribeService describes,
        SfCliRunner runner,
        OrgService orgService,
        SalesforceSoapClient soap,
        AppSettingsStore settings,
        BackupStateStore state,
        AppLog log)
    {
        _paths = paths;
        _rest = rest;
        _describes = describes;
        _runner = runner;
        _orgService = orgService;
        _soap = soap;
        _settings = settings;
        _state = state;
        _log = log;
    }

    /// <summary>バックアップの保存ルート（data\backups）。</summary>
    public string BackupsRoot => Path.Combine(_paths.DataRoot, "backups");

    // ================================================================
    // 件数（バックグラウンド取得 + キャッシュ）
    // ================================================================

    /// <summary>件数キャッシュを取得する。</summary>
    public IReadOnlyDictionary<string, int> GetCachedCounts(string orgKey) => _state.GetCachedCounts(orgKey);

    /// <summary>件数を並列（最大 4）で取得し、逐次キャッシュへ保存する。progress = (done, total)。</summary>
    public async Task FetchCountsAsync(
        string targetOrg,
        string orgKey,
        IReadOnlyList<string> objectNames,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        var total = objectNames.Count;
        var done = 0;
        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var flushLock = new object();
        using var semaphore = new SemaphoreSlim(4);

        var tasks = objectNames.Select(async name =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = await _rest.QueryAsync(targetOrg, $"SELECT COUNT() FROM {name}", false, cancellationToken).ConfigureAwait(false);
                var count = 0;
                try
                {
                    if (document.RootElement.TryGetProperty("totalSize", out var size) && size.ValueKind == JsonValueKind.Number)
                    {
                        count = size.GetInt32();
                    }
                }
                finally
                {
                    document.Dispose();
                }

                lock (flushLock)
                {
                    pending[name] = count;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"件数取得に失敗: {name} ({ex.Message})");
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref done);
                progress?.Invoke(current, total);

                // 100 件ごとに逐次保存（中断時も進捗が残る）
                if (current % 100 == 0)
                {
                    lock (flushLock)
                    {
                        if (pending.Count > 0)
                        {
                            _state.UpdateCounts(orgKey, new Dictionary<string, int>(pending, StringComparer.OrdinalIgnoreCase), fetched: false);
                            pending.Clear();
                        }
                    }
                }
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        lock (flushLock)
        {
            _state.UpdateCounts(orgKey, pending, fetched: pending.Count > 0);
        }
    }

    /// <summary>件数キャッシュを破棄する（再取得用）。</summary>
    public void ClearCounts(string orgKey) => _state.ClearCounts(orgKey);

    // ================================================================
    // バックアップ
    // ================================================================

    /// <summary>選択オブジェクトをバックアップする（オブジェクト単位に失敗を記録して継続）。</summary>
    public async Task<BackupMetadata> RunBackupAsync(
        string targetOrg,
        string orgDisplay,
        string orgUsername,
        string? orgId,
        string appVersion,
        string label,
        string description,
        IReadOnlyList<string> objectNames,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BackupsRoot);
        var id = MakeBackupId();
        var directory = Path.Combine(BackupsRoot, id);
        Directory.CreateDirectory(directory);

        var restMax = _settings.Current.BackupRestMaxRecords;
        var cachedCounts = _state.GetCachedCounts(orgUsername);
        var entries = new List<BackupObjectInfo>();
        var index = 0;

        foreach (var name in objectNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackupObjectInfo entry;
            try
            {
                progress?.Report(new BackupProgress(index, objectNames.Count, name, "describe", 0));
                var describe = await _describes.DescribeAsync(targetOrg, name, cancellationToken: cancellationToken).ConfigureAwait(false);
                var soql = BuildAllFieldsSoql(describe);
                var count = cachedCounts.TryGetValue(name, out var cached)
                    ? cached
                    : await CountAsync(targetOrg, name, cancellationToken).ConfigureAwait(false);

                if (count <= restMax)
                {
                    progress?.Report(new BackupProgress(index, objectNames.Count, name, "rest", 0));
                    var paged = await RestQueryPager
                        .QueryAllAsync(_rest, targetOrg, soql, count + 1_000, cancellationToken)
                        .ConfigureAwait(false);
                    var file = name + ".json";
                    await File.WriteAllTextAsync(
                        Path.Combine(directory, file),
                        JsonSerializer.Serialize(paged.Records),
                        cancellationToken).ConfigureAwait(false);
                    entry = new BackupObjectInfo(name, describe.Label, paged.Records.Count, BackupEngine.Rest, file);
                }
                else
                {
                    progress?.Report(new BackupProgress(index, objectNames.Count, name, "bulk", 0));
                    var file = name + ".csv";
                    var path = Path.Combine(directory, file);
                    var processed = await RunBulkExportAsync(targetOrg, soql, path, cancellationToken).ConfigureAwait(false);
                    entry = new BackupObjectInfo(name, describe.Label, processed, BackupEngine.Bulk, file);
                }

                _log.Info($"バックアップ: {name} {entry.Count} 件 ({entry.Engine}) → {entry.File}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"バックアップ ({name}) に失敗しました", ex);
                entry = new BackupObjectInfo(name, name, 0, BackupEngine.Rest, string.Empty, ex.Message);
            }

            entries.Add(entry);
            index++;
            progress?.Report(new BackupProgress(index, objectNames.Count, name, "done", entry.Count));
        }

        var metadata = new BackupMetadata(
            id, label, description, DateTimeOffset.Now, orgId, orgUsername, orgDisplay, appVersion, entries);
        var metadataPath = Path.Combine(directory, "metadata.json");
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, MetadataJsonOptions),
            cancellationToken).ConfigureAwait(false);
        _log.Info($"バックアップ完了: {id} ({entries.Sum(e => e.Count)} 件 / {entries.Count} オブジェクト)");
        return metadata;
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    /// <summary>バックアップ一覧（新しい順）。</summary>
    public IReadOnlyList<BackupMetadata> ListBackups()
    {
        var list = new List<BackupMetadata>();
        if (!Directory.Exists(BackupsRoot))
        {
            return list;
        }

        foreach (var directory in Directory.EnumerateDirectories(BackupsRoot))
        {
            var metadataPath = Path.Combine(directory, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metadataPath));
                if (metadata is not null)
                {
                    list.Add(metadata);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"バックアップのメタデータを読み込めません: {metadataPath} ({ex.Message})");
            }
        }

        return list.OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <summary>バックアップのメタデータを取得する（無ければ null）。</summary>
    public BackupMetadata? LoadMetadata(string backupId) =>
        ValidateBackupId(backupId) ? ListBackups().FirstOrDefault(m => string.Equals(m.Id, backupId, StringComparison.Ordinal)) : null;

    /// <summary>バックアップを削除する。</summary>
    public void DeleteBackup(string backupId)
    {
        if (!ValidateBackupId(backupId))
        {
            throw new ArgumentException("不正なバックアップ ID です", nameof(backupId));
        }

        var directory = Path.Combine(BackupsRoot, backupId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
            _log.Info($"バックアップを削除: {backupId}");
        }
    }

    // ================================================================
    // レコード詳細（別ウィンドウ用）
    // ================================================================

    /// <summary>バックアップ内のレコードを表示用（文字列）で読み込む（上限 20,000 件）。</summary>
    public async Task<BackupRecords> LoadRecordsAsync(string backupId, BackupObjectInfo info, CancellationToken cancellationToken = default)
    {
        if (!ValidateBackupId(backupId) || string.IsNullOrEmpty(info.File))
        {
            return BackupRecords.Empty;
        }

        var path = Path.Combine(BackupsRoot, backupId, info.File);
        if (!File.Exists(path))
        {
            return BackupRecords.Empty;
        }

        if (info.Engine == BackupEngine.Rest)
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var records = ParseJsonRecords(json);
            var truncated = records.Count > MaxDetailRecords;
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                foreach (var key in record.Keys)
                {
                    if (seen.Add(key))
                    {
                        columns.Add(key);
                    }
                }
            }

            var rows = records
                .Take(MaxDetailRecords)
                .Select(record => (IReadOnlyDictionary<string, string?>)columns.ToDictionary(
                    column => column,
                    column => record.TryGetValue(column, out var value) ? ValueDisplay(value) : null,
                    StringComparer.Ordinal))
                .ToList();
            return new BackupRecords(columns, rows, truncated);
        }

        var (text, _) = CsvParser.ReadFile(path);
        var table = CsvParser.Parse(text);
        var csvTruncated = table.RowCount > MaxDetailRecords;
        var csvRows = table.Rows
            .Take(MaxDetailRecords)
            .Select(row => (IReadOnlyDictionary<string, string?>)table.Headers
                .Select((header, i) => new { header, value = i < row.Count ? row[i] : null })
                .ToDictionary(x => x.header, x => x.value, StringComparer.Ordinal))
            .ToList();
        return new BackupRecords(table.Headers, csvRows, csvTruncated);
    }

    // ================================================================
    // 復元
    // ================================================================

    /// <summary>バックアップから選択オブジェクトを復元する。</summary>
    public async Task<RestoreSummary> RunRestoreAsync(
        string targetOrg,
        string? targetOrgId,
        BackupMetadata backup,
        IReadOnlyList<BackupObjectInfo> objects,
        RestoreOptions options,
        IProgress<RestoreProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var mode = options.MatchMode == RestoreMatchMode.Auto
            ? (!string.IsNullOrEmpty(targetOrgId) &&
               !string.IsNullOrEmpty(backup.OrgId) &&
               string.Equals(targetOrgId, backup.OrgId, StringComparison.OrdinalIgnoreCase)
                ? RestoreMatchMode.Id
                : RestoreMatchMode.Key)
            : options.MatchMode;
        _log.Info($"復元開始: {backup.Id} → {targetOrg}（照合 = {mode} / 既存一致時 = {options.ExistingAction}）");

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var results = new List<RestoreObjectResult>();
        var apiVersion = await _rest.GetApiVersionAsync(targetOrg, cancellationToken).ConfigureAwait(false);
        var index = 0;

        foreach (var info in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreObjectResult result;
            try
            {
                options.KeyFields.TryGetValue(info.Name, out var keyField);
                result = await RestoreObjectAsync(
                    targetOrg, apiVersion, backup, info, mode, options.ExistingAction, keyField,
                    idMap, index, objects.Count, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"復元 ({info.Name}) に失敗しました", ex);
                result = new RestoreObjectResult(info.Name, info.Label, 0, 0, 0, 0, info.Count, Array.Empty<RestoreRowResult>())
                {
                    HasError = true,
                    ErrorMessage = ex.Message,
                };
            }

            results.Add(result);
            index++;
            progress?.Report(new RestoreProgress(index, objects.Count, info.Name, "done", result.Total, result.Total));
        }

        var summary = new RestoreSummary(backup.Id, backup.Label, DateTimeOffset.Now, stopwatch.Elapsed, results);
        _log.Info(
            $"復元完了: {backup.Id} → {targetOrg}（作成 {summary.Created} / 上書き {summary.Updated} / 復元(削除取消) {summary.Undeleted} / スキップ {summary.Skipped} / 失敗 {summary.Failed}）");
        return summary;
    }

    private async Task<RestoreObjectResult> RestoreObjectAsync(
        string targetOrg,
        string apiVersion,
        BackupMetadata backup,
        BackupObjectInfo info,
        RestoreMatchMode mode,
        RestoreExistingAction existingAction,
        string? keyField,
        Dictionary<string, string> idMap,
        int objectIndex,
        int objectTotal,
        IProgress<RestoreProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "read", 0, info.Count));
        var describe = await _describes.DescribeAsync(targetOrg, info.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        var fieldsByName = describe.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var loaded = await LoadRawRecordsAsync(backup.Id, info, fieldsByName, cancellationToken).ConfigureAwait(false);

        var created = 0;
        var updated = 0;
        var undeleted = 0;
        var skipped = 0;
        var failed = 0;
        var rows = new List<RestoreRowResult>();
        void AddRow(int rowIndex, RestoreRecordAction action, bool success, string? id, string? error)
        {
            if (rows.Count < MaxResultRows)
            {
                rows.Add(new RestoreRowResult(rowIndex, info.Name, action, success, id, error));
            }
        }

        // 読み込み時に変換エラーだったレコード
        var actions = new RestoreRecordAction[loaded.Count];
        var loadErrors = new string?[loaded.Count];
        var pending = new List<int>();
        for (var i = 0; i < loaded.Count; i++)
        {
            loadErrors[i] = loaded[i].Error;
            if (loaded[i].Error is not null)
            {
                actions[i] = RestoreRecordAction.Skip;
                failed++;
                AddRow(i, RestoreRecordAction.Insert, false, null, loaded[i].Error);
            }
            else
            {
                pending.Add(i);
            }
        }

        // ---------- 照合 ----------
        progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "match", 0, loaded.Count));
        if (mode == RestoreMatchMode.Id)
        {
            var ids = pending
                .Select(i => (Index: i, Id: GetString(loaded[i].Fields, "Id")))
                .Where(x => !string.IsNullOrEmpty(x.Id))
                .Select(x => (x.Index, Id: x.Id!))
                .ToList();
            var (existing, deleted) = await GetIdStatesAsync(targetOrg, info.Name, ids.Select(x => x.Id).ToList(), cancellationToken).ConfigureAwait(false);
            var undeleteOk = await UndeleteAsync(targetOrg, deleted.ToList(), cancellationToken).ConfigureAwait(false);

            foreach (var (i, id) in ids)
            {
                if (existing.Contains(id))
                {
                    actions[i] = existingAction == RestoreExistingAction.Skip ? RestoreRecordAction.Skip : RestoreRecordAction.Overwrite;
                }
                else if (deleted.Contains(id) && undeleteOk.Contains(id))
                {
                    actions[i] = RestoreRecordAction.Undelete;
                }
                else
                {
                    actions[i] = RestoreRecordAction.Insert;
                }
            }

            // Id が空のレコードは挿入扱い
            foreach (var i in pending.Where(i => string.IsNullOrEmpty(GetString(loaded[i].Fields, "Id"))))
            {
                actions[i] = RestoreRecordAction.Insert;
            }
        }
        else if (!string.IsNullOrWhiteSpace(keyField) && fieldsByName.ContainsKey(keyField))
        {
            var keys = pending
                .Select(i => (Index: i, Key: GetString(loaded[i].Fields, keyField)))
                .Where(x => !string.IsNullOrEmpty(x.Key))
                .ToList();
            var matches = await FindByKeyAsync(targetOrg, info.Name, keyField, keys.Select(x => x.Key!).Distinct(StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false);
            foreach (var i in pending)
            {
                var key = GetString(loaded[i].Fields, keyField);
                actions[i] = !string.IsNullOrEmpty(key) && matches.ContainsKey(key)
                    ? (existingAction == RestoreExistingAction.Skip ? RestoreRecordAction.Skip : RestoreRecordAction.Overwrite)
                    : RestoreRecordAction.Insert;
            }
        }
        else
        {
            foreach (var i in pending)
            {
                actions[i] = RestoreRecordAction.Insert;
            }
        }

        // ---------- 実行 ----------
        progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "restore", 0, loaded.Count));
        var processed = 0;

        foreach (var i in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (actions[i])
            {
                case RestoreRecordAction.Skip:
                    skipped++;
                    AddRow(i, RestoreRecordAction.Skip, true, GetString(loaded[i].Fields, "Id"), null);
                    break;

                case RestoreRecordAction.Undelete:
                    undeleted++;
                    var oldId = GetString(loaded[i].Fields, "Id");
                    if (!string.IsNullOrEmpty(oldId))
                    {
                        idMap[oldId] = oldId;
                    }

                    AddRow(i, RestoreRecordAction.Undelete, true, oldId, null);
                    break;
            }

            processed++;
            if (processed % BatchSize == 0)
            {
                progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "restore", processed, loaded.Count));
            }
        }

        // 挿入（200 件単位）
        var insertIndexes = pending.Where(i => actions[i] == RestoreRecordAction.Insert).ToList();
        for (var offset = 0; offset < insertIndexes.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIndexes = insertIndexes.Skip(offset).Take(BatchSize).ToList();
            var batch = new List<ImportPlannedRow>();
            var batchMeta = new List<(int Index, string? OldId)>();
            foreach (var i in batchIndexes)
            {
                var oldId = GetString(loaded[i].Fields, "Id");
                var fields = BuildFields(loaded[i].Fields, fieldsByName, idMap, forInsert: true);
                if (fields.Count == 0)
                {
                    failed++;
                    AddRow(i, RestoreRecordAction.Insert, false, null, UiText.T("Backup_Err_NoInsertableFields"));
                    continue;
                }

                batch.Add(new ImportPlannedRow(i, fields, null, null, null));
                batchMeta.Add((i, oldId));
            }

            if (batch.Count > 0)
            {
                var body = ImportBatchPlanner.BuildCompositeBody(batch, info.Name, includeId: false);
                var response = await _rest.SendRawAsync(
                    targetOrg, HttpMethod.Post, $"/services/data/v{apiVersion}/composite/sobjects", body, cancellationToken).ConfigureAwait(false);
                var batchResults = ImportResultMapper.FromCompositeResponse(response, batch);
                for (var r = 0; r < batchResults.Count; r++)
                {
                    var result = batchResults[r];
                    var (sourceIndex, oldId) = batchMeta[r];
                    if (result.Success)
                    {
                        created++;
                        if (!string.IsNullOrEmpty(oldId) && !string.IsNullOrEmpty(result.Id))
                        {
                            idMap[oldId] = result.Id!;
                        }

                        AddRow(sourceIndex, RestoreRecordAction.Insert, true, result.Id, null);
                    }
                    else
                    {
                        failed++;
                        AddRow(sourceIndex, RestoreRecordAction.Insert, false, null, result.Error);
                    }
                }
            }

            progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "restore", Math.Min(offset + batchIndexes.Count, loaded.Count), loaded.Count));
        }

        // 上書き（200 件単位）
        var overwriteIndexes = pending.Where(i => actions[i] == RestoreRecordAction.Overwrite).ToList();
        for (var offset = 0; offset < overwriteIndexes.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIndexes = overwriteIndexes.Skip(offset).Take(BatchSize).ToList();
            var batch = new List<ImportPlannedRow>();
            foreach (var i in batchIndexes)
            {
                var id = GetString(loaded[i].Fields, "Id");
                if (string.IsNullOrEmpty(id))
                {
                    failed++;
                    AddRow(i, RestoreRecordAction.Overwrite, false, null, UiText.T("Backup_Err_RowIdMissing"));
                    continue;
                }

                var fields = BuildFields(loaded[i].Fields, fieldsByName, idMap, forInsert: false);
                batch.Add(new ImportPlannedRow(i, fields, id, null, null));
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var body = ImportBatchPlanner.BuildCompositeBody(batch, info.Name, includeId: true);
            var response = await _rest.SendRawAsync(
                targetOrg, HttpMethod.Patch, $"/services/data/v{apiVersion}/composite/sobjects", body, cancellationToken).ConfigureAwait(false);
            var batchResults = ImportResultMapper.FromCompositeResponse(response, batch);
            for (var r = 0; r < batchResults.Count; r++)
            {
                var result = batchResults[r];
                var sourceIndex = batch[r].RowIndex;
                if (result.Success)
                {
                    updated++;
                    AddRow(sourceIndex, RestoreRecordAction.Overwrite, true, result.Id, null);
                }
                else
                {
                    failed++;
                    AddRow(sourceIndex, RestoreRecordAction.Overwrite, false, null, result.Error);
                }
            }

            progress?.Report(new RestoreProgress(objectIndex, objectTotal, info.Name, "restore", Math.Min(offset + batchIndexes.Count, loaded.Count), loaded.Count));
        }

        return new RestoreObjectResult(info.Name, info.Label, created, updated, undeleted, skipped, failed, rows);
    }

    // ================================================================
    // 外部連携ヘルパー
    // ================================================================

    private async Task<int> RunBulkExportAsync(string targetOrg, string soql, string outputPath, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "data", "export", "bulk",
            "--target-org", targetOrg,
            "--query", soql,
            "--output-file", outputPath,
            "--result-format", "csv",
            "--wait", "30",
            "--json",
        };
        var run = await _runner.RunAsync(arguments, timeout: TimeSpan.FromMinutes(31), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!run.Success)
        {
            var raw = SfCommandResult.From(run);
            throw new SalesforceApiException(raw.ErrorMessage ?? "Bulk エクスポートに失敗しました");
        }

        var (_, processed, _) = ImportResultMapper.ParseBulkJobInfo(run.StdOut);
        return processed;
    }

    private async Task<int> CountAsync(string targetOrg, string objectName, CancellationToken cancellationToken)
    {
        var document = await _rest.QueryAsync(targetOrg, $"SELECT COUNT() FROM {objectName}", false, cancellationToken).ConfigureAwait(false);
        try
        {
            return document.RootElement.TryGetProperty("totalSize", out var size) && size.ValueKind == JsonValueKind.Number
                ? size.GetInt32()
                : 0;
        }
        finally
        {
            document.Dispose();
        }
    }

    private async Task<(HashSet<string> Existing, HashSet<string> Deleted)> GetIdStatesAsync(
        string targetOrg,
        string objectName,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in RecordAccessService.ChunkIds(ids))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inList = string.Join(",", chunk.Select(id => $"'{OrgInfoQueryBuilder.EscapeSoqlString(id)}'"));
            var paged = await RestQueryPager.QueryAllAsync(
                _rest, targetOrg, $"SELECT Id, IsDeleted FROM {objectName} WHERE Id IN ({inList})", chunk.Count + 10, cancellationToken, allRows: true).ConfigureAwait(false);
            foreach (var row in paged.Records)
            {
                var id = GetStringFromJson(row, "Id");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var isDeleted = row.TryGetProperty("IsDeleted", out var deletedElement) && deletedElement.ValueKind == JsonValueKind.True;
                (isDeleted ? deleted : existing).Add(id);
            }
        }

        return (existing, deleted);
    }

    private async Task<HashSet<string>> UndeleteAsync(string targetOrg, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var ok = new HashSet<string>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return ok;
        }

        var auth = await _orgService.GetAuthAsync(targetOrg, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var chunk in RecordAccessService.ChunkIds(ids))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await _soap
                .UndeleteAsync(auth.InstanceUrl ?? string.Empty, auth.AccessToken, auth.ApiVersion ?? "59.0", chunk, cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
            {
                if (result.Success)
                {
                    ok.Add(result.Id);
                }
                else
                {
                    _log.Warn($"undelete に失敗（新規挿入へフォールバック）: {result.Id} {result.Error}");
                }
            }
        }

        return ok;
    }

    private async Task<Dictionary<string, string>> FindByKeyAsync(
        string targetOrg,
        string objectName,
        string keyField,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in RecordAccessService.ChunkIds(keys, KeyChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inList = string.Join(",", chunk.Select(key => $"'{OrgInfoQueryBuilder.EscapeSoqlString(key)}'"));
            var paged = await RestQueryPager.QueryAllAsync(
                _rest, targetOrg, $"SELECT Id, {keyField} FROM {objectName} WHERE {keyField} IN ({inList})", chunk.Count + 100, cancellationToken).ConfigureAwait(false);
            foreach (var row in paged.Records)
            {
                var key = GetStringFromJson(row, keyField);
                var id = GetStringFromJson(row, "Id");
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(id) && !matches.ContainsKey(key))
                {
                    matches[key] = id;
                }
            }
        }

        return matches;
    }

    private async Task<List<(Dictionary<string, object?> Fields, string? Error)>> LoadRawRecordsAsync(
        string backupId,
        BackupObjectInfo info,
        IReadOnlyDictionary<string, DataIoField> fieldsByName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(BackupsRoot, backupId, info.File);
        var result = new List<(Dictionary<string, object?>, string?)>();

        if (info.Engine == BackupEngine.Rest)
        {
            if (!File.Exists(path))
            {
                return result;
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            foreach (var record in ParseJsonRecords(json))
            {
                result.Add((record, null));
            }

            return result;
        }

        var (text, _) = CsvParser.ReadFile(path);
        var table = CsvParser.Parse(text);
        foreach (var row in table.Rows)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? error = null;
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var header = table.Headers[i];
                var raw = i < row.Count ? row[i] : null;
                var (value, fieldError) = CoerceCsvValue(fieldsByName.TryGetValue(header, out var field) ? field : null, raw);
                if (fieldError is not null && error is null)
                {
                    error = UiText.T("DataIo_Err_RowFmt", result.Count + 1, fieldError);
                }

                fields[header] = value;
            }

            result.Add((fields, error));
        }

        return result;
    }

    // ================================================================
    // 純関数（テスト対象）
    // ================================================================

    /// <summary>describe から全項目の SOQL を組み立てる（複合項目 address / location / base64 は除外）。</summary>
    public static string BuildAllFieldsSoql(DataIoObjectDescribe describe)
    {
        var fields = describe.Fields
            .Where(f => !string.Equals(f.Type, "address", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.Type, "location", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.Type, "base64", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!fields.Contains("Id", StringComparer.OrdinalIgnoreCase))
        {
            fields.Insert(0, "Id");
        }

        return $"SELECT {string.Join(", ", fields)} FROM {describe.Name}";
    }

    /// <summary>バックアップ JSON（レコード配列）→ 型付き辞書へ変換する（attributes は除去）。</summary>
    public static List<Dictionary<string, object?>> ParseJsonRecords(string json)
    {
        var list = new List<Dictionary<string, object?>>();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var record = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("attributes"))
                {
                    continue;
                }

                record[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => property.Value.TryGetInt64(out var number) ? (object)number : property.Value.GetDecimal(),
                    _ => property.Value.GetRawText(),
                };
            }

            list.Add(record);
        }

        return list;
    }

    /// <summary>CSV セル値を describe に基づいて変換する。</summary>
    public static (object? Value, string? Error) CoerceCsvValue(DataIoField? field, string? raw)
    {
        if (field is not null)
        {
            var (kind, value, error) = ImportValueCoercion.Convert(field, raw, emptyAsNull: true);
            return kind switch
            {
                ImportValueKind.Error => (null, error),
                ImportValueKind.Null => (null, null),
                ImportValueKind.Omit => (null, null),
                _ => (value, null),
            };
        }

        return string.IsNullOrEmpty(raw) ? (null, null) : (raw, null);
    }

    /// <summary>送信する項目を組み立てる（Id・非 createable/updateable を除去・参照の自動張り替え）。</summary>
    public static Dictionary<string, object?> BuildFields(
        IReadOnlyDictionary<string, object?> record,
        IReadOnlyDictionary<string, DataIoField> fieldsByName,
        IReadOnlyDictionary<string, string> idMap,
        bool forInsert)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in record)
        {
            if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!fieldsByName.TryGetValue(name, out var field))
            {
                continue;
            }

            if (forInsert ? !field.Createable : !field.Updateable)
            {
                continue;
            }

            if (value is null)
            {
                fields[name] = null;
                continue;
            }

            if (string.Equals(field.Type, "reference", StringComparison.OrdinalIgnoreCase)
                && value is string reference
                && idMap.TryGetValue(reference.Trim(), out var newId))
            {
                fields[name] = newId;
                continue;
            }

            fields[name] = value;
        }

        return fields;
    }

    /// <summary>表示用の文字列へ変換する。</summary>
    public static string? ValueDisplay(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>バックアップ ID（yyyyMMdd-HHmmss・衝突時は -2 等を付与）を生成する。</summary>
    public string MakeBackupId()
    {
        var baseId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = baseId;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(BackupsRoot, candidate)))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    /// <summary>バックアップ ID を検証する（パス区切り等を拒否）。</summary>
    public static bool ValidateBackupId(string? backupId) =>
        !string.IsNullOrWhiteSpace(backupId) &&
        backupId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    private static string? GetString(IReadOnlyDictionary<string, object?> record, string name) =>
        record.TryGetValue(name, out var value) ? ValueDisplay(value) : null;

    private static string? GetStringFromJson(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
