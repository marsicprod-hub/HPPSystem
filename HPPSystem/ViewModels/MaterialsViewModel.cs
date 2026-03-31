using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class MaterialsViewModel : PageViewModelBase
{
    private const string SortNameAsc = "name_asc";
    private const string SortNameDesc = "name_desc";
    private const string SortUnitAsc = "unit_asc";
    private const string SortUnitDesc = "unit_desc";
    private const string SortPriceAsc = "price_asc";
    private const string SortPriceDesc = "price_desc";
    private const string SortPackAsc = "pack_asc";
    private const string SortPackDesc = "pack_desc";
    private const string SortUnitCostAsc = "unit_cost_asc";
    private const string SortUnitCostDesc = "unit_cost_desc";

    public MaterialsViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        FormUnit = "gram";
        BuildSortOptions();
        Refresh();
    }

    public override string Title => "Material";

    public ObservableCollection<HPPSystem.Models.Material> FilteredMaterials { get; } = new();
    public ObservableCollection<MaterialCardViewModel> MaterialCards { get; } = new();
    public ObservableCollection<PriceHistoryRowViewModel> HistoryPriceRecords { get; } = new();
    public ObservableCollection<SelectionOptionViewModel> SortOptions { get; } = new();

    [ObservableProperty]
    private bool _showForm;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _editingId = string.Empty;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formBrand = string.Empty;

    [ObservableProperty]
    private decimal _formPrice;

    [ObservableProperty]
    private decimal _formWeight;

    [ObservableProperty]
    private string _formUnit = "gram";

    [ObservableProperty]
    private HPPSystem.Models.Material? _pendingDelete;

    [ObservableProperty]
    private HPPSystem.Models.Material? _historyMaterial;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedSortOption;

    public bool HasMaterials => FilteredMaterials.Count > 0;
    public bool ShowDeletePrompt => PendingDelete is not null;
    public bool ShowHistory => HistoryMaterial is not null;
    public bool HasHistoryPriceRecords => HistoryPriceRecords.Count > 0;
    public bool HasSearchTerm => !string.IsNullOrWhiteSpace(SearchTerm);
    public string MaterialCountText { get; private set; } = "0 material";
    public string AverageUnitCostText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string SearchSummaryText { get; private set; } = "0 hasil";
    public string SortSummaryText => SelectedSortOption?.Label ?? "Nama A-Z";
    public string CatalogInsightText { get; private set; } = "Master material belum tersedia.";
    public string EstimatedUnitCostText => FormWeight <= 0 ? FormattingHelper.FormatCurrency(0) : FormattingHelper.FormatCurrency(FormPrice / FormWeight);
    public string SearchHelperText => string.IsNullOrWhiteSpace(SearchTerm)
        ? "Cari nama material atau merk produk untuk fokus ke item tertentu."
        : $"Filter aktif: \"{SearchTerm.Trim()}\"";
    public string FormTitleText => string.IsNullOrWhiteSpace(EditingId) ? "Tambah Material Baru" : "Edit Material";
    public string FormActionText => string.IsNullOrWhiteSpace(EditingId) ? "Simpan Material" : "Update Material";
    public string FormHelperText => string.IsNullOrWhiteSpace(EditingId)
        ? "Isi nama bahan, merk produk, harga per pack, berat bersih, dan satuan. Stok awal diatur terpisah dari halaman Gudang."
        : "Perbarui data master bahan dan merk produk. Stok fisik tidak diubah dari halaman ini.";
    public string DeletePromptText => PendingDelete is null ? string.Empty : BuildDeletePromptText(PendingDelete);
    public string HistoryTitleText => HistoryMaterial is null ? string.Empty : $"Riwayat Harga {BuildMaterialLabel(HistoryMaterial)}";
    public string HistorySummaryText => HistoryMaterial is null
        ? string.Empty
        : $"{BuildMaterialLabel(HistoryMaterial)} | harga aktif {FormattingHelper.FormatCurrency(HistoryMaterial.Price)} per pack | {FormattingHelper.FormatCurrency(HistoryMaterial.PricePerUnit)} per {HistoryMaterial.Unit}";
    public string HistoryPriceCountText => $"{HistoryPriceRecords.Count} perubahan harga";
    public string ImportGuideText => "Import Excel membaca nama bahan, merk produk, jumlah isi per pack, satuan pack, dan estimasi harga. Re-import akan update varian yang identitas nama + merk + netto + satuannya sama tanpa membuat duplikat.";

    partial void OnSearchTermChanged(string value) => Refresh();
    partial void OnSelectedSortOptionChanged(SelectionOptionViewModel? value)
    {
        OnPropertyChanged(nameof(SortSummaryText));
        Refresh();
    }
    partial void OnFormPriceChanged(decimal value) => OnPropertyChanged(nameof(EstimatedUnitCostText));
    partial void OnFormWeightChanged(decimal value) => OnPropertyChanged(nameof(EstimatedUnitCostText));

    partial void OnEditingIdChanged(string value)
    {
        OnPropertyChanged(nameof(FormTitleText));
        OnPropertyChanged(nameof(FormActionText));
        OnPropertyChanged(nameof(FormHelperText));
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
        OnPropertyChanged(nameof(HistorySummaryText));
        RefreshHistoryDetails();
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var allProfileItems = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .ToList();
        var sortedProfileItems = ApplySort(allProfileItems)
            .ToList();

        var items = sortedProfileItems
            .Where(x => MatchesSearch(x, SearchTerm))
            .ToList();

        FilteredMaterials.Clear();
        MaterialCards.Clear();
        foreach (var item in items)
        {
            FilteredMaterials.Add(item);
            MaterialCards.Add(BuildMaterialCard(item));
        }

        if (HistoryMaterial is not null)
        {
            HistoryMaterial = FilteredMaterials.FirstOrDefault(x => x.Id == HistoryMaterial.Id)
                ?? allProfileItems.FirstOrDefault(x => x.Id == HistoryMaterial.Id);
        }

        MaterialCountText = $"{sortedProfileItems.Count} material aktif";
        AverageUnitCostText = sortedProfileItems.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(sortedProfileItems.Average(x => x.PricePerUnit));
        SearchSummaryText = $"{items.Count} hasil ditampilkan";
        CatalogInsightText = sortedProfileItems.Count switch
        {
            0 => "Belum ada master material. Tambahkan bahan utama dulu, lalu atur stok fisiknya dari halaman Gudang.",
            _ => "Halaman ini khusus master material. Harga, berat per pack, dan modal per satuan dikelola di sini; stok fisik dikelola di Gudang."
        };

        OnPropertyChanged(nameof(HasMaterials));
        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(MaterialCountText));
        OnPropertyChanged(nameof(AverageUnitCostText));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(SortSummaryText));
        OnPropertyChanged(nameof(CatalogInsightText));
        OnPropertyChanged(nameof(SearchHelperText));
        OnPropertyChanged(nameof(HasHistoryPriceRecords));
        OnPropertyChanged(nameof(HistoryPriceCountText));
    }

    [RelayCommand]
    private void StartCreate()
    {
        ResetForm();
        ShowForm = true;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchTerm = string.Empty;
    }

    [RelayCommand]
    private void Edit(HPPSystem.Models.Material material)
    {
        EditingId = material.Id;
        FormName = material.Name;
        FormBrand = material.Brand;
        FormPrice = material.Price;
        FormWeight = material.Weight;
        FormUnit = material.Unit;
        ShowForm = true;
    }

    [RelayCommand]
    private void ShowHistoryFor(HPPSystem.Models.Material material)
    {
        HistoryMaterial = material;
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
        if (string.IsNullOrWhiteSpace(FormName) || FormPrice <= 0 || FormWeight <= 0 || string.IsNullOrWhiteSpace(FormUnit))
        {
            Error("Nama, harga, berat, dan satuan material wajib diisi.");
            return;
        }

        var existing = DataService.Materials.FirstOrDefault(x => x.Id == EditingId);
        var material = new HPPSystem.Models.Material
        {
            Id = string.IsNullOrWhiteSpace(EditingId) ? Guid.NewGuid().ToString("N") : EditingId,
            Name = FormName.Trim(),
            Brand = FormBrand.Trim(),
            Price = FormPrice,
            Weight = FormWeight,
            Unit = FormUnit.Trim(),
            Stock = existing?.Stock ?? 0,
            IsTrackedInWarehouse = existing?.IsTrackedInWarehouse ?? false,
            ProfileId = DataService.Settings.ActiveProfileId
        };

        await DataService.SaveMaterialAsync(material);
        Success(string.IsNullOrWhiteSpace(EditingId) ? "Material disimpan. Atur stok awalnya dari halaman Gudang." : "Perubahan material disimpan.");
        ShowForm = false;
        ResetForm();
    }

    [RelayCommand]
    private async Task DeleteAsync(HPPSystem.Models.Material material)
    {
        await DataService.DeleteMaterialAsync(material.Id);
        Success($"Material {material.Name} dihapus.");
        PendingDelete = null;
    }

    public MaterialImportPreview PreviewImport(string path)
        => MaterialExcelImportService.CreatePreview(path, DataService.Materials.ToList(), DataService.Settings.ActiveProfileId);

    public async Task ImportPreviewAsync(MaterialImportPreview preview)
    {
        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, DataService, DataService.Settings.ActiveProfileId);
        Success($"Import selesai: {result.ImportedCount} material diproses ({result.CreatedCount} baru, {result.UpdatedCount} update).");
    }

    private void BuildSortOptions()
    {
        SortOptions.Clear();
        SortOptions.Add(new SelectionOptionViewModel { Value = SortNameAsc, Label = "Nama A-Z" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortNameDesc, Label = "Nama Z-A" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitAsc, Label = "Satuan A-Z" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitDesc, Label = "Satuan Z-A" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPriceAsc, Label = "Harga Termurah-Termahal" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPriceDesc, Label = "Harga Termahal-Termurah" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPackAsc, Label = "Isi Pack Terkecil-Terbesar" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPackDesc, Label = "Isi Pack Terbesar-Terkecil" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitCostAsc, Label = "Modal Satuan Termurah-Termahal" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitCostDesc, Label = "Modal Satuan Termahal-Termurah" });
        SelectedSortOption = SortOptions.FirstOrDefault(x => x.Value == SortNameAsc) ?? SortOptions.FirstOrDefault();
    }

    private void ResetForm()
    {
        EditingId = string.Empty;
        FormName = string.Empty;
        FormBrand = string.Empty;
        FormPrice = 0;
        FormWeight = 0;
        FormUnit = "gram";
        OnPropertyChanged(nameof(EstimatedUnitCostText));
    }

    private IEnumerable<HPPSystem.Models.Material> ApplySort(IEnumerable<HPPSystem.Models.Material> materials)
    {
        return (SelectedSortOption?.Value ?? SortNameAsc) switch
        {
            SortNameDesc => materials.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitAsc => materials.OrderBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitDesc => materials.OrderByDescending(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPriceAsc => materials.OrderBy(x => x.Price).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPriceDesc => materials.OrderByDescending(x => x.Price).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPackAsc => materials.OrderBy(x => x.Weight).ThenBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPackDesc => materials.OrderByDescending(x => x.Weight).ThenBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitCostAsc => materials.OrderBy(x => x.PricePerUnit).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitCostDesc => materials.OrderByDescending(x => x.PricePerUnit).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            _ => materials.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshHistoryDetails()
    {
        HistoryPriceRecords.Clear();

        if (HistoryMaterial is null)
        {
            OnPropertyChanged(nameof(HasHistoryPriceRecords));
            OnPropertyChanged(nameof(HistoryPriceCountText));
            return;
        }

        foreach (var record in HistoryMaterial.PriceHistory
                     .OrderByDescending(x => DateTime.TryParse(x.Date, out var date) ? date : DateTime.MinValue))
        {
            var dateValue = DateTime.TryParse(record.Date, out var parsedDate) ? parsedDate : DateTime.MinValue;
            HistoryPriceRecords.Add(new PriceHistoryRowViewModel
            {
                DateValue = dateValue,
                PriceValue = record.Price,
                DateText = FormattingHelper.FormatDateTime(record.Date),
                PriceText = FormattingHelper.FormatCurrency(record.Price)
            });
        }

        OnPropertyChanged(nameof(HasHistoryPriceRecords));
        OnPropertyChanged(nameof(HistoryPriceCountText));
    }

    private string BuildDeletePromptText(HPPSystem.Models.Material material)
    {
        var recipeCount = DataService.Recipes
            .Where(x => x.ProfileId == material.ProfileId)
            .Count(x => x.IngredientGroups.Any(group => group.Ingredients.Any(ingredient => ingredient.MaterialId == material.Id)));
        var referenceCount = DataService.Recipes
            .Where(x => x.ProfileId == material.ProfileId)
            .Sum(x => x.IngredientGroups.Sum(group => group.Ingredients.Count(ingredient => ingredient.MaterialId == material.Id)));
        var movementCount = DataService.StockMovements.Count(x => x.MaterialId == material.Id);

        return $"Hapus material {material.Name}? Dampak: {recipeCount} resep, {referenceCount} referensi bahan, {movementCount} catatan stok, dan stok aktif {material.Stock:0.##} {material.Unit}.";
    }

    private static MaterialCardViewModel BuildMaterialCard(HPPSystem.Models.Material material)
    {
        return new MaterialCardViewModel
        {
            Material = material,
            NameText = material.Name,
            BrandText = string.IsNullOrWhiteSpace(material.Brand) ? "-" : material.Brand,
            UnitBadgeText = material.Unit,
            HistoryBadgeText = $"Riwayat Harga {material.PriceHistory.Count}",
            PackPriceValue = material.Price,
            PackPriceText = FormattingHelper.FormatCurrency(material.Price),
            PackQuantityValue = material.Weight,
            PackUnitText = material.Unit,
            WeightText = $"{material.Weight:0.##} {material.Unit}",
            UnitCostValue = material.PricePerUnit,
            UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit),
            PriceHistoryCountValue = material.PriceHistory.Count
        };
    }

    private static bool MatchesSearch(HPPSystem.Models.Material material, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        return material.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            || material.Brand.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMaterialLabel(HPPSystem.Models.Material material)
        => string.IsNullOrWhiteSpace(material.Brand)
            ? material.Name
            : $"{material.Name} | {material.Brand}";
}
