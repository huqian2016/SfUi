using System.Diagnostics;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Verification;

/// <summary>
/// 移行後の自動検証（件数・サンプル値・数値合計）。ステージング済みキュー（OK 行）と
/// ターゲットの実レコードを突き合わせてレポートを作る。ソースの再読込は行わない。
/// </summary>
public sealed class EtlVerifier
{
    private readonly RunStagingStore _store;
    private readonly IEtlRecordFetcher _fetcher;
    private readonly string _stepId;
    private readonly string _objectName;
    private readonly IReadOnlyList<StagingColumn> _columns;
    private readonly EtlVerificationOptions _options;

    /// <param name="columns">ステージング（= 適用キュー）のデータ列（ターゲット項目名 + 型。RowMapper.StagingColumns と同じ並び）。</param>
    public EtlVerifier(
        RunStagingStore store,
        IEtlRecordFetcher fetcher,
        string stepId,
        string objectName,
        IReadOnlyList<StagingColumn> columns,
        EtlVerificationOptions? options = null)
    {
        _store = store;
        _fetcher = fetcher;
        _stepId = stepId;
        _objectName = objectName;
        _columns = columns;
        _options = options ?? new EtlVerificationOptions();
    }

    /// <summary>均等間隔でサンプル行のインデックスを選ぶ（先頭と末尾を含む）。</summary>
    public static IReadOnlyList<int> SelectSample(int rowCount, int sampleSize)
    {
        if (rowCount <= 0)
        {
            return Array.Empty<int>();
        }

        if (sampleSize <= 0)
        {
            return Array.Empty<int>();
        }

        if (sampleSize >= rowCount)
        {
            return Enumerable.Range(0, rowCount).ToList();
        }

        if (sampleSize == 1)
        {
            return new[] { 0 };
        }

        var indexes = new List<int>(sampleSize);
        for (var i = 0; i < sampleSize; i++)
        {
            var index = (int)Math.Round(i * (rowCount - 1.0) / (sampleSize - 1.0));
            if (indexes.Count == 0 || indexes[^1] != index)
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    /// <summary>検証を実行する。</summary>
    public async Task<EtlVerificationResult> VerifyAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EtlVerificationResult { StepId = _stepId, ObjectName = _objectName };

        // ---- OK 行を読み出す（ステージング済みキュー。ソースは読まない）----
        var rows = ReadOkRows();
        result.OkRows = rows.Count;

        var targetRows = rows.Where(r => !string.Equals(r.Op, RowOp.Delete, StringComparison.OrdinalIgnoreCase)).ToList();
        var deleteRows = rows.Where(r => string.Equals(r.Op, RowOp.Delete, StringComparison.OrdinalIgnoreCase)).ToList();
        result.VerifiedRows = targetRows.Count;
        result.DeletedRows = deleteRows.Count;

        // ---- 件数（存在確認。数値列は合計照合用に同じ取得で読む）----
        var numericColumns = _options.CheckTotals
            ? _columns.Where(c => c.Type is StagingColumnType.Integer or StagingColumnType.Real).Select(c => c.Name).ToList()
            : new List<string>();

        var keyed = targetRows
            .Where(r => !string.IsNullOrEmpty(r.TargetId))
            .Select(r => (Row: r, Key: r.TargetId!))
            .ToList();
        var allKeys = keyed.Select(k => k.Key).Distinct(StringComparer.Ordinal).ToList();

        var fetched = await FetchAllAsync(allKeys, numericColumns, cancellationToken).ConfigureAwait(false);
        result.FetchedRows = fetched.Count;

        var fetchedKeys = new HashSet<string>(fetched.Select(f => f.Key), StringComparer.Ordinal);
        result.MissingKeys = allKeys.Where(k => !fetchedKeys.Contains(k)).ToList();

        // delete 対象は「残っていない」ことを確認する
        var deleteKeys = deleteRows
            .Where(r => !string.IsNullOrEmpty(r.TargetId))
            .Select(r => r.TargetId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (deleteKeys.Count > 0)
        {
            var remaining = await FetchAllAsync(deleteKeys, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            result.UndeletedKeys = remaining.Select(f => f.Key).Distinct(StringComparer.Ordinal).ToList();
        }

        // ---- 合計照合 ----
        if (numericColumns.Count > 0)
        {
            var byKey = fetched.ToDictionary(f => f.Key, f => f.Fields, StringComparer.Ordinal);
            for (var i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].Type is not (StagingColumnType.Integer or StagingColumnType.Real))
                {
                    continue;
                }

                var column = _columns[i];
                decimal expected = 0;
                foreach (var row in targetRows)
                {
                    TrySum(row.Values.Length > i ? row.Values[i] : null, ref expected);
                }

                decimal actual = 0;
                foreach (var key in allKeys)
                {
                    if (byKey.TryGetValue(key, out var fields) && fields.TryGetValue(column.Name, out var value))
                    {
                        TrySum(value, ref actual);
                    }
                }

                result.Totals.Add(new EtlVerificationTotal(column.Name, expected, actual));
            }
        }

        // ---- サンプル値照合 ----
        var sampleIndexes = SelectSample(targetRows.Count, _options.SampleSize);
        var sampleRows = sampleIndexes
            .Select(i => targetRows[i])
            .Where(r => !string.IsNullOrEmpty(r.TargetId))
            .ToList();

        if (sampleRows.Count > 0 && _columns.Count > 0)
        {
            result.SampledRows = sampleRows.Count;
            var sampleFields = _columns.Select(c => c.Name).ToList();
            var sampleFetched = await FetchAllAsync(
                sampleRows.Select(r => r.TargetId!).Distinct(StringComparer.Ordinal).ToList(),
                sampleFields,
                cancellationToken).ConfigureAwait(false);
            var sampleByKey = sampleFetched.ToDictionary(f => f.Key, f => f.Fields, StringComparer.Ordinal);

            foreach (var row in sampleRows)
            {
                if (!sampleByKey.TryGetValue(row.TargetId!, out var fields))
                {
                    continue;   // 欠落は MissingKeys 側で記録済み
                }

                for (var i = 0; i < _columns.Count; i++)
                {
                    var expected = row.Values.Length > i ? row.Values[i] : null;
                    fields.TryGetValue(_columns[i].Name, out var actual);
                    result.ComparedValues++;
                    if (EtlVerificationValues.Equal(expected, actual))
                    {
                        result.MatchedValues++;
                    }
                    else if (result.Mismatches.Count < 100)
                    {
                        result.Mismatches.Add(new EtlVerificationMismatch(
                            row.RowId,
                            row.TargetId,
                            _columns[i].Name,
                            EtlVerificationValues.Text(expected),
                            EtlVerificationValues.Text(actual)));
                    }
                }
            }
        }

        stopwatch.Stop();
        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private List<QueueRow> ReadOkRows()
    {
        var rows = new List<QueueRow>();
        long cursor = 0;
        while (rows.Count < _options.MaxRows)
        {
            var batch = _store.FetchQueueRows(_objectName, new[] { QueueStatus.Ok }, limit: 1000, afterRowId: cursor);
            if (batch.Count == 0)
            {
                break;
            }

            rows.AddRange(batch);
            cursor = batch[^1].RowId;
            if (batch.Count < 1000)
            {
                break;
            }
        }

        return rows;
    }

    private async Task<List<EtlFetchedRecord>> FetchAllAsync(
        IReadOnlyList<string> keys,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        var all = new List<EtlFetchedRecord>();
        var batchSize = Math.Max(1, _options.FetchBatchSize);
        for (var i = 0; i < keys.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = keys.Skip(i).Take(batchSize).ToList();
            var records = await _fetcher.FetchByKeysAsync(chunk, fields, cancellationToken).ConfigureAwait(false);
            all.AddRange(records);
        }

        return all;
    }

    private static bool TrySum(object? value, ref decimal total)
    {
        var text = EtlVerificationValues.Text(value).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            total += parsed;
            return true;
        }

        return false;
    }
}
