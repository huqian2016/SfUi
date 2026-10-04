using System.Text.Json;

namespace SfUi.Core;

/// <summary>REST クエリ（query + queryMore）の共通ページング。</summary>
internal static class RestQueryPager
{
    /// <summary>取得結果（maxRecords で打ち切った場合は Truncated = true）。</summary>
    internal sealed record PagedResult(List<JsonElement> Records, bool Truncated);

    public static async Task<PagedResult> QueryAllAsync(
        SalesforceRestClient rest,
        string targetOrg,
        string soql,
        int maxRecords,
        CancellationToken cancellationToken,
        bool allRows = false)
    {
        var records = new List<JsonElement>();
        var truncated = false;

        var document = allRows
            ? await rest.QueryAllAsync(targetOrg, soql, cancellationToken).ConfigureAwait(false)
            : await rest.QueryAsync(targetOrg, soql, false, cancellationToken).ConfigureAwait(false);
        try
        {
            var (done, next) = Append(document.RootElement, records);
            while (!done && !string.IsNullOrEmpty(next))
            {
                if (records.Count >= maxRecords)
                {
                    truncated = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var page = await rest.GetPageAsync(targetOrg, next!, cancellationToken).ConfigureAwait(false);
                try
                {
                    (done, next) = Append(page.RootElement, records);
                }
                finally
                {
                    page.Dispose();
                }
            }
        }
        finally
        {
            document.Dispose();
        }

        if (records.Count > maxRecords)
        {
            truncated = true;
            records.RemoveRange(maxRecords, records.Count - maxRecords);
        }

        return new PagedResult(records, truncated);
    }

    private static (bool Done, string? Next) Append(JsonElement root, List<JsonElement> records)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (true, null);
        }

        if (root.TryGetProperty("records", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in array.EnumerateArray())
            {
                records.Add(record.Clone());
            }
        }

        var done = root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.True;
        var next = root.TryGetProperty("nextRecordsUrl", out var nextElement) && nextElement.ValueKind == JsonValueKind.String
            ? nextElement.GetString()
            : null;
        return (done, next);
    }
}
