using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Cross-platform port of the mod-enumeration and dependency-resolution logic from
    /// the legacy <c>ModManagerForm</c> (<c>GetMods()</c>, <c>LoadModFoldersFromFile()</c>,
    /// <c>CheckforInstalledMods()</c>).
    /// </summary>
    public sealed class ModService : IModService
    {
        private readonly IModuleParser _parser;

        public ModService(IModuleParser parser)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <inheritdoc />
        public IReadOnlyList<ModInfo> GetPlayableMods(string installDir, bool isOldGame)
        {
            if (string.IsNullOrEmpty(installDir))
                throw new ArgumentException("Install directory must be provided.", nameof(installDir));

            var result = new List<ModInfo>();

            if (!Directory.Exists(installDir))
                return result;

            string[] moduleFilePaths = Directory.GetFiles(installDir, "*.module");

            foreach (string filePath in moduleFilePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(filePath);

                // There is no point adding base modules (W40k, ..., DoWDE) to the list.
                if (BaseModules.Contains(fileName))
                    continue;

                ModInfo mod = _parser.Parse(filePath, isOldGame);

                // Filter the unplayable mods and populate the list only with playable mods.
                if (mod.IsPlayable)
                    result.Add(mod);
            }

            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<ModDependencyStatus> ResolveDependencies(string installDir, ModInfo mod)
        {
            if (string.IsNullOrEmpty(installDir))
                throw new ArgumentException("Install directory must be provided.", nameof(installDir));
            ArgumentNullException.ThrowIfNull(mod);

            var result = new List<ModDependencyStatus>(mod.RequiredMods.Count);

            foreach (string requiredModName in mod.RequiredMods)
            {
                // RequiredMod values may or may not carry the ".module" suffix; normalise it
                // so the file lookup matches the legacy "<name>.module" behaviour.
                string normalisedName = requiredModName.Replace(".module", "");
                // Mods are authored on Windows: "RequiredMod.1 = dxp2" must find "DXP2.module" on a
                // case-sensitive file system too.
                string moduleFilePath = GamePaths.Resolve(installDir, normalisedName + ".module");

                string modFolder;
                if (File.Exists(moduleFilePath))
                {
                    // Mirror LoadModFoldersFromFile: read ModFolder with ".module" stripped.
                    modFolder = ReadModFolder(moduleFilePath);
                }
                else
                {
                    modFolder = ModDependencyStatus.Missing;
                }

                // Mirror CheckforInstalledMods: the dependency is installed when the asset
                // folder exists on disk. A MISSING folder name resolves to "<dir>/MISSING"
                // which will not exist, so it is reported as not installed.
                string folderPath = GamePaths.Resolve(installDir, modFolder);
                bool isInstalled = Directory.Exists(folderPath);

                result.Add(new ModDependencyStatus
                {
                    RequiredModName = requiredModName,
                    ModFolder = modFolder,
                    IsInstalled = isInstalled
                });
            }

            return result;
        }

        /// <summary>
        /// Reads the last <c>ModFolder</c> attribute from a <c>.module</c> file (legacy
        /// <c>LoadModFoldersFromFile</c> kept the last match), with <c>.module</c> stripped.
        /// </summary>
        private static string ReadModFolder(string moduleFilePath)
        {
            string modFolder = "";

            foreach (string line in File.ReadLines(moduleFilePath))
            {
                if (line.Contains("ModFolder"))
                    modFolder = line.GetValueFromLine(deleteModule: true);
            }

            return modFolder;
        }
    }
}
