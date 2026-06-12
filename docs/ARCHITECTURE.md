# DoW Mod Manager — native port architecture (WS0)

This document records the **frozen contracts** for the .NET 8 + Avalonia port
(epic [#1](https://github.com/Azraeht/DoW-Mod-Manager/issues/1)). Once WS0 is merged,
the interfaces in `Abstractions/` and the models in `Models/` **do not change** without a
discussion on issue #2. Every other workstream codes against them.

## Project layout

Single multi-targeted project `DoW Mod Manager/DowModManager.csproj`:

- `net8.0` — built everywhere (Linux dev + CI).
- `net8.0-windows` — added automatically on Windows / CI; the **only** TFM that compiles
  `Platform/Windows/**` (P/Invoke, Registry, WMI). Guard Windows code with the `WINDOWS`
  compile constant or `[SupportedOSPlatform("windows")]`.

```
DoW Mod Manager/
  Abstractions/      IPlatformService + sub-interfaces        (WS0, frozen)
  Models/            GameInstall, LaunchOptions, ModInfo, ...  (WS0, frozen)
  Platform/
    Stubs/           safe no-op impl (default until WS7)
    Windows/         WS1  — compiled only on net8.0-windows
    Linux/           WS2  — Proton/Wine launcher, Steam discovery
  Services/          WS3  — cross-platform domain logic
  ViewModels/        WS4 (base + shell) + WS5 (feature VMs)
  Views/             WS4 (shell/shared) + WS5 (feature windows)
  Styles/ Assets/    WS4 — theme, icons
  Composition/       ServiceConfiguration — the single DI root
  Legacy/            original WinForms code, reference only (NOT compiled)
```

## Composition root

`Composition/ServiceConfiguration.AddDowModManager()` is the one place everything is wired:

- `AddPlatformServices()` — selects the OS `IPlatformService`. **WS7 seam**: replace the stub
  registration with `AddWindowsPlatform()` / `AddLinuxPlatform()` extension methods that WS1/WS2
  provide. Sub-services (`IGameLauncher`, `IFileSystemService`, …) are already re-exported from
  the aggregate so ViewModels can depend on them directly.
- `AddCoreServices()` — WS3 registers its parsers/services here.
- `AddViewModels()` — WS4/WS5 register feature ViewModels here.

## Frozen interfaces (`Abstractions/IPlatformService.cs`)

```csharp
IPlatformService {
    IGameLauncher Launcher; IFileSystemService FileSystem;
    ISystemPerformanceService Performance; IGpuInfoService Gpu;
    IGameInstallLocator InstallLocator; IGraphicsModService GraphicsMods;
    bool IsWindows;
}

IGameLauncher.LaunchAsync(GameInstall, LaunchOptions, CancellationToken) -> GameLaunchResult
    // implementation owns post-launch tweaks it supports (affinity/fog on Windows)

IFileSystemService { OpenFolder(path); ShowFileProperties(path); CreateShortcut(ShortcutSpec); }

ISystemPerformanceService {
    PerfCapabilities GetCapabilities();
    TimerResolutionInfo? GetTimerResolution(); SetTimerResolution(ms); ResetTimerResolution();
    ApplyCompatibilityProfile(GameInstall, PerfProfile); PerfProfile GetCompatibilityProfile(GameInstall);
    string? GetActivePowerPlan(); SetPowerPlan(name); UnlockUltimatePerformancePlan(); OpenPowerOptions();
}

IGpuInfoService.GetGpusAsync(ct) -> IReadOnlyList<GpuInfo>

IGameInstallLocator {
    DiscoverAsync(ct) -> IReadOnlyList<GameInstall>;   // Steam/Proton scan on Linux
    GameInstall? IdentifyInstall(string directory);    // "manager in game folder" mode
}

IGraphicsModService {
    bool IsDxvkApplicable;                              // false under Proton (built-in DXVK)
    IsDxvkInstalledAsync / InstallDxvkAsync / RemoveDxvkAsync (GameInstall, ct)
}
```

## Models (`Models/`)

- `GameInstall` — `InstallDir`, `ExecutableName`, `Variant` (Original/WinterAssault/DarkCrusade/
  Soulstorm), `Distribution` (Steam/GoG), `SteamAppId?`, `ProtonPrefix?`, `SteamRoot?`. Helpers:
  `ExecutablePath`, `IsOldGame`, `SupportsNoFog`.
- `LaunchOptions` — mod name + flags (`Dev`, `NoMovies`, `ForceHighPoly`, `NoPrecacheModels`,
  `NoFog`, `CpuAffinityOptimization`, `ExtraArguments`) + `ToArgumentString()` that reproduces the
  legacy `-modname X -dev -nomovies …` command line.
- `GameLaunchResult` — `ProcessId`, `ProcessName`, `Started`, `Detail`.
- `ModInfo` — parsed `.module` (`Name`, `ModuleFilePath`, `ModFolder?`, `Version?`, `Playable`,
  `RequiredMods`). `ModDependencyStatus` for resolution results.
- `PerfCapabilities`, `TimerResolutionInfo`, `PerfProfile`, `GpuInfo`, `ShortcutSpec`.

## MVVM

`ViewModelBase : CommunityToolkit.Mvvm.ComponentModel.ObservableObject`. Use
`[ObservableProperty]` / `[RelayCommand]`. ViewModels take fine-grained interface dependencies
via constructor injection and stay UI-framework-agnostic (no Avalonia types in VMs).

## Build / run

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build  "DoW Mod Manager/DowModManager.csproj"
dotnet run    --project "DoW Mod Manager/DowModManager.csproj" --framework net8.0
```
