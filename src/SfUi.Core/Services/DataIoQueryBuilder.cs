using System.Text;

namespace SfUi.Core;

/// <summary>データエクスポート用の SOQL を組み立てる。</summary>
public static class DataIoQueryBuilder
{
    /// <summary>LIMIT の上限（それ以上はクランプ）。</summary>
    public const int MaxLimit = 200000;

    /// <summary>オブジェクト + 項目 + WHERE / ORDER BY / LIMIT から SOQL を生成する。</summary>
    public static string Build(string objectName, IReadOnlyList<string> fields, string? where, string? orderBy, int? limit)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException(UiText.T("DataIo_Err_ObjectRequired"));
        }

        if (fields is null || fields.Count == 0 || fields.All(f => string.IsNullOrWhiteSpace(f)))
        {
            throw new ArgumentException(UiText.T("DataIo_Err_FieldsRequired"));
        }

        var builder = new StringBuilder("SELECT ");
        builder.Append(string.Join(", ", fields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())));
        builder.Append(" FROM ").Append(objectName.Trim());

        var whereText = NormalizeClause(where, "WHERE");
        if (whereText is not null)
        {
            builder.Append(" WHERE ").Append(whereText);
        }

        var orderText = NormalizeClause(orderBy, "ORDER BY");
        if (orderText is not null)
        {
            builder.Append(" ORDER BY ").Append(orderText);
        }

        if (limit is int value && value > 0)
        {
            builder.Append(" LIMIT ").Append(Math.Min(value, MaxLimit));
        }

        return builder.ToString();
    }

    private static string? NormalizeClause(string? clause, string keyword)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return null;
        }

        var text = clause.Trim().TrimEnd(';').Trim();
        if (text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            text = text[keyword.Length..].Trim();
        }

        return text.Length == 0 ? null : text;
    }
}
