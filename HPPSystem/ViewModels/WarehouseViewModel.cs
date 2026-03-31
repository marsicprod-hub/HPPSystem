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

    private string _quickRestockMaterialName = string.Empty;

    public WarehouseViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
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

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _materialFocusMode = FocusAllValue;

    [ObservableProperty]
    private HPPSystem.Models.Material? _historyMaterial;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedLedgerSourceOption;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedLedgerMaterialOption;

    [ObservableProperty]
    private SelectionOptionViewModel? _selectedCatalogMaterialOption;

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
    public bool HasLedgerStockMovements => LedgerStockMovements.Count > 0;
    public bool HasHistoryPriceRecords => HistoryPriceRecords.Count > 0;
    public bool HasHistoryStockMovements => HistoryStockMovements.Count > 0;
    public bool HasQuickRestockTarget => !string.IsNullOrWhiteSpace(_quickRestockMaterialName);
    public bool HasActiveMaterialFocus => !IsFocusAll;
    public bool HasSearchTerm => !string.IsNullOrWhiteSpace(SearchTerm);
    public bool HasActiveSearchOrFocus => HasSearchTerm || HasActiveMaterialFocus;
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
        ? "Cari nama bahan untuk fokus ke stok tertentu."
        : $"Filter aktif: \"{SearchTerm.Trim()}\"";

    public string HistoryTitleText => HistoryMaterial is null ? string.Empty : $"Audit Gudang {HistoryMaterial.Name}";
    public string HistorySummaryText => HistoryMaterial is null
        ? string.Empty
        : $"Stok aktif {HistoryMaterial.Stock:0.##} {HistoryMaterial.Unit} | harga pack {FormattingHelper.FormatCurrency(HistoryMaterial.Price)}";
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
    partial void OnMaterialFocusModeChanged(string value) => Refresh();
    partial void OnHistoryMaterialChanged(HPPSystem.Models.Material? value)
    {
        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(HistoryTitleText));
        OnPropertyChanged(nameof(HistorySummaryText));
        RefreshHistoryDetails();
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
            .OrderBy(x => x.Name)
            .ToList();
        var warehouseItems = allProfileItems
            .Where(x => x.IsTrackedInWarehouse)
            .ToList();
        var catalogCandidates = allProfileItems
            .Where(x => !x.IsTrackedInWarehouse)
            .OrderBy(x => x.Name)
            .ToList();

        var items = warehouseItems
            .Where(x => string.IsNullOrWhiteSpace(SearchTerm) || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
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

        var lowStockCount = warehouseItems.Count(x => x.Stock <= 5);
        var outStockCount = warehouseItems.Count(x => x.Stock <= 0);

        MaterialCountText = $"{warehouseItems.Count} bahan aktif";
        LowStockCountText = $"{lowStockCount} bahan kritis";
        TotalStockText = $"{warehouseItems.Sum(x => x.Stock):0.##} unit tersimpan";
        SearchSummaryText = $"{items.Count} hasil ditampilkan";
        FocusAllText = $"Semua {warehouseItems.Count}";
        FocusCriticalText = $"Stok Tipis {lowStockCount}";
        FocusOutText = $"Kosong {outStockCount}";
        MaterialFocusSummaryText = MaterialFocusMode switch
        {
            FocusCriticalValue => "Menampilkan bahan dengan stok tipis atau kosong.",
            FocusOutValue => "Menampilkan bahan yang stoknya sudah habis.",
            _ => "Menampilkan semua bahan aktif."
        };
        InventoryInsightText = warehouseItems.Count switch
        {
            0 => catalogCandidates.Count == 0
                ? "Gudang belum punya stok aktif. Tambahkan material dulu dari halaman Material."
                : "Gudang belum punya stok aktif. Pilih material katalog di panel kanan lalu masukkan ke daftar stok.",
            _ when lowStockCount == 0 => "Stok gudang dalam kondisi aman. Fokuskan audit pada bahan dengan pergerakan tertinggi.",
            _ => $"Ada {lowStockCount} bahan dengan stok tipis. Prioritaskan restock agar produksi tidak terganggu."
        };
        RestockFocusText = BuildRestockFocusText(warehouseItems);
        _quickRestockMaterialName = warehouseItems
            .Where(x => x.Stock <= 5)
            .OrderBy(x => x.Stock)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .FirstOrDefault() ?? string.Empty;
        QuickRestockActionText = string.IsNullOrWhiteSpace(_quickRestockMaterialName)
            ? "Belum ada stok kritis."
            : $"Fokus restock: {_quickRestockMaterialName}";
        CatalogPickerSummaryText = catalogCandidates.Count switch
        {
            0 => "Semua material katalog sudah tersedia di gudang.",
            1 => $"1 material katalog siap ditambahkan ke gudang.",
            _ => $"{catalogCandidates.Count} material katalog siap ditambahkan ke gudang."
        };

        OnPropertyChanged(nameof(HasMaterials));
        OnPropertyChanged(nameof(MaterialCountText));
        OnPropertyChanged(nameof(LowStockCountText));
        OnPropertyChanged(nameof(TotalStockText));
        OnPropertyChanged(nameof(SearchSummaryText));
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
        OnPropertyChanged(nameof(IsFocusAll));
        OnPropertyChanged(nameof(IsFocusCritical));
        OnPropertyChanged(nameof(IsFocusOut));
        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasActiveMaterialFocus));
        OnPropertyChanged(nameof(HasActiveSearchOrFocus));
        OnPropertyChanged(nameof(HasQuickRestockTarget));
        OnPropertyChanged(nameof(HasCatalogMaterialOptions));
        OnPropertyChanged(nameof(CanAddCatalogMaterial));

        RefreshCatalogMaterialOptions(catalogCandidates);
        RefreshLedgerFilters(warehouseItems, profileMovements);
        RefreshLedgerDetails();
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

        Success($"{updated.Name} ditambahkan ke daftar stok gudang.");
        StartAdjust(updated);
    }

    [RelayCommand]
    private void StartAdjust(HPPSystem.Models.Material material)
    {
        AdjustingMaterialId = material.Id;
        AdjustingMaterialName = material.Name;
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
            HistoryPriceRecords.Add(new PriceHistoryRowViewModel
            {
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
            .OrderBy(GetSourceTypeLabel)
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
            .OrderBy(x => x.Name)
            .Select(x => new SelectionOptionViewModel
            {
                Value = x.Id,
                Label = x.Name
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
            .OrderBy(x => x.Name)
            .Select(x => new SelectionOptionViewModel
            {
                Value = x.Id,
                Label = x.Name
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
                MaterialNameText = string.IsNullOrWhiteSpace(movement.MaterialName) ? material?.Name ?? "Bahan Terhapus" : movement.MaterialName,
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
            UnitBadgeText = $"Unit {material.Unit}",
            HistoryBadgeText = $"Riwayat Harga {material.PriceHistory.Count}",
            StockValueText = $"{material.Stock:0.##} {material.Unit}",
            StockStatusText = statusText,
            StockStatusDetailText = statusDetail,
            PackPriceText = FormattingHelper.FormatCurrency(material.Price),
            WeightText = $"{material.Weight:0.##} {material.Unit}",
            UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit),
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
            .ThenBy(x => x.Name)
            .Take(3)
            .Select(x => x.Name)
            .ToList();

        return lowStockNames.Count switch
        {
            0 => "Belum ada bahan dengan stok kritis.",
            1 => $"Prioritas restock: {lowStockNames[0]}.",
            _ => $"Prioritas restock: {string.Join(", ", lowStockNames)}."
        };
    }
}
