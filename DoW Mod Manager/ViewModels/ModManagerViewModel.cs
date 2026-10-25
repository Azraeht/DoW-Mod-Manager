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
    private readonly IDialogService _dialogs;
    private readonly IFeatureViewModelFactory _features;
    private readonly IGameExtrasService _extras;
    private readonly IGraphicsModService _graphics;

    public ObservableCollection<GameInstall> Installs { get; } = [];
    public ObservableCollection<ModInfo> Mods { get; } = [];
    public ObservableCollection<ModDependencyStatus> RequiredMods { get; } = [];

    [ObservableProperty] private GameInstall? _selectedInstall;
    [ObservableProperty] private ModInfo? _selectedMod;

    [NotifyCanExecuteChangedFor(nameof(FixMissingModCommand))]
    [ObservableProperty] private ModDependencyStatus? _selectedRequiredMod;

    [ObservableProperty] private string _manualPath = "";
    [ObservableProperty] private string _status = "Discovering Dawn of War installations…";
    [ObservableProperty] private string _laaStatus = "";

    /// <summary>False for 64-bit (Definitive Edition) or unreadable executables — the toggle is disabled.</summary>
    [NotifyCanExecuteChangedFor(nameof(ToggleLaaCommand))]
    [ObservableProperty] private bool _laaApplicable;

    // Optional game tweaks (legacy camera / larger fonts / DXVK buttons).
    [NotifyPropertyChangedFor(nameof(CameraLabel))]
    [ObservableProperty] private bool _isCameraInstalled;

    [NotifyPropertyChangedFor(nameof(LargerFontsLabel))]
    [ObservableProperty] private bool _isLargerFontsInstalled;

    [NotifyPropertyChangedFor(nameof(DxvkLabel))]
    [ObservableProperty] private bool _isDxvkInstalled;

    public string CameraLabel => IsCameraInstalled ? "Remove better camera" : "Install better camera";
    public string LargerFontsLabel => IsLargerFontsInstalled ? "Remove larger fonts" : "Install larger fonts";
    public string DxvkLabel => IsDxvkInstalled ? "Remove DXVK" : "Install DXVK";

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

    /// <summary>DXVK is bundled with Proton on Linux, so the toggle only shows on Windows.</summary>
    public bool DxvkApplicable => _graphics.IsDxvkApplicable;

    public ModManagerViewModel(
        IPlatformService platform,
        IGameInstallLocator locator,
        IModService mods,
        IGameLauncher launcher,
        IFileSystemService files,
        IWarningsLogService warnings,
        ILaaService laa,
        ISettingsStore settings,
        IDialogService dialogs,
        IFeatureViewModelFactory features,
        IGameExtrasService extras,
        IGraphicsModService graphics)
    {
        _platform = platform;
        _locator = locator;
        _mods = mods;
        _launcher = launcher;
        _files = files;
        _warnings = warnings;
        _laa = laa;
        _settings = settings;
        _dialogs = dialogs;
        _features = features;
        _extras = extras;
        _graphics = graphics;

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
        _ = RefreshExtrasAsync(value);
    }

    partial void OnSelectedModChanged(ModInfo? value) => ResolveDependencies();

    private void LoadMods(GameInstall install, string? select = null)
    {
        Mods.Clear();
        RequiredMods.Clear();
        try
        {
            foreach (var mod in _mods.GetPlayableMods(install.InstallDir, install.IsOldGame))
                Mods.Add(mod);

            SelectedMod = Mods.FirstOrDefault(m => m.Name == select) ?? Mods.FirstOrDefault();
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
            if (result.Started)
            {
                // ProcessId is 0 when the game was started through Steam and its pid could not be confirmed.
                Status = result.ProcessId > 0
                    ? $"Started {modName} (pid {result.ProcessId}). {result.Detail}"
                    : $"Started {modName}. {result.Detail}";
            }
            else
            {
                Status = $"Launch failed: {result.Detail}";
                await _dialogs.ShowMessageAsync($"Could not launch {modName}.\n\n{result.Detail}", "Launch failed");
            }
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
        Status = $"Loaded {install.Variant} from {install.InstallDir}.";
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (SelectedInstall is null)
            return;

        var folder = SelectedMod is { ModFolder.Length: > 0 }
            ? GamePaths.Resolve(SelectedInstall.InstallDir, SelectedMod.ModFolder)
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

    [RelayCommand(CanExecute = nameof(LaaApplicable))]
    private void ToggleLaa()
    {
        if (SelectedInstall is null || !LaaApplicable)
            return;

        try
        {
            bool now = _laa.ToggleLAA(SelectedInstall.ExecutablePath, IsGogVersion);
            LaaStatus = now ? "LAA: Enabled" : "LAA: Disabled";
            Status = $"Large-Address-Aware {(now ? "enabled" : "disabled")} for {SelectedInstall.ExecutableName}.";
        }
        catch (LaaNotApplicableException ex)
        {
            Status = ex.Message;
            RefreshLaaStatus(SelectedInstall);
        }
        catch (Exception ex)
        {
            Status = $"LAA toggle failed: {ex.Message}";
        }
    }

    private void RefreshLaaStatus(GameInstall install)
    {
        var applicability = _laa.GetLaaApplicability(install.ExecutablePath);
        LaaApplicable = applicability == LaaApplicability.Applicable;

        switch (applicability)
        {
            case LaaApplicability.NotNeeded64Bit:
                LaaStatus = "LAA: not needed (64-bit)";
                return;
            case LaaApplicability.UnsupportedImage:
                LaaStatus = "LAA: n/a (not a 32-bit exe)";
                return;
            case LaaApplicability.FileUnavailable:
                LaaStatus = "LAA: unknown";
                return;
        }

        try
        {
            bool aware = _laa.IsLargeAddressAware(install.ExecutablePath, IsGogVersion);
            LaaStatus = aware ? "LAA: Enabled" : "LAA: Disabled";
        }
        catch
        {
            LaaApplicable = false;
            LaaStatus = "LAA: unknown";
        }
    }

    // ------------------------------------------------------------------ secondary windows

    /// <summary>Re-reads the mod list (e.g. after the merger created a <c>_MERGED</c> module).</summary>
    private void ReloadMods()
    {
        if (SelectedInstall is not null)
            LoadMods(SelectedInstall, SelectedMod?.Name);
    }

    /// <summary>Settings presets can toggle -forcehighpoly / -noprecachemodels (legacy ChangeSetting).</summary>
    private void ApplyLaunchFlags(LaunchFlagChange change)
    {
        if (change.ForceHighPoly is bool highPoly)
            ForceHighPoly = highPoly;
        if (change.NoPrecacheModels is bool noPrecache)
            NoPrecacheModels = noPrecache;
        if (SelectedInstall is not null)
            SaveSettings(SelectedInstall);
    }

    [RelayCommand]
    private void OpenModMerger()
    {
        if (SelectedInstall is not null)
            _dialogs.ShowWindow(_features.CreateModMerger(SelectedInstall, ReloadMods));
    }

    [RelayCommand]
    private void OpenModDownloader()
    {
        if (SelectedInstall is not null)
            _dialogs.ShowWindow(_features.CreateModDownloader(SelectedInstall));
    }

    private bool CanFixMissingMod => SelectedRequiredMod is { IsInstalled: false };

    [RelayCommand(CanExecute = nameof(CanFixMissingMod))]
    private async Task FixMissingModAsync()
    {
        if (SelectedInstall is null || SelectedRequiredMod is null)
            return;

        string name = SelectedRequiredMod.RequiredModName.Replace(".module", "").Trim();
        if (name is "DXP2" or "WXP" or "W40k")
        {
            await _dialogs.ShowMessageAsync("You are missing one of the base modules! Reinstall the game to fix it.", "Warning!");
            return;
        }

        _dialogs.ShowWindow(_features.CreateModDownloader(SelectedInstall, name));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (SelectedInstall is not null)
            _dialogs.ShowWindow(_features.CreateSettings(SelectedInstall, ApplyLaunchFlags));
    }

    [RelayCommand]
    private void OpenAbout() => _dialogs.ShowWindow(_features.CreateAbout());

    // ------------------------------------------------------------------ optional tweaks

    private async Task RefreshExtrasAsync(GameInstall install)
    {
        IsCameraInstalled = _extras.IsCameraInstalled(install.InstallDir);
        IsLargerFontsInstalled = _extras.IsLargerFontsInstalled(install.InstallDir);
        try
        {
            IsDxvkInstalled = DxvkApplicable && await _graphics.IsDxvkInstalledAsync(install);
        }
        catch
        {
            IsDxvkInstalled = false;
        }
    }

    [RelayCommand]
    private Task ToggleCameraAsync() => ToggleExtraAsync(
        IsCameraInstalled, "better camera",
        dir => _extras.InstallCameraAsync(dir),
        dir => { _extras.RemoveCamera(dir); return Task.CompletedTask; });

    [RelayCommand]
    private Task ToggleLargerFontsAsync() => ToggleExtraAsync(
        IsLargerFontsInstalled, "larger fonts",
        dir => _extras.InstallLargerFontsAsync(dir),
        dir => { _extras.RemoveLargerFonts(dir); return Task.CompletedTask; });

    [RelayCommand]
    private Task ToggleDxvkAsync() => ToggleExtraAsync(
        IsDxvkInstalled, "DXVK",
        _ => _graphics.InstallDxvkAsync(SelectedInstall!),
        _ => _graphics.RemoveDxvkAsync(SelectedInstall!));

    private async Task ToggleExtraAsync(bool installed, string name, Func<string, Task> install, Func<string, Task> remove)
    {
        if (SelectedInstall is null || Busy)
            return;

        var game = SelectedInstall;
        Busy = true;
        try
        {
            if (installed)
            {
                await remove(game.InstallDir);
                Status = $"The {name} tweak is disabled and deleted.";
            }
            else
            {
                Status = $"Downloading {name}…";
                await install(game.InstallDir);
                Status = $"The {name} tweak is downloaded and enabled.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Could not change {name}: {ex.Message}";
        }
        finally
        {
            Busy = false;
            await RefreshExtrasAsync(game);
        }
    }
}
