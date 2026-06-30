using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT.UI;
using NarcoNet.Services;
using NarcoNet.UI;
using NarcoNet.Utilities;
using SPT.Common.Utils;
using UnityEngine;

namespace NarcoNet;
using SyncPathFileList = Dictionary<string, List<string>>;
using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Main NarcoNet client plugin that coordinates file synchronization between SPT server and client
/// </summary>
[BepInPlugin(NarcoNetConstants.ClientPluginGuid, NarcoNetConstants.PluginDisplayName, NarcoNetVersion.Version)]
public class NarcoPlugin : BaseUnityPlugin, IDisposable
{
    // Static paths
    private static readonly string NarcoNetDir = Path.Combine(Directory.GetCurrentDirectory(), NarcoNetConstants.DataDirectoryName);
    private static readonly string PendingUpdatesDir = Path.Combine(NarcoNetDir, NarcoNetConstants.PendingUpdatesDirectoryName);
    private static readonly string LocalHashesPath = Path.Combine(NarcoNetDir, "LocalHashes.json");
    private static readonly string PreviousServerHashesPath = Path.Combine(NarcoNetDir, "PreviousServerHashes.json");
    private static readonly string LocalExclusionsPath = Path.Combine(NarcoNetDir, "Exclusions.json");
    public new static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(NarcoNetConstants.ProductName);

    // Services
    private readonly IClientUIService _uiService;
    private readonly IClientConfigService _configService;
    private readonly IClientSyncService _syncService;
    private readonly IClientInitializationService _initService;
    private readonly PreviousServerHashManifestStore _previousServerHashManifestStore;
    private readonly ServerModule _server;

    // State
    private SyncPathFileList _addedFiles = [];
    private SyncPathFileList _updatedFiles = [];
    private SyncPathFileList _removedFiles = [];
    private SyncPathFileList _createdDirectories = [];
    private SyncPathModFiles _localModFiles = [];
    private SyncPathModFiles _remoteModFiles = [];
    private List<string> _localExclusions = [];
    private List<string>? _optional;
    private List<string>? _required;
    private List<string>? _noRestart;
    private volatile bool _pluginFinished;
    private CancellationTokenSource _cts = new();
    private const string NarcoNetOldExtension = ".narconet_old";

    /// <summary>
    ///     Constructor - initializes services
    /// </summary>
    public NarcoPlugin()
    {
        _server = new ServerModule(Info.Metadata.Version);
        _uiService = new ClientUIService();
        _configService = new ClientConfigService();
        _syncService = new ClientSyncService(Logger, _server);
        _initService = new ClientInitializationService();
        _previousServerHashManifestStore = new PreviousServerHashManifestStore(Logger);
    }

    private int UpdateCount =>
        _syncService.GetUpdateCount(
            _addedFiles,
            _updatedFiles,
            _removedFiles,
            _createdDirectories,
            _configService.EnabledSyncPaths,
            _configService.DeleteRemovedFiles.Value
        );

    private List<SyncPath> EnabledSyncPaths => _configService.EnabledSyncPaths;

    private bool SilentMode =>
        _syncService.IsSilentMode(
            _addedFiles,
            _updatedFiles,
            _removedFiles,
            _createdDirectories,
            _configService.EnabledSyncPaths,
            _configService.DeleteRemovedFiles.Value,
            _configService.IsHeadless()
        );

    private bool NoRestartMode =>
        !_syncService.IsRestartRequired(
            _addedFiles,
            _updatedFiles,
            _removedFiles,
            _createdDirectories,
            _configService.EnabledSyncPaths,
            _configService.DeleteRemovedFiles.Value
        );

    private List<string> Optional =>
        _optional ??= EnabledSyncPaths
            .Where(syncPath => !syncPath.Enforced)
            .SelectMany(syncPath =>
                _addedFiles[syncPath.Path]
                    .Select(file => $"ADDED {file}")
                    .Concat(_updatedFiles[syncPath.Path].Select(file => $"UPDATED {file}"))
                    .Concat(_configService.DeleteRemovedFiles.Value || syncPath.Enforced
                        ? _removedFiles[syncPath.Path].Select(file => $"REMOVED {file}")
                        : [])
                    .Concat(_createdDirectories[syncPath.Path].Select(file => $@"CREATED {file}\"))
            )
            .ToList();

    private List<string> Required =>
        _required ??= EnabledSyncPaths
            .Where(syncPath => syncPath.Enforced)
            .SelectMany(syncPath =>
                _addedFiles[syncPath.Path]
                    .Select(file => $"ADDED {file} [enforced]")
                    .Concat(_updatedFiles[syncPath.Path].Select(file => $"UPDATED {file} [enforced]"))
                    .Concat(_configService.DeleteRemovedFiles.Value
                        ? _removedFiles[syncPath.Path].Select(file => $"REMOVED {file} [enforced]")
                        : [])
                    .Concat(_createdDirectories[syncPath.Path].Select(file => $@"CREATED {file}\ [enforced]"))
            )
            .ToList();

    private List<string> NoRestart =>
        _noRestart ??= EnabledSyncPaths
            .Where(syncPath => !syncPath.RestartRequired)
            .SelectMany(syncPath =>
                _addedFiles[syncPath.Path]
                    .Concat(_updatedFiles[syncPath.Path])
                    .Concat(_configService.DeleteRemovedFiles.Value || syncPath.Enforced
                        ? _removedFiles[syncPath.Path]
                        : [])
                    .Concat(_createdDirectories[syncPath.Path])
            )
            .ToList();

    /// <summary>
    ///     Whether there are non-enforced removed files that the user could choose to keep.
    /// </summary>
    private bool HasRemovableFiles =>
        _configService.DeleteRemovedFiles.Value &&
        EnabledSyncPaths.Any(sp =>
            !sp.Enforced &&
            _removedFiles.TryGetValue(sp.Path, out var files) &&
            files.Count > 0);

    /// <summary>
    ///     Returns a removed files list containing only enforced-path removals.
    ///     Used when the user checks "Keep deleted files" — non-enforced removals are skipped
    ///     while enforced removals are still applied.
    /// </summary>
    private SyncPathFileList FilterEnforcedRemovals() =>
        EnabledSyncPaths.ToDictionary(
            sp => sp.Path,
            sp => sp.Enforced ? _removedFiles[sp.Path] : new List<string>(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Unity lifecycle - registers console command
    /// </summary>
    private void Awake()
    {
        ConsoleScreen.Processor.RegisterCommand(
            "narconet",
            () =>
            {
                ConsoleScreen.Log("Checking for updates.");
                StartCoroutine(StartPlugin());
            }
        );
    }

    /// <summary>
    ///     Unity lifecycle - starts the plugin initialization
    /// </summary>
    public void Start()
    {
        StartCoroutine(StartPlugin());
    }

    /// <summary>
    ///     Unity lifecycle - handles UI visibility management
    /// </summary>
    public void Update()
    {
        _uiService.HandleGameUIVisibility(_uiService.IsAnyWindowActive);

        if (!_uiService.IsAnyWindowActive && _pluginFinished)
        {
            _pluginFinished = false;
            _uiService.HandleGameUIVisibility(false);
        }
    }

    /// <summary>
    ///     Unity lifecycle - delegates UI rendering to service
    /// </summary>
    private void OnGUI()
    {
        _uiService.DrawWindows();
    }

    /// <summary>
    ///     Disposes resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Analyzes differences between local and remote files, then shows update UI or silently syncs
    /// </summary>
    private void AnalyzeModFiles(SyncPathModFiles localModFiles, SyncPathModFiles? previousServerModFiles)
    {
        _syncService.AnalyzeModFiles(
            localModFiles,
            _remoteModFiles,
            previousServerModFiles,
            EnabledSyncPaths,
            out _addedFiles,
            out _updatedFiles,
            out _removedFiles,
            out _createdDirectories
        );

        if (UpdateCount <= 0)
        {
            // No updates means the filtered current server set is already applied locally; refreshing the
            // baseline keeps first-run/no-op launches from repeatedly falling back to legacy comparison.
            SavePreviousServerHashManifest();
            return;
        }
        if (SilentMode)
        {
            Task.Run(() => SyncMods(_addedFiles, _updatedFiles, _createdDirectories, _removedFiles));
        }
        else
        {
            _uiService.ShowUpdateWindow(
                Optional,
                Required,
                (keepDeletedFiles) => Task.Run(() => SyncMods(
                    _addedFiles, _updatedFiles, _createdDirectories,
                    keepDeletedFiles ? FilterEnforcedRemovals() : _removedFiles)),
                Required.Count != 0 && Optional.Count == 0 ? null : SkipUpdatingMods,
                HasRemovableFiles
            );
        }
    }

    /// <summary>
    ///     Handles user skipping optional updates - only syncs enforced changes
    /// </summary>
    private void SkipUpdatingMods()
    {
        SyncPathFileList enforcedAddedFiles = EnabledSyncPaths.ToDictionary(
            syncPath => syncPath.Path,
            syncPath => syncPath.Enforced ? _addedFiles[syncPath.Path] : [],
            StringComparer.OrdinalIgnoreCase
        );

        SyncPathFileList enforcedUpdatedFiles = EnabledSyncPaths.ToDictionary(
            syncPath => syncPath.Path,
            syncPath => syncPath.Enforced ? _updatedFiles[syncPath.Path] : [],
            StringComparer.OrdinalIgnoreCase
        );

        SyncPathFileList enforcedCreatedDirectories = EnabledSyncPaths.ToDictionary(
            syncPath => syncPath.Path,
            syncPath => syncPath.Enforced ? _createdDirectories[syncPath.Path] : [],
            StringComparer.OrdinalIgnoreCase
        );

        if (
            enforcedAddedFiles.Values.Any(files => files.Count != 0)
            || enforcedUpdatedFiles.Values.Any(files => files.Count != 0)
            || enforcedCreatedDirectories.Values.Any(files => files.Count != 0)
        )
        {
            Task.Run(() => SyncMods(enforcedAddedFiles, enforcedUpdatedFiles, enforcedCreatedDirectories, FilterEnforcedRemovals()));
        }
        else
        {
            _pluginFinished = true;
            _uiService.HideAllWindows();
        }
    }

    /// <summary>
    ///     Downloads and synchronizes mod files, showing progress UI
    /// </summary>
    private async Task SyncMods(SyncPathFileList filesToAdd, SyncPathFileList filesToUpdate,
        SyncPathFileList directoriesToCreate, SyncPathFileList filesToRemove)
    {
        _uiService.HideAllWindows();

        var effectiveRemovedFiles = filesToRemove;

        if (!_configService.IsHeadless())
        {
            _uiService.ShowProgressWindow();
        }

        Progress<(int current, int total)> progress = new(p => _uiService.UpdateProgress(p.current, p.total,
                Required.Count != 0 || NoRestart.Count != 0 ? null : () => Task.Run(CancelUpdatingMods)));

        Progress<(long current, long total)> byteProgress = new(p => _uiService.UpdateByteProgress(p.current, p.total));

        try
        {
            await _syncService.SyncModsAsync(
                filesToAdd,
                filesToUpdate,
                directoriesToCreate,
                effectiveRemovedFiles,
                EnabledSyncPaths,
                _configService.DeleteRemovedFiles.Value,
                PendingUpdatesDir,
                progress,
                byteProgress,
                _cts.Token
            );

            _uiService.HideProgressWindow();

            if (!_cts.IsCancellationRequested)
            {
                if (NoRestartMode)
                {
                    // No restart-required files changed — updates were applied immediately.
                    if (Directory.Exists(PendingUpdatesDir))
                    {
                        Directory.Delete(PendingUpdatesDir, true);
                    }
                    SavePreviousServerHashManifest();
                    _pluginFinished = true;
                }
                else
                {
                    if (!_configService.IsHeadless())
                        _uiService.ShowRestartWindow(() => ApplyPendingUpdatesInProcess(effectiveRemovedFiles));
                    else
                        ApplyPendingUpdatesInProcess(effectiveRemovedFiles);
                }
            }
        }
        catch (Exception)
        {
            _uiService.HideProgressWindow();
            if (!_configService.IsHeadless())
            {
                _uiService.ShowErrorWindow(Application.Quit);
            }
            throw;
        }
    }

    /// <summary>
    ///     Cancels the download process and cleans up pending updates
    /// </summary>
    private async Task CancelUpdatingMods()
    {
        _uiService.HideProgressWindow();
        _cts.Cancel();

        if (Directory.Exists(PendingUpdatesDir))
        {
            Directory.Delete(PendingUpdatesDir, true);
        }

        _pluginFinished = true;
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Applies pending restart-required updates in-process, then exits.
    ///     Copies staged files from PendingUpdates/, creates directories, and
    ///     deletes removed files — all using rename-then-copy/rename fallback
    ///     for locked DLLs.
    /// </summary>
    private void ApplyPendingUpdatesInProcess(SyncPathFileList effectiveRemovedFiles)
    {
        Logger.LogInfo("Applying restart-required updates in-process...");

        string gameRoot = Directory.GetCurrentDirectory();

        try
        {
            // Copy files from PendingUpdates/ to game root
            if (Directory.Exists(PendingUpdatesDir))
            {
                foreach (string src in Directory.EnumerateFiles(PendingUpdatesDir, "*", SearchOption.AllDirectories))
                {
                    string relativePath = src.Substring(PendingUpdatesDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string dst = Path.Combine(gameRoot, relativePath);
                    string? dstDir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                        Directory.CreateDirectory(dstDir);
                    try
                    {
                        File.Copy(src, dst, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        string backup = dst + NarcoNetOldExtension;
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(dst, backup);
                        File.Copy(src, dst);
                        Logger.LogInfo($"Replaced locked file via rename: {relativePath}");
                    }
                    Logger.LogInfo($"Copied: {relativePath}");
                }
            }

            // Create directories (restart-required paths only)
            foreach (SyncPath syncPath in EnabledSyncPaths.Where(sp => sp.RestartRequired))
            {
                foreach (string dir in _createdDirectories[syncPath.Path])
                {
                    try
                    {
                        string fullPath = Path.Combine(gameRoot, dir);
                        Directory.CreateDirectory(fullPath);
                        Logger.LogInfo($"Created directory: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to create directory '{dir}': {ex.Message}");
                    }
                }
            }

            // Delete files (restart-required paths, respecting DeleteRemovedFiles/Enforced)
            foreach (SyncPath syncPath in EnabledSyncPaths.Where(sp => sp.RestartRequired))
            {
                if (!(_configService.DeleteRemovedFiles.Value || syncPath.Enforced))
                    continue;

                if (!effectiveRemovedFiles.TryGetValue(syncPath.Path, out var removeFiles))
                    continue;

                foreach (string file in removeFiles)
                {
                    try
                    {
                        string fullPath = Path.Combine(gameRoot, file);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                File.Delete(fullPath);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                string backup = fullPath + NarcoNetOldExtension;
                                if (File.Exists(backup)) File.Delete(backup);
                                File.Move(fullPath, backup);
                                Logger.LogInfo($"Renamed locked file for deletion: {file}");
                            }
                            Logger.LogInfo($"Deleted: {file}");
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            Directory.Delete(fullPath, true);
                            Logger.LogInfo($"Deleted directory: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to delete '{file}': {ex.Message}");
                    }
                }
            }

            // Clean up
            if (Directory.Exists(PendingUpdatesDir))
                Directory.Delete(PendingUpdatesDir, true);

            SavePreviousServerHashManifest();
            Logger.LogInfo("Restart-required updates applied successfully. Exiting for restart.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply pending updates in-process: {ex.Message}");
            Logger.LogError($"Stack trace: {ex.StackTrace}");
        }

        Application.Quit();
    }

    /// <summary>
    ///     Removes .narconet_old files left behind by the rename-then-copy update strategy.
    ///     These are DLLs that were memory-mapped when we updated them — we renamed them
    ///     out of the way and copied the new version. Now that the process restarted,
    ///     the old files are no longer mapped and can be deleted.
    /// </summary>
    private static void CleanupRenamedFiles()
    {
        try
        {
            string gameRoot = Directory.GetCurrentDirectory();
            foreach (string file in Directory.EnumerateFiles(gameRoot, "*"+NarcoNetOldExtension, SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    Logger.LogDebug($"Cleaned up old file: {file}");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not delete {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error during old file cleanup: {ex.Message}");
        }
    }

    /// <summary>
    ///     Resolve the active SPT profile identifier (file stem) for ignored-profile bypass.
    ///     Returns null when no profile is active (e.g. headless) or the runtime is not ready.
    /// </summary>
    private static string? GetActiveProfileId()
    {
        try
        {
            if (!Singleton<EFT.TarkovApplication>.Instantiated)
            {
                return null;
            }

            var session = Singleton<EFT.TarkovApplication>.Instance?.Session;
            return ProfileBypass.NormalizeProfileIdentifier(session?.Profile?.Id);
        }
        catch (Exception e)
        {
#if NARCONET_DEBUG_LOGGING
            Logger.LogDebug($"Could not evaluate active profile for NarcoNet bypass: {e.GetType().Name}: {e.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    ///     Main plugin initialization coroutine - fetches server config and checks for updates
    /// </summary>
    private IEnumerator StartPlugin()
    {
        _cts = new CancellationTokenSource();

        // Clean up .narconet_old files left over from rename-then-copy updates
        CleanupRenamedFiles();

        if (Directory.Exists(PendingUpdatesDir))
        {
            Logger.LogWarning("Cleaning up stale pending updates from previous session.");
            try
            {
                Directory.Delete(PendingUpdatesDir, true);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to clean up pending updates directory: {ex.Message}");
            }
        }

        Logger.LogDebug("Requesting server version...");
        Task<string> versionTask = _server.GetNarcoNetVersion();
        yield return new WaitUntil(() => versionTask is { IsCompleted: true });
        try
        {
            string? version = versionTask.Result;

            Logger.LogInfo($"NarcoNet plugin loaded");
            Logger.LogDebug($"Server version: {version}");
            if (version != Info.Metadata.Version.ToString())
            {
                Logger.LogWarning(
                    $"Version mismatch: Server is running {version}, client is running {Info.Metadata.Version}");
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting server version. Please ensure the server mod is properly installed and try again."
            );
            yield break;
        }

        Logger.LogDebug("Requesting sync paths...");
        Task<List<SyncPath>> syncPathTask = _server.GetLocalSyncPaths();
        yield return new WaitUntil(() => syncPathTask is { IsCompleted: true });
        List<SyncPath>? syncPaths;
        try
        {
            syncPaths = syncPathTask.Result;
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to get sync paths: {e.GetType().Name}: {e.Message}");
            Logger.LogError($"Stack trace: {e.StackTrace}");
            if (e.InnerException != null)
            {
                Logger.LogError($"Inner exception: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
            }
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting sync paths: {e.Message}"
            );
            yield break;
        }

        Logger.LogDebug("Validating sync paths...");
        string? validationError = _initService.ValidateSyncPaths(syncPaths, Directory.GetCurrentDirectory());
        if (validationError != null)
        {
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to invalid sync path. {validationError}"
            );
            yield break;
        }

        Logger.LogDebug("Loading configuration...");
        try
        {
            _configService.Initialize(Config, syncPaths);
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to bind sync path configuration:\n{e}");
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error binding sync path configs. Please check your server configuration and try again."
            );
        }

        Logger.LogDebug("Loading local exclusions...");
        try
        {
            _localExclusions = _initService.LoadLocalExclusions(
                LocalExclusionsPath,
                _configService.IsHeadless(),
                _configService.GetHeadlessExclusionTemplates()
            );
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error with local exclusions. Please check BepInEx/LogOutput.log for more information."
            );
            yield break;
        }

        Logger.LogDebug("Requesting exclusions from server...");

        List<string>? exclusions;
        Task<List<string>> exclusionsTask = _server.GetListExclusions();
        yield return new WaitUntil(() => exclusionsTask is { IsCompleted: true });
        try
        {
            exclusions = exclusionsTask.Result;
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            Chainloader.DependencyErrors.Add(
                $"Could not load {Info.Metadata.Name} due to error requesting exclusions. Please ensure the server mod is properly installed and try again."
            );
            yield break;
        }

        Logger.LogDebug("Requesting ignored profiles...");
        var ignoredProfiles = new List<string>();
        Task<List<string>> ignoredProfilesTask = _server.GetIgnoredProfiles();
        yield return new WaitUntil(() => ignoredProfilesTask is { IsCompleted: true });
        try
        {
            ignoredProfiles = ignoredProfilesTask.Result ?? [];
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Failed to get ignored profiles: {e.GetType().Name}: {e.Message}. Continuing normal NarcoNet sync.");
        }

        Logger.LogDebug("Waiting for UI to initialize...");
        yield return new WaitUntil(() => Singleton<CommonUI>.Instantiated);

        string? activeProfileId = GetActiveProfileId();
        if (ProfileBypass.ShouldBypass(activeProfileId, ignoredProfiles))
        {
            Task notifyBypassTask = _server.NotifyProfileBypass(activeProfileId!);
            yield return new WaitUntil(() => notifyBypassTask.IsCompleted);

            Logger.LogInfo($"NarcoNet sync bypassed for configured profile '{activeProfileId}'.");
            yield break;
        }

#if NARCONET_DEBUG_LOGGING
        if (ignoredProfiles.Count > 0 && string.IsNullOrEmpty(activeProfileId))
        {
            Logger.LogDebug("Ignored profiles are configured, but the active profile could not be evaluated. Continuing normal NarcoNet sync.");
        }
#endif

        Logger.LogDebug("Hashing local files...");
        if (exclusions == null)
        {
            yield break;
        }

        if (!_configService.IsHeadless())
        {
            _uiService.ShowDiagnosticWindow();
        }
        _uiService.UpdateDiagnosticStep("local_hash", "Hashing local files...", DiagnosticState.InProgress);

        {
            List<Regex> remoteExclusionRegex = exclusions.Select(Glob.CreateNoEnd).ToList();
            List<Regex> localExclusionRegex = _localExclusions.Select(Glob.CreateNoEnd).ToList();

            Task<SyncPathModFiles> localModFilesTask = Sync.HashLocalFiles(
                Directory.GetCurrentDirectory(),
                EnabledSyncPaths,
                remoteExclusionRegex,
                localExclusionRegex
            );

            yield return new WaitUntil(() => localModFilesTask.IsCompleted);

            if (localModFilesTask.IsFaulted)
            {
                Logger.LogError($"Failed to hash local files: {localModFilesTask.Exception?.GetType().Name}: {localModFilesTask.Exception?.Message}");
                if (localModFilesTask.Exception?.InnerException != null)
                {
                    Logger.LogError($"Inner exception: {localModFilesTask.Exception.InnerException.GetType().Name}: {localModFilesTask.Exception.InnerException.Message}");
                }
                Logger.LogError($"Stack trace: {localModFilesTask.Exception?.StackTrace}");
                _uiService.UpdateDiagnosticStep("local_hash", "Error hashing local files", DiagnosticState.Error);
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to error hashing local files: {localModFilesTask.Exception?.InnerException?.Message ?? localModFilesTask.Exception?.Message}"
                );
                _pluginFinished = true;
                yield break;
            }

            _localModFiles = localModFilesTask.Result;
            SyncPathModFiles localModFiles = _localModFiles;
            int localFileCount = localModFiles.Sum(kvp => kvp.Value.Count);

            Logger.LogDebug($"Hashed {localFileCount} local files");
            _uiService.UpdateDiagnosticStep("local_hash", $"Done — {localFileCount} files hashed", DiagnosticState.Done);

            VFS.WriteTextFile(LocalHashesPath, Json.Serialize(localModFiles));

            Logger.LogDebug("Requesting remote hashes...");
            _uiService.UpdateDiagnosticStep("remote_hash", "Fetching remote hashes...", DiagnosticState.InProgress);
            Task<SyncPathModFiles> remoteHashesTask =
                _server.GetRemoteHashes(EnabledSyncPaths);
            yield return new WaitUntil(() => remoteHashesTask is { IsCompleted: true });
            try
            {
                SyncPathModFiles? remoteHashes = remoteHashesTask.Result;
                if (remoteHashes == null)
                {
                    Logger.LogError("Remote hashes task returned null");
                    yield break;
                }

                _remoteModFiles = remoteHashes;
                WriteDiagDump("RemoteHashes_Raw.txt", _remoteModFiles, "Raw server response (before client-side filtering)");
                FilterRemoteModFiles(remoteExclusionRegex, localExclusionRegex);
                WriteDiagDump("RemoteHashes_Filtered.txt", _remoteModFiles, "After client-side FilterRemoteModFiles");

                int remoteFileCount = _remoteModFiles.Sum(kvp => kvp.Value.Count);
                _uiService.UpdateDiagnosticStep("remote_hash", $"Done — {remoteFileCount} files from server", DiagnosticState.Done);
            }
            catch (Exception e)
            {
                _uiService.UpdateDiagnosticStep("remote_hash", "Error fetching remote hashes", DiagnosticState.Error);
                Logger.LogError("Failed to get remote hashes");
                Logger.LogError($"  Exception Type: {e.GetType().FullName}");
                Logger.LogError($"  Message: {(string.IsNullOrEmpty(e.Message) ? "<empty>" : e.Message)}");

                if (e.InnerException != null)
                {
                    Logger.LogError($"  Inner Exception: {e.InnerException.GetType().FullName}");
                    Logger.LogError($"  Inner Message: {(string.IsNullOrEmpty(e.InnerException.Message) ? "<empty>" : e.InnerException.Message)}");

                    // Check for deeper nested exceptions
                    if (e.InnerException.InnerException != null)
                    {
                        Logger.LogError($"  Nested Exception: {e.InnerException.InnerException.GetType().FullName}");
                        Logger.LogError($"  Nested Message: {(string.IsNullOrEmpty(e.InnerException.InnerException.Message) ? "<empty>" : e.InnerException.InnerException.Message)}");
                    }
                }

                Logger.LogError($"  Stack Trace: {e.StackTrace}");

                string errorMsg = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to error requesting server mod list: {errorMsg}"
                );
                yield break;
            }

            Logger.LogDebug("Loading previous server hash manifest...");
            SyncPathModFiles? previousServerModFiles = _previousServerHashManifestStore.Load(PreviousServerHashesPath);

            Logger.LogDebug("Comparing local and remote files...");
            _uiService.UpdateDiagnosticStep("compare", "Comparing files...", DiagnosticState.InProgress);
            try
            {
                AnalyzeModFiles(localModFiles, previousServerModFiles);

                int added = _addedFiles.Sum(kvp => kvp.Value.Count);
                int updated = _updatedFiles.Sum(kvp => kvp.Value.Count);
                int removed = _removedFiles.Sum(kvp => kvp.Value.Count);
                _uiService.UpdateDiagnosticStep("compare", $"Done — {added} added, {updated} updated, {removed} removed", DiagnosticState.Done);

                _uiService.SetDiagnosticShowFilesAction(() =>
                    _uiService.ShowFileComparisonWindow(_localModFiles, _remoteModFiles));

                if (_configService.AutoCloseDiagnostic.Value)
                {
                    _uiService.HideDiagnosticWindow();
                }
            }
            catch (Exception e)
            {
                _uiService.UpdateDiagnosticStep("compare", "Error comparing files", DiagnosticState.Error);
                Logger.LogError($"Failed to analyze mod files: {e.GetType().Name}: {e.Message}");
                Logger.LogError($"Stack trace: {e.StackTrace}");
                if (e.InnerException != null)
                {
                    Logger.LogError($"Inner exception: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
                }
                Chainloader.DependencyErrors.Add(
                    $"Could not load {Info.Metadata.Name} due to error analyzing mod files: {e.Message}"
                );
            }
        }
    }

    /// <summary>
    ///     Persists the full current filtered server hash baseline after a successful sync/apply path.
    /// </summary>
    private void SavePreviousServerHashManifest()
    {
        try
        {
            _previousServerHashManifestStore.Save(PreviousServerHashesPath, _remoteModFiles);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not persist previous server hash manifest after successful sync: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Removes excluded files from _remoteModFiles so they are never downloaded.
    ///     Remote exclusions always apply; local exclusions apply only on non-enforced paths.
    /// </summary>
    private void FilterRemoteModFiles(List<Regex> remoteExclusions, List<Regex> localExclusions)
    {
        int removedCount = 0;
        var filteredLog = new StringBuilder();
        filteredLog.AppendLine($"# FilterRemoteModFiles Diagnostic");
        filteredLog.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        filteredLog.AppendLine($"# Remote exclusion patterns ({remoteExclusions.Count}):");
        foreach (Regex r in remoteExclusions)
            filteredLog.AppendLine($"#   {r}");
        filteredLog.AppendLine($"# Local exclusion patterns ({localExclusions.Count}):");
        foreach (Regex r in localExclusions)
            filteredLog.AppendLine($"#   {r}");
        filteredLog.AppendLine($"# EnabledSyncPaths ({EnabledSyncPaths.Count}):");
        foreach (SyncPath sp in EnabledSyncPaths)
            filteredLog.AppendLine($"#   {sp.Path} (Enforced={sp.Enforced})");
        filteredLog.AppendLine($"# RemoteModFiles keys ({_remoteModFiles.Count}):");
        foreach (string key in _remoteModFiles.Keys)
            filteredLog.AppendLine($"#   '{key}' ({_remoteModFiles[key].Count} files)");
        filteredLog.AppendLine($"#");
        filteredLog.AppendLine($"# Removed files:");

        foreach (SyncPath syncPath in EnabledSyncPaths)
        {
            if (!_remoteModFiles.TryGetValue(syncPath.Path, out var files))
            {
                filteredLog.AppendLine($"# WARN: TryGetValue FAILED for syncPath '{syncPath.Path}'");
                continue;
            }

            List<string> keysToRemove = [];
            foreach (string filePath in files.Keys)
            {
                string normalized = filePath.Replace(@"\", "/");
                Regex? matchedRemote = remoteExclusions.FirstOrDefault(r => r.IsMatch(normalized));
                if (matchedRemote != null)
                {
                    keysToRemove.Add(filePath);
                    filteredLog.AppendLine($"REMOTE_EXCL | {filePath} | pattern: {matchedRemote}");
                    continue;
                }

                if (syncPath.Enforced) continue;
                {
                    Regex? matchedLocal = localExclusions.FirstOrDefault(r => r.IsMatch(normalized));
                    if (matchedLocal == null) continue;
                    keysToRemove.Add(filePath);
                    filteredLog.AppendLine($"LOCAL_EXCL  | {filePath} | pattern: {matchedLocal}");
                }
            }

            foreach (string key in keysToRemove)
            {
                files.Remove(key);
                removedCount++;
            }
        }

        filteredLog.AppendLine($"#");
        filteredLog.AppendLine($"# Total removed: {removedCount}");

        try
        {
            File.WriteAllText(Path.Combine(NarcoNetDir, "FilterDiagnostic.txt"), filteredLog.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to write filter diagnostic: {ex.Message}");
        }

        if (removedCount > 0)
        {
            Logger.LogInfo($"Filtered {removedCount} excluded files from remote file list");
        }
    }

    /// <summary>
    ///     Writes a diagnostic dump of mod files to a text file for debugging.
    /// </summary>
    private static void WriteDiagDump(string fileName, SyncPathModFiles modFiles, string header)
    {
        try
        {
            string path = Path.Combine(NarcoNetDir, fileName);
            var sb = new StringBuilder();
            sb.AppendLine($"# {header}");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"# Format: [SyncPath] | [FilePath] | [Hash] | [Type]");
            sb.AppendLine($"# ─────────────────────────────────────────────────────────");

            int total = 0;
            foreach (var kvp in modFiles)
            {
                sb.AppendLine($"#");
                sb.AppendLine($"# SyncPath: {kvp.Key} ({kvp.Value.Count} entries)");

                foreach (var file in kvp.Value.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string type = file.Value.Directory ? "DIR" : "FILE";
                    string hash = file.Value.Directory ? "--" : file.Value.Hash;
                    sb.AppendLine($"{kvp.Key} | {file.Key} | {hash} | {type}");
                    total++;
                }
            }

            sb.AppendLine($"#");
            sb.AppendLine($"# Total: {total} entries");

            File.WriteAllText(path, sb.ToString());
            Logger.LogDebug($"Wrote diagnostic dump: {path} ({total} entries)");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to write diagnostic dump {fileName}: {ex.Message}");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Dispose();
        }
    }
}
