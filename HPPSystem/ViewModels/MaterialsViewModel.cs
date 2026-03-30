using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class MaterialsViewModel : PageViewModelBase
{
    public MaterialsViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        FormUnit = "gram";
        Refresh();
    }

    public override string Title => "Katalog Bahan Baku";

    public ObservableCollection<HPPSystem.Models.Material> FilteredMaterials { get; } = new();

    [ObservableProperty]
    private bool _showForm;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _editingId = string.Empty;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private decimal _formPrice;

    [ObservableProperty]
    private decimal _formWeight;

    [ObservableProperty]
    private string _formUnit = "gram";

    [ObservableProperty]
    private decimal _formStock;

    [ObservableProperty]
    private HPPSystem.Models.Material? _pendingDelete;

    [ObservableProperty]
    private HPPSystem.Models.Material? _historyMaterial;

    public bool IsAdvancedMode => DataService.Settings.IsAdvancedMode;
    public string EstimatedUnitCostText => FormWeight <= 0 ? FormattingHelper.FormatCurrency(0) : FormattingHelper.FormatCurrency(FormPrice / FormWeight);
    public bool ShowDeletePrompt => PendingDelete is not null;
    public bool ShowHistory => HistoryMaterial is not null;
    public bool HasMaterials => FilteredMaterials.Count > 0;
    public string MaterialCountText { get; private set; } = "0 bahan";
    public string LowStockCountText { get; private set; } = "0 bahan kritis";
    public string TotalStockText { get; private set; } = "0 unit";
    public string AverageUnitCostText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string SearchSummaryText { get; private set; } = "0 hasil";
    public string InventoryInsightText { get; private set; } = "Inventaris bahan baku belum tersedia.";
    public string FormTitleText => string.IsNullOrWhiteSpace(EditingId) ? "Input Bahan Baku Baru" : "Edit Data Bahan Baku";
    public string FormActionText => string.IsNullOrWhiteSpace(EditingId) ? "Simpan Bahan" : "Update Bahan";
    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : $"Hapus bahan {PendingDelete.Name} dari sistem? Aksi ini dapat memengaruhi perhitungan resep yang memakainya.";
    public string HistoryTitleText => HistoryMaterial is null ? string.Empty : $"Audit Harga {HistoryMaterial.Name}";
    public string HistoryCurrentPriceText => HistoryMaterial is null
        ? FormattingHelper.FormatCurrency(0)
        : $"Harga aktif {FormattingHelper.FormatCurrency(HistoryMaterial.Price)}";

    partial void OnSearchTermChanged(string value) => Refresh();
    partial void OnFormPriceChanged(decimal value) => OnPropertyChanged(nameof(EstimatedUnitCostText));
    partial void OnFormWeightChanged(decimal value) => OnPropertyChanged(nameof(EstimatedUnitCostText));
    partial void OnEditingIdChanged(string value)
    {
        OnPropertyChanged(nameof(FormTitleText));
        OnPropertyChanged(nameof(FormActionText));
    }
    partial void OnPendingDeleteChanged(HPPSystem.Models.Material? value)
    {
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }
    partial void OnHistoryMaterialChanged(HPPSystem.Models.Material? value)
    {
        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(HistoryTitleText));
        OnPropertyChanged(nameof(HistoryCurrentPriceText));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var items = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .Where(x => string.IsNullOrWhiteSpace(SearchTerm) || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name)
            .ToList();

        FilteredMaterials.Clear();
        foreach (var item in items)
        {
            FilteredMaterials.Add(item);
        }

        var allProfileItems = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .ToList();

        MaterialCountText = $"{allProfileItems.Count} bahan aktif";
        LowStockCountText = $"{allProfileItems.Count(x => x.Stock <= 5)} bahan kritis";
        TotalStockText = $"{allProfileItems.Sum(x => x.Stock):0.##} unit tersimpan";
        AverageUnitCostText = allProfileItems.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(allProfileItems.Average(x => x.PricePerUnit));
        SearchSummaryText = $"{items.Count} hasil ditampilkan";
        InventoryInsightText = allProfileItems.Count switch
        {
            0 => "Inventaris bahan baku belum tersedia. Tambahkan material pertama untuk mulai membangun basis HPP.",
            _ when allProfileItems.Count(x => x.Stock <= 5) == 0 => "Stok bahan dalam kondisi aman. Fokuskan pembaruan pada harga pack dan audit riwayat belanja.",
            _ => $"Ada {allProfileItems.Count(x => x.Stock <= 5)} bahan dengan stok tipis. Pertimbangkan restock agar produksi tidak terganggu."
        };

        OnPropertyChanged(nameof(MaterialCountText));
        OnPropertyChanged(nameof(LowStockCountText));
        OnPropertyChanged(nameof(TotalStockText));
        OnPropertyChanged(nameof(AverageUnitCostText));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(InventoryInsightText));
        OnPropertyChanged(nameof(HasMaterials));
        OnPropertyChanged(nameof(IsAdvancedMode));
    }

    [RelayCommand]
    private void StartCreate()
    {
        ResetForm();
        ShowForm = true;
    }

    [RelayCommand]
    private void Edit(HPPSystem.Models.Material material)
    {
        EditingId = material.Id;
        FormName = material.Name;
        FormPrice = material.Price;
        FormWeight = material.Weight;
        FormUnit = material.Unit;
        FormStock = material.Stock;
        ShowForm = true;
    }

    [RelayCommand]
    private void ShowHistoryFor(HPPSystem.Models.Material material)
    {
        HistoryMaterial = material;
        OnPropertyChanged(nameof(ShowHistory));
    }

    [RelayCommand]
    private void Cancel()
    {
        ShowForm = false;
        ResetForm();
    }

    [RelayCommand]
    private void RequestDelete(HPPSystem.Models.Material material)
    {
        PendingDelete = material;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
    }

    [RelayCommand]
    private void CloseHistory()
    {
        HistoryMaterial = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName) || FormPrice <= 0 || FormWeight <= 0)
        {
            Error("Nama, harga, dan berat bahan wajib diisi.");
            return;
        }

        var material = new HPPSystem.Models.Material
        {
            Id = string.IsNullOrWhiteSpace(EditingId) ? Guid.NewGuid().ToString("N") : EditingId,
            Name = FormName.Trim(),
            Price = FormPrice,
            Weight = FormWeight,
            Unit = FormUnit,
            Stock = FormStock,
            ProfileId = DataService.Settings.ActiveProfileId
        };

        await DataService.SaveMaterialAsync(material);
        Success(string.IsNullOrWhiteSpace(EditingId) ? "Bahan baku disimpan." : "Perubahan bahan baku disimpan.");
        ShowForm = false;
        ResetForm();
    }

    [RelayCommand]
    private async Task DeleteAsync(HPPSystem.Models.Material material)
    {
        await DataService.DeleteMaterialAsync(material.Id);
        Success($"Bahan {material.Name} dihapus.");
        PendingDelete = null;
    }

    private void ResetForm()
    {
        EditingId = string.Empty;
        FormName = string.Empty;
        FormPrice = 0;
        FormWeight = 0;
        FormUnit = "gram";
        FormStock = 0;
        OnPropertyChanged(nameof(EstimatedUnitCostText));
    }
}
