namespace NarcoNet.Utilities;

/// <summary>
///     Solution-wide constants for NarcoNet
/// </summary>
public static class NarcoNetConstants
{
    // Branding
    public const string ProductName = "NarcoNet";
    public const string Author = "MadManBeavis";
    public const string PluginDisplayName = "MadManBeavis's NarcoNet";
    public const string PluginDirectoryName = "MadManBeavis-NarcoNet";

    // Plugin GUIDs
    public const string PluginGuid = "com.madmanbeavis.narconet";
    public const string ClientPluginGuid = PluginGuid + ".client";
    public const string ServerPluginGuid = PluginGuid + ".server";

    // Directory Names
    public const string DataDirectoryName = "NarcoNet_Data";
    public const string PendingUpdatesDirectoryName = "PendingUpdates";

    // URLs
    public static class Urls
    {
        public const string Repository = "https://github.com/MadManBeavis/NarcoNet";
        public const string Issues = "https://github.com/MadManBeavis/NarcoNet/issues";
        public const string Documentation = "https://github.com/MadManBeavis/NarcoNet/wiki";
    }
}
