using System.Text.Json;
using System.Text.Json.Nodes;

using JetBrains.Annotations;

using Microsoft.Extensions.Logging;

using NarcoNet.Server.Models;
using NarcoNet.Utilities;

using SPTarkov.DI.Annotations;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NarcoNet.Server.Services;

/// <summary>
/// Service for loading and validating NarcoNet configuration
/// Supports both YAML (.yaml/.yml) and JSON (.json/.jsonc) formats
/// </summary>
[Injectable]
[UsedImplicitly]
public class ConfigService(ILogger<ConfigService> logger)
{
    internal const string DefaultYamlConfig = """
                                             # ╔══════════════════════════════════════════════════════════════════════╗
                                             # ║                    NarcoNet Configuration                            ║
                                             # ║          Sync mods & plugins from SPT server to clients              ║
                                             # ╚══════════════════════════════════════════════════════════════════════╝
                                             #
                                             # SPT 4.0 Folder Structure:
                                             #   C:\SPT\                  <- Game root (EscapeFromTarkov.exe)
                                             #   ├── BepInEx\             <- Client-side mods (synced to clients)
                                             #   │   ├── plugins\         <- Client plugins
                                             #   │   ├── patchers\        <- Client patchers
                                             #   │   └── config\          <- Client configs
                                             #   └── SPT\                 <- Server directory
                                             #       ├── SPT.Server.exe
                                             #       └── user\mods\       <- Server-side mods
                                             #
                                             # Path Reference Rules:
                                             #   - All paths are relative to the game root folder (where EscapeFromTarkov.exe lives)
                                             #   - Use forward slashes only
                                             #
                                             # Examples:
                                             #   "BepInEx/plugins"           -> C:\SPT\BepInEx\plugins
                                             #   "SPT/user/mods"             -> C:\SPT\SPT\user\mods
                                             #   "BepInEx/config"            -> C:\SPT\BepInEx\config

                                             syncPaths:
                                               # Client-side mods (in game root)
                                               - BepInEx/plugins
                                               - BepInEx/patchers
                                               # - BepInEx/config    # Disabled by default — client configs are personal preference

                                               # Server-side mods
                                               - name: "(Optional) Server mods"
                                                 path: SPT/user/mods
                                                 enabled: false           # Set to true to sync this path
                                                 enforced: false          # If true, client cannot opt out via local exclusions
                                                 silent: false            # If true, updates without showing UI
                                                 restartRequired: false   # If true, prompts client to restart after update

                                             # Exclusions prevent specific files/patterns from being synced
                                             #
                                             # Glob Pattern Examples:
                                             #   **/*.log           - All .log files in any subdirectory
                                             #   **/cache           - All 'cache' folders anywhere
                                             #   **/node_modules    - All 'node_modules' folders (recursive)
                                             #   SPT/user/mods/*/logs - 'logs' folder in any immediate subdirectory
                                             #   **/*.{js,map}      - All .js and .map files anywhere
                                             #   mod-name/**        - Everything inside 'mod-name' folder
                                             #
                                             # Special Characters:
                                             #   *    - Matches any characters except /
                                             #   **   - Matches any characters including / (recursive)
                                             #   ?    - Matches exactly one character
                                             #   [abc] - Matches any character in brackets
                                             #   {a,b} - Matches any pattern in braces
                                             #
                                             # All paths are relative to the game root folder
                                             exclusions:
                                               # SPT Core (never sync)
                                               - BepInEx/plugins/spt
                                               - BepInEx/patchers/spt-prepatch.dll
                                               # if you comment this line clients will need
                                               # to update the narconet plugin manually
                                               - BepInEx/plugins/MadManBeavis-NarcoNet

                                               # sain auto generates those files and modifies them
                                               - BepInEx/plugins/SAIN/BotTypes.json
                                               - BepInEx/plugins/SAIN/Presets/ConfigSettings.json
                                               - BepInEx/plugins/SAIN/Default Bot Config Values/Santa Claus.json

                                               # Fika Headless
                                               - BepInEx/plugins/Fika/Fika.Headless*
                                               - BepInEx/plugins/Fika/LICENSE-HEADLESS*

                                               # Common client mod data folders
                                               - BepInEx/plugins/DrakiaXYZ-QuestTracker/config/*
                                               - BepInEx/plugins/DanW-SPTQuestingBots/log
                                               - BepInEx/plugins/kmyuhkyuk-EFTApi/cache

                                               # Common server mod data folders
                                               - SPT/user/mods/SPT-Realism/ProfileBackups
                                               - SPT/user/mods/fika-server/types
                                               - SPT/user/mods/fika-server/cache
                                               - SPT/user/mods/zzDrakiaXYZ-LiveFleaPrices/config
                                               - SPT/user/mods/ExpandedTaskText/src/**/cache.json
                                               - SPT/user/mods/leaves-loot_fuckery/output
                                               - SPT/user/mods/zz_guiltyman-addmissingquestweaponrequirements/log.log
                                               - SPT/user/mods/zz_guiltyman-addmissingquestweaponrequirements/user/logs
                                               - SPT/user/mods/acidphantasm-progressivebotsystem/logs

                                               # Admin/Dev exclusions (use .nosync marker files)
                                               - "**/*.nosync"              # Any file ending in .nosync
                                               - "**/*.nosync.txt"          # Any .nosync.txt file

                                               # Development files (recursive patterns)
                                               - SPT/user/mods/**/.git          # All .git folders in server mods
                                               - SPT/user/mods/**/node_modules  # All node_modules folders
                                               - SPT/user/mods/**/*.js          # All JavaScript files (recursive)
                                               - SPT/user/mods/**/*.js.map      # All source maps (recursive)
                                               - SPT/user/mods/**/*.ts          # All TypeScript source files
                                               - "**/src/**/*.ts"               # All TS files in any 'src' folder

                                               # Log files (pattern matching)
                                               - "**/*.log"                 # All .log files anywhere
                                               - "**/logs/**"               # All files in any 'logs' folder
                                               - "**/log/**"                # All files in any 'log' folder

                                               # Cache and temporary files
                                               - "**/cache/**"              # All files in any 'cache' folder
                                               - "**/temp/**"               # All files in any 'temp' folder
                                               - "**/*.tmp"                 # All temporary files
                                               - "**/*.cache"               # All cache files

                                               # Windows metadata
                                               - "**/*:Zone.Identifier"     # Windows download zone markers

                                             # ═══════════════════════════════════════════════════════════════════════
                                             # GLOB PATTERN QUICK REFERENCE
                                             # ═══════════════════════════════════════════════════════════════════════
                                             #
                                             # Pattern Matching Wildcards:
                                             #   *        Matches any characters EXCEPT / (single directory level)
                                             #   **       Matches any characters INCLUDING / (multiple directory levels)
                                             #   ?        Matches exactly ONE character
                                             #   [abc]    Matches any character in brackets: a, b, or c
                                             #   [a-z]    Matches any character in range: a through z
                                             #   {a,b}    Matches either pattern: a OR b
                                             #
                                             # Real-World Examples:
                                             #
                                             #   *.dll                        # Only .dll files in root (not subdirectories)
                                             #   **/*.dll                     # All .dll files recursively in any subdirectory
                                             #   SPT/user/mods/*/config.json  # config.json in immediate subdirectories only
                                             #   SPT/user/mods/**/config.json # config.json in any nested subdirectory
                                             #   **/{logs,cache}              # Any folder named 'logs' OR 'cache' anywhere
                                             #   **/*.{log,txt}               # All .log OR .txt files anywhere
                                             #   mod-??.dll                   # Matches: mod-01.dll, mod-ab.dll (2 chars)
                                             #   config-[0-9].json            # Matches: config-0.json through config-9.json
                                             #   !important.dll               # Negation - INCLUDE this file (override exclusion)
                                             #
                                             # Common Use Cases:
                                             #
                                             #   Exclude all logs:             - "**/*.log"
                                             #   Exclude specific mod:         - "SPT/user/mods/problem-mod/**"
                                             #   Exclude all node_modules:     - "**/node_modules/**"
                                             #   Exclude TypeScript sources:   - "**/*.ts"
                                             #   Exclude development folders:  - "**/{.git,node_modules,src}/**"
                                             #   Exclude backup files:         - "**/*.{bak,backup,old}"
                                             #   Exclude cache anywhere:       - "**/cache/**"
                                             #
                                             # Pro Tips:
                                             #   - Use quotes around patterns with special chars: "**/*.{js,ts}"
                                             #   - All paths are relative to the game root folder
                                             #   - Test patterns carefully - they match anywhere in the path
                                             #   - More specific patterns = better performance
                                             #   - Order doesn't matter - all patterns are checked
                                             # ═══════════════════════════════════════════════════════════════════════
                                             """;


    /// <summary>
    /// Load the NarcoNet configuration from file (supports YAML and JSON)
    /// </summary>
    public async Task<NarcoNetConfig> LoadConfigAsync(string modPath)
    {
        // Check for config files in order of preference: YAML first, then JSON
        string? configPath = FindConfigFile(modPath);

        if (configPath == null)
        {
            // Create default YAML config
            configPath = Path.Combine(modPath, "config.yaml");
            await File.WriteAllTextAsync(configPath, DefaultYamlConfig);
        }

        (List<SyncPath> rawSyncPaths, List<string> exclusions) = await LoadConfigFileAsync(configPath);

        // Migrate old-format paths (with ../ prefix) to gameroot-relative paths
        MigrateOldFormatPaths(ref rawSyncPaths, ref exclusions);

        ValidateConfig(rawSyncPaths, exclusions, configPath);

        var syncPaths = new List<SyncPath>();

        // Only add enabled or enforced sync paths
        var filteredPaths = rawSyncPaths.Where(sp => sp.Enabled || sp.Enforced).ToList();

        syncPaths.AddRange(filteredPaths);

        // Sort by path length descending
        syncPaths = syncPaths.OrderByDescending(sp => sp.Path.Length).ToList();

        return new NarcoNetConfig
        {
            SyncPaths = syncPaths,
            Exclusions = exclusions
        };
    }

    /// <summary>
    /// Find config file (checks YAML first, then JSON)
    /// </summary>
    private static string? FindConfigFile(string modPath)
    {
        // Check YAML variants
        string yamlPath = Path.Combine(modPath, "config.yaml");
        if (File.Exists(yamlPath)) return yamlPath;

        string ymlPath = Path.Combine(modPath, "config.yml");
        if (File.Exists(ymlPath)) return ymlPath;

        // Check JSON variants
        string jsoncPath = Path.Combine(modPath, "config.jsonc");
        if (File.Exists(jsoncPath)) return jsoncPath;

        string jsonPath = Path.Combine(modPath, "config.json");
        return File.Exists(jsonPath) ? jsonPath : null;
    }

    /// <summary>
    /// Load config file based on extension
    /// </summary>
    private async Task<(List<SyncPath> syncPaths, List<string> exclusions)> LoadConfigFileAsync(string configPath)
    {
        string extension = Path.GetExtension(configPath).ToLowerInvariant();
        string configText = await File.ReadAllTextAsync(configPath);

        return extension switch
        {
            ".yaml" or ".yml" => LoadYamlConfig(configText),
            ".json" or ".jsonc" => LoadJsonConfig(configText),
            _ => throw new NotSupportedException($"Unsupported config format: {extension}")
        };
    }

    /// <summary>
    /// Load YAML configuration
    /// </summary>
    internal (List<SyncPath>, List<string>) LoadYamlConfig(string yamlContent)
    {
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        YamlConfig config;
        try
        {
            config = deserializer.Deserialize<YamlConfig>(yamlContent);
        }
        catch (Exception) when (!yamlContent.Contains("\nsyncPaths:") && !yamlContent.StartsWith("syncPaths:"))
        {
            // Auto-fix: older config templates may be missing the 'syncPaths:' key (removed in v1.0.13)
            yamlContent = InsertSyncPathsKey(yamlContent);
            logger.LogWarning("NarcoNet config is missing 'syncPaths:' key — auto-fixed for this session. " +
                              "Please delete your config.yaml and restart the server to regenerate a clean template.");
            config = deserializer.Deserialize<YamlConfig>(yamlContent);
        }

        var syncPaths = new List<SyncPath>();
        if (config.SyncPaths != null)
        {
            foreach (object item in config.SyncPaths)
            {
                switch (item)
                {
                    case string pathStr:
                        // Simple string format
                        syncPaths.Add(new SyncPath(
                            Path: pathStr,
                            Name: pathStr,
                            Enabled: true,
                            Enforced: false,
                            Silent: false,
                            RestartRequired: true
                        ));
                        break;
                    case Dictionary<object, object> dict:
                    {
                        // Object format
                        string? path = dict.TryGetValue("path", out object? p)
                            ? p.ToString()
                            : throw new InvalidOperationException("Missing 'path' in syncPath object");

                        var enabledValue = true;
                        if (dict.TryGetValue("enabled", out object? e))
                        {
                            enabledValue = e switch
                            {
                                bool b => b,
                                string s => bool.Parse(s),
                                _ => true
                            };
                        }

                        var enforcedValue = false;
                        if (dict.TryGetValue("enforced", out object? enf))
                        {
                            enforcedValue = enf switch
                            {
                                bool b => b,
                                string s => bool.Parse(s),
                                _ => false
                            };
                        }

                        var silentValue = false;
                        if (dict.TryGetValue("silent", out object? sil))
                        {
                            silentValue = sil switch
                            {
                                bool b => b,
                                string s => bool.Parse(s),
                                _ => false
                            };
                        }

                        var restartValue = true;
                        if (dict.TryGetValue("restartRequired", out object? r))
                        {
                            restartValue = r switch
                            {
                                bool b => b,
                                string s => bool.Parse(s),
                                _ => true
                            };
                        }

                        syncPaths.Add(new SyncPath(
                            Path: path!,
                            Name: dict.TryGetValue("name", out object? n) ? n.ToString() ?? path! : path!,
                            Enabled: enabledValue,
                            Enforced: enforcedValue,
                            Silent: silentValue,
                            RestartRequired: restartValue
                        ));
                        break;
                    }
                }
            }
        }

        List<string> exclusions = config.Exclusions ?? [];

        return (syncPaths, exclusions);
    }

    /// <summary>
    /// Load JSON configuration
    /// </summary>
    private (List<SyncPath>, List<string>) LoadJsonConfig(string jsonContent)
    {
        JsonNode? jsonNode = JsonNode.Parse(jsonContent);
        JsonArray? syncPathsNode = jsonNode?["syncPaths"]?.AsArray();
        JsonArray? exclusionsNode = jsonNode?["exclusions"]?.AsArray();

        var rawSyncPaths = new List<SyncPath>();
        if (syncPathsNode != null)
        {
            foreach (JsonNode? node in syncPathsNode)
            {
                switch (node)
                {
                    case JsonValue when node.GetValueKind() == JsonValueKind.String:
                    {
                        // String path
                        var pathStr = node.GetValue<string>();
                        rawSyncPaths.Add(new SyncPath(
                            Path: pathStr,
                            Name: pathStr,
                            Enabled: true,
                            Enforced: false,
                            Silent: false,
                            RestartRequired: true
                        ));
                        break;
                    }
                    case JsonObject obj:
                    {
                        // Object path
                        string path = obj["path"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing 'path' in syncPath object");
                        rawSyncPaths.Add(new SyncPath(
                            Path: path,
                            Name: obj["name"]?.GetValue<string>() ?? path,
                            Enabled: obj["enabled"]?.GetValue<bool>() ?? true,
                            Enforced: obj["enforced"]?.GetValue<bool>() ?? false,
                            Silent: obj["silent"]?.GetValue<bool>() ?? false,
                            RestartRequired: obj["restartRequired"]?.GetValue<bool>() ?? true
                        ));
                        break;
                    }
                }
            }
        }

        var exclusions = new List<string>();
        if (exclusionsNode == null) return (rawSyncPaths, exclusions);
        {
            foreach (JsonNode? node in exclusionsNode)
            {
                if (node is JsonValue)
                {
                    exclusions.Add(node.GetValue<string>());
                }
            }
        }

        return (rawSyncPaths, exclusions);
    }

    /// <summary>
    /// Validate the configuration
    /// </summary>
    private void ValidateConfig(List<SyncPath> syncPaths, List<string> exclusions, string configPath)
    {
        if (syncPaths == null)
            throw new InvalidOperationException($"NarcoNet: '{configPath}' 'syncPaths' is not an array. Please verify your config is correct and try again.");

        if (exclusions == null)
            throw new InvalidOperationException($"NarcoNet: '{configPath}' 'exclusions' is not an array. Please verify your config is correct and try again.");

        var uniquePaths = new HashSet<string>();

        // All paths are relative to game root
        string gameRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));

        foreach (string path in syncPaths.Select(syncPath => syncPath.Path))
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException($"NarcoNet: '{configPath}' 'syncPaths' is missing 'path'. Please verify your config is correct and try again.");

            if (Path.IsPathRooted(path))
                throw new InvalidOperationException($"NarcoNet: SyncPaths must be relative paths. Invalid path '{path}'");

            if (path.Contains(".."))
                throw new InvalidOperationException($"NarcoNet: SyncPaths must not contain '..'. Invalid path '{path}'. All paths should be relative to the game root folder.");

            // Resolve the full path from game root
            string fullPath = Path.GetFullPath(Path.Combine(gameRoot, path));

            // Check that the resolved path is within game root
            string relativePath = Path.GetRelativePath(gameRoot, fullPath);
            if (relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
                throw new InvalidOperationException($"NarcoNet: SyncPaths must stay within game root folder. Invalid path '{path}' resolves outside game root.");

            if (!uniquePaths.Add(path))
                throw new InvalidOperationException($"NarcoNet: SyncPaths must be unique. Duplicate path '{path}'");

            if (exclusions.Contains(path))
                throw new InvalidOperationException($"NarcoNet: '{path}' has been added as a sync path and is also in the 'exclusions' array.");
        }
    }

    /// <summary>
    /// Migrate old-format paths that used "../" prefix (server-CWD-relative) to
    /// gameroot-relative paths. Also migrates bare "user/" paths to "SPT/user/".
    /// </summary>
    private void MigrateOldFormatPaths(ref List<SyncPath> syncPaths, ref List<string> exclusions)
    {
        bool migrated = false;

        // Migrate sync paths
        for (int i = 0; i < syncPaths.Count; i++)
        {
            string path = syncPaths[i].Path;
            string? newPath = MigrateSinglePath(path);
            if (newPath == null) continue;

            syncPaths[i] = syncPaths[i] with { Path = newPath, Name = syncPaths[i].Name == path ? newPath : syncPaths[i].Name };
            migrated = true;
        }

        // Migrate exclusions
        for (int i = 0; i < exclusions.Count; i++)
        {
            string? newExclusion = MigrateSinglePath(exclusions[i]);
            if (newExclusion == null) continue;

            exclusions[i] = newExclusion;
            migrated = true;
        }

        if (migrated)
        {
            logger.LogWarning("NarcoNet config uses deprecated '../' path format. Paths have been automatically migrated to gameroot-relative format. Please update your config file to use the new format (e.g., 'BepInEx/plugins' instead of '../BepInEx/plugins', 'SPT/user/mods' instead of 'user/mods').");
        }
    }

    /// <summary>
    /// Migrate a single path from old format to new format.
    /// Returns null if no migration is needed.
    /// </summary>
    private static string? MigrateSinglePath(string path)
    {
        // Strip ../ or ..\ prefix
        if (path.StartsWith("../") || path.StartsWith(@"..\"))
        {
            return path.Substring(3);
        }

        // Bare "user/" paths need "SPT/" prefix (they were relative to server CWD which is SPT/)
        if (path.StartsWith("user/") || path.StartsWith(@"user\"))
        {
            return "SPT/" + path;
        }

        return null;
    }

    /// <summary>
    /// Insert the missing 'syncPaths:' key before the first list item in the YAML content.
    /// Handles broken configs generated between v1.0.13 and the fix.
    /// </summary>
    private static string InsertSyncPathsKey(string yamlContent)
    {
        var lines = yamlContent.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            // Stop at exclusions: — only insert before sync path items
            if (trimmed.StartsWith("exclusions:"))
                break;
            if (trimmed.StartsWith("- "))
            {
                lines.Insert(i, "syncPaths:");
                break;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// YAML config structure for deserialization
    /// </summary>
    [UsedImplicitly]
    private class YamlConfig
    {
        // ReSharper disable UnusedAutoPropertyAccessor.Local
        public List<object>? SyncPaths { get; set; }
        public List<string>? Exclusions { get; set; }
        // ReSharper restore UnusedAutoPropertyAccessor.Local
    }
}
