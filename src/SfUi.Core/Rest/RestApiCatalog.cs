namespace SfUi.Core;

/// <summary>REST ツリーのノード種別。</summary>
public enum RestApiNodeKind
{
    /// <summary>展開できるフォルダー（エンドポイントではない）。</summary>
    Folder,

    /// <summary>選択すると Method / パス / ボディを入力欄へ反映するエンドポイント。</summary>
    Request,
}

/// <summary>エンドポイント選択時に入力を求めるパラメータ（Id / q など）。</summary>
public sealed record RestApiParameter(
    string Name,
    string LabelKey,
    string? DefaultValue = null,
    bool Required = true);

/// <summary>REST ツリーの 1 ノード（静的カタログ / 組織の SObject から生成）。</summary>
public sealed record RestApiNode(
    string Id,
    string Title,
    RestApiNodeKind Kind = RestApiNodeKind.Folder,
    string? Method = null,
    string? PathTemplate = null,
    string? BodyTemplate = null,
    IReadOnlyList<RestApiParameter>? Parameters = null,
    IReadOnlyList<RestApiNode>? Children = null,
    string? ObjectName = null,
    Func<IReadOnlyList<RestApiNode>>? ChildFactory = null);

/// <summary>
/// REST エクスプローラーのツリー定義。静的なエンドポイント カタログと、
/// 組織の SObject 一覧から組み立てる動的ノードを提供する。
/// タイトルは <see cref="UiText"/> でローカライズするため、言語切替時は作り直す。
/// </summary>
public static class RestApiCatalog
{
    /// <summary>アクセスする API バージョンのプレースホルダー。</summary>
    public const string VersionToken = "{version}";

    private const string DataPrefix = "/services/data/";
    private const string BasePath = DataPrefix + "v" + VersionToken;
    private const string ObjectToken = "{object}";

    /// <summary>静的なグループ（システム / Tooling / Composite / Actions / UI API / Bulk）。</summary>
    public static IReadOnlyList<RestApiNode> BuildStaticGroups() =>
        new[] { BuildSystemGroup(), BuildToolingGroup(), BuildCompositeGroup(), BuildActionsGroup(), BuildUiGroup(), BuildBulkGroup() };

    private static RestApiNode BuildSystemGroup() =>
        new("group.system", UiText.T("ApiTree_GroupSystem"), Children: new[]
        {
            Request("system.versions", UiText.T("ApiTree_Versions"), "GET", $"{DataPrefix}"),
            Request("system.limits", UiText.T("ApiTree_Limits"), "GET", $"{BasePath}/limits"),
            Request("system.theme", UiText.T("ApiTree_Theme"), "GET", $"{BasePath}/theme"),
            Request("system.recent", UiText.T("ApiTree_Recent"), "GET", $"{BasePath}/recent"),
            Request(
                "system.query",
                UiText.T("ApiTree_Query"),
                "GET",
                $"{BasePath}/query?q={{q}}",
                parameters: new[] { new RestApiParameter("q", "ApiTree_ParamQuery", "SELECT Id, Name FROM Account LIMIT 10") }),
            Request(
                "system.search",
                UiText.T("ApiTree_Search"),
                "GET",
                $"{BasePath}/search?q={{q}}",
                parameters: new[] { new RestApiParameter("q", "ApiTree_ParamQuery", "FIND {Acme}") }),
        });

    private static RestApiNode BuildToolingGroup() =>
        new("group.tooling", UiText.T("ApiTree_GroupTooling"), Children: new[]
        {
            Request(
                "tooling.query",
                UiText.T("ApiTree_ToolingQuery"),
                "GET",
                $"{BasePath}/tooling/query?q={{q}}",
                parameters: new[] { new RestApiParameter("q", "ApiTree_ParamQuery", "SELECT Id, Name FROM ApexClass LIMIT 10") }),
            ToolingGet("ApexClass"),
            ToolingGet("ApexTrigger"),
            ToolingGet("ApexPage"),
            ToolingGet("Flow"),
        });

    private static RestApiNode ToolingGet(string type) =>
        Request(
            $"tooling.{type.ToLowerInvariant()}",
            UiText.T("ApiTree_ToolingGetFmt", type),
            "GET",
            $"{BasePath}/tooling/sobjects/{type}/{{id}}",
            parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") });

    private static RestApiNode BuildCompositeGroup() =>
        new("group.composite", UiText.T("ApiTree_GroupComposite"), Children: new[]
        {
            Request(
                "composite.sobjects",
                UiText.T("ApiTree_CompositeSObjects"),
                "POST",
                $"{BasePath}/composite/sobjects",
                Body("""
                    {
                      "allOrNone": false,
                      "records": [
                        { "attributes": { "type": "Account" }, "Name": "Sample" }
                      ]
                    }
                    """)),
            Request(
                "composite.batch",
                UiText.T("ApiTree_CompositeBatch"),
                "POST",
                $"{BasePath}/composite/batch",
                Body("""
                    {
                      "batchRequests": [
                        { "method": "GET", "url": "/services/data/v{version}/limits" }
                      ]
                    }
                    """)),
            Request(
                "composite.graph",
                UiText.T("ApiTree_CompositeGraph"),
                "POST",
                $"{BasePath}/composite/graph",
                Body("""
                    {
                      "graphs": [
                        {
                          "graphId": "graph1",
                          "compositeRequest": [
                            {
                              "method": "POST",
                              "url": "/services/data/v{version}/sobjects/Account",
                              "referenceId": "ref1",
                              "body": { "Name": "Sample" }
                            }
                          ]
                        }
                      ]
                    }
                    """)),
        });

    private static RestApiNode BuildActionsGroup() =>
        new("group.actions", UiText.T("ApiTree_GroupActions"), Children: new[]
        {
            Request("actions.standard", UiText.T("ApiTree_ActionsStandard"), "GET", $"{BasePath}/actions/standard"),
            Request("actions.custom", UiText.T("ApiTree_ActionsCustom"), "GET", $"{BasePath}/actions/custom"),
        });

    private static RestApiNode BuildUiGroup() =>
        new("group.ui", UiText.T("ApiTree_GroupUi"), Children: new[]
        {
            Request(
                "ui.record",
                UiText.T("ApiTree_UiRecord"),
                "GET",
                $"{BasePath}/ui-api/records/{{id}}",
                parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") }),
            Request(
                "ui.layout",
                UiText.T("ApiTree_UiLayout"),
                "GET",
                $"{BasePath}/ui-api/layout/{{objectName}}",
                parameters: new[] { new RestApiParameter("objectName", "ApiTree_ParamObject", "Account") }),
        });

    private static RestApiNode BuildBulkGroup() =>
        new("group.bulk", UiText.T("ApiTree_GroupBulk"), Children: new[]
        {
            Request(
                "bulk.ingest.create",
                UiText.T("ApiTree_BulkIngestCreate"),
                "POST",
                $"{BasePath}/jobs/ingest",
                Body("""
                    {
                      "object": "Account",
                      "operation": "insert",
                      "contentType": "CSV",
                      "lineEnding": "LF"
                    }
                    """)),
            Request("bulk.ingest.list", UiText.T("ApiTree_BulkIngestList"), "GET", $"{BasePath}/jobs/ingest"),
            Request(
                "bulk.ingest.get",
                UiText.T("ApiTree_BulkIngestGet"),
                "GET",
                $"{BasePath}/jobs/ingest/{{id}}",
                parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") }),
            Request("bulk.query.list", UiText.T("ApiTree_BulkQueryList"), "GET", $"{BasePath}/jobs/query"),
        });

    /// <summary>
    /// 組織の SObject フォルダー。件数が多いので頭文字で分け、各文字の子は展開時に生成する。
    /// 一覧は展開時に <paramref name="provider"/> から取得するので、後から読み込まれても反映される。
    /// </summary>
    public static RestApiNode BuildObjectGroup(Func<IReadOnlyList<string>> provider) =>
        new(
            "group.objects",
            UiText.T("ApiTree_GroupObjects"),
            ChildFactory: () => BuildObjectLetterGroups(provider()));

    /// <summary>組織の SObject フォルダー（固定の一覧から作る）。</summary>
    public static RestApiNode BuildObjectGroup(IReadOnlyList<string> objectNames) =>
        BuildObjectGroup(() => objectNames);

    private static IReadOnlyList<RestApiNode> BuildObjectLetterGroups(IReadOnlyList<string> objectNames)
    {
        var groups = objectNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(LetterOf)
            .OrderBy(group => group.Key == "#" ? "ZZZ" : group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var names = group.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
                return new RestApiNode(
                    $"objects.letter.{group.Key}",
                    $"{group.Key} ({names.Count})",
                    ChildFactory: () => names.Select(BuildObjectNode).ToList());
            })
            .ToList();

        return groups;
    }

    private static string LetterOf(string name)
    {
        var first = name[0];
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }

    /// <summary>SObject 1 件のノード（レコード一覧 / 取得 / 作成 / 更新 / 削除 / describe / レイアウト / リストビュー）。</summary>
    public static RestApiNode BuildObjectNode(string objectName) =>
        new(
            $"objects.{objectName}",
            objectName,
            ChildFactory: () => BuildObjectRequests(objectName));

    /// <summary>SObject ノードの子（展開時に生成）。</summary>
    public static IReadOnlyList<RestApiNode> BuildObjectRequests(string objectName) =>
        new[]
        {
            Request(
                $"object.{objectName}.records",
                UiText.T("ApiTree_ObjectRecords"),
                "GET",
                $"{BasePath}/sobjects/{ObjectToken}",
                objectName: objectName),
            Request(
                $"object.{objectName}.get",
                UiText.T("ApiTree_ObjectGet"),
                "GET",
                $"{BasePath}/sobjects/{ObjectToken}/{{id}}",
                parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") },
                objectName: objectName),
            Request(
                $"object.{objectName}.create",
                UiText.T("ApiTree_ObjectCreate"),
                "POST",
                $"{BasePath}/sobjects/{ObjectToken}",
                Body("""
                    {
                      "Name": "Sample"
                    }
                    """),
                objectName: objectName),
            Request(
                $"object.{objectName}.update",
                UiText.T("ApiTree_ObjectUpdate"),
                "PATCH",
                $"{BasePath}/sobjects/{ObjectToken}/{{id}}",
                Body("""
                    {
                      "Name": "Sample"
                    }
                    """),
                parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") },
                objectName: objectName),
            Request(
                $"object.{objectName}.delete",
                UiText.T("ApiTree_ObjectDelete"),
                "DELETE",
                $"{BasePath}/sobjects/{ObjectToken}/{{id}}",
                parameters: new[] { new RestApiParameter("id", "ApiTree_ParamId") },
                objectName: objectName),
            Request(
                $"object.{objectName}.describe",
                UiText.T("ApiTree_ObjectDescribe"),
                "GET",
                $"{BasePath}/sobjects/{ObjectToken}/describe",
                objectName: objectName),
            Request(
                $"object.{objectName}.layouts",
                UiText.T("ApiTree_ObjectLayouts"),
                "GET",
                $"{BasePath}/sobjects/{ObjectToken}/describe/layouts",
                objectName: objectName),
            Request(
                $"object.{objectName}.listviews",
                UiText.T("ApiTree_ObjectListViews"),
                "GET",
                $"{BasePath}/sobjects/{ObjectToken}/listviews",
                objectName: objectName),
        };

    /// <summary>
    /// パスを組み立てる。`{version}` / `{object}` / パラメータの `{name}` を置換し、
    /// パラメータ値は URL エンコードする（未入力のトークンはそのまま残す）。
    /// </summary>
    public static string BuildPath(RestApiNode node, string version, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(node.PathTemplate))
        {
            return string.Empty;
        }

        var path = node.PathTemplate.Replace(VersionToken, version);
        if (!string.IsNullOrWhiteSpace(node.ObjectName))
        {
            path = path.Replace(ObjectToken, node.ObjectName);
        }

        foreach (var parameter in node.Parameters ?? Array.Empty<RestApiParameter>())
        {
            if (values.TryGetValue(parameter.Name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                path = path.Replace("{" + parameter.Name + "}", Uri.EscapeDataString(value.Trim()));
            }
        }

        return path;
    }

    /// <summary>ボディ テンプレートの `{version}` を API バージョンに置換する。</summary>
    public static string? BuildBody(RestApiNode node, string version) =>
        node.BodyTemplate?.Replace(VersionToken, version);

    /// <summary>未入力の必須パラメータ名を返す。</summary>
    public static IReadOnlyList<string> MissingParameters(RestApiNode node, IReadOnlyDictionary<string, string> values) =>
        (node.Parameters ?? Array.Empty<RestApiParameter>())
            .Where(parameter => parameter.Required
                                && (!values.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value)))
            .Select(parameter => parameter.Name)
            .ToList();

    private static RestApiNode Request(
        string id,
        string title,
        string method,
        string path,
        string? body = null,
        IReadOnlyList<RestApiParameter>? parameters = null,
        string? objectName = null) =>
        new(
            id,
            title,
            RestApiNodeKind.Request,
            method,
            path,
            body,
            parameters,
            ObjectName: objectName);

    private static string Body(string template) => template.Replace("\r\n", "\n");
}
