using System;
using System.Collections.Generic;

namespace HPPSystem.Models;

public class Material
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string CatalogLabel => string.IsNullOrWhiteSpace(Brand) ? Name : $"{Name} | {Brand}";
    public decimal Price { get; set; }
    public decimal Weight { get; set; }
    public string Unit { get; set; } = "gram";
    public decimal PricePerUnit { get; set; }
    public decimal Stock { get; set; }
    public bool IsTrackedInWarehouse { get; set; }
    public string ProfileId { get; set; } = "default";
    public List<PriceHistoryRecord> PriceHistory { get; set; } = new();
}

public class StockMovement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MaterialId { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public decimal QuantityDelta { get; set; }
    public decimal PreviousStock { get; set; }
    public decimal CurrentStock { get; set; }
    public string Direction { get; set; } = "adjustment";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Date { get; set; } = DateTime.UtcNow.ToString("O");
    public string ProfileId { get; set; } = "default";
}

public class MaterialStockAdjustment
{
    public string MaterialId { get; set; } = string.Empty;
    public decimal QuantityDelta { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal? UpdatedPrice { get; set; }
    public decimal? UpdatedWeight { get; set; }
}

public class PriceHistoryRecord
{
    public string Date { get; set; } = DateTime.UtcNow.ToString("O");
    public decimal Price { get; set; }
}

public class Recipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int Portions { get; set; } = 1;
    public List<IngredientGroup> IngredientGroups { get; set; } = new();
    public List<OverheadCost> OverheadCosts { get; set; } = new();
    public decimal TargetMargin { get; set; } = 40;
    public string ProfileId { get; set; } = "default";
}

public class ProductionOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RecipeId { get; set; } = string.Empty;
    public string RecipeName { get; set; } = string.Empty;
    public int PortionsPerBatch { get; set; } = 1;
    public int BatchCount { get; set; } = 1;
    public string ProductionDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Status { get; set; } = "pending";
    public string CompletedAt { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ProfileId { get; set; } = "default";
    public List<ProductionMaterialRequirement> Requirements { get; set; } = new();
}

public class ProductionMaterialRequirement
{
    public string MaterialId { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string MaterialBrand { get; set; } = string.Empty;
    public string MaterialLabel => string.IsNullOrWhiteSpace(MaterialBrand) ? MaterialName : $"{MaterialName} | {MaterialBrand}";
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityPerBatch { get; set; }
}

public class IngredientGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<Ingredient> Ingredients { get; set; } = new();
}

public class Ingredient
{
    public string MaterialId { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public class OverheadCost
{
    public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; }
}

public class Combo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<ComboRecipe> Recipes { get; set; } = new();
    public decimal SellingPrice { get; set; }
    public string ProfileId { get; set; } = "default";
}

public class ComboRecipe
{
    public string RecipeId { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
}

public class Sale
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Date { get; set; } = DateTime.UtcNow.ToString("O");
    public decimal TotalPrice { get; set; }
    public decimal TotalHpp { get; set; }
    public decimal TotalProfit { get; set; }
    public string ProfileId { get; set; } = "default";
}

public class Transaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "expense";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "Belanja Bahan";
    public string Description { get; set; } = string.Empty;
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public List<PurchasedItem> PurchasedItems { get; set; } = new();
    public string ProfileId { get; set; } = "default";
}

public class PurchasedItem
{
    public string MaterialId { get; set; } = string.Empty;
    public string CustomName { get; set; } = string.Empty;
    public decimal Qty { get; set; } = 1;
    public decimal Price { get; set; }
    public bool SyncToMaterialCatalog { get; set; }
    public decimal PreviousMaterialPrice { get; set; }
    public decimal PreviousMaterialWeight { get; set; }
    public decimal AppliedMaterialPrice { get; set; }
    public decimal AppliedMaterialWeight { get; set; }
}

public class Profile
{
    public string Id { get; set; } = "default";
    public string BusinessName { get; set; } = "Bisnis Utama";
    public string OwnerName { get; set; } = string.Empty;
}

public class AppSettings
{
    public bool IsAdvancedMode { get; set; }
    public bool IsDarkMode { get; set; }
    public string FontSizePreset { get; set; } = "normal";
    public string ActiveProfileId { get; set; } = "default";
}

public class HppDataSnapshot
{
    public string Version { get; set; } = "3.1";
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
    public List<Profile> Profiles { get; set; } = new();
    public List<Material> Materials { get; set; } = new();
    public List<StockMovement> StockMovements { get; set; } = new();
    public List<Recipe> Recipes { get; set; } = new();
    public List<ProductionOrder> ProductionOrders { get; set; } = new();
    public List<Combo> Combos { get; set; } = new();
    public List<Sale> Sales { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
