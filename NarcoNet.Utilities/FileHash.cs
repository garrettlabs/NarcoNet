using System.Security.Cryptography;

namespace NarcoNet.Utilities;

public static class FileHash
{
    public static async Task<string> HashFile(string filename)
    {
        byte[] data;
        using (FileStream fs = new(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            data = new byte[fs.Length];
            int bytesRead = await fs.ReadAsync(data, 0, data.Length);
            if (bytesRead < data.Length)
                throw new IOException("Could not read enough data");
        }

        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
