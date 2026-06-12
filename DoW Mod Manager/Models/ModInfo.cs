using System.Collections.Generic;

namespace DoW_Mod_Manager.Models;

/// <summary>
/// Parsed representation of a Dawn of War <c>*.module</c> file. Produced by the cross-platform
/// module parser (WS3). Immutable.
/// </summary>
public sealed class ModInfo
{
    /// <summary>Module file name without extension (the value passed to <c>-modname</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path of the <c>.module</c> file.</summary>
    public required string ModuleFilePath { get; init; }

    /// <summary>Value of the <c>ModFolder</c> attribute — the asset folder backing the mod.</summary>
    public string? ModFolder { get; init; }

    /// <summary>Value of the <c>ModVersion</c> attribute, if present.</summary>
    public string? Version { get; init; }

    /// <summary>True when <c>Playable = 1</c> (always true for the old games).</summary>
    public bool Playable { get; init; }

    /// <summary>Names referenced by <c>RequiredMod.N =</c> lines, in declared order.</summary>
    public IReadOnlyList<string> RequiredMods { get; init; } = [];

    public override string ToString() =>
        Version is { Length: > 0 } ? $"{Name}   (Version {Version})" : Name;
}

/// <summary>A required dependency resolved against the install directory.</summary>
public sealed class ModDependencyStatus
{
    public required string ModName { get; init; }
    public string? ModFolder { get; init; }
    public bool Installed { get; init; }
}
