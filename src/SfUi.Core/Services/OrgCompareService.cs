using System.Data;

namespace SfUi.Core;

/// <summary>
/// 複数組織の比較表を組み立てる。キャッシュ（OrgInfoCacheStore）を優先し、未取得セクションは
/// OrgInfoService で取得してキャッシュへ保存する。突合・差分判定は static の純関数（BuildTable など）に
/// 分離してテスト可能にする。
/// </summary>
public sealed class OrgCompareService
{
    /// <summary>セル内の複数列を連結する区切り文字。</summary>
    public const string CellSeparator = " ・ ";

    /// <summary>比較不能（その組織に存在しない）セルの表示文字列。</summary>
    public const string MissingText = "—";

    /// <summary>差分行を表す文字列（CSV の差分列）。</summary>
    public const string DiffMark = "≠";

    private readonly OrgInfoService _orgInfo;
    private readonly OrgInfoCacheStore _cache;
    private readonly AppLog _log;

    public OrgCompareService(OrgInfoService orgInfo, OrgInfoCacheStore cache, AppLog log)
    {
        _orgInfo = orgInfo;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// 選択組織の比較表を構築する。キャッシュ優先。未取得（または forceRefresh）のセクションは
    /// API から取得する。fetchMissing=false のときは取得せず「未取得」状態のままにする（言語切替の再構築用）。
    /// </summary>
    public async Task<OrgCompareTable> BuildAsync(
        IReadOnlyList<OrgInfo> orgs,
        OrgCompareCategory category,
        bool forceRefresh,
        bool fetchMissing,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var columns = orgs
            .Select(o => new OrgCompareOrgColumn(OrgInfoCacheStore.GetOrgKey(o), o.DisplayName, o.Username, o.InstanceUrl))
            .ToList();

        var sources = new List<OrgCompareSource>();
        var sectionIds = OrgCompareCategories.RequiredSectionIds(category);

        foreach (var org in orgs)
        {
            var orgKey = OrgInfoCacheStore.GetOrgKey(org);
            foreach (var sectionId in sectionIds)
            {
                ct.ThrowIfCancellationRequested();

                var cached = _cache.GetSection(orgKey, sectionId);
                var needsFetch = forceRefresh || cached is null || !cached.HasData;

                if (!needsFetch)
                {
                    sources.Add(new OrgCompareSource(orgKey, sectionId, OrgCompareCellState.Value, cached));
                    continue;
                }

                if (!fetchMissing)
                {
                    sources.Add(new OrgCompareSource(orgKey, sectionId, OrgCompareCellState.NotFetched, null));
                    continue;
                }

                progress?.Report(UiText.T("Compare_FetchingFmt", org.DisplayName, OrgInfoSearchService.SectionTitle(sectionId)));
                try
                {
                    var fetched = await _orgInfo.FetchSectionAsync(org, sectionId, ct).ConfigureAwait(false);
                    _cache.UpsertSection(orgKey, fetched);
                    sources.Add(new OrgCompareSource(orgKey, sectionId, OrgCompareCellState.Value, fetched));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warn($"組織比較: {org.DisplayName} / {sectionId} の取得に失敗しました: {ex.Message}");
                    if (cached is { HasData: true })
                    {
                        // 再取得失敗時は既存キャッシュを表示し続ける（fetchedAt の古さで判断できる）
                        sources.Add(new OrgCompareSource(orgKey, sectionId, OrgCompareCellState.Value, cached));
                    }
                    else
                    {
                        sources.Add(new OrgCompareSource(orgKey, sectionId, OrgCompareCellState.Failed, null));
                    }
                }
            }
        }

        return BuildTable(category, columns, sources);
    }

    /// <summary>セクションをキャッシュ優先で確保する（未取得なら取得してキャッシュへ保存。失敗時はキャッシュ値を返す）。</summary>
    public async Task<OrgInfoSection?> EnsureSectionAsync(
        OrgInfo org,
        string sectionId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var orgKey = OrgInfoCacheStore.GetOrgKey(org);
        var cached = _cache.GetSection(orgKey, sectionId);
        if (!forceRefresh && cached is { HasData: true })
        {
            return cached;
        }

        try
        {
            var fetched = await _orgInfo.FetchSectionAsync(org, sectionId, cancellationToken).ConfigureAwait(false);
            _cache.UpsertSection(orgKey, fetched);
            return fetched;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"組織比較: {org.DisplayName} / {sectionId} の取得に失敗しました: {ex.Message}");
            return cached;
        }
    }

    /// <summary>比較表を組み立てる（純関数・テスト対象）。</summary>
    public static OrgCompareTable BuildTable(
        OrgCompareCategory category,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        IReadOnlyList<OrgCompareSource> sources)
    {
        var map = new Dictionary<(string OrgKey, string SectionId), OrgCompareSource>();
        foreach (var source in sources)
        {
            map[(source.OrgKey, source.SectionId)] = source;
        }

        OrgCompareSource? SourceOf(string orgKey, string sectionId) =>
            map.TryGetValue((orgKey, sectionId), out var source) ? source : null;

        return category.Kind switch
        {
            OrgCompareKind.Items => BuildItems(category, orgs, SourceOf),
            OrgCompareKind.Keyed => BuildKeyed(category, orgs, SourceOf),
            _ => BuildStats(category, orgs, SourceOf),
        };
    }

    /// <summary>行の突合キーを求める（KeyColumns 未指定 = 行 Id。複数列は "." 連結、欠損時は行 Id にフォールバック）。</summary>
    public static string ComputeKey(OrgCompareCategory category, OrgInfoRow row)
    {
        if (category.KeyColumns.Count == 0)
        {
            return row.Id;
        }

        var parts = new string[category.KeyColumns.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var value = row.Get(category.KeyColumns[i]);
            if (string.IsNullOrWhiteSpace(value))
            {
                return row.Id;
            }

            parts[i] = value.Trim();
        }

        return string.Join(".", parts);
    }

    /// <summary>行の差分判定。Value / Missing のセルだけを対象にする（未取得・失敗は比較から除外）。
    /// 値の比較は大文字小文字を無視し、null と空文字は同一視する（キー突合と揃える）。</summary>
    public static bool IsRowDiff(IReadOnlyList<OrgCompareCell> cells)
    {
        var participating = cells
            .Where(c => c.State is OrgCompareCellState.Value or OrgCompareCellState.Missing)
            .ToList();

        if (participating.Count < 2)
        {
            return false;
        }

        if (participating.Any(c => c.State == OrgCompareCellState.Missing))
        {
            return participating.Any(c => c.State == OrgCompareCellState.Value);
        }

        var first = Normalize(participating[0].RawValues);
        return participating.Skip(1).Any(c => !Normalize(c.RawValues).SequenceEqual(first, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>セルの表示文字列（状態をローカライズして返す）。</summary>
    public static string CellText(OrgCompareCell cell) => cell.State switch
    {
        OrgCompareCellState.Missing => MissingText,
        OrgCompareCellState.NotFetched => UiText.T("Compare_NotFetched"),
        OrgCompareCellState.Failed => UiText.T("Compare_FetchFailed"),
        _ => cell.Text ?? string.Empty,
    };

    /// <summary>CSV 出力用の DataTable を構築する（列 = 項目 / API 名 + 組織 + 差分）。diffOnly=true は差分行のみ。</summary>
    public static DataTable BuildDataTable(OrgCompareTable table, bool diffOnly = false)
    {
        var dataTable = new DataTable();
        dataTable.Columns.Add(UiText.T("Compare_KeyHeader"), typeof(string));
        foreach (var org in table.Orgs)
        {
            dataTable.Columns.Add(org.DisplayName, typeof(string));
        }

        dataTable.Columns.Add(UiText.T("Compare_DiffColumn"), typeof(string));

        foreach (var row in table.Rows.Where(r => !diffOnly || r.IsDiff))
        {
            var values = new object?[table.Orgs.Count + 2];
            values[0] = row.Label;
            for (var i = 0; i < table.Orgs.Count; i++)
            {
                values[i + 1] = CellText(row.Cells[i]);
            }

            values[^1] = row.IsDiff ? DiffMark : string.Empty;
            dataTable.Rows.Add(values);
        }

        return dataTable;
    }

    private static OrgCompareTable BuildItems(
        OrgCompareCategory category,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        Func<string, string, OrgCompareSource?> sourceOf)
    {
        var sectionId = category.SectionId!;
        // 行テンプレート = 最初に見つかった取得済みセクションの行（項目集合は固定）
        var template = orgs
            .Select(o => sourceOf(o.OrgKey, sectionId))
            .Where(s => s is { State: OrgCompareCellState.Value, Section: not null })
            .Select(s => s!.Section!)
            .FirstOrDefault();

        var rows = new List<OrgCompareRow>();
        if (template is not null)
        {
            foreach (var templateRow in template.Rows)
            {
                var label = OrgInfoDisplay.FormatCell(sectionId, templateRow, "item", templateRow.Get("item")) ?? templateRow.Id;
                var cells = new List<OrgCompareCell>(orgs.Count);

                foreach (var org in orgs)
                {
                    var source = sourceOf(org.OrgKey, sectionId);
                    if (source is null || source.State == OrgCompareCellState.NotFetched)
                    {
                        cells.Add(OrgCompareCells.NotFetched);
                        continue;
                    }

                    if (source.State == OrgCompareCellState.Failed)
                    {
                        cells.Add(OrgCompareCells.Failed);
                        continue;
                    }

                    var row = FindRow(source.Section, templateRow.Id);
                    if (row is null)
                    {
                        cells.Add(OrgCompareCells.Missing);
                        continue;
                    }

                    var raw = row.Get("value");
                    var text = OrgInfoDisplay.FormatCell(sectionId, row, "value", raw);
                    cells.Add(new OrgCompareCell(OrgCompareCellState.Value, text, row.Link ?? SectionLink(org, sectionId), new[] { raw }));
                }

                rows.Add(new OrgCompareRow(templateRow.Id, label, IsRowDiff(cells), cells));
            }
        }

        return new OrgCompareTable(category.Id, orgs, rows);
    }

    private static OrgCompareTable BuildKeyed(
        OrgCompareCategory category,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        Func<string, string, OrgCompareSource?> sourceOf)
    {
        var sectionId = category.SectionId!;

        // 組織ごとのキー → 行（キー突合は大文字小文字を無視）
        var byOrg = new Dictionary<string, Dictionary<string, OrgInfoRow>>(StringComparer.Ordinal);
        foreach (var org in orgs)
        {
            var dict = new Dictionary<string, OrgInfoRow>(StringComparer.OrdinalIgnoreCase);
            var source = sourceOf(org.OrgKey, sectionId);
            if (source is { State: OrgCompareCellState.Value, Section: not null })
            {
                foreach (var row in source.Section!.Rows)
                {
                    dict.TryAdd(ComputeKey(category, row), row);
                }
            }

            byOrg[org.OrgKey] = dict;
        }

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in byOrg.Values)
        {
            foreach (var key in dict.Keys)
            {
                keys.Add(key);
            }
        }

        var rows = new List<OrgCompareRow>();
        foreach (var key in keys)
        {
            var cells = new List<OrgCompareCell>(orgs.Count);
            string? label = null;

            foreach (var org in orgs)
            {
                var source = sourceOf(org.OrgKey, sectionId);
                if (source is null || source.State == OrgCompareCellState.NotFetched)
                {
                    cells.Add(OrgCompareCells.NotFetched);
                    continue;
                }

                if (source.State == OrgCompareCellState.Failed)
                {
                    cells.Add(OrgCompareCells.Failed);
                    continue;
                }

                if (!byOrg[org.OrgKey].TryGetValue(key, out var row))
                {
                    cells.Add(OrgCompareCells.Missing);
                    continue;
                }

                label ??= category.KeyDisplayColumn is { Length: > 0 } keyColumn
                    ? OrgInfoDisplay.FormatCell(sectionId, row, keyColumn, row.Get(keyColumn)) ?? key
                    : key;

                var text = BuildCellText(sectionId, row, category.DisplayColumns);
                var rawValues = category.DisplayColumns.Select(c => row.Get(c)).ToArray();
                cells.Add(new OrgCompareCell(OrgCompareCellState.Value, text, row.Link ?? SectionLink(org, sectionId), rawValues));
            }

            rows.Add(new OrgCompareRow(key, label ?? key, IsRowDiff(cells), cells));
        }

        return new OrgCompareTable(category.Id, orgs, rows);
    }

    private static OrgCompareTable BuildStats(
        OrgCompareCategory category,
        IReadOnlyList<OrgCompareOrgColumn> orgs,
        Func<string, string, OrgCompareSource?> sourceOf)
    {
        var rows = new List<OrgCompareRow>();
        foreach (var stat in OrgInfoCatalog.Stats)
        {
            var sourceSectionId = OrgInfoCatalog.StatSourceSection(stat.Id);
            var cells = new List<OrgCompareCell>(orgs.Count);

            foreach (var org in orgs)
            {
                var source = sourceSectionId is null ? null : sourceOf(org.OrgKey, sourceSectionId);
                if (source is null || source.State == OrgCompareCellState.NotFetched)
                {
                    cells.Add(OrgCompareCells.NotFetched);
                    continue;
                }

                if (source.State == OrgCompareCellState.Failed)
                {
                    cells.Add(OrgCompareCells.Failed);
                    continue;
                }

                var value = OrgInfoCatalog.ComputeStat(stat.Id, id => sourceOf(org.OrgKey, id)?.Section);
                cells.Add(new OrgCompareCell(OrgCompareCellState.Value, value, null, new[] { value }));
            }

            rows.Add(new OrgCompareRow(stat.Id, UiText.T(stat.LabelKey), IsRowDiff(cells), cells));
        }

        return new OrgCompareTable(category.Id, orgs, rows);
    }

    private static string? BuildCellText(string sectionId, OrgInfoRow row, IReadOnlyList<string> columns)
    {
        var parts = new List<string>(columns.Count);
        foreach (var column in columns)
        {
            var text = OrgInfoDisplay.FormatCell(sectionId, row, column, row.Get(column));
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(CellSeparator, parts);
    }

    /// <summary>行固有のリンクが無いときのセクション Setup URL（その組織のページ）を返す。</summary>
    private static string? SectionLink(OrgCompareOrgColumn org, string sectionId) =>
        OrgInfoUrlBuilder.ForSection(org.InstanceUrl, sectionId);

    private static OrgInfoRow? FindRow(OrgInfoSection? section, string rowId) =>
        section?.Rows.FirstOrDefault(r => string.Equals(r.Id, rowId, StringComparison.Ordinal));

    private static string[] Normalize(IReadOnlyList<string?> rawValues) =>
        rawValues.Select(v => v ?? string.Empty).ToArray();
}
