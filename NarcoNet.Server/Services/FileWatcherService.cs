using Microsoft.Extensions.Logging;

using NarcoNet.Utilities;

using SPTarkov.DI.Annotations;

namespace NarcoNet.Server.Services;

/// <summary>
///     Watches sync path directories at runtime and invalidates the hash cache
///     when files are added, modified, or deleted — so the next client request
///     recomputes fresh hashes via full comparison.
/// </summary>
[Injectable]
public class FileWatcherService : IDisposable
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly SyncService _syncService;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _debounceTimer;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        SyncService syncService)
    {
        _logger = logger;
        _syncService = syncService;
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    ///     Start watching all enabled sync path directories for file changes
    /// </summary>
    public void StartWatching(List<SyncPath> syncPaths)
    {
        foreach (SyncPath syncPath in syncPaths)
        {
            if (!syncPath.Enabled)
            {
                continue;
            }

            string gameRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            string fullPath = Path.GetFullPath(Path.Combine(gameRoot, syncPath.Path));

            try
            {
                if (File.Exists(fullPath))
                {
                    // Single file: watch parent directory with filename filter
                    string? parentDir = Path.GetDirectoryName(fullPath);
                    string fileName = Path.GetFileName(fullPath);

                    if (parentDir == null || !Directory.Exists(parentDir))
                    {
                        _logger.LogWarning("Parent directory for sync path '{Path}' does not exist, skipping watcher", syncPath.Path);
                        continue;
                    }

                    CreateWatcher(parentDir, fileName, syncPath.Path);
                }
                else if (Directory.Exists(fullPath))
                {
                    CreateWatcher(fullPath, "*", syncPath.Path);
                }
                else
                {
                    _logger.LogWarning("Sync path '{Path}' does not exist, skipping watcher", syncPath.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file watcher for '{Path}'", syncPath.Path);
            }
        }

        if (_watchers.Count > 0)
        {
            _logger.LogInformation("File watchers started for {Count} sync paths", _watchers.Count);
        }
    }

    private void CreateWatcher(string directory, string filter, string syncPathName)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = filter == "*",
            InternalBufferSize = 65536, // 64KB (default 8KB too small for bulk ops)
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size
                           | NotifyFilters.CreationTime
        };

        watcher.Created += OnFileSystemEvent;
        watcher.Changed += OnFileSystemEvent;
        watcher.Deleted += OnFileSystemEvent;
        watcher.Renamed += OnFileSystemEvent;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;

        _watchers.Add(watcher);

#if NARCONET_DEBUG_LOGGING
        _logger.LogDebug("File watcher created for '{SyncPath}' (directory: {Dir}, filter: {Filter})",
            syncPathName, directory, filter);
#endif
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ResetDebounceTimer();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "File watcher error (buffer overflow?), scheduling rescan");
        ResetDebounceTimer();
    }

    private void ResetDebounceTimer()
    {
        _debounceTimer.Change(1000, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        _syncService.InvalidateHashCache();
        _logger.LogDebug("File change detected, hash cache invalidated");
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
