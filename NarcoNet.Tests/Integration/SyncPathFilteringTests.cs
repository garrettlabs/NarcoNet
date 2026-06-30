using BepInEx.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NarcoNet.Services;
using NarcoNet.Server.Models;
using NarcoNet.Server.Services;
using NarcoNet.Server.Utilities;
using NarcoNet.Utilities;

using Xunit.Abstractions;

namespace NarcoNet.Tests.Integration;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

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
    public void PreviousServerHashManifest_LoadCompareWriteReload_PreservesLocalEditsAndAppliesServerChanges()
    {
        testOutputHelper.WriteLine("=== TEST: Previous server manifest lifecycle preserves local edits ===\n");

        string tempDir = Path.Combine(Path.GetTempPath(), "narconet-manifest-lifecycle-" + Guid.NewGuid().ToString("N"));
        string manifestPath = Path.Combine(tempDir, "NarcoNet", "PreviousServerHashes.json");
        Directory.CreateDirectory(tempDir);

        const string syncPath = "BepInEx/plugins";
        const string locallyEditedUnchangedServerFile = "BepInEx/plugins/local-edit-preserved.dll";
        const string serverBumpedFile = "BepInEx/plugins/server-bumped.dll";
        const string serverDeletedTrackedFile = "BepInEx/plugins/server-deleted.dll";
        const string userCreatedFile = "BepInEx/plugins/user-created.dll";

        var syncPaths = new List<SyncPath>
        {
            new(syncPath, "Plugins", Enabled: true, Enforced: false, Silent: false, RestartRequired: true)
        };
        var manifestStore = new PreviousServerHashManifestStore(new ManualLogSource("ManifestLifecycleTest"));
        var syncService = new ClientSyncService(
            new ManualLogSource("ManifestLifecycleSyncTest"),
            new ServerModule(new Version("1.0.0"))
        );

        try
        {
            SyncPathModFiles firstRunLocalFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("local-edited-before-baseline", false),
                    [serverBumpedFile] = new("server-old-hash", false),
                    [serverDeletedTrackedFile] = new("server-deleted-old-hash", false),
                    [userCreatedFile] = new("user-created-hash", false)
                }
            };
            SyncPathModFiles firstRunServerFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("server-original-hash", false),
                    [serverBumpedFile] = new("server-old-hash", false),
                    [serverDeletedTrackedFile] = new("server-deleted-old-hash", false)
                }
            };

            SyncPathModFiles? missingManifest = manifestStore.Load(manifestPath);
            Assert.Null(missingManifest);

            syncService.AnalyzeModFiles(
                firstRunLocalFiles,
                firstRunServerFiles,
                missingManifest,
                syncPaths,
                out var firstRunAddedFiles,
                out var firstRunUpdatedFiles,
                out var firstRunRemovedFiles,
                out var firstRunCreatedDirectories
            );

            Assert.Empty(firstRunAddedFiles[syncPath]);
            Assert.Single(firstRunUpdatedFiles[syncPath]);
            Assert.Contains(locallyEditedUnchangedServerFile, firstRunUpdatedFiles[syncPath]);
            Assert.Single(firstRunRemovedFiles[syncPath]);
            Assert.Contains(userCreatedFile, firstRunRemovedFiles[syncPath]);
            Assert.Empty(firstRunCreatedDirectories[syncPath]);

            manifestStore.Save(manifestPath, firstRunServerFiles);
            Assert.True(File.Exists(manifestPath));

            SyncPathModFiles? reloadedManifest = manifestStore.Load(manifestPath);
            Assert.NotNull(reloadedManifest);
            Assert.True(reloadedManifest.ContainsKey(syncPath));
            Assert.Equal(3, reloadedManifest[syncPath].Count);
            Assert.Equal("server-original-hash", reloadedManifest[syncPath][locallyEditedUnchangedServerFile].Hash);
            Assert.Equal("server-old-hash", reloadedManifest[syncPath][serverBumpedFile].Hash);
            Assert.Equal("server-deleted-old-hash", reloadedManifest[syncPath][serverDeletedTrackedFile].Hash);

            SyncPathModFiles secondRunLocalFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("local-edit-after-relaunch", false),
                    [serverBumpedFile] = new("server-old-hash", false),
                    [serverDeletedTrackedFile] = new("server-deleted-old-hash", false),
                    [userCreatedFile] = new("user-created-hash", false)
                }
            };
            SyncPathModFiles secondRunServerFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("server-original-hash", false),
                    [serverBumpedFile] = new("server-new-hash", false)
                }
            };

            syncService.AnalyzeModFiles(
                secondRunLocalFiles,
                secondRunServerFiles,
                reloadedManifest,
                syncPaths,
                out var secondRunAddedFiles,
                out var secondRunUpdatedFiles,
                out var secondRunRemovedFiles,
                out var secondRunCreatedDirectories
            );

            Assert.Empty(secondRunAddedFiles[syncPath]);
            Assert.Single(secondRunUpdatedFiles[syncPath]);
            Assert.Contains(serverBumpedFile, secondRunUpdatedFiles[syncPath]);
            Assert.DoesNotContain(locallyEditedUnchangedServerFile, secondRunUpdatedFiles[syncPath]);
            Assert.Single(secondRunRemovedFiles[syncPath]);
            Assert.Contains(serverDeletedTrackedFile, secondRunRemovedFiles[syncPath]);
            Assert.DoesNotContain(userCreatedFile, secondRunRemovedFiles[syncPath]);
            Assert.Empty(secondRunCreatedDirectories[syncPath]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PreviousServerHashManifest_CorruptInputFallsBackAndSuccessfulWriteReplacesIt()
    {
        testOutputHelper.WriteLine("=== TEST: Corrupt previous server manifest falls back then rewrites ===\n");

        string tempDir = Path.Combine(Path.GetTempPath(), "narconet-manifest-corrupt-" + Guid.NewGuid().ToString("N"));
        string manifestPath = Path.Combine(tempDir, "PreviousServerHashes.json");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(manifestPath, "{ not valid json");

        const string syncPath = "BepInEx/plugins";
        const string locallyEditedUnchangedServerFile = "BepInEx/plugins/local-edit-preserved.dll";
        var syncPaths = new List<SyncPath>
        {
            new(syncPath, "Plugins", Enabled: true, Enforced: false, Silent: false, RestartRequired: true)
        };
        var manifestStore = new PreviousServerHashManifestStore(new ManualLogSource("CorruptManifestTest"));
        var syncService = new ClientSyncService(
            new ManualLogSource("CorruptManifestSyncTest"),
            new ServerModule(new Version("1.0.0"))
        );

        try
        {
            SyncPathModFiles? corruptManifest = manifestStore.Load(manifestPath);
            Assert.Null(corruptManifest);

            SyncPathModFiles localFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("local-edited-hash", false)
                }
            };
            SyncPathModFiles serverFiles = new()
            {
                [syncPath] = new Dictionary<string, ModFile>
                {
                    [locallyEditedUnchangedServerFile] = new("server-original-hash", false)
                }
            };

            syncService.AnalyzeModFiles(
                localFiles,
                serverFiles,
                corruptManifest,
                syncPaths,
                out var addedFiles,
                out var updatedFiles,
                out var removedFiles,
                out var createdDirectories
            );

            Assert.Empty(addedFiles[syncPath]);
            Assert.Single(updatedFiles[syncPath]);
            Assert.Contains(locallyEditedUnchangedServerFile, updatedFiles[syncPath]);
            Assert.Empty(removedFiles[syncPath]);
            Assert.Empty(createdDirectories[syncPath]);

            manifestStore.Save(manifestPath, serverFiles);
            SyncPathModFiles? reloadedManifest = manifestStore.Load(manifestPath);

            Assert.NotNull(reloadedManifest);
            Assert.True(reloadedManifest.ContainsKey(syncPath));
            Assert.Equal("server-original-hash", reloadedManifest[syncPath][locallyEditedUnchangedServerFile].Hash);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
