using System.Text;

namespace SfUi.Core;

/// <summary>Named Credential プリセットの種類。</summary>
public enum ScratchNamedCredentialPreset
{
    Sap,
    ServiceNow,
    MuleSoft,
    Generic,
}

/// <summary>
/// Named Credential のプリセット生成。
/// 認証情報はリポジトリに保存しない方針のため、生成するのは
/// 「NoAuthentication + 匿名」のスケルトンのみ（実際の資格情報は組織の Setup で設定する）。
/// </summary>
public static class ScratchNamedCredentialPresets
{
    public const string PackageRelativeDirectory = "force-app/main/default/namedCredentials";

    public static string Endpoint(ScratchNamedCredentialPreset preset) => preset switch
    {
        ScratchNamedCredentialPreset.Sap => "https://your-sap-host/sap/opu/odata/sap/",
        ScratchNamedCredentialPreset.ServiceNow => "https://your-instance.service-now.com/",
        ScratchNamedCredentialPreset.MuleSoft => "https://your-app.cloudhub.io/",
        _ => "https://api.example.com/",
    };

    /// <summary>Named Credential の API 名として使えるか（英字始まり・英数字と _・40 文字以内）。</summary>
    public static bool IsValidApiName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 40
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>Named Credential のメタデータ XML を組み立てる。</summary>
    public static string BuildXml(string apiName, ScratchNamedCredentialPreset preset)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<NamedCredential xmlns=\"http://soap.sforce.com/2006/04/metadata\">");
        builder.AppendLine("    <!-- SfUi: credentials are intentionally NOT stored in source. Configure the protocol / credentials in Setup > Named Credentials. -->");
        builder.AppendLine($"    <endpoint>{Endpoint(preset)}</endpoint>");
        builder.AppendLine($"    <label>{apiName}</label>");
        builder.AppendLine("    <principalType>Anonymous</principalType>");
        builder.AppendLine("    <protocol>NoAuthentication</protocol>");
        builder.AppendLine("</NamedCredential>");
        return builder.ToString();
    }

    /// <summary>
    /// プロファイルの force-app にプリセットを書き込む。既存ファイルがある場合は上書きしない。
    /// 戻り値は作成したファイルのフルパス（既存の場合は null）。
    /// </summary>
    public static string? Write(string profileDirectory, string apiName, ScratchNamedCredentialPreset preset)
    {
        if (!IsValidApiName(apiName))
        {
            throw new ArgumentException(UiText.T("Scratch_InvalidApiName"), nameof(apiName));
        }

        var directory = Path.Combine(
            profileDirectory,
            PackageRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{apiName}.namedCredential-meta.xml");
        if (File.Exists(path))
        {
            return null;
        }

        File.WriteAllText(path, BuildXml(apiName, preset));
        return path;
    }
}
