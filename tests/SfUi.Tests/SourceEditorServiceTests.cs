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
}
