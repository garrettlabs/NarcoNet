using System.Text.RegularExpressions;

using NarcoNet.Utilities;

namespace NarcoNet.Server.Models;

/// <summary>
///     Configuration for NarcoNet mod synchronization
/// </summary>
public record NarcoNetConfig
{
    public required List<SyncPath> SyncPaths { get; init; }
    public required List<string> Exclusions { get; init; }

    private List<Regex>? _compiledExclusions;
    public List<Regex> CompiledExclusions =>
        _compiledExclusions ??= Exclusions.Select(Glob.CreateNoEnd).ToList();
}
