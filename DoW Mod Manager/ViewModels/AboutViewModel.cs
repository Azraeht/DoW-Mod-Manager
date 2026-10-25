using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DoW_Mod_Manager.Abstractions;

namespace DoW_Mod_Manager.ViewModels;

/// <summary>
/// Port of the legacy <c>AboutForm</c>. The .NET Framework JIT/NGEN options and the in-place .exe
/// self-update are gone (meaningless on .NET 8 / Linux); "check for updates" opens the releases page.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public const string HomePageUrl = "https://github.com/Azraeht/DoW-Mod-Manager";
    public const string ReleasesUrl = HomePageUrl + "/releases";
    public const string OriginalProjectUrl = "https://github.com/IgorTheLight/DoW-Mod-Manager";

    private readonly IDialogService _dialogs;

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "?";

    /// <summary>The upstream release this port is built on: the version without its "-linux.N" suffix.</summary>
    public string UpstreamBase => Version.Split('-')[0];

    public string Platform { get; }

    public AboutViewModel(IPlatformService platform, IDialogService dialogs)
    {
        _dialogs = dialogs;
        Platform = platform.IsWindows ? "Windows" : "Linux (game launched through Proton / Wine)";
    }

    [RelayCommand]
    private Task OpenUrlAsync(string url) => _dialogs.OpenUrlAsync(url);

    [RelayCommand]
    private void Close() => RequestClose();
}
