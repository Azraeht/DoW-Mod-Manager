using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Enumerates installed mods in a game directory and resolves required-mod dependencies.
    /// Cross-platform; takes the directory as a parameter and uses <see cref="System.IO"/>.
    /// </summary>
    public interface IModService
    {
        /// <summary>
        /// Enumerates <c>*.module</c> files in <paramref name="installDir"/>, skips the base
        /// modules (<c>W40k</c>, <c>WXP</c>, <c>DXP2</c>) and returns the playable mods.
        /// Mirrors the legacy <c>GetMods()</c>.
        /// </summary>
        IReadOnlyList<ModInfo> GetPlayableMods(string installDir, bool isOldGame);

        /// <summary>
        /// Resolves the required-mod dependencies of <paramref name="mod"/> by checking, for
        /// each required mod, whether its declared <c>ModFolder</c> exists on disk.
        /// Mirrors the legacy <c>LoadModFoldersFromFile()</c> + <c>CheckforInstalledMods()</c>.
        /// </summary>
        IReadOnlyList<ModDependencyStatus> ResolveDependencies(string installDir, ModInfo mod);
    }
}
