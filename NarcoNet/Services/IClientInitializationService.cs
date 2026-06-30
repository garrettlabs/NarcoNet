using NarcoNet.Utilities;

namespace NarcoNet.Services;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Handles client initialization and data loading
/// </summary>
public interface IClientInitializationService
{
    /// <summary>
    ///     Validates that sync paths are relative and within the server root
    /// </summary>
    /// <param name="syncPaths">Paths to validate</param>
    /// <param name="serverRoot">Server root directory</param>
    /// <returns>Validation error message, or null if valid</returns>
    string? ValidateSyncPaths(List<SyncPath> syncPaths, string serverRoot);

    /// <summary>
    ///     Loads local exclusions from disk, creating a template file for headless clients on first run
    /// </summary>
    /// <param name="localExclusionsPath">Path to exclusions file</param>
    /// <param name="isHeadless">Whether running in headless mode</param>
    /// <param name="headlessExclusionTemplates">Initial exclusions written to disk for headless clients when no file exists</param>
    /// <returns>List of exclusion patterns</returns>
    List<string> LoadLocalExclusions(string localExclusionsPath, bool isHeadless, List<string>? headlessExclusionTemplates);

    /// <summary>
    ///     Builds remote mod files dictionary from server hashes
    /// </summary>
    /// <param name="enabledSyncPaths">Enabled sync paths</param>
    /// <param name="remoteHashes">Remote file hashes from server</param>
    /// <param name="localExclusions">Local exclusion patterns</param>
    /// <returns>Dictionary of remote mod files by path</returns>
    SyncPathModFiles BuildRemoteModFiles(
        List<SyncPath> enabledSyncPaths,
        Dictionary<string, Dictionary<string, string>> remoteHashes,
        List<string> localExclusions);
}
