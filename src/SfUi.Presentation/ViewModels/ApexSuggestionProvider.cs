using SfUi.Core;

namespace SfUi.App.ViewModels;

/// <summary>
/// Apex 補完候補の計算（匿名Apex タブとソース エディタで共有。Phase 5）。
/// コンテキスト解析は <see cref="ApexCompletionParser"/>、組織データは describe / メタデータ サービスから取得する。
/// </summary>
public sealed class ApexSuggestionProvider
{
    private readonly SObjectDescribeService _describes;
    private readonly OrgMetadataService _metadata;

    public ApexSuggestionProvider(SObjectDescribeService describes, OrgMetadataService metadata)
    {
        _describes = describes;
        _metadata = metadata;
    }

    /// <summary>
    /// 解析済みコンテキストから候補を計算する。null = 候補なし（呼び出し側は表示を消す）。
    /// org が null でもスニペット等の組織非依存候補は返す。
    /// </summary>
    public async Task<(string Header, IReadOnlyList<SoqlCompletionItem> Items)?> GetAsync(
        string? org, string text, int caret, ApexCompletionContext context, CancellationToken cancellationToken)
    {
        switch (context.Kind)
        {
            case ApexCompletionKind.Soql when context.Soql is { } soql:
            {
                if (string.IsNullOrWhiteSpace(org)
                    || soql.InsideString
                    || soql.Clause is SoqlClause.None or SoqlClause.Other or SoqlClause.Limit or SoqlClause.Offset)
                {
                    return null;
                }

                if (soql.Clause == SoqlClause.From)
                {
                    var objects = await _describes.ListObjectsAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
                    return (UiText.T("Soql_SuggestObjects"), SoqlCompletionEngine.ObjectItems(objects, soql.Prefix));
                }

                var target = await SoqlCompletionEngine.ResolveTargetAsync(_describes, org!, soql, cancellationToken).ConfigureAwait(true);
                if (target is null)
                {
                    return null;
                }

                var describe = await _describes.DescribeAsync(org!, target, cancellationToken: cancellationToken).ConfigureAwait(true);
                var includeFunctions = soql.IncludeFunctions && soql.Path.Count == 0;
                return (UiText.T("Soql_SuggestFieldsFmt", target), SoqlCompletionEngine.FieldItems(describe, soql.Prefix, includeFunctions));
            }

            case ApexCompletionKind.Objects:
            {
                if (string.IsNullOrWhiteSpace(org))
                {
                    return null;
                }

                var objectList = await _describes.ListObjectsAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
                var objectItems = new List<SoqlCompletionItem>(SoqlCompletionEngine.ObjectItems(objectList, context.Prefix));
                var metadataTypes = await _metadata.ListCustomMetadataTypesAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
                objectItems.AddRange(ApexCompletionEngine.NameItems(metadataTypes, context.Prefix, "Custom metadata"));
                var classNames = await _metadata.ListApexClassesAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
                objectItems.AddRange(ApexCompletionEngine.NameItems(classNames, context.Prefix, "Apex class"));
                return (UiText.T("Soql_SuggestObjects"), objectItems.Take(ApexCompletionEngine.MaxItems).ToList());
            }

            case ApexCompletionKind.Variables:
            {
                if (string.IsNullOrWhiteSpace(org))
                {
                    return null;
                }

                var items = await ResolveVariableItemsAsync(org!, text, caret, context.Prefix, cancellationToken).ConfigureAwait(true);
                return (UiText.T("Apex_SuggestVariables"), items);
            }

            case ApexCompletionKind.Members:
                return await ResolveMemberItemsAsync(org, text, caret, context, cancellationToken).ConfigureAwait(true);

            default:
            {
                var snippetItems = new List<SoqlCompletionItem>(ApexCompletionEngine.Snippets(context.Prefix));
                if (!string.IsNullOrWhiteSpace(org))
                {
                    var orgClasses = await _metadata.ListApexClassesAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
                    snippetItems.AddRange(ApexCompletionEngine.NameItems(orgClasses, context.Prefix, "Apex class"));
                }

                return (UiText.T("Apex_SuggestSnippets"), snippetItems.Take(ApexCompletionEngine.MaxItems).ToList());
            }
        }
    }

    /// <summary>DML（insert / update …）の直後: 宣言済みの sObject / コレクション変数を候補にする。</summary>
    private async Task<IReadOnlyList<SoqlCompletionItem>> ResolveVariableItemsAsync(
        string org, string text, int caret, string prefix, CancellationToken cancellationToken)
    {
        var objects = await _describes.ListObjectsAsync(org, cancellationToken: cancellationToken).ConfigureAwait(true);
        var names = objects.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declarations = ApexCompletionParser.ScanDeclarations(text[..caret]);

        var items = new List<SoqlCompletionItem>();
        foreach (var (name, declaredType) in declarations.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isSObject = names.Contains(declaredType)
                || (ApexCompletionEngine.TryParseCollectionType(declaredType, out _, out var element) && names.Contains(element));
            if (isSObject)
            {
                items.Add(new SoqlCompletionItem(name, declaredType));
            }
        }

        return items.Take(ApexCompletionEngine.MaxItems).ToList();
    }

    /// <summary>メンバーアクセス（X. / a.Owner.）: 静的クラス / コレクション変数 / sObject 変数の候補を解決する。</summary>
    private async Task<(string Header, IReadOnlyList<SoqlCompletionItem> Items)?> ResolveMemberItemsAsync(
        string? org, string text, int caret, ApexCompletionContext context, CancellationToken cancellationToken)
    {
        var root = context.Root ?? string.Empty;
        var parentPath = context.Path ?? Array.Empty<string>();

        // System.Label.<名前> はカスタムラベル候補
        if (root.Equals("System", StringComparison.OrdinalIgnoreCase)
            && parentPath.Count == 1
            && parentPath[0].Equals("Label", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(org))
            {
                return null;
            }

            var labels = await _metadata.ListCustomLabelsAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
            var labelItems = labels
                .Where(p => context.Prefix.Length == 0 || p.Name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .Take(ApexCompletionEngine.MaxItems)
                .Select(p => new SoqlCompletionItem(p.Name, string.IsNullOrEmpty(p.Label) ? "Label" : p.Label))
                .ToList();
            return (UiText.T("Apex_SuggestLabels"), labelItems);
        }

        // 1) 静的クラス（System / Database …）
        if (ApexCompletionEngine.HasStaticClass(root))
        {
            return parentPath.Count == 0
                ? (UiText.T("Apex_SuggestMembersFmt", root), ApexCompletionEngine.StaticMembers(root, context.Prefix))
                : null;
        }

        // 2) 宣言から型を推定した変数
        var declarations = ApexCompletionParser.ScanDeclarations(text[..caret]);
        if (!declarations.TryGetValue(root, out var declaredType))
        {
            return null;
        }

        if (ApexCompletionEngine.TryParseCollectionType(declaredType, out var kind, out _))
        {
            return parentPath.Count == 0
                ? (UiText.T("Apex_SuggestMembersFmt", kind), ApexCompletionEngine.CollectionMethods(kind, context.Prefix))
                : null;
        }

        if (string.IsNullOrWhiteSpace(org))
        {
            return null;
        }

        var objects = await _describes.ListObjectsAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
        var isKnownType = objects.Any(o => string.Equals(o.Name, declaredType, StringComparison.OrdinalIgnoreCase));
        if (!isKnownType)
        {
            var metadataTypes = await _metadata.ListCustomMetadataTypesAsync(org!, cancellationToken: cancellationToken).ConfigureAwait(true);
            isKnownType = metadataTypes.Any(n => string.Equals(n, declaredType, StringComparison.OrdinalIgnoreCase));
        }

        if (!isKnownType)
        {
            return null;   // sObject / カスタムメタデータ型以外は対象外
        }

        // 参照の連鎖（a.Owner.）を describe で解決する
        var target = declaredType;
        foreach (var segment in parentPath)
        {
            var describe = await _describes.DescribeAsync(org!, target, cancellationToken: cancellationToken).ConfigureAwait(true);
            var relationship = SoqlCompletionEngine.ResolveRelationship(describe, segment);
            if (relationship is null)
            {
                return null;
            }

            target = relationship;
        }

        var targetDescribe = await _describes.DescribeAsync(org!, target, cancellationToken: cancellationToken).ConfigureAwait(true);
        var items = new List<SoqlCompletionItem>();
        items.AddRange(SoqlCompletionEngine.FieldItems(targetDescribe, context.Prefix, includeFunctions: false));
        items.AddRange(ApexCompletionEngine.SObjectMethods(context.Prefix));
        return (UiText.T("Soql_SuggestFieldsFmt", target), items);
    }
}
