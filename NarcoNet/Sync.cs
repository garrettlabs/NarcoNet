using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

using NarcoNet.Utilities;

namespace NarcoNet;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public static class Sync
{
    public static SyncPathFileList GetAddedFiles(List<SyncPath> syncPaths, SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles)
    {
        return syncPaths
            .Select(syncPath => new KeyValuePair<string, List<string>>(
                syncPath.Path,
                remoteModFiles[syncPath.Path]
                    .Where(kvp => !kvp.Value.Directory)
                    .Select(kvp => kvp.Key)
                    .Except(
                        localModFiles.TryGetValue(syncPath.Path, out Dictionary<string, ModFile>? modFiles)
                            ? modFiles.Keys
                            : [], StringComparer.OrdinalIgnoreCase)
                    .ToList()
            ))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static SyncPathFileList GetUpdatedFiles(
        List<SyncPath> syncPaths,
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles
    )
    {
        return syncPaths
            .Select(syncPath =>
            {
                if (!localModFiles.TryGetValue(syncPath.Path, out Dictionary<string, ModFile>? localPathFiles))
                {
                    return new KeyValuePair<string, List<string>>(syncPath.Path, []);
                }

                IEnumerable<string> query = remoteModFiles[syncPath.Path]
                    .Where(kvp => !kvp.Value.Directory)
                    .Select(kvp => kvp.Key)
                    .Intersect(localPathFiles.Keys, StringComparer.OrdinalIgnoreCase);

                query = query.Where(file =>
                {
                    // Find the actual key in localPathFiles (case-insensitive)
                    string? localKey = localPathFiles.Keys.FirstOrDefault(k => string.Equals(k, file, StringComparison.OrdinalIgnoreCase));
                    return localKey == null || remoteModFiles[syncPath.Path][file].Hash != localPathFiles[localKey].Hash;
                });

                return new KeyValuePair<string, List<string>>(syncPath.Path, query.ToList());
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static SyncPathFileList GetRemovedFiles(
        List<SyncPath> syncPaths,
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles
    )
    {
        return syncPaths
            .Select(syncPath =>
            {
                if (!localModFiles.TryGetValue(syncPath.Path, out Dictionary<string, ModFile>? localPathFiles))
                {
                    return new KeyValuePair<string, List<string>>(syncPath.Path, []);
                }

                IEnumerable<string> query = localPathFiles.Keys.Except(remoteModFiles[syncPath.Path].Keys, StringComparer.OrdinalIgnoreCase);

                return new KeyValuePair<string, List<string>>(syncPath.Path, query.ToList());
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static SyncPathFileList GetCreatedDirectories(
        string basePath,
        List<SyncPath> syncPaths,
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles
    )
    {
        return syncPaths
            .Select(syncPath =>
            {
                return new KeyValuePair<string, List<string>>(
                    syncPath.Path,
                    remoteModFiles[syncPath.Path]
                        .Where(kvp => kvp.Value.Directory)
                        .Select(kvp => kvp.Key)
                        .Except(localModFiles[syncPath.Path].Keys, StringComparer.OrdinalIgnoreCase)
                        .Where(dir => !Directory.Exists(Path.Combine(basePath, dir)))
                        .ToList()
                );
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static List<string> GetFilesInDirectory(string basePath, string directory, List<Regex> exclusions)
    {
        if (File.Exists(directory))
        {
            return [directory];
        }

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !IsExcluded(exclusions, file.Replace($"{basePath}{Path.DirectorySeparatorChar}", "")))
            .Concat(
                Directory
                    .GetDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(subDir => !IsExcluded(exclusions, subDir.Replace($"{basePath}{Path.DirectorySeparatorChar}", "")))
                    .SelectMany(subDir => Directory.GetFileSystemEntries(subDir).Length == 0
                        ? [subDir]
                        : GetFilesInDirectory(basePath, subDir, exclusions))
            )
            .ToList();
    }

    public static async Task<SyncPathModFiles> HashLocalFiles(
        string basePath,
        List<SyncPath> syncPaths,
        List<Regex> remoteExclusions,
        List<Regex> localExclusions
    )
    {
        Stopwatch watch = Stopwatch.StartNew();
        // Use ConcurrentDictionary with case-insensitive comparison (matching Windows FS behavior)
        // to deduplicate files across overlapping sync paths. Keyed on normalized absolute paths
        // to avoid mixed-slash mismatches from Path.Combine preserving forward slashes in config
        // paths while Directory.GetDirectories uses backslashes for discovered subdirectories.
        ConcurrentDictionary<string, byte> processedFiles = new(StringComparer.OrdinalIgnoreCase);
        SemaphoreSlim limitOpenFiles = new(1024);

        SyncPathModFiles results = new();

        foreach (SyncPath syncPath in syncPaths)
        {
            string path = Path.Combine(basePath, syncPath.Path);

            List<Regex> exclusionsToUse = [.. remoteExclusions, .. (syncPath.Enforced ? [] : localExclusions)];
            results[syncPath.Path] = (
                await Task.WhenAll(
                    GetFilesInDirectory(basePath, path, exclusionsToUse)
                        .Where(file => processedFiles.TryAdd(
                            file.Replace('/', Path.DirectorySeparatorChar), 0))
                        .AsParallel()
                        .Select(async file =>
                            {
                                await limitOpenFiles.WaitAsync();
                                try
                                {
                                    ModFile modFile = await CreateModFile(file);

                                    // Convert absolute path back to gameroot-relative path with forward slashes
                                    string pathFromGameRoot = file.Replace($"{basePath}{Path.DirectorySeparatorChar}", "");
                                    string relativePath = pathFromGameRoot.Replace(Path.DirectorySeparatorChar, '/');
                                    return new KeyValuePair<string, ModFile>(relativePath, modFile);
                                }
                                finally
                                {
                                    limitOpenFiles.Release();
                                }
                            }
                        )
                )
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        watch.Stop();
        NarcoPlugin.Logger.LogDebug(
            $"Hashed {processedFiles.Count} files in {watch.Elapsed.TotalMilliseconds}ms");

        return results;
    }

    public static async Task<ModFile> CreateModFile(string file)
    {
        var hash = "";

        if (Directory.Exists(file))
        {
            return new ModFile(hash, true);
        }

        try
        {
            hash = await FileHash.HashFile(file);
        }
        catch (Exception e)
        {
            NarcoPlugin.Logger.LogError($"Error hashing file '{file}': {e.Message}");
            hash = "";
        }

        return new ModFile(hash);
    }

    public static void CompareModFiles(
        string basePath,
        List<SyncPath> syncPaths,
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles,
        out SyncPathFileList addedFiles,
        out SyncPathFileList updatedFiles,
        out SyncPathFileList removedFiles,
        out SyncPathFileList createdDirectories
    )
    {
        addedFiles = GetAddedFiles(syncPaths, localModFiles, remoteModFiles);
        updatedFiles = GetUpdatedFiles(syncPaths, localModFiles, remoteModFiles);
        removedFiles = GetRemovedFiles(syncPaths, localModFiles, remoteModFiles);
        createdDirectories = GetCreatedDirectories(basePath, syncPaths, localModFiles, remoteModFiles);
    }

    public static bool IsExcluded(List<Regex> exclusions, string path)
    {
        return exclusions.Any(regex => regex.IsMatch(path.Replace(@"\", "/")));
    }
}
