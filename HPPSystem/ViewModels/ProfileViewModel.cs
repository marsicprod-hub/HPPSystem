using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class ProfileViewModel : PageViewModelBase
{
    public ProfileViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Profil & Cabang";

    public ObservableCollection<Profile> Profiles { get; } = new();

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private string _businessName = string.Empty;

    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private Profile? _pendingDelete;

    public bool ShowDeletePrompt => PendingDelete is not null;

    partial void OnSelectedProfileChanged(Profile? value)
    {
        BusinessName = value?.BusinessName ?? string.Empty;
        OwnerName = value?.OwnerName ?? string.Empty;
    }

    public override void Refresh()
    {
        Profiles.Clear();
        foreach (var profile in DataService.Profiles.OrderBy(x => x.BusinessName))
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(x => x.Id == DataService.Settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SwitchAsync(Profile profile)
    {
        SelectedProfile = profile;
        await DataService.SetActiveProfileAsync(profile.Id);
    }

    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(BusinessName))
        {
            Error("Nama bisnis aktif wajib diisi.");
            return;
        }

        SelectedProfile.BusinessName = BusinessName.Trim();
        SelectedProfile.OwnerName = OwnerName.Trim();
        await DataService.SaveProfileAsync(SelectedProfile);
        Success("Profil cabang diperbarui.");
    }

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            Error("Nama cabang baru wajib diisi.");
            return;
        }

        var profile = new Profile
        {
            Id = $"prof_{Guid.NewGuid():N}",
            BusinessName = NewProfileName.Trim()
        };

        await DataService.SaveProfileAsync(profile);
        await DataService.SetActiveProfileAsync(profile.Id);
        NewProfileName = string.Empty;
        Success("Cabang baru dibuat.");
    }

    [RelayCommand]
    private async Task DeleteAsync(Profile profile)
    {
        if (Profiles.Count <= 1)
        {
            Error("Minimal harus ada satu cabang aktif.");
            return;
        }

        await DataService.DeleteProfileAsync(profile.Id);
        Success($"Cabang {profile.BusinessName} dihapus.");
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }

    [RelayCommand]
    private void RequestDelete(Profile profile)
    {
        PendingDelete = profile;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }
}
