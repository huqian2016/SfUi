using SfUi.App.ViewModels;
using SfUi.Core;
using SfUi.Presentation;
using Xunit;

namespace SfUi.Tests;

/// <summary>接続アプリ（ConnectedApp / ExternalClientApplication）の XML・サービス・ViewMode のテスト。</summary>
[Collection("Localization")]
public class ConnectedAppTests
{
    private const string Ns = "http://soap.sforce.com/2006/04/metadata";

    // ---------------------------------------------------------------- XML 生成

    [Fact]
    public void BuildConnectedAppFiles_WritesOauthConfigAndPolicy()
    {
        var draft = new ConnectedAppDraft
        {
            Kind = ConnectedAppKinds.ConnectedApp,
            Name = "SfUiApp",
            Label = "SfUi App",
            ContactEmail = "ops@example.com",
            Description = "desc",
            CallbackUrl = "https://example.com/cb1\nhttps://example.com/cb2",
            Scopes = new[] { "Api", "RefreshToken" },
            IpRelaxation = "BYPASS",
            RefreshTokenPolicy = "zero",
        };

        var files = ConnectedAppXml.BuildFiles(draft);

        var file = Assert.Single(files);
        Assert.Equal("connectedApps/SfUiApp.connectedApp-meta.xml", file.RelativePath);
        Assert.Contains("<label>SfUi App</label>", file.Content);
        Assert.Contains("<callbackUrl>https://example.com/cb1\rhttps://example.com/cb2</callbackUrl>", file.Content);
        Assert.Contains("<scopes>Api</scopes>", file.Content);
        Assert.Contains("<scopes>RefreshToken</scopes>", file.Content);
        Assert.Contains("<ipRelaxation>BYPASS</ipRelaxation>", file.Content);
        Assert.Contains("<refreshTokenPolicy>zero</refreshTokenPolicy>", file.Content);
    }

    [Fact]
    public void BuildConnectedAppFiles_WithoutOauth_OmitsOauthNodes()
    {
        var draft = new ConnectedAppDraft
        {
            Name = "Plain",
            Label = "Plain",
            ContactEmail = "a@example.com",
        };

        var content = Assert.Single(ConnectedAppXml.BuildFiles(draft)).Content;

        Assert.DoesNotContain("oauthConfig", content);
        Assert.DoesNotContain("oauthPolicy", content);
    }

    [Fact]
    public void BuildEcaFiles_CreatesAllPluginFiles()
    {
        var draft = new ConnectedAppDraft
        {
            Kind = ConnectedAppKinds.ExternalClientApp,
            Name = "SfUiEca",
            Label = "SfUi ECA",
            ContactEmail = "ops@example.com",
            CallbackUrl = "https://example.com/cb",
            Scopes = new[] { "Api", "RefreshToken" },
            IpRelaxation = "ENFORCE",
        };

        var files = ConnectedAppXml.BuildFiles(draft);

        Assert.Equal(5, files.Count);
        Assert.Contains(files, f => f.RelativePath == "externalClientApps/SfUiEca.eca-meta.xml");
        Assert.Contains(files, f => f.RelativePath == "extlClntAppGlobalOauthSets/SfUiEca.ecaGlblOauth-meta.xml");
        Assert.Contains(files, f => f.RelativePath == "extlClntAppOauthSettings/SfUiEca.ecaOauth-meta.xml");
        Assert.Contains(files, f => f.RelativePath == "extlClntAppOauthPolicies/SfUiEca_oauth.ecaOauthPlcy-meta.xml");
        Assert.Contains(files, f => f.RelativePath == "extlClntAppPolicies/SfUiEca_defaultPolicy.ecaPlcy-meta.xml");

        var global = files.First(f => f.RelativePath.Contains("ecaGlblOauth")).Content;
        Assert.Contains("<callbackUrl>https://example.com/cb</callbackUrl>", global);
        Assert.Contains("<externalClientApplication>SfUiEca</externalClientApplication>", global);

        var settings = files.First(f => f.RelativePath.Contains("extlClntAppOauthSettings")).Content;
        Assert.Contains("<commaSeparatedOauthScopes>Api, RefreshToken</commaSeparatedOauthScopes>", settings);

        var policies = files.First(f => f.RelativePath.Contains("ecaOauthPlcy")).Content;
        Assert.Contains("<ipRelaxationPolicyType>Enforce</ipRelaxationPolicyType>", policies);
        Assert.Contains("<refreshTokenPolicyType>Infinite</refreshTokenPolicyType>", policies);
    }

    // ---------------------------------------------------------------- 解析

    [Fact]
    public void ParseDocument_ReadsConnectedAppFields()
    {
        var draft = new ConnectedAppDraft
        {
            Name = "SfUiApp",
            Label = "Label",
            ContactEmail = "a@example.com",
            CallbackUrl = "https://example.com/cb",
            Scopes = new[] { "Api" },
            IpRelaxation = "ENFORCE",
            RefreshTokenPolicy = "infinite",
        };
        var files = ConnectedAppXml.BuildFiles(draft);

        var document = ConnectedAppXml.ParseDocument(ConnectedAppKinds.ConnectedApp, "SfUiApp", files);

        Assert.NotNull(document);
        Assert.Equal("Label", document!.Label);
        Assert.Equal("a@example.com", document.ContactEmail);
        Assert.Equal("https://example.com/cb", document.CallbackUrl);
        Assert.Equal(new[] { "Api" }, document.Scopes);
        Assert.Equal("ENFORCE", document.IpRelaxation);
        Assert.Equal("infinite", document.RefreshTokenPolicy);
        Assert.True(document.HasOauth);
    }

    [Fact]
    public void ParseSummaries_GroupsEcaFiles()
    {
        var draft = new ConnectedAppDraft
        {
            Kind = ConnectedAppKinds.ExternalClientApp,
            Name = "SfUiEca",
            Label = "ECA Label",
            ContactEmail = "a@example.com",
            CallbackUrl = "https://example.com/cb",
            Scopes = new[] { "Api", "Web" },
        };
        var files = ConnectedAppXml.BuildFiles(draft);

        var summaries = ConnectedAppXml.ParseSummaries(ConnectedAppKinds.ExternalClientApp, files);

        var summary = Assert.Single(summaries);
        Assert.Equal("SfUiEca", summary.Name);
        Assert.Equal("ECA Label", summary.Label);
        Assert.Equal("https://example.com/cb", summary.CallbackUrl);
        Assert.Equal(new[] { "Api", "Web" }, summary.Scopes);
        Assert.Equal("Api, Web", summary.ScopesText);
    }

    [Fact]
    public void ParseDocument_ReturnsNullWhenMissing()
    {
        var files = new[] { new ConnectedAppFile("connectedApps/Other.connectedApp-meta.xml", $"<ConnectedApp xmlns=\"{Ns}\"><label>Other</label></ConnectedApp>") };

        Assert.Null(ConnectedAppXml.ParseDocument(ConnectedAppKinds.ConnectedApp, "Missing", files));
    }

    // ---------------------------------------------------------------- 更新

    [Fact]
    public void ApplyDraft_PreservesUnknownNodes()
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ConnectedApp xmlns="{Ns}">
                <contactEmail>old@example.com</contactEmail>
                <label>Old</label>
                <customSetting>keep-me</customSetting>
                <oauthConfig>
                    <callbackUrl>https://old/cb</callbackUrl>
                    <scopes>Api</scopes>
                </oauthConfig>
                <oauthPolicy>
                    <ipRelaxation>ENFORCE</ipRelaxation>
                    <refreshTokenPolicy>infinite</refreshTokenPolicy>
                </oauthPolicy>
            </ConnectedApp>
            """;
        var document = new ConnectedAppDocument
        {
            Kind = ConnectedAppKinds.ConnectedApp,
            Name = "App",
            Files = new[] { new ConnectedAppFile("connectedApps/App.connectedApp-meta.xml", xml) },
        };

        var files = ConnectedAppXml.ApplyDraft(document, new ConnectedAppDraft
        {
            Name = "App",
            Label = "New",
            ContactEmail = "new@example.com",
            CallbackUrl = "https://new/cb",
            Scopes = new[] { "Api", "RefreshToken" },
            IpRelaxation = "BYPASS",
            RefreshTokenPolicy = "zero",
        });

        var content = Assert.Single(files).Content;
        Assert.Contains("<label>New</label>", content);
        Assert.Contains("<contactEmail>new@example.com</contactEmail>", content);
        Assert.Contains("<customSetting>keep-me</customSetting>", content);
        Assert.Contains("<callbackUrl>https://new/cb</callbackUrl>", content);
        Assert.Contains("<scopes>RefreshToken</scopes>", content);
        Assert.DoesNotContain("<scopes>Api</scopes><scopes>Api</scopes>", content);
        Assert.Contains("<ipRelaxation>BYPASS</ipRelaxation>", content);
        Assert.Contains("<refreshTokenPolicy>zero</refreshTokenPolicy>", content);
    }

    [Fact]
    public void ApplyDraft_ReplacesMainXmlWhenRawXmlProvided()
    {
        var document = new ConnectedAppDocument
        {
            Kind = ConnectedAppKinds.ConnectedApp,
            Name = "App",
            Files = new[]
            {
                new ConnectedAppFile("connectedApps/App.connectedApp-meta.xml", $"<ConnectedApp xmlns=\"{Ns}\"><label>Old</label></ConnectedApp>"),
            },
        };
        const string raw = $"<ConnectedApp xmlns=\"{Ns}\"><label>Raw</label></ConnectedApp>";

        var files = ConnectedAppXml.ApplyDraft(document, new ConnectedAppDraft
        {
            Name = "App",
            Label = "Form",
            ContactEmail = "a@example.com",
            RawXml = raw,
        });

        var content = Assert.Single(files).Content;
        Assert.Contains("<label>Raw</label>", content);
        Assert.DoesNotContain("<label>Form</label>", content);
    }

    // ---------------------------------------------------------------- 削除 / 結果解析

    [Fact]
    public void BuildDeleteManifests_UsesKindSpecificType()
    {
        var (_, eca) = ConnectedAppService.BuildDeleteManifests(ConnectedAppKinds.ExternalClientApp, "SfUiEca");
        Assert.Contains("<name>ExternalClientApplication</name>", eca);
        Assert.Contains("<members>SfUiEca</members>", eca);

        var (_, app) = ConnectedAppService.BuildDeleteManifests(ConnectedAppKinds.ConnectedApp, "SfUiApp");
        Assert.Contains("<name>ConnectedApp</name>", app);
    }

    [Fact]
    public void ParseDeployResult_ReadsComponentFailures()
    {
        var json = """
            {"status":1,"result":{"success":false,"status":"Failed","deployUrl":"https://example.com/deploy",
            "details":{"componentFailures":[{"fileName":"connectedApps/X.connectedApp","componentType":"ConnectedApp",
            "problem":"You can't create a connected app. To enable connected app creation, contact Salesforce Customer Support."}]}}}
            """;
        var result = new SfCliResult(new[] { "project", "deploy", "start" }, 1, json, "", false, TimeSpan.FromSeconds(1));

        var parsed = ConnectedAppService.ParseDeployResult(result);

        Assert.False(parsed.Success);
        Assert.Equal("Failed", parsed.Status);
        Assert.Single(parsed.Problems);
        Assert.True(parsed.IsCreationRestricted);
        Assert.Equal("https://example.com/deploy", parsed.DeployUrl);
    }

    [Fact]
    public void ParseDeployResult_Success()
    {
        var json = """{"status":0,"result":{"success":true,"status":"Succeeded","details":{"componentFailures":[]}}}""";
        var result = new SfCliResult(Array.Empty<string>(), 0, json, "", false, TimeSpan.Zero);

        var parsed = ConnectedAppService.ParseDeployResult(result);

        Assert.True(parsed.Success);
        Assert.Empty(parsed.Problems);
        Assert.False(parsed.IsCreationRestricted);
    }

    [Fact]
    public void ParseDeployResult_WithoutJsonUsesStderr()
    {
        var result = new SfCliResult(Array.Empty<string>(), 1, "", "Error (1): Not authenticated", false, TimeSpan.Zero);

        var parsed = ConnectedAppService.ParseDeployResult(result);

        Assert.False(parsed.Success);
        Assert.Contains("Not authenticated", parsed.ProblemText);
    }

    // ---------------------------------------------------------------- 入力検証

    [Theory]
    [InlineData("Valid_Name1", true)]
    [InlineData("1Invalid", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData("ハイフン-", false)]
    public void IsValidApiName(string name, bool expected)
    {
        Assert.Equal(expected, ConnectedAppDraft.IsValidApiName(name));
    }

    [Fact]
    public void Validate_RequiresCallbackScopes()
    {
        var draft = new ConnectedAppDraft
        {
            Name = "App",
            Label = "Label",
            ContactEmail = "a@example.com",
            CallbackUrl = "https://example.com/cb",
        };

        Assert.NotNull(draft.Validate());
    }

    [Fact]
    public void Validate_RejectsNonAbsoluteCallback()
    {
        var draft = new ConnectedAppDraft
        {
            Name = "App",
            Label = "Label",
            ContactEmail = "a@example.com",
            CallbackUrl = "not-a-url",
            Scopes = new[] { "Api" },
        };

        Assert.NotNull(draft.Validate());
    }

    // ---------------------------------------------------------------- ViewModel

    [Fact]
    public async Task ViewModel_LoadsAndDeploysNewEca()
    {
        var service = new FakeService();
        var editor = new FakeEditor(new ConnectedAppEditorResult(new ConnectedAppDraft
        {
            Kind = ConnectedAppKinds.ExternalClientApp,
            Name = "NewEca",
            Label = "New ECA",
            ContactEmail = "a@example.com",
            CallbackUrl = "https://example.com/cb",
            Scopes = new[] { "Api" },
        }, DryRun: false));
        var viewModel = new ConnectedAppsViewModel(service, editor, new FakeDialogs(), CreateLog())
        {
            SelectedKind = new ReportChoice(ConnectedAppKinds.ExternalClientApp, "ECA"),
        };

        viewModel.Initialize(new[] { new OrgInfo("user@example.com", "dev", null, "https://x.my.salesforce.com", "Connected", true, true) }, null);
        await WaitAsync(() => service.ListCalls > 0);

        await viewModel.NewCommand.ExecuteAsync(null);

        Assert.Equal(1, service.DeployCalls);
        Assert.Equal(5, service.LastFiles!.Count);
    }

    [Fact]
    public async Task ViewModel_DeleteRequiresConfirmation()
    {
        var service = new FakeService();
        var dialogs = new FakeDialogs { ConfirmDestructiveResult = false };
        var viewModel = new ConnectedAppsViewModel(service, new FakeEditor(null), dialogs, CreateLog());

        viewModel.Initialize(new[] { new OrgInfo("user@example.com", null, null, "https://x.my.salesforce.com", "Connected", false, false) }, null);
        await WaitAsync(() => service.ListCalls > 0);

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(0, service.DeleteCalls);
    }

    /// <summary>
    /// 詳細ペインの見出しは XAML から名前でバインドされる。1 つでも欠けると
    /// 画面に空のラベルが出るため、ここで存在と非空をまとめて保証する。
    /// </summary>
    [Fact]
    public void ViewModel_ExposesDetailLabels()
    {
        var viewModel = new ConnectedAppsViewModel(new FakeService(), new FakeEditor(null), new FakeDialogs(), CreateLog());

        var labels = new[]
        {
            viewModel.LabelName,
            viewModel.LabelDisplayName,
            viewModel.LabelContactEmail,
            viewModel.LabelDescription,
            viewModel.LabelCallbackUrlText,
            viewModel.LabelScopesText,
            viewModel.LabelConsumerKeyText,
            viewModel.LabelIpRelaxationText,
            viewModel.LabelRefreshTokenPolicyText,
        };

        Assert.All(labels, label =>
        {
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.DoesNotContain("Conn_", label, StringComparison.Ordinal);
        });
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ViewModel_HasAppsTracksTheFilteredList()
    {
        var service = new FakeService();
        var viewModel = new ConnectedAppsViewModel(service, new FakeEditor(null), new FakeDialogs(), CreateLog());

        viewModel.Initialize(new[] { new OrgInfo("user@example.com", null, null, "https://x.my.salesforce.com", "Connected", false, false) }, null);
        await WaitAsync(() => service.ListCalls > 0 && viewModel.HasApps);

        Assert.True(viewModel.HasApps);

        viewModel.Filter = "does-not-exist";
        Assert.False(viewModel.HasApps);

        viewModel.Filter = "";
        await WaitAsync(() => viewModel.HasApps);
        Assert.True(viewModel.HasApps);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private static AppLog CreateLog()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
        return new AppLog(paths);
    }

    private sealed class FakeService : IConnectedAppService
    {
        public int ListCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public int DeployCalls { get; private set; }

        public IReadOnlyList<ConnectedAppFile>? LastFiles { get; private set; }

        public Task<IReadOnlyList<ConnectedAppSummary>> ListAsync(string targetOrg, string kind, CancellationToken cancellationToken)
        {
            ListCalls++;
            IReadOnlyList<ConnectedAppSummary> list = new[]
            {
                new ConnectedAppSummary { Kind = kind, Name = "App1", Label = "App One", ContactEmail = "a@example.com" },
            };
            return Task.FromResult(list);
        }

        public Task<ConnectedAppDocument?> GetAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectedAppDocument?>(new ConnectedAppDocument
            {
                Kind = kind,
                Name = name,
                Label = "App One",
                Files = new[] { new ConnectedAppFile(ConnectedAppXml.MainRelativePath(kind, name), "<ConnectedApp/>") },
            });

        public Task<ConnectedAppDeployResult> DeployAsync(string targetOrg, IReadOnlyList<ConnectedAppFile> files, bool dryRun, CancellationToken cancellationToken)
        {
            DeployCalls++;
            LastFiles = files;
            return Task.FromResult(new ConnectedAppDeployResult { Success = true, Status = "Succeeded" });
        }

        public Task<ConnectedAppDeployResult> DeleteAsync(string targetOrg, string kind, string name, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.FromResult(new ConnectedAppDeployResult { Success = true, Status = "Succeeded" });
        }
    }

    private sealed class FakeEditor : IConnectedAppEditorDialog
    {
        private readonly ConnectedAppEditorResult? _result;

        public FakeEditor(ConnectedAppEditorResult? result)
        {
            _result = result;
        }

        public Task<ConnectedAppEditorResult?> EditAsync(ConnectedAppEditorRequest request) => Task.FromResult(_result);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmDestructiveResult { get; set; } = true;

        public void Info(string message, string caption)
        {
        }

        public void Warning(string message, string caption)
        {
        }

        public bool Confirm(string message, string caption) => true;

        public bool ConfirmDestructive(string message, string caption) => ConfirmDestructiveResult;

        public string? Prompt(string message, string caption, string defaultValue) => defaultValue;
    }
}
