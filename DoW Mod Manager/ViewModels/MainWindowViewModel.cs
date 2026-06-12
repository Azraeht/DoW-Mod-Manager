using CommunityToolkit.Mvvm.ComponentModel;
using DoW_Mod_Manager.Abstractions;

namespace DoW_Mod_Manager.ViewModels;

/// <summary>
/// Shell ViewModel. WS5 replaces the placeholder content with the ported ModManager view and
/// navigation between the feature windows.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPlatformService _platform;

    [ObservableProperty]
    private string _greeting = "DoW Mod Manager — native port (WS0 foundation)";

    [ObservableProperty]
    private string _platformName;

    public MainWindowViewModel(IPlatformService platform)
    {
        _platform = platform;
        _platformName = platform.IsWindows ? "Windows" : "Linux / Unix";
    }
}
