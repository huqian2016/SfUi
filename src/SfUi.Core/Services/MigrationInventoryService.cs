using System.Globalization;
using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// 移行棚卸し（Workflow ルール / プロセスビルダー / フロー）を取得する。
/// WorkflowRule は Tooling API、FlowDefinitionView は REST（query + queryMore）で読み取る。
/// </summary>
public sealed class MigrationInventoryService
{
    private readonly SalesforceRestClient _rest;
    private readonly AppLog _log;

    public MigrationInventoryService(SalesforceRestClient rest, AppLog log)
    {
        _rest = rest;
        _log = log;
    }

    /// <summary>棚卸しを取得する（Workflow ルールの失敗は警告として返し、フローは継続する）。</summary>
    public async Task<MigrationInventory> FetchAsync(string targetOrg, CancellationToken cancellationToken)
    {
        var items = new List<MigrationItem>();
        string? workflowError = null;
        try
        {
            var rows = await QueryRowsAsync(
                targetOrg, "SELECT Id, Name, TableEnumOrId, LastModifiedDate FROM WorkflowRule", useToolingApi: true, cancellationToken)
                .ConfigureAwait(false);
            items.AddRange(ParseWorkflowRules(rows));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            workflowError = ex.Message;
            _log.Warn($"Workflow ルールの取得に失敗しました: {ex.Message}");
        }

        var flowRows = await QueryRowsAsync(
            targetOrg,
            "SELECT Id, ApiName, Label, ProcessType, TriggerType, IsActive, LastModifiedDate, TriggerObjectOrEventLabel FROM FlowDefinitionView",
            useToolingApi: false,
            cancellationToken).ConfigureAwait(false);
        items.AddRange(ParseFlows(flowRows));

        var ordered = items
            .OrderBy(item => (int)item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _log.Info($"移行棚卸し: {targetOrg}（Workflow {ordered.Count(i => i.Kind == MigrationItemKind.WorkflowRule)}" +
            $" / Process Builder {ordered.Count(i => i.Kind == MigrationItemKind.ProcessBuilder)}" +
            $" / Flow {ordered.Count(i => i.Kind == MigrationItemKind.Flow)}）");
        return new MigrationInventory(
            ordered,
            ordered.Count(i => i.Kind == MigrationItemKind.WorkflowRule),
            ordered.Count(i => i.Kind == MigrationItemKind.ProcessBuilder),
            ordered.Count(i => i.Kind == MigrationItemKind.Flow),
            ordered.Count(i => i.Active == true),
            workflowError);
    }

    /// <summary>WorkflowRule（Tooling API）のレコードを棚卸し行へ変換する。</summary>
    public static List<MigrationItem> ParseWorkflowRules(IReadOnlyList<JsonElement> rows)
    {
        var items = new List<MigrationItem>();
        foreach (var row in rows)
        {
            var name = GetString(row, "Name") ?? string.Empty;
            items.Add(new MigrationItem(
                MigrationItemKind.WorkflowRule,
                name,
                string.Empty,
                GetString(row, "TableEnumOrId") ?? string.Empty,
                null,
                GetDate(row, "LastModifiedDate"),
                string.Empty,
                GetString(row, "Id") ?? string.Empty));
        }

        return items;
    }

    /// <summary>FlowDefinitionView のレコードを棚卸し行へ変換する（ProcessType = Workflow はプロセスビルダー）。</summary>
    public static List<MigrationItem> ParseFlows(IReadOnlyList<JsonElement> rows)
    {
        var items = new List<MigrationItem>();
        foreach (var row in rows)
        {
            var apiName = GetString(row, "ApiName") ?? string.Empty;
            var label = GetString(row, "Label");
            var processType = GetString(row, "ProcessType") ?? string.Empty;
            var kind = string.Equals(processType, "Workflow", StringComparison.OrdinalIgnoreCase)
                ? MigrationItemKind.ProcessBuilder
                : MigrationItemKind.Flow;
            items.Add(new MigrationItem(
                kind,
                string.IsNullOrEmpty(label) ? apiName : label,
                apiName,
                GetString(row, "TriggerObjectOrEventLabel") ?? string.Empty,
                GetBool(row, "IsActive"),
                GetDate(row, "LastModifiedDate"),
                processType,
                GetString(row, "Id") ?? string.Empty));
        }

        return items;
    }

    // ---- 内部 ----

    private async Task<List<JsonElement>> QueryRowsAsync(
        string targetOrg, string soql, bool useToolingApi, CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        using (var document = await _rest.QueryAsync(targetOrg, soql, useToolingApi, cancellationToken).ConfigureAwait(false))
        {
            AddRows(document.RootElement, rows);
            var next = GetNextUrl(document.RootElement);
            while (next is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = await _rest.GetPageAsync(targetOrg, next, cancellationToken).ConfigureAwait(false);
                AddRows(page.RootElement, rows);
                next = GetNextUrl(page.RootElement);
            }
        }

        return rows;
    }

    private static void AddRows(JsonElement root, List<JsonElement> rows)
    {
        if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in records.EnumerateArray())
            {
                rows.Add(row.Clone());
            }
        }
    }

    private static string? GetNextUrl(JsonElement root) =>
        root.TryGetProperty("nextRecordsUrl", out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static string? GetString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool? GetBool(JsonElement row, string name) =>
        row.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;

    private static DateTimeOffset? GetDate(JsonElement row, string name) =>
        row.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
}
