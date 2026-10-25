using System;
using System.IO;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform;

/// <summary>
/// Identifies which Dawn of War variant lives in a directory, shared by the Windows and Linux
/// locators so both platforms agree. Mirrors the legacy <c>GetCurrentGameEXE()</c> priority
/// (Soulstorm &gt; Dark Crusade &gt; Winter Assault &gt; Original), with the Definitive Edition
/// checked first: it ships a W40k.exe that would otherwise be mistaken for the Original game.
/// </summary>
public static class GameVariantDetector
{
    public const string OriginalExe = "W40k.exe";
    public const string WinterAssaultExe = "W40kWA.exe";
    public const string DarkCrusadeExe = "DarkCrusade.exe";
    public const string SoulstormExe = "Soulstorm.exe";

    /// <summary>The Definitive Edition's own base module, absent from every classic install.</summary>
    public const string DefinitiveEditionModule = "DoWDE.module";

    /// <summary>Steam appid of Warhammer 40,000: Dawn of War - Definitive Edition.</summary>
    public const int DefinitiveEditionAppId = 3556750;

    /// <summary>
    /// Detects the variant from the files present. <paramref name="steamAppId"/> (from the Steam
    /// app manifest, when known) also identifies a Definitive Edition install.
    /// </summary>
    public static GameVariant? Detect(string directory, int? steamAppId = null)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        bool hasW40k = File.Exists(Path.Combine(directory, OriginalExe));

        if (hasW40k && IsDefinitiveEdition(directory, steamAppId))
            return GameVariant.DefinitiveEdition;

        if (File.Exists(Path.Combine(directory, SoulstormExe))) return GameVariant.Soulstorm;
        if (File.Exists(Path.Combine(directory, DarkCrusadeExe))) return GameVariant.DarkCrusade;
        if (File.Exists(Path.Combine(directory, WinterAssaultExe))) return GameVariant.WinterAssault;
        if (hasW40k) return GameVariant.Original;
        return null;
    }

    /// <summary>The executable launched for a variant.</summary>
    public static string ExecutableFor(GameVariant variant) => variant switch
    {
        GameVariant.Soulstorm => SoulstormExe,
        GameVariant.DarkCrusade => DarkCrusadeExe,
        GameVariant.WinterAssault => WinterAssaultExe,
        GameVariant.Original => OriginalExe,
        GameVariant.DefinitiveEdition => OriginalExe,
        _ => OriginalExe
    };

    private static bool IsDefinitiveEdition(string directory, int? steamAppId)
    {
        if (steamAppId == DefinitiveEditionAppId)
            return true;

        if (File.Exists(Path.Combine(directory, DefinitiveEditionModule)))
            return true;

        // The Steam build also ships steam_appid.txt next to the executable.
        try
        {
            string appIdFile = Path.Combine(directory, "steam_appid.txt");
            return File.Exists(appIdFile)
                && int.TryParse(File.ReadAllText(appIdFile).Trim(), out int id)
                && id == DefinitiveEditionAppId;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
