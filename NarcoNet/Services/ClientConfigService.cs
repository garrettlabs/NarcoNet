using BepInEx.Bootstrap;
using BepInEx.Configuration;
using NarcoNet.Utilities;

namespace NarcoNet.Services;

/// <summary>
///     Manages client configuration settings
/// </summary>
public class ClientConfigService : IClientConfigService
{
    private static readonly List<string> HeadlessExclusionTemplates =
    [
        "BepInEx/plugins/Fika/Fika.Headless*",
        "BepInEx/plugins/Fika/LICENSE-HEADLESS*",
        "BepInEx/plugins/AirFilterQOLClientMod.dll",
        "BepInEx/plugins/AllQuestsCheckmarks/",
        "BepInEx/plugins/AmandsSense/",
        "BepInEx/plugins/BorkelRNVG/",
        "BepInEx/plugins/BringBackConcussion.dll",
        "BepInEx/plugins/CaliberUnderName.dll",
        "BepInEx/plugins/CloudSix/",
        "BepInEx/plugins/com.swiftxp.spt.showmethemoney/",
        "BepInEx/plugins/com.swiftxp.spt.showmethemoney.quicksell/",
        "BepInEx/plugins/ContinuousHealing.dll",
        "BepInEx/plugins/Deminvincibility.dll",
        "BepInEx/plugins/DrakiaXYZ-GildedKeyStorage-Client.dll",
        "BepInEx/plugins/DynamicMaps",
        "BepInEx/plugins/flir.*",
        "BepInEx/plugins/dvize.GTFO",
        "BepInEx/plugins/flir.IncreaseLookDirection/",
        "BepInEx/plugins/SamSWAT.TimeWeatherChanger",
        "BepInEx/plugins/FOVFix.dll",
        "BepInEx/plugins/Gaylatea-UseLooseLoot.dll",
        "BepInEx/plugins/HallOfFameImprovements.dll",
        "BepInEx/plugins/HandsAreNotBusy.dll",
        "BepInEx/plugins/headshotdarkness/",
        "BepInEx/plugins/HealingAutoCancel.dll",
        "BepInEx/plugins/HollywoodFX/",
        "BepInEx/plugins/HollywoodGraphics/",
        "BepInEx/plugins/MedEffectsHUD/",
        "BepInEx/plugins/MoxoPixel.MenuOverhaul/",
        "BepInEx/plugins/SkillDistribution-Client.dll",
        "BepInEx/plugins/SPTPropaneRemover.dll",
        "BepInEx/plugins/Tetris.DeHazardifier.dll",
        "BepInEx/plugins/Tosox.ChamberAmmoInfo.dll",
        "BepInEx/plugins/Tosox.DynamicItemWeights/",
        "BepInEx/plugins/Tosox.DynamicItemWeights.dll",
        "BepInEx/plugins/Tyfon.HideoutInProgress.dll",
        "BepInEx/plugins/VisualAssist/",
        "BepInEx/plugins/DrakiaXYZ-QuestTracker",
        "BepInEx/plugins/IcyClawz.MunitionsExpert.dll",
        "BepInEx/plugins/Kaeno-TraderScrolling.dll",
        "BepInEx/Plugins/NerfBotGrenades.dll",
        "BepInEx/plugins/Terkoiz.Skipper.dll",
        "BepInEx/plugins/ChouUn.Iof.dll",
        "BepInEx/plugins/acidphantasm-stattrack/",
        "BepInEx/plugins/HealingAutoCancel.dll",
        "BepInEx/plugins/InteractableExfilsAPI/"
    ];

    private ConfigEntry<bool>? _deleteRemovedFiles;
    private ConfigEntry<bool>? _autoCloseDiagnostic;
    private Dictionary<string, ConfigEntry<bool>>? _syncPathToggles;
    private List<SyncPath>? _syncPaths;

    /// <inheritdoc/>
    public ConfigEntry<bool> DeleteRemovedFiles =>
        _deleteRemovedFiles ?? throw new InvalidOperationException("Configuration not initialized");

    /// <inheritdoc/>
    public ConfigEntry<bool> AutoCloseDiagnostic =>
        _autoCloseDiagnostic ?? throw new InvalidOperationException("Configuration not initialized");

    /// <inheritdoc/>
    public Dictionary<string, ConfigEntry<bool>> SyncPathToggles =>
        _syncPathToggles ?? throw new InvalidOperationException("Configuration not initialized");

    /// <inheritdoc/>
    public List<SyncPath> EnabledSyncPaths
    {
        get
        {
            if (_syncPaths == null)
            {
                throw new InvalidOperationException("Configuration not initialized");
            }

            if (_syncPathToggles == null)
            {
                return [];
            }

            return _syncPaths
                .Where(syncPath => _syncPathToggles[syncPath.Path].Value || syncPath.Enforced)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public void Initialize(ConfigFile config, List<SyncPath> syncPaths)
    {
        var deleteRemovedFiles = config.Bind(
            "General",
            "Delete Removed Files",
            true,
            "Should the mod delete files that have been removed from the server?"
        );

        var autoCloseDiagnostic = config.Bind(
            "General",
            "Auto-Close Diagnostic Window",
            true,
            "Automatically close the sync diagnostic window when sync completes. Disable for debugging."
        );

        var syncPathToggles = syncPaths
            .Select(syncPath => new KeyValuePair<string, ConfigEntry<bool>>(
                syncPath.Path,
                config.Bind(
                    "Synced Paths",
                    syncPath.Name.Replace("\\", "/"),
                    syncPath.Enabled,
                    new ConfigDescription(
                        $"Should the mod attempt to sync files from {syncPath.Path.Replace("\\", "/")}",
                        null,
                        new ConfigurationManagerAttributes { ReadOnly = syncPath.Enforced }
                    )
                )
            ))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Assign all fields only after everything succeeds
        _syncPaths = syncPaths;
        _deleteRemovedFiles = deleteRemovedFiles;
        _autoCloseDiagnostic = autoCloseDiagnostic;
        _syncPathToggles = syncPathToggles;
    }

    /// <inheritdoc/>
    public bool IsHeadless()
    {
        return Chainloader.PluginInfos.ContainsKey("com.fika.headless");
    }

    /// <inheritdoc/>
    public List<string> GetHeadlessExclusionTemplates()
    {
        return HeadlessExclusionTemplates;
    }
}
