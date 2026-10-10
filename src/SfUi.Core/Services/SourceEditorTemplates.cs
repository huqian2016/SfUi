namespace SfUi.Core;

/// <summary>
/// ソース エディタの新規作成テンプレート（Phase 3）。
/// メタデータ（-meta.xml）は対象外: Apex / VF は反映時に自動生成され、LWC は反映時に
/// <c>&lt;name&gt;.js-meta.xml</c> が無ければサービスが生成する。
/// </summary>
public static class SourceEditorTemplates
{
    /// <summary>LWC バンドル名の上限（Salesforce のフォルダー名制限）。</summary>
    public const int LwcNameMaxLength = 40;

    /// <summary>種類ごとの初期ファイルを作る（LWC は js / html / css の 3 ファイル）。</summary>
    public static IReadOnlyList<SourceFileInfo> BuildFiles(SourceMemberKind kind, string name, string? sobjectName = null)
    {
        switch (kind)
        {
            case SourceMemberKind.ApexClass:
                return new[] { new SourceFileInfo(name + ".cls", "C#", ClassTemplate(name)) };
            case SourceMemberKind.ApexTrigger:
                if (string.IsNullOrWhiteSpace(sobjectName))
                {
                    throw new ArgumentException("対象オブジェクト名が必要です。", nameof(sobjectName));
                }

                return new[] { new SourceFileInfo(name + ".trigger", "C#", TriggerTemplate(name, sobjectName.Trim())) };
            case SourceMemberKind.VisualforcePage:
                return new[] { new SourceFileInfo(name + ".page", "XML", PageTemplate()) };
            case SourceMemberKind.LightningComponentBundle:
                return new[]
                {
                    new SourceFileInfo(name + ".js", "JavaScript", LwcJsTemplate(name)),
                    new SourceFileInfo(name + ".html", "HTML", LwcHtmlTemplate()),
                    new SourceFileInfo(name + ".css", "CSS", LwcCssTemplate()),
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>Apex クラスのテンプレート（with sharing + 既定コンストラクター）。</summary>
    public static string ClassTemplate(string name)
        => $"public with sharing class {name} {{\n    public {name}() {{\n    }}\n}}\n";

    /// <summary>Apex トリガーのテンプレート（before insert。対象オブジェクトを差し込む）。</summary>
    public static string TriggerTemplate(string name, string sobjectName)
        => $"trigger {name} on {sobjectName} (before insert) {{\n}}\n";

    /// <summary>Visualforce ページのテンプレート。</summary>
    public static string PageTemplate()
        => "<apex:page>\n</apex:page>\n";

    /// <summary>LWC の JS テンプレート（クラス名はフォルダー名の先頭を大文字化）。</summary>
    public static string LwcJsTemplate(string name)
        => $"import {{ LightningElement }} from 'lwc';\n\nexport default class {PascalCase(name)} extends LightningElement {{\n}}\n";

    /// <summary>LWC の HTML テンプレート。</summary>
    public static string LwcHtmlTemplate()
        => "<template>\n</template>\n";

    /// <summary>LWC の CSS テンプレート。</summary>
    public static string LwcCssTemplate()
        => ":host {\n    display: block;\n}\n";

    /// <summary>camelCase のフォルダー名を JS クラス名（先頭大文字）に変換する。</summary>
    public static string PascalCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
