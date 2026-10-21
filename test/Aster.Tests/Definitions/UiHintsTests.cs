using System.Reflection;
using Aster.Core.Models.Definitions;

namespace Aster.Tests.Definitions;

/// <summary>
/// #107: the standard UI hint vocabulary is complete, distinct, and spelled as documented in docs/ui-hints.md.
/// </summary>
public sealed class UiHintsTests
{
    private static readonly string[] Constants = typeof(UiHints)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.IsLiteral)
        .Select(static field => (string)field.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void All_ListsEveryConstantOnce()
    {
        Assert.Equal(Constants.Order(StringComparer.Ordinal), UiHints.All.Order(StringComparer.Ordinal));
        Assert.Equal(UiHints.All.Count, UiHints.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Hints_AreLowerKebabCase()
    {
        Assert.All(UiHints.All, static hint => Assert.Matches("^[a-z]+(-[a-z]+)*$", hint));
    }

    [Fact]
    public void Docs_ListEveryHint()
    {
        var docs = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "ui-hints.md"));

        Assert.All(UiHints.All, hint => Assert.Contains($"| `{hint}` |", docs, StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aster.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
