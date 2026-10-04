using System.Text;
using System.Xml.Linq;

namespace SfUi.Core;

/// <summary>SOAP undelete の 1 件分の結果。</summary>
public sealed record SoapUndeleteResult(string Id, bool Success, string? Error);

/// <summary>
/// SOAP API（Partner）の最小クライアント。<c>undelete</c>（ごみ箱からの復元 = Id 維持）のみサポートする。
/// </summary>
public sealed class SalesforceSoapClient
{
    private const int BatchSize = 200;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };
    private static readonly XNamespace PartnerNs = "urn:partner.soap.sforce.com";

    private readonly AppLog _log;

    public SalesforceSoapClient(AppLog log)
    {
        _log = log;
    }

    /// <summary>削除済みレコードを復元する（Id 維持・最大 200 件 / 呼び出し）。結果は要求順。</summary>
    public async Task<IReadOnlyList<SoapUndeleteResult>> UndeleteAsync(
        string instanceUrl,
        string accessToken,
        string apiVersion,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SoapUndeleteResult>();
        if (ids.Count == 0)
        {
            return results;
        }

        var url = instanceUrl.TrimEnd('/') + $"/services/Soap/u/{apiVersion}";
        for (var offset = 0; offset < ids.Count; offset += BatchSize)
        {
            var chunk = ids.Skip(offset).Take(BatchSize).ToList();
            try
            {
                var envelope = BuildUndeleteEnvelope(chunk, accessToken);
                using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var parsed = ParseUndeleteResponse(xml);
                if (parsed.Count == 0)
                {
                    _log.Warn($"SOAP undelete: 応答を解析できません (HTTP {(int)response.StatusCode}): {Truncate(xml, 400)}");
                }
                if (parsed.Count != chunk.Count)
                {
                    // 応答が欠けている場合は失敗として揃える
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        var result = i < parsed.Count ? parsed[i] : new SoapUndeleteResult(string.Empty, false, "no response");
                        results.Add(string.IsNullOrEmpty(result.Id) ? result with { Id = chunk[i] } : result);
                    }
                }
                else
                {
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        var result = parsed[i];
                        results.Add(string.IsNullOrEmpty(result.Id) ? result with { Id = chunk[i] } : result);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"SOAP undelete に失敗: {ex.Message}");
                foreach (var id in chunk)
                {
                    results.Add(new SoapUndeleteResult(id, false, ex.Message));
                }
            }
        }

        return results;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>undelete の SOAP エンベロープを組み立てる。</summary>
    public static string BuildUndeleteEnvelope(IReadOnlyList<string> ids, string sessionId)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:urn=\"urn:partner.soap.sforce.com\">");
        sb.Append("<soapenv:Header><urn:SessionHeader><urn:sessionId>").Append(Escape(sessionId)).Append("</urn:sessionId></urn:SessionHeader></soapenv:Header>");
        sb.Append("<soapenv:Body><urn:undelete>");
        foreach (var id in ids)
        {
            sb.Append("<urn:ids>").Append(Escape(id)).Append("</urn:ids>");
        }

        sb.Append("</urn:undelete></soapenv:Body></soapenv:Envelope>");
        return sb.ToString();
    }

    /// <summary>undelete 応答を解析する（要求順・id は成功時のみ設定されることがある）。</summary>
    public static IReadOnlyList<SoapUndeleteResult> ParseUndeleteResponse(string xml)
    {
        var results = new List<SoapUndeleteResult>();
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return results;
        }

        var response = document.Descendants(PartnerNs + "undeleteResponse").FirstOrDefault();
        if (response is null)
        {
            return results;
        }

        foreach (var result in response.Elements(PartnerNs + "result"))
        {
            var id = result.Element(PartnerNs + "id")?.Value ?? string.Empty;
            var success = string.Equals(result.Element(PartnerNs + "success")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var error = result.Elements(PartnerNs + "errors")
                .Select(e => e.Element(PartnerNs + "message")?.Value)
                .FirstOrDefault(m => !string.IsNullOrEmpty(m));
            results.Add(new SoapUndeleteResult(id, success, success ? null : error ?? "undelete failed"));
        }

        return results;
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
