using System;
using System.Linq;
using System.Threading.Tasks;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;
using HPPSystem.ViewModels;
using Xunit;

namespace HPPSystem.Tests;

public sealed class ViewModelBehaviorTests
{
    [Fact]
    public async Task SettingsToggleAdvanced_PreservesDarkMode()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var viewModel = new SettingsViewModel(dataService, notifications);
        await dataService.UpdateSettingsAsync(new AppSettings
        {
            ActiveProfileId = "default",
            IsAdvancedMode = true,
            IsDarkMode = true,
            FontSizePreset = FontSizingHelper.Large
        });

        await viewModel.ToggleAdvancedCommand.ExecuteAsync(null);

        Assert.False(dataService.Settings.IsAdvancedMode);
        Assert.True(dataService.Settings.IsDarkMode);
        Assert.Equal(FontSizingHelper.Large, dataService.Settings.FontSizePreset);
    }

    [Fact]
    public async Task SettingsFontSizePreset_UpdatesStoredPreference()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var viewModel = new SettingsViewModel(dataService, notifications);

        viewModel.FontSizePresetIndex = 4;
        await Task.Delay(10);

        Assert.Equal(FontSizingHelper.ExtraLarge, dataService.Settings.FontSizePreset);
        Assert.Equal("Extra Large", viewModel.FontSizePresetLabel);
    }

    [Fact]
    public async Task SettingsClearAllData_RemovesOperationalData_AndKeepsUiPreferences()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        dataService.Profiles.Add(new Profile
        {
            Id = "branch-1",
            BusinessName = "Cabang Lama",
            OwnerName = "Hazel"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            ProfileId = "branch-1"
        });
        dataService.StockMovements.Add(new StockMovement
        {
            Id = "mov-1",
            MaterialId = "mat-1",
            MaterialName = "Flour",
            QuantityDelta = 10,
            PreviousStock = 0,
            CurrentStock = 10,
            ProfileId = "branch-1"
        });
        dataService.Recipes.Add(new Recipe { Id = "recipe-1", Name = "Cake", ProfileId = "branch-1" });
        dataService.ProductionOrders.Add(new ProductionOrder { Id = "prod-1", RecipeName = "Cake", ProfileId = "branch-1" });
        dataService.Combos.Add(new Combo { Id = "combo-1", Name = "Bundle", ProfileId = "branch-1" });
        dataService.Sales.Add(new Sale { Id = "sale-1", ItemName = "Cake", ProfileId = "branch-1" });
        dataService.Transactions.Add(new Transaction { Id = "trx-1", Description = "Belanja", ProfileId = "branch-1" });
        await dataService.UpdateSettingsAsync(new AppSettings
        {
            ActiveProfileId = "branch-1",
            IsAdvancedMode = false,
            IsDarkMode = false,
            FontSizePreset = FontSizingHelper.Small
        });

        var viewModel = new SettingsViewModel(dataService, notifications);

        await viewModel.ClearAllDataCommand.ExecuteAsync(null);

        Assert.Empty(dataService.Materials);
        Assert.Empty(dataService.StockMovements);
        Assert.Empty(dataService.Recipes);
        Assert.Empty(dataService.ProductionOrders);
        Assert.Empty(dataService.Combos);
        Assert.Empty(dataService.Sales);
        Assert.Empty(dataService.Transactions);
        var profile = Assert.Single(dataService.Profiles);
        Assert.Equal("default", profile.Id);
        Assert.Equal("Bisnis Utama", profile.BusinessName);
        Assert.False(dataService.Settings.IsAdvancedMode);
        Assert.False(dataService.Settings.IsDarkMode);
        Assert.Equal(FontSizingHelper.Small, dataService.Settings.FontSizePreset);
        Assert.Equal("default", dataService.Settings.ActiveProfileId);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("Backup otomatis dibuat"));
        Assert.Contains("hppsystem-backup-before-clear-", dataService.LastExportPath);
        Assert.EndsWith(".json", dataService.LastExportPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BookkeepingSave_SyncsLatestPurchaseToCatalog_AndCreatesLedger()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 10,
            ProfileId = "default"
        });

        var viewModel = new BookkeepingViewModel(dataService, notifications)
        {
            FormDescription = "Restock Flour"
        };
        viewModel.AddPurchasedItemCommand.Execute(null);
        var item = Assert.Single(viewModel.PurchasedItems);
        item.MaterialId = "mat-1";
        item.Qty = 500;
        item.Price = 12000;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var material = Assert.Single(dataService.Materials, x => x.Id == "mat-1");
        Assert.Equal(510, material.Stock);
        Assert.Equal(12000, material.Price);
        Assert.Equal(500, material.Weight);
        Assert.Equal(24, material.PricePerUnit);
        Assert.Single(material.PriceHistory);
        var transaction = Assert.Single(dataService.Transactions);
        var purchased = Assert.Single(transaction.PurchasedItems);
        Assert.True(purchased.SyncToMaterialCatalog);
        Assert.Equal(10000, purchased.PreviousMaterialPrice);
        Assert.Equal(1000, purchased.PreviousMaterialWeight);
        Assert.Equal(12000, purchased.AppliedMaterialPrice);
        Assert.Equal(500, purchased.AppliedMaterialWeight);
        Assert.Contains(dataService.StockMovements, x => x.SourceType == "purchase" && x.QuantityDelta == 500);
    }

    [Fact]
    public async Task BookkeepingDelete_RollsBackPurchasedStock_WhenInventoryStillAvailable()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var material = new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 10,
            ProfileId = "default"
        };
        var transaction = new Transaction
        {
            Id = "trx-1",
            Type = "expense",
            Category = "Belanja Bahan",
            Description = "Restock Flour",
            Amount = 5000,
            ProfileId = "default",
            PurchasedItems =
            [
                new PurchasedItem
                {
                    MaterialId = "mat-1",
                    CustomName = "Flour",
                    Qty = 4,
                    Price = 5000
                }
            ]
        };

        dataService.Materials.Add(material);
        dataService.Transactions.Add(transaction);

        var viewModel = new BookkeepingViewModel(dataService, notifications);

        await viewModel.DeleteCommand.ExecuteAsync(transaction);

        Assert.Equal(6, Assert.Single(dataService.Materials, x => x.Id == "mat-1").Stock);
        Assert.Empty(dataService.Transactions);
        Assert.Contains(notifications.Toasts, x => !x.IsError);
    }

    [Fact]
    public async Task BookkeepingDelete_Blocks_WhenPurchasedStockAlreadyConsumed()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var material = new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 3,
            ProfileId = "default"
        };
        var transaction = new Transaction
        {
            Id = "trx-1",
            Type = "expense",
            Category = "Belanja Bahan",
            Description = "Restock Flour",
            Amount = 5000,
            ProfileId = "default",
            PurchasedItems =
            [
                new PurchasedItem
                {
                    MaterialId = "mat-1",
                    CustomName = "Flour",
                    Qty = 4,
                    Price = 5000
                }
            ]
        };

        dataService.Materials.Add(material);
        dataService.Transactions.Add(transaction);

        var viewModel = new BookkeepingViewModel(dataService, notifications);

        await viewModel.DeleteCommand.ExecuteAsync(transaction);

        Assert.Equal(3, Assert.Single(dataService.Materials, x => x.Id == "mat-1").Stock);
        Assert.Single(dataService.Transactions);
        Assert.Contains(notifications.Toasts, x => x.IsError && x.Message.Contains("tidak bisa dihapus"));
    }

    [Fact]
    public async Task Bookkeeping_ProductionShortageTemplate_RoundsUpToFullPack()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Brand = "Anchor",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 750,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        await dataService.SaveProductionOrderAsync(new ProductionOrder
        {
            Id = "prod-1",
            RecipeId = "recipe-1",
            RecipeName = "Cake",
            BatchCount = 1,
            PortionsPerBatch = 1,
            ProductionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Status = "pending",
            ProfileId = "default",
            Requirements =
            [
                new ProductionMaterialRequirement
                {
                    MaterialId = "mat-1",
                    MaterialName = "Flour",
                    MaterialBrand = "Anchor",
                    Unit = "gram",
                    QuantityPerBatch = 1000
                }
            ]
        });

        var viewModel = new BookkeepingViewModel(dataService, notifications);

        var recommendation = Assert.Single(viewModel.ProductionShortageRecommendations);
        Assert.Equal(250, recommendation.MissingQuantityValue);
        Assert.Equal(1000, recommendation.RecommendedQuantityValue);
        Assert.Equal(10000, recommendation.RecommendedPriceValue);

        viewModel.ApplyProductionShortageTemplateCommand.Execute(null);

        Assert.True(viewModel.IsBelanjaBahan);
        var item = Assert.Single(viewModel.PurchasedItems);
        Assert.Equal("mat-1", item.MaterialId);
        Assert.Equal(1000, item.Qty);
        Assert.Equal(10000, item.Price);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("shortage produksi"));
    }

    [Fact]
    public async Task Bookkeeping_ProductionShortageTemplate_UsesSmartVariantMixByNameAndBrand()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "milk-1000",
            Name = "Susu Cair",
            Brand = "Ultra",
            Price = 22000,
            Weight = 1000,
            Unit = "ml",
            Stock = 0,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "milk-250",
            Name = "Susu Cair",
            Brand = "Ultra",
            Price = 7000,
            Weight = 250,
            Unit = "ml",
            Stock = 0,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        await dataService.SaveProductionOrderAsync(new ProductionOrder
        {
            Id = "prod-1",
            RecipeId = "recipe-1",
            RecipeName = "Milk Tea",
            BatchCount = 1,
            PortionsPerBatch = 1,
            ProductionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Status = "pending",
            ProfileId = "default",
            Requirements =
            [
                new ProductionMaterialRequirement
                {
                    MaterialId = "milk-1000",
                    MaterialName = "Susu Cair",
                    MaterialBrand = "Ultra",
                    Unit = "ml",
                    QuantityPerBatch = 1125
                }
            ]
        });

        var viewModel = new BookkeepingViewModel(dataService, notifications);

        var recommendation = Assert.Single(viewModel.ProductionShortageRecommendations);
        Assert.Equal(1125, recommendation.MissingQuantityValue);
        Assert.Equal(1250, recommendation.RecommendedQuantityValue);
        Assert.Contains("1x 1000", recommendation.RecommendedMixText);
        Assert.Contains("1x 250", recommendation.RecommendedMixText);

        viewModel.ApplyProductionShortageTemplateCommand.Execute(null);

        Assert.Equal(2, viewModel.PurchasedItems.Count);
        Assert.Contains(viewModel.PurchasedItems, x => x.MaterialId == "milk-1000" && x.Qty == 1000 && x.Price == 22000);
        Assert.Contains(viewModel.PurchasedItems, x => x.MaterialId == "milk-250" && x.Qty == 250 && x.Price == 7000);
    }

    [Fact]
    public async Task Bookkeeping_PrepareShortageTemplateFromProductionOrder_FocusesSelectedOrderOnly()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Sugar",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        await dataService.SaveProductionOrderAsync(new ProductionOrder
        {
            Id = "prod-1",
            RecipeId = "recipe-1",
            RecipeName = "Cake",
            BatchCount = 1,
            PortionsPerBatch = 1,
            ProductionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Status = "pending",
            ProfileId = "default",
            Requirements = [ new ProductionMaterialRequirement { MaterialId = "mat-1", MaterialName = "Flour", Unit = "gram", QuantityPerBatch = 200 } ]
        });
        await dataService.SaveProductionOrderAsync(new ProductionOrder
        {
            Id = "prod-2",
            RecipeId = "recipe-2",
            RecipeName = "Cookie",
            BatchCount = 1,
            PortionsPerBatch = 1,
            ProductionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Status = "pending",
            ProfileId = "default",
            Requirements = [ new ProductionMaterialRequirement { MaterialId = "mat-2", MaterialName = "Sugar", Unit = "gram", QuantityPerBatch = 300 } ]
        });

        var viewModel = new BookkeepingViewModel(dataService, notifications);

        viewModel.PrepareShortageTemplateFromProductionOrder("prod-2");

        var item = Assert.Single(viewModel.PurchasedItems);
        Assert.Equal("mat-2", item.MaterialId);
        Assert.DoesNotContain(viewModel.PurchasedItems, x => x.MaterialId == "mat-1");
        Assert.Contains("Cookie", viewModel.ProductionShortageHeadlineText);
    }

    [Fact]
    public async Task ProductionOrder_StaysPending_WhenStockInsufficient()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 5,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 10 }
                    ]
                }
            ]
        });

        var viewModel = new ProductionViewModel(dataService, notifications);
        viewModel.SelectedRecipeOption = Assert.Single(viewModel.RecipeOptions);
        viewModel.DraftBatchCount = 1;
        await viewModel.CreateOrderCommand.ExecuteAsync(null);

        var order = Assert.Single(dataService.ProductionOrders);
        await viewModel.ConfirmOrderCommand.ExecuteAsync(Assert.Single(viewModel.ProductionQueue));

        Assert.Equal(5, dataService.Materials[0].Stock);
        Assert.Equal("pending", order.Status);
        Assert.Single(dataService.ProductionOrders);
        Assert.Contains(notifications.Toasts, x => x.IsError && x.Message.Contains("belum bisa dikonfirmasi"));
    }

    [Fact]
    public async Task ProductionOrder_Completes_AndReducesStock_WhenStockIsSufficient()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 25,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 10 }
                    ]
                }
            ]
        });

        var viewModel = new ProductionViewModel(dataService, notifications);
        viewModel.SelectedRecipeOption = Assert.Single(viewModel.RecipeOptions);
        viewModel.DraftBatchCount = 2;
        await viewModel.CreateOrderCommand.ExecuteAsync(null);
        await viewModel.ConfirmOrderCommand.ExecuteAsync(Assert.Single(viewModel.ProductionQueue));

        Assert.Equal(5, dataService.Materials[0].Stock);
        Assert.Single(dataService.ProductionOrders);
        Assert.Equal("completed", dataService.ProductionOrders[0].Status);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("stok gudang"));
    }

    [Fact]
    public async Task ProductionOrder_CanUseCombinedFamilyStockAcrossPackVariants()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "milk-1000",
            Name = "Susu Cair",
            Brand = "Ultra",
            Price = 22000,
            Weight = 1000,
            Unit = "ml",
            Stock = 1000,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "milk-250",
            Name = "Susu Cair",
            Brand = "Ultra",
            Price = 7000,
            Weight = 250,
            Unit = "ml",
            Stock = 250,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Milk Tea",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Base",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "milk-1000", Quantity = 1125 }
                    ]
                }
            ]
        });

        var viewModel = new ProductionViewModel(dataService, notifications);
        viewModel.SelectedRecipeOption = Assert.Single(viewModel.RecipeOptions);
        await viewModel.CreateOrderCommand.ExecuteAsync(null);
        await viewModel.ConfirmOrderCommand.ExecuteAsync(Assert.Single(viewModel.ProductionQueue));

        Assert.Equal(0, dataService.Materials.Single(x => x.Id == "milk-1000").Stock);
        Assert.Equal(125, dataService.Materials.Single(x => x.Id == "milk-250").Stock);
        Assert.Equal("completed", Assert.Single(dataService.ProductionOrders).Status);
    }

    [Fact]
    public void RecipesBuilder_ListsAllCatalogMaterials_AndShowsWarehouseGuidance()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Sugar",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var viewModel = new RecipesViewModel(dataService, notifications);

        Assert.Equal(2, viewModel.BuilderMaterials.Count);
        Assert.Contains(viewModel.BuilderMaterials, x => x.Name == "Flour");
        Assert.Contains(viewModel.BuilderMaterials, x => x.Name == "Sugar");
        Assert.Contains("2 material katalog", viewModel.EditorMaterialScopeText);
        Assert.Contains("Ikon ! merah", viewModel.EditorMaterialScopeText);
    }

    [Fact]
    public async Task RecipeSave_AllowsCatalogMaterialOutsideWarehouse_AndMarksWarning()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Legacy Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var viewModel = new RecipesViewModel(dataService, notifications);
        viewModel.StartCreateCommand.Execute(null);
        viewModel.Editor.Name = "Cake";
        viewModel.AddIngredientCommand.Execute(viewModel.Editor.Groups[0]);
        viewModel.Editor.Groups[0].Ingredients[0].MaterialId = "mat-1";
        viewModel.Editor.Groups[0].Ingredients[0].Quantity = 100;

        Assert.True(viewModel.Editor.Groups[0].Ingredients[0].HasWarehouseWarning);
        Assert.Contains("belum aktif di Gudang", viewModel.Editor.Groups[0].Ingredients[0].WarehouseWarningText);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(dataService.Recipes);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("disimpan"));
    }

    [Fact]
    public async Task PosCheckout_UsesCustomPrice_AndPersistsSaleTotals()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var material = new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 100,
            ProfileId = "default"
        };
        await dataService.SaveMaterialAsync(material);
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            TargetMargin = 50,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 100 }
                    ]
                }
            ]
        });

        var viewModel = new PosViewModel(dataService, notifications);
        var catalogItem = Assert.Single(viewModel.RecipeCatalog);

        viewModel.AddToCartCommand.Execute(catalogItem);
        var cartItem = Assert.Single(viewModel.Cart);
        viewModel.SelectCartItemCommand.Execute(cartItem);
        viewModel.CustomPrice = 2500;
        viewModel.ApplyCustomPriceCommand.Execute(null);
        viewModel.IncreaseQtyCommand.Execute(cartItem);

        await viewModel.CheckoutCommand.ExecuteAsync(null);

        var sale = Assert.Single(dataService.Sales);
        var transaction = Assert.Single(dataService.Transactions);
        Assert.Equal("Cake", sale.ItemName);
        Assert.Equal(2, sale.Qty);
        Assert.Equal(5000, sale.TotalPrice);
        Assert.Equal(2000, sale.TotalHpp);
        Assert.Equal(3000, sale.TotalProfit);
        Assert.Equal("income", transaction.Type);
        Assert.Equal("Penjualan POS", transaction.Category);
        Assert.Equal(5000, transaction.Amount);
        Assert.Contains("Checkout POS", transaction.Description);
        Assert.Empty(viewModel.Cart);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("POS"));
    }

    [Fact]
    public async Task BookkeepingTotals_DoNotDoubleCount_AutoGeneratedPosTransactions()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 100,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            TargetMargin = 50,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 100 }
                    ]
                }
            ]
        });

        var pos = new PosViewModel(dataService, notifications);
        pos.AddToCartCommand.Execute(Assert.Single(pos.RecipeCatalog));
        await pos.CheckoutCommand.ExecuteAsync(null);

        var bookkeeping = new BookkeepingViewModel(dataService, notifications);

        Assert.Equal("Rp2.000", bookkeeping.TotalIncomeText);
        Assert.Single(bookkeeping.FilteredTransactionCards);
        Assert.Equal("Penjualan POS", bookkeeping.FilteredTransactionCards[0].CategoryText);
    }

    [Fact]
    public async Task PosAddToCart_IncrementsExistingLine_ForSameCatalogItem()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 100,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            TargetMargin = 50,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 100 }
                    ]
                }
            ]
        });

        var viewModel = new PosViewModel(dataService, notifications);
        var catalogItem = Assert.Single(viewModel.RecipeCatalog);

        viewModel.AddToCartCommand.Execute(catalogItem);
        viewModel.AddToCartCommand.Execute(catalogItem);

        var cartItem = Assert.Single(viewModel.Cart);
        Assert.Equal(2, cartItem.Qty);
        Assert.Single(viewModel.Cart);
    }

    [Fact]
    public async Task CombosRefresh_ComputesSummaryMetrics()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 100,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 100 }
                    ]
                }
            ]
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-2",
            Name = "Cookie",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 80 }
                    ]
                }
            ]
        });
        dataService.Combos.Add(new Combo
        {
            Id = "combo-1",
            Name = "Bundle Prime",
            ProfileId = "default",
            SellingPrice = 2000,
            Recipes =
            [
                new ComboRecipe { RecipeId = "recipe-1", Qty = 1 }
            ]
        });
        dataService.Combos.Add(new Combo
        {
            Id = "combo-2",
            Name = "Bundle Saver",
            ProfileId = "default",
            SellingPrice = 1000,
            Recipes =
            [
                new ComboRecipe { RecipeId = "recipe-2", Qty = 1 }
            ]
        });

        var viewModel = new CombosViewModel(dataService, notifications);

        Assert.Equal("2 paket", viewModel.ComboCountText);
        Assert.Equal("35%", viewModel.AverageMarginText);
        Assert.Equal("Bundle Prime", viewModel.BestComboNameText);
        Assert.Equal("50%", viewModel.BestComboMarginText);
        Assert.Contains("Bundle Prime", viewModel.PortfolioInsightText);
    }

    [Fact]
    public void RecipesLibraryMode_CanSwitchBetweenCardAndTable()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var viewModel = new RecipesViewModel(dataService, notifications);

        Assert.False(viewModel.IsLibraryTableMode);
        Assert.True(viewModel.IsLibraryCardMode);

        viewModel.SetLibraryTableModeCommand.Execute(null);

        Assert.True(viewModel.IsLibraryTableMode);
        Assert.False(viewModel.IsLibraryCardMode);

        viewModel.SetLibraryCardModeCommand.Execute(null);

        Assert.False(viewModel.IsLibraryTableMode);
        Assert.True(viewModel.IsLibraryCardMode);
    }

    [Fact]
    public void CombosLibraryMode_CanSwitchBetweenCardAndTable()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();
        var viewModel = new CombosViewModel(dataService, notifications);

        Assert.False(viewModel.IsLibraryTableMode);
        Assert.True(viewModel.IsLibraryCardMode);

        viewModel.SetLibraryTableModeCommand.Execute(null);

        Assert.True(viewModel.IsLibraryTableMode);
        Assert.False(viewModel.IsLibraryCardMode);

        viewModel.SetLibraryCardModeCommand.Execute(null);

        Assert.False(viewModel.IsLibraryTableMode);
        Assert.True(viewModel.IsLibraryCardMode);
    }

    [Fact]
    public async Task MainViewModel_TracksNavigation_AndRespondsToSettingsChanges()
    {
        var dataService = new TestDataService();
        dataService.Profiles.Add(new Profile
        {
            Id = "default",
            BusinessName = "Main Branch",
            OwnerName = "Hazel"
        });

        var notifications = new NotificationService();
        var dashboard = new DashboardViewModel(dataService, notifications);
        var materials = new MaterialsViewModel(dataService, notifications);
        var warehouse = new WarehouseViewModel(dataService, notifications);
        var recipes = new RecipesViewModel(dataService, notifications);
        var production = new ProductionViewModel(dataService, notifications);
        var combos = new CombosViewModel(dataService, notifications);
        var pos = new PosViewModel(dataService, notifications);
        var bookkeeping = new BookkeepingViewModel(dataService, notifications);
        var simulation = new SimulationViewModel(dataService, notifications);
        var profile = new ProfileViewModel(dataService, notifications);
        var settings = new SettingsViewModel(dataService, notifications);

        var viewModel = new MainViewModel(
            dataService,
            notifications,
            dashboard,
            materials,
            warehouse,
            recipes,
            production,
            combos,
            pos,
            bookkeeping,
            simulation,
            profile,
            settings);

        await Task.Delay(10);

        Assert.Equal(AppPage.Dashboard, viewModel.CurrentPage);
        Assert.Equal("11 modules", viewModel.NavigationSummaryText);
        Assert.Equal("Main Branch", viewModel.ActiveProfileName);

        viewModel.Navigate(AppPage.Settings);

        Assert.Equal(AppPage.Settings, viewModel.CurrentPage);
        Assert.Same(settings, viewModel.CurrentView);
        Assert.Equal("System preferences and backup.", viewModel.CurrentPageDescription);

        await viewModel.ToggleThemeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDarkMode);
        Assert.Equal("Light", viewModel.ThemeLabel);

        await dataService.UpdateSettingsAsync(new AppSettings
        {
            ActiveProfileId = "default",
            IsAdvancedMode = false,
            IsDarkMode = false,
            FontSizePreset = FontSizingHelper.Small
        });

        Assert.False(viewModel.ShowAdvancedNavigation);
        Assert.Equal("7 modules", viewModel.NavigationSummaryText);
        Assert.Equal("BASIC", viewModel.EditionLabel);
        Assert.Equal("Small", viewModel.FontSizeLabel);
    }

    [Fact]
    public void WarehouseLedger_FiltersBySourceAndMaterial()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 20,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Milk",
            Price = 18000,
            Weight = 1000,
            Unit = "ml",
            Stock = 12,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.StockMovements.Add(new StockMovement
        {
            Id = "mov-1",
            MaterialId = "mat-1",
            MaterialName = "Flour",
            QuantityDelta = 10,
            PreviousStock = 10,
            CurrentStock = 20,
            SourceType = "purchase",
            SourceLabel = "Restock Flour",
            Date = "2026-03-28T07:00:00Z",
            ProfileId = "default"
        });
        dataService.StockMovements.Add(new StockMovement
        {
            Id = "mov-2",
            MaterialId = "mat-1",
            MaterialName = "Flour",
            QuantityDelta = -4,
            PreviousStock = 20,
            CurrentStock = 16,
            SourceType = "production",
            SourceLabel = "Cake Batch",
            Date = "2026-03-29T07:00:00Z",
            ProfileId = "default"
        });
        dataService.StockMovements.Add(new StockMovement
        {
            Id = "mov-3",
            MaterialId = "mat-2",
            MaterialName = "Milk",
            QuantityDelta = 2,
            PreviousStock = 10,
            CurrentStock = 12,
            SourceType = "manual-adjustment",
            SourceLabel = "Audit Rak",
            Date = "2026-03-30T07:00:00Z",
            ProfileId = "default"
        });

        var viewModel = new WarehouseViewModel(dataService, notifications);

        Assert.Equal(3, viewModel.LedgerStockMovements.Count);
        Assert.Equal("3 pergerakan ditampilkan", viewModel.LedgerMovementCountText);
        Assert.Equal("2 masuk", viewModel.LedgerInboundCountText);
        Assert.Equal("1 keluar", viewModel.LedgerOutboundCountText);
        Assert.Equal("Milk", viewModel.LedgerStockMovements[0].MaterialNameText);
        Assert.Contains("Semua Sumber", viewModel.LedgerFilterSummaryText);

        viewModel.SelectedLedgerSourceOption = Assert.Single(viewModel.LedgerSourceOptions, x => x.Value == "production");

        Assert.Single(viewModel.LedgerStockMovements);
        Assert.Equal("Flour", viewModel.LedgerStockMovements[0].MaterialNameText);
        Assert.Contains("Produksi Resep", viewModel.LedgerFilterSummaryText);

        viewModel.SelectedLedgerSourceOption = viewModel.LedgerSourceOptions[0];
        viewModel.SelectedLedgerMaterialOption = Assert.Single(viewModel.LedgerMaterialOptions, x => x.Value == "mat-2");

        Assert.Single(viewModel.LedgerStockMovements);
        Assert.Equal("Milk", viewModel.LedgerStockMovements[0].MaterialNameText);
        Assert.Contains("Milk", viewModel.LedgerHeadlineText);
    }

    [Fact]
    public async Task MaterialsSave_SeparatesCatalogFromWarehouseStock()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        var viewModel = new MaterialsViewModel(dataService, notifications)
        {
            FormName = "Butter",
            FormBrand = "Anchor",
            FormPrice = 24000,
            FormWeight = 1000,
            FormUnit = "gram"
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(dataService.Materials);
        Assert.Equal("Butter", created.Name);
        Assert.Equal("Anchor", created.Brand);
        Assert.Equal(0, created.Stock);
        Assert.False(created.IsTrackedInWarehouse);
        Assert.Empty(dataService.StockMovements);

        dataService.Materials[0] = new HPPSystem.Models.Material
        {
            Id = created.Id,
            Name = created.Name,
            Brand = created.Brand,
            Price = created.Price,
            Weight = created.Weight,
            Unit = created.Unit,
            Stock = 18,
            IsTrackedInWarehouse = false,
            ProfileId = created.ProfileId,
            PriceHistory = created.PriceHistory
        };

        viewModel.EditCommand.Execute(dataService.Materials[0]);
        viewModel.FormPrice = 26000;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var updated = Assert.Single(dataService.Materials);
        Assert.Equal(18, updated.Stock);
        Assert.Equal(26000, updated.Price);
        Assert.False(updated.IsTrackedInWarehouse);
    }

    [Theory]
    [InlineData("1 kg", 1000, "gram")]
    [InlineData("500 gr", 500, "gram")]
    [InlineData("1 Liter", 1000, "ml")]
    [InlineData("11 gr x 4", 44, "gram")]
    [InlineData("10 Lembar", 10, "lembar")]
    [InlineData("4 Pcs (750 gr)", 750, "gram")]
    [InlineData("1 Tray (30 btr)", 30, "butir")]
    [InlineData("1 Roll (5m)", 5, "m")]
    [InlineData("1 Liter x 12 (Karton)", 12000, "ml")]
    [InlineData("75 gr / 90 gr (Kotak Kecil)", 90, "gram")]
    public void MaterialExcelImport_ParsesPackSpecification(string rawPack, decimal expectedWeight, string expectedUnit)
    {
        var parsed = MaterialExcelImportService.TryParsePackSpecification(rawPack, out var weight, out var unit);

        Assert.True(parsed);
        Assert.Equal(expectedWeight, weight);
        Assert.Equal(expectedUnit, unit);
    }

    [Fact]
    public async Task MaterialExcelImport_PreservesWarehouseState_WhenUpdatingExistingMaterial()
    {
        var dataService = new TestDataService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Terigu Protein Tinggi (Roti/Mie)",
            Brand = "Segitiga Biru",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            Stock = 25,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        var preview = new MaterialImportPreview(
            SourcePath: "sample.xlsx",
            WorksheetRowCount: 3,
            CandidateRowCount: 1,
            SkippedRowCount: 0,
            CreateCount: 0,
            UpdateCount: 1,
            NormalizedPackCount: 1,
            Notes: [],
            Entries:
            [
                new MaterialImportEntry(
                    RowNumber: 3,
                    Name: "Terigu Protein Tinggi (Roti/Mie)",
                    Brand: "Segitiga Biru",
                    PackPrice: 14500,
                    PackQuantity: 1000,
                    PackUnit: "gram",
                    OriginalPackText: "1 kg",
                    WillUpdateExisting: true)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        var material = Assert.Single(dataService.Materials);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("Segitiga Biru", material.Brand);
        Assert.Equal(14500, material.Price);
        Assert.Equal(1000, material.Weight);
        Assert.Equal("gram", material.Unit);
        Assert.Equal(25, material.Stock);
        Assert.True(material.IsTrackedInWarehouse);
    }

    [Fact]
    public async Task MaterialExcelImport_UpdatesExistingMaterial_ByNormalizedNameAndBrand_WithoutCreatingDuplicate()
    {
        var dataService = new TestDataService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Butter Unsalted",
            Brand = "Anchor",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 8,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var preview = new MaterialImportPreview(
            SourcePath: "sample.xlsx",
            WorksheetRowCount: 3,
            CandidateRowCount: 1,
            SkippedRowCount: 0,
            CreateCount: 0,
            UpdateCount: 1,
            NormalizedPackCount: 0,
            Notes: [],
            Entries:
            [
                new MaterialImportEntry(
                    RowNumber: 3,
                    Name: "  butter   unsalted  ",
                    Brand: "  anchor  ",
                    PackPrice: 26500,
                    PackQuantity: 500,
                    PackUnit: "gram",
                    OriginalPackText: "500 gr",
                    WillUpdateExisting: true)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        var material = Assert.Single(dataService.Materials);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("Butter Unsalted", material.Name);
        Assert.Equal("Anchor", material.Brand);
        Assert.Equal(26500, material.Price);
        Assert.Equal(500, material.Weight);
        Assert.Equal(8, material.Stock);
    }

    [Fact]
    public async Task MaterialExcelImport_ReimportExactVariant_UpdatesMatchingPack_WhenFamilyHasMultipleVariants()
    {
        var dataService = new TestDataService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "butter-1000",
            Name = "Butter Unsalted",
            Brand = "Anchor",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 4,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "butter-250",
            Name = "Butter Unsalted",
            Brand = "Anchor",
            Price = 8000,
            Weight = 250,
            Unit = "gram",
            Stock = 2,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var preview = new MaterialImportPreview(
            SourcePath: "sample.xlsx",
            WorksheetRowCount: 2,
            CandidateRowCount: 1,
            SkippedRowCount: 0,
            CreateCount: 0,
            UpdateCount: 1,
            NormalizedPackCount: 0,
            Notes: [],
            Entries:
            [
                new MaterialImportEntry(
                    RowNumber: 2,
                    Name: "Butter Unsalted",
                    Brand: "Anchor",
                    PackPrice: 9500,
                    PackQuantity: 250,
                    PackUnit: "gram",
                    OriginalPackText: "250 gr",
                    WillUpdateExisting: true)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(2, dataService.Materials.Count);
        Assert.Contains(dataService.Materials, x => x.Id == "butter-1000" && x.Price == 24000 && x.Weight == 1000);
        Assert.Contains(dataService.Materials, x => x.Id == "butter-250" && x.Price == 9500 && x.Weight == 250 && x.Stock == 2);
    }

    [Fact]
    public async Task MaterialExcelImport_CreatesSeparateVariants_ForSameNameAndBrandWithDifferentNetto()
    {
        var dataService = new TestDataService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "milk-1000",
            Name = "Susu Cair",
            Brand = "Ultra",
            Price = 22000,
            Weight = 1000,
            Unit = "ml",
            Stock = 1,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        var preview = new MaterialImportPreview(
            SourcePath: "sample.xlsx",
            WorksheetRowCount: 3,
            CandidateRowCount: 2,
            SkippedRowCount: 0,
            CreateCount: 1,
            UpdateCount: 1,
            NormalizedPackCount: 0,
            Notes: [],
            Entries:
            [
                new MaterialImportEntry(
                    RowNumber: 2,
                    Name: "Susu Cair",
                    Brand: "Ultra",
                    PackPrice: 23000,
                    PackQuantity: 1000,
                    PackUnit: "ml",
                    OriginalPackText: "1 Liter",
                    WillUpdateExisting: true),
                new MaterialImportEntry(
                    RowNumber: 3,
                    Name: "Susu Cair",
                    Brand: "Ultra",
                    PackPrice: 7000,
                    PackQuantity: 250,
                    PackUnit: "ml",
                    OriginalPackText: "250 ml",
                    WillUpdateExisting: false)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(2, dataService.Materials.Count);
        Assert.Contains(dataService.Materials, x => x.Id == "milk-1000" && x.Price == 23000 && x.Stock == 1);
        Assert.Contains(dataService.Materials, x => x.Id != "milk-1000" && x.Name == "Susu Cair" && x.Brand == "Ultra" && x.Weight == 250 && x.Unit == "ml" && x.Price == 7000);
    }

    [Fact]
    public async Task MaterialExcelImport_UpgradesLegacyBlankBrandMaterial_WithoutCreatingDuplicate()
    {
        var dataService = new TestDataService();
        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Butter Unsalted",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 8,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        var preview = new MaterialImportPreview(
            SourcePath: "sample.xlsx",
            WorksheetRowCount: 3,
            CandidateRowCount: 1,
            SkippedRowCount: 0,
            CreateCount: 0,
            UpdateCount: 1,
            NormalizedPackCount: 0,
            Notes: [],
            Entries:
            [
                new MaterialImportEntry(
                    RowNumber: 3,
                    Name: "Butter Unsalted",
                    Brand: "Anchor",
                    PackPrice: 26500,
                    PackQuantity: 500,
                    PackUnit: "gram",
                    OriginalPackText: "500 gr",
                    WillUpdateExisting: true)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        var material = Assert.Single(dataService.Materials);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal("Anchor", material.Brand);
        Assert.Equal(8, material.Stock);
        Assert.True(material.IsTrackedInWarehouse);
    }

    [Fact]
    public async Task Warehouse_ActivatesCatalogMaterial_OnlyAfterManualSelection()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Butter",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var viewModel = new WarehouseViewModel(dataService, notifications);

        Assert.False(viewModel.HasMaterials);
        Assert.True(viewModel.HasCatalogMaterialOptions);
        Assert.Equal("0 bahan aktif", viewModel.MaterialCountText);

        viewModel.SelectedCatalogMaterialOption = Assert.Single(viewModel.CatalogMaterialOptions);
        await viewModel.AddCatalogMaterialCommand.ExecuteAsync(null);

        var material = Assert.Single(dataService.Materials);
        Assert.True(material.IsTrackedInWarehouse);
        Assert.True(viewModel.HasMaterials);
        Assert.True(viewModel.ShowAdjustForm);
        Assert.Equal("1 bahan aktif", viewModel.MaterialCountText);
    }

    [Fact]
    public async Task Warehouse_CatalogSearch_FiltersCatalogMaterialPicker()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Butter",
            Brand = "Anchor",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Cream Cheese",
            Brand = "Prochiz",
            Price = 32000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var viewModel = new WarehouseViewModel(dataService, notifications);
        Assert.Equal(2, viewModel.CatalogMaterialOptions.Count);

        viewModel.CatalogSearchTerm = "anchor";

        var option = Assert.Single(viewModel.CatalogMaterialOptions);
        Assert.Contains("Butter", option.Label);
        Assert.Contains("1 dari 2 material katalog", viewModel.CatalogPickerSummaryText);

        viewModel.ClearCatalogSearchCommand.Execute(null);
        Assert.Equal(2, viewModel.CatalogMaterialOptions.Count);
    }

    [Fact]
    public async Task Warehouse_DeleteFromWarehouse_RemovesTrackingButKeepsCatalogMaterial()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Butter",
            Brand = "Anchor",
            Price = 24000,
            Weight = 1000,
            Unit = "gram",
            Stock = 12,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        var viewModel = new WarehouseViewModel(dataService, notifications);
        var material = Assert.Single(viewModel.FilteredMaterials);

        viewModel.RequestDeleteFromWarehouseCommand.Execute(material);
        Assert.Contains("tetap ada di katalog Material", viewModel.DeletePromptText);
        Assert.Contains("catatan ledger gudang", viewModel.DeletePromptText);

        await viewModel.DeleteFromWarehouseCommand.ExecuteAsync(material);

        var stored = Assert.Single(dataService.Materials);
        Assert.False(stored.IsTrackedInWarehouse);
        Assert.Equal(0, stored.Stock);
        Assert.Empty(dataService.StockMovements);
        Assert.False(viewModel.HasMaterials);
        Assert.True(viewModel.HasCatalogMaterialOptions);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("dikeluarkan dari daftar gudang"));
    }

    [Fact]
    public async Task Dashboard_InventoryRisk_OnlyUsesWarehouseTrackedMaterials()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        await dataService.SaveMaterialAsync(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Terigu",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            Stock = 0,
            IsTrackedInWarehouse = false,
            ProfileId = "default"
        });

        var viewModel = new DashboardViewModel(dataService, notifications);

        Assert.Equal(0, viewModel.MaterialCount);
        Assert.Equal("0 unit", viewModel.TotalInventoryText);
        Assert.False(viewModel.HasLowStockMaterials);
        Assert.False(viewModel.ShowInventoryRiskSection);
        Assert.Equal("0 bahan kritis", viewModel.LowStockCountText);
    }

    [Fact]
    public void MaterialsRefresh_SortsNamesAlphabetically_AndExposesPackColumnsSeparately()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "zebra flour",
            Brand = "Brand Z",
            Price = 20000,
            Weight = 1000,
            Unit = "gram",
            ProfileId = "default"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Alpha sugar",
            Brand = "Brand A",
            Price = 15000,
            Weight = 500,
            Unit = "gram",
            ProfileId = "default"
        });

        var viewModel = new MaterialsViewModel(dataService, notifications);

        Assert.Equal("Alpha sugar", viewModel.MaterialCards[0].NameText);
        Assert.Equal("Brand A", viewModel.MaterialCards[0].BrandText);
        Assert.Equal("zebra flour", viewModel.MaterialCards[1].NameText);
        Assert.Equal(500, viewModel.MaterialCards[0].PackQuantityValue);
        Assert.Equal("gram", viewModel.MaterialCards[0].PackUnitText);
    }

    [Fact]
    public void MaterialsSortOption_CanSwitchToNameDescending()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material { Id = "mat-1", Name = "Alpha", Price = 10000, Weight = 1000, Unit = "gram", ProfileId = "default" });
        dataService.Materials.Add(new HPPSystem.Models.Material { Id = "mat-2", Name = "Zulu", Brand = "Brand Z", Price = 12000, Weight = 1000, Unit = "gram", ProfileId = "default" });

        var viewModel = new MaterialsViewModel(dataService, notifications);
        viewModel.SelectedSortOption = Assert.Single(viewModel.SortOptions, x => x.Value == "name_desc");

        Assert.Equal("Zulu", viewModel.MaterialCards[0].NameText);
        Assert.Equal("Nama Z-A", viewModel.SortSummaryText);
    }

    [Fact]
    public void WarehouseSortOption_CanSwitchToHighestStockFirst()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Alpha",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 5,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-2",
            Name = "Zulu",
            Price = 12000,
            Weight = 1000,
            Unit = "gram",
            Stock = 15,
            IsTrackedInWarehouse = true,
            ProfileId = "default"
        });

        var viewModel = new WarehouseViewModel(dataService, notifications);
        viewModel.SelectedSortOption = Assert.Single(viewModel.SortOptions, x => x.Value == "stock_desc");

        Assert.Equal("Zulu", viewModel.MaterialCards[0].NameText);
        Assert.Equal(15, viewModel.MaterialCards[0].StockQuantityValue);
        Assert.Equal("Stok Terbesar-Terkecil", viewModel.SortSummaryText);
    }

    [Fact]
    public void DeleteImpactPrompts_ExposeConcreteImpactCounts()
    {
        var dataService = new TestDataService();
        var notifications = new NotificationService();

        dataService.Profiles.Add(new Profile
        {
            Id = "default",
            BusinessName = "Main Branch",
            OwnerName = "Owner"
        });
        dataService.Materials.Add(new HPPSystem.Models.Material
        {
            Id = "mat-1",
            Name = "Flour",
            Price = 10000,
            Weight = 1000,
            Unit = "gram",
            Stock = 20,
            ProfileId = "default"
        });
        dataService.StockMovements.Add(new StockMovement
        {
            Id = "mov-1",
            MaterialId = "mat-1",
            MaterialName = "Flour",
            QuantityDelta = 5,
            PreviousStock = 15,
            CurrentStock = 20,
            ProfileId = "default"
        });
        dataService.Recipes.Add(new Recipe
        {
            Id = "recipe-1",
            Name = "Cake",
            Portions = 1,
            ProfileId = "default",
            IngredientGroups =
            [
                new IngredientGroup
                {
                    Name = "Main",
                    Ingredients =
                    [
                        new Ingredient { MaterialId = "mat-1", Quantity = 100 }
                    ]
                }
            ]
        });
        dataService.Combos.Add(new Combo
        {
            Id = "combo-1",
            Name = "Bundle",
            ProfileId = "default",
            SellingPrice = 20000,
            Recipes =
            [
                new ComboRecipe { RecipeId = "recipe-1", Qty = 1 }
            ]
        });
        dataService.Sales.Add(new Sale
        {
            Id = "sale-1",
            ItemName = "Cake",
            Qty = 1,
            Type = "recipe",
            TotalPrice = 20000,
            TotalHpp = 10000,
            TotalProfit = 10000,
            ProfileId = "default"
        });
        dataService.Transactions.Add(new Transaction
        {
            Id = "trx-1",
            Description = "Restock",
            Amount = 5000,
            ProfileId = "default"
        });

        var materialsViewModel = new MaterialsViewModel(dataService, notifications);
        var material = Assert.Single(materialsViewModel.FilteredMaterials);
        materialsViewModel.RequestDeleteCommand.Execute(material);
        Assert.Contains("1 resep", materialsViewModel.DeletePromptText);
        Assert.Contains("1 catatan stok", materialsViewModel.DeletePromptText);

        var recipesViewModel = new RecipesViewModel(dataService, notifications);
        var recipe = Assert.Single(recipesViewModel.FilteredRecipes);
        recipesViewModel.RequestDeleteCommand.Execute(recipe);
        Assert.Contains("1 bundle terkait", recipesViewModel.DeletePromptText);
        Assert.Contains("ikut terhapus", recipesViewModel.DeletePromptText);

        var profileViewModel = new ProfileViewModel(dataService, notifications);
        var profile = Assert.Single(profileViewModel.Profiles);
        profileViewModel.RequestDeleteCommand.Execute(profile);
        Assert.Contains("1 bahan", profileViewModel.DeletePromptText);
        Assert.Contains("1 catatan ledger stok", profileViewModel.DeletePromptText);
    }
}
