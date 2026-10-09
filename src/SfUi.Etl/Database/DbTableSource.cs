using SfUi.Etl.Sources;

namespace SfUi.Etl.Database;

/// <summary>
/// SQL クエリの結果をストリーミングで読む入力ソース（SQLite / SQL Server / PostgreSQL / ODBC）。
/// 列はクエリのメタデータから取得する（SchemaOnly。非対応のドライバーでは全行読み込みにフォールバック）。
/// </summary>
public sealed class DbTableSource : IEtlSource
{
    private readonly DbConnectionSpec _spec;
    private readonly string _query;
    private readonly List<object?[]>? _bufferedRows;

    /// <param name="spec">接続指定。</param>
    /// <param name="query">データ取得クエリ（SELECT）。</param>
    public DbTableSource(DbConnectionSpec spec, string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        _spec = spec;
        _query = query;
        Name = $"DB:{spec.Kind}";

        try
        {
            Columns = ReadColumnsSchemaOnly();
        }
        catch (Exception)
        {
            // SchemaOnly 非対応ドライバー: 全行読み込みにフォールバック
            var (columns, rows) = ReadAllWithSchema();
            Columns = columns;
            _bufferedRows = rows;
        }
    }

    public string Name { get; }

    public IReadOnlyList<string> Columns { get; }

    public IEnumerable<object?[]> ReadRows()
    {
        if (_bufferedRows is not null)
        {
            foreach (var row in _bufferedRows)
            {
                yield return row;
            }

            yield break;
        }

        using var connection = _spec.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = _query;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            yield return row;
        }
    }

    private IReadOnlyList<string> ReadColumnsSchemaOnly()
    {
        using var connection = _spec.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = _query;
        using var reader = command.ExecuteReader(System.Data.CommandBehavior.SchemaOnly);
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        return columns;
    }

    private (IReadOnlyList<string> Columns, List<object?[]> Rows) ReadAllWithSchema()
    {
        using var connection = _spec.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = _query;
        using var reader = command.ExecuteReader();
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return (columns, rows);
    }
}
