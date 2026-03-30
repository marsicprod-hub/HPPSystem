using System.Collections.Generic;
using System.Linq;
using HPPSystem.Models;

namespace HPPSystem.Helpers;

public sealed class RecipeCostBreakdown
{
    public decimal MaterialCost { get; init; }
    public decimal OverheadCost { get; init; }
    public decimal TotalCost => MaterialCost + OverheadCost;
}

public static class CostCalculator
{
    public static RecipeCostBreakdown CalculateRecipeCost(Recipe? recipe, IEnumerable<HPPSystem.Models.Material> materials)
    {
        if (recipe is null)
        {
            return new RecipeCostBreakdown();
        }

        var materialMap = materials.ToDictionary(m => m.Id, m => m);
        decimal materialCost = 0;

        foreach (var group in recipe.IngredientGroups)
        {
            foreach (var ingredient in group.Ingredients)
            {
                if (materialMap.TryGetValue(ingredient.MaterialId, out var material))
                {
                    materialCost += material.PricePerUnit * ingredient.Quantity;
                }
            }
        }

        return new RecipeCostBreakdown
        {
            MaterialCost = materialCost,
            OverheadCost = recipe.OverheadCosts.Sum(x => x.Cost)
        };
    }

    public static decimal CalculateRecipeHppPerPortion(Recipe? recipe, IEnumerable<HPPSystem.Models.Material> materials)
    {
        if (recipe is null || recipe.Portions <= 0)
        {
            return 0;
        }

        return CalculateRecipeCost(recipe, materials).TotalCost / recipe.Portions;
    }

    public static decimal CalculateRecommendedSellingPrice(decimal hppPerPortion, decimal marginPercent)
    {
        if (marginPercent >= 100)
        {
            return hppPerPortion;
        }

        var ratio = 1 - (marginPercent / 100m);
        return ratio <= 0 ? hppPerPortion : hppPerPortion / ratio;
    }

    public static decimal CalculateComboHpp(Combo? combo, IEnumerable<Recipe> recipes, IEnumerable<HPPSystem.Models.Material> materials)
    {
        if (combo is null)
        {
            return 0;
        }

        var recipeMap = recipes.ToDictionary(x => x.Id, x => x);
        decimal total = 0;

        foreach (var comboRecipe in combo.Recipes)
        {
            if (recipeMap.TryGetValue(comboRecipe.RecipeId, out var recipe))
            {
                total += CalculateRecipeHppPerPortion(recipe, materials) * comboRecipe.Qty;
            }
        }

        return total;
    }
}
