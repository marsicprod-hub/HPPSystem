using System;
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
    public BookkeepingViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        FormDate = DateTime.Today.ToString("yyyy-MM-dd");
        Refresh();
    }

    public override string Title => "Pembukuan & Kas";

    public ObservableCollection<Transaction> Transactions { get; } = new();
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
    private Transaction? _pendingDelete;

    public string TotalIncomeText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TotalExpenseText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string NetBalanceText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public bool IsBelanjaBahan => FormCategory == "Belanja Bahan";
    public decimal AutoCalculatedAmount => PurchasedItems.Sum(x => x.Price);
    public bool ShowDeletePrompt => PendingDelete is not null;
    public ObservableCollection<string> CategoryOptions { get; } = new();

    partial void OnFormCategoryChanged(string value) => OnPropertyChanged(nameof(IsBelanjaBahan));
    partial void OnFormTypeChanged(string value)
    {
        RefreshCategoryOptions();
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
        var manualIncome = transactions.Where(x => x.Type == "income").Sum(x => x.Amount);
        var totalExpense = transactions.Where(x => x.Type == "expense").Sum(x => x.Amount);
        var totalIncome = posRevenue + manualIncome;

        TotalIncomeText = FormattingHelper.FormatCurrency(totalIncome);
        TotalExpenseText = FormattingHelper.FormatCurrency(totalExpense);
        NetBalanceText = FormattingHelper.FormatCurrency(totalIncome - totalExpense);

        OnPropertyChanged(nameof(TotalIncomeText));
        OnPropertyChanged(nameof(TotalExpenseText));
        OnPropertyChanged(nameof(NetBalanceText));
        OnPropertyChanged(nameof(IsBelanjaBahan));
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        OnPropertyChanged(nameof(ShowDeletePrompt));
        RefreshCategoryOptions();
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
    private void AddPurchasedItem()
    {
        var item = new PurchasedItemEntryViewModel();
        item.PropertyChanged += OnPurchasedItemChanged;
        PurchasedItems.Add(item);
        OnPropertyChanged(nameof(AutoCalculatedAmount));
    }

    [RelayCommand]
    private void RemovePurchasedItem(PurchasedItemEntryViewModel item)
    {
        PurchasedItems.Remove(item);
        OnPropertyChanged(nameof(AutoCalculatedAmount));
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

        var transaction = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = FormType,
            Category = FormCategory,
            Description = FormDescription.Trim(),
            Date = FormDate,
            Amount = amount,
            ProfileId = DataService.Settings.ActiveProfileId,
            PurchasedItems = IsBelanjaBahan
                ? PurchasedItems
                    .Where(x => (!string.IsNullOrWhiteSpace(x.MaterialId) || !string.IsNullOrWhiteSpace(x.CustomName)) && x.Qty > 0 && x.Price > 0)
                    .Select(x => new PurchasedItem
                    {
                        MaterialId = x.MaterialId,
                        CustomName = string.IsNullOrWhiteSpace(x.CustomName)
                            ? AvailableMaterials.FirstOrDefault(m => m.Id == x.MaterialId)?.Name ?? string.Empty
                            : x.CustomName,
                        Qty = x.Qty,
                        Price = x.Price
                    })
                    .ToList()
                : new()
        };

        await DataService.SaveTransactionAsync(transaction);

        if (FormCategory == "Belanja Bahan")
        {
            foreach (var item in PurchasedItems.Where(x => !string.IsNullOrWhiteSpace(x.MaterialId) && x.MaterialId != "custom"))
            {
                var material = AvailableMaterials.FirstOrDefault(x => x.Id == item.MaterialId);
                if (material is null)
                {
                    continue;
                }

                material.Stock += item.Qty;
                await DataService.SaveMaterialAsync(material);
            }
        }

        Success("Transaksi pembukuan disimpan.");
        ResetForm();
    }

    [RelayCommand]
    private async Task DeleteAsync(Transaction transaction)
    {
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

                material.Stock -= item.Qty;
                await DataService.SaveMaterialAsync(material);
            }
        }

        await DataService.DeleteTransactionAsync(transaction.Id);
        Success($"Transaksi {transaction.Description} dihapus.");
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }

    [RelayCommand]
    private void RequestDelete(Transaction transaction)
    {
        PendingDelete = transaction;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
    }

    private void ResetForm()
    {
        FormType = "expense";
        FormCategory = "Belanja Bahan";
        FormDescription = string.Empty;
        FormDate = DateTime.Today.ToString("yyyy-MM-dd");
        FormAmount = 0;
        PurchasedItems.Clear();
        OnPropertyChanged(nameof(AutoCalculatedAmount));
        RefreshCategoryOptions();
    }

    private void OnPurchasedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AutoCalculatedAmount));
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
}
