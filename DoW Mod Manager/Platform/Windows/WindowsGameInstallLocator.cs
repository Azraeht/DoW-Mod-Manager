using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Windows;

/// <summary>
/// Identifies which Dawn of War variant lives in a directory. Mirrors the legacy
/// <c>ModManagerForm.GetCurrentGameEXE()</c> priority order:
/// Soulstorm &gt; Dark Crusade &gt; Winter Assault &gt; Original. The first executable found wins.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameInstallLocator : IGameInstallLocator
{
    // Executable file names, matching the legacy GameExecutable constants.
    const string ORIGINAL = "W40k.exe";
    const string WINTER_ASSAULT = "W40kWA.exe";
    const string DARK_CRUSADE = "DarkCrusade.exe";
    const string SOULSTORM = "Soulstorm.exe";

    public GameInstall? IdentifyInstall(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        // Priority order mirrors GetCurrentGameEXE(): a folder with both Original and Winter
        // Assault resolves to Winter Assault, and a full Soulstorm install resolves to Soulstorm.
        if (File.Exists(Path.Combine(directory, SOULSTORM)))
            return Build(directory, SOULSTORM, GameVariant.Soulstorm);

        if (File.Exists(Path.Combine(directory, DARK_CRUSADE)))
            return Build(directory, DARK_CRUSADE, GameVariant.DarkCrusade);

        if (File.Exists(Path.Combine(directory, WINTER_ASSAULT)))
            return Build(directory, WINTER_ASSAULT, GameVariant.WinterAssault);

        if (File.Exists(Path.Combine(directory, ORIGINAL)))
            return Build(directory, ORIGINAL, GameVariant.Original);

        return null;
    }

    /// <summary>
    /// Minimal discovery: the classic Windows usage is "manager sits in the game folder", so we
    /// probe the current working directory. A full Steam/registry scan can be added later; the
    /// frozen contract only requires we return whatever installs we can confidently identify.
    /// </summary>
    public Task<IReadOnlyList<GameInstall>> DiscoverAsync(CancellationToken ct = default)
    {
        var results = new List<GameInstall>();

        GameInstall? cwd = IdentifyInstall(Directory.GetCurrentDirectory());
        if (cwd != null)
            results.Add(cwd);

        return Task.FromResult<IReadOnlyList<GameInstall>>(results);
    }

    static GameInstall Build(string directory, string executable, GameVariant variant) => new()
    {
        InstallDir = directory,
        ExecutableName = executable,
        Variant = variant,
        // The Steam/GoG distinction is a user setting in the legacy app; default to Steam here.
        // (Linux discovery and future Windows registry probing can refine this.)
        Distribution = GameDistribution.Steam
    };
}
