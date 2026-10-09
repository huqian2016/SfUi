using System.Data.Common;
using System.Data.Odbc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace SfUi.Etl.Database;

/// <summary>データベース プロバイダーの種別。</summary>
public enum DbProviderKind
{
    Sqlite,
    SqlServer,
    PostgreSql,
    Odbc,
}

/// <summary>
/// データベース接続の指定（プロバイダー種別 + 接続文字列）。接続・識別子クォートの共通化。
/// </summary>
public sealed record DbConnectionSpec(DbProviderKind Kind, string ConnectionString)
{
    /// <summary>使用可能なプロバイダー（UI の選択肢用）。</summary>
    public static IReadOnlyList<DbProviderKind> All { get; } = new[]
    {
        DbProviderKind.Sqlite,
        DbProviderKind.SqlServer,
        DbProviderKind.PostgreSql,
        DbProviderKind.Odbc,
    };

    /// <summary>新しい接続を生成する（未オープン）。</summary>
    public DbConnection CreateConnection() => Kind switch
    {
        DbProviderKind.Sqlite => new SqliteConnection(ConnectionString),
        DbProviderKind.SqlServer => new SqlConnection(ConnectionString),
        DbProviderKind.PostgreSql => new NpgsqlConnection(ConnectionString),
        DbProviderKind.Odbc => new OdbcConnection(ConnectionString),
        _ => throw new InvalidOperationException("未対応のプロバイダーです: " + Kind),
    };

    /// <summary>識別子（テーブル / 列名）をプロバイダー仕様でクォートする。</summary>
    public static string QuoteIdentifier(DbProviderKind kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return kind switch
        {
            DbProviderKind.SqlServer => "[" + name.Replace("]", "]]") + "]",
            _ => "\"" + name.Replace("\"", "\"\"") + "\"",
        };
    }

    /// <summary>この指定のクォート済み識別子。</summary>
    public string Quote(string name) => QuoteIdentifier(Kind, name);
}
