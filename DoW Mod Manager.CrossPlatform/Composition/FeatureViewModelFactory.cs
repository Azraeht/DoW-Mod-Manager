using System;
using DoW_Mod_Manager.Models;
using DoW_Mod_Manager.Services;
using DoW_Mod_Manager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DoW_Mod_Manager.Composition;

/// <summary><see cref="IFeatureViewModelFactory"/> backed by <see cref="ActivatorUtilities"/>.</summary>
public sealed class FeatureViewModelFactory(IServiceProvider services) : IFeatureViewModelFactory
{
    private T Create<T>(params object[] args) => ActivatorUtilities.CreateInstance<T>(services, args);

    public ModMergerViewModel CreateModMerger(GameInstall install, Action onModulesChanged) =>
        Create<ModMergerViewModel>(install, onModulesChanged);

    public ModDownloaderViewModel CreateModDownloader(GameInstall install, string moduleFilter = "") =>
        Create<ModDownloaderViewModel>(install, moduleFilter);

    public SettingsViewModel CreateSettings(GameInstall install, Action<LaunchFlagChange> applyLaunchFlags) =>
        Create<SettingsViewModel>(install, applyLaunchFlags);

    public SystemPerformanceViewModel CreateSystemPerformance(GameInstall install) =>
        Create<SystemPerformanceViewModel>(install);

    public FontsManagerViewModel CreateFontsManager(GameInstall install, string gameScreenWidth) =>
        Create<FontsManagerViewModel>(install, gameScreenWidth);

    public AboutViewModel CreateAbout() => Create<AboutViewModel>();
}
