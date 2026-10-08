using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// 組織情報の定義書エクスポート（オブジェクト / 項目 / 画面レイアウト / リストビュー / フロー）。
/// Tooling / REST API から取得して 1 ブック複数シートの xlsx と CSV を書き出す。
/// 一部の取得失敗は警告に集約して継続する（部分成功を許容）。
/// </summary>
public sealed class OrgExportService
{
    /// <summary>1 クエリで保持する最大行数（安全弁）。</summary>
    public const int MaxRows = 20000;

    /// <summary>レイアウト Metadata 個別取得の上限。</summary>
    public const int MaxLayoutFetches = 1000;

    /// <summary>フロー Metadata 個別取得の上限。</summary>
    public const int MaxFlowFetches = 500;

    private const int LayoutObjectChunkSize = 50;
    private const int MaxPages = 200;

    private readonly SalesforceRestClient _rest;
    private readonly OrgInfoService _orgInfo;

    public OrgExportService(SalesforceRestClient rest, OrgInfoService orgInfo)
    {
        _rest = rest;
        _orgInfo = orgInfo;
    }

    public async Task<OrgExportResult> ExportAsync(
        OrgInfo org,
        OrgExportRequest request,
        IProgress<OrgExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(org.Alias) ? org.Username : org.Alias!;
        var objects = request.ObjectApiNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new OrgExportResult { ObjectCount = objects.Count };
        var sheets = new List<ExportSheet>();

        if (request.Documents.HasFlag(OrgExportDocuments.Objects) && objects.Count > 0)
        {
            progress?.Report(new OrgExportProgress(OrgExportStage.Objects, 0, 1));
            var section = await _orgInfo.FetchObjectsAsync(org, cancellationToken).ConfigureAwait(false);
            var allowed = new HashSet<string>(objects, StringComparer.OrdinalIgnoreCase);
            sheets.Add(OrgExportMapper.MapSection(
                UiText.T("OrgExport_Sheet_Objects"),
                section,
                row => allowed.Contains(row.Get("apiName") ?? string.Empty)));
            progress?.Report(new OrgExportProgress(OrgExportStage.Objects, 1, 1));
        }

        if (request.Documents.HasFlag(OrgExportDocuments.Fields))
        {
            for (var i = 0; i < objects.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OrgExportProgress(OrgExportStage.Fields, i, objects.Count, objects[i]));
                try
                {
                    var section = await _orgInfo.FetchFieldsAsync(org, objects[i], cancellationToken).ConfigureAwait(false);
                    sheets.Add(OrgExportMapper.MapSection(OrgExportMapper.FieldsSheetName(objects[i]), section));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_FieldFmt"), objects[i], ex.Message));
                }
            }

            progress?.Report(new OrgExportProgress(OrgExportStage.Fields, objects.Count, objects.Count));
        }

        if (request.Documents.HasFlag(OrgExportDocuments.Layouts) && objects.Count > 0)
        {
            var rows = new List<IReadOnlyList<string?>>();
            await FetchLayoutsAsync(target, objects, rows, result, progress, cancellationToken).ConfigureAwait(false);
            if (rows.Count > 0)
            {
                sheets.Add(OrgExportMapper.BuildLayoutSheet(rows));
            }
        }

        if (request.Documents.HasFlag(OrgExportDocuments.ListViews) && objects.Count > 0)
        {
            var rows = new List<IReadOnlyList<string?>>();
            await FetchListViewsAsync(target, objects, rows, result, progress, cancellationToken).ConfigureAwait(false);
            if (rows.Count > 0)
            {
                sheets.Add(OrgExportMapper.BuildListViewSheet(rows));
            }
        }

        if (request.Documents.HasFlag(OrgExportDocuments.Flows))
        {
            var (summaryRows, elementRows) = await FetchFlowsAsync(target, request.ActiveFlowsOnly, result, progress, cancellationToken).ConfigureAwait(false);
            if (summaryRows.Count > 0)
            {
                sheets.Add(OrgExportMapper.BuildFlowSummarySheet(summaryRows));
                sheets.Add(OrgExportMapper.BuildFlowElementsSheet(elementRows));
            }
        }

        result.SheetCount = sheets.Count;
        if (sheets.Count == 0)
        {
            return result;
        }

        progress?.Report(new OrgExportProgress(OrgExportStage.WritingFiles, 0, 1));
        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory) ? ResolveDefaultDirectory() : request.OutputDirectory;
        var baseName = string.IsNullOrWhiteSpace(request.BaseName) ? BuildDefaultBaseName(org) : request.BaseName;
        result.Files.AddRange(OrgExportWriter.Write(outputDirectory, baseName, sheets, request.Excel, request.Csv));
        progress?.Report(new OrgExportProgress(OrgExportStage.WritingFiles, 1, 1));
        return result;
    }

    /// <summary>既定の出力ファイル名（例: 定義書_hks4sand1_20261008-153000）。</summary>
    public static string BuildDefaultBaseName(OrgInfo org)
    {
        var alias = string.IsNullOrWhiteSpace(org.Alias) ? org.Username : org.Alias!;
        return OrgExportWriter.SanitizeFileName($"{UiText.T("OrgExport_FilePrefix")}_{alias}_{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    }

    private static string ResolveDefaultDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents) ? Path.GetTempPath() : Path.Combine(documents, "SfUi");
    }

    private async Task FetchLayoutsAsync(
        string target,
        List<string> objects,
        List<IReadOnlyList<string?>> rows,
        OrgExportResult result,
        IProgress<OrgExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var layouts = new List<(string ObjectApiName, string Id, string Name)>();
        for (var offset = 0; offset < objects.Count; offset += LayoutObjectChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = objects.Skip(offset).Take(LayoutObjectChunkSize).ToList();
            var records = await QueryAllAsync(target, OrgInfoQueryBuilder.BuildLayoutListQuery(chunk), useToolingApi: true, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                var id = GetString(record, "Id");
                var name = GetString(record, "Name");
                var objectApiName = GetString(record, "EntityDefinitionId");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(objectApiName))
                {
                    layouts.Add((objectApiName, id, name));
                }
            }
        }

        if (layouts.Count > MaxLayoutFetches)
        {
            result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_TruncatedFmt"), UiText.T("OrgExport_Sheet_Layouts")));
            layouts = layouts.Take(MaxLayoutFetches).ToList();
        }

        for (var i = 0; i < layouts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = layouts[i];
            progress?.Report(new OrgExportProgress(OrgExportStage.Layouts, i, layouts.Count, layout.Name));
            try
            {
                var records = await QueryAllAsync(target, OrgInfoQueryBuilder.BuildLayoutMetadataQuery(layout.Id), useToolingApi: true, cancellationToken).ConfigureAwait(false);
                var record = records.FirstOrDefault();
                if (record.ValueKind != JsonValueKind.Object ||
                    !record.TryGetProperty("Metadata", out var metadata) ||
                    metadata.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                rows.AddRange(OrgExportMapper.MapLayoutRows(layout.ObjectApiName, layout.Name, metadata));
                result.LayoutCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_LayoutFmt"), layout.Name, ex.Message));
            }
        }

        progress?.Report(new OrgExportProgress(OrgExportStage.Layouts, layouts.Count, layouts.Count));
    }

    private async Task FetchListViewsAsync(
        string target,
        List<string> objects,
        List<IReadOnlyList<string?>> rows,
        OrgExportResult result,
        IProgress<OrgExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var apiVersion = await _rest.GetApiVersionAsync(target, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < objects.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectApiName = objects[i];
            progress?.Report(new OrgExportProgress(OrgExportStage.ListViews, i, objects.Count, objectApiName));
            try
            {
                var listPath = $"/services/data/v{apiVersion}/sobjects/{Uri.EscapeDataString(objectApiName)}/listviews";
                using var document = JsonDocument.Parse(await _rest.SendRawAsync(target, HttpMethod.Get, listPath, null, cancellationToken).ConfigureAwait(false));
                if (!document.RootElement.TryGetProperty("listviews", out var listviews) || listviews.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in listviews.EnumerateArray())
                {
                    var summary = new ListViewSummary(
                        GetString(entry, "id") ?? string.Empty,
                        GetString(entry, "developerName") ?? string.Empty,
                        GetString(entry, "label") ?? string.Empty,
                        entry.TryGetProperty("soqlCompatible", out var compatible) && compatible.ValueKind == JsonValueKind.True);
                    if (summary.Id.Length == 0)
                    {
                        continue;
                    }

                    var describePath = $"/services/data/v{apiVersion}/sobjects/{Uri.EscapeDataString(objectApiName)}/listviews/{Uri.EscapeDataString(summary.Id)}/describe";
                    using var describeDocument = JsonDocument.Parse(await _rest.SendRawAsync(target, HttpMethod.Get, describePath, null, cancellationToken).ConfigureAwait(false));
                    rows.Add(OrgExportMapper.MapListViewRow(objectApiName, summary, describeDocument.RootElement));
                    result.ListViewCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_ListViewFmt"), objectApiName, ex.Message));
            }
        }

        progress?.Report(new OrgExportProgress(OrgExportStage.ListViews, objects.Count, objects.Count));
    }

    private async Task<(List<IReadOnlyList<string?>> Summary, List<IReadOnlyList<string?>> Elements)> FetchFlowsAsync(
        string target,
        bool activeOnly,
        OrgExportResult result,
        IProgress<OrgExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var summaryRows = new List<IReadOnlyList<string?>>();
        var elementRows = new List<IReadOnlyList<string?>>();

        var records = await QueryAllAsync(target, OrgInfoQueryBuilder.BuildFlowListQuery(activeOnly), useToolingApi: true, cancellationToken).ConfigureAwait(false);
        var flows = ParseFlowSummaries(records, activeOnly);

        if (flows.Count > MaxFlowFetches)
        {
            result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_TruncatedFmt"), UiText.T("OrgExport_Sheet_Flows")));
            flows = flows.Take(MaxFlowFetches).ToList();
        }

        for (var i = 0; i < flows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var flow = flows[i];
            progress?.Report(new OrgExportProgress(OrgExportStage.Flows, i, flows.Count, flow.Label));
            string? apiName = null;
            var elementCount = 0;
            try
            {
                var details = await QueryAllAsync(target, OrgInfoQueryBuilder.BuildFlowMetadataQuery(flow.Id), useToolingApi: true, cancellationToken).ConfigureAwait(false);
                var record = details.FirstOrDefault();
                if (record.ValueKind == JsonValueKind.Object)
                {
                    apiName = GetString(record, "FullName");
                    if (record.TryGetProperty("Metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                    {
                        var rows = OrgExportMapper.MapFlowElementRows(flow.Label, metadata);
                        elementCount = rows.Count;
                        elementRows.AddRange(rows);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Warnings.Add(string.Format(CultureInfo.CurrentCulture, UiText.T("OrgExport_Warn_FlowFmt"), flow.Label, ex.Message));
            }

            summaryRows.Add(OrgExportMapper.MapFlowSummaryRow(flow, apiName, elementCount));
            result.FlowCount++;
        }

        progress?.Report(new OrgExportProgress(OrgExportStage.Flows, flows.Count, flows.Count));
        return (summaryRows, elementRows);
    }

    /// <summary>
    /// フロー一覧クエリの結果を FlowSummary へ変換する。
    /// 非アクティブも含む場合は DefinitionId ごとに最新バージョンのみを残す（版違いの重複を排除）。
    /// </summary>
    internal static List<FlowSummary> ParseFlowSummaries(IReadOnlyList<JsonElement> records, bool activeOnly)
    {
        var list = new List<FlowSummary>();
        foreach (var record in records)
        {
            var id = GetString(record, "Id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var version = 1;
            if (record.TryGetProperty("VersionNumber", out var versionElement) &&
                versionElement.ValueKind == JsonValueKind.Number &&
                versionElement.TryGetInt32(out var versionNumber))
            {
                version = versionNumber;
            }

            list.Add(new FlowSummary(
                id,
                GetString(record, "DefinitionId"),
                GetString(record, "MasterLabel") ?? string.Empty,
                GetString(record, "Status") ?? string.Empty,
                GetString(record, "ProcessType") ?? string.Empty,
                version,
                GetString(record, "LastModifiedDate")));
        }

        if (activeOnly)
        {
            return list;
        }

        return list
            .GroupBy(flow => string.IsNullOrEmpty(flow.DefinitionId) ? flow.Id : flow.DefinitionId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(flow => flow.Version).First())
            .OrderBy(flow => flow.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>SOQL を全ページ取得する（records は JsonDocument 破棄後も使えるよう Clone 済み）。</summary>
    private async Task<List<JsonElement>> QueryAllAsync(string target, string soql, bool useToolingApi, CancellationToken cancellationToken)
    {
        var records = new List<JsonElement>();
        var pages = 0;

        using (var document = await _rest.QueryAsync(target, soql, useToolingApi, cancellationToken).ConfigureAwait(false))
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("records", out var firstRecords) || firstRecords.ValueKind != JsonValueKind.Array)
            {
                return records;
            }

            foreach (var record in firstRecords.EnumerateArray())
            {
                records.Add(record.Clone());
                if (records.Count >= MaxRows)
                {
                    return records;
                }
            }

            var next = GetString(root, "nextRecordsUrl");
            while (!string.IsNullOrEmpty(next) && pages < MaxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages++;
                using var page = await _rest.GetPageAsync(target, next!, cancellationToken).ConfigureAwait(false);
                var pageRoot = page.RootElement;
                if (pageRoot.TryGetProperty("records", out var pageRecords) && pageRecords.ValueKind == JsonValueKind.Array)
                {
                    foreach (var record in pageRecords.EnumerateArray())
                    {
                        records.Add(record.Clone());
                        if (records.Count >= MaxRows)
                        {
                            return records;
                        }
                    }
                }

                next = GetString(pageRoot, "nextRecordsUrl");
            }
        }

        return records;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
