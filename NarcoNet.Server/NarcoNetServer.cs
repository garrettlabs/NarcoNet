using JetBrains.Annotations;

using Microsoft.Extensions.Logging;

using NarcoNet.Server.Models;
using NarcoNet.Server.Services;
using NarcoNet.Utilities;

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.External;
using SPTarkov.Server.Core.Models.Spt.Mod;

using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;
#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).

#pragma warning disable IDE0160
namespace NarcoNet.Server;
#pragma warning restore IDE0160

/// <summary>
///     Metadata for the NarcoNet server mod
/// </summary>
[UsedImplicitly]
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = NarcoNetConstants.ServerPluginGuid;
    public override string Name { get; init; } = NarcoNetConstants.ProductName;
    public override string Author { get; init; } = NarcoNetConstants.Author;
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new(NarcoNetVersion.Version);
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string? License { get; init; } = "MIT";
}

/// <summary>
///     Main NarcoNet server mod class
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 2), UsedImplicitly]
public class NarcoNetServer(
    ILogger<NarcoNetServer> logger,
    ConfigService configService,
    NarcoNetHttpListener httpListener,
    FileWatcherService fileWatcherService)
    : IPreSptLoadModAsync
{
    private static bool _loadFailed;

    public async Task PreSptLoadAsync()
    {
        try
        {
#if NARCONET_DEBUG_LOGGING
            logger.LogDebug("PreSptLoadAsync starting...");
#endif
            // Get mod path
            string? modPath = GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                _loadFailed = true;
                logger.LogError("Failed to find mod directory");
                return;
            }

#if NARCONET_DEBUG_LOGGING
            logger.LogDebug("Mod path found: {ModPath}", modPath);
#endif

            // Load configuration
            NarcoNetConfig config = await configService.LoadConfigAsync(modPath);
#if NARCONET_DEBUG_LOGGING
            logger.LogDebug("Configuration loaded successfully");
            logger.LogDebug("Sync paths configured: {SyncPathsCount}", config.SyncPaths.Count);
            foreach (var syncPath in config.SyncPaths)
            {
                logger.LogDebug("  - {SyncPathPath} (Enabled: {SyncPathEnabled}, RestartRequired: {SyncPathRestartRequired}, Enforced: {SyncPathEnforced})", syncPath.Path, syncPath.Enabled, syncPath.RestartRequired, syncPath.Enforced);
            }
#endif

            // Start watching for runtime file changes (invalidates hash cache on disk changes)
            fileWatcherService.StartWatching(config.SyncPaths);

            // Initialize HTTP listener (only if load succeeded)
            if (!_loadFailed)
            {
                httpListener.Initialize(config, NarcoNetVersion.Version);
                logger.LogInformation("NarcoNet server mod loaded successfully");
#if NARCONET_DEBUG_LOGGING
                logger.LogDebug("HTTP listener initialized successfully");
#endif
            }
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            logger.LogError(ex, "Failed to load NarcoNet server mod");
            throw;
        }
    }

    private string? GetModPath()
    {
        try
        {
            // Try to find the mod directory
            string modsPath = Path.Combine(Directory.GetCurrentDirectory(), "user", "mods");
            if (!Directory.Exists(modsPath))
            {
                return null;
            }

            // Case-insensitive search for cross-platform compatibility
            string[] modDirectories = Directory.GetDirectories(modsPath, "*", SearchOption.TopDirectoryOnly)
                .Where(dir => Path.GetFileName(dir).Contains("narconet", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return modDirectories.Length > 0 ? modDirectories[0] : null;
        }
        catch
        {
            return null;
        }
    }
}
