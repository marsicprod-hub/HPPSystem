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

public sealed partial class PosViewModel : PageViewModelBase
{
    private const string PosIncomeCategory = "Penjualan POS";
    private const string FocusAllValue = "all";
    private const string FocusRecipeValue = "recipe";
    private const string FocusComboValue = "combo";

    private string _quickAddItemId = string.Empty;
    private string _quickAddItemType = string.Empty;

    public PosViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Point of Sale";

    public ObservableCollection<PosCatalogItemViewModel> RecipeCatalog { get; } = new();
    public ObservableCollection<PosCatalogItemViewModel> ComboCatalog { get; } = new();
    public ObservableCollection<PosCatalogItemViewModel> FilteredCatalog { get; } = new();
    public ObservableCollection<CartItemViewModel> Cart { get; } = new();

    [ObservableProperty]
    private string _catalogSearchTerm = string.Empty;

    [ObservableProperty]
    private string _catalogFocusMode = FocusAllValue;

    [ObservableProperty]
    private CartItemViewModel? _selectedCartItem;

    [ObservableProperty]
    private decimal _customPrice;

    public string FocusAllText { get; private set; } = "Semua 0";
    public string FocusRecipeText { get; private set; } = "Resep 0";
    public string FocusComboText { get; private set; } = "Bundle 0";
    public string CatalogSummaryText { get; private set; } = "0 item ditampilkan";
    public string CatalogFocusSummaryText { get; private set; } = "Menampilkan seluruh katalog POS.";
    public string QuickAddActionText { get; private set; } = "Belum ada item untuk quick add";

    public bool IsFocusAll => string.Equals(CatalogFocusMode, FocusAllValue, StringComparison.Ordinal);
    public bool IsFocusRecipe => string.Equals(CatalogFocusMode, FocusRecipeValue, StringComparison.Ordinal);
    public bool IsFocusCombo => string.Equals(CatalogFocusMode, FocusComboValue, StringComparison.Ordinal);
    public bool HasSearchTerm => !string.IsNullOrWhiteSpace(CatalogSearchTerm);
    public bool HasActiveCatalogFocus => !IsFocusAll;
    public bool HasActiveSearchOrFocus => HasSearchTerm || HasActiveCatalogFocus;
    public bool HasCatalogItems => FilteredCatalog.Count > 0;
    public bool HasQuickAddTarget => !string.IsNullOrWhiteSpace(_quickAddItemId);

    public string CatalogSearchHelperText => string.IsNullOrWhiteSpace(CatalogSearchTerm)
        ? "Cari nama menu untuk mempercepat scan katalog."
        : $"Filter katalog aktif: \"{CatalogSearchTerm.Trim()}\"";

    public string CartTotalText => FormattingHelper.FormatCurrency(Cart.Sum(x => x.Price * x.Qty));
    public string CartHppTotalText => FormattingHelper.FormatCurrency(Cart.Sum(x => x.Hpp * x.Qty));
    public string CartProjectedProfitText => FormattingHelper.FormatCurrency(Cart.Sum(x => (x.Price - x.Hpp) * x.Qty));
    public int CartItemCount => Cart.Sum(x => x.Qty);
    public int CartLineCount => Cart.Count;
    public bool HasCart => Cart.Count > 0;
    public bool HasSelectedCartItem => SelectedCartItem is not null;

    public string CheckoutSummaryText => Cart.Count == 0
        ? "Keranjang kosong. Pilih item dari katalog untuk mulai transaksi."
        : $"{CartItemCount} item | HPP {CartHppTotalText} | proyeksi laba {CartProjectedProfitText}";

    public string SelectedCartItemHeaderText => SelectedCartItem is null
        ? "Belum ada item dipilih"
        : $"Custom pricing: {SelectedCartItem.Name}";

    public string SelectedCartItemGuideText => SelectedCartItem is null
        ? "Pilih item keranjang untuk mengubah harga jual sementara."
        : $"Harga dasar {FormattingHelper.FormatCurrency(SelectedCartItem.BasePrice)} | subtotal saat ini {SelectedCartItem.TotalText}.";

    public string CustomPriceInsightText
    {
        get
        {
            if (SelectedCartItem is null)
            {
                return "Pilih item dulu agar custom price bisa diterapkan.";
            }

            if (CustomPrice <= 0)
            {
                return "Masukkan harga custom di atas nol.";
            }

            var delta = CustomPrice - SelectedCartItem.BasePrice;
            if (delta == 0)
            {
                return "Harga custom sama dengan harga katalog.";
            }

            var direction = delta > 0 ? "naik" : "turun";
            return $"Harga custom {direction} {FormattingHelper.FormatCurrency(Math.Abs(delta))} dari harga katalog.";
        }
    }

    public bool CanApplyCustomPrice => SelectedCartItem is not null && CustomPrice > 0;
    public bool CanResetSelectedPrice => SelectedCartItem is not null && SelectedCartItem.Price != SelectedCartItem.BasePrice;

    partial void OnCatalogSearchTermChanged(string value) => RefreshCatalogView();
    partial void OnCatalogFocusModeChanged(string value) => RefreshCatalogView();

    partial void OnSelectedCartItemChanged(CartItemViewModel? value)
    {
        CustomPrice = value?.Price ?? 0;
        OnPropertyChanged(nameof(HasSelectedCartItem));
        OnPropertyChanged(nameof(SelectedCartItemHeaderText));
        OnPropertyChanged(nameof(SelectedCartItemGuideText));
        OnPropertyChanged(nameof(CustomPriceInsightText));
        OnPropertyChanged(nameof(CanApplyCustomPrice));
        OnPropertyChanged(nameof(CanResetSelectedPrice));
    }

    partial void OnCustomPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(CustomPriceInsightText));
        OnPropertyChanged(nameof(CanApplyCustomPrice));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var materials = DataService.Materials.Where(x => x.ProfileId == profileId).ToList();
        var recipes = DataService.Recipes.Where(x => x.ProfileId == profileId).ToList();
        var combos = DataService.Combos.Where(x => x.ProfileId == profileId).ToList();

        RecipeCatalog.Clear();
        foreach (var recipe in recipes.OrderBy(x => x.Name))
        {
            var hpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, materials);
            RecipeCatalog.Add(new PosCatalogItemViewModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Type = FocusRecipeValue,
                Hpp = hpp,
                BasePrice = CostCalculator.CalculateRecommendedSellingPrice(hpp, recipe.TargetMargin)
            });
        }

        ComboCatalog.Clear();
        foreach (var combo in combos.OrderBy(x => x.Name))
        {
            ComboCatalog.Add(new PosCatalogItemViewModel
            {
                Id = combo.Id,
                Name = combo.Name,
                Type = FocusComboValue,
                Hpp = CostCalculator.CalculateComboHpp(combo, recipes, materials),
                BasePrice = combo.SellingPrice
            });
        }

        RefreshCatalogView();
        RefreshCartSummary();
    }

    [RelayCommand]
    private void FocusAllCatalog()
    {
        CatalogFocusMode = FocusAllValue;
    }

    [RelayCommand]
    private void FocusRecipeCatalog()
    {
        CatalogFocusMode = FocusRecipeValue;
    }

    [RelayCommand]
    private void FocusComboCatalog()
    {
        CatalogFocusMode = FocusComboValue;
    }

    [RelayCommand]
    private void ClearCatalogFilter()
    {
        CatalogSearchTerm = string.Empty;
        CatalogFocusMode = FocusAllValue;
    }

    [RelayCommand]
    private void AddSuggestedItem()
    {
        if (string.IsNullOrWhiteSpace(_quickAddItemId))
        {
            Error("Tidak ada item katalog yang bisa diprioritaskan.");
            return;
        }

        var target = RecipeCatalog
            .Concat(ComboCatalog)
            .FirstOrDefault(x => x.Id == _quickAddItemId && x.Type == _quickAddItemType);

        if (target is null)
        {
            Error("Item quick add tidak ditemukan di katalog aktif.");
            return;
        }

        AddToCart(target);
    }

    [RelayCommand]
    private void AddToCart(PosCatalogItemViewModel item)
    {
        var existing = Cart.FirstOrDefault(x => x.Id == item.Id && x.Type == item.Type);
        if (existing is not null)
        {
            existing.Qty++;
            SelectedCartItem = existing;
            RefreshCartSummary();
            return;
        }

        var cartItem = new CartItemViewModel
        {
            Id = item.Id,
            Type = item.Type,
            Name = item.Name,
            Hpp = item.Hpp,
            BasePrice = item.BasePrice,
            Price = item.BasePrice,
            Qty = 1
        };

        cartItem.PropertyChanged += (_, _) =>
        {
            RefreshCartSummary();
            if (SelectedCartItem == cartItem)
            {
                OnPropertyChanged(nameof(SelectedCartItemGuideText));
                OnPropertyChanged(nameof(CanResetSelectedPrice));
            }
        };

        Cart.Add(cartItem);
        SelectedCartItem = cartItem;
        RefreshCartSummary();
    }

    [RelayCommand]
    private void IncreaseQty(CartItemViewModel item)
    {
        item.Qty++;
        RefreshCartSummary();
    }

    [RelayCommand]
    private void DecreaseQty(CartItemViewModel item)
    {
        item.Qty--;
        if (item.Qty <= 0)
        {
            Cart.Remove(item);
            if (SelectedCartItem == item)
            {
                SelectedCartItem = null;
            }
        }

        RefreshCartSummary();
    }

    [RelayCommand]
    private void RemoveFromCart(CartItemViewModel item)
    {
        Cart.Remove(item);
        if (SelectedCartItem == item)
        {
            SelectedCartItem = null;
        }

        RefreshCartSummary();
    }

    [RelayCommand]
    private void ClearCart()
    {
        if (Cart.Count == 0)
        {
            return;
        }

        Cart.Clear();
        SelectedCartItem = null;
        RefreshCartSummary();
        Success("Keranjang dikosongkan.");
    }

    [RelayCommand]
    private void ApplyCustomPrice()
    {
        if (SelectedCartItem is null || CustomPrice <= 0)
        {
            return;
        }

        SelectedCartItem.Price = CustomPrice;
        RefreshCartSummary();
        OnPropertyChanged(nameof(SelectedCartItemGuideText));
        OnPropertyChanged(nameof(CanResetSelectedPrice));
        Success($"Harga {SelectedCartItem.Name} diperbarui.");
    }

    [RelayCommand]
    private void ResetSelectedPrice()
    {
        if (SelectedCartItem is null)
        {
            return;
        }

        SelectedCartItem.Price = SelectedCartItem.BasePrice;
        CustomPrice = SelectedCartItem.BasePrice;
        RefreshCartSummary();
        OnPropertyChanged(nameof(SelectedCartItemGuideText));
        OnPropertyChanged(nameof(CanResetSelectedPrice));
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (Cart.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O");
        var profileId = DataService.Settings.ActiveProfileId;
        var sales = Cart.Select(item => new Sale
        {
            Id = Guid.NewGuid().ToString("N"),
            ItemName = item.Name,
            Qty = item.Qty,
            Type = item.Type,
            Date = now,
            TotalPrice = item.Price * item.Qty,
            TotalHpp = item.Hpp * item.Qty,
            TotalProfit = (item.Price * item.Qty) - (item.Hpp * item.Qty),
            ProfileId = profileId
        }).ToList();
        var totalAmount = Cart.Sum(item => item.Price * item.Qty);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "income",
            Category = PosIncomeCategory,
            Description = BuildPosTransactionDescription(),
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Amount = totalAmount,
            ProfileId = profileId
        };

        await DataService.SaveSalesAsync(sales);
        await DataService.SaveTransactionAsync(transaction);
        Cart.Clear();
        SelectedCartItem = null;
        Success("Transaksi POS berhasil dicatat.");
        RefreshCartSummary();
    }

    [RelayCommand]
    private void SelectCartItem(CartItemViewModel item)
    {
        SelectedCartItem = item;
    }

    private void RefreshCatalogView()
    {
        var allItems = RecipeCatalog
            .Concat(ComboCatalog)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filtered = allItems
            .Where(x => string.IsNullOrWhiteSpace(CatalogSearchTerm) || x.Name.Contains(CatalogSearchTerm, StringComparison.OrdinalIgnoreCase))
            .Where(x => CatalogFocusMode switch
            {
                FocusRecipeValue => x.Type == FocusRecipeValue,
                FocusComboValue => x.Type == FocusComboValue,
                _ => true
            })
            .ToList();

        FilteredCatalog.Clear();
        foreach (var item in filtered)
        {
            FilteredCatalog.Add(item);
        }

        FocusAllText = $"Semua {allItems.Count}";
        FocusRecipeText = $"Resep {allItems.Count(x => x.Type == FocusRecipeValue)}";
        FocusComboText = $"Bundle {allItems.Count(x => x.Type == FocusComboValue)}";
        CatalogSummaryText = $"{filtered.Count} item ditampilkan";
        CatalogFocusSummaryText = CatalogFocusMode switch
        {
            FocusRecipeValue => "Mode fokus pada resep satuan.",
            FocusComboValue => "Mode fokus pada paket bundling.",
            _ => "Menampilkan seluruh katalog POS."
        };

        var quickPick = filtered
            .OrderByDescending(x => x.BasePrice)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        _quickAddItemId = quickPick?.Id ?? string.Empty;
        _quickAddItemType = quickPick?.Type ?? string.Empty;
        QuickAddActionText = quickPick is null
            ? "Belum ada item untuk quick add"
            : $"Quick add: {quickPick.Name}";

        OnPropertyChanged(nameof(FocusAllText));
        OnPropertyChanged(nameof(FocusRecipeText));
        OnPropertyChanged(nameof(FocusComboText));
        OnPropertyChanged(nameof(CatalogSummaryText));
        OnPropertyChanged(nameof(CatalogFocusSummaryText));
        OnPropertyChanged(nameof(CatalogSearchHelperText));
        OnPropertyChanged(nameof(IsFocusAll));
        OnPropertyChanged(nameof(IsFocusRecipe));
        OnPropertyChanged(nameof(IsFocusCombo));
        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasActiveCatalogFocus));
        OnPropertyChanged(nameof(HasActiveSearchOrFocus));
        OnPropertyChanged(nameof(HasCatalogItems));
        OnPropertyChanged(nameof(HasQuickAddTarget));
        OnPropertyChanged(nameof(QuickAddActionText));
    }

    private void RefreshCartSummary()
    {
        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartHppTotalText));
        OnPropertyChanged(nameof(CartProjectedProfitText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(CartLineCount));
        OnPropertyChanged(nameof(HasCart));
        OnPropertyChanged(nameof(CheckoutSummaryText));
        OnPropertyChanged(nameof(HasSelectedCartItem));
        OnPropertyChanged(nameof(SelectedCartItemGuideText));
        OnPropertyChanged(nameof(CustomPriceInsightText));
        OnPropertyChanged(nameof(CanResetSelectedPrice));
    }

    private string BuildPosTransactionDescription()
    {
        var labels = Cart
            .Select(item => $"{item.Name} x{item.Qty}")
            .Take(3)
            .ToList();
        var suffix = Cart.Count > 3 ? ", ..." : string.Empty;

        return labels.Count == 0
            ? "Checkout POS"
            : $"Checkout POS: {string.Join(", ", labels)}{suffix}";
    }
}
