using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using NarcoNet.Server.Models;
using NarcoNet.Server.Utilities;
using NarcoNet.Utilities;

using SPTarkov.DI.Annotations;

namespace NarcoNet.Server.Services;

/// <summary>
///     Service for handling file synchronization operations
/// </summary>
[Injectable]
public class SyncService
{
    private readonly SemaphoreSlim _limiter = new(1024, 1024);
    private readonly SemaphoreSlim _hashCacheLock = new(1, 1);
    private readonly ILogger<SyncService> _logger;
    private volatile Dictionary<string, Dictionary<string, ModFile>>? _hashCache;

    public SyncService(ILogger<SyncService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Invalidate the hash cache so the next request recomputes fresh hashes.
    ///     Called by FileWatcherService when files change on disk.
    /// </summary>
    public void InvalidateHashCache()
    {
        _hashCache = null;
        _logger.LogDebug("Hash cache invalidated");
    }

    /// <summary>
    ///     Get all files in a directory recursively, respecting exclusions
    /// </summary>
    private async Task<List<string>> GetFilesInDirectoryAsync(string baseDir, string dir, NarcoNetConfig config)
    {
        if (!Directory.Exists(dir))
        {
            _logger.LogWarning("Directory '{Dir}' does not exist", dir);
            return [];
        }

        FileInfo fileInfo = new(dir);
        if (fileInfo.Attributes.HasFlag(FileAttributes.Normal) || File.Exists(dir))
        {
            return [dir];
        }

        List<string> files =
        [
        ];
        DirectoryInfo dirInfo = new(dir);

        // Get files in current directory
        foreach (FileInfo file in dirInfo.GetFiles())
        {
            string filePath = file.FullName;
            if (IsExcluded(filePath, config.CompiledExclusions, baseDir))
            {
                continue;
            }

            files.Add(filePath);
        }

        // Get subdirectories
        foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
        {
            string subDirPath = subDir.FullName;
            if (IsExcluded(subDirPath, config.CompiledExclusions, baseDir))
            {
                continue;
            }

            List<string> subFiles = await GetFilesInDirectoryAsync(baseDir, subDirPath, config);
            if (subFiles.Count == 0)
            {
                // Include empty directories so clients don't treat them as removed
                files.Add(subDirPath);
            }
            else
            {
                files.AddRange(subFiles);
            }
        }

        return files;
    }

    /// <summary>
    ///     Check if a path is excluded based on exclusion patterns
    /// </summary>
    private bool IsExcluded(string path, List<Regex> compiledExclusions, string? baseDir = null)
    {
        // Convert absolute path to relative path from server root for pattern matching
        string relativePath;
        if (baseDir != null && Path.IsPathFullyQualified(path))
        {
            relativePath = Path.GetRelativePath(baseDir, path);
        }
        else
        {
            relativePath = path;
        }

        string unixPath = PathHelper.ToUnixPath(relativePath);
        return compiledExclusions.Any(regex => regex.IsMatch(unixPath));
    }

    /// <summary>
    ///     Build a ModFile object for a given file path
    /// </summary>
    private async Task<ModFile> BuildModFileAsync(string file, CancellationToken cancellationToken = default)
    {
        FileInfo fileInfo = new(file);
        if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
        {
            return new ModFile("", true);
        }

        var retryCount = 0;
        while (true)
        {
            try
            {
                await _limiter.WaitAsync(cancellationToken);
                try
                {
                    string hash = await FileHash.HashFile(file);
                    return new ModFile(hash);
                }
                finally
                {
                    _limiter.Release();
                }
            }
            catch (IOException) when (retryCount < 5)
            {
                _logger.LogDebug("File '{File}' is locked, retrying... (Attempt {RetryCount}/5)", file, retryCount);
                await Task.Delay(500, cancellationToken);
                retryCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file '{File}'", file);
                throw new InvalidOperationException($"NarcoNet: Error reading '{file}'", ex);
            }
        }
    }

    /// <summary>
    ///     Hash all files in the configured sync paths
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, ModFile>>> HashModFilesAsync(
        List<SyncPath> syncPaths,
        NarcoNetConfig config,
        CancellationToken cancellationToken = default)
    {
        // Return cached results if available (snapshot to local to avoid TOCTOU with InvalidateHashCache)
        var cache = _hashCache;
        if (cache != null)
        {
            // Filter to only requested sync paths
            Dictionary<string, Dictionary<string, ModFile>> filtered = new();
            foreach (SyncPath syncPath in syncPaths)
            {
                string key = PathHelper.ToUnixPath(syncPath.Path);
                if (cache.TryGetValue(key, out var cachedFiles))
                {
                    filtered[key] = cachedFiles;
                }
            }
            _logger.LogDebug("Returning cached hashes for {Count} paths", filtered.Count);
            return filtered;
        }

        // Only one hash computation at a time; concurrent waiters share the result
        await _hashCacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock (snapshot again)
            cache = _hashCache;
            if (cache != null)
            {
                Dictionary<string, Dictionary<string, ModFile>> filtered = new();
                foreach (SyncPath syncPath in syncPaths)
                {
                    string key = PathHelper.ToUnixPath(syncPath.Path);
                    if (cache.TryGetValue(key, out var cachedFiles))
                    {
                        filtered[key] = cachedFiles;
                    }
                }
                return filtered;
            }

            Dictionary<string, Dictionary<string, ModFile>> result = await ComputeHashesAsync(syncPaths, config, cancellationToken);
            _hashCache = result;
            WriteHashDump(result, config.Exclusions);
            return result;
        }
        finally
        {
            _hashCacheLock.Release();
        }
    }

    private async Task<Dictionary<string, Dictionary<string, ModFile>>> ComputeHashesAsync(
        List<SyncPath> syncPaths,
        NarcoNetConfig config,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Dictionary<string, ModFile>> result = new();
        ConcurrentDictionary<string, byte> processedFiles = new();
        DateTime startTime = DateTime.UtcNow;

        string baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
#if NARCONET_DEBUG_LOGGING
        _logger.LogDebug($"ComputeHashesAsync: Starting to hash files in {syncPaths.Count} sync paths");
#endif

        foreach (SyncPath syncPath in syncPaths)
        {
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, syncPath.Path));
            List<string> files = await GetFilesInDirectoryAsync(baseDir, fullPath, config);
#if NARCONET_DEBUG_LOGGING
            _logger.LogDebug($"  {syncPath.Path}: Found {files.Count} files");
#endif
            ConcurrentDictionary<string, ModFile> filesResult = new();

            // Process files in parallel
            await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
            {
                // Convert absolute path to relative path from server root
                string relativePath = Path.GetRelativePath(baseDir, file);
                string unixPath = PathHelper.ToUnixPath(relativePath);
                if (processedFiles.TryAdd(unixPath, 0))
                {
                    ModFile modFile = await BuildModFileAsync(file, ct);
                    filesResult[unixPath] = modFile;
                }
            });

            // Use the original syncPath.Path as dictionary key to match client expectations
            result[PathHelper.ToUnixPath(syncPath.Path)] = new Dictionary<string, ModFile>(filesResult);
        }

        double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        _logger.LogDebug("Hashed {Count} files in {Elapsed:F0}ms", processedFiles.Count, elapsed);

        return result;
    }

    /// <summary>
    ///     Write a diagnostic dump of all hashed files to NarcoNet_KnownFiles.txt
    /// </summary>
    private void WriteHashDump(Dictionary<string, Dictionary<string, ModFile>> hashes, List<string> exclusions)
    {
        try
        {
            string gameRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            string dumpPath = Path.Combine(Directory.GetCurrentDirectory(), "NarcoNet_KnownFiles.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"# NarcoNet Server — Known Files Dump");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"# Game Root: {gameRoot}");
            sb.AppendLine($"#");
            sb.AppendLine($"# Exclusions ({exclusions.Count}):");
            foreach (string ex in exclusions)
                sb.AppendLine($"#   {ex}");
            sb.AppendLine($"#");
            sb.AppendLine($"# Format: [SyncPath] | [RelativePath] | [Hash] | [Type]");
            sb.AppendLine($"# ─────────────────────────────────────────────────────────");

            int totalFiles = 0;
            foreach (var (syncPathKey, files) in hashes)
            {
                sb.AppendLine($"#");
                sb.AppendLine($"# SyncPath: {syncPathKey} ({files.Count} entries)");
                sb.AppendLine($"#");

                foreach (var (filePath, modFile) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string type = modFile.Directory ? "DIR" : "FILE";
                    string hash = modFile.Directory ? "--" : modFile.Hash;
                    sb.AppendLine($"{syncPathKey} | {filePath} | {hash} | {type}");
                    totalFiles++;
                }
            }

            sb.AppendLine($"#");
            sb.AppendLine($"# Total: {totalFiles} entries across {hashes.Count} sync paths");

            File.WriteAllText(dumpPath, sb.ToString());
            _logger.LogInformation("Wrote known files dump to {Path} ({Count} entries)", dumpPath, totalFiles);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write known files dump");
        }
    }

    /// <summary>
    ///     Sanitize a download path to ensure it's within allowed sync paths
    /// </summary>
    public string SanitizeDownloadPath(string file, List<SyncPath> syncPaths)
    {
        string gameRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        string normalized = Path.GetFullPath(Path.Combine(gameRoot, file));

        foreach (SyncPath syncPath in syncPaths)
        {
            string fullPath = Path.GetFullPath(Path.Combine(gameRoot, syncPath.Path));

            // Check if the normalized file path is within the sync path
            // GetRelativePath returns a path without ".." if the file is within the base
            string relativePath = Path.GetRelativePath(fullPath, normalized);
            if (!relativePath.StartsWith("..") && !Path.IsPathRooted(relativePath))
            {
                return normalized;
            }
        }

        throw new UnauthorizedAccessException("Path must match one of the configured sync paths");
    }

}
