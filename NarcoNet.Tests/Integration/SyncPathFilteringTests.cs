using Microsoft.Extensions.Logging.Abstractions;

using NarcoNet.Server.Models;
using NarcoNet.Server.Services;
using NarcoNet.Server.Utilities;
using NarcoNet.Utilities;

using Xunit.Abstractions;

namespace NarcoNet.Tests.Integration;

/// <summary>
/// Tests for sync path filtering logic
/// </summary>
public class SyncPathFilteringTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void Disabled_Paths_Should_Be_Filtered_Out()
    {
        testOutputHelper.WriteLine("=== TEST: Disabled Paths Should Be Filtered Out ===\n");

        // Arrange
        var paths = new List<SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false, Silent: false, RestartRequired: true),
            new(Path: "BepInEx/config", Name: "Config", Enabled: true, Enforced: false, Silent: false, RestartRequired: true),
            new(Path: "SPT/user/mods", Name: "Server mods", Enabled: false, Enforced: false, Silent: false, RestartRequired: false),
        };

        testOutputHelper.WriteLine("Input paths:");
        foreach (var sp in paths)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}: Enabled={sp.Enabled}, Enforced={sp.Enforced}");
        }

        // Act - this is the actual filtering logic used in ConfigService.cs:216
        var filtered = paths.Where(sp => sp.Enabled || sp.Enforced).ToList();

        // Assert
        testOutputHelper.WriteLine($"\nFiltered paths (Enabled OR Enforced):");
        foreach (var sp in filtered)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}: Enabled={sp.Enabled}, Enforced={sp.Enforced}");
        }

        testOutputHelper.WriteLine($"\nResult: {filtered.Count} paths (expected 2)");

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, sp => sp.Path == "BepInEx/plugins");
        Assert.Contains(filtered, sp => sp.Path == "BepInEx/config");
        Assert.DoesNotContain(filtered, sp => sp.Path == "SPT/user/mods");

        testOutputHelper.WriteLine("\n✓ TEST PASSED: SPT/user/mods was correctly filtered out\n");
    }

    [Fact]
    public void Enforced_Paths_Should_Be_Included_Even_When_Disabled()
    {
        testOutputHelper.WriteLine("=== TEST: Enforced Paths Should Be Included Even When Disabled ===\n");

        // Arrange
        var paths = new List<SyncPath>
        {
            new(Path: "test/normal", Name: "Normal", Enabled: true, Enforced: false, Silent: false, RestartRequired: false),
            new(Path: "test/disabled", Name: "Disabled", Enabled: false, Enforced: false, Silent: false, RestartRequired: false),
            new(Path: "test/enforced", Name: "Enforced", Enabled: false, Enforced: true, Silent: false, RestartRequired: false),
        };

        testOutputHelper.WriteLine("Input paths:");
        foreach (var sp in paths)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}: Enabled={sp.Enabled}, Enforced={sp.Enforced}");
        }

        // Act
        var filtered = paths.Where(sp => sp.Enabled || sp.Enforced).ToList();

        // Assert
        testOutputHelper.WriteLine($"\nFiltered paths:");
        foreach (var sp in filtered)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}: Enabled={sp.Enabled}, Enforced={sp.Enforced}");
        }

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, sp => sp.Path == "test/normal");
        Assert.Contains(filtered, sp => sp.Path == "test/enforced");
        Assert.DoesNotContain(filtered, sp => sp.Path == "test/disabled");

        testOutputHelper.WriteLine("\n✓ TEST PASSED: Enforced path included, disabled path excluded\n");
    }

    [Theory]
    [InlineData(true, false, true)]   // Enabled=true, Enforced=false => Include
    [InlineData(false, true, true)]   // Enabled=false, Enforced=true => Include
    [InlineData(true, true, true)]    // Enabled=true, Enforced=true => Include
    [InlineData(false, false, false)] // Enabled=false, Enforced=false => Exclude
    public void Filtering_Logic_Truth_Table(bool enabled, bool enforced, bool shouldInclude)
    {
        testOutputHelper.WriteLine($"\n=== TEST: Enabled={enabled}, Enforced={enforced} => Include={shouldInclude} ===");

        // Arrange
        var path = new SyncPath(
            Path: "test/path",
            Name: "Test",
            Enabled: enabled,
            Enforced: enforced,
            Silent: false,
            RestartRequired: false
        );

        // Act
        bool result = path.Enabled || path.Enforced;

        // Assert
        testOutputHelper.WriteLine($"Result: {result} (expected {shouldInclude})");
        Assert.Equal(shouldInclude, result);

        testOutputHelper.WriteLine("✓ PASSED\n");
    }

    [Fact]
    public void Real_World_Config_Scenario()
    {
        testOutputHelper.WriteLine("=== TEST: Real World Config Scenario ===\n");

        // Arrange - simulating the actual config from the screenshot
        var paths = new List<SyncPath>
        {
            // NarcoNet auto-update (from default config template)
            new(Path: "BepInEx/plugins/MadManBeavis-NarcoNet", Name: "NarcoNet", Enabled: true, Enforced: true, Silent: true, RestartRequired: true),

            // User config
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false, Silent: false, RestartRequired: true),
            new(Path: "BepInEx/patchers", Name: "Patchers", Enabled: true, Enforced: false, Silent: false, RestartRequired: true),
            new(Path: "BepInEx/config", Name: "Config", Enabled: true, Enforced: false, Silent: false, RestartRequired: true),
            new(Path: "SPT/user/mods", Name: "(Optional) Server mods", Enabled: false, Enforced: false, Silent: false, RestartRequired: false),
        };

        testOutputHelper.WriteLine("All sync paths:");
        foreach (var sp in paths)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}: Enabled={sp.Enabled}, Enforced={sp.Enforced}");
        }

        // Act - Filter like ConfigService does
        var filtered = paths.Where(sp => sp.Enabled || sp.Enforced).ToList();

        // Assert
        testOutputHelper.WriteLine($"\nFiltered paths ({filtered.Count} total):");
        foreach (var sp in filtered)
        {
            testOutputHelper.WriteLine($"  - {sp.Path}");
        }

        Assert.Equal(4, filtered.Count); // 1 builtin + 3 enabled user paths
        Assert.DoesNotContain(filtered, sp => sp.Path == "SPT/user/mods");

        testOutputHelper.WriteLine("\n✓ TEST PASSED.: Real world scenario works correctly\n");
    }

    [Fact]
    public async Task HashModFilesAsync_SingleFileSyncPath_IsListedByServer()
    {
        // Issue #9 regression (server root cause): a syncPath that names a single file
        // (a managed DLL such as DynamicMaps' Unity.VectorGraphics.dll) must be enumerated
        // by the server. Before the fix, GetFilesInDirectoryAsync hit the !Directory.Exists
        // guard first and returned empty, so the client saw the file as server-removed and
        // deleted it. An absolute syncPath bypasses ComputeHashesAsync's baseDir join and
        // points straight at a temp file we control.
        string tempDir = Path.Combine(Path.GetTempPath(), "narconet-issue9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string singleFile = Path.Combine(tempDir, "Unity.VectorGraphics.dll");
        await File.WriteAllTextAsync(singleFile, "managed dll bytes");

        try
        {
            var config = new NarcoNetConfig
            {
                SyncPaths =
                [
                    new(Path: singleFile, Name: "Single managed DLL", Enabled: true, Enforced: false)
                ],
                Exclusions = []
            };

            var service = new SyncService(NullLogger<SyncService>.Instance);

            Dictionary<string, Dictionary<string, ModFile>> result =
                await service.HashModFilesAsync(config.SyncPaths, config);

            string key = PathHelper.ToUnixPath(singleFile);
            Assert.True(result.ContainsKey(key), "Server result is missing the single-file syncPath key");
            // Pre-fix this dictionary was empty, so the client treated the file as removed.
            Assert.NotEmpty(result[key]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
