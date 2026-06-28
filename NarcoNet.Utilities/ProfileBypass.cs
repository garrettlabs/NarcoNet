namespace NarcoNet.Utilities;

/// <summary>
///     Determines whether a NarcoNet sync session should be bypassed for a configured profile.
/// </summary>
public static class ProfileBypass
{
    /// <summary>
    ///     Returns true when the active profile identifier is present in the configured ignored profile list.
    /// </summary>
    public static bool ShouldBypass(string? activeProfileId, IEnumerable<string>? ignoredProfiles)
    {
        string? normalizedActiveProfileId = NormalizeProfileIdentifier(activeProfileId);
        if (string.IsNullOrEmpty(normalizedActiveProfileId) || ignoredProfiles == null)
        {
            return false;
        }

        foreach (string ignoredProfile in ignoredProfiles)
        {
            string? normalizedIgnoredProfile = NormalizeProfileIdentifier(ignoredProfile);
            if (string.Equals(normalizedIgnoredProfile, normalizedActiveProfileId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Normalizes configured profile IDs, profile file names, or profile paths to the SPT profile file stem.
    /// </summary>
    public static string? NormalizeProfileIdentifier(string? profileIdentifier)
    {
        if (string.IsNullOrWhiteSpace(profileIdentifier))
        {
            return null;
        }

        string normalized = profileIdentifier!.Trim().Replace('\\', '/');
        int lastSlashIndex = normalized.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            normalized = normalized.Substring(lastSlashIndex + 1);
        }

        if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - ".json".Length);
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
