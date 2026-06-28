using NarcoNet.Server.Services;
using NarcoNet.Utilities;

namespace NarcoNet.Tests.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"NarcoNetConfigTests-{Guid.NewGuid():N}");

    public ConfigServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task LoadConfigAsync_WhenYamlHasIgnoredProfiles_LoadsProfileIds()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "config.yaml"), """
            syncPaths:
              - ../BepInEx/plugins
            ignoredProfiles:
              - profile-one
              - user/profiles/profile-two.json
            exclusions: []
            """);

        var service = new ConfigService();

        // Act
        var config = await service.LoadConfigAsync(_tempDirectory);

        // Assert
        Assert.Equal(["profile-one", "user/profiles/profile-two.json"], config.IgnoredProfiles);
        Assert.Contains(config.SyncPaths, syncPath => syncPath.Path == "../BepInEx/plugins");
    }

    [Fact]
    public async Task LoadConfigAsync_WhenYamlOmitsIgnoredProfiles_DefaultsToEmptyList()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "config.yaml"), """
            syncPaths:
              - ../BepInEx/plugins
            exclusions: []
            """);

        var service = new ConfigService();

        // Act
        var config = await service.LoadConfigAsync(_tempDirectory);

        // Assert
        Assert.NotNull(config.IgnoredProfiles);
        Assert.Empty(config.IgnoredProfiles);
    }

    [Fact]
    public async Task LoadConfigAsync_WhenJsonHasIgnoredProfiles_LoadsProfileIds()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "config.json"), """
            {
              "syncPaths": ["../BepInEx/plugins"],
              "ignoredProfiles": ["profile-one", "profile-two"],
              "exclusions": []
            }
            """);

        var service = new ConfigService();

        // Act
        var config = await service.LoadConfigAsync(_tempDirectory);

        // Assert
        Assert.Equal(["profile-one", "profile-two"], config.IgnoredProfiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

public class ProfileBypassTests
{
    [Theory]
    [InlineData("profile-one", "profile-one")]
    [InlineData("profile-one", "profile-one.json")]
    [InlineData("profile-one", "user/profiles/profile-one.json")]
    [InlineData("profile-one", "user\\profiles\\profile-one.json")]
    public void ShouldBypass_WhenConfiguredIdentifierMatchesProfileStem_ReturnsTrue(
        string activeProfileId,
        string configuredProfileId)
    {
        // Act
        bool shouldBypass = ProfileBypass.ShouldBypass(activeProfileId, [configuredProfileId]);

        // Assert
        Assert.True(shouldBypass);
    }

    [Fact]
    public void ShouldBypass_WhenConfiguredIdentifierDoesNotMatch_ReturnsFalse()
    {
        // Act
        bool shouldBypass = ProfileBypass.ShouldBypass("profile-one", ["profile-two"]);

        // Assert
        Assert.False(shouldBypass);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldBypass_WhenActiveProfileIsMissing_ReturnsFalse(string? activeProfileId)
    {
        // Act
        bool shouldBypass = ProfileBypass.ShouldBypass(activeProfileId, ["profile-one"]);

        // Assert
        Assert.False(shouldBypass);
    }
}
