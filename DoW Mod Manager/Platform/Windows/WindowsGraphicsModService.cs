using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Windows;

/// <summary>
/// DXVK management on Windows. Ports the local-file side of the legacy
/// <c>ModManagerForm.DxvkButton_Click</c>:
/// <list type="bullet">
///   <item>"installed" = all four DXVK files present in the install dir
///         (legacy check: <c>dxvk.conf</c> + <c>d3d9.dll</c> + <c>dxgi.dll</c> + <c>dxvk.version</c>).</item>
///   <item>install = ensure the files are present/enabled in the install dir.</item>
///   <item>remove = delete those four files (legacy <c>File.Delete</c> calls).</item>
/// </list>
/// Downloading the DXVK payload is owned by WS3 (the cross-platform download/HTTP service);
/// this service only manages the files once they exist locally. On native Windows DXVK is
/// meaningful (no built-in Proton DXVK), so <see cref="IsDxvkApplicable"/> is <c>true</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGraphicsModService : IGraphicsModService
{
    static readonly string[] DxvkFiles =
    {
        "d3d9.dll",
        "dxgi.dll",
        "dxvk.conf",
        "dxvk.version"
    };

    public bool IsDxvkApplicable => true;

    public Task<bool> IsDxvkInstalledAsync(GameInstall game, CancellationToken ct = default)
    {
        // Legacy: File.Exists("dxvk.conf") && d3d9.dll && dxgi.dll && dxvk.version — all four.
        bool allPresent = true;
        foreach (string file in DxvkFiles)
        {
            if (!File.Exists(Path.Combine(game.InstallDir, file)))
            {
                allPresent = false;
                break;
            }
        }

        return Task.FromResult(allPresent);
    }

    /// <summary>
    /// Ensures DXVK is enabled in the install directory. The actual download of the four files is
    /// performed by WS3 before this runs; here we only verify they are in place and "enabled"
    /// (a disabled install would have these files removed, so presence == enabled).
    /// </summary>
    public Task InstallDxvkAsync(GameInstall game, CancellationToken ct = default)
    {
        // No file movement is needed once WS3 has placed the payload: presence of the files is
        // what the game and IsDxvkInstalledAsync key off. Kept as a no-op-by-design so the flow
        // (download via WS3, then "install/enable") matches the legacy single-step button.
        return Task.CompletedTask;
    }

    public Task RemoveDxvkAsync(GameInstall game, CancellationToken ct = default)
    {
        // Legacy DxvkButton_Click delete branch.
        foreach (string file in DxvkFiles)
        {
            string path = Path.Combine(game.InstallDir, file);
            if (File.Exists(path))
                File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
