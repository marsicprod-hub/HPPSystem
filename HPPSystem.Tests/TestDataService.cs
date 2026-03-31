using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.Tests;

internal sealed class TestDataService : IDataService
{
    public event EventHandler? StateChanged;

    public ObservableCollection<HPPSystem.Models.Material> Materials { get; } = new();
    public ObservableCollection<StockMovement> StockMovements { get; } = new();
    public ObservableCollection<Recipe> Recipes { get; } = new();
    public ObservableCollection<Combo> Combos { get; } = new();
    public ObservableCollection<Sale> Sales { get; } = new();
    public ObservableCollection<Transaction> Transactions { get; } = new();
    public ObservableCollection<Profile> Profiles { get; } = new();
    public AppSettings Settings { get; private set; } = new()
    {
        ActiveProfileId = "default",
        IsAdvancedMode = true,
        IsDarkMode = true
    };

    public string DataStorePath => "test-store.json";

    public Task LoadDataAsync() => Task.CompletedTask;

    public Task SaveMaterialAsync(HPPSystem.Models.Material material)
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

        var previousStock = existing?.Stock ?? 0;
        Upsert(Materials, material, x => x.Id);
        if (material.Stock != previousStock)
        {
            StockMovements.Add(new StockMovement
            {
                MaterialId = material.Id,
                MaterialName = material.Name,
                QuantityDelta = material.Stock - previousStock,
                PreviousStock = previousStock,
                CurrentStock = material.Stock,
                Direction = material.Stock >= previousStock ? "in" : "out",
                SourceType = "manual-adjustment",
                SourceId = material.Id,
                SourceLabel = material.Name,
                ProfileId = material.ProfileId
            });
        }

        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveMaterialsAsync(IEnumerable<HPPSystem.Models.Material> materials)
    {
        foreach (var material in materials)
        {
            SaveMaterialAsync(material);
        }

        return Task.CompletedTask;
    }

    public Task ApplyMaterialStockAdjustmentAsync(MaterialStockAdjustment adjustment)
    {
        var existing = Materials.FirstOrDefault(x => x.Id == adjustment.MaterialId);
        if (existing is null)
        {
            return Task.CompletedTask;
        }

        var updated = new HPPSystem.Models.Material
        {
            Id = existing.Id,
            Name = existing.Name,
            Price = adjustment.UpdatedPrice ?? existing.Price,
            Weight = adjustment.UpdatedWeight ?? existing.Weight,
            Unit = existing.Unit,
            Stock = existing.Stock + adjustment.QuantityDelta,
            IsTrackedInWarehouse = true,
            ProfileId = existing.ProfileId,
            PriceHistory = existing.PriceHistory.ToList()
        };

        if (existing.Price != updated.Price)
        {
            updated.PriceHistory.Add(new PriceHistoryRecord
            {
                Date = DateTime.UtcNow.ToString("O"),
                Price = existing.Price
            });
        }

        updated.PricePerUnit = updated.Weight <= 0 ? 0 : updated.Price / updated.Weight;
        Upsert(Materials, updated, x => x.Id);
        StockMovements.Add(new StockMovement
        {
            MaterialId = updated.Id,
            MaterialName = updated.Name,
            QuantityDelta = adjustment.QuantityDelta,
            PreviousStock = existing.Stock,
            CurrentStock = updated.Stock,
            Direction = adjustment.QuantityDelta > 0 ? "in" : adjustment.QuantityDelta < 0 ? "out" : "adjustment",
            SourceType = adjustment.SourceType,
            SourceId = adjustment.SourceId,
            SourceLabel = adjustment.SourceLabel,
            Notes = adjustment.Notes,
            ProfileId = updated.ProfileId
        });
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task DeleteMaterialAsync(string id)
    {
        RemoveById(Materials, id);
        RemoveAll(StockMovements, x => x.MaterialId == id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveRecipeAsync(Recipe recipe)
    {
        Upsert(Recipes, recipe, x => x.Id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task DeleteRecipeAsync(string id)
    {
        RemoveById(Recipes, id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveComboAsync(Combo combo)
    {
        Upsert(Combos, combo, x => x.Id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task DeleteComboAsync(string id)
    {
        RemoveById(Combos, id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveSaleAsync(Sale sale)
    {
        Upsert(Sales, sale, x => x.Id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveSalesAsync(IEnumerable<Sale> sales)
    {
        foreach (var sale in sales)
        {
            Upsert(Sales, sale, x => x.Id);
        }

        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveTransactionAsync(Transaction transaction)
    {
        Upsert(Transactions, transaction, x => x.Id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task DeleteTransactionAsync(string id)
    {
        RemoveById(Transactions, id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveProfileAsync(Profile profile)
    {
        Upsert(Profiles, profile, x => x.Id);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task DeleteProfileAsync(string id)
    {
        RemoveById(Profiles, id);
        RemoveAllByProfile(Materials, id);
        RemoveAllByProfile(Recipes, id);
        RemoveAllByProfile(Combos, id);
        RemoveAllByProfile(Sales, id);
        RemoveAllByProfile(Transactions, id);
        RemoveAllByProfile(StockMovements, id);
        if (Settings.ActiveProfileId == id && Profiles.Count > 0)
        {
            Settings.ActiveProfileId = Profiles[0].Id;
        }
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task UpdateSettingsAsync(AppSettings settings)
    {
        Settings = new AppSettings
        {
            ActiveProfileId = settings.ActiveProfileId,
            IsAdvancedMode = settings.IsAdvancedMode,
            IsDarkMode = settings.IsDarkMode
        };

        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SetActiveProfileAsync(string profileId)
    {
        Settings.ActiveProfileId = profileId;
        NotifyChanged();
        return Task.CompletedTask;
    }

    public HppDataSnapshot CreateSnapshot()
    {
        return new HppDataSnapshot
        {
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

    public Task ExportSnapshotAsync(string path) => throw new NotSupportedException();

    public Task ImportSnapshotAsync(string path) => throw new NotSupportedException();

    private void NotifyChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

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

    private static void RemoveAll<T>(ObservableCollection<T> collection, Func<T, bool> predicate)
    {
        var items = collection.Where(predicate).ToList();
        foreach (var item in items)
        {
            collection.Remove(item);
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
}
