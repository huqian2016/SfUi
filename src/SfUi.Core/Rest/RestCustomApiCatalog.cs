using System.Text.Json;

namespace SfUi.Core;

/// <summary>
/// 組織のカスタム API（<c>/services/data/vXX.X/actions/custom</c> が返す一覧）を
/// REST エクスプローラーのツリーに載せるためのカタログ。
/// </summary>
public static class RestCustomApiCatalog
{
    private const string ActionsCustomPath = "/services/data/v" + RestApiCatalog.VersionToken + "/actions/custom";
    private const string ApexRestPath = "/services/apexrest/{mapping}";

    /// <summary>カスタム API のグループ（アクション / Apex REST）。</summary>
    public static RestApiNode BuildGroup(IReadOnlyList<string> actionNames) =>
        new(
            "group.custom",
            UiText.T("ApiTree_GroupCustom"),
            Children: new[] { BuildActionsFolder(actionNames), BuildApexRestFolder() });

    /// <summary>
    /// <c>/actions/custom</c> の応答（API 名 → URL のオブジェクト）から API 名を取り出す。
    /// 想定外の形（配列 / actions 配列を持つ標準アクションの応答 など）では空を返す。
    /// </summary>
    public static IReadOnlyList<string> ParseNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            // /actions/standard は { "actions": [...] } を返すのでカスタム API ではない
            if (document.RootElement.TryGetProperty("actions", out _))
            {
                return Array.Empty<string>();
            }

            return document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>アクション（<c>POST /actions/custom/{name}</c>）のフォルダー。</summary>
    private static RestApiNode BuildActionsFolder(IReadOnlyList<string> actionNames)
    {
        var children = actionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new RestApiNode(
                $"custom.action.{name}",
                name,
                RestApiNodeKind.Request,
                "POST",
                $"{ActionsCustomPath}/{Uri.EscapeDataString(name)}",
                BodyTemplate: "[\n  {}\n]"))
            .Cast<RestApiNode>()
            .ToList();

        if (children.Count == 0)
        {
            children.Add(new RestApiNode("custom.actions.empty", UiText.T("ApiTree_NoCustomApis")));
        }

        return new RestApiNode("custom.actions", UiText.T("ApiTree_CustomActions"), Children: children);
    }

    /// <summary>Apex REST（<c>@RestResource(urlMapping='…')</c>）の呼び出しテンプレート。</summary>
    private static RestApiNode BuildApexRestFolder()
    {
        var mapping = new RestApiParameter("mapping", "ApiTree_ParamMapping", Escape: false);

        return new RestApiNode(
            "custom.apexrest",
            UiText.T("ApiTree_ApexRest"),
            Children: new[]
            {
                new RestApiNode(
                    "custom.apexrest.get",
                    UiText.T("ApiTree_ApexRestRequest"),
                    RestApiNodeKind.Request,
                    "GET",
                    ApexRestPath,
                    Parameters: new[] { mapping }),
                new RestApiNode(
                    "custom.apexrest.post",
                    UiText.T("ApiTree_ApexRestRequest"),
                    RestApiNodeKind.Request,
                    "POST",
                    ApexRestPath,
                    "{\n  \"Name\": \"Sample\"\n}",
                    new[] { mapping }),
            });
    }
}
