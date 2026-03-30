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
    public PosViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Point of Sale";

    public ObservableCollection<PosCatalogItemViewModel> RecipeCatalog { get; } = new();
    public ObservableCollection<PosCatalogItemViewModel> ComboCatalog { get; } = new();
    public ObservableCollection<CartItemViewModel> Cart { get; } = new();

    [ObservableProperty]
    private CartItemViewModel? _selectedCartItem;

    [ObservableProperty]
    private decimal _customPrice;

    public string CartTotalText => FormattingHelper.FormatCurrency(Cart.Sum(x => x.Price * x.Qty));
    public int CartItemCount => Cart.Sum(x => x.Qty);
    public bool HasCart => Cart.Count > 0;
    public bool HasSelectedCartItem => SelectedCartItem is not null;

    partial void OnSelectedCartItemChanged(CartItemViewModel? value)
    {
        CustomPrice = value?.Price ?? 0;
        OnPropertyChanged(nameof(HasSelectedCartItem));
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
                Type = "recipe",
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
                Type = "combo",
                Hpp = CostCalculator.CalculateComboHpp(combo, recipes, materials),
                BasePrice = combo.SellingPrice
            });
        }

        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(HasCart));
        OnPropertyChanged(nameof(HasSelectedCartItem));
    }

    [RelayCommand]
    private void AddToCart(PosCatalogItemViewModel item)
    {
        var existing = Cart.FirstOrDefault(x => x.Id == item.Id && x.Type == item.Type);
        if (existing is not null)
        {
            existing.Qty++;
            OnPropertyChanged(nameof(CartTotalText));
            OnPropertyChanged(nameof(CartItemCount));
            OnPropertyChanged(nameof(HasCart));
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

        cartItem.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CartTotalText));
        Cart.Add(cartItem);
        SelectedCartItem = cartItem;
        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(HasCart));
        OnPropertyChanged(nameof(HasSelectedCartItem));
    }

    [RelayCommand]
    private void IncreaseQty(CartItemViewModel item)
    {
        item.Qty++;
        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(HasCart));
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

        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(HasCart));
        OnPropertyChanged(nameof(HasSelectedCartItem));
    }

    [RelayCommand]
    private void ApplyCustomPrice()
    {
        if (SelectedCartItem is null || CustomPrice <= 0)
        {
            return;
        }

        SelectedCartItem.Price = CustomPrice;
        OnPropertyChanged(nameof(CartTotalText));
        Success($"Harga {SelectedCartItem.Name} diperbarui.");
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (Cart.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O");
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
            ProfileId = DataService.Settings.ActiveProfileId
        }).ToList();

        await DataService.SaveSalesAsync(sales);
        Cart.Clear();
        SelectedCartItem = null;
        Success("Transaksi POS berhasil dicatat.");
        OnPropertyChanged(nameof(CartTotalText));
        OnPropertyChanged(nameof(CartItemCount));
        OnPropertyChanged(nameof(HasCart));
        OnPropertyChanged(nameof(HasSelectedCartItem));
    }

    [RelayCommand]
    private void SelectCartItem(CartItemViewModel item)
    {
        SelectedCartItem = item;
    }
}
