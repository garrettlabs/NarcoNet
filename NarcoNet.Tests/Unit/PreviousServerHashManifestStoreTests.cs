using NarcoNet.Services;
using NarcoNet.Utilities;

namespace NarcoNet.Tests.Unit;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public class PreviousServerHashManifestStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public PreviousServerHashManifestStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"NarcoNetManifestTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void Load_WhenManifestFileIsMissing_ReturnsNull()
    {
        var store = new PreviousServerHashManifestStore();
        string manifestPath = Path.Combine(_tempRoot, "missing", "PreviousServerHashes.json");

        SyncPathModFiles? result = store.Load(manifestPath);

        Assert.Null(result);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSyncPathModFilesManifest()
    {
        var store = new PreviousServerHashManifestStore();
        string manifestPath = Path.Combine(_tempRoot, "state", "PreviousServerHashes.json");
        SyncPathModFiles expected = new()
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/NarcoNet.dll"] = new("hash-1"),
                ["BepInEx/plugins/empty-folder"] = new("", true)
            },
            ["user/mods"] = new Dictionary<string, ModFile>
            {
                ["user/mods/example/config.json"] = new("hash-2")
            }
        };

        store.Save(manifestPath, expected);
        SyncPathModFiles? result = store.Load(manifestPath);

        Assert.NotNull(result);
        Assert.Equal(expected.Keys.OrderBy(key => key), result.Keys.OrderBy(key => key));
        Assert.Equal("hash-1", result["BepInEx/plugins"]["BepInEx/plugins/NarcoNet.dll"].Hash);
        Assert.False(result["BepInEx/plugins"]["BepInEx/plugins/NarcoNet.dll"].Directory);
        Assert.True(result["BepInEx/plugins"]["BepInEx/plugins/empty-folder"].Directory);
        Assert.Equal("hash-2", result["user/mods"]["user/mods/example/config.json"].Hash);
    }

    [Fact]
    public void Load_WhenManifestJsonIsCorrupt_ReturnsNull()
    {
        var store = new PreviousServerHashManifestStore();
        string manifestPath = Path.Combine(_tempRoot, "PreviousServerHashes.json");
        File.WriteAllText(manifestPath, "{ this is not valid json");

        SyncPathModFiles? result = store.Load(manifestPath);

        Assert.Null(result);
    }

    [Fact]
    public void Save_ReplacesExistingManifestWithCompleteCurrentServerManifest()
    {
        var store = new PreviousServerHashManifestStore();
        string manifestPath = Path.Combine(_tempRoot, "state", "PreviousServerHashes.json");
        SyncPathModFiles staleManifest = new()
        {
            ["BepInEx/plugins"] = new Dictionary<string, ModFile>
            {
                ["BepInEx/plugins/stale.dll"] = new("stale-hash")
            }
        };
        SyncPathModFiles replacementManifest = new()
        {
            ["user/mods"] = new Dictionary<string, ModFile>
            {
                ["user/mods/current.dll"] = new("current-hash")
            }
        };

        store.Save(manifestPath, staleManifest);
        store.Save(manifestPath, replacementManifest);
        SyncPathModFiles? result = store.Load(manifestPath);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.False(result.ContainsKey("BepInEx/plugins"));
        Assert.True(result.ContainsKey("user/mods"));
        Assert.Equal("current-hash", result["user/mods"]["user/mods/current.dll"].Hash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }
}
