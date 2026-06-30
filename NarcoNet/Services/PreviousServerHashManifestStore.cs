using BepInEx.Logging;
using NarcoNet.Utilities;
using SPT.Common.Utils;

namespace NarcoNet.Services;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Persists the previous successful server hash baseline used by client sync comparisons.
/// </summary>
public class PreviousServerHashManifestStore(ManualLogSource? logger = null)
{
    /// <summary>
    ///     Loads the persisted previous-server hash manifest, returning null when no safe baseline exists.
    /// </summary>
    public SyncPathModFiles? Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            logger?.LogDebug($"Previous server hash manifest not found: {manifestPath}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            SyncPathModFiles? manifest = Json.Deserialize<SyncPathModFiles>(json);
            if (manifest == null)
            {
                logger?.LogWarning($"Previous server hash manifest deserialized to null: {manifestPath}");
            }

            return manifest;
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Failed to load previous server hash manifest '{manifestPath}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Replaces the persisted baseline with the full current server hash manifest.
    /// </summary>
    public void Save(string manifestPath, SyncPathModFiles currentServerModFiles)
    {
        try
        {
            string? manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDirectory))
            {
                Directory.CreateDirectory(manifestDirectory);
            }

            File.WriteAllText(manifestPath, Json.Serialize(currentServerModFiles));
            int entryCount = currentServerModFiles.Sum(path => path.Value.Count);
            logger?.LogDebug($"Wrote previous server hash manifest: {manifestPath} ({currentServerModFiles.Count} paths, {entryCount} entries)");
        }
        catch (Exception ex)
        {
            logger?.LogError($"Failed to save previous server hash manifest '{manifestPath}': {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
