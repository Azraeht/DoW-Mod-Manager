using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoW_Mod_Manager.Models;
using DoW_Mod_Manager.Services;

namespace DoW_Mod_Manager.ViewModels;

/// <summary>
/// MVVM port of the legacy <c>ModDownloaderForm</c>: browse the curated mod list for the current game
/// and open the mod page or download links. Downloads always go through the system browser (the
/// legacy embedded Internet Explorer window is Windows-only).
/// </summary>
public partial class ModDownloaderViewModel : ViewModelBase
{
    public const string ModDbUrl = "https://www.moddb.com/games/dawn-of-war/mods?sort=rating-desc";

    private readonly IModlistService _modlist;
    private readonly IDialogService _dialogs;
    private IReadOnlyList<DownloadableMod> _all = [];

    public ObservableCollection<DownloadableMod> Mods { get; } = [];

    public string Header { get; }

    [NotifyCanExecuteChangedFor(nameof(OpenModPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [ObservableProperty] private DownloadableMod? _selectedMod;

    [ObservableProperty] private string _searchText;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _status = "Loading the mod list…";

    /// <param name="moduleFilter">
    /// Name of a missing required module (the "fix missing mod" flow): the list starts filtered on it.
    /// </param>
    public ModDownloaderViewModel(
        GameInstall install,
        string moduleFilter,
        IModlistService modlist,
        IDialogService dialogs)
    {
        _modlist = modlist;
        _dialogs = dialogs;
        _searchText = moduleFilter;

        Header = "Popular mods for " + install.Variant switch
        {
            GameVariant.Soulstorm => "Soulstorm:",
            GameVariant.DarkCrusade => "Dark Crusade:",
            GameVariant.WinterAssault => "Winter Assault:",
            _ => "Original:",
        };

        _ = LoadAsync(install.Variant);
    }

    private async Task LoadAsync(GameVariant variant)
    {
        try
        {
            _all = await _modlist.LoadAsync(variant);
            ApplyFilter();
            Status = $"{_all.Count} mods available. Mod list cached in {_modlist.ModlistPath}";
        }
        catch (Exception ex)
        {
            Status = $"Could not download the mod list: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Matches the display name or the module file name, case-insensitively.</summary>
    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        Mods.Clear();
        foreach (var mod in _all)
        {
            if (query.Length == 0 ||
                mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                mod.ModuleFileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Mods.Add(mod);
            }
        }

        SelectedMod = Mods.Count > 0 ? Mods[0] : null;
        if (!IsLoading && Mods.Count == 0 && query.Length > 0)
            Status = $"No mod matches \"{query}\". Try ModDB.";
    }

    [RelayCommand]
    private Task OpenModDbAsync() => _dialogs.OpenUrlAsync(ModDbUrl);

    private bool CanOpenPage => SelectedMod is { SiteLink.Length: > 0 };

    [RelayCommand(CanExecute = nameof(CanOpenPage))]
    private Task OpenModPageAsync() => _dialogs.OpenUrlAsync(SelectedMod!.SiteLink);

    private bool CanDownload => SelectedMod is { DownloadLink.Length: > 0 };

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var mod = SelectedMod!;
        await _dialogs.OpenUrlAsync(mod.DownloadLink);

        if (mod.PatchLink.Length > 0)
        {
            // Small delay so the browser does not drop the second download.
            await Task.Delay(250);
            await _dialogs.OpenUrlAsync(mod.PatchLink);
        }

        Status = mod.PatchLink.Length > 0
            ? $"Opened the download and patch links of {mod.Name} in your browser."
            : $"Opened the download link of {mod.Name} in your browser.";
    }

    [RelayCommand]
    private void Close() => RequestClose();
}
