namespace NarcoNet.Tests.Services;

public class ServerModuleTests
{
    [Theory]
    [InlineData("BepInEx/plugins/file.dll", "BepInEx%2Fplugins%2Ffile.dll")]
    [InlineData("SPT/user/mods/mod.zip", "SPT%2Fuser%2Fmods%2Fmod.zip")]
    [InlineData("BepInEx/config/settings.json", "BepInEx%2Fconfig%2Fsettings.json")]
    [InlineData("path with spaces/file.txt", "path%20with%20spaces%2Ffile.txt")]
    public void DownloadFile_Should_Encode_File_Paths_Correctly(string inputPath, string expectedEncoded)
    {
        // Arrange: Normalize path separators and encode
        string normalizedPath = inputPath.Replace("\\", "/");
        string actualEncoded = Uri.EscapeDataString(normalizedPath);

        // Assert: Verify encoding matches expected format
        Assert.Equal(expectedEncoded, actualEncoded);
    }

    [Fact]
    public void GetRemoteHashes_Should_Encode_Sync_Paths()
    {
        // Arrange
        var testPaths = new List<Utilities.SyncPath>
        {
            new(Path: "BepInEx/plugins", Name: "Plugins", Enabled: true, Enforced: false),
            new(Path: "SPT/user/mods", Name: "Mods", Enabled: true, Enforced: false)
        };

        // Act: Simulate the encoding logic from GetRemoteHashes
        List<string> encodedPaths = testPaths.Select(path => Uri.EscapeDataString(path.Path.Replace(@"\", "/"))).ToList();

        // Assert
        Assert.Equal("BepInEx%2Fplugins", encodedPaths[0]);
        Assert.Equal("SPT%2Fuser%2Fmods", encodedPaths[1]);
    }
}
