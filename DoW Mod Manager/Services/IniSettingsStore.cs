namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Cross-platform port of the <c>DoW Mod Manager.ini</c> reader
    /// (<c>ReadSettingsFromDoWModManagerINI</c>) and writer (the <c>ModManagerForm_Closing</c>
    /// block). The settings file lives inside the supplied directory, exactly like the legacy
    /// in-directory file behaviour.
    /// </summary>
    public sealed class IniSettingsStore : ISettingsStore
    {
        public const string ConfigFileName = "DoW Mod Manager.ini";

        // Number of distinct Action enum states; ActionState must be below this (legacy rule).
        private const int ActionStateCount = 6;

        /// <inheritdoc />
        public IDictionary<string, int> GetDefaults()
        {
            // Legacy defaults from ModManagerForm's settings dictionary.
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [SettingsKeys.ActionState] = 1,      // (int)Action.CreateNativeImage
                [SettingsKeys.ChoiceIndex] = 0,
                [SettingsKeys.Dev] = 0,
                [SettingsKeys.NoMovies] = 1,
                [SettingsKeys.ForceHighPoly] = 0,
                [SettingsKeys.NoPrecacheModels] = 0,
                [SettingsKeys.RemoveMapFog] = 0,
                [SettingsKeys.DowOptimizations] = 0,
                [SettingsKeys.Autoupdate] = 1,
                [SettingsKeys.MultithreadedJIT] = 0,
                [SettingsKeys.AOTCompilation] = 1,
                [SettingsKeys.IsGOGVersion] = 0,
                [SettingsKeys.DXVKUpdateCheck] = 1,
                [SettingsKeys.DgVoodoo2UpdateCheck] = 1
            };
        }

        /// <inheritdoc />
        public IDictionary<string, int> Read(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Directory must be provided.", nameof(directory));

            IDictionary<string, int> settings = GetDefaults();

            string configPath = Path.Combine(directory, ConfigFileName);
            if (!File.Exists(configPath))
                return settings;

            // Read every line of config file and try to ignore or correct all common mistakes.
            string[] lines = File.ReadAllLines(configPath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Replace(" ", "");

                int firstIndexOfEqualSign = line.IndexOf('=');
                int lastIndexOfEqualSign = line.LastIndexOf('=');

                // There must be only one "=" in the line!
                if (firstIndexOfEqualSign != lastIndexOfEqualSign)
                    continue;

                if (firstIndexOfEqualSign <= 0)
                    continue;

                string setting = line.Substring(0, firstIndexOfEqualSign);

                int value;
                try
                {
                    value = Convert.ToInt32(line.Substring(firstIndexOfEqualSign + 1,
                        line.Length - firstIndexOfEqualSign - 1));
                }
                catch (Exception)
                {
                    value = 0;
                }

                ApplySetting(settings, setting, value);
            }

            return settings;
        }

        /// <summary>
        /// Applies one parsed setting to the dictionary using the legacy switch semantics:
        /// ActionState is only accepted when below the number of action states; all known
        /// keys clamp negative values to 0; unknown keys are ignored.
        /// </summary>
        private static void ApplySetting(IDictionary<string, int> settings, string setting, int value)
        {
            switch (setting)
            {
                case SettingsKeys.ActionState:
                    // Legacy: if value < 6 fall through to the common assignment, else ignore.
                    if (value >= ActionStateCount)
                        break;
                    goto case SettingsKeys.ChoiceIndex;

                case SettingsKeys.ChoiceIndex:
                case SettingsKeys.Dev:
                case SettingsKeys.NoMovies:
                case SettingsKeys.ForceHighPoly:
                case SettingsKeys.NoPrecacheModels:
                case SettingsKeys.DowOptimizations:
                case SettingsKeys.Autoupdate:
                case SettingsKeys.MultithreadedJIT:
                case SettingsKeys.AOTCompilation:
                case SettingsKeys.RemoveMapFog:
                case SettingsKeys.IsGOGVersion:
                case SettingsKeys.DXVKUpdateCheck:
                case SettingsKeys.DgVoodoo2UpdateCheck:
                    settings[setting] = value > 0 ? value : 0;
                    break;
            }
        }

        /// <inheritdoc />
        public void Write(string directory, IDictionary<string, int> settings)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Directory must be provided.", nameof(directory));
            ArgumentNullException.ThrowIfNull(settings);

            string configPath = Path.Combine(directory, ConfigFileName);

            // Legacy writer order. The final line had no trailing newline in the original;
            // we keep the same ordering and emit one key per line.
            string[] orderedKeys =
            {
                SettingsKeys.ActionState,
                SettingsKeys.ChoiceIndex,
                SettingsKeys.Dev,
                SettingsKeys.NoMovies,
                SettingsKeys.ForceHighPoly,
                SettingsKeys.NoPrecacheModels,
                SettingsKeys.DowOptimizations,
                SettingsKeys.Autoupdate,
                SettingsKeys.MultithreadedJIT,
                SettingsKeys.AOTCompilation,
                SettingsKeys.RemoveMapFog,
                SettingsKeys.IsGOGVersion,
                SettingsKeys.DXVKUpdateCheck,
                SettingsKeys.DgVoodoo2UpdateCheck
            };

            // CRLF like the legacy writer: the file is shared with the Windows build.
            using var sw = new StreamWriter(File.Create(configPath)) { NewLine = WindowsText.NewLine };
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                string key = orderedKeys[i];
                int value = settings.TryGetValue(key, out int v) ? v : 0;

                string entry = $"{key}={value}";

                // Mirror the legacy writer: every line written with WriteLine except the last,
                // which was written without a trailing newline.
                if (i < orderedKeys.Length - 1)
                    sw.WriteLine(entry);
                else
                    sw.Write(entry);
            }
        }
    }
}
