using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>Phase 3: 新規作成テンプレートと API 名検証のテスト。</summary>
[Collection("Localization")]
public class SourceEditorTemplatesTests
{
    // ---- API 名の検証（クラス / トリガー / VF）----

    [Theory]
    [InlineData(null, SourceNameError.Required)]
    [InlineData("", SourceNameError.Required)]
    [InlineData("   ", SourceNameError.Required)]
    [InlineData("1Alpha", SourceNameError.MustStartWithLetter)]
    [InlineData("_Alpha", SourceNameError.MustStartWithLetter)]
    [InlineData("Alpha-Beta", SourceNameError.InvalidCharacters)]
    [InlineData("Alpha Beta", SourceNameError.InvalidCharacters)]
    [InlineData("Alpha__Beta", SourceNameError.DoubleUnderscore)]
    [InlineData("Alpha_", SourceNameError.TrailingUnderscore)]
    [InlineData("Alpha1_Beta", SourceNameError.None)]
    [InlineData("Alpha", SourceNameError.None)]
    [InlineData(" Hoge_2 ", SourceNameError.None)]
    public void ValidateName_ClassRules(string? name, SourceNameError expected)
        => Assert.Equal(expected, SourceEditorNameValidator.ValidateName(SourceMemberKind.ApexClass, name));

    [Theory]
    [InlineData("myComponent1", SourceNameError.None)]
    [InlineData("MyComponent", SourceNameError.MustStartWithLowercase)]
    [InlineData("1myComponent", SourceNameError.MustStartWithLowercase)]
    [InlineData("my_component", SourceNameError.LwcInvalidCharacters)]
    [InlineData("my-component", SourceNameError.LwcInvalidCharacters)]
    public void ValidateName_LwcRules(string name, SourceNameError expected)
        => Assert.Equal(expected, SourceEditorNameValidator.ValidateName(SourceMemberKind.LightningComponentBundle, name));

    [Fact]
    public void ValidateName_LwcLength_LimitIs40()
    {
        Assert.Equal(SourceNameError.None, SourceEditorNameValidator.ValidateName(SourceMemberKind.LightningComponentBundle, new string('a', 40)));
        Assert.Equal(SourceNameError.TooLong, SourceEditorNameValidator.ValidateName(SourceMemberKind.LightningComponentBundle, new string('a', 41)));
    }

    // ---- 対象オブジェクト名（カスタム オブジェクトの __c を許可）----

    [Theory]
    [InlineData("Account", SourceNameError.None)]
    [InlineData("My_Custom_Obj__c", SourceNameError.None)]
    [InlineData("ns__Obj__c", SourceNameError.None)]
    [InlineData("1Obj", SourceNameError.MustStartWithLetter)]
    [InlineData("My-Obj", SourceNameError.InvalidCharacters)]
    [InlineData("", SourceNameError.Required)]
    public void ValidateSObjectName_Rules(string? name, SourceNameError expected)
        => Assert.Equal(expected, SourceEditorNameValidator.ValidateSObjectName(name));

    // ---- 複合検証（ダイアログ用）----

    [Fact]
    public void ValidateNewMember_ExistingName_IsRejected()
    {
        var error = SourceEditorNameValidator.ValidateNewMember(
            SourceMemberKind.ApexClass, "Alpha", null, new[] { "alpha", "Beta" });
        Assert.Equal(UiText.T("SourceEditor_NameExistsFmt", "Alpha"), error);
    }

    [Fact]
    public void ValidateNewMember_Trigger_RequiresSObject()
    {
        Assert.NotNull(SourceEditorNameValidator.ValidateNewMember(SourceMemberKind.ApexTrigger, "Trg", null, null));
        Assert.NotNull(SourceEditorNameValidator.ValidateNewMember(SourceMemberKind.ApexTrigger, "Trg", "1Bad", null));
        Assert.Null(SourceEditorNameValidator.ValidateNewMember(SourceMemberKind.ApexTrigger, "Trg", "Account", null));
    }

    [Fact]
    public void ValidateNewMember_ValidInput_ReturnsNull()
    {
        Assert.Null(SourceEditorNameValidator.ValidateNewMember(SourceMemberKind.ApexClass, "NewClass", null, Array.Empty<string>()));
        Assert.Null(SourceEditorNameValidator.ValidateNewMember(SourceMemberKind.LightningComponentBundle, "myComp", null, null));
    }

    [Fact]
    public void Describe_MapsAllErrorsToMessages()
    {
        foreach (var error in Enum.GetValues<SourceNameError>())
        {
            var message = SourceEditorNameValidator.Describe(error);
            if (error == SourceNameError.None)
            {
                Assert.Null(message);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(message), error.ToString());
            }
        }
    }

    // ---- テンプレート ----

    [Fact]
    public void BuildFiles_ApexClass_ContainsClassAndConstructor()
    {
        var files = SourceEditorTemplates.BuildFiles(SourceMemberKind.ApexClass, "MyClass");
        var file = Assert.Single(files);
        Assert.Equal("MyClass.cls", file.FileName);
        Assert.Equal("C#", file.LanguageId);
        Assert.Contains("public with sharing class MyClass", file.Text);
        Assert.Contains("public MyClass()", file.Text);
    }

    [Fact]
    public void BuildFiles_ApexTrigger_RequiresSObject()
    {
        Assert.Throws<ArgumentException>(() => SourceEditorTemplates.BuildFiles(SourceMemberKind.ApexTrigger, "Trg"));

        var files = SourceEditorTemplates.BuildFiles(SourceMemberKind.ApexTrigger, "Trg", "Account");
        var file = Assert.Single(files);
        Assert.Equal("Trg.trigger", file.FileName);
        Assert.Contains("trigger Trg on Account (before insert)", file.Text);
    }

    [Fact]
    public void BuildFiles_VfPage_ContainsApexPageTag()
    {
        var files = SourceEditorTemplates.BuildFiles(SourceMemberKind.VisualforcePage, "MyPage");
        var file = Assert.Single(files);
        Assert.Equal("MyPage.page", file.FileName);
        Assert.Contains("<apex:page>", file.Text);
    }

    [Fact]
    public void BuildFiles_Lwc_ReturnsJsHtmlCssWithPascalClass()
    {
        var files = SourceEditorTemplates.BuildFiles(SourceMemberKind.LightningComponentBundle, "myComp");
        Assert.Equal(3, files.Count);
        Assert.Equal(new[] { "myComp.js", "myComp.html", "myComp.css" }, files.Select(f => f.FileName));

        var js = files[0].Text;
        Assert.Contains("import { LightningElement } from 'lwc';", js);
        Assert.Contains("export default class MyComp extends LightningElement", js);
        Assert.Contains("<template>", files[1].Text);
    }

    [Fact]
    public void PascalCase_CapitalizesFirstLetterOnly()
    {
        Assert.Equal("MyComp", SourceEditorTemplates.PascalCase("myComp"));
        Assert.Equal("Mycomp2", SourceEditorTemplates.PascalCase("mycomp2"));
        Assert.Equal("", SourceEditorTemplates.PascalCase(""));
    }

    [Fact]
    public void BuildDefaultMeta_Lwc_ContainsBundleAndApiVersion()
    {
        var meta = SourceEditorService.BuildDefaultMeta(SourceMemberKind.LightningComponentBundle, "myComp", "62.0");
        Assert.Contains("<LightningComponentBundle", meta);
        Assert.Contains("<apiVersion>62.0</apiVersion>", meta);
        Assert.Contains("<isExposed>false</isExposed>", meta);
    }
}
