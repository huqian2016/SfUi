namespace SfUi.Core;

/// <summary>ER 図のノード / 項目に表示する名前。</summary>
public enum ErdNameStyle
{
    /// <summary>ラベル（例: 取引先）。</summary>
    Label,

    /// <summary>API 名（例: Account）。</summary>
    ApiName,

    /// <summary>ラベル＋API 名（例: 取引先 (Account)）。</summary>
    LabelAndApiName,
}

/// <summary>ER 図のテキスト形式。</summary>
public enum ErdDiagramFormat
{
    Mermaid,
    Dbml,
    Drawio,
    PlantUml,
    Dot,
}

/// <summary>ER 図の 1 項目。</summary>
public sealed class ErdField
{
    public required string ApiName { get; init; }

    public string Label { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public bool IsPrimaryKey { get; init; }

    public bool IsRequired { get; init; }

    public bool IsUnique { get; init; }

    /// <summary>参照先 sObject（多態参照は複数）。</summary>
    public IReadOnlyList<string> ReferenceTo { get; init; } = Array.Empty<string>();

    public string? RelationshipName { get; init; }

    public bool IsMasterDetail { get; init; }

    public bool IsLookup => ReferenceTo.Count > 0;

    public string DisplayName(ErdNameStyle style) => ErdNames.Format(ApiName, Label, style);
}

/// <summary>ER 図の 1 エンティティ（sObject）。</summary>
public sealed class ErdEntity
{
    public required string ApiName { get; init; }

    public string Label { get; init; } = string.Empty;

    public bool IsCustom { get; init; }

    public IReadOnlyList<ErdField> Fields { get; init; } = Array.Empty<ErdField>();

    public string DisplayName(ErdNameStyle style) => ErdNames.Format(ApiName, Label, style);
}

/// <summary>ER 図の 1 リレーション（子 → 親）。</summary>
public sealed class ErdRelationship
{
    /// <summary>参照項目を持つ側（N 側）。</summary>
    public required string ChildApiName { get; init; }

    /// <summary>参照される側（1 側）。</summary>
    public required string ParentApiName { get; init; }

    public required string FieldApiName { get; init; }

    public string FieldLabel { get; init; } = string.Empty;

    public bool IsMasterDetail { get; init; }

    public bool IsRequired { get; init; }
}

/// <summary>ER 図 1 件分のデータ。各形式のライターはこれだけを受け取る。</summary>
public sealed class ErdDocument
{
    public required string Title { get; init; }

    public string? Org { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public ErdNameStyle NameStyle { get; init; } = ErdNameStyle.LabelAndApiName;

    public IReadOnlyList<ErdEntity> Entities { get; init; } = Array.Empty<ErdEntity>();

    public IReadOnlyList<ErdRelationship> Relationships { get; init; } = Array.Empty<ErdRelationship>();

    public string DisplayNameOf(string apiName)
    {
        var entity = Entities.FirstOrDefault(item => string.Equals(item.ApiName, apiName, StringComparison.OrdinalIgnoreCase));
        return entity is null ? apiName : entity.DisplayName(NameStyle);
    }
}

/// <summary>ER 図の表示名を組み立てるヘルパー。</summary>
public static class ErdNames
{
    public static string Format(string apiName, string label, ErdNameStyle style)
    {
        var hasLabel = !string.IsNullOrWhiteSpace(label) && !string.Equals(label, apiName, StringComparison.OrdinalIgnoreCase);
        return style switch
        {
            ErdNameStyle.ApiName => apiName,
            ErdNameStyle.Label => hasLabel ? label : apiName,
            _ => hasLabel ? label + " (" + apiName + ")" : apiName,
        };
    }

    /// <summary>Mermaid / DOT / PlantUML / draw.io の id に使える形へ整形する。</summary>
    public static string Identifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        var text = builder.ToString();
        if (text.Length == 0)
        {
            return "Object";
        }

        return char.IsDigit(text[0]) ? "E_" + text : text;
    }
}
