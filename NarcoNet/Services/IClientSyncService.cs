using NarcoNet.Utilities;

namespace NarcoNet.Services;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Service interface for handling file synchronization operations
/// </summary>
public interface IClientSyncService
{
    /// <summary>
    ///     Analyzes differences between local and remote mod files
    /// </summary>
    void AnalyzeModFiles(
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles,
        List<SyncPath> enabledSyncPaths,
        out SyncPathFileList addedFiles,
        out SyncPathFileList updatedFiles,
        out SyncPathFileList removedFiles,
        out SyncPathFileList createdDirectories);

    /// <summary>
    ///     Downloads and synchronizes modified files
    /// </summary>
    Task SyncModsAsync(
        SyncPathFileList filesToAdd,
        SyncPathFileList filesToUpdate,
        SyncPathFileList directoriesToCreate,
        SyncPathFileList filesToRemove,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles,
        string pendingUpdatesDir,
        IProgress<(int current, int total)> progress,
        IProgress<(long current, long total)> byteProgress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Gets the total count of updates across all sync paths
    /// </summary>
    int GetUpdateCount(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles);

    /// <summary>
    ///     Checks if update should run in silent mode
    /// </summary>
    bool IsSilentMode(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles,
        bool isHeadless);

    /// <summary>
    ///     Checks if restart is required after updates
    /// </summary>
    bool IsRestartRequired(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles);

}
