using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DoW_Mod_Manager.Platform.Linux;

/// <summary>
/// Steam client locations shared by install discovery and the Proton launcher, so both agree on
/// where Steam lives (native, Flatpak and Snap packagings).
/// </summary>
internal static partial class SteamPaths
{
    /// <summary>Candidate Steam client install roots (STEAM_COMPAT_CLIENT_INSTALL_PATH).</summary>
    public static IEnumerable<string> DefaultRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
        yield return Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");
    }

    /// <summary>The default roots that exist on disk, de-duplicated.</summary>
    public static IEnumerable<string> ExistingRoots()
        => DefaultRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the real Steam appid of a game from the <c>appmanifest_*.acf</c> whose
    /// <c>installdir</c> matches the game folder name. Null when no manifest matches.
    /// </summary>
    public static int? ReadAppId(string steamAppsDir, string gameDirectory)
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(gameDirectory));

        IEnumerable<string> manifests;
        try { manifests = Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        foreach (var manifest in manifests)
        {
            string text;
            try { text = File.ReadAllText(manifest); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            var installDir = InstallDirRegex().Match(text);
            if (!installDir.Success
                || !string.Equals(installDir.Groups[1].Value, folderName, StringComparison.OrdinalIgnoreCase))
                continue;

            var appId = AppIdRegex().Match(text);
            if (appId.Success && int.TryParse(appId.Groups[1].Value, out var id))
                return id;
        }

        return null;
    }

    [GeneratedRegex("\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\"appid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdRegex();
}
