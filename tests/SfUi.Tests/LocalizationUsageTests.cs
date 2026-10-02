using System.Text.RegularExpressions;
using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>
/// XAML / C# で使用されているローカライズキーがすべて辞書に存在することを検証する。
/// （{loc:Tr Key} と UiText.T("Key") を走査）
/// </summary>
public class LocalizationUsageTests
{
    [Fact]
    public void Xaml_Files_UseOnlyDefinedKeys()
    {
        var root = FindRepoRoot();
        var regex = new Regex(@"loc:Tr\s+([A-Za-z0-9_]+)");
        var missing = new List<string>();

        foreach (var file in EnumerateSources(Path.Combine(root, "src", "SfUi.App"), "*.xaml"))
        {
            foreach (Match match in regex.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (!UiText.HasKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Code_Files_UseOnlyDefinedKeys()
    {
        var root = FindRepoRoot();
        var regex = new Regex(@"UiText\.T\(\s*""([A-Za-z0-9_]+)""");
        var missing = new List<string>();

        foreach (var directory in new[] { Path.Combine(root, "src", "SfUi.App"), Path.Combine(root, "src", "SfUi.Core") })
        {
            foreach (var file in EnumerateSources(directory, "*.cs"))
            {
                foreach (Match match in regex.Matches(File.ReadAllText(file)))
                {
                    var key = match.Groups[1].Value;
                    if (!UiText.HasKey(key))
                    {
                        missing.Add($"{Path.GetFileName(file)}: {key}");
                    }
                }
            }
        }

        Assert.Empty(missing);
    }

    private static IEnumerable<string> EnumerateSources(string directory, string pattern)
        => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SfUi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("リポジトリルート（SfUi.sln）が見つかりません");
    }
}
