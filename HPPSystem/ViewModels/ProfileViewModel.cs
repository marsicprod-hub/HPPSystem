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
    public ObservableCollection<ProfileCardViewModel> ProfileCards { get; } = new();

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
    public string ProfileCountText => $"{ProfileCards.Count} cabang";
    public string ActiveProfileNameText => SelectedProfile?.BusinessName ?? "Belum ada cabang aktif";
    public string ActiveProfileOwnerText => string.IsNullOrWhiteSpace(SelectedProfile?.OwnerName)
        ? "PIC belum diisi"
        : SelectedProfile!.OwnerName;
    public string ActiveProfileInsightText => SelectedProfile is null
        ? "Buat cabang pertama untuk mulai memisahkan data operasional."
        : $"Profil aktif menyimpan data operasional untuk {SelectedProfile.BusinessName}. Update identitas cabang di panel kanan.";
    public string ActiveProfileFootprintText => SelectedProfile is null
        ? "0 bahan | 0 resep | 0 bundle"
        : BuildDataFootprintText(SelectedProfile.Id);
    public string NewBranchGuideText => string.IsNullOrWhiteSpace(NewProfileName)
        ? "Tambahkan cabang baru saat bisnis perlu memisahkan stok, resep, dan transaksi."
        : $"Cabang baru akan dibuat dengan nama \"{NewProfileName.Trim()}\".";
    public bool CanSaveCurrent => SelectedProfile is not null && !string.IsNullOrWhiteSpace(BusinessName);
    public bool CanAddProfile => !string.IsNullOrWhiteSpace(NewProfileName);
    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : BuildDeletePromptText(PendingDelete);

    partial void OnSelectedProfileChanged(Profile? value)
    {
        BusinessName = value?.BusinessName ?? string.Empty;
        OwnerName = value?.OwnerName ?? string.Empty;
        OnPropertyChanged(nameof(ActiveProfileNameText));
        OnPropertyChanged(nameof(ActiveProfileOwnerText));
        OnPropertyChanged(nameof(ActiveProfileInsightText));
        OnPropertyChanged(nameof(ActiveProfileFootprintText));
        OnPropertyChanged(nameof(CanSaveCurrent));
    }

    partial void OnBusinessNameChanged(string value) => OnPropertyChanged(nameof(CanSaveCurrent));
    partial void OnNewProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(NewBranchGuideText));
        OnPropertyChanged(nameof(CanAddProfile));
    }

    public override void Refresh()
    {
        ProfileCards.Clear();
        Profiles.Clear();
        foreach (var profile in DataService.Profiles.OrderBy(x => x.BusinessName))
        {
            Profiles.Add(profile);
            ProfileCards.Add(new ProfileCardViewModel
            {
                Profile = profile,
                BusinessNameText = profile.BusinessName,
                OwnerNameText = string.IsNullOrWhiteSpace(profile.OwnerName) ? "PIC belum diisi" : profile.OwnerName,
                DataFootprintText = BuildDataFootprintText(profile.Id),
                ActivitySummaryText = BuildActivitySummaryText(profile.Id),
                IsActive = string.Equals(profile.Id, DataService.Settings.ActiveProfileId, StringComparison.Ordinal)
            });
        }

        SelectedProfile = Profiles.FirstOrDefault(x => x.Id == DataService.Settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(ProfileCountText));
        OnPropertyChanged(nameof(ActiveProfileNameText));
        OnPropertyChanged(nameof(ActiveProfileOwnerText));
        OnPropertyChanged(nameof(ActiveProfileInsightText));
        OnPropertyChanged(nameof(ActiveProfileFootprintText));
        OnPropertyChanged(nameof(NewBranchGuideText));
        OnPropertyChanged(nameof(CanSaveCurrent));
        OnPropertyChanged(nameof(CanAddProfile));
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
        OnPropertyChanged(nameof(DeletePromptText));
    }

    [RelayCommand]
    private void RequestDelete(Profile profile)
    {
        PendingDelete = profile;
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    private string BuildDeletePromptText(Profile profile)
    {
        var materialCount = DataService.Materials.Count(x => x.ProfileId == profile.Id);
        var recipeCount = DataService.Recipes.Count(x => x.ProfileId == profile.Id);
        var comboCount = DataService.Combos.Count(x => x.ProfileId == profile.Id);
        var saleCount = DataService.Sales.Count(x => x.ProfileId == profile.Id);
        var transactionCount = DataService.Transactions.Count(x => x.ProfileId == profile.Id);
        var movementCount = DataService.StockMovements.Count(x => x.ProfileId == profile.Id);

        return $"Hapus cabang {profile.BusinessName}? Dampak: {materialCount} bahan, {recipeCount} resep, {comboCount} bundle, {saleCount} penjualan, {transactionCount} transaksi kas, dan {movementCount} catatan ledger stok akan ikut hilang.";
    }

    private string BuildDataFootprintText(string profileId)
    {
        var materialCount = DataService.Materials.Count(x => x.ProfileId == profileId);
        var recipeCount = DataService.Recipes.Count(x => x.ProfileId == profileId);
        var comboCount = DataService.Combos.Count(x => x.ProfileId == profileId);
        return $"{materialCount} bahan | {recipeCount} resep | {comboCount} bundle";
    }

    private string BuildActivitySummaryText(string profileId)
    {
        var saleCount = DataService.Sales.Count(x => x.ProfileId == profileId);
        var transactionCount = DataService.Transactions.Count(x => x.ProfileId == profileId);
        return $"{saleCount} penjualan | {transactionCount} transaksi kas";
    }
}
