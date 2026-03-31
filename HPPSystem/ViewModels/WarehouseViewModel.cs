using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class WarehouseViewModel : PageViewModelBase
{
    private const string AllLedgerSourcesValue = "__all_sources";
    private const string AllLedgerMaterialsValue = "__all_materials";
    private const string FocusAllValue = "all";
    private const string FocusCriticalValue = "critical";
    private const string FocusOutValue = "out";
    private const string SortNameAsc = "name_asc";
    private const string SortNameDesc = "name_desc";
    private const string SortUnitAsc = "unit_asc";
    private const string SortUnitDesc = "unit_desc";
    private const string SortStockAsc = "stock_asc";
    private const string SortStockDesc = "stock_desc";
    private const string SortPriceAsc = "price_asc";
    private const string SortPriceDesc = "price_desc";
    private const string SortPackAsc = "pack_asc";
    private const string SortPackDesc = "pack_desc";

    private string _quickRestockMaterialName = string.Empty;

    public WarehouseViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        BuildSortOptions();
        Refresh();
    }

    public override string Title => "Gudang & Stok";

    public ObservableCollection<HPPSystem.Models.Material> FilteredMaterials { get; } = new();
    public ObservableCollection<MaterialCardViewModel> MaterialCards { get; } = new();
    public ObservableCollection<PriceHistoryRowViewModel> HistoryPriceRecords { get; } = new();
    public ObservableCollection<StockMovementRowViewModel> HistoryStockMovements { get; } = new();
    public ObservableCollection<StockMovementRowViewModel> LedgerStockMovements { get; } = new();
    public ObservableCollection<SelectionOptionViewModel> LedgerSourceOptions { get; } = new();
    public ObservableCollection<SelectionOptionViewModel> LedgerMaterialOptions { get; } = new();
    public ObservableCollection<SelectionOptionViewModel> CatalogMaterialOptions { get; } = new();
    public ObservableCollection<SelectionOptionViewModel> SortOptions { get; } = new();

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _catalogSearchTerm = string.Empty;

    [ObservableProperty]
    private string _materialFocusMode = FocusAllValue;

    [ObservableProperty]
    private HPPSystem.Models.Material? _historyMaterial;

    [ObservableProperty]
    private HPPSystem.Models.Material? _pendingWarehouseRemoval;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedLedgerSourceOption;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedLedgerMaterialOption;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedCatalogMaterialOption;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedSortOption;

    [ObservableProperty]
    private bool _showAdjustForm;

    [ObservableProperty]
    private string _adjustingMaterialId = string.Empty;

    [ObservableProperty]
    private string _adjustingMaterialName = string.Empty;

    [ObservableProperty]
    private string _adjustingUnit = string.Empty;

    [ObservableProperty]
    private decimal _currentStock;

    [ObservableProperty]
    private decimal _newStock;

    [ObservableProperty]
    private string _adjustmentNotes = string.Empty;

    public bool HasMaterials => FilteredMaterials.Count > 0;
    public bool ShowHistory => HistoryMaterial is not null;
    public bool ShowDeletePrompt => PendingWarehouseRemoval is not null;
    public bool HasLedgerStockMovements => LedgerStockMovements.Count > 0;
    public bool HasHistoryPriceRecords => HistoryPriceRecords.Count > 0;
    public bool HasHistoryStockMovements => HistoryStockMovements.Count > 0;
    public bool HasQuickRestockTarget => !string.IsNullOrWhiteSpace(_quickRestockMaterialName);
    public bool HasActiveMaterialFocus => !IsFocusAll;
    public bool HasSearchTerm => !string.IsNullOrWhiteSpace(SearchTerm);
    public bool HasActiveSearchOrFocus => HasSearchTerm || HasActiveMaterialFocus;
    public bool HasCatalogSearchTerm => !string.IsNullOrWhiteSpace(CatalogSearchTerm);
    public bool HasCatalogMaterialOptions => CatalogMaterialOptions.Count > 0;
    public bool IsFocusAll => string.Equals(MaterialFocusMode, FocusAllValue, StringComparison.Ordinal);
    public bool IsFocusCritical => string.Equals(MaterialFocusMode, FocusCriticalValue, StringComparison.Ordinal);
    public bool IsFocusOut => string.Equals(MaterialFocusMode, FocusOutValue, StringComparison.Ordinal);
    public bool CanSaveAdjustment => !string.IsNullOrWhiteSpace(AdjustingMaterialId) && NewStock != CurrentStock;
    public bool CanAddCatalogMaterial => SelectedCatalogMaterialOption is not null;

    public string MaterialCountText { get; private set; } = "0 bahan";
    public string LowStockCountText { get; private set; } = "0 bahan kritis";
    public string TotalStockText { get; private set; } = "0 unit";
    public string SearchSummaryText { get; private set; } = "0 hasil";
    public string SortSummaryText => SelectedSortOption?.Label ?? "Nama A-Z";
    public string FocusAllText { get; private set; } = "Semua 0";
    public string FocusCriticalText { get; private set; } = "Stok Tipis 0";
    public string FocusOutText { get; private set; } = "Kosong 0";
    public string MaterialFocusSummaryText { get; private set; } = "Menampilkan semua bahan aktif.";
    public string InventoryInsightText { get; private set; } = "Gudang belum memiliki data stok aktif.";
    public string RestockFocusText { get; private set; } = "Belum ada prioritas restock.";
    public string QuickRestockActionText { get; private set; } = "Belum ada stok kritis.";
    public string CatalogPickerSummaryText { get; private set; } = "Belum ada material katalog yang siap dimasukkan ke gudang.";
    public string CatalogPickerActionText => SelectedCatalogMaterialOption is null
        ? "Pilih material katalog dulu."
        : $"Masukkan {SelectedCatalogMaterialOption.Label} ke daftar stok gudang.";
    public string SearchHelperText => string.IsNullOrWhiteSpace(SearchTerm)
        ? "Cari nama bahan atau merk produk untuk fokus ke stok tertentu."
        : $"Filter aktif: \"{SearchTerm.Trim()}\"";
    public string CatalogSearchHelperText => string.IsNullOrWhiteSpace(CatalogSearchTerm)
        ? "Cari nama bahan atau merk untuk mempersempit daftar katalog."
        : $"Filter katalog aktif: \"{CatalogSearchTerm.Trim()}\"";

    public string HistoryTitleText => HistoryMaterial is null ? string.Empty : $"Audit Gudang {BuildMaterialLabel(HistoryMaterial)}";
    public string HistorySummaryText => HistoryMaterial is null
        ? string.Empty
        : $"{BuildMaterialLabel(HistoryMaterial)} | stok aktif {HistoryMaterial.Stock:0.##} {HistoryMaterial.Unit} | harga pack {FormattingHelper.FormatCurrency(HistoryMaterial.Price)}";
    public string HistoryPriceCountText => $"{HistoryPriceRecords.Count} perubahan harga";
    public string HistoryMovementCountText => $"{HistoryStockMovements.Count} pergerakan stok";

    public string LedgerMovementCountText => $"{LedgerStockMovements.Count} pergerakan ditampilkan";
    public string LedgerInboundCountText { get; private set; } = "0 masuk";
    public string LedgerOutboundCountText { get; private set; } = "0 keluar";
    public string LedgerFilterSummaryText { get; private set; } = "Semua sumber, semua bahan";
    public string LedgerHeadlineText { get; private set; } = "Belum ada aktivitas stok untuk profil aktif.";

    public string AdjustFormTitleText => string.IsNullOrWhiteSpace(AdjustingMaterialId)
        ? "Penyesuaian Stok"
        : $"Atur Stok: {AdjustingMaterialName}";
    public string AdjustFormHelperText => string.IsNullOrWhiteSpace(AdjustingMaterialId)
        ? "Pilih salah satu bahan untuk mengubah stok fisik gudang."
        : $"Set stok fisik terbaru untuk {AdjustingMaterialName}. Harga dan berat pack tetap dikelola di halaman Material.";
    public string DeletePromptText => PendingWarehouseRemoval is null ? string.Empty : BuildWarehouseDeletePromptText(PendingWarehouseRemoval);
    public string AdjustmentDeltaText
    {
        get
        {
            var delta = NewStock - CurrentStock;
            var prefix = delta > 0 ? "+" : string.Empty;
            return $"{prefix}{delta:0.##} {AdjustingUnit}".Trim();
        }
    }

    partial void OnSearchTermChanged(string value) => Refresh();
    partial void OnCatalogSearchTermChanged(string value) => Refresh();
    partial void OnMaterialFocusModeChanged(string value) => Refresh();
    partial void OnSelectedSortOptionChanged(SelectionOptionViewModel? value)
    {
        OnPropertyChanged(nameof(SortSummaryText));
        Refresh();
    }
    partial void OnHistoryMaterialChanged(HPPSystem.Models.Material? value)
    {
        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(HistoryTitleText));
        OnPropertyChanged(nameof(HistorySummaryText));
        RefreshHistoryDetails();
    }
    partial void OnPendingWarehouseRemovalChanged(HPPSystem.Models.Material? value)
    {
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    partial void OnSelectedLedgerSourceOptionChanged(SelectionOptionViewModel? value) => RefreshLedgerDetails();
    partial void OnSelectedLedgerMaterialOptionChanged(SelectionOptionViewModel? value) => RefreshLedgerDetails();
    partial void OnSelectedCatalogMaterialOptionChanged(SelectionOptionViewModel? value)
    {
        OnPropertyChanged(nameof(CanAddCatalogMaterial));
        OnPropertyChanged(nameof(CatalogPickerActionText));
    }
    partial void OnAdjustingMaterialIdChanged(string value)
    {
        OnPropertyChanged(nameof(AdjustFormTitleText));
        OnPropertyChanged(nameof(AdjustFormHelperText));
        OnPropertyChanged(nameof(CanSaveAdjustment));
    }

    partial void OnAdjustingMaterialNameChanged(string value)
    {
        OnPropertyChanged(nameof(AdjustFormTitleText));
        OnPropertyChanged(nameof(AdjustFormHelperText));
    }

    partial void OnCurrentStockChanged(decimal value)
    {
        OnPropertyChanged(nameof(AdjustmentDeltaText));
        OnPropertyChanged(nameof(CanSaveAdjustment));
    }

    partial void OnNewStockChanged(decimal value)
    {
        OnPropertyChanged(nameof(AdjustmentDeltaText));
        OnPropertyChanged(nameof(CanSaveAdjustment));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var allProfileItems = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .ToList();
        var warehouseItems = allProfileItems
            .Where(x => x.IsTrackedInWarehouse)
            .ToList();
        var sortedWarehouseItems = ApplySort(warehouseItems)
            .ToList();
        var allCatalogCandidates = allProfileItems
            .Where(x => !x.IsTrackedInWarehouse)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var catalogCandidates = allCatalogCandidates
            .Where(x => MatchesSearch(x, CatalogSearchTerm))
            .ToList();

        var items = sortedWarehouseItems
            .Where(x => MatchesSearch(x, SearchTerm))
            .Where(x => MaterialFocusMode switch
            {
                FocusCriticalValue => x.Stock <= 5,
                FocusOutValue => x.Stock <= 0,
                _ => true
            })
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
            HistoryMaterial = FilteredMaterials.FirstOrDefault(x => x.Id == HistoryMaterial.Id);
        }

        var profileMovements = DataService.StockMovements
            .Where(x => x.ProfileId == profileId)
            .ToList();

        var lowStockCount = sortedWarehouseItems.Count(x => x.Stock <= 5);
        var outStockCount = sortedWarehouseItems.Count(x => x.Stock <= 0);

        MaterialCountText = $"{sortedWarehouseItems.Count} bahan aktif";
        LowStockCountText = $"{lowStockCount} bahan kritis";
        TotalStockText = $"{sortedWarehouseItems.Sum(x => x.Stock):0.##} unit tersimpan";
        SearchSummaryText = $"{items.Count} hasil ditampilkan";
        FocusAllText = $"Semua {sortedWarehouseItems.Count}";
        FocusCriticalText = $"Stok Tipis {lowStockCount}";
        FocusOutText = $"Kosong {outStockCount}";
        MaterialFocusSummaryText = MaterialFocusMode switch
        {
            FocusCriticalValue => "Menampilkan bahan dengan stok tipis atau kosong.",
            FocusOutValue => "Menampilkan bahan yang stoknya sudah habis.",
            _ => "Menampilkan semua bahan aktif."
        };
        InventoryInsightText = sortedWarehouseItems.Count switch
        {
            0 => allCatalogCandidates.Count == 0
                ? "Gudang belum punya stok aktif. Tambahkan material dulu dari halaman Material."
                : "Gudang belum punya stok aktif. Pilih material katalog di panel kanan lalu masukkan ke daftar stok.",
            _ when lowStockCount == 0 => "Stok gudang dalam kondisi aman. Fokuskan audit pada bahan dengan pergerakan tertinggi.",
            _ => $"Ada {lowStockCount} bahan dengan stok tipis. Prioritaskan restock agar produksi tidak terganggu."
        };
        RestockFocusText = BuildRestockFocusText(sortedWarehouseItems);
        _quickRestockMaterialName = sortedWarehouseItems
            .Where(x => x.Stock <= 5)
            .OrderBy(x => x.Stock)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildMaterialLabel)
            .FirstOrDefault() ?? string.Empty;
        QuickRestockActionText = string.IsNullOrWhiteSpace(_quickRestockMaterialName)
            ? "Belum ada stok kritis."
            : $"Fokus restock: {_quickRestockMaterialName}";
        CatalogPickerSummaryText = allCatalogCandidates.Count switch
        {
            0 => "Semua material katalog sudah tersedia di gudang.",
            _ when string.IsNullOrWhiteSpace(CatalogSearchTerm) => allCatalogCandidates.Count == 1
                ? "1 material katalog siap ditambahkan ke gudang."
                : $"{allCatalogCandidates.Count} material katalog siap ditambahkan ke gudang.",
            _ when catalogCandidates.Count == 0 => $"Tidak ada hasil katalog untuk \"{CatalogSearchTerm.Trim()}\".",
            _ => $"{catalogCandidates.Count} dari {allCatalogCandidates.Count} material katalog cocok dengan pencarian."
        };

        OnPropertyChanged(nameof(HasMaterials));
        OnPropertyChanged(nameof(MaterialCountText));
        OnPropertyChanged(nameof(LowStockCountText));
        OnPropertyChanged(nameof(TotalStockText));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(SortSummaryText));
        OnPropertyChanged(nameof(FocusAllText));
        OnPropertyChanged(nameof(FocusCriticalText));
        OnPropertyChanged(nameof(FocusOutText));
        OnPropertyChanged(nameof(MaterialFocusSummaryText));
        OnPropertyChanged(nameof(InventoryInsightText));
        OnPropertyChanged(nameof(RestockFocusText));
        OnPropertyChanged(nameof(QuickRestockActionText));
        OnPropertyChanged(nameof(CatalogPickerSummaryText));
        OnPropertyChanged(nameof(CatalogPickerActionText));
        OnPropertyChanged(nameof(SearchHelperText));
        OnPropertyChanged(nameof(CatalogSearchHelperText));
        OnPropertyChanged(nameof(IsFocusAll));
        OnPropertyChanged(nameof(IsFocusCritical));
        OnPropertyChanged(nameof(IsFocusOut));
        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasActiveMaterialFocus));
        OnPropertyChanged(nameof(HasActiveSearchOrFocus));
        OnPropertyChanged(nameof(HasCatalogSearchTerm));
        OnPropertyChanged(nameof(HasQuickRestockTarget));
        OnPropertyChanged(nameof(HasCatalogMaterialOptions));
        OnPropertyChanged(nameof(CanAddCatalogMaterial));

        RefreshCatalogMaterialOptions(catalogCandidates);
        RefreshLedgerFilters(sortedWarehouseItems, profileMovements);
        RefreshLedgerDetails();
    }

    private void BuildSortOptions()
    {
        SortOptions.Clear();
        SortOptions.Add(new SelectionOptionViewModel { Value = SortNameAsc, Label = "Nama A-Z" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortNameDesc, Label = "Nama Z-A" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitAsc, Label = "Satuan A-Z" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortUnitDesc, Label = "Satuan Z-A" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortStockAsc, Label = "Stok Terkecil-Terbesar" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortStockDesc, Label = "Stok Terbesar-Terkecil" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPriceAsc, Label = "Harga Termurah-Termahal" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPriceDesc, Label = "Harga Termahal-Termurah" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPackAsc, Label = "Isi Pack Terkecil-Terbesar" });
        SortOptions.Add(new SelectionOptionViewModel { Value = SortPackDesc, Label = "Isi Pack Terbesar-Terkecil" });
        SelectedSortOption = SortOptions.FirstOrDefault(x => x.Value == SortNameAsc) ?? SortOptions.FirstOrDefault();
    }

    [RelayCommand]
    private void FocusAll()
    {
        MaterialFocusMode = FocusAllValue;
    }

    [RelayCommand]
    private void FocusCritical()
    {
        MaterialFocusMode = FocusCriticalValue;
    }

    [RelayCommand]
    private void FocusOut()
    {
        MaterialFocusMode = FocusOutValue;
    }

    [RelayCommand]
    private void ClearSearchAndFocus()
    {
        MaterialFocusMode = FocusAllValue;
        SearchTerm = string.Empty;
    }

    [RelayCommand]
    private void ClearCatalogSearch()
    {
        CatalogSearchTerm = string.Empty;
    }

    [RelayCommand]
    private void FocusRestockPriority()
    {
        if (string.IsNullOrWhiteSpace(_quickRestockMaterialName))
        {
            Error("Belum ada bahan dengan stok kritis untuk difokuskan.");
            return;
        }

        MaterialFocusMode = FocusCriticalValue;
        SearchTerm = _quickRestockMaterialName;
    }

    [RelayCommand]
    private async Task AddCatalogMaterialAsync()
    {
        if (SelectedCatalogMaterialOption is null)
        {
            Error("Pilih material katalog yang ingin dimasukkan ke gudang.");
            return;
        }

        var material = DataService.Materials.FirstOrDefault(x => string.Equals(x.Id, SelectedCatalogMaterialOption.Value, StringComparison.Ordinal));
        if (material is null)
        {
            Error("Material katalog tidak ditemukan.");
            return;
        }

        if (material.IsTrackedInWarehouse)
        {
            Error("Material ini sudah aktif di gudang.");
            return;
        }

        var updated = CloneMaterial(material);
        updated.IsTrackedInWarehouse = true;
        await DataService.SaveMaterialAsync(updated);

        Success($"{BuildMaterialLabel(updated)} ditambahkan ke daftar stok gudang.");
        StartAdjust(updated);
    }

    [RelayCommand]
    private void StartAdjust(HPPSystem.Models.Material material)
    {
        AdjustingMaterialId = material.Id;
        AdjustingMaterialName = BuildMaterialLabel(material);
        AdjustingUnit = material.Unit;
        CurrentStock = material.Stock;
        NewStock = material.Stock;
        AdjustmentNotes = string.Empty;
        ShowAdjustForm = true;
    }

    [RelayCommand]
    private void CancelAdjust()
    {
        ShowAdjustForm = false;
        AdjustingMaterialId = string.Empty;
        AdjustingMaterialName = string.Empty;
        AdjustingUnit = string.Empty;
        CurrentStock = 0;
        NewStock = 0;
        AdjustmentNotes = string.Empty;
    }

    [RelayCommand]
    private async Task SaveStockAsync()
    {
        var material = DataService.Materials.FirstOrDefault(x => x.Id == AdjustingMaterialId);
        if (material is null)
        {
            Error("Bahan untuk penyesuaian stok tidak ditemukan.");
            return;
        }

        var delta = NewStock - material.Stock;
        if (delta == 0)
        {
            Error("Tidak ada perubahan stok yang perlu disimpan.");
            return;
        }

        await DataService.ApplyMaterialStockAdjustmentAsync(new MaterialStockAdjustment
        {
            MaterialId = material.Id,
            QuantityDelta = delta,
            SourceType = "manual-adjustment",
            SourceId = material.Id,
            SourceLabel = $"Gudang: {material.Name}",
            Notes = string.IsNullOrWhiteSpace(AdjustmentNotes)
                ? $"Penyesuaian stok gudang ke {NewStock:0.##} {material.Unit}."
                : AdjustmentNotes.Trim()
        });

        Success($"Stok {material.Name} diperbarui.");
        CancelAdjust();
    }

    [RelayCommand]
    private void ShowHistoryFor(HPPSystem.Models.Material material)
    {
        HistoryMaterial = material;
    }

    [RelayCommand]
    private void RequestDeleteFromWarehouse(HPPSystem.Models.Material material)
    {
        PendingWarehouseRemoval = material;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingWarehouseRemoval = null;
    }

    [RelayCommand]
    private async Task DeleteFromWarehouseAsync(HPPSystem.Models.Material material)
    {
        await DataService.RemoveMaterialFromWarehouseAsync(material.Id);

        if (HistoryMaterial?.Id == material.Id)
        {
            HistoryMaterial = null;
        }

        if (string.Equals(AdjustingMaterialId, material.Id, StringComparison.Ordinal))
        {
            CancelAdjust();
        }

        PendingWarehouseRemoval = null;
        Success($"{BuildMaterialLabel(material)} dikeluarkan dari daftar gudang. Master material tetap ada di katalog.");
    }

    [RelayCommand]
    private void CloseHistory()
    {
        HistoryMaterial = null;
    }

    private void RefreshHistoryDetails()
    {
        HistoryPriceRecords.Clear();
        HistoryStockMovements.Clear();

        if (HistoryMaterial is null)
        {
            OnPropertyChanged(nameof(HasHistoryPriceRecords));
            OnPropertyChanged(nameof(HistoryPriceCountText));
            OnPropertyChanged(nameof(HasHistoryStockMovements));
            OnPropertyChanged(nameof(HistoryMovementCountText));
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

        foreach (var movement in DataService.StockMovements
                     .Where(x => x.MaterialId == HistoryMaterial.Id)
                     .OrderByDescending(x => DateTime.TryParse(x.Date, out var date) ? date : DateTime.MinValue))
        {
            var quantityPrefix = movement.QuantityDelta > 0 ? "+" : string.Empty;
            HistoryStockMovements.Add(new StockMovementRowViewModel
            {
                DateText = FormattingHelper.FormatDateTime(movement.Date),
                SourceText = string.IsNullOrWhiteSpace(movement.SourceLabel) ? movement.SourceType : movement.SourceLabel,
                QuantityText = $"{quantityPrefix}{movement.QuantityDelta:0.##} {HistoryMaterial.Unit}",
                BalanceText = $"{movement.PreviousStock:0.##} -> {movement.CurrentStock:0.##} {HistoryMaterial.Unit}",
                NotesText = movement.Notes,
                IsInbound = movement.QuantityDelta >= 0
            });
        }

        OnPropertyChanged(nameof(HasHistoryPriceRecords));
        OnPropertyChanged(nameof(HistoryPriceCountText));
        OnPropertyChanged(nameof(HasHistoryStockMovements));
        OnPropertyChanged(nameof(HistoryMovementCountText));
    }

    private void RefreshLedgerFilters(
        IReadOnlyCollection<HPPSystem.Models.Material> materials,
        IReadOnlyCollection<StockMovement> stockMovements)
    {
        var selectedSourceValue = SelectedLedgerSourceOption?.Value ?? AllLedgerSourcesValue;
        var selectedMaterialValue = SelectedLedgerMaterialOption?.Value ?? AllLedgerMaterialsValue;

        var sourceOptions = stockMovements
            .Select(x => x.SourceType)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetSourceTypeLabel, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectionOptionViewModel
            {
                Value = x,
                Label = GetSourceTypeLabel(x)
            })
            .ToList();
        sourceOptions.Insert(0, new SelectionOptionViewModel
        {
            Value = AllLedgerSourcesValue,
            Label = "Semua Sumber"
        });

        var materialOptions = materials
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectionOptionViewModel
            {
                Value = x.Id,
                Label = BuildMaterialLabel(x)
            })
            .ToList();
        materialOptions.Insert(0, new SelectionOptionViewModel
        {
            Value = AllLedgerMaterialsValue,
            Label = "Semua Bahan"
        });

        ReplaceOptions(LedgerSourceOptions, sourceOptions);
        ReplaceOptions(LedgerMaterialOptions, materialOptions);

        SelectedLedgerSourceOption = LedgerSourceOptions.FirstOrDefault(x => x.Value == selectedSourceValue) ?? LedgerSourceOptions.FirstOrDefault();
        SelectedLedgerMaterialOption = LedgerMaterialOptions.FirstOrDefault(x => x.Value == selectedMaterialValue) ?? LedgerMaterialOptions.FirstOrDefault();
    }

    private void RefreshCatalogMaterialOptions(IReadOnlyCollection<HPPSystem.Models.Material> materials)
    {
        var selectedValue = SelectedCatalogMaterialOption?.Value ?? string.Empty;
        var options = materials
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectionOptionViewModel
            {
                Value = x.Id,
                Label = BuildMaterialLabel(x)
            })
            .ToList();

        ReplaceOptions(CatalogMaterialOptions, options);
        SelectedCatalogMaterialOption = CatalogMaterialOptions.FirstOrDefault(x => x.Value == selectedValue)
            ?? CatalogMaterialOptions.FirstOrDefault();
    }

    private void RefreshLedgerDetails()
    {
        LedgerStockMovements.Clear();

        var profileId = DataService.Settings.ActiveProfileId;
        var materials = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .ToDictionary(x => x.Id, x => x);
        var selectedSource = SelectedLedgerSourceOption?.Value ?? AllLedgerSourcesValue;
        var selectedMaterial = SelectedLedgerMaterialOption?.Value ?? AllLedgerMaterialsValue;

        var movements = DataService.StockMovements
            .Where(x => x.ProfileId == profileId)
            .Where(x => selectedSource == AllLedgerSourcesValue || string.Equals(x.SourceType, selectedSource, StringComparison.OrdinalIgnoreCase))
            .Where(x => selectedMaterial == AllLedgerMaterialsValue || string.Equals(x.MaterialId, selectedMaterial, StringComparison.Ordinal))
            .OrderByDescending(x => DateTime.TryParse(x.Date, out var date) ? date : DateTime.MinValue)
            .ToList();

        foreach (var movement in movements)
        {
            materials.TryGetValue(movement.MaterialId, out var material);
            var unit = material?.Unit ?? string.Empty;
            var quantityPrefix = movement.QuantityDelta > 0 ? "+" : string.Empty;
            var unitSuffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";

            LedgerStockMovements.Add(new StockMovementRowViewModel
            {
                MaterialNameText = material is not null
                    ? BuildMaterialLabel(material)
                    : string.IsNullOrWhiteSpace(movement.MaterialName) ? "Bahan Terhapus" : movement.MaterialName,
                DateText = FormattingHelper.FormatDateTime(movement.Date),
                SourceText = string.IsNullOrWhiteSpace(movement.SourceLabel) ? GetSourceTypeLabel(movement.SourceType) : movement.SourceLabel,
                QuantityText = $"{quantityPrefix}{movement.QuantityDelta:0.##}{unitSuffix}",
                BalanceText = $"{movement.PreviousStock:0.##} -> {movement.CurrentStock:0.##}{unitSuffix}",
                NotesText = movement.Notes,
                IsInbound = movement.QuantityDelta >= 0
            });
        }

        LedgerInboundCountText = $"{movements.Count(x => x.QuantityDelta >= 0)} masuk";
        LedgerOutboundCountText = $"{movements.Count(x => x.QuantityDelta < 0)} keluar";
        LedgerFilterSummaryText = $"{SelectedLedgerSourceOption?.Label ?? "Semua Sumber"}, {SelectedLedgerMaterialOption?.Label ?? "Semua Bahan"}";
        LedgerHeadlineText = movements.Count switch
        {
            0 => "Belum ada aktivitas yang cocok dengan filter ledger saat ini.",
            _ when selectedMaterial != AllLedgerMaterialsValue && SelectedLedgerMaterialOption is not null
                => $"Audit stok fokus pada {SelectedLedgerMaterialOption.Label} dengan {movements.Count} pergerakan terbaru.",
            _ => $"Ledger gudang menampilkan {movements.Count} pergerakan lintas bahan untuk profil aktif."
        };

        OnPropertyChanged(nameof(HasLedgerStockMovements));
        OnPropertyChanged(nameof(LedgerMovementCountText));
        OnPropertyChanged(nameof(LedgerInboundCountText));
        OnPropertyChanged(nameof(LedgerOutboundCountText));
        OnPropertyChanged(nameof(LedgerFilterSummaryText));
        OnPropertyChanged(nameof(LedgerHeadlineText));
    }

    private static void ReplaceOptions(
        ObservableCollection<SelectionOptionViewModel> target,
        IEnumerable<SelectionOptionViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string GetSourceTypeLabel(string sourceType)
    {
        return sourceType switch
        {
            "purchase" => "Belanja Bahan",
            "purchase-rollback" => "Rollback Belanja",
            "production" => "Produksi Resep",
            "manual-adjustment" => "Koreksi Manual",
            _ => string.IsNullOrWhiteSpace(sourceType) ? "Tanpa Sumber" : sourceType
        };
    }

    private static MaterialCardViewModel BuildMaterialCard(HPPSystem.Models.Material material)
    {
        var isOutOfStock = material.Stock <= 0;
        var isLowStock = !isOutOfStock && material.Stock <= 5;
        var isHealthyStock = !isOutOfStock && !isLowStock;

        var statusText = isOutOfStock
            ? "Kosong"
            : isLowStock
                ? "Tipis"
                : "Aman";
        var statusDetail = isOutOfStock
            ? "Segera restock agar resep yang memakai bahan ini tidak tertahan."
            : isLowStock
                ? "Stok mulai menipis, masukkan ke daftar pembelian terdekat."
                : "Ketersediaan masih aman untuk operasional rutin.";

        return new MaterialCardViewModel
        {
            Material = material,
            NameText = material.Name,
            BrandText = string.IsNullOrWhiteSpace(material.Brand) ? "-" : material.Brand,
            UnitBadgeText = material.Unit,
            HistoryBadgeText = $"Riwayat Harga {material.PriceHistory.Count}",
            StockQuantityValue = material.Stock,
            StockUnitText = material.Unit,
            StockValueText = $"{material.Stock:0.##} {material.Unit}",
            StockStatusText = statusText,
            StockStatusDetailText = statusDetail,
            PackPriceValue = material.Price,
            PackPriceText = FormattingHelper.FormatCurrency(material.Price),
            PackQuantityValue = material.Weight,
            PackUnitText = material.Unit,
            WeightText = $"{material.Weight:0.##} {material.Unit}",
            UnitCostValue = material.PricePerUnit,
            UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit),
            PriceHistoryCountValue = material.PriceHistory.Count,
            StockBalanceValue = material.Stock,
            StockBalanceText = $"Sisa {material.Stock:0.##} {material.Unit}",
            IsOutOfStock = isOutOfStock,
            IsLowStock = isLowStock,
            IsHealthyStock = isHealthyStock
        };
    }

    private static HPPSystem.Models.Material CloneMaterial(HPPSystem.Models.Material material)
    {
        return new HPPSystem.Models.Material
        {
            Id = material.Id,
            Name = material.Name,
            Brand = material.Brand,
            Price = material.Price,
            Weight = material.Weight,
            Unit = material.Unit,
            PricePerUnit = material.PricePerUnit,
            Stock = material.Stock,
            IsTrackedInWarehouse = material.IsTrackedInWarehouse,
            ProfileId = material.ProfileId,
            PriceHistory = material.PriceHistory.ToList()
        };
    }

    private static string BuildRestockFocusText(IReadOnlyCollection<HPPSystem.Models.Material> materials)
    {
        var lowStockNames = materials
            .Where(x => x.Stock <= 5)
            .OrderBy(x => x.Stock)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(BuildMaterialLabel)
            .ToList();

        return lowStockNames.Count switch
        {
            0 => "Belum ada bahan dengan stok kritis.",
            1 => $"Prioritas restock: {lowStockNames[0]}.",
            _ => $"Prioritas restock: {string.Join(", ", lowStockNames)}."
        };
    }

    private IEnumerable<HPPSystem.Models.Material> ApplySort(IEnumerable<HPPSystem.Models.Material> materials)
    {
        return (SelectedSortOption?.Value ?? SortNameAsc) switch
        {
            SortNameDesc => materials.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitAsc => materials.OrderBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortUnitDesc => materials.OrderByDescending(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortStockAsc => materials.OrderBy(x => x.Stock).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortStockDesc => materials.OrderByDescending(x => x.Stock).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPriceAsc => materials.OrderBy(x => x.Price).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPriceDesc => materials.OrderByDescending(x => x.Price).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPackAsc => materials.OrderBy(x => x.Weight).ThenBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            SortPackDesc => materials.OrderByDescending(x => x.Weight).ThenBy(x => x.Unit, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase),
            _ => materials.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
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

    private string BuildWarehouseDeletePromptText(HPPSystem.Models.Material material)
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var recipeUsageCount = DataService.Recipes
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
            .Count(x => x.IngredientGroups.Any(group => group.Ingredients.Any(ingredient => string.Equals(ingredient.MaterialId, material.Id, StringComparison.Ordinal))));
        var stockMovementCount = DataService.StockMovements.Count(x => string.Equals(x.MaterialId, material.Id, StringComparison.Ordinal));
        var recipeImpactText = recipeUsageCount switch
        {
            0 => "belum dipakai di resep aktif",
            1 => "dipakai di 1 resep aktif",
            _ => $"dipakai di {recipeUsageCount} resep aktif"
        };

        return $"{BuildMaterialLabel(material)} akan dihapus dari daftar Gudang. Material tetap ada di katalog Material, tetapi stok fisik akan direset ke 0 dan {stockMovementCount} catatan ledger gudang untuk bahan ini akan dibersihkan. Saat ini bahan ini {recipeImpactText}.";
    }
}
