using NarcoNet.Utilities;

namespace NarcoNet.Tests.Unit;

/// <summary>
///     Tests for the profile-bypass matching logic that decides whether a client's active
///     profile is configured to skip NarcoNet sync entirely.
/// </summary>
public class ProfileBypassTests
{
    [Theory]
    [InlineData("64f1a2b3c4d5e6f7a8b9c0d1", "64f1a2b3c4d5e6f7a8b9c0d1")]            // exact id
    [InlineData("64f1a2b3c4d5e6f7a8b9c0d1", "64f1a2b3c4d5e6f7a8b9c0d1.json")]       // configured with .json
    [InlineData("64f1a2b3c4d5e6f7a8b9c0d1", "user/profiles/64f1a2b3c4d5e6f7a8b9c0d1.json")] // configured as path
    public void ShouldBypass_True_When_Active_Matches_Configured(string activeProfileId, string configured)
    {
        Assert.True(ProfileBypass.ShouldBypass(activeProfileId, [configured]));
    }

    [Fact]
    public void ShouldBypass_False_When_No_Active_Profile()
    {
        Assert.False(ProfileBypass.ShouldBypass(null, ["64f1a2b3c4d5e6f7a8b9c0d1"]));
        Assert.False(ProfileBypass.ShouldBypass("", ["64f1a2b3c4d5e6f7a8b9c0d1"]));
    }

    [Fact]
    public void ShouldBypass_False_When_Not_In_List()
    {
        Assert.False(ProfileBypass.ShouldBypass("active-profile", ["someone-else"]));
        Assert.False(ProfileBypass.ShouldBypass("active-profile", null));
        Assert.False(ProfileBypass.ShouldBypass("active-profile", []));
    }

    [Theory]
    [InlineData("user/profiles/abc.json", "abc")]
    [InlineData(@"user\profiles\abc.json", "abc")]
    [InlineData("abc.json", "abc")]
    [InlineData("abc", "abc")]
    [InlineData("  abc  ", "abc")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeProfileIdentifier_Reduces_To_File_Stem(string? input, string? expected)
    {
        Assert.Equal(expected, ProfileBypass.NormalizeProfileIdentifier(input));
    }
}
