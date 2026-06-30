using NarcoNet.Utilities;

namespace NarcoNet.Tests.Unit;

public class FileHashTests : IDisposable
{
    private readonly string _tempDir;

    public FileHashTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FileHashTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task IdenticalFiles_ProduceSameHash()
    {
        byte[] content = "Hello, World!"u8.ToArray();
        string file1 = Path.Combine(_tempDir, "file1.bin");
        string file2 = Path.Combine(_tempDir, "file2.bin");
        await File.WriteAllBytesAsync(file1, content);
        await File.WriteAllBytesAsync(file2, content);

        string hash1 = await FileHash.HashFile(file1);
        string hash2 = await FileHash.HashFile(file2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task FilesDifferingByOneByte_ProduceDifferentHashes()
    {
        byte[] content1 = "Hello, World!"u8.ToArray();
        byte[] content2 = "Hello, World?"u8.ToArray();
        string file1 = Path.Combine(_tempDir, "file1.bin");
        string file2 = Path.Combine(_tempDir, "file2.bin");
        await File.WriteAllBytesAsync(file1, content1);
        await File.WriteAllBytesAsync(file2, content2);

        string hash1 = await FileHash.HashFile(file1);
        string hash2 = await FileHash.HashFile(file2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task EmptyFile_ProducesValidHash()
    {
        string file = Path.Combine(_tempDir, "empty.bin");
        await File.WriteAllBytesAsync(file, []);

        string hash = await FileHash.HashFile(file);

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public async Task Hash_IsDeterministic()
    {
        byte[] content = new byte[1024];
        new Random(42).NextBytes(content);
        string file = Path.Combine(_tempDir, "deterministic.bin");
        await File.WriteAllBytesAsync(file, content);

        string hash1 = await FileHash.HashFile(file);
        string hash2 = await FileHash.HashFile(file);

        Assert.Equal(hash1, hash2);
    }
}
