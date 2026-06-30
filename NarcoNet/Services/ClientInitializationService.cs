using System.Text.RegularExpressions;

using NarcoNet.Utilities;
using SPT.Common.Utils;

namespace NarcoNet.Services;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Handles client initialization and data loading
/// </summary>
public class ClientInitializationService : IClientInitializationService
{
    /// <inheritdoc/>
    public string? ValidateSyncPaths(List<SyncPath> syncPaths, string serverRoot)
    {
        foreach (SyncPath syncPath in syncPaths)
        {
            if (Path.IsPathRooted(syncPath.Path))
            {
                return $"Paths must be relative to the game root! Invalid path '{syncPath}'";
            }

            if (syncPath.Path.Contains(".."))
            {
                return $"Paths must not contain '..'. Invalid path '{syncPath.Path}'. All paths should be relative to the game root folder.";
            }

            // Get the full resolved path
            string fullPath = Path.GetFullPath(Path.Combine(serverRoot, syncPath.Path));

            // Check if the path exists or can be created (validate it's a legitimate path)
            try
            {
                // Just validate the path format is valid, don't check if it exists yet
                _ = Path.GetDirectoryName(fullPath);
            }
            catch (Exception)
            {
                return $"Invalid path format! Invalid path '{syncPath}'";
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public List<string> LoadLocalExclusions(string localExclusionsPath, bool isHeadless, List<string>? headlessExclusionTemplates)
    {
        if (!File.Exists(localExclusionsPath))
        {
            // Auto-create a template file for headless clients so users have a starting point to edit
            if (isHeadless && headlessExclusionTemplates != null)
            {
                string? parentDir = Path.GetDirectoryName(localExclusionsPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                File.WriteAllText(localExclusionsPath, Json.Serialize(headlessExclusionTemplates));
                NarcoPlugin.Logger.LogInfo(
                    $"Created default exclusions file at '{localExclusionsPath}'. Edit this file to customize which files are excluded from sync.");
                return headlessExclusionTemplates;
            }

            return [];
        }

        string json = File.ReadAllText(localExclusionsPath);
        List<string> exclusions = Json.Deserialize<List<string>>(json);

        // Migrate old-format paths (with ../ prefix) to gameroot-relative paths
        bool migrated = false;
        for (int i = 0; i < exclusions.Count; i++)
        {
            string original = exclusions[i];
            if (original.StartsWith("../") || original.StartsWith(@"..\"))
            {
                exclusions[i] = original.Substring(3);
                migrated = true;
            }
        }

        if (migrated)
        {
            NarcoPlugin.Logger.LogWarning(
                $"Exclusions.json uses deprecated '../' path format. Paths have been automatically migrated to gameroot-relative format. The file has been updated.");
            File.WriteAllText(localExclusionsPath, Json.Serialize(exclusions));
        }

        return exclusions;
    }

    /// <inheritdoc/>
    public SyncPathModFiles BuildRemoteModFiles(
        List<SyncPath> enabledSyncPaths,
        Dictionary<string, Dictionary<string, string>> remoteHashes,
        List<string> localExclusions)
    {
        List<Regex> localExclusionsRegex = localExclusions.Select(Glob.CreateNoEnd).ToList();

        return enabledSyncPaths
            .Select(syncPath =>
            {
                // Get remote hashes for this path, or empty dict if path doesn't exist on server
                var remotePathHashes = remoteHashes.TryGetValue(syncPath.Path, out var hashes)
                    ? hashes
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!syncPath.Enforced)
                {
                    // Filter out locally excluded files for non-enforced paths
                    remotePathHashes = remotePathHashes
                        .Where(kvp => !Sync.IsExcluded(localExclusionsRegex, kvp.Key))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                }

                var remoteModFilesForPath = remotePathHashes
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new ModFile(kvp.Value, kvp.Key.EndsWith("\\") || kvp.Key.EndsWith("/")),
                        StringComparer.OrdinalIgnoreCase
                    );

                return new KeyValuePair<string, Dictionary<string, ModFile>>(syncPath.Path, remoteModFilesForPath);
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }
}
