using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

public class ImportValueCoercionTests
{
    private static DataIoField Field(string type) =>
        new("F", "F", type, true, true, true, false, false, false, Array.Empty<string>());

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("No", false)]
    public void Convert_BooleanVariants(string raw, bool expected)
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field("boolean"), raw, emptyAsNull: false);

        Assert.Equal(ImportValueKind.Value, kind);
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Fact]
    public void Convert_BooleanInvalid_ReturnsError()
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field("boolean"), "maybe", emptyAsNull: false);

        Assert.Equal(ImportValueKind.Error, kind);
        Assert.Null(value);
        Assert.Contains("maybe", error);
    }

    [Theory]
    [InlineData("double", "1.5", 1.5)]
    [InlineData("currency", "100", 100)]
    [InlineData("int", "-42", -42)]
    [InlineData("percent", "0.25", 0.25)]
    public void Convert_Numeric(string type, string raw, decimal expected)
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field(type), raw, emptyAsNull: false);

        Assert.Equal(ImportValueKind.Value, kind);
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    [Fact]
    public void Convert_NumericInvalid_ReturnsError()
    {
        var (kind, _, error) = ImportValueCoercion.Convert(Field("double"), "1,5", emptyAsNull: false);

        Assert.Equal(ImportValueKind.Error, kind);
        Assert.Contains("1,5", error);
    }

    [Fact]
    public void Convert_String_PassThrough()
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field("string"), "hello", emptyAsNull: false);

        Assert.Equal(ImportValueKind.Value, kind);
        Assert.Equal("hello", value);
        Assert.Null(error);
    }

    [Fact]
    public void Convert_EmptyCell_OmitByDefault()
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field("string"), string.Empty, emptyAsNull: false);

        Assert.Equal(ImportValueKind.Omit, kind);
        Assert.Null(value);
        Assert.Null(error);
    }

    [Fact]
    public void Convert_EmptyCell_AsNullWhenEnabled()
    {
        var (kind, value, error) = ImportValueCoercion.Convert(Field("string"), string.Empty, emptyAsNull: true);

        Assert.Equal(ImportValueKind.Null, kind);
        Assert.Null(value);
        Assert.Null(error);
    }
}
