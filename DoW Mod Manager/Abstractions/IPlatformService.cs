using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Abstractions;

/// <summary>
/// Aggregate entry point for everything OS-specific. Exactly one implementation is selected
/// at start-up by the DI container: <c>WindowsPlatformService</c> (WS1) or
/// <c>LinuxPlatformService</c> (WS2). FROZEN in WS0 — do not change signatures without a
/// discussion on issue #2.
/// </summary>
public interface IPlatformService
{
    IGameLauncher Launcher { get; }
    IFileSystemService FileSystem { get; }
    ISystemPerformanceService Performance { get; }
    IGpuInfoService Gpu { get; }
    IGameInstallLocator InstallLocator { get; }
    IGraphicsModService GraphicsMods { get; }

    /// <summary>True on the Windows host. Lets ViewModels adapt without OS sniffing.</summary>
    bool IsWindows { get; }
}

/// <summary>Starts the game (directly on Windows, via Proton/Wine on Linux).</summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launches <paramref name="game"/> with <paramref name="options"/>. The implementation is
    /// responsible for any post-launch tweaks it supports (CPU affinity, fog removal on Windows).
    /// </summary>
    Task<GameLaunchResult> LaunchAsync(GameInstall game, LaunchOptions options, CancellationToken ct = default);
}

/// <summary>Folder/shortcut/file-properties operations that differ per OS.</summary>
public interface IFileSystemService
{
    /// <summary>Opens a folder in the system file manager (explorer.exe / xdg-open).</summary>
    void OpenFolder(string path);

    /// <summary>Shows the OS file-properties dialog (Windows). No-op / open-containing on Linux.</summary>
    void ShowFileProperties(string path);

    /// <summary>Creates a desktop shortcut (.lnk on Windows, .desktop on Linux).</summary>
    void CreateShortcut(ShortcutSpec spec);
}

/// <summary>Timer resolution, power plans and compatibility flags. All no-ops on Linux.</summary>
public interface ISystemPerformanceService
{
    PerfCapabilities GetCapabilities();

    TimerResolutionInfo? GetTimerResolution();
    void SetTimerResolution(double milliseconds);
    void ResetTimerResolution();

    /// <summary>Persists the AppCompatFlags for the game executable (Windows registry).</summary>
    void ApplyCompatibilityProfile(GameInstall game, PerfProfile profile);
    PerfProfile GetCompatibilityProfile(GameInstall game);

    string? GetActivePowerPlan();
    void SetPowerPlan(string planName);
    void UnlockUltimatePerformancePlan();
    void OpenPowerOptions();
}

/// <summary>Enumerates installed GPUs (WMI on Windows, /sys or lspci on Linux).</summary>
public interface IGpuInfoService
{
    Task<IReadOnlyList<GpuInfo>> GetGpusAsync(CancellationToken ct = default);
}

/// <summary>Discovers Dawn of War installations.</summary>
public interface IGameInstallLocator
{
    /// <summary>Auto-discovers installs (Steam/Proton prefixes on Linux, registry/known paths on Windows).</summary>
    Task<IReadOnlyList<GameInstall>> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Identifies which game (if any) lives in <paramref name="directory"/>. Used for the
    /// classic "manager sits in the game folder" mode.</summary>
    GameInstall? IdentifyInstall(string directory);
}

/// <summary>DXVK / graphics mod management. Largely a no-op under Proton (DXVK is built in).</summary>
public interface IGraphicsModService
{
    Task<bool> IsDxvkInstalledAsync(GameInstall game, CancellationToken ct = default);
    Task InstallDxvkAsync(GameInstall game, CancellationToken ct = default);
    Task RemoveDxvkAsync(GameInstall game, CancellationToken ct = default);

    /// <summary>True when DXVK is meaningful on this host (false under Proton, which bundles it).</summary>
    bool IsDxvkApplicable { get; }
}
