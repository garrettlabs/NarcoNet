using System.Text.RegularExpressions;

using NarcoNet.Utilities;

namespace NarcoNet.Tests.Unit;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public class SyncTests
{
    [Fact]
    public void GetAddedFiles_Should_Exclude_Directories()
    {
        // Arrange
        var syncPaths = new List<SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false)
        };

        var localModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>()
        };

        var remoteModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/file.dll"] = new ModFile("hash123", Directory: false),
                ["BepInEx/plugins/Scripts"] = new ModFile("", Directory: true),
                ["BepInEx/plugins/another.dll"] = new ModFile("hash456", Directory: false)
            }
        };

        // Act
        var addedFiles = Sync.GetAddedFiles(syncPaths, localModFiles, remoteModFiles);

        // Assert
        Assert.Equal(2, addedFiles["BepInEx/plugins"].Count);
        Assert.Contains("BepInEx/plugins/file.dll", addedFiles["BepInEx/plugins"]);
        Assert.Contains("BepInEx/plugins/another.dll", addedFiles["BepInEx/plugins"]);
        Assert.DoesNotContain("BepInEx/plugins/Scripts", addedFiles["BepInEx/plugins"]);
    }

    [Fact]
    public void GetUpdatedFiles_Should_Exclude_Directories()
    {
        // Arrange
        var syncPaths = new List<SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false)
        };

        var localModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/file.dll"] = new ModFile("oldhash", Directory: false),
                ["BepInEx/plugins/config"] = new ModFile("", Directory: true)
            }
        };

        var remoteModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/file.dll"] = new ModFile("newhash", Directory: false),
                ["BepInEx/plugins/config"] = new ModFile("", Directory: true)
            }
        };

        // Act
        var updatedFiles = Sync.GetUpdatedFiles(syncPaths, localModFiles, remoteModFiles);

        // Assert
        Assert.Single(updatedFiles["BepInEx/plugins"]);
        Assert.Contains("BepInEx/plugins/file.dll", updatedFiles["BepInEx/plugins"]);
        Assert.DoesNotContain("BepInEx/plugins/config", updatedFiles["BepInEx/plugins"]);
    }

    [Fact]
    public void GetCreatedDirectories_Should_Only_Include_Directories()
    {
        // Arrange
        string basePath = Directory.GetCurrentDirectory();
        var syncPaths = new List<SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false)
        };

        var localModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>()
        };

        var remoteModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/file.dll"] = new ModFile("hash123", Directory: false),
                ["BepInEx/plugins/Scripts"] = new ModFile("", Directory: true),
                ["BepInEx/plugins/config"] = new ModFile("", Directory: true)
            }
        };

        // Act
        var createdDirectories = Sync.GetCreatedDirectories(basePath, syncPaths, localModFiles, remoteModFiles);

        // Assert
        Assert.Equal(2, createdDirectories["BepInEx/plugins"].Count);
        Assert.Contains("BepInEx/plugins/Scripts", createdDirectories["BepInEx/plugins"]);
        Assert.Contains("BepInEx/plugins/config", createdDirectories["BepInEx/plugins"]);
        Assert.DoesNotContain("BepInEx/plugins/file.dll", createdDirectories["BepInEx/plugins"]);
    }

    [Fact]
    public void Directory_Sync_Integration_Test()
    {
        // This test simulates the real-world scenario from the log file where
        // directories like "Scripts" and "config" were being treated as files to download

        // Arrange
        var syncPaths = new List<SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false)
        };

        var localModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>()
        };

        var remoteModFiles = new SyncPathModFiles
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                // Real-world case: UnityExplorer Scripts directory
                ["BepInEx/plugins/sinai-dev-UnityExplorer/Scripts"] = new ModFile("", Directory: true),
                ["BepInEx/plugins/sinai-dev-UnityExplorer/UnityExplorer.BIE5.Mono.dll"] = new ModFile("hash1", Directory: false),

                // Real-world case: QuestTracker config directory
                ["BepInEx/plugins/DrakiaXYZ-QuestTracker/config"] = new ModFile("", Directory: true),
                ["BepInEx/plugins/DrakiaXYZ-QuestTracker/QuestTracker.dll"] = new ModFile("hash2", Directory: false)
            }
        };

        // Act
        var addedFiles = Sync.GetAddedFiles(syncPaths, localModFiles, remoteModFiles);
        var updatedFiles = Sync.GetUpdatedFiles(syncPaths, localModFiles, remoteModFiles);
        var createdDirectories = Sync.GetCreatedDirectories(Directory.GetCurrentDirectory(), syncPaths, localModFiles, remoteModFiles);

        // Assert: Files should only contain actual files, not directories
        var allFilesToDownload = addedFiles["BepInEx/plugins"]
            .Concat(updatedFiles["BepInEx/plugins"])
            .ToList();

        Assert.Equal(2, allFilesToDownload.Count);
        Assert.Contains("BepInEx/plugins/sinai-dev-UnityExplorer/UnityExplorer.BIE5.Mono.dll", allFilesToDownload);
        Assert.Contains("BepInEx/plugins/DrakiaXYZ-QuestTracker/QuestTracker.dll", allFilesToDownload);

        // These should NOT be in files to download
        Assert.DoesNotContain("BepInEx/plugins/sinai-dev-UnityExplorer/Scripts", allFilesToDownload);
        Assert.DoesNotContain("BepInEx/plugins/DrakiaXYZ-QuestTracker/config", allFilesToDownload);

        // Assert: Directories should be in createdDirectories
        Assert.Equal(2, createdDirectories["BepInEx/plugins"].Count);
        Assert.Contains("BepInEx/plugins/sinai-dev-UnityExplorer/Scripts", createdDirectories["BepInEx/plugins"]);
        Assert.Contains("BepInEx/plugins/DrakiaXYZ-QuestTracker/config", createdDirectories["BepInEx/plugins"]);
    }

    [Theory]
    [InlineData("**/*.dll", "BepInEx/plugins/test.dll", true)]
    [InlineData("**/*.dll", "BepInEx/plugins/test.txt", false)]
    [InlineData("**/*.log", "logs/debug.log", true)]
    [InlineData("**/cache/**", "foo/cache/bar.txt", true)]
    [InlineData("**/*.nosync", "mods/test.nosync", true)]
    public void IsExcluded_WithGlobPatterns(string pattern, string path, bool expected)
    {
        List<Regex> exclusions = [Glob.CreateNoEnd(pattern)];
        Assert.Equal(expected, Sync.IsExcluded(exclusions, path));
    }

    [Fact]
    public void IsExcluded_BackslashNormalization()
    {
        // The exclusion pattern uses forward slashes; the path uses backslashes
        List<Regex> exclusions = [Glob.CreateNoEnd("BepInEx/plugins/spt")];
        Assert.True(Sync.IsExcluded(exclusions, @"BepInEx\plugins\spt\core.dll"));
    }

    [Fact]
    public void GetRemovedFiles_SingleFileSyncPath_PreservedWhenServerReportsIt_RemovedWhenReportEmpty()
    {
        // Issue #9 regression (client symptom): a single-file managed-DLL syncPath must
        // not be flagged for deletion when the server correctly reports it. The wrongful
        // deletion only happens when the server lists the file as absent (empty report) —
        // which the server-side SyncService.GetFilesInDirectoryAsync fix prevents.
        const string syncPath = "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll";

        var syncPaths = new List<SyncPath>
        {
            new(Path: syncPath, Name: "DynamicMaps managed DLL", Enabled: true, Enforced: false)
        };

        var localModFiles = new SyncPathModFiles
        {
            [syncPath] = new Dictionary<string, ModFile>
            {
                [syncPath] = new ModFile("hash123", Directory: false)
            }
        };

        var emptyServerReport = new SyncPathModFiles
        {
            [syncPath] = new Dictionary<string, ModFile>()
        };

        var correctServerReport = new SyncPathModFiles
        {
            [syncPath] = new Dictionary<string, ModFile>
            {
                [syncPath] = new ModFile("hash123", Directory: false)
            }
        };

        // Empty server report (pre-fix server behaviour) => file wrongly flagged for removal
        var removedWhenEmpty = Sync.GetRemovedFiles(syncPaths, localModFiles, emptyServerReport);
        Assert.Contains(syncPath, removedWhenEmpty[syncPath]);

        // Correct server report (post-fix server behaviour) => file preserved
        var removedWhenReported = Sync.GetRemovedFiles(syncPaths, localModFiles, correctServerReport);
        Assert.DoesNotContain(syncPath, removedWhenReported[syncPath]);
    }
}
