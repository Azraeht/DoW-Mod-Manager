using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Linux;

/// <summary>
/// Discovers Dawn of War installations on Linux by scanning Steam library folders and the
/// Proton compatdata prefixes. Mirrors the legacy <c>GetCurrentGameEXE()</c> executable
/// priority (Soulstorm &gt; Dark Crusade &gt; Winter Assault &gt; Original).
/// </summary>
public sealed partial class LinuxGameInstallLocator : IGameInstallLocator
{
    // Executable file names, in the same priority order the legacy manager used.
    private const string SoulstormExe = "Soulstorm.exe";
    private const string DarkCrusadeExe = "DarkCrusade.exe";
    private const string WinterAssaultExe = "W40kWA.exe";
    private const string OriginalExe = "W40k.exe";

    // Steam appid <-> variant mapping. ASSUMPTION (documented in the WS2 spec):
    //   Soulstorm = 9450, Dark Crusade = 4570, Winter Assault = 4580, Original (DoW GOTY) = 4570.
    // Dark Crusade and the Original GOTY share the 4570 app family on Steam, so we resolve the
    // actual variant from the executables present on disk; the appid is only used to locate the
    // matching compatdata/<appid> Proton prefix.

    /// <summary>Maps a variant to its canonical Steam appid for the Proton prefix path.</summary>
    private static int AppIdForVariant(GameVariant variant) => variant switch
    {
        GameVariant.Soulstorm => 9450,
        GameVariant.DarkCrusade => 4570,
        GameVariant.WinterAssault => 4580,
        GameVariant.Original => 4570, // GOTY shares the 4570 app family
        _ => 4570
    };

    public Task<IReadOnlyList<GameInstall>> DiscoverAsync(CancellationToken ct = default)
    {
        var results = new List<GameInstall>();

        foreach (var steamRoot in EnumerateSteamRoots())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var library in EnumerateLibraryFolders(steamRoot))
            {
                var commonDir = Path.Combine(library, "steamapps", "common");
                if (!Directory.Exists(commonDir))
                    continue;

                IEnumerable<string> gameDirs;
                try
                {
                    // Steam installs DoW under directories like "Dawn of War Soulstorm".
                    gameDirs = Directory.EnumerateDirectories(commonDir, "Dawn of War*");
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var gameDir in gameDirs)
                {
                    ct.ThrowIfCancellationRequested();

                    var variant = DetectVariant(gameDir);
                    if (variant is null)
                        continue;

                    var appId = AppIdForVariant(variant.Value);
                    var compatData = Path.Combine(library, "steamapps", "compatdata", appId.ToString());

                    results.Add(new GameInstall
                    {
                        InstallDir = gameDir,
                        ExecutableName = ExecutableFor(variant.Value),
                        Variant = variant.Value,
                        Distribution = GameDistribution.Steam,
                        SteamAppId = appId,
                        // The prefix may not exist yet if the game was never launched; we still
                        // surface the canonical path so the launcher can create/use it.
                        ProtonPrefix = compatData,
                        SteamRoot = steamRoot
                    });
                }
            }
        }

        // De-duplicate by install directory (same library can be reached via several roots).
        IReadOnlyList<GameInstall> deduped = results
            .GroupBy(g => g.InstallDir, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return Task.FromResult(deduped);
    }

    public GameInstall? IdentifyInstall(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        var variant = DetectVariant(directory);
        if (variant is null)
            return null;

        var distribution = directory.Contains("steamapps", StringComparison.OrdinalIgnoreCase)
            ? GameDistribution.Steam
            : GameDistribution.Unknown;

        int? appId = null;
        string? protonPrefix = null;
        string? steamRoot = null;

        if (distribution == GameDistribution.Steam)
        {
            appId = AppIdForVariant(variant.Value);
            // .../steamapps/common/<game> -> .../steamapps/compatdata/<appid>
            var steamAppsDir = LocateSteamAppsDir(directory);
            if (steamAppsDir is not null)
            {
                protonPrefix = Path.Combine(steamAppsDir, "compatdata", appId.Value.ToString());
                // The Steam root is the parent of the steamapps dir's library; the client
                // install path is normally ~/.steam/steam, so fall back to that.
                steamRoot = DefaultSteamRoots().FirstOrDefault(Directory.Exists);
            }
        }

        return new GameInstall
        {
            InstallDir = directory,
            ExecutableName = ExecutableFor(variant.Value),
            Variant = variant.Value,
            Distribution = distribution,
            SteamAppId = appId,
            ProtonPrefix = protonPrefix,
            SteamRoot = steamRoot
        };
    }

    /// <summary>Detects the game variant from the executables present, in legacy priority order.</summary>
    private static GameVariant? DetectVariant(string directory)
    {
        if (File.Exists(Path.Combine(directory, SoulstormExe))) return GameVariant.Soulstorm;
        if (File.Exists(Path.Combine(directory, DarkCrusadeExe))) return GameVariant.DarkCrusade;
        if (File.Exists(Path.Combine(directory, WinterAssaultExe))) return GameVariant.WinterAssault;
        if (File.Exists(Path.Combine(directory, OriginalExe))) return GameVariant.Original;
        return null;
    }

    private static string ExecutableFor(GameVariant variant) => variant switch
    {
        GameVariant.Soulstorm => SoulstormExe,
        GameVariant.DarkCrusade => DarkCrusadeExe,
        GameVariant.WinterAssault => WinterAssaultExe,
        GameVariant.Original => OriginalExe,
        _ => OriginalExe
    };

    /// <summary>Walks up from a game directory to its <c>steamapps</c> folder, if any.</summary>
    private static string? LocateSteamAppsDir(string gameDirectory)
    {
        var dir = new DirectoryInfo(gameDirectory);
        while (dir is not null)
        {
            if (string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Candidate Steam client install roots (STEAM_COMPAT_CLIENT_INSTALL_PATH).</summary>
    private static IEnumerable<string> DefaultSteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
    }

    private static IEnumerable<string> EnumerateSteamRoots()
        => DefaultSteamRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <c>steamapps/libraryfolders.vdf</c> and returns every library root (the Steam
    /// root itself always counts). Uses a dependency-free regex scan of the VDF.
    /// </summary>
    private static IEnumerable<string> EnumerateLibraryFolders(string steamRoot)
    {
        var roots = new List<string> { steamRoot };

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { text = ""; }
            catch (UnauthorizedAccessException) { text = ""; }

            foreach (Match m in PathRegex().Matches(text))
            {
                // VDF escapes backslashes; on Linux paths use forward slashes, but unescape anyway.
                var path = m.Groups[1].Value.Replace("\\\\", "/").Replace("\\", "/");
                if (!string.IsNullOrWhiteSpace(path))
                    roots.Add(path);
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();
}
