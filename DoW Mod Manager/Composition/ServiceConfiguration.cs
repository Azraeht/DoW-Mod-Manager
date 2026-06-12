using System;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Platform.Stubs;
using DoW_Mod_Manager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DoW_Mod_Manager.Composition;

/// <summary>
/// Single composition root for the whole application. WS1/WS2 add their platform registration
/// extension methods; WS3 adds the cross-platform services; WS5 adds the feature ViewModels.
/// </summary>
public static class ServiceConfiguration
{
    public static IServiceCollection AddDowModManager(this IServiceCollection services)
    {
        services.AddPlatformServices();
        services.AddCoreServices();
        services.AddViewModels();
        return services;
    }

    /// <summary>
    /// Selects the OS-specific <see cref="IPlatformService"/>. Replaced in WS7 with
    /// <c>AddWindowsPlatform()</c> / <c>AddLinuxPlatform()</c> once WS1/WS2 land; until then a
    /// safe stub keeps the shell runnable everywhere.
    /// </summary>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        // WS7 seam:
        //   if (OperatingSystem.IsWindows()) return services.AddWindowsPlatform();
        //   if (OperatingSystem.IsLinux())   return services.AddLinuxPlatform();
        services.AddSingleton<IPlatformService, StubPlatformService>();

        // Convenience: expose the sub-services directly so ViewModels can take fine-grained deps.
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().Launcher);
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().FileSystem);
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().Performance);
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().Gpu);
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().InstallLocator);
        services.AddSingleton(sp => sp.GetRequiredService<IPlatformService>().GraphicsMods);
        return services;
    }

    /// <summary>Cross-platform domain services (WS3 registers parsers, mod/download services here).</summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // WS3 fills this in (IModuleParser, IModService, IDownloadService, ISettingsStore, ...).
        return services;
    }

    /// <summary>MVVM ViewModels (WS4 shell + WS5 feature windows).</summary>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
