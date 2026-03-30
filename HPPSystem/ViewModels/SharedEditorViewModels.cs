using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HPPSystem.Helpers;
using HPPSystem.Models;

namespace HPPSystem.ViewModels;

public sealed partial class IngredientEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _materialId = string.Empty;

    [ObservableProperty]
    private decimal _quantity;

    [ObservableProperty]
    private string _unitName = string.Empty;

    [ObservableProperty]
    private string _unitCostText = string.Empty;

    [ObservableProperty]
    private string _lineCostText = string.Empty;
}

public sealed partial class IngredientGroupEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "Kelompok Bahan";

    public ObservableCollection<IngredientEntryViewModel> Ingredients { get; } = new();
}

public sealed partial class OverheadEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private decimal _cost;
}

public sealed partial class ComboRecipeEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _recipeId = string.Empty;

    [ObservableProperty]
    private int _qty = 1;
}

public sealed partial class PurchasedItemEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _materialId = string.Empty;

    [ObservableProperty]
    private string _customName = string.Empty;

    [ObservableProperty]
    private decimal _qty = 1;

    [ObservableProperty]
    private decimal _price;
}

public sealed partial class RecipeEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _portions = 1;

    [ObservableProperty]
    private decimal _targetMargin = 40;

    public ObservableCollection<IngredientGroupEditorViewModel> Groups { get; } = new();
    public ObservableCollection<OverheadEntryViewModel> Overheads { get; } = new();

    public void LoadFrom(Recipe? recipe)
    {
        Id = recipe?.Id ?? string.Empty;
        Name = recipe?.Name ?? string.Empty;
        Portions = recipe?.Portions ?? 1;
        TargetMargin = recipe?.TargetMargin ?? 40;

        Groups.Clear();
        Overheads.Clear();

        if (recipe is null)
        {
            Groups.Add(new IngredientGroupEditorViewModel());
            return;
        }

        foreach (var group in recipe.IngredientGroups)
        {
            var vm = new IngredientGroupEditorViewModel { Name = group.Name };
            foreach (var ingredient in group.Ingredients)
            {
                vm.Ingredients.Add(new IngredientEntryViewModel
                {
                    MaterialId = ingredient.MaterialId,
                    Quantity = ingredient.Quantity
                });
            }

            Groups.Add(vm);
        }

        if (Groups.Count == 0)
        {
            Groups.Add(new IngredientGroupEditorViewModel());
        }

        foreach (var overhead in recipe.OverheadCosts)
        {
            Overheads.Add(new OverheadEntryViewModel
            {
                Name = overhead.Name,
                Cost = overhead.Cost
            });
        }
    }

    public Recipe ToRecipe(string profileId)
    {
        return new Recipe
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = Name.Trim(),
            Portions = Portions <= 0 ? 1 : Portions,
            TargetMargin = TargetMargin,
            ProfileId = profileId,
            IngredientGroups = Groups
                .Where(x => x.Ingredients.Any(i => !string.IsNullOrWhiteSpace(i.MaterialId) && i.Quantity > 0))
                .Select(x => new IngredientGroup
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(x.Name) ? "Kelompok Bahan" : x.Name.Trim(),
                    Ingredients = x.Ingredients
                        .Where(i => !string.IsNullOrWhiteSpace(i.MaterialId) && i.Quantity > 0)
                        .Select(i => new Ingredient
                        {
                            MaterialId = i.MaterialId,
                            Quantity = i.Quantity
                        })
                        .ToList()
                })
                .ToList(),
            OverheadCosts = Overheads
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Cost > 0)
                .Select(x => new OverheadCost
                {
                    Name = x.Name.Trim(),
                    Cost = x.Cost
                })
                .ToList()
        };
    }
}

public sealed partial class RecipeCardViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HppText { get; init; } = string.Empty;
    public string MaterialCostText { get; init; } = string.Empty;
    public string OverheadCostText { get; init; } = string.Empty;
    public string TotalCostText { get; init; } = string.Empty;
    public string RecommendedPriceText { get; init; } = string.Empty;
    public string MarginText { get; init; } = string.Empty;
    public string PortionsText { get; init; } = string.Empty;
    public string IngredientCountText { get; init; } = string.Empty;
    public string GroupCountText { get; init; } = string.Empty;
    public string OverheadCountText { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public Recipe Recipe { get; init; } = new();
}

public sealed partial class RecipeIngredientDetailViewModel : ObservableObject
{
    public string GroupName { get; init; } = string.Empty;
    public string MaterialName { get; init; } = string.Empty;
    public string QuantityText { get; init; } = string.Empty;
    public string UnitCostText { get; init; } = string.Empty;
    public string TotalCostText { get; init; } = string.Empty;
}

public sealed partial class RecipeOverheadDetailViewModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string CostText { get; init; } = string.Empty;
}

public sealed partial class ComboCardViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HppText { get; init; } = string.Empty;
    public string SellingPriceText { get; init; } = string.Empty;
    public string MarginText { get; init; } = string.Empty;
    public Combo Combo { get; init; } = new();
}

public sealed partial class SalesPointViewModel : ObservableObject
{
    public string Label { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string TotalText => FormattingHelper.FormatCurrency(Total);
    public double BarValue { get; init; }
    public string PercentText { get; init; } = "0%";
}

public sealed partial class SaleRowViewModel : ObservableObject
{
    public string ItemName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string TotalPriceText { get; init; } = string.Empty;
    public string TotalProfitText { get; init; } = string.Empty;
    public int Qty { get; init; }
    public string QtyText => $"{Qty}x";
    public string ProfitLabelText => $"Laba {TotalProfitText}";
}

public sealed partial class SimulationResultViewModel : ObservableObject
{
    public string RecipeName { get; init; } = string.Empty;
    public string CurrentHppText { get; init; } = string.Empty;
    public string SimulatedHppText { get; init; } = string.Empty;
    public string RemainingMarginText { get; init; } = string.Empty;
    public bool IsDanger { get; init; }
}

public sealed partial class PosCatalogItemViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal BasePrice { get; init; }
    public decimal Hpp { get; init; }
    public string DisplayPrice => FormattingHelper.FormatCurrency(BasePrice);
}

public sealed partial class CartItemViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Hpp { get; init; }
    public decimal BasePrice { get; init; }

    [ObservableProperty]
    private int _qty = 1;

    [ObservableProperty]
    private decimal _price;

    public string TotalText => FormattingHelper.FormatCurrency(Price * Qty);
}
