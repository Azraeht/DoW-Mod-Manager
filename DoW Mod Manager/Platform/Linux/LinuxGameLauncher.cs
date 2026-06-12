using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Linux;

/// <summary>
/// Launches Dawn of War on Linux through Steam Proton (preferred) or a plain Wine fallback.
/// The legacy Windows post-launch tweaks (CPU affinity, in-memory fog removal) are Windows-only
/// and become no-ops here; when requested but unsupported, that is noted in
/// <see cref="GameLaunchResult.Detail"/>.
/// </summary>
public sealed class LinuxGameLauncher : IGameLauncher
{
    // Optional user overrides.
    private const string ProtonOverrideEnv = "DOWMM_PROTON"; // full path to a `proton` script
    private const string WineOverrideEnv = "DOWMM_WINE";     // wine binary name or path

    public Task<GameLaunchResult> LaunchAsync(GameInstall game, LaunchOptions options, CancellationToken ct = default)
    {
        var arguments = options.ToArgumentString();
        var workingDir = Directory.Exists(game.InstallDir) ? game.InstallDir : Environment.CurrentDirectory;

        var unsupportedNotes = new List<string>();
        if (options.CpuAffinityOptimization)
            unsupportedNotes.Add("CPU affinity optimization is Windows-only and was skipped");
        if (options.NoFog)
            unsupportedNotes.Add("in-memory fog removal is Windows-only and was skipped");

        try
        {
            var protonPath = ResolveProton();
            ProcessStartInfo psi;
            string launchDescription;

            if (protonPath is not null)
            {
                psi = BuildProtonStartInfo(game, protonPath, arguments, workingDir, out launchDescription);
            }
            else
            {
                var winePath = ResolveWine();
                if (winePath is null)
                {
                    return Task.FromResult(new GameLaunchResult
                    {
                        Started = false,
                        Detail = "No Proton runtime found and no Wine binary available on PATH. "
                               + $"Set {ProtonOverrideEnv} to a proton script or {WineOverrideEnv} to a wine binary."
                    });
                }

                psi = BuildWineStartInfo(game, winePath, arguments, workingDir, out launchDescription);
            }

            var process = Process.Start(psi);
            if (process is null)
            {
                return Task.FromResult(new GameLaunchResult
                {
                    Started = false,
                    Detail = $"Process.Start returned null for: {launchDescription}"
                });
            }

            var detail = new StringBuilder(launchDescription);
            if (unsupportedNotes.Count > 0)
                detail.Append(" | ").Append(string.Join("; ", unsupportedNotes));

            return Task.FromResult(new GameLaunchResult
            {
                Started = true,
                ProcessId = process.Id,
                ProcessName = SafeProcessName(process),
                Detail = detail.ToString()
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GameLaunchResult
            {
                Started = false,
                Detail = $"Launch failed: {ex.GetType().Name}: {ex.Message}"
            });
        }
    }

    // ---- Proton -----------------------------------------------------------------------------

    private static ProcessStartInfo BuildProtonStartInfo(
        GameInstall game, string protonPath, string arguments, string workingDir, out string description)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var compatData = !string.IsNullOrWhiteSpace(game.ProtonPrefix)
            ? game.ProtonPrefix!
            : Path.Combine(home, ".steam", "steam", "steamapps", "compatdata",
                           (game.SteamAppId ?? 9450).ToString());

        var clientInstall = !string.IsNullOrWhiteSpace(game.SteamRoot)
            ? game.SteamRoot!
            : Path.Combine(home, ".steam", "steam");

        // `proton` is a Python script; invoking it directly works because it has a shebang and
        // is executable. We pass: proton run <game.exe> <args...>
        var psi = new ProcessStartInfo
        {
            FileName = protonPath,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(game.ExecutablePath);
        foreach (var arg in SplitArguments(arguments))
            psi.ArgumentList.Add(arg);

        psi.Environment["STEAM_COMPAT_DATA_PATH"] = compatData;
        psi.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = clientInstall;

        description = $"Proton: \"{protonPath}\" run \"{game.ExecutablePath}\" {arguments} "
                    + $"[STEAM_COMPAT_DATA_PATH={compatData}, STEAM_COMPAT_CLIENT_INSTALL_PATH={clientInstall}]";
        return psi;
    }

    /// <summary>
    /// Resolves the Proton script: honours the <c>DOWMM_PROTON</c> override first, then scans
    /// Steam's <c>compatibilitytools.d</c> and the bundled <c>common/Proton*</c> runtimes,
    /// preferring the newest by version.
    /// </summary>
    private static string? ResolveProton()
    {
        var overridePath = Environment.GetEnvironmentVariable(ProtonOverrideEnv);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var candidates = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
        };

        foreach (var steamRoot in steamRoots.Where(Directory.Exists))
        {
            // Custom / GE-Proton tools.
            CollectProtonScripts(Path.Combine(steamRoot, "compatibilitytools.d"), candidates);
            // Bundled Valve Proton runtimes.
            CollectProtonScripts(Path.Combine(steamRoot, "steamapps", "common"), candidates);
        }

        if (candidates.Count == 0)
            return null;

        // Prefer the newest: sort by a parsed version descending, then by name descending so
        // "GE-Proton9-x" / "Proton 9.0" win over older ones.
        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => ProtonVersionKey(p))
            .ThenByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>Finds <c>&lt;dir&gt;/Proton*/proton</c> scripts and adds them to <paramref name="into"/>.</summary>
    private static void CollectProtonScripts(string dir, List<string> into)
    {
        if (!Directory.Exists(dir))
            return;

        IEnumerable<string> subDirs;
        try { subDirs = Directory.EnumerateDirectories(dir, "*Proton*"); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var sub in subDirs)
        {
            var script = Path.Combine(sub, "proton");
            if (File.Exists(script))
                into.Add(script);
        }
    }

    /// <summary>Extracts a comparable numeric key from a Proton directory name (e.g. 9.07 -&gt; 9.07).</summary>
    private static double ProtonVersionKey(string protonScriptPath)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(protonScriptPath)) ?? "";
        var digits = new StringBuilder();
        var seenDot = false;
        foreach (var c in dirName)
        {
            if (char.IsDigit(c)) digits.Append(c);
            else if (c == '.' && !seenDot && digits.Length > 0) { digits.Append('.'); seenDot = true; }
            else if (digits.Length > 0 && c != '.') break;
        }

        return double.TryParse(digits.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0d;
    }

    // ---- Wine fallback ----------------------------------------------------------------------

    private static ProcessStartInfo BuildWineStartInfo(
        GameInstall game, string winePath, string arguments, string workingDir, out string description)
    {
        var psi = new ProcessStartInfo
        {
            FileName = winePath,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(game.ExecutablePath);
        foreach (var arg in SplitArguments(arguments))
            psi.ArgumentList.Add(arg);

        // Reuse the Proton prefix's pfx as a WINEPREFIX when available; this is where Proton
        // keeps the actual Wine prefix (compatdata/<appid>/pfx).
        string? winePrefix = null;
        if (!string.IsNullOrWhiteSpace(game.ProtonPrefix))
        {
            var pfx = Path.Combine(game.ProtonPrefix!, "pfx");
            winePrefix = Directory.Exists(pfx) ? pfx : game.ProtonPrefix;
        }

        if (winePrefix is not null)
            psi.Environment["WINEPREFIX"] = winePrefix;

        description = $"Wine: \"{winePath}\" \"{game.ExecutablePath}\" {arguments}"
                    + (winePrefix is not null ? $" [WINEPREFIX={winePrefix}]" : " [default WINEPREFIX]");
        return psi;
    }

    /// <summary>Resolves the wine binary: <c>DOWMM_WINE</c> override, else "wine" on PATH.</summary>
    private static string? ResolveWine()
    {
        var overrideWine = Environment.GetEnvironmentVariable(WineOverrideEnv);
        if (!string.IsNullOrWhiteSpace(overrideWine))
        {
            if (File.Exists(overrideWine))
                return overrideWine;
            var resolved = FindOnPath(overrideWine);
            if (resolved is not null)
                return resolved;
        }

        return FindOnPath("wine");
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Splits a command-line string into individual arguments, honouring double quotes. The
    /// DoW argument string is simple (no embedded quotes), so this lightweight splitter is enough.
    /// </summary>
    private static IEnumerable<string> SplitArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            yield break;

        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string SafeProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return "proton"; }
    }
}
