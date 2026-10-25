using System.Text.RegularExpressions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Cross-platform port of the file handling in the legacy <c>ModMergerForm</c>. Fixes two legacy
    /// defects while keeping the file format: lines after the <c>RequiredMod</c> block are no longer
    /// dropped on save, and <c>RequiredMod</c> indices are always sequential.
    /// </summary>
    public sealed partial class ModMergerService : IModMergerService
    {
        private readonly IModService _mods;

        public ModMergerService(IModService mods)
        {
            _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        }

        // Same pattern as the legacy form: optional ";;" or "//" comment prefix, then the mod name.
        [GeneratedRegex(@"^\s*(;*/*)\s*RequiredMod\.\s*\d*\s*=\s*(\w*)", RegexOptions.IgnoreCase)]
        private static partial Regex RequiredModRegex();

        /// <inheritdoc />
        public IReadOnlyList<MergeableModule> GetMergeableModules(string installDir, bool isOldGame)
        {
            var playable = _mods.GetPlayableMods(installDir, isOldGame);
            var byName = playable.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            var result = new List<MergeableModule>();

            foreach (var mod in playable.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                string mergedPath = MergedPathFor(installDir, mod.Name);

                if (mod.Name.EndsWith(IModMergerService.MergePostfix, StringComparison.OrdinalIgnoreCase))
                {
                    // A merged copy is shown through its original; an orphan copy is its own merge.
                    string original = mod.Name[..^IModMergerService.MergePostfix.Length];
                    if (byName.ContainsKey(original))
                        continue;
                    mergedPath = mod.ModuleFilePath;
                }

                result.Add(new MergeableModule
                {
                    Name = mod.Name,
                    FilePath = mod.ModuleFilePath,
                    MergedFilePath = File.Exists(mergedPath) ? mergedPath : null,
                });
            }

            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetAllModuleNames(string installDir)
        {
            if (!Directory.Exists(installDir))
                return [];

            return Directory.GetFiles(installDir, "*.module")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(n => !IsBaseModule(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <inheritdoc />
        public MergeDefinition Load(string moduleFilePath, IReadOnlyCollection<string> installedModules)
        {
            var installed = new HashSet<string>(installedModules, StringComparer.OrdinalIgnoreCase);
            var entries = new List<MergeEntry>();
            var baseModules = new List<string>();

            foreach (string line in File.ReadLines(moduleFilePath))
            {
                Match match = RequiredModRegex().Match(line);
                if (!match.Success)
                    continue;

                string prefix = match.Groups[1].Value;
                string name = match.Groups[2].Value;
                if (name.Length == 0)
                    continue;

                if (IsBaseModule(name))
                {
                    if (!baseModules.Contains(name, StringComparer.OrdinalIgnoreCase))
                        baseModules.Add(name);
                    continue;
                }

                MergeModState state = !installed.Contains(name)
                    ? MergeModState.Missing
                    : prefix.StartsWith(";;") || prefix.StartsWith("//")
                        ? MergeModState.Inactive
                        : MergeModState.Active;

                entries.Add(new MergeEntry(name, state));
            }

            return new MergeDefinition
            {
                Entries = SortInactiveToBottom(entries),
                BaseModules = baseModules,
            };
        }

        /// <inheritdoc />
        public string Save(MergeableModule module, MergeDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(definition);

            string[] source = File.ReadAllLines(module.EffectiveFilePath);

            var requiredBlock = new List<string>();
            int index = 1;
            foreach (string baseModule in definition.BaseModules)
                requiredBlock.Add($"RequiredMod.{index++} = {baseModule}");

            foreach (MergeEntry entry in definition.Entries)
            {
                string disabler = entry.State == MergeModState.Active ? "" : "//";
                requiredBlock.Add($"{disabler}RequiredMod.{index++} = {entry.Name}");
            }

            // Replace the RequiredMod lines in place: the new block goes where the first one was.
            var output = new List<string>(source.Length + requiredBlock.Count);
            bool inserted = false;
            foreach (string line in source)
            {
                if (RequiredModRegex().IsMatch(line))
                {
                    if (!inserted)
                    {
                        output.AddRange(requiredBlock);
                        inserted = true;
                    }
                    continue;
                }
                output.Add(line);
            }
            if (!inserted)
                output.AddRange(requiredBlock);

            string target = module.MergedFilePath ?? MergedPathFor(Path.GetDirectoryName(module.FilePath)!, module.Name);
            // The game reads .module files with Windows line endings.
            WindowsText.WriteAllLines(target, output);
            return target;
        }

        /// <inheritdoc />
        public void DeleteMerge(MergeableModule module)
        {
            if (module.MergedFilePath is not null && File.Exists(module.MergedFilePath))
                File.Delete(module.MergedFilePath);
        }

        /// <summary>
        /// Keeps active entries first and moves inactive/missing ones to the bottom, preserving the
        /// relative order within each group (legacy <c>SortInactiveModsToBottom</c>).
        /// </summary>
        public static IReadOnlyList<MergeEntry> SortInactiveToBottom(IEnumerable<MergeEntry> entries)
        {
            var list = entries.ToList();
            return list.Where(e => e.State == MergeModState.Active)
                .Concat(list.Where(e => e.State != MergeModState.Active))
                .ToList();
        }

        private static bool IsBaseModule(string name) => BaseModules.Contains(name);

        private static string MergedPathFor(string installDir, string moduleName) =>
            Path.Combine(installDir, moduleName + IModMergerService.MergePostfix + ".module");
    }
}
