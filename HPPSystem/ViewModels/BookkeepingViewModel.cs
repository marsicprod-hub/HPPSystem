using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class BookkeepingViewModel : PageViewModelBase
{
    private const string PosIncomeCategory = "Penjualan POS";
    private const string HistoryAllValue = "all";
    private const string HistoryExpenseValue = "expense";
    private const string HistoryIncomeValue = "income";
    private const string HistoryBelanjaValue = "belanja-bahan";

    private string _latestBelanjaTemplateId = string.Empty;

    public BookkeepingViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        FormDate = DateTime.Today.ToString("yyyy-MM-dd");
        Refresh();
    }

    public override string Title => "Pembukuan & Kas";

    public ObservableCollection<Transaction> Transactions { get; } = new();
    public ObservableCollection<BookkeepingTransactionCardViewModel> FilteredTransactionCards { get; } = new();
    public ObservableCollection<HPPSystem.Models.Material> AvailableMaterials { get; } = new();
    public ObservableCollection<PurchasedItemEntryViewModel> PurchasedItems { get; } = new();

    [ObservableProperty]
    private string _formType = "expense";

    [ObservableProperty]
    private string _formCategory = "Belanja Bahan";

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private string _formDate = DateTime.Today.ToString("yyyy-MM-dd");

    [ObservableProperty]
    private decimal _formAmount;

    [ObservableProperty]
    private bool _syncPurchasesToCatalog = true;

    [ObservableProperty]
    private string _historySearchTerm = string.Empty;

    [ObservableProperty]
    private string _historyFocusMode = HistoryAllValue;

    [ObservableProperty]
    private Transaction? _pendingDelete;

    public string TotalIncomeText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TotalExpenseText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string NetBalanceText { get; private set; } = FormattingHelper.FormatCurrency(0);

    public bool IsBelanjaBahan => FormCategory == "Belanja Bahan";
    public bool IsExpenseMode => FormType == "expense";
    public bool IsIncomeMode => FormType == "income";
    public decimal AutoCalculatedAmount => PurchasedItems.Sum(x => x.Price);
    public bool HasPurchasedItems => PurchasedItems.Count > 0;
    public bool ShowDeletePrompt => PendingDelete is not null;

    public ObservableCollection<string> CategoryOptions { get; } = new();
    public int SyncCandidateCount => PurchasedItems.Count(x => !string.IsNullOrWhiteSpace(x.MaterialId));

    public string SyncCatalogSummaryText => !IsBelanjaBahan
        ? "Sinkronisasi harga bahan hanya berlaku untuk transaksi belanja bahan."
        : !SyncPurchasesToCatalog
            ? "Belanja akan menambah stok tanpa memperbarui harga master bahan."
            : SyncCandidateCount == 0
                ? "Pilih material master agar harga beli terbaru bisa mendorong update katalog."
                : $"{SyncCandidateCount} bahan terhubung akan memperbarui harga pack, bobot acuan, dan histori harga.";

    public string FormAmountPreviewText => FormattingHelper.FormatCurrency(IsBelanjaBahan && PurchasedItems.Any() ? AutoCalculatedAmount : FormAmount);
    public string FormFlowSummaryText => FormType switch
    {
        "income" => "Mode pemasukan aktif: catat arus kas masuk non-POS secara konsisten.",
        _ when IsBelanjaBahan => "Mode belanja bahan aktif: subtotal item dipakai sebagai nominal transaksi.",
        _ => "Mode pengeluaran aktif: gunakan kategori agar laporan kas lebih bersih."
    };
    public string SaveActionText => FormType == "income" ? "Simpan Pemasukan" : "Simpan Pengeluaran";

    public string HistoryAllText { get; private set; } = "Semua 0";
    public string HistoryExpenseText { get; private set; } = "Keluar 0";
    public string HistoryIncomeText { get; private set; } = "Masuk 0";
    public string HistoryBelanjaText { get; private set; } = "Belanja 0";
    public string HistorySummaryText { get; private set; } = "0 transaksi ditampilkan.";
    public string LastTransactionText { get; private set; } = "Belum ada transaksi terakhir.";
    public string BelanjaTemplateActionText { get; private set; } = "Belum ada template belanja terakhir.";

    public bool IsHistoryAll => string.Equals(HistoryFocusMode, HistoryAllValue, StringComparison.Ordinal);
    public bool IsHistoryExpense => string.Equals(HistoryFocusMode, HistoryExpenseValue, StringComparison.Ordinal);
    public bool IsHistoryIncome => string.Equals(HistoryFocusMode, HistoryIncomeValue, StringComparison.Ordinal);
    public bool IsHistoryBelanja => string.Equals(HistoryFocusMode, HistoryBelanjaValue, StringComparison.Ordinal);
    public bool HasHistorySearch => !string.IsNullOrWhiteSpace(HistorySearchTerm);
    public bool HasActiveHistoryFilter => !IsHistoryAll;
    public bool HasActiveHistorySearchOrFilter => HasHistorySearch || HasActiveHistoryFilter;
    public bool HasHistoryItems => FilteredTransactionCards.Count > 0;
    public bool HasBelanjaTemplateSource => !string.IsNullOrWhiteSpace(_latestBelanjaTemplateId);

    public string HistorySearchHelperText => string.IsNullOrWhiteSpace(HistorySearchTerm)
        ? "Cari deskripsi atau kategori transaksi untuk fokus ke catatan tertentu."
        : $"Filter histori aktif: \"{HistorySearchTerm.Trim()}\"";

    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : $"Hapus transaksi {PendingDelete.Description} ({FormattingHelper.FormatCurrency(PendingDelete.Amount)}) tanggal {FormatTransactionDate(PendingDelete.Date)}?";

    partial void OnFormCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsBelanjaBahan));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
        OnPropertyChanged(nameof(FormFlowSummaryText));
        OnPropertyChanged(nameof(FormAmountPreviewText));
    }

    partial void OnSyncPurchasesToCatalogChanged(bool value) => OnPropertyChanged(nameof(SyncCatalogSummaryText));

    partial void OnFormTypeChanged(string value)
    {
        RefreshCategoryOptions();
        OnPropertyChanged(nameof(IsExpenseMode));
        OnPropertyChanged(nameof(IsIncomeMode));
        OnPropertyChanged(nameof(FormFlowSummaryText));
        OnPropertyChanged(nameof(SaveActionText));
        OnPropertyChanged(nameof(FormAmountPreviewText));
    }

    partial void OnFormAmountChanged(decimal value) => OnPropertyChanged(nameof(FormAmountPreviewText));
    partial void OnHistorySearchTermChanged(string value) => RefreshHistoryView();
    partial void OnHistoryFocusModeChanged(string value) => RefreshHistoryView();
    partial void OnPendingDeleteChanged(Transaction? value)
    {
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var transactions = DataService.Transactions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => DateTime.TryParse(x.Date, out var date) ? date : DateTime.MinValue)
            .ToList();

        Transactions.Clear();
        foreach (var transaction in transactions)
        {
            Transactions.Add(transaction);
        }

        AvailableMaterials.Clear();
        foreach (var material in DataService.Materials.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name))
        {
            AvailableMaterials.Add(material);
        }

        var posRevenue = DataService.Sales.Where(x => x.ProfileId == profileId).Sum(x => x.TotalPrice);
        var manualIncome = transactions
            .Where(x => x.Type == "income" && !IsPosTransaction(x))
            .Sum(x => x.Amount);
        var totalExpense = transactions.Where(x => x.Type == "expense").Sum(x => x.Amount);
        var totalIncome = posRevenue + manualIncome;

        TotalIncomeText = FormattingHelper.FormatCurrency(totalIncome);
        TotalExpenseText = FormattingHelper.FormatCurrency(totalExpense);
        NetBalanceText = FormattingHelper.FormatCurrency(totalIncome - totalExpense);

        OnPropertyChanged(nameof(TotalIncomeText));
        OnPropertyChanged(nameof(TotalExpenseText));
        OnPropertyChanged(nameof(NetBalanceText));
        OnPropertyChanged(nameof(IsBelanjaBahan));
        OnPropertyChanged(nameof(IsExpenseMode));
        OnPropertyChanged(nameof(IsIncomeMode));
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(HasPurchasedItems));
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
        OnPropertyChanged(nameof(SyncCandidateCount));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(FormFlowSummaryText));
        OnPropertyChanged(nameof(SaveActionText));

        RefreshCategoryOptions();
        RefreshHistoryView();
        RefreshBelanjaTemplateState();
    }

    [RelayCommand]
    private void SetExpense()
    {
        FormType = "expense";
        FormCategory = "Belanja Bahan";
    }

    [RelayCommand]
    private void SetIncome()
    {
        FormType = "income";
        FormCategory = "Pendapatan Tambahan";
    }

    [RelayCommand]
    private void FocusAllHistory()
    {
        HistoryFocusMode = HistoryAllValue;
    }

    [RelayCommand]
    private void FocusExpenseHistory()
    {
        HistoryFocusMode = HistoryExpenseValue;
    }

    [RelayCommand]
    private void FocusIncomeHistory()
    {
        HistoryFocusMode = HistoryIncomeValue;
    }

    [RelayCommand]
    private void FocusBelanjaHistory()
    {
        HistoryFocusMode = HistoryBelanjaValue;
    }

    [RelayCommand]
    private void ClearHistoryFilter()
    {
        HistorySearchTerm = string.Empty;
        HistoryFocusMode = HistoryAllValue;
    }

    [RelayCommand]
    private void AddPurchasedItem()
    {
        var item = new PurchasedItemEntryViewModel();
        item.PropertyChanged += OnPurchasedItemChanged;
        PurchasedItems.Add(item);
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(HasPurchasedItems));
    }

    [RelayCommand]
    private void RemovePurchasedItem(PurchasedItemEntryViewModel item)
    {
        item.PropertyChanged -= OnPurchasedItemChanged;
        PurchasedItems.Remove(item);
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(HasPurchasedItems));
        OnPropertyChanged(nameof(SyncCandidateCount));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
    }

    [RelayCommand]
    private void ClearPurchasedItems()
    {
        foreach (var item in PurchasedItems)
        {
            item.PropertyChanged -= OnPurchasedItemChanged;
        }

        PurchasedItems.Clear();
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(HasPurchasedItems));
        OnPropertyChanged(nameof(SyncCandidateCount));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
    }

    [RelayCommand]
    private void ApplyLastBelanjaTemplate()
    {
        var template = Transactions.FirstOrDefault(x => x.Id == _latestBelanjaTemplateId);
        if (template is null || template.PurchasedItems.Count == 0)
        {
            Error("Template belanja terakhir tidak tersedia.");
            return;
        }

        SetExpense();
        FormCategory = "Belanja Bahan";
        FormDescription = $"Belanja ulang: {template.Description}";
        FormDate = DateTime.Today.ToString("yyyy-MM-dd");
        ClearPurchasedItems();

        foreach (var source in template.PurchasedItems)
        {
            var item = new PurchasedItemEntryViewModel
            {
                MaterialId = source.MaterialId,
                CustomName = source.CustomName,
                Qty = source.Qty,
                Price = source.Price
            };
            item.PropertyChanged += OnPurchasedItemChanged;
            PurchasedItems.Add(item);
        }

        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(HasPurchasedItems));
        OnPropertyChanged(nameof(SyncCandidateCount));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
        Success("Template belanja terakhir diterapkan ke form.");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var amount = IsBelanjaBahan && PurchasedItems.Any() ? AutoCalculatedAmount : FormAmount;
        if (amount <= 0 || string.IsNullOrWhiteSpace(FormDescription))
        {
            Error("Nominal dan deskripsi transaksi wajib diisi.");
            return;
        }

        var purchaseItems = IsBelanjaBahan
            ? PurchasedItems
                .Where(x => (!string.IsNullOrWhiteSpace(x.MaterialId) || !string.IsNullOrWhiteSpace(x.CustomName)) && x.Qty > 0 && x.Price > 0)
                .Select(x =>
                {
                    var material = AvailableMaterials.FirstOrDefault(m => m.Id == x.MaterialId);
                    var shouldSync = SyncPurchasesToCatalog && material is not null;
                    return new PurchasedItem
                    {
                        MaterialId = x.MaterialId,
                        CustomName = string.IsNullOrWhiteSpace(x.CustomName)
                            ? material?.Name ?? string.Empty
                            : x.CustomName,
                        Qty = x.Qty,
                        Price = x.Price,
                        SyncToMaterialCatalog = shouldSync,
                        PreviousMaterialPrice = material?.Price ?? 0,
                        PreviousMaterialWeight = material?.Weight ?? 0,
                        AppliedMaterialPrice = shouldSync ? x.Price : 0,
                        AppliedMaterialWeight = shouldSync ? x.Qty : 0
                    };
                })
                .ToList()
            : new List<PurchasedItem>();

        var transaction = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = FormType,
            Category = FormCategory,
            Description = FormDescription.Trim(),
            Date = FormDate,
            Amount = amount,
            ProfileId = DataService.Settings.ActiveProfileId,
            PurchasedItems = purchaseItems
        };

        await DataService.SaveTransactionAsync(transaction);

        if (FormCategory == "Belanja Bahan")
        {
            foreach (var item in purchaseItems.Where(x => !string.IsNullOrWhiteSpace(x.MaterialId)))
            {
                await DataService.ApplyMaterialStockAdjustmentAsync(new MaterialStockAdjustment
                {
                    MaterialId = item.MaterialId,
                    QuantityDelta = item.Qty,
                    SourceType = "purchase",
                    SourceId = transaction.Id,
                    SourceLabel = transaction.Description,
                    Notes = item.SyncToMaterialCatalog
                        ? "Belanja bahan menambah stok dan menyinkronkan harga master."
                        : "Belanja bahan menambah stok tanpa sinkronisasi harga master.",
                    UpdatedPrice = item.SyncToMaterialCatalog ? item.AppliedMaterialPrice : null,
                    UpdatedWeight = item.SyncToMaterialCatalog ? item.AppliedMaterialWeight : null
                });
            }
        }

        Success("Transaksi pembukuan disimpan.");
        ResetForm();
    }

    [RelayCommand]
    private async Task DeleteAsync(Transaction transaction)
    {
        if (IsPosTransaction(transaction))
        {
            Error("Transaksi POS dibuat otomatis dari checkout dan tidak bisa dihapus dari pembukuan.");
            return;
        }

        if (string.Equals(transaction.Category, "Belanja Bahan", StringComparison.Ordinal) && transaction.PurchasedItems.Count > 0)
        {
            var shortages = transaction.PurchasedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.MaterialId))
                .Select(x => new
                {
                    Item = x,
                    Material = AvailableMaterials.FirstOrDefault(material => material.Id == x.MaterialId)
                })
                .Where(x => x.Material is not null && x.Material.Stock < x.Item.Qty)
                .Select(x => $"{x.Material!.Name} tersisa {x.Material.Stock:0.##}, perlu rollback {x.Item.Qty:0.##}")
                .ToList();

            if (shortages.Count > 0)
            {
                Error($"Transaksi tidak bisa dihapus karena stok sudah terpakai: {string.Join("; ", shortages)}.");
                return;
            }

            foreach (var item in transaction.PurchasedItems.Where(x => !string.IsNullOrWhiteSpace(x.MaterialId)))
            {
                var material = AvailableMaterials.FirstOrDefault(x => x.Id == item.MaterialId);
                if (material is null)
                {
                    continue;
                }

                var shouldRevertPricing = item.SyncToMaterialCatalog
                    && item.PreviousMaterialPrice > 0
                    && item.PreviousMaterialWeight > 0
                    && material.Price == item.AppliedMaterialPrice
                    && material.Weight == item.AppliedMaterialWeight;

                await DataService.ApplyMaterialStockAdjustmentAsync(new MaterialStockAdjustment
                {
                    MaterialId = item.MaterialId,
                    QuantityDelta = -item.Qty,
                    SourceType = "purchase-rollback",
                    SourceId = transaction.Id,
                    SourceLabel = transaction.Description,
                    Notes = shouldRevertPricing
                        ? "Rollback transaksi belanja mengurangi stok dan mengembalikan harga master sebelumnya."
                        : "Rollback transaksi belanja mengurangi stok tanpa mengubah harga master.",
                    UpdatedPrice = shouldRevertPricing ? item.PreviousMaterialPrice : null,
                    UpdatedWeight = shouldRevertPricing ? item.PreviousMaterialWeight : null
                });
            }
        }

        await DataService.DeleteTransactionAsync(transaction.Id);
        Success($"Transaksi {transaction.Description} dihapus.");
        PendingDelete = null;
    }

    [RelayCommand]
    private void RequestDelete(Transaction transaction)
    {
        PendingDelete = transaction;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
    }

    private void ResetForm()
    {
        FormType = "expense";
        FormCategory = "Belanja Bahan";
        FormDescription = string.Empty;
        FormDate = DateTime.Today.ToString("yyyy-MM-dd");
        FormAmount = 0;
        SyncPurchasesToCatalog = true;
        ClearPurchasedItems();
        RefreshCategoryOptions();
    }

    private void OnPurchasedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(FormAmountPreviewText));
        OnPropertyChanged(nameof(HasPurchasedItems));
        OnPropertyChanged(nameof(SyncCandidateCount));
        OnPropertyChanged(nameof(SyncCatalogSummaryText));
    }

    private void RefreshCategoryOptions()
    {
        CategoryOptions.Clear();
        if (FormType == "expense")
        {
            CategoryOptions.Add("Belanja Bahan");
            CategoryOptions.Add("Operasional");
            CategoryOptions.Add("Gaji Karyawan");
            CategoryOptions.Add("Aset / Peralatan");
            CategoryOptions.Add("Lain-lain");
        }
        else
        {
            CategoryOptions.Add("Pendapatan Tambahan");
            CategoryOptions.Add("Suntikan Modal");
            CategoryOptions.Add("Lain-lain");
        }

        if (!CategoryOptions.Contains(FormCategory))
        {
            FormCategory = CategoryOptions.FirstOrDefault() ?? string.Empty;
        }
    }

    private void RefreshHistoryView()
    {
        var all = Transactions.ToList();
        var filtered = all
            .Where(x => string.IsNullOrWhiteSpace(HistorySearchTerm) || MatchHistorySearch(x, HistorySearchTerm))
            .Where(x => HistoryFocusMode switch
            {
                HistoryExpenseValue => string.Equals(x.Type, "expense", StringComparison.OrdinalIgnoreCase),
                HistoryIncomeValue => string.Equals(x.Type, "income", StringComparison.OrdinalIgnoreCase),
                HistoryBelanjaValue => string.Equals(x.Category, "Belanja Bahan", StringComparison.OrdinalIgnoreCase),
                _ => true
            })
            .ToList();

        FilteredTransactionCards.Clear();
        foreach (var transaction in filtered)
        {
            FilteredTransactionCards.Add(BuildTransactionCard(transaction));
        }

        HistoryAllText = $"Semua {all.Count}";
        HistoryExpenseText = $"Keluar {all.Count(x => string.Equals(x.Type, "expense", StringComparison.OrdinalIgnoreCase))}";
        HistoryIncomeText = $"Masuk {all.Count(x => string.Equals(x.Type, "income", StringComparison.OrdinalIgnoreCase))}";
        HistoryBelanjaText = $"Belanja {all.Count(x => string.Equals(x.Category, "Belanja Bahan", StringComparison.OrdinalIgnoreCase))}";
        HistorySummaryText = $"{filtered.Count} transaksi ditampilkan";
        LastTransactionText = all.FirstOrDefault() is { } latest
            ? $"{FormatTransactionDate(latest.Date)} | {latest.Description} | {FormattingHelper.FormatCurrency(latest.Amount)}"
            : "Belum ada transaksi terakhir.";

        OnPropertyChanged(nameof(HistoryAllText));
        OnPropertyChanged(nameof(HistoryExpenseText));
        OnPropertyChanged(nameof(HistoryIncomeText));
        OnPropertyChanged(nameof(HistoryBelanjaText));
        OnPropertyChanged(nameof(HistorySummaryText));
        OnPropertyChanged(nameof(LastTransactionText));
        OnPropertyChanged(nameof(IsHistoryAll));
        OnPropertyChanged(nameof(IsHistoryExpense));
        OnPropertyChanged(nameof(IsHistoryIncome));
        OnPropertyChanged(nameof(IsHistoryBelanja));
        OnPropertyChanged(nameof(HasHistorySearch));
        OnPropertyChanged(nameof(HasActiveHistoryFilter));
        OnPropertyChanged(nameof(HasActiveHistorySearchOrFilter));
        OnPropertyChanged(nameof(HasHistoryItems));
        OnPropertyChanged(nameof(HistorySearchHelperText));
    }

    private void RefreshBelanjaTemplateState()
    {
        var template = Transactions
            .FirstOrDefault(x => string.Equals(x.Category, "Belanja Bahan", StringComparison.OrdinalIgnoreCase) && x.PurchasedItems.Count > 0);

        _latestBelanjaTemplateId = template?.Id ?? string.Empty;
        BelanjaTemplateActionText = template is null
            ? "Belum ada template belanja terakhir."
            : $"Gunakan template: {template.Description}";

        OnPropertyChanged(nameof(HasBelanjaTemplateSource));
        OnPropertyChanged(nameof(BelanjaTemplateActionText));
    }

    private static bool MatchHistorySearch(Transaction transaction, string keyword)
    {
        var term = keyword.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        if (transaction.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (transaction.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (transaction.Date.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return transaction.PurchasedItems.Any(x =>
            x.CustomName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.MaterialId.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatTransactionDate(string value)
    {
        return DateTime.TryParse(value, out var date)
            ? date.ToString("dd MMM yyyy")
            : value;
    }

    private static bool IsPosTransaction(Transaction transaction)
    {
        return string.Equals(transaction.Type, "income", StringComparison.OrdinalIgnoreCase)
            && string.Equals(transaction.Category, PosIncomeCategory, StringComparison.OrdinalIgnoreCase);
    }

    private static BookkeepingTransactionCardViewModel BuildTransactionCard(Transaction transaction)
    {
        var isIncome = string.Equals(transaction.Type, "income", StringComparison.OrdinalIgnoreCase);
        var purchasedCount = transaction.PurchasedItems.Count;
        var purchaseSummary = purchasedCount == 0
            ? "Tanpa item belanja."
            : string.Join(", ", transaction.PurchasedItems
                .Select(x => string.IsNullOrWhiteSpace(x.CustomName) ? x.MaterialId : x.CustomName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3));

        if (purchasedCount > 3)
        {
            purchaseSummary += ", ...";
        }

        var amountPrefix = isIncome ? "+" : "-";
        return new BookkeepingTransactionCardViewModel
        {
            Transaction = transaction,
            DescriptionText = transaction.Description,
            CategoryText = transaction.Category,
            DateText = FormatTransactionDate(transaction.Date),
            AmountText = $"{amountPrefix}{FormattingHelper.FormatCurrency(transaction.Amount)}",
            TypeBadgeText = isIncome ? "Kas Masuk" : "Kas Keluar",
            PurchaseCountText = $"{purchasedCount} item belanja",
            PurchaseSummaryText = purchaseSummary,
            HasPurchasedItems = purchasedCount > 0,
            IsIncome = isIncome,
            IsExpense = !isIncome
        };
    }
}
