using Microsoft.Extensions.Logging.Abstractions;

using NarcoNet.Server.Services;
using NarcoNet.Utilities;

namespace NarcoNet.Tests.Services;

/// <summary>
///     Tests for the server-side ConfigService
///     YAML template should be valid and parsable.
/// </summary>
public class ConfigServiceTests
{
    private readonly ConfigService _configService = new(NullLogger<ConfigService>.Instance);

    [Fact]
    public void DefaultYamlConfig_Deserializes_Successfully()
    {
        (List<SyncPath> syncPaths, List<string> exclusions) =
            _configService.LoadYamlConfig(ConfigService.DefaultYamlConfig);

        // Assert — template must produce non-empty lists
        Assert.NotNull(syncPaths);
        Assert.NotEmpty(syncPaths);
        Assert.NotNull(exclusions);
        Assert.NotEmpty(exclusions);
    }

    [Fact]
    public void DefaultYamlConfig_Contains_Expected_SyncPaths()
    {
        (List<SyncPath> syncPaths, _) =
            _configService.LoadYamlConfig(ConfigService.DefaultYamlConfig);

        // The default template ships with BepInEx/plugins and BepInEx/patchers enabled
        Assert.Contains(syncPaths, sp => sp.Path == "BepInEx/plugins" && sp.Enabled);
        Assert.Contains(syncPaths, sp => sp.Path == "BepInEx/patchers" && sp.Enabled);

        // The optional server mods path is present but disabled
        Assert.Contains(syncPaths, sp => sp.Path == "SPT/user/mods" && !sp.Enabled);
    }

    [Fact]
    public void DefaultYamlConfig_Contains_Expected_Exclusions()
    {
        (_, List<string> exclusions) =
            _configService.LoadYamlConfig(ConfigService.DefaultYamlConfig);

        // SPT core must always be excluded
        Assert.Contains("BepInEx/plugins/spt", exclusions);
        Assert.Contains("BepInEx/patchers/spt-prepatch.dll", exclusions);

        // NarcoNet client plugin excluded (manual update by default)
        Assert.Contains("BepInEx/plugins/MadManBeavis-NarcoNet", exclusions);
    }

    [Fact]
    public void DefaultYamlConfig_SyncPath_Defaults_Match_Record_Defaults()
    {
        (List<SyncPath> syncPaths, _) =
            _configService.LoadYamlConfig(ConfigService.DefaultYamlConfig);

        // Simple string-format sync paths should get the record defaults
        SyncPath plugins = syncPaths.First(sp => sp.Path == "BepInEx/plugins");
        Assert.True(plugins.Enabled);
        Assert.False(plugins.Enforced);
        Assert.False(plugins.Silent);
        Assert.True(plugins.RestartRequired);
    }
}
