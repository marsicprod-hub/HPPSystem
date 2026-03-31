using System.Linq;
using System.Threading.Tasks;
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

        await viewModel.ToggleAdvancedCommand.ExecuteAsync(null);

        Assert.False(dataService.Settings.IsAdvancedMode);
        Assert.True(dataService.Settings.IsDarkMode);
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
    public async Task Produce_Blocks_WhenStockInsufficient()
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

        var viewModel = new RecipesViewModel(dataService, notifications);
        var selectedCard = Assert.Single(viewModel.FilteredRecipes);
        viewModel.PrepareProduceCommand.Execute(selectedCard);

        await viewModel.ProduceCommand.ExecuteAsync(null);

        Assert.Equal(5, dataService.Materials[0].Stock);
        Assert.Contains(notifications.Toasts, x => x.IsError && x.Message.Contains("stok tidak cukup"));
    }

    [Fact]
    public async Task Produce_ReducesStock_WhenStockIsSufficient()
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

        var viewModel = new RecipesViewModel(dataService, notifications);
        var selectedCard = Assert.Single(viewModel.FilteredRecipes);
        viewModel.PrepareProduceCommand.Execute(selectedCard);
        viewModel.ProduceBatches = 2;

        await viewModel.ProduceCommand.ExecuteAsync(null);

        Assert.Equal(5, dataService.Materials[0].Stock);
        Assert.Contains(notifications.Toasts, x => !x.IsError && x.Message.Contains("Produksi"));
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
            combos,
            pos,
            bookkeeping,
            simulation,
            profile,
            settings);

        await Task.Delay(10);

        Assert.Equal(AppPage.Dashboard, viewModel.CurrentPage);
        Assert.Equal("10 modules", viewModel.NavigationSummaryText);
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
            IsDarkMode = false
        });

        Assert.False(viewModel.ShowAdvancedNavigation);
        Assert.Equal("6 modules", viewModel.NavigationSummaryText);
        Assert.Equal("BASIC", viewModel.EditionLabel);
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
            FormPrice = 24000,
            FormWeight = 1000,
            FormUnit = "gram"
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(dataService.Materials);
        Assert.Equal("Butter", created.Name);
        Assert.Equal(0, created.Stock);
        Assert.False(created.IsTrackedInWarehouse);
        Assert.Empty(dataService.StockMovements);

        dataService.Materials[0] = new HPPSystem.Models.Material
        {
            Id = created.Id,
            Name = created.Name,
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
                    PackPrice: 14500,
                    PackWeight: 1000,
                    Unit: "gram",
                    OriginalPackText: "1 kg",
                    WillUpdateExisting: true)
            ]);

        var result = await MaterialExcelImportService.ApplyPreviewAsync(preview, dataService, "default");

        var material = Assert.Single(dataService.Materials);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(14500, material.Price);
        Assert.Equal(1000, material.Weight);
        Assert.Equal("gram", material.Unit);
        Assert.Equal(25, material.Stock);
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
