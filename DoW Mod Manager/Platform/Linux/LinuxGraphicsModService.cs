using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Linux;

/// <summary>
/// DXVK management is not applicable on Linux: Proton already ships and manages DXVK inside its
/// prefix, so manually installing/removing DXVK DLLs (as the Windows build does) would only
/// fight Proton. Every operation is therefore a no-op and <see cref="IsDxvkApplicable"/> is false.
/// </summary>
public sealed class LinuxGraphicsModService : IGraphicsModService
{
    /// <summary>False: DXVK is bundled and managed by Proton on Linux.</summary>
    public bool IsDxvkApplicable => false;

    public Task<bool> IsDxvkInstalledAsync(GameInstall game, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task InstallDxvkAsync(GameInstall game, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveDxvkAsync(GameInstall game, CancellationToken ct = default)
        => Task.CompletedTask;
}
