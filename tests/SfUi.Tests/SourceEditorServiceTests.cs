using System.Text.Json;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// ソース エディタの読み取りサービス（純関数部）のテスト。
/// Tooling REST の実呼び出しはスモーク（--smoke-source）で検証し、ここでは SOQL / パース / 判定を固定する。
/// </summary>
public class SourceEditorServiceTests
{
    [Fact]
    public void BuildListSoql_TargetsToolingObjects()
    {
        Assert.Contains("FROM ApexClass", SourceEditorService.BuildListSoql(SourceMemberKind.ApexClass));
        Assert.Contains("NamespacePrefix = null", SourceEditorService.BuildListSoql(SourceMemberKind.ApexClass));
        Assert.Contains("FROM ApexTrigger", SourceEditorService.BuildListSoql(SourceMemberKind.ApexTrigger));
        Assert.Contains("FROM ApexPage", SourceEditorService.BuildListSoql(SourceMemberKind.VisualforcePage));
        Assert.Contains("DeveloperName", SourceEditorService.BuildListSoql(SourceMemberKind.LightningComponentBundle));
    }

    [Fact]
    public void ParseMembers_ReadsNameIdDateAndSize()
    {
        using var document = JsonDocument.Parse("""
            {"totalSize":2,"records":[
              {"attributes":{"type":"ApexClass"},"Id":"01p1","Name":"Alpha","ApiVersion":62.0,"LastModifiedDate":"2026-10-09T12:34:56.000+0000","LengthWithoutComments":123},
              {"attributes":{"type":"ApexClass"},"Id":"01p2","Name":"Beta","ApiVersion":62.0,"LastModifiedDate":null,"LengthWithoutComments":456}
            ]}
            """);

        var members = SourceEditorService.ParseMembers(document, SourceMemberKind.ApexClass);

        Assert.Equal(2, members.Count);
        Assert.Equal("Alpha", members[0].Name);
        Assert.Equal("01p1", members[0].Id);
        Assert.Equal(123, members[0].SizeHint);
        Assert.NotNull(members[0].LastModifiedDate);
        Assert.Null(members[1].LastModifiedDate);
        Assert.Equal("Beta", members[1].ToString());
    }

    [Fact]
    public void ParseMembers_Lwc_UsesDeveloperName()
    {
        using var document = JsonDocument.Parse("""
            {"totalSize":1,"records":[{"attributes":{"type":"LightningComponentBundle"},"Id":"0RB1","DeveloperName":"myWidget"}]}
            """);

        var members = SourceEditorService.ParseMembers(document, SourceMemberKind.LightningComponentBundle);

        Assert.Single(members);
        Assert.Equal("myWidget", members[0].Name);
    }

    [Fact]
    public void ParseMembers_SkipsIncompleteRecords()
    {
        using var document = JsonDocument.Parse("""
            {"totalSize":2,"records":[{"Id":"01p1"},{"Name":"Beta"}]}
            """);

        Assert.Empty(SourceEditorService.ParseMembers(document, SourceMemberKind.ApexClass));
    }

    [Fact]
    public void ParseLwcResources_OrdersJsHtmlMetaAndMapsLanguages()
    {
        using var document = JsonDocument.Parse("""
            {"totalSize":3,"records":[
              {"FilePath":"lwc/helper/helper.js-meta.xml","Format":"xml","Source":"<bundle/>"},
              {"FilePath":"lwc/helper/styles.css","Format":"css","Source":".a{}"},
              {"FilePath":"lwc/helper/helper.js","Format":"js","Source":"export default class {}"},
              {"FilePath":"lwc/helper/helper.html","Format":"html","Source":"<template/>"}
            ]}
            """);

        var files = SourceEditorService.ParseLwcResources(document);

        Assert.Equal(new[] { "helper.js", "helper.html", "styles.css", "helper.js-meta.xml" }, files.Select(f => f.FileName));
        Assert.Equal("JavaScript", files[0].LanguageId);
        Assert.Equal("HTML", files[1].LanguageId);
        Assert.Equal("CSS", files[2].LanguageId);
        Assert.Equal("XML", files[3].LanguageId);
    }

    [Theory]
    [InlineData("AccountService.cls", "C#")]
    [InlineData("Account.trigger", "C#")]
    [InlineData("helper.js", "JavaScript")]
    [InlineData("helper.html", "HTML")]
    [InlineData("styles.css", "CSS")]
    [InlineData("helper.js-meta.xml", "XML")]
    [InlineData("MyPage.page", "XML")]
    [InlineData("readme.txt", "")]
    public void LanguageForFileName_MapsExtensions(string fileName, string expected)
        => Assert.Equal(expected, SourceEditorService.LanguageForFileName(fileName));

    [Fact]
    public void ParseSourceText_ReadsBodyOrMarkup()
    {
        using var classDocument = JsonDocument.Parse("""{"Id":"01p1","Body":"public class X {}"}""");
        Assert.Equal("public class X {}", SourceEditorService.ParseSourceText(classDocument, SourceMemberKind.ApexClass));

        using var pageDocument = JsonDocument.Parse("""{"Id":"0661","Markup":"<apex:page/>"}""");
        Assert.Equal("<apex:page/>", SourceEditorService.ParseSourceText(pageDocument, SourceMemberKind.VisualforcePage));

        using var emptyDocument = JsonDocument.Parse("""{"Id":"01p1"}""");
        Assert.Null(SourceEditorService.ParseSourceText(emptyDocument, SourceMemberKind.ApexClass));
    }

    [Fact]
    public void BuildLwcResourceSoql_EscapesQuotes()
    {
        var soql = SourceEditorService.BuildLwcResourceSoql("O'Brien");
        Assert.Contains("= 'O\\'Brien'", soql);
    }

    [Fact]
    public void EscapeSoql_HandlesBackslashAndQuote()
        => Assert.Equal("a\\\\b\\'c", SourceEditorService.EscapeSoql("a\\b'c"));

    [Fact]
    public void FileNameForMember_AppendsKindExtension()
    {
        Assert.Equal("Alpha.cls", SourceEditorService.FileNameForMember(new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1")));
        Assert.Equal("Trg.trigger", SourceEditorService.FileNameForMember(new SourceMemberInfo(SourceMemberKind.ApexTrigger, "Trg", "01q1")));
        Assert.Equal("Page1.page", SourceEditorService.FileNameForMember(new SourceMemberInfo(SourceMemberKind.VisualforcePage, "Page1", "0661")));
    }

    // ---- Phase 2: 反映結果の解析 / ワークスペース ----

    [Fact]
    public void ParseDeployResult_Success_ReturnsOk()
    {
        var result = SourceEditorService.ParseDeployResult("""{"status":0,"result":{"status":"Succeeded","numberComponentErrors":0}}""", dryRun: false);
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseDeployResult_CompileErrors_ReadLineColumnAndFile()
    {
        const string json = """
            {"status":1,"message":"Deploy failed","result":{"details":{"componentFailures":[
              {"fileName":"classes/Alpha.cls","lineNumber":3,"columnNumber":17,"problem":"Unexpected token ';'.","problemType":"Error","success":false},
              {"fileName":"classes/Alpha.cls","lineNumber":2,"problem":"Missing return statement","problemType":"Error","success":false}
            ]}}}
            """;
        var result = SourceEditorService.ParseDeployResult(json, dryRun: false);

        Assert.False(result.Success);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(3, result.Errors[0].Line);
        Assert.Equal(17, result.Errors[0].Column);
        Assert.Equal("Unexpected token ';'.", result.Errors[0].Problem);
        Assert.Contains("Alpha.cls", result.Errors[0].ToString());
        Assert.Equal(0, result.Errors[1].Column);
    }

    [Fact]
    public void ParseDeployResult_ConversionError_ReturnsMessage()
    {
        var result = SourceEditorService.ParseDeployResult("""{"name":"ConversionError","message":"Component conversion failed","exitCode":1}""", dryRun: false);
        Assert.False(result.Success);
        Assert.Contains("conversion", result.Message);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseSfStatus_ReadsStatusField()
    {
        Assert.True(SourceEditorService.ParseSfStatus("""{"status":0}""").Success);
        Assert.False(SourceEditorService.ParseSfStatus("""{"status":1,"message":"nope"}""").Success);
        Assert.False(SourceEditorService.ParseSfStatus("not json").Success);
    }

    [Fact]
    public void AreaAndExtension_MatchSfdxLayout()
    {
        Assert.Equal("classes", SourceEditorService.AreaFor(SourceMemberKind.ApexClass));
        Assert.Equal("triggers", SourceEditorService.AreaFor(SourceMemberKind.ApexTrigger));
        Assert.Equal("pages", SourceEditorService.AreaFor(SourceMemberKind.VisualforcePage));
        Assert.Equal("lwc", SourceEditorService.AreaFor(SourceMemberKind.LightningComponentBundle));
        Assert.Equal(".cls", SourceEditorService.ExtensionFor(SourceMemberKind.ApexClass));
        Assert.Equal(".page", SourceEditorService.ExtensionFor(SourceMemberKind.VisualforcePage));
        Assert.Equal(string.Empty, SourceEditorService.ExtensionFor(SourceMemberKind.LightningComponentBundle));
    }

    [Fact]
    public void BuildDefaultMeta_GeneratesApiVersionAndLabel()
    {
        var cls = SourceEditorService.BuildDefaultMeta(SourceMemberKind.ApexClass, "Alpha", "62.0");
        Assert.Contains("<ApexClass", cls);
        Assert.Contains("<apiVersion>62.0</apiVersion>", cls);
        Assert.Contains("<status>Active</status>", cls);

        var page = SourceEditorService.BuildDefaultMeta(SourceMemberKind.VisualforcePage, "MyPage", "62.0");
        Assert.Contains("<ApexPage", page);
        Assert.Contains("<label>MyPage</label>", page);
    }

    [Fact]
    public void Workspace_KeyFor_SanitizesUsername()
    {
        Assert.Equal("user-example.com.hks4sand1", SourceEditorWorkspace.KeyFor("user@example.com.hks4sand1"));
        Assert.Equal("org", SourceEditorWorkspace.KeyFor(null));
        Assert.Equal("org", SourceEditorWorkspace.KeyFor("@@@"));
    }

    [Fact]
    public async Task Workspace_DraftRoundTrip_AndClear()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "sfui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var paths = AppPaths.Resolve(dataRootOverride: sandbox, baseDirectory: sandbox, appDataDirectory: sandbox);
            var workspace = new SourceEditorWorkspace(paths, new AppLog(paths));
            var member = new SourceMemberInfo(SourceMemberKind.ApexClass, "Alpha", "01p1");
            var baseline = new[] { new SourceFileInfo("Alpha.cls", "C#", "org text") };
            var working = new[] { new SourceFileInfo("Alpha.cls", "C#", "edited text") };

            Assert.Null(await workspace.LoadDraftAsync("key", member));

            await workspace.SaveDraftAsync("key", member, baseline, working);
            var draft = await workspace.LoadDraftAsync("key", member);

            Assert.NotNull(draft);
            Assert.Equal("org text", draft!.Baseline[0].Text);
            Assert.Equal("edited text", draft.Working[0].Text);
            Assert.Equal("C#", draft.Working[0].LanguageId);

            await workspace.ClearDraftAsync("key", member);
            Assert.Null(await workspace.LoadDraftAsync("key", member));
        }
        finally
        {
            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

    // ---- Phase 2: CLI 側の偽の失敗（反映済みでも Failed を返す）への耐性 ----

    [Fact]
    public void ShouldVerifyAgainstOrg_OnlyForCliLevelFailures()
    {
        var cliFailure = SourceEditorService.ParseDeployResult(
            """{"name":"MetadataTransferError","message":"Metadata API request failed: Missing message metadata.transfer:Finalizing for locale en_US.","exitCode":1}""",
            dryRun: false);
        Assert.False(cliFailure.Success);
        Assert.True(SourceEditorService.ShouldVerifyAgainstOrg(cliFailure, dryRun: false));

        // 成功・検証のみ・コンパイル エラーありは組織での再判定を行わない
        Assert.False(SourceEditorService.ShouldVerifyAgainstOrg(SourceDeployResult.Ok(false, "Succeeded"), dryRun: false));
        Assert.False(SourceEditorService.ShouldVerifyAgainstOrg(cliFailure, dryRun: true));
        var compileFailure = SourceEditorService.ParseDeployResult(
            """{"status":1,"result":{"details":{"componentFailures":[{"fileName":"classes/A.cls","lineNumber":1,"problem":"bad"}]}}}""",
            dryRun: false);
        Assert.False(SourceEditorService.ShouldVerifyAgainstOrg(compileFailure, dryRun: false));
    }

    [Fact]
    public void ContentMatches_IgnoresNewlineStyleAndFileNameCase()
    {
        var intended = new[] { new SourceFileInfo("Alpha.cls", "C#", "line1\r\nline2") };
        Assert.True(SourceEditorService.ContentMatches(intended, new[] { new SourceFileInfo("alpha.CLS", "C#", "line1\nline2") }));
        Assert.False(SourceEditorService.ContentMatches(intended, new[] { new SourceFileInfo("Alpha.cls", "C#", "line1\nchanged") }));
        Assert.False(SourceEditorService.ContentMatches(intended, Array.Empty<SourceFileInfo>()));
        Assert.False(SourceEditorService.ContentMatches(intended, new[]
        {
            new SourceFileInfo("Alpha.cls", "C#", "line1\r\nline2"),
            new SourceFileInfo("Beta.cls", "C#", "extra"),
        }));
    }

    [Fact]
    public void ContentMatches_IgnoresTrailingNewline()
    {
        // 組織は末尾の改行を落とすことがある（deploy したテンプレートとの比較で必要）
        var intended = new[] { new SourceFileInfo("Alpha.cls", "C#", "class Alpha {\n}\n") };
        Assert.True(SourceEditorService.ContentMatches(intended, new[] { new SourceFileInfo("Alpha.cls", "C#", "class Alpha {\n}") }));
        Assert.True(SourceEditorService.ContentMatches(new[] { new SourceFileInfo("Alpha.cls", "C#", "a\n") }, new[] { new SourceFileInfo("Alpha.cls", "C#", "a") }));
        Assert.False(SourceEditorService.ContentMatches(intended, new[] { new SourceFileInfo("Alpha.cls", "C#", "class Alpha {\n} changed") }));
    }

    [Fact]
    public void ContentMatches_LwcBundle_ComparesAllFilesInAnyOrder()
    {
        var intended = new[]
        {
            new SourceFileInfo("bundle.js", "JavaScript", "// js"),
            new SourceFileInfo("bundle.html", "HTML", "<template></template>"),
        };
        var org = new[]
        {
            new SourceFileInfo("bundle.html", "HTML", "<template></template>"),
            new SourceFileInfo("bundle.js", "JavaScript", "// js"),
        };
        Assert.True(SourceEditorService.ContentMatches(intended, org));

        Assert.False(SourceEditorService.ContentMatches(intended, new[]
        {
            new SourceFileInfo("bundle.js", "JavaScript", "// js v2"),
            new SourceFileInfo("bundle.html", "HTML", "<template></template>"),
        }));
    }
}
