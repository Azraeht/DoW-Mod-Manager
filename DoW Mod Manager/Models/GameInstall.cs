using System.IO;

namespace DoW_Mod_Manager.Models;

/// <summary>The four supported Dawn of War standalone variants.</summary>
public enum GameVariant
{
    Original,        // W40k.exe
    WinterAssault,   // W40kWA.exe
    DarkCrusade,     // DarkCrusade.exe
    Soulstorm        // Soulstorm.exe
}

/// <summary>How the game was distributed — affects LAA patching and Proton appids.</summary>
public enum GameDistribution
{
    Unknown,
    Steam,
    GoG
}

/// <summary>
/// A discovered Dawn of War installation. Produced by <see cref="Abstractions.IGameInstallLocator"/>
/// and consumed by every platform service. Immutable.
/// </summary>
public sealed class GameInstall
{
    /// <summary>Directory that contains the game executable and the *.module files.</summary>
    public required string InstallDir { get; init; }

    /// <summary>Executable file name, e.g. "Soulstorm.exe".</summary>
    public required string ExecutableName { get; init; }

    public GameVariant Variant { get; init; }

    public GameDistribution Distribution { get; init; } = GameDistribution.Unknown;

    /// <summary>Steam appid, when discovered through a Steam/Proton prefix. Null otherwise.</summary>
    public int? SteamAppId { get; init; }

    /// <summary>
    /// On Linux: the Proton prefix directory (STEAM_COMPAT_DATA_PATH), i.e.
    /// <c>~/.steam/steam/steamapps/compatdata/&lt;appid&gt;</c>. Null on Windows / native.
    /// </summary>
    public string? ProtonPrefix { get; init; }

    /// <summary>Steam root install path (STEAM_COMPAT_CLIENT_INSTALL_PATH). Null on Windows.</summary>
    public string? SteamRoot { get; init; }

    public string ExecutablePath => Path.Combine(InstallDir, ExecutableName);

    /// <summary>Winter Assault and the Original game have no "Playable" flag in *.module files.</summary>
    public bool IsOldGame => Variant is GameVariant.Original or GameVariant.WinterAssault;

    /// <summary>Only Soulstorm supports the in-memory fog removal hack.</summary>
    public bool SupportsNoFog => Variant == GameVariant.Soulstorm;

    public override string ToString() => $"{Variant} ({Distribution}) @ {InstallDir}";
}
