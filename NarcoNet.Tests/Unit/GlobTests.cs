using System.Text.RegularExpressions;

using NarcoNet.Utilities;

namespace NarcoNet.Tests.Unit;

public class GlobTests
{
    [Theory]
    [InlineData("*.dll", "foo.dll", true)]
    [InlineData("*.dll", "dir/foo.dll", false)]
    public void SingleWildcard_MatchesSingleSegment(string pattern, string input, bool expected)
    {
        Regex regex = Glob.Create(pattern);
        Assert.Equal(expected, regex.IsMatch(input));
    }

    [Theory]
    [InlineData("**/*.dll", "dir/foo.dll", true)]
    [InlineData("**/*.dll", "deep/nested/foo.dll", true)]
    [InlineData("**/*.dll", "foo.dll", true)]
    public void RecursiveWildcard_MatchesAnyDepth(string pattern, string input, bool expected)
    {
        Regex regex = Glob.Create(pattern);
        Assert.Equal(expected, regex.IsMatch(input));
    }

    [Fact]
    public void CreateNoEnd_DirectoryPrefix_MatchesFilesInside()
    {
        Regex regex = Glob.CreateNoEnd("../BepInEx/plugins/spt");
        Assert.Matches(regex, "../BepInEx/plugins/spt/core.dll");
        Assert.Matches(regex, "../BepInEx/plugins/spt");
    }

    [Fact]
    public void Create_DirectoryPrefix_DoesNotMatchExtension()
    {
        Regex regex = Glob.Create("foo");
        Assert.Matches(regex, "foo");
        Assert.DoesNotMatch(regex, "foobar");
    }

    [Fact]
    public void CreateNoEnd_DirectoryPrefix_MatchesExtension()
    {
        Regex regex = Glob.CreateNoEnd("foo");
        Assert.Matches(regex, "foo");
        Assert.Matches(regex, "foobar");
    }

    [Theory]
    [InlineData("**/cache/", "foo/cache/bar.txt", true)]
    [InlineData("**/cache/", "cache/bar.txt", true)]
    public void TrailingSlash_MatchesDirectoryContents(string pattern, string input, bool expected)
    {
        Regex regex = Glob.Create(pattern);
        Assert.Equal(expected, regex.IsMatch(input));
    }

    [Theory]
    [InlineData("*.log", "test.log", true)]
    [InlineData("*.log", "xlog", false)]
    public void DotEscaping_DotsAreLiteral(string pattern, string input, bool expected)
    {
        Regex regex = Glob.Create(pattern);
        Assert.Equal(expected, regex.IsMatch(input));
    }

    [Theory]
    [InlineData("**/*.log", "debug.log", true)]
    [InlineData("**/*.log", "logs/debug.log", true)]
    [InlineData("**/cache/**", "foo/cache/bar.txt", true)]
    [InlineData("**/*.nosync", "mods/test.nosync", true)]
    [InlineData("**/*.nosync", "test.nosync", true)]
    public void RealConfigPatterns_MatchExpected(string pattern, string input, bool expected)
    {
        Regex regex = Glob.Create(pattern);
        Assert.Equal(expected, regex.IsMatch(input));
    }
}
