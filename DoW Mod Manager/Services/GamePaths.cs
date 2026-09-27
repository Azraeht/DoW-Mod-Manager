namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Resolves paths inside a game install. The legacy code used Windows paths with inconsistent
    /// casing (<c>Engine\Locale\English\data\font</c> vs <c>...\Data</c>); on a case-sensitive Linux
    /// file system each segment is matched against what actually exists on disk.
    /// </summary>
    public static class GamePaths
    {
        /// <summary>
        /// Combines <paramref name="segments"/> onto <paramref name="root"/>, reusing the on-disk
        /// casing of every segment that already exists. Missing segments keep the given casing.
        /// </summary>
        public static string Resolve(string root, params string[] segments)
        {
            string current = root;
            bool exists = Directory.Exists(current);

            foreach (string segment in segments)
            {
                string? match = null;
                if (exists)
                {
                    match = Directory.EnumerateFileSystemEntries(current)
                        .Select(Path.GetFileName)
                        .FirstOrDefault(n => string.Equals(n, segment, StringComparison.OrdinalIgnoreCase));
                }

                current = Path.Combine(current, match ?? segment);
                exists = match is not null && Directory.Exists(current);
            }

            return current;
        }

        /// <summary><c>Local.ini</c>: the game's own video/audio/profile settings.</summary>
        public static string LocalIni(string installDir) => Resolve(installDir, "Local.ini");

        /// <summary><c>Drivers/spdx9_config.txt</c>: holds the <c>allowhwcursor</c> switch.</summary>
        public static string DriverConfig(string installDir) => Resolve(installDir, "Drivers", "spdx9_config.txt");

        /// <summary><c>Profiles/</c>: one <c>ProfileN</c> directory per player.</summary>
        public static string Profiles(string installDir) => Resolve(installDir, "Profiles");

        /// <summary><c>Engine/Data</c>: where the "better camera" LUA files go.</summary>
        public static string EngineData(string installDir) => Resolve(installDir, "Engine", "Data");

        /// <summary><c>Engine/Locale/English/Data</c>: root of the "larger fonts" mod.</summary>
        public static string EnglishLocaleData(string installDir) => Resolve(installDir, "Engine", "Locale", "English", "Data");

        /// <summary><c>Engine/Locale/English/Data/font</c>: the *.fnt files managed by the Fonts Manager.</summary>
        public static string Fonts(string installDir) => Resolve(installDir, "Engine", "Locale", "English", "Data", "font");

        /// <summary>Per-user data directory of the manager (modlist cache…), created on demand.</summary>
        public static string AppDataDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                "DowModManager");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
