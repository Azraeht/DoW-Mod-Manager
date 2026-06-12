using CommunityToolkit.Mvvm.ComponentModel;

namespace DoW_Mod_Manager.ViewModels;

/// <summary>
/// Base class for every ViewModel. Built on CommunityToolkit.Mvvm
/// (<see cref="ObservableObject"/>) — use <c>[ObservableProperty]</c> and
/// <c>[RelayCommand]</c> in derived types. Chosen over ReactiveUI in WS4 for its
/// source-generator ergonomics and zero ceremony.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
}
