using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;
using Xunit;

namespace HPPSystem.Tests;

public sealed class LocalJsonDataServiceTests
{
    [Fact]
    public async Task ImportSnapshotAsync_ReplacesExistingState()
    {
        using var sandbox = new TempSandbox();
        var storePath = Path.Combine(sandbox.Path, "store.json");
        var importPath = Path.Combine(sandbox.Path, "import.json");

        var service = new LocalJsonDataService(storePath);
        await service.LoadDataAsync();

        await service.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "legacy-material",
            Name = "Legacy",
            Price = 1000,
            Weight = 100,
            Unit = "gram",
            Stock = 9,
            ProfileId = "default"
        });

        var snapshot = new HppDataSnapshot
        {
            Profiles =
            [
                new Profile
                {
                    Id = "imported",
                    BusinessName = "Imported Workspace",
                    OwnerName = "Owner Imported"
                }
            ],
            Materials =
            [
                new HPPSystem.Models.Material
                {
                    Id = "fresh-material",
                    Name = "Fresh",
                    Price = 8000,
                    Weight = 500,
                    Unit = "gram",
                    Stock = 20,
                    ProfileId = "imported"
                }
            ],
            Recipes = [],
            Combos = [],
            Sales = [],
            Transactions = [],
            Settings = new AppSettings
            {
                ActiveProfileId = "imported",
                IsAdvancedMode = false,
                IsDarkMode = true,
                FontSizePreset = FontSizingHelper.ExtraSmall
            }
        };

        await File.WriteAllTextAsync(importPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

        await service.ImportSnapshotAsync(importPath);

        Assert.Single(service.Profiles);
        Assert.Single(service.Materials);
        Assert.Equal("imported", service.Settings.ActiveProfileId);
        Assert.Equal("Fresh", service.Materials[0].Name);
        Assert.Equal(FontSizingHelper.ExtraSmall, service.Settings.FontSizePreset);
        Assert.DoesNotContain(service.Materials, x => x.Id == "legacy-material");
    }

    [Fact]
    public async Task DeleteRecipeAsync_RemovesRecipeReferencesAndDeletesEmptyCombos()
    {
        using var sandbox = new TempSandbox();
        var service = new LocalJsonDataService(Path.Combine(sandbox.Path, "store.json"));
        await service.LoadDataAsync();

        var recipeA = new Recipe
        {
            Id = "recipe-a",
            Name = "Recipe A",
            ProfileId = "default",
            Portions = 1,
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients = []
                }
            ]
        };
        var recipeB = new Recipe
        {
            Id = "recipe-b",
            Name = "Recipe B",
            ProfileId = "default",
            Portions = 1,
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients = []
                }
            ]
        };

        await service.SaveRecipeAsync(recipeA);
        await service.SaveRecipeAsync(recipeB);
        await service.SaveComboAsync(new Combo
        {
            Id = "combo-mixed",
            Name = "Combo Mixed",
            ProfileId = "default",
            SellingPrice = 20000,
            Recipes =
            [
                new ComboRecipe { RecipeId = "recipe-a", Qty = 1 },
                new ComboRecipe { RecipeId = "recipe-b", Qty = 1 }
            ]
        });
        await service.SaveComboAsync(new Combo
        {
            Id = "combo-empty",
            Name = "Combo Empty",
            ProfileId = "default",
            SellingPrice = 10000,
            Recipes =
            [
                new ComboRecipe { RecipeId = "recipe-a", Qty = 1 }
            ]
        });

        await service.DeleteRecipeAsync("recipe-a");

        var mixedCombo = Assert.Single(service.Combos);
        Assert.Equal("combo-mixed", mixedCombo.Id);
        Assert.Single(mixedCombo.Recipes);
        Assert.Equal("recipe-b", mixedCombo.Recipes[0].RecipeId);
        Assert.DoesNotContain(service.Recipes, x => x.Id == "recipe-a");
    }

    [Fact]
    public async Task DeleteMaterialAsync_RemovesIngredientReferencesAndEmptyGroups()
    {
        using var sandbox = new TempSandbox();
        var service = new LocalJsonDataService(Path.Combine(sandbox.Path, "store.json"));
        await service.LoadDataAsync();

        var keepMaterial = new HPPSystem.Models.Material
        {
            Id = "keep-material",
            Name = "Keep",
            Price = 5000,
            Weight = 500,
            Unit = "gram",
            Stock = 10,
            ProfileId = "default"
        };
        var removeMaterial = new HPPSystem.Models.Material
        {
            Id = "remove-material",
            Name = "Remove",
            Price = 7000,
            Weight = 500,
            Unit = "gram",
            Stock = 10,
            ProfileId = "default"
        };

        await service.SaveMaterialAsync(keepMaterial);
        await service.SaveMaterialAsync(removeMaterial);
        await service.SaveRecipeAsync(new Recipe
        {
            Id = "recipe-target",
            Name = "Recipe Target",
            ProfileId = "default",
            Portions = 1,
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Id = "group-remove",
                    Name = "Remove Group",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "remove-material", Quantity = 10 }
                    ]
                },
                new IngredientGroup
                {
                    Id = "group-keep",
                    Name = "Keep Group",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "keep-material", Quantity = 5 }
                    ]
                }
            ]
        });

        await service.DeleteMaterialAsync("remove-material");

        var recipe = Assert.Single(service.Recipes, x => x.Id == "recipe-target");
        Assert.Single(recipe.IngredientGroups);
        Assert.Equal("group-keep", recipe.IngredientGroups[0].Id);
        Assert.DoesNotContain(recipe.IngredientGroups.SelectMany(x => x.Ingredients), x => x.MaterialId == "remove-material");
    }

    [Fact]
    public async Task SaveMaterialAsync_UpdatesPricePerUnit_AndAppendsPriceHistoryOnPriceChange()
    {
        using var sandbox = new TempSandbox();
        var service = new LocalJsonDataService(Path.Combine(sandbox.Path, "store.json"));
        await service.LoadDataAsync();

        var material = new HPPSystem.Models.Material
        {
            Id = "material-1",
            Name = "Sugar",
            Price = 10000,
            Weight = 500,
            Unit = "gram",
            Stock = 3,
            ProfileId = "default"
        };

        await service.SaveMaterialAsync(material);

        await service.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = material.Id,
            Name = material.Name,
            Price = 12500,
            Weight = material.Weight,
            Unit = material.Unit,
            Stock = material.Stock,
            ProfileId = material.ProfileId
        });

        var saved = Assert.Single(service.Materials, x => x.Id == "material-1");
        Assert.Equal(25, saved.PricePerUnit);
        var history = Assert.Single(saved.PriceHistory);
        Assert.Equal(10000, history.Price);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileScopedData_AndSwitchesActiveProfile()
    {
        using var sandbox = new TempSandbox();
        var service = new LocalJsonDataService(Path.Combine(sandbox.Path, "store.json"));
        await service.LoadDataAsync();

        await service.SaveProfileAsync(new Profile
        {
            Id = "branch-2",
            BusinessName = "Branch Two",
            OwnerName = "Owner Two"
        });

        await service.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "default-material",
            Name = "Default Material",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 5,
            ProfileId = "default"
        });
        await service.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "branch-material",
            Name = "Branch Material",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            Stock = 7,
            ProfileId = "branch-2"
        });
        await service.SaveRecipeAsync(new Recipe
        {
            Id = "default-recipe",
            Name = "Default Recipe",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups = []
        });
        await service.SaveRecipeAsync(new Recipe
        {
            Id = "branch-recipe",
            Name = "Branch Recipe",
            Portions = 1,
            ProfileId = "branch-2",
            IngredientGroups = []
        });
        await service.SaveComboAsync(new Combo
        {
            Id = "default-combo",
            Name = "Default Combo",
            ProfileId = "default",
            SellingPrice = 15000,
            Recipes = []
        });
        await service.SaveComboAsync(new Combo
        {
            Id = "branch-combo",
            Name = "Branch Combo",
            ProfileId = "branch-2",
            SellingPrice = 16000,
            Recipes = []
        });
        await service.SaveSaleAsync(new Sale
        {
            Id = "default-sale",
            ItemName = "Default Sale",
            Qty = 1,
            Type = "recipe",
            TotalPrice = 1000,
            TotalHpp = 500,
            TotalProfit = 500,
            ProfileId = "default"
        });
        await service.SaveSaleAsync(new Sale
        {
            Id = "branch-sale",
            ItemName = "Branch Sale",
            Qty = 1,
            Type = "recipe",
            TotalPrice = 1200,
            TotalHpp = 600,
            TotalProfit = 600,
            ProfileId = "branch-2"
        });
        await service.SaveTransactionAsync(new Transaction
        {
            Id = "default-transaction",
            Description = "Default Transaction",
            Amount = 2000,
            ProfileId = "default"
        });
        await service.SaveTransactionAsync(new Transaction
        {
            Id = "branch-transaction",
            Description = "Branch Transaction",
            Amount = 2500,
            ProfileId = "branch-2"
        });

        await service.DeleteProfileAsync("default");

        var profile = Assert.Single(service.Profiles);
        Assert.Equal("branch-2", profile.Id);
        Assert.Equal("branch-2", service.Settings.ActiveProfileId);
        Assert.Single(service.Materials);
        Assert.All(service.Materials, x => Assert.Equal("branch-2", x.ProfileId));
        Assert.Single(service.Recipes);
        Assert.All(service.Recipes, x => Assert.Equal("branch-2", x.ProfileId));
        Assert.Single(service.Combos);
        Assert.All(service.Combos, x => Assert.Equal("branch-2", x.ProfileId));
        Assert.Single(service.Sales);
        Assert.All(service.Sales, x => Assert.Equal("branch-2", x.ProfileId));
        Assert.Single(service.Transactions);
        Assert.All(service.Transactions, x => Assert.Equal("branch-2", x.ProfileId));
    }

    [Fact]
    public async Task ApplyMaterialStockAdjustmentAsync_UpdatesStockPriceAndCreatesLedger()
    {
        using var sandbox = new TempSandbox();
        var service = new LocalJsonDataService(Path.Combine(sandbox.Path, "store.json"));
        await service.LoadDataAsync();

        await service.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "material-ledger",
            Name = "Milk",
            Price = 10000,
            Weight = 500,
            Unit = "ml",
            Stock = 5,
            ProfileId = "default"
        });

        await service.ApplyMaterialStockAdjustmentAsync(new MaterialStockAdjustment
        {
            MaterialId = "material-ledger",
            QuantityDelta = 3,
            SourceType = "purchase",
            SourceId = "trx-1",
            SourceLabel = "Restock Milk",
            Notes = "Belanja bahan menambah stok dan sinkron harga.",
            UpdatedPrice = 15000,
            UpdatedWeight = 750
        });

        var material = Assert.Single(service.Materials, x => x.Id == "material-ledger");
        Assert.Equal(8, material.Stock);
        Assert.Equal(15000, material.Price);
        Assert.Equal(750, material.Weight);
        Assert.Equal(20, material.PricePerUnit);
        Assert.Single(material.PriceHistory);
        var movement = Assert.Single(service.StockMovements, x => x.SourceType == "purchase");
        Assert.Equal(3, movement.QuantityDelta);
        Assert.Equal(5, movement.PreviousStock);
        Assert.Equal(8, movement.CurrentStock);
    }

    private sealed class TempSandbox : IDisposable
    {
        public TempSandbox()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hppsystem-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
