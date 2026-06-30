using BepInEx.Logging;
using NarcoNet.Utilities;

namespace NarcoNet.Services;

using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Handles file synchronization operations for the client
/// </summary>
public class ClientSyncService(ManualLogSource logger, ServerModule serverModule) : IClientSyncService
{
    /// <inheritdoc/>
    public void AnalyzeModFiles(
        SyncPathModFiles localModFiles,
        SyncPathModFiles remoteModFiles,
        List<SyncPath> enabledSyncPaths,
        out SyncPathFileList addedFiles,
        out SyncPathFileList updatedFiles,
        out SyncPathFileList removedFiles,
        out SyncPathFileList createdDirectories)
    {
#if NARCONET_DEBUG_LOGGING
        logger.LogDebug($"AnalyzeModFiles: Comparing {localModFiles.Count} local sync paths with {remoteModFiles.Count} remote sync paths");
#endif
        Sync.CompareModFiles(
            Directory.GetCurrentDirectory(),
            enabledSyncPaths,
            localModFiles,
            remoteModFiles,
            out addedFiles,
            out updatedFiles,
            out removedFiles,
            out createdDirectories
        );

        int addedCount = addedFiles.SelectMany(path => path.Value).Count();
        int updatedCount = updatedFiles.SelectMany(path => path.Value).Count();
        int removedCount = removedFiles.SelectMany(path => path.Value).Count();

        logger.LogDebug($"File changes detected: {addedCount} added, {updatedCount} updated, {removedCount} removed");

        LogFileChanges("Added", addedFiles);
        LogFileChanges("Updated", updatedFiles);
        LogFileChanges("Removed", removedFiles);
    }

    /// <inheritdoc/>
    public async Task SyncModsAsync(
        SyncPathFileList filesToAdd,
        SyncPathFileList filesToUpdate,
        SyncPathFileList directoriesToCreate,
        SyncPathFileList filesToRemove,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles,
        string pendingUpdatesDir,
        IProgress<(int current, int total)> progress,
        IProgress<(long current, long total)> byteProgress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(pendingUpdatesDir))
        {
            Directory.CreateDirectory(pendingUpdatesDir);
#if NARCONET_DEBUG_LOGGING
            logger.LogDebug($"Created pending updates directory: {pendingUpdatesDir}");
#endif
        }

#if NARCONET_DEBUG_LOGGING
        logger.LogDebug($"SyncModsAsync: Processing {filesToAdd.Sum(x => x.Value.Count)} additions, {filesToUpdate.Sum(x => x.Value.Count)} updates, {filesToRemove.Sum(x => x.Value.Count)} removals");
#endif

        // Delete removed files first (only for non-restart-required paths)
        if (deleteRemovedFiles)
        {
            foreach (SyncPath syncPath in enabledSyncPaths.Where(sp => !sp.RestartRequired))
            {
                if (!filesToRemove.TryGetValue(syncPath.Path, out var removeFiles))
                {
                    continue;
                }

                foreach (string file in removeFiles)
                {
                    try
                    {
                        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), file);
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            logger.LogInfo($"Deleted: {file}");
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            Directory.Delete(fullPath, true);
                            logger.LogInfo($"Deleted directory: {file}");
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Failed to delete '{file}': {e.Message}");
                    }
                }
            }
        }
        // Also delete files for enforced paths when deleteRemovedFiles is off
        foreach (SyncPath syncPath in enabledSyncPaths.Where(sp => sp.Enforced && !sp.RestartRequired && !deleteRemovedFiles))
        {
            if (!filesToRemove.TryGetValue(syncPath.Path, out var removeFiles))
            {
                continue;
            }

            foreach (string file in removeFiles)
            {
                try
                {
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), file);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        logger.LogInfo($"Deleted (enforced): {file}");
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                        logger.LogInfo($"Deleted directory (enforced): {file}");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError($"Failed to delete '{file}': {e.Message}");
                }
            }
        }

        // Create directories for ALL paths (directories are never locked, no restart needed)
        foreach (SyncPath syncPath in enabledSyncPaths)
        {
            if (!directoriesToCreate.TryGetValue(syncPath.Path, out var dirsToCreate))
                continue;

            foreach (string dir in dirsToCreate)
            {
                try
                {
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), dir);
                    Directory.CreateDirectory(fullPath);
                    logger.LogInfo($"Created directory: {dir}");
                }
                catch (Exception e)
                {
                    logger.LogError($"Failed to create directory: {e}");
                }
            }

            // Clear handled entries so they don't count toward restart
            if (syncPath.RestartRequired)
                dirsToCreate.Clear();
        }

        // Delete empty directory entries for restart-required paths immediately
        // (empty dirs are never locked — dirs with files are left for the restart path)
        foreach (SyncPath syncPath in enabledSyncPaths.Where(sp => sp.RestartRequired))
        {
            if (!(deleteRemovedFiles || syncPath.Enforced))
                continue;

            if (!filesToRemove.TryGetValue(syncPath.Path, out var removeFiles))
                continue;

            List<string> handledDirs = [];
            foreach (string file in removeFiles)
            {
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), file);
                if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    try
                    {
                        Directory.Delete(fullPath);
                        logger.LogInfo($"Deleted empty directory: {file}");
                        handledDirs.Add(file);
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Failed to delete directory '{file}': {e.Message}");
                    }
                }
            }

            // Remove handled directories so they don't count toward restart
            foreach (string dir in handledDirs)
                removeFiles.Remove(dir);
        }

        // Prepare download tasks
        SemaphoreSlim limiter = new(8);
        SyncPathFileList filesToDownload = enabledSyncPaths
            .Select(syncPath => new KeyValuePair<string, List<string>>(
                syncPath.Path,
                [.. filesToAdd[syncPath.Path], .. filesToUpdate[syncPath.Path]]
            ))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        logger.LogDebug($"Downloading files...");
#if NARCONET_DEBUG_LOGGING
        foreach (var kvp in filesToDownload)
        {
            if (kvp.Value.Count > 0)
            {
                logger.LogDebug($"  {kvp.Key}: {kvp.Value.Count} files to download");
            }
        }
#endif

        // Aggregate byte progress counters shared across all concurrent downloads
        long bytesDownloaded = 0;
        long bytesTotal = 0;

        List<Task> downloadTasks = enabledSyncPaths
            .SelectMany(syncPath =>
                filesToDownload.TryGetValue(syncPath.Path, out List<string>? pathFilesToDownload)
                    ? pathFilesToDownload.Select(file =>
                    {
                        // Per-file byte progress that aggregates into the shared counters
                        bool fileSizeRegistered = false;
                        long previousBytes = 0;
                        var fileByteProgress = new Progress<(long bytesDownloaded, long totalBytes)>(p =>
                        {
                            if (!fileSizeRegistered)
                            {
                                fileSizeRegistered = true;
                                Interlocked.Add(ref bytesTotal, p.totalBytes);
                            }
                            long delta = p.bytesDownloaded - previousBytes;
                            previousBytes = p.bytesDownloaded;
                            if (delta > 0)
                            {
                                Interlocked.Add(ref bytesDownloaded, delta);
                            }
                            byteProgress.Report((Interlocked.Read(ref bytesDownloaded), Interlocked.Read(ref bytesTotal)));
                        });

                        if (syncPath.RestartRequired)
                        {
                            // For restart-required files, download to PendingUpdates
                            return serverModule.DownloadFile(
                                file,
                                pendingUpdatesDir,
                                limiter,
                                cancellationToken,
                                fileByteProgress
                            );
                        }

                        // For non-restart files, download directly to game root
                        return serverModule.DownloadFile(
                            file,
                            Directory.GetCurrentDirectory(),
                            limiter,
                            cancellationToken,
                            fileByteProgress
                        );
                    })
                    : []
            )
            .ToList();

        int totalDownloadCount = downloadTasks.Count;
        var downloadCount = 0;

        // Report initial progress so the UI shows the total immediately
        progress.Report((0, totalDownloadCount));

        // Download files with progress reporting
        while (downloadTasks.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            Task task = await Task.WhenAny(downloadTasks);

            try
            {
                await task;
            }
            catch (Exception e)
            {
                if (e is TaskCanceledException && cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                logger.LogError($"Download failed: {e.Message}");
                throw;
            }

            downloadTasks.Remove(task);
            downloadCount++;
            progress.Report((downloadCount, totalDownloadCount));
        }

        logger.LogDebug("All files downloaded successfully");
    }

    /// <inheritdoc/>
    public int GetUpdateCount(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles)
    {
        return enabledSyncPaths
            .Select(syncPath =>
                addedFiles[syncPath.Path].Count
                + updatedFiles[syncPath.Path].Count
                + (deleteRemovedFiles || syncPath.Enforced ? removedFiles[syncPath.Path].Count : 0)
                + createdDirectories[syncPath.Path].Count
            )
            .Sum();
    }

    /// <inheritdoc/>
    public bool IsSilentMode(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles,
        bool isHeadless)
    {
        if (isHeadless)
        {
            return true;
        }

        return enabledSyncPaths.All(syncPath =>
            syncPath.Silent
            || addedFiles[syncPath.Path].Count == 0
            && updatedFiles[syncPath.Path].Count == 0
            && (!(deleteRemovedFiles || syncPath.Enforced) || removedFiles[syncPath.Path].Count == 0)
            && createdDirectories[syncPath.Path].Count == 0
        );
    }

    /// <inheritdoc/>
    public bool IsRestartRequired(
        SyncPathFileList addedFiles,
        SyncPathFileList updatedFiles,
        SyncPathFileList removedFiles,
        SyncPathFileList createdDirectories,
        List<SyncPath> enabledSyncPaths,
        bool deleteRemovedFiles)
    {
        return !enabledSyncPaths.All(syncPath =>
            !syncPath.RestartRequired
            || addedFiles[syncPath.Path].Count == 0
            && updatedFiles[syncPath.Path].Count == 0
            && (!(deleteRemovedFiles || syncPath.Enforced) || removedFiles[syncPath.Path].Count == 0)
            && createdDirectories[syncPath.Path].Count == 0
        );
    }

    private void LogFileChanges(string changeType, SyncPathFileList changes)
    {
        int totalCount = changes.SelectMany(path => path.Value).Count();
        if (totalCount > 0)
        {
            foreach (KeyValuePair<string, List<string>> syncPath in changes.Where(kvp => kvp.Value.Count > 0))
            {
                logger.LogDebug($"  [{syncPath.Key}]");
                string prefix = changeType switch
                {
                    "Added" => "+",
                    "Updated" => "*",
                    "Removed" => "-",
                    _ => "?"
                };

                foreach (string file in syncPath.Value)
                {
                    logger.LogDebug($"    {prefix} {file}");
                }
            }
        }
    }
}
