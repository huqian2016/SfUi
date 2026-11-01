using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SfUi.Core;

/// <summary>
/// 接続アプリ（ConnectedApp / ExternalClientApplication）のメタデータ XML を
/// 生成・解析する純関数群。sf を呼ばないため単体テストできる。
/// </summary>
public static class ConnectedAppXml
{
    public static readonly XNamespace Ns = "http://soap.sforce.com/2006/04/metadata";

    public const string ConnectedAppDirectory = "connectedApps";
    public const string EcaDirectory = "externalClientApps";
    public const string EcaGlobalOauthDirectory = "extlClntAppGlobalOauthSets";
    public const string EcaOauthSettingsDirectory = "extlClntAppOauthSettings";
    public const string EcaOauthPoliciesDirectory = "extlClntAppOauthPolicies";
    public const string EcaPoliciesDirectory = "extlClntAppPolicies";

    public const string ConnectedAppSuffix = ".connectedApp-meta.xml";
    public const string EcaSuffix = ".eca-meta.xml";
    public const string EcaGlobalOauthSuffix = ".ecaGlblOauth-meta.xml";
    public const string EcaOauthSettingsSuffix = ".ecaOauth-meta.xml";
    public const string EcaOauthPoliciesSuffix = ".ecaOauthPlcy-meta.xml";
    public const string EcaPoliciesSuffix = ".ecaPlcy-meta.xml";

    // ---------------------------------------------------------------- 生成

    /// <summary>新規作成用のメタデータ ファイル一式を組み立てる。</summary>
    public static IReadOnlyList<ConnectedAppFile> BuildFiles(ConnectedAppDraft draft)
    {
        return string.Equals(draft.Kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? BuildEcaFiles(draft)
            : BuildConnectedAppFiles(draft);
    }

    /// <summary>既存ドキュメントへ編集内容を反映したファイル一式を返す（未知の項目は保持）。</summary>
    public static IReadOnlyList<ConnectedAppFile> ApplyDraft(ConnectedAppDocument document, ConnectedAppDraft draft)
    {
        if (!string.IsNullOrWhiteSpace(draft.RawXml))
        {
            // XML タブで直接編集された場合はメイン コンポーネントだけ差し替える
            var files = document.Files.ToList();
            var index = files.FindIndex(f => IsMainFile(document.Kind, f.RelativePath));
            if (index >= 0)
            {
                files[index] = files[index] with { Content = EnsureXmlDeclaration(draft.RawXml!) };
            }
            else
            {
                files.Add(new ConnectedAppFile(MainRelativePath(document.Kind, document.Name), EnsureXmlDeclaration(draft.RawXml!)));
            }

            return files;
        }

        return string.Equals(document.Kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? ApplyEcaDraft(document, draft)
            : ApplyConnectedAppDraft(document, draft);
    }

    private static IReadOnlyList<ConnectedAppFile> BuildConnectedAppFiles(ConnectedAppDraft draft)
    {
        var root = new XElement(Ns + "ConnectedApp");
        Set(root, "contactEmail", draft.ContactEmail);
        Set(root, "description", draft.Description);
        Set(root, "infoUrl", draft.InfoUrl);
        Set(root, "label", draft.Label);
        Set(root, "mobileStartUrl", draft.MobileStartUrl);
        Set(root, "startUrl", draft.StartUrl);

        var hasOauth = draft.Scopes.Count > 0 || !string.IsNullOrWhiteSpace(draft.CallbackUrl);
        if (hasOauth)
        {
            var oauth = new XElement(Ns + "oauthConfig");
            Set(oauth, "callbackUrl", NormalizeCallback(draft.CallbackUrl));
            Set(oauth, "consumerKey", string.IsNullOrWhiteSpace(draft.ConsumerKey) ? null : draft.ConsumerKey.Trim());
            Set(oauth, "isAdminApproved", draft.IsAdminApproved ? "true" : "false");
            foreach (var scope in draft.Scopes)
            {
                oauth.Add(new XElement(Ns + "scopes", scope));
            }

            root.Add(oauth);

            var policy = new XElement(Ns + "oauthPolicy");
            Set(policy, "ipRelaxation", string.IsNullOrWhiteSpace(draft.IpRelaxation) ? "ENFORCE" : draft.IpRelaxation);
            Set(policy, "refreshTokenPolicy", string.IsNullOrWhiteSpace(draft.RefreshTokenPolicy) ? "infinite" : draft.RefreshTokenPolicy);
            root.Add(policy);
        }

        return new[] { new ConnectedAppFile(MainRelativePath(ConnectedAppKinds.ConnectedApp, draft.Name), Serialize(root)) };
    }

    private static IReadOnlyList<ConnectedAppFile> BuildEcaFiles(ConnectedAppDraft draft)
    {
        var files = new List<ConnectedAppFile>
        {
            new(MainRelativePath(ConnectedAppKinds.ExternalClientApp, draft.Name), Serialize(BuildEcaRoot(draft))),
        };

        if (draft.Scopes.Count > 0 || !string.IsNullOrWhiteSpace(draft.CallbackUrl))
        {
            files.Add(new ConnectedAppFile(
                $"{EcaGlobalOauthDirectory}/{draft.Name}{EcaGlobalOauthSuffix}",
                Serialize(BuildEcaGlobalOauth(draft))));
            files.Add(new ConnectedAppFile(
                $"{EcaOauthSettingsDirectory}/{draft.Name}{EcaOauthSettingsSuffix}",
                Serialize(BuildEcaOauthSettings(draft))));
            files.Add(new ConnectedAppFile(
                $"{EcaOauthPoliciesDirectory}/{draft.Name}_oauth{EcaOauthPoliciesSuffix}",
                Serialize(BuildEcaOauthPolicies(draft))));
            files.Add(new ConnectedAppFile(
                $"{EcaPoliciesDirectory}/{draft.Name}_defaultPolicy{EcaPoliciesSuffix}",
                Serialize(BuildEcaPolicies(draft))));
        }

        return files;
    }

    private static XElement BuildEcaRoot(ConnectedAppDraft draft)
    {
        var root = new XElement(Ns + "ExternalClientApplication");
        Set(root, "contactEmail", draft.ContactEmail);
        Set(root, "description", draft.Description);
        Set(root, "distributionState", "Local");
        Set(root, "infoUrl", draft.InfoUrl);
        Set(root, "isProtected", "false");
        Set(root, "label", draft.Label);
        return root;
    }

    private static XElement BuildEcaGlobalOauth(ConnectedAppDraft draft)
    {
        var oauth = new XElement(Ns + "ExtlClntAppGlobalOauthSettings");
        Set(oauth, "callbackUrl", NormalizeCallback(draft.CallbackUrl));
        Set(oauth, "consumerKey", string.IsNullOrWhiteSpace(draft.ConsumerKey) ? null : draft.ConsumerKey.Trim());
        Set(oauth, "externalClientApplication", draft.Name);
        Set(oauth, "isConsumerSecretOptional", "false");
        Set(oauth, "isIntrospectAllTokens", "false");
        Set(oauth, "isPkceRequired", "true");
        Set(oauth, "isSecretRequiredForRefreshToken", "true");
        Set(oauth, "label", draft.Name);
        return oauth;
    }

    private static XElement BuildEcaOauthSettings(ConnectedAppDraft draft)
    {
        var oauth = new XElement(Ns + "ExtlClntAppOauthSettings");
        Set(oauth, "commaSeparatedOauthScopes", ConnectedAppOauthScopes.ToCommaSeparated(draft.Scopes));
        Set(oauth, "externalClientApplication", draft.Name);
        Set(oauth, "label", draft.Name);
        return oauth;
    }

    private static XElement BuildEcaOauthPolicies(ConnectedAppDraft draft)
    {
        var policy = new XElement(Ns + "ExtlClntAppOauthConfigurablePolicies");
        Set(policy, "externalClientApplication", draft.Name);
        Set(policy, "ipRelaxationPolicyType", MapIpRelaxation(draft.IpRelaxation));
        Set(policy, "label", $"{draft.Name}_oauth");
        Set(policy, "permittedUsersPolicyType", draft.IsAdminApproved ? "AdminApprovedPreAuthorized" : "AllSelfAuthorized");
        Set(policy, "refreshTokenPolicyType", MapRefreshTokenPolicy(draft.RefreshTokenPolicy));
        return policy;
    }

    private static XElement BuildEcaPolicies(ConnectedAppDraft draft)
    {
        var policy = new XElement(Ns + "ExtlClntAppConfigurablePolicies");
        Set(policy, "externalClientApplication", draft.Name);
        Set(policy, "isEnabled", "true");
        Set(policy, "isOauthPluginEnabled", "true");
        Set(policy, "label", $"{draft.Name}_defaultPolicy");
        Set(policy, "startPage", "None");
        return policy;
    }

    private static IReadOnlyList<ConnectedAppFile> ApplyConnectedAppDraft(ConnectedAppDocument document, ConnectedAppDraft draft)
    {
        var files = document.Files.ToList();
        var index = files.FindIndex(f => IsMainFile(ConnectedAppKinds.ConnectedApp, f.RelativePath));
        var root = index >= 0 ? Parse(files[index].Content) : new XElement(Ns + "ConnectedApp");

        Set(root, "contactEmail", draft.ContactEmail);
        Set(root, "description", draft.Description);
        Set(root, "infoUrl", draft.InfoUrl);
        Set(root, "label", draft.Label);
        Set(root, "mobileStartUrl", draft.MobileStartUrl);
        Set(root, "startUrl", draft.StartUrl);

        var hasOauth = draft.Scopes.Count > 0 || !string.IsNullOrWhiteSpace(draft.CallbackUrl);
        var oauth = Child(root, "oauthConfig");
        if (hasOauth)
        {
            oauth ??= Add(root, "oauthConfig");
            Set(oauth, "callbackUrl", NormalizeCallback(draft.CallbackUrl));
            ReplaceScopes(oauth, draft.Scopes);
            Set(oauth, "isAdminApproved", draft.IsAdminApproved ? "true" : "false");

            var policy = Child(root, "oauthPolicy");
            if (policy is not null)
            {
                Set(policy, "ipRelaxation", string.IsNullOrWhiteSpace(draft.IpRelaxation) ? "ENFORCE" : draft.IpRelaxation);
                Set(policy, "refreshTokenPolicy", string.IsNullOrWhiteSpace(draft.RefreshTokenPolicy) ? "infinite" : draft.RefreshTokenPolicy);
            }
        }
        else if (oauth is not null)
        {
            Set(oauth, "callbackUrl", null);
            ReplaceScopes(oauth, Array.Empty<string>());
        }

        var content = Serialize(root);
        if (index >= 0)
        {
            files[index] = files[index] with { Content = content };
        }
        else
        {
            files.Add(new ConnectedAppFile(MainRelativePath(ConnectedAppKinds.ConnectedApp, document.Name), content));
        }

        return files;
    }

    private static IReadOnlyList<ConnectedAppFile> ApplyEcaDraft(ConnectedAppDocument document, ConnectedAppDraft draft)
    {
        var files = document.Files.ToList();

        UpdateOrAdd(files, EcaDirectory, document.Name, EcaSuffix, root =>
        {
            Set(root, "contactEmail", draft.ContactEmail);
            Set(root, "description", draft.Description);
            Set(root, "infoUrl", draft.InfoUrl);
            Set(root, "label", draft.Label);
        });

        if (draft.Scopes.Count > 0 || !string.IsNullOrWhiteSpace(draft.CallbackUrl))
        {
            UpdateOrAdd(files, EcaGlobalOauthDirectory, document.Name, EcaGlobalOauthSuffix, root =>
            {
                Set(root, "callbackUrl", NormalizeCallback(draft.CallbackUrl));
                Set(root, "externalClientApplication", document.Name);
                Set(root, "label", document.Name);
            });

            UpdateOrAdd(files, EcaOauthSettingsDirectory, document.Name, EcaOauthSettingsSuffix, root =>
            {
                Set(root, "commaSeparatedOauthScopes", ConnectedAppOauthScopes.ToCommaSeparated(draft.Scopes));
                Set(root, "externalClientApplication", document.Name);
                Set(root, "label", document.Name);
            });

            UpdateOrAdd(files, EcaOauthPoliciesDirectory, $"{document.Name}_oauth", EcaOauthPoliciesSuffix, root =>
            {
                Set(root, "externalClientApplication", document.Name);
                Set(root, "ipRelaxationPolicyType", MapIpRelaxation(draft.IpRelaxation));
                Set(root, "permittedUsersPolicyType", draft.IsAdminApproved ? "AdminApprovedPreAuthorized" : "AllSelfAuthorized");
                Set(root, "refreshTokenPolicyType", MapRefreshTokenPolicy(draft.RefreshTokenPolicy));
            });

            UpdateOrAdd(files, EcaPoliciesDirectory, $"{document.Name}_defaultPolicy", EcaPoliciesSuffix, root =>
            {
                Set(root, "externalClientApplication", document.Name);
                Set(root, "isOauthPluginEnabled", "true");
            });
        }

        return files;
    }

    private static void UpdateOrAdd(
        List<ConnectedAppFile> files,
        string directory,
        string name,
        string suffix,
        Action<XElement> update)
    {
        var index = files.FindIndex(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), directory, StringComparison.OrdinalIgnoreCase)
                                         && f.RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var root = index >= 0 ? Parse(files[index].Content) : new XElement(Ns + ElementNameFor(suffix));
        update(root);
        var content = Serialize(root);
        var path = $"{directory}/{name}{suffix}";
        if (index >= 0)
        {
            files[index] = files[index] with { Content = content };
        }
        else
        {
            files.Add(new ConnectedAppFile(path, content));
        }
    }

    // ---------------------------------------------------------------- 解析

    /// <summary>取得したファイル一式からアプリの一覧を作る。</summary>
    public static IReadOnlyList<ConnectedAppSummary> ParseSummaries(string kind, IEnumerable<ConnectedAppFile> files)
    {
        var list = files.ToList();
        if (string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase))
        {
            return ParseEcaSummaries(list);
        }

        var summaries = new List<ConnectedAppSummary>();
        foreach (var file in list.Where(f => IsMainFile(ConnectedAppKinds.ConnectedApp, f.RelativePath)))
        {
            var root = Parse(file.Content);
            if (root.Name.LocalName != "ConnectedApp")
            {
                continue;
            }

            var name = NameFromFile(file.RelativePath, ConnectedAppSuffix);
            var oauth = Child(root, "oauthConfig");
            summaries.Add(new ConnectedAppSummary
            {
                Kind = ConnectedAppKinds.ConnectedApp,
                Name = name,
                Label = Value(root, "label") ?? name,
                Description = Value(root, "description"),
                ContactEmail = Value(root, "contactEmail"),
                CallbackUrl = oauth is null ? null : Value(oauth, "callbackUrl"),
                Scopes = oauth is null
                    ? Array.Empty<string>()
                    : ConnectedAppOauthScopes.Combine(oauth.Elements().Where(e => e.Name.LocalName == "scopes").Select(e => e.Value)),
            });
        }

        return summaries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<ConnectedAppSummary> ParseEcaSummaries(List<ConnectedAppFile> files)
    {
        var summaries = new List<ConnectedAppSummary>();
        foreach (var file in files.Where(f => IsMainFile(ConnectedAppKinds.ExternalClientApp, f.RelativePath)))
        {
            var root = Parse(file.Content);
            if (root.Name.LocalName != "ExternalClientApplication")
            {
                continue;
            }

            var name = NameFromFile(file.RelativePath, EcaSuffix);
            var globalOauth = files
                .Where(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), EcaGlobalOauthDirectory, StringComparison.OrdinalIgnoreCase))
                .Select(f => Parse(f.Content))
                .FirstOrDefault(e => string.Equals(EcaOwner(e), name, StringComparison.OrdinalIgnoreCase));
            var oauthSettings = files
                .Where(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), EcaOauthSettingsDirectory, StringComparison.OrdinalIgnoreCase))
                .Select(f => Parse(f.Content))
                .FirstOrDefault(e => string.Equals(EcaOwner(e), name, StringComparison.OrdinalIgnoreCase));

            summaries.Add(new ConnectedAppSummary
            {
                Kind = ConnectedAppKinds.ExternalClientApp,
                Name = name,
                Label = Value(root, "label") ?? name,
                Description = Value(root, "description"),
                ContactEmail = Value(root, "contactEmail"),
                CallbackUrl = globalOauth is null ? null : Value(globalOauth, "callbackUrl"),
                Scopes = oauthSettings is null
                    ? Array.Empty<string>()
                    : ConnectedAppOauthScopes.Parse(Value(oauthSettings, "commaSeparatedOauthScopes")),
            });
        }

        return summaries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>1 件分のドキュメント（複数ファイル）を作る。見つからなければ null。</summary>
    public static ConnectedAppDocument? ParseDocument(string kind, string name, IEnumerable<ConnectedAppFile> files)
    {
        var all = files.ToList();
        var isEca = string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase);

        var owned = all.Where(f => isEca
                ? string.Equals(EcaOwner(Parse(f.Content)), name, StringComparison.OrdinalIgnoreCase)
                  || IsMainFile(ConnectedAppKinds.ExternalClientApp, f.RelativePath) && NameFromFile(f.RelativePath, EcaSuffix).Equals(name, StringComparison.OrdinalIgnoreCase)
                : IsMainFile(ConnectedAppKinds.ConnectedApp, f.RelativePath) && NameFromFile(f.RelativePath, ConnectedAppSuffix).Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var main = owned.FirstOrDefault(f => IsMainFile(kind, f.RelativePath));
        if (main is null)
        {
            return null;
        }

        var root = Parse(main.Content);
        var globalOauth = owned
            .Where(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), isEca ? EcaGlobalOauthDirectory : ConnectedAppDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(f => Parse(f.Content))
            .FirstOrDefault();
        var oauthSettings = owned
            .Where(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), EcaOauthSettingsDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(f => Parse(f.Content))
            .FirstOrDefault();
        var oauthPolicies = owned
            .Where(f => string.Equals(Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'), EcaOauthPoliciesDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(f => Parse(f.Content))
            .FirstOrDefault();

        if (isEca)
        {
            return new ConnectedAppDocument
            {
                Kind = ConnectedAppKinds.ExternalClientApp,
                Name = name,
                Label = Value(root, "label") ?? name,
                Description = Value(root, "description"),
                ContactEmail = Value(root, "contactEmail"),
                CallbackUrl = globalOauth is null ? null : Value(globalOauth, "callbackUrl"),
                Scopes = oauthSettings is null
                    ? Array.Empty<string>()
                    : ConnectedAppOauthScopes.Parse(Value(oauthSettings, "commaSeparatedOauthScopes")),
                IpRelaxation = oauthPolicies is null ? null : Value(oauthPolicies, "ipRelaxationPolicyType"),
                RefreshTokenPolicy = oauthPolicies is null ? null : Value(oauthPolicies, "refreshTokenPolicyType"),
                PermittedUsersPolicy = oauthPolicies is null ? null : Value(oauthPolicies, "permittedUsersPolicyType"),
                ConsumerKey = globalOauth is null ? null : Value(globalOauth, "consumerKey"),
                MainXml = NormalizeNewLines(main.Content),
                Files = owned,
            };
        }

        var oauth = Child(root, "oauthConfig");
        return new ConnectedAppDocument
        {
            Kind = ConnectedAppKinds.ConnectedApp,
            Name = name,
            Label = Value(root, "label") ?? name,
            Description = Value(root, "description"),
            ContactEmail = Value(root, "contactEmail"),
            CallbackUrl = oauth is null ? null : Value(oauth, "callbackUrl"),
            Scopes = oauth is null
                ? Array.Empty<string>()
                : ConnectedAppOauthScopes.Combine(oauth.Elements().Where(e => e.Name.LocalName == "scopes").Select(e => e.Value)),
            IpRelaxation = Child(root, "oauthPolicy") is { } policy ? Value(policy, "ipRelaxation") : null,
            RefreshTokenPolicy = Child(root, "oauthPolicy") is { } p2 ? Value(p2, "refreshTokenPolicy") : null,
            ConsumerKey = oauth is null ? null : Value(oauth, "consumerKey"),
            MainXml = NormalizeNewLines(main.Content),
            Files = owned,
        };
    }

    // ---------------------------------------------------------------- ヘルパー

    /// <summary>メイン コンポーネントの相対パス。</summary>
    public static string MainRelativePath(string kind, string name) =>
        string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? $"{EcaDirectory}/{name}{EcaSuffix}"
            : $"{ConnectedAppDirectory}/{name}{ConnectedAppSuffix}";

    public static bool IsMainFile(string kind, string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        return string.Equals(kind, ConnectedAppKinds.ExternalClientApp, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(directory, EcaDirectory, StringComparison.OrdinalIgnoreCase)
              && relativePath.EndsWith(EcaSuffix, StringComparison.OrdinalIgnoreCase)
            : string.Equals(directory, ConnectedAppDirectory, StringComparison.OrdinalIgnoreCase)
              && relativePath.EndsWith(ConnectedAppSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ファイル名から API 名を取り出す。</summary>
    public static string NameFromFile(string relativePath, string suffix)
    {
        var file = Path.GetFileName(relativePath);
        return file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? file[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(file);
    }

    /// <summary>IP 緩和を ECA の値へ変換する。</summary>
    public static string MapIpRelaxation(string? value) => (value ?? "").ToUpperInvariant() switch
    {
        "BYPASS" => "Bypass",
        "BYPASS_2FACTOR" => "Bypass_2factor",
        "ENFORCE_RELAXREFRESH" => "Enforce_RelaxRefresh",
        _ => "Enforce",
    };

    /// <summary>リフレッシュ トークン ポリシーを ECA の値へ変換する。</summary>
    public static string MapRefreshTokenPolicy(string? value) => (value ?? "").ToLowerInvariant() switch
    {
        "zero" => "Zero",
        "specificinactivity" => "SpecificInactivity",
        "specificlifetime" => "SpecificLifetime",
        _ => "Infinite",
    };

    private static string ElementNameFor(string suffix) => suffix switch
    {
        EcaSuffix => "ExternalClientApplication",
        EcaGlobalOauthSuffix => "ExtlClntAppGlobalOauthSettings",
        EcaOauthSettingsSuffix => "ExtlClntAppOauthSettings",
        EcaOauthPoliciesSuffix => "ExtlClntAppOauthConfigurablePolicies",
        EcaPoliciesSuffix => "ExtlClntAppConfigurablePolicies",
        _ => "ConnectedApp",
    };

    private static string? EcaOwner(XElement root) => Value(root, "externalClientApplication");

    private static XElement Parse(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Root ?? new XElement(Ns + "ConnectedApp");
        }
        catch (XmlException)
        {
            return new XElement(Ns + "ConnectedApp");
        }
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static XElement Add(XElement parent, string localName)
    {
        var element = new XElement(Ns + localName);
        parent.Add(element);
        return element;
    }

    private static string? Value(XElement parent, string localName)
    {
        var value = Child(parent, localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void Set(XElement parent, string localName, string? value)
    {
        var element = Child(parent, localName);
        if (string.IsNullOrWhiteSpace(value))
        {
            element?.Remove();
            return;
        }

        if (element is null)
        {
            parent.Add(new XElement(Ns + localName, value));
        }
        else
        {
            element.Value = value;
        }
    }

    private static void ReplaceScopes(XElement oauth, IReadOnlyList<string> scopes)
    {
        oauth.Elements().Where(e => e.Name.LocalName == "scopes").Remove();
        foreach (var scope in scopes)
        {
            oauth.Add(new XElement(Ns + "scopes", scope));
        }
    }

    private static string? NormalizeCallback(string? callbackUrl)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return null;
        }

        var urls = callbackUrl
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\r", urls);
    }

    private static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Serialize(XElement root)
    {
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.None,
            Encoding = new UTF8Encoding(false),
        };
        using (var writer = XmlWriter.Create(builder, settings))
        {
            document.Save(writer);
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + builder.ToString();
    }

    private static string EnsureXmlDeclaration(string xml)
    {
        var text = xml.Trim();
        return text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            ? text
            : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + text;
    }
}
