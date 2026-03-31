using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HPPSystem.Models;

namespace HPPSystem.Services;

public interface IDataService
{
    event EventHandler? StateChanged;

    ObservableCollection<HPPSystem.Models.Material> Materials { get; }
    ObservableCollection<StockMovement> StockMovements { get; }
    ObservableCollection<Recipe> Recipes { get; }
    ObservableCollection<Combo> Combos { get; }
    ObservableCollection<Sale> Sales { get; }
    ObservableCollection<Transaction> Transactions { get; }
    ObservableCollection<Profile> Profiles { get; }
    AppSettings Settings { get; }
    string DataStorePath { get; }

    Task LoadDataAsync();
    Task SaveMaterialAsync(HPPSystem.Models.Material material);
    Task SaveMaterialsAsync(IEnumerable<HPPSystem.Models.Material> materials);
    Task ApplyMaterialStockAdjustmentAsync(MaterialStockAdjustment adjustment);
    Task DeleteMaterialAsync(string id);
    Task SaveRecipeAsync(Recipe recipe);
    Task DeleteRecipeAsync(string id);
    Task SaveComboAsync(Combo combo);
    Task DeleteComboAsync(string id);
    Task SaveSaleAsync(Sale sale);
    Task SaveSalesAsync(IEnumerable<Sale> sales);
    Task SaveTransactionAsync(Transaction transaction);
    Task DeleteTransactionAsync(string id);
    Task SaveProfileAsync(Profile profile);
    Task DeleteProfileAsync(string id);
    Task UpdateSettingsAsync(AppSettings settings);
    Task SetActiveProfileAsync(string profileId);
    HppDataSnapshot CreateSnapshot();
    Task ExportSnapshotAsync(string path);
    Task ImportSnapshotAsync(string path);
}

public sealed class LocalJsonDataService : ObservableObject, IDataService
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler? StateChanged;

    public ObservableCollection<HPPSystem.Models.Material> Materials { get; } = new();
    public ObservableCollection<StockMovement> StockMovements { get; } = new();
    public ObservableCollection<Recipe> Recipes { get; } = new();
    public ObservableCollection<Combo> Combos { get; } = new();
    public ObservableCollection<Sale> Sales { get; } = new();
    public ObservableCollection<Transaction> Transactions { get; } = new();
    public ObservableCollection<Profile> Profiles { get; } = new();

    private AppSettings _settings = new();
    public AppSettings Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public string DataStorePath { get; }

    public LocalJsonDataService(string? dataStorePath = null)
    {
        if (!string.IsNullOrWhiteSpace(dataStorePath))
        {
            DataStorePath = dataStorePath;
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataStorePath = Path.Combine(appData, "HPPSystem", "hppsystem-data.json");
    }

    public async Task LoadDataAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataStorePath)!);

            HppDataSnapshot snapshot;
            if (!File.Exists(DataStorePath))
            {
                snapshot = CreateDefaultSnapshot();
                await SaveSnapshotCoreAsync(snapshot, DataStorePath);
            }
            else
            {
                await using var stream = File.OpenRead(DataStorePath);
                snapshot = await JsonSerializer.DeserializeAsync<HppDataSnapshot>(stream, _serializerOptions) ?? CreateDefaultSnapshot();
            }

            ApplySnapshot(NormalizeSnapshot(snapshot));
        }
        finally
        {
            _gate.Release();
        }

        NotifyStateChanged();
    }

    public async Task SaveMaterialAsync(HPPSystem.Models.Material material)
    {
        var movement = BuildManualStockMovement(material);
        await SaveMaterialCoreAsync(material, movement);
    }

    public async Task SaveMaterialsAsync(IEnumerable<HPPSystem.Models.Material> materials)
    {
        foreach (var material in materials)
        {
            await SaveMaterialCoreAsync(material, movement: null, persistChanges: false);
        }

        await PersistAndNotifyAsync();
    }

    public async Task ApplyMaterialStockAdjustmentAsync(MaterialStockAdjustment adjustment)
    {
        var existing = Materials.FirstOrDefault(x => x.Id == adjustment.MaterialId);
        if (existing is null)
        {
            return;
        }

        var updated = CloneMaterial(existing);
        updated.IsTrackedInWarehouse = true;
        updated.Stock += adjustment.QuantityDelta;

        if (adjustment.UpdatedPrice is > 0)
        {
            updated.Price = adjustment.UpdatedPrice.Value;
        }

        if (adjustment.UpdatedWeight is > 0)
        {
            updated.Weight = adjustment.UpdatedWeight.Value;
        }

        var movement = CreateStockMovement(
            updated,
            adjustment.QuantityDelta,
            existing.Stock,
            updated.Stock,
            adjustment.SourceType,
            adjustment.SourceId,
            adjustment.SourceLabel,
            adjustment.Notes);

        await SaveMaterialCoreAsync(updated, movement);
    }

    public async Task DeleteMaterialAsync(string id)
    {
        foreach (var recipe in Recipes.Where(x => x.IngredientGroups.Any(g => g.Ingredients.Any(i => i.MaterialId == id))).ToList())
        {
            recipe.IngredientGroups = recipe.IngredientGroups
                .Select(group => new IngredientGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    Ingredients = group.Ingredients
                        .Where(ingredient => !string.Equals(ingredient.MaterialId, id, StringComparison.Ordinal))
                        .ToList()
                })
                .Where(group => group.Ingredients.Count > 0)
                .ToList();

            Upsert(Recipes, recipe, x => x.Id);
        }

        RemoveAll(StockMovements, x => string.Equals(x.MaterialId, id, StringComparison.Ordinal));
        RemoveById(Materials, id);
        await PersistAndNotifyAsync();
    }

    public async Task SaveRecipeAsync(Recipe recipe)
    {
        recipe.ProfileId = string.IsNullOrWhiteSpace(recipe.ProfileId) ? Settings.ActiveProfileId : recipe.ProfileId;
        Upsert(Recipes, recipe, x => x.Id);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteRecipeAsync(string id)
    {
        foreach (var combo in Combos.Where(x => x.Recipes.Any(recipe => recipe.RecipeId == id)).ToList())
        {
            combo.Recipes = combo.Recipes
                .Where(recipe => !string.Equals(recipe.RecipeId, id, StringComparison.Ordinal))
                .ToList();

            if (combo.Recipes.Count == 0)
            {
                Combos.Remove(combo);
                continue;
            }

            Upsert(Combos, combo, x => x.Id);
        }

        RemoveById(Recipes, id);
        await PersistAndNotifyAsync();
    }

    public async Task SaveComboAsync(Combo combo)
    {
        combo.ProfileId = string.IsNullOrWhiteSpace(combo.ProfileId) ? Settings.ActiveProfileId : combo.ProfileId;
        Upsert(Combos, combo, x => x.Id);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteComboAsync(string id)
    {
        RemoveById(Combos, id);
        await PersistAndNotifyAsync();
    }

    public async Task SaveSaleAsync(Sale sale)
    {
        sale.ProfileId = string.IsNullOrWhiteSpace(sale.ProfileId) ? Settings.ActiveProfileId : sale.ProfileId;
        Upsert(Sales, sale, x => x.Id);
        await PersistAndNotifyAsync();
    }

    public async Task SaveSalesAsync(IEnumerable<Sale> sales)
    {
        foreach (var sale in sales)
        {
            sale.ProfileId = string.IsNullOrWhiteSpace(sale.ProfileId) ? Settings.ActiveProfileId : sale.ProfileId;
            Upsert(Sales, sale, x => x.Id);
        }

        await PersistAndNotifyAsync();
    }

    public async Task SaveTransactionAsync(Transaction transaction)
    {
        transaction.ProfileId = string.IsNullOrWhiteSpace(transaction.ProfileId) ? Settings.ActiveProfileId : transaction.ProfileId;
        Upsert(Transactions, transaction, x => x.Id);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteTransactionAsync(string id)
    {
        RemoveById(Transactions, id);
        await PersistAndNotifyAsync();
    }

    public async Task SaveProfileAsync(Profile profile)
    {
        Upsert(Profiles, profile, x => x.Id);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteProfileAsync(string id)
    {
        if (Profiles.Count <= 1)
        {
            return;
        }

        RemoveById(Profiles, id);
        RemoveAllByProfile(Materials, id);
        RemoveAllByProfile(Recipes, id);
        RemoveAllByProfile(Combos, id);
        RemoveAllByProfile(Sales, id);
        RemoveAllByProfile(Transactions, id);
        RemoveAllByProfile(StockMovements, id);

        if (Settings.ActiveProfileId == id)
        {
            Settings.ActiveProfileId = Profiles.First().Id;
        }

        await PersistAndNotifyAsync();
    }

    public async Task UpdateSettingsAsync(AppSettings settings)
    {
        Settings = settings;
        await PersistAndNotifyAsync();
    }

    public async Task SetActiveProfileAsync(string profileId)
    {
        Settings.ActiveProfileId = profileId;
        await PersistAndNotifyAsync();
    }

    public HppDataSnapshot CreateSnapshot()
    {
        return new HppDataSnapshot
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            Profiles = Profiles.ToList(),
            Materials = Materials.ToList(),
            StockMovements = StockMovements.ToList(),
            Recipes = Recipes.ToList(),
            Combos = Combos.ToList(),
            Sales = Sales.ToList(),
            Transactions = Transactions.ToList(),
            Settings = new AppSettings
            {
                ActiveProfileId = Settings.ActiveProfileId,
                IsAdvancedMode = Settings.IsAdvancedMode,
                IsDarkMode = Settings.IsDarkMode
            }
        };
    }

    public async Task ExportSnapshotAsync(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await SaveSnapshotCoreAsync(CreateSnapshot(), path);
    }

    public async Task ImportSnapshotAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<HppDataSnapshot>(stream, _serializerOptions);
        if (snapshot is null)
        {
            return;
        }

        var normalized = NormalizeSnapshot(snapshot);
        ApplySnapshot(normalized);
        await PersistAndNotifyAsync();
    }

    private async Task PersistAndNotifyAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataStorePath)!);
            await SaveSnapshotCoreAsync(CreateSnapshot(), DataStorePath);
        }
        finally
        {
            _gate.Release();
        }

        NotifyStateChanged();
    }

    private async Task SaveSnapshotCoreAsync(HppDataSnapshot snapshot, string path)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, _serializerOptions);
    }

    private void ApplySnapshot(HppDataSnapshot snapshot)
    {
        ReplaceCollection(Profiles, snapshot.Profiles);
        ReplaceCollection(Materials, snapshot.Materials);
        ReplaceCollection(StockMovements, snapshot.StockMovements);
        ReplaceCollection(Recipes, snapshot.Recipes);
        ReplaceCollection(Combos, snapshot.Combos);
        ReplaceCollection(Sales, snapshot.Sales);
        ReplaceCollection(Transactions, snapshot.Transactions);
        Settings = snapshot.Settings;
    }

    private HppDataSnapshot NormalizeSnapshot(HppDataSnapshot snapshot)
    {
        snapshot.Profiles ??= new List<Profile>();
        snapshot.Materials ??= new List<HPPSystem.Models.Material>();
        snapshot.StockMovements ??= new List<StockMovement>();
        snapshot.Recipes ??= new List<Recipe>();
        snapshot.Combos ??= new List<Combo>();
        snapshot.Sales ??= new List<Sale>();
        snapshot.Transactions ??= new List<Transaction>();
        snapshot.Settings ??= new AppSettings();

        if (snapshot.Profiles.Count == 0)
        {
            snapshot.Profiles.Add(new Profile());
        }

        foreach (var material in snapshot.Materials)
        {
            material.ProfileId = string.IsNullOrWhiteSpace(material.ProfileId) ? "default" : material.ProfileId;
            material.PriceHistory ??= new List<PriceHistoryRecord>();
            material.PricePerUnit = material.Weight <= 0 ? 0 : material.Price / material.Weight;
            material.IsTrackedInWarehouse = material.IsTrackedInWarehouse
                || material.Stock != 0
                || snapshot.StockMovements.Any(x => string.Equals(x.MaterialId, material.Id, StringComparison.Ordinal));
        }

        foreach (var recipe in snapshot.Recipes)
        {
            recipe.ProfileId = string.IsNullOrWhiteSpace(recipe.ProfileId) ? "default" : recipe.ProfileId;
            recipe.IngredientGroups ??= new List<IngredientGroup>();
            recipe.OverheadCosts ??= new List<OverheadCost>();
        }

        foreach (var combo in snapshot.Combos)
        {
            combo.ProfileId = string.IsNullOrWhiteSpace(combo.ProfileId) ? "default" : combo.ProfileId;
            combo.Recipes ??= new List<ComboRecipe>();
        }

        foreach (var sale in snapshot.Sales)
        {
            sale.ProfileId = string.IsNullOrWhiteSpace(sale.ProfileId) ? "default" : sale.ProfileId;
        }

        foreach (var transaction in snapshot.Transactions)
        {
            transaction.ProfileId = string.IsNullOrWhiteSpace(transaction.ProfileId) ? "default" : transaction.ProfileId;
            transaction.PurchasedItems ??= new List<PurchasedItem>();
        }

        foreach (var movement in snapshot.StockMovements)
        {
            movement.ProfileId = string.IsNullOrWhiteSpace(movement.ProfileId) ? "default" : movement.ProfileId;
        }

        if (!snapshot.Profiles.Any(x => x.Id == snapshot.Settings.ActiveProfileId))
        {
            snapshot.Settings.ActiveProfileId = snapshot.Profiles.First().Id;
        }

        return snapshot;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static void Upsert<T, TKey>(ObservableCollection<T> collection, T item, Func<T, TKey> keySelector)
    {
        var existing = collection.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals(keySelector(x), keySelector(item)));
        if (existing is not null)
        {
            collection[collection.IndexOf(existing)] = item;
            return;
        }

        collection.Add(item);
    }

    private static void RemoveById<T>(ObservableCollection<T> collection, string id) where T : class
    {
        var property = typeof(T).GetProperty("Id");
        var existing = collection.FirstOrDefault(x => string.Equals(property?.GetValue(x)?.ToString(), id, StringComparison.Ordinal));
        if (existing is not null)
        {
            collection.Remove(existing);
        }
    }

    private static void RemoveAllByProfile<T>(ObservableCollection<T> collection, string profileId) where T : class
    {
        var property = typeof(T).GetProperty("ProfileId");
        var items = collection
            .Where(x => string.Equals(property?.GetValue(x)?.ToString(), profileId, StringComparison.Ordinal))
            .ToList();

        foreach (var item in items)
        {
            collection.Remove(item);
        }
    }

    private static void RemoveAll<T>(ObservableCollection<T> collection, Func<T, bool> predicate)
    {
        var items = collection.Where(predicate).ToList();
        foreach (var item in items)
        {
            collection.Remove(item);
        }
    }

    private async Task SaveMaterialCoreAsync(HPPSystem.Models.Material material, StockMovement? movement, bool persistChanges = true)
    {
        var existing = Materials.FirstOrDefault(x => x.Id == material.Id);
        if (existing is not null)
        {
            material.PriceHistory = existing.PriceHistory.ToList();
            if (existing.Price != material.Price)
            {
                material.PriceHistory.Add(new PriceHistoryRecord
                {
                    Date = DateTime.UtcNow.ToString("O"),
                    Price = existing.Price
                });
            }
        }

        material.PricePerUnit = material.Weight <= 0 ? 0 : material.Price / material.Weight;
        material.ProfileId = string.IsNullOrWhiteSpace(material.ProfileId) ? Settings.ActiveProfileId : material.ProfileId;

        Upsert(Materials, material, x => x.Id);

        if (movement is not null)
        {
            Upsert(StockMovements, movement, x => x.Id);
        }

        if (persistChanges)
        {
            await PersistAndNotifyAsync();
        }
    }

    private StockMovement? BuildManualStockMovement(HPPSystem.Models.Material material)
    {
        var existing = Materials.FirstOrDefault(x => x.Id == material.Id);
        var previousStock = existing?.Stock ?? 0;
        var currentStock = material.Stock;
        var delta = currentStock - previousStock;
        if (delta == 0)
        {
            return null;
        }

        return CreateStockMovement(
            material,
            delta,
            previousStock,
            currentStock,
            "manual-adjustment",
            material.Id,
            material.Name,
            existing is null ? "Stok awal material dicatat dari katalog." : "Stok material diperbarui dari katalog bahan.");
    }

    private static StockMovement CreateStockMovement(
        HPPSystem.Models.Material material,
        decimal delta,
        decimal previousStock,
        decimal currentStock,
        string sourceType,
        string sourceId,
        string sourceLabel,
        string notes)
    {
        return new StockMovement
        {
            MaterialId = material.Id,
            MaterialName = material.Name,
            QuantityDelta = delta,
            PreviousStock = previousStock,
            CurrentStock = currentStock,
            Direction = delta > 0 ? "in" : delta < 0 ? "out" : "adjustment",
            SourceType = sourceType,
            SourceId = sourceId,
            SourceLabel = sourceLabel,
            Notes = notes,
            ProfileId = material.ProfileId
        };
    }

    private static HPPSystem.Models.Material CloneMaterial(HPPSystem.Models.Material material)
    {
        return new HPPSystem.Models.Material
        {
            Id = material.Id,
            Name = material.Name,
            Price = material.Price,
            Weight = material.Weight,
            Unit = material.Unit,
            PricePerUnit = material.PricePerUnit,
            Stock = material.Stock,
            IsTrackedInWarehouse = material.IsTrackedInWarehouse,
            ProfileId = material.ProfileId,
            PriceHistory = material.PriceHistory.ToList()
        };
    }

    private static HppDataSnapshot CreateDefaultSnapshot()
    {
        var profile = new Profile
        {
            Id = "default",
            BusinessName = "Bisnis Utama",
            OwnerName = "Owner"
        };

        var materials = new List<HPPSystem.Models.Material>
        {
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Tepung Terigu",
                Price = 14000,
                Weight = 1000,
                Unit = "gram",
                PricePerUnit = 14,
                Stock = 3500,
                IsTrackedInWarehouse = true,
                ProfileId = "default"
            },
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Gula Pasir",
                Price = 18000,
                Weight = 1000,
                Unit = "gram",
                PricePerUnit = 18,
                Stock = 2000,
                IsTrackedInWarehouse = true,
                ProfileId = "default"
            },
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Susu Cair",
                Price = 22000,
                Weight = 1000,
                Unit = "ml",
                PricePerUnit = 22,
                Stock = 1200,
                IsTrackedInWarehouse = true,
                ProfileId = "default"
            }
        };

        var recipe = new Recipe
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Pancake Signature",
            Portions = 4,
            ProfileId = "default",
            TargetMargin = 40,
            IngredientGroups = new List<IngredientGroup>
            {
                new()
                {
                    Name = "Adonan",
                    Ingredients = new List<Ingredient>
                    {
                        new() { MaterialId = materials[0].Id, Quantity = 300 },
                        new() { MaterialId = materials[1].Id, Quantity = 60 },
                        new() { MaterialId = materials[2].Id, Quantity = 250 }
                    }
                }
            },
            OverheadCosts = new List<OverheadCost>
            {
                new() { Name = "Gas dan utilitas", Cost = 5000 }
            }
        };

        return new HppDataSnapshot
        {
            Profiles = new List<Profile> { profile },
            Materials = materials,
            Recipes = new List<Recipe> { recipe },
            Settings = new AppSettings
            {
                ActiveProfileId = "default",
                IsAdvancedMode = true,
                IsDarkMode = false
            }
        };
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Settings));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
