using System.Globalization;
using SfUi.Etl.Expressions;
using SfUi.Etl.Staging;

namespace SfUi.Etl.Engine;

/// <summary>
/// crosswalk（ステップ間のキー対応表）を式言語と接続するヘルパー。
/// 複数ステップのジョブでは、子ステップの式で <c>LOOKUP("Object", "キー", 値)</c> を使って親ステップの Id を解決する。
/// </summary>
public static class EtlCrosswalk
{
    /// <summary>
    /// <see cref="ExpressionHost.Lookup"/> が未設定の場合に、crosswalk 参照（オブジェクト名横断）で配線する。
    /// ストア作成後に呼び出し、そのあとで RowMapper / 式を構築すること。
    /// </summary>
    public static void WireLookup(ExpressionHost host, RunStagingStore store)
    {
        host.Lookup ??= (objectName, key, value) =>
            store.LookupCrosswalkByObject(
                objectName,
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
