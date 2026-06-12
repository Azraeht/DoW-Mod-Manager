using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;
using DoW_Mod_Manager.Services;

namespace DoW_Mod_Manager.ViewModels;

/// <summary>
/// The main screen — a faithful MVVM port of the legacy <c>ModManagerForm</c> core: discover the
/// install(s), list playable mods, show required-mod status, and launch the game (directly on
/// Windows, via Proton/Wine on Linux) with the selected options.
/// </summary>
public partial class ModManagerViewModel : ViewModelBase
{
    private readonly IPlatformService _platform;
    private readonly IGameInstallLocator _locator;
    private readonly IModService _mods;
    private readonly IGameLauncher _launcher;
    private readonly IFileSystemService _files;
    private readonly IWarningsLogService _warnings;
    private readonly ILaaService _laa;
    private readonly ISettingsStore _settings;

    public ObservableCollection<GameInstall> Installs { get; } = [];
    public ObservableCollection<ModInfo> Mods { get; } = [];
    public ObservableCollection<ModDependencyStatus> RequiredMods { get; } = [];

    [ObservableProperty] private GameInstall? _selectedInstall;
    [ObservableProperty] private ModInfo? _selectedMod;

    [ObservableProperty] private string _manualPath = "";
    [ObservableProperty] private string _status = "Discovering Dawn of War installations…";
    [ObservableProperty] private string _laaStatus = "";

    // Launch options (persisted to DoW Mod Manager.ini).
    [ObservableProperty] private bool _dev;
    [ObservableProperty] private bool _noMovies = true;
    [ObservableProperty] private bool _forceHighPoly;
    [ObservableProperty] private bool _noPrecacheModels;

    [NotifyPropertyChangedFor(nameof(NoFogEnabled))]
    [ObservableProperty] private bool _noFog;

    [ObservableProperty] private bool _cpuAffinityOptimization;
    [ObservableProperty] private bool _isGogVersion;

    [NotifyCanExecuteChangedFor(nameof(StartModCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBaseGameCommand))]
    [ObservableProperty] private bool _busy;

    public string PlatformName => _platform.IsWindows ? "Windows" : "Linux / Proton";

    /// <summary>NoFog only works on Soulstorm and only with a Windows host (process-memory patch).</summary>
    public bool NoFogEnabled => SelectedInstall?.SupportsNoFog == true && _platform.IsWindows;

    public bool PerfTweaksSupported => _platform.IsWindows;

    public ModManagerViewModel(
        IPlatformService platform,
        IGameInstallLocator locator,
        IModService mods,
        IGameLauncher launcher,
        IFileSystemService files,
        IWarningsLogService warnings,
        ILaaService laa,
        ISettingsStore settings)
    {
        _platform = platform;
        _locator = locator;
        _mods = mods;
        _launcher = launcher;
        _files = files;
        _warnings = warnings;
        _laa = laa;
        _settings = settings;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var found = new List<GameInstall>(await _locator.DiscoverAsync());

            // Also honour the classic "manager sits in the game folder" mode.
            var here = _locator.IdentifyInstall(Directory.GetCurrentDirectory());
            if (here is not null && found.All(g => g.InstallDir != here.InstallDir))
                found.Insert(0, here);

            Installs.Clear();
            foreach (var g in found)
                Installs.Add(g);

            if (Installs.Count > 0)
            {
                SelectedInstall = Installs[0];
                Status = $"Found {Installs.Count} installation(s).";
            }
            else
            {
                Status = "No Dawn of War installation found. Enter the game folder path below and press Load.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Discovery failed: {ex.Message}";
        }
    }

    partial void OnSelectedInstallChanged(GameInstall? value)
    {
        OnPropertyChanged(nameof(NoFogEnabled));
        if (value is null)
            return;

        LoadSettings(value);
        LoadMods(value);
        RefreshLaaStatus(value);
    }

    partial void OnSelectedModChanged(ModInfo? value) => ResolveDependencies();

    private void LoadMods(GameInstall install)
    {
        Mods.Clear();
        RequiredMods.Clear();
        try
        {
            foreach (var mod in _mods.GetPlayableMods(install.InstallDir, install.IsOldGame))
                Mods.Add(mod);

            SelectedMod = Mods.FirstOrDefault();
            if (Mods.Count == 0)
                Status = "No playable mods found in this installation.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to read mods: {ex.Message}";
        }
    }

    private void ResolveDependencies()
    {
        RequiredMods.Clear();
        if (SelectedInstall is null || SelectedMod is null)
        {
            StartModCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            foreach (var dep in _mods.ResolveDependencies(SelectedInstall.InstallDir, SelectedMod))
                RequiredMods.Add(dep);
        }
        catch (Exception ex)
        {
            Status = $"Failed to resolve dependencies: {ex.Message}";
        }

        StartModCommand.NotifyCanExecuteChanged();
    }

    private void LoadSettings(GameInstall install)
    {
        var s = _settings.Read(install.InstallDir);
        Dev = s[SettingsKeys.Dev] == 1;
        NoMovies = s[SettingsKeys.NoMovies] == 1;
        ForceHighPoly = s[SettingsKeys.ForceHighPoly] == 1;
        NoPrecacheModels = s[SettingsKeys.NoPrecacheModels] == 1;
        NoFog = s[SettingsKeys.RemoveMapFog] == 1 && NoFogEnabled;
        CpuAffinityOptimization = s[SettingsKeys.DowOptimizations] == 1;
        IsGogVersion = s[SettingsKeys.IsGOGVersion] == 1;
    }

    private void SaveSettings(GameInstall install)
    {
        var s = _settings.Read(install.InstallDir);
        s[SettingsKeys.Dev] = Dev ? 1 : 0;
        s[SettingsKeys.NoMovies] = NoMovies ? 1 : 0;
        s[SettingsKeys.ForceHighPoly] = ForceHighPoly ? 1 : 0;
        s[SettingsKeys.NoPrecacheModels] = NoPrecacheModels ? 1 : 0;
        s[SettingsKeys.RemoveMapFog] = NoFog ? 1 : 0;
        s[SettingsKeys.DowOptimizations] = CpuAffinityOptimization ? 1 : 0;
        s[SettingsKeys.IsGOGVersion] = IsGogVersion ? 1 : 0;
        _settings.Write(install.InstallDir, s);
    }

    private LaunchOptions BuildOptions(string modName) => new()
    {
        ModName = modName,
        Dev = Dev,
        NoMovies = NoMovies,
        ForceHighPoly = ForceHighPoly,
        NoPrecacheModels = NoPrecacheModels,
        NoFog = NoFog && NoFogEnabled,
        CpuAffinityOptimization = CpuAffinityOptimization && PerfTweaksSupported,
    };

    private bool CanStartMod =>
        !Busy && SelectedInstall is not null && SelectedMod is not null &&
        RequiredMods.All(r => r.IsInstalled);

    private bool CanStartBaseGame => !Busy && SelectedInstall is not null;

    [RelayCommand(CanExecute = nameof(CanStartMod))]
    private Task StartModAsync() => LaunchAsync(SelectedMod!.Name);

    [RelayCommand(CanExecute = nameof(CanStartBaseGame))]
    private Task StartBaseGameAsync() => LaunchAsync("W40k");

    private async Task LaunchAsync(string modName)
    {
        if (SelectedInstall is null)
            return;

        Busy = true;
        try
        {
            SaveSettings(SelectedInstall);
            Status = $"Launching {modName}…";
            var result = await _launcher.LaunchAsync(SelectedInstall, BuildOptions(modName));
            Status = result.Started
                ? $"Started {modName} (pid {result.ProcessId}). {result.Detail}"
                : $"Launch failed: {result.Detail}";
        }
        catch (Exception ex)
        {
            Status = $"Launch error: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void LoadManualPath()
    {
        if (string.IsNullOrWhiteSpace(ManualPath))
            return;

        var install = _locator.IdentifyInstall(ManualPath.Trim());
        if (install is null)
        {
            Status = $"No Dawn of War executable found in \"{ManualPath}\".";
            return;
        }

        if (Installs.All(g => g.InstallDir != install.InstallDir))
            Installs.Add(install);
        SelectedInstall = install;
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (SelectedInstall is null)
            return;

        var folder = SelectedMod is { ModFolder.Length: > 0 }
            ? Path.Combine(SelectedInstall.InstallDir, SelectedMod.ModFolder)
            : SelectedInstall.InstallDir;

        if (Directory.Exists(folder))
            _files.OpenFolder(folder);
        else
            _files.OpenFolder(SelectedInstall.InstallDir);
    }

    [RelayCommand]
    private void CheckForErrors()
    {
        if (SelectedInstall is null)
            return;

        var errors = _warnings.GetFatalDataErrors(SelectedInstall.InstallDir);
        Status = errors.Count > 0
            ? $"{errors.Count} fatal data error(s):\n{string.Join('\n', errors)}"
            : "No fatal errors found in warnings.log.";
    }

    [RelayCommand]
    private void ToggleLaa()
    {
        if (SelectedInstall is null)
            return;

        try
        {
            bool now = _laa.ToggleLAA(SelectedInstall.ExecutablePath, IsGogVersion);
            LaaStatus = now ? "LAA: Enabled" : "LAA: Disabled";
            Status = $"Large-Address-Aware {(now ? "enabled" : "disabled")} for {SelectedInstall.ExecutableName}.";
        }
        catch (Exception ex)
        {
            Status = $"LAA toggle failed: {ex.Message}";
        }
    }

    private void RefreshLaaStatus(GameInstall install)
    {
        try
        {
            bool aware = _laa.IsLargeAddressAware(install.ExecutablePath, IsGogVersion);
            LaaStatus = aware ? "LAA: Enabled" : "LAA: Disabled";
        }
        catch
        {
            LaaStatus = "LAA: unknown";
        }
    }
}
