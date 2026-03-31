using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class ProductionViewModel : PageViewModelBase
{
    private string? _focusedOrderId;

    public ProductionViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        DraftProductionDate = DateTime.Today.ToString("yyyy-MM-dd");
        Refresh();
    }

    public override string Title => "Produksi";

    public ObservableCollection<ProductionRecipeOptionViewModel> RecipeOptions { get; } = new();
    public ObservableCollection<ProductionOrderCardViewModel> ProductionQueue { get; } = new();
    public ObservableCollection<ProductionRequirementRowViewModel> DraftRequirements { get; } = new();
    public ObservableCollection<ProductionRequirementRowViewModel> SelectedOrderRequirements { get; } = new();

    [ObservableProperty]
    private ProductionRecipeOptionViewModel? _selectedRecipeOption;

    [ObservableProperty]
    private string _draftProductionDate = string.Empty;

    [ObservableProperty]
    private int _draftBatchCount = 1;

    [ObservableProperty]
    private ProductionOrderCardViewModel? _selectedOrder;

    public bool HasRecipes => RecipeOptions.Count > 0;
    public bool HasOrders => ProductionQueue.Count > 0;
    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasDraftRequirements => DraftRequirements.Count > 0;
    public bool CanCreateOrder => SelectedRecipeOption is not null && DraftBatchCount > 0;
    public bool CanConfirmSelectedOrder => SelectedOrder is not null && !SelectedOrder.IsCompleted;
    public string QueueCountText => $"{ProductionQueue.Count} produksi";
    public string PendingCountText => $"{ProductionQueue.Count(x => !x.IsCompleted)} aktif";
    public string ReadyCountText => $"{ProductionQueue.Count(x => x.IsReady)} siap";
    public string BlockedCountText => $"{ProductionQueue.Count(x => x.IsBlocked)} tertahan";
    public string CompletedCountText => $"{ProductionQueue.Count(x => x.IsCompleted)} selesai";
    public string DraftTitleText => SelectedRecipeOption is null ? "Studio Produksi" : $"Produksi: {SelectedRecipeOption.Name}";
    public string DraftRecipeGuideText => SelectedRecipeOption is null
        ? "Pilih resep yang sudah selesai dibuat, lalu set tanggal produksi dan jumlah batch."
        : $"{SelectedRecipeOption.PortionsText} | {SelectedRecipeOption.StructureText} | rekomendasi jual {SelectedRecipeOption.RecommendedPriceText}.";
    public string DraftDateGuideText => string.IsNullOrWhiteSpace(DraftProductionDate)
        ? "Isi tanggal produksi dengan format yyyy-MM-dd."
        : $"Produksi akan dijadwalkan untuk {DraftProductionDate}.";
    public string DraftBatchGuideText => SelectedRecipeOption is null
        ? "Jumlah batch akan menentukan total bahan yang dibutuhkan dari Gudang."
        : $"{DraftBatchCount} batch x {SelectedRecipeOption.PortionsValue} porsi = {DraftBatchOutputText}.";
    public string DraftBatchOutputText => SelectedRecipeOption is null
        ? "0 porsi output"
        : $"{Math.Max(DraftBatchCount, 0) * SelectedRecipeOption.PortionsValue} porsi output";
    public string DraftReadinessText
    {
        get
        {
            if (SelectedRecipeOption is null)
            {
                return "Belum ada resep produksi yang dipilih.";
            }

            if (!TryParseDraftDate(out _))
            {
                return "Tanggal produksi belum valid. Gunakan format yyyy-MM-dd.";
            }

            if (DraftBatchCount <= 0)
            {
                return "Jumlah batch minimal 1.";
            }

            var shortageCount = DraftRequirements.Count(x => x.HasShortage);
            return shortageCount == 0
                ? "Semua kebutuhan bahan saat ini cukup untuk diproduksi."
                : $"{shortageCount} bahan masih kurang atau belum aktif di Gudang. Order tetap bisa disimpan, tetapi konfirmasi selesai akan tertahan.";
        }
    }
    public string DraftActionText => SelectedRecipeOption is null ? "Buat Order Produksi" : $"Masukkan {SelectedRecipeOption.Name} ke List Produksi";
    public string QueueInsightText => !HasOrders
        ? "Belum ada order produksi aktif. Ambil resep dari library lalu jadwalkan batch pertama."
        : "List produksi menahan order yang stoknya belum cukup, dan hanya order siap yang bisa dikonfirmasi selesai.";
    public string SelectedOrderTitleText => SelectedOrder?.RecipeName ?? "Belum ada order dipilih";
    public string SelectedOrderStatusText => SelectedOrder?.StatusText ?? "Belum ada status";
    public string SelectedOrderScheduleText => SelectedOrder is null
        ? "Pilih satu order dari list produksi untuk membaca kebutuhan bahan dan status stoknya."
        : $"{SelectedOrder.DateText} | {SelectedOrder.BatchText} | {SelectedOrder.OutputText}";
    public string SelectedOrderHintText => SelectedOrder is null
        ? "Detail kebutuhan bahan akan tampil di sini."
        : SelectedOrder.IsCompleted
            ? $"Order ini sudah selesai pada {SelectedOrder.CompletedAtText}."
            : SelectedOrder.IsBlocked
                ? "Order ini masih tertahan. Lengkapi stok gudang dulu, lalu konfirmasi selesai produksi."
                : "Semua stok siap. Konfirmasi selesai produksi akan mengurangi stok gudang sesuai kebutuhan batch.";
    public string SelectedOrderActionText => SelectedOrder is null
        ? "Konfirmasi Selesai"
        : $"Konfirmasi selesai: {SelectedOrder.RecipeName}";
    public Action<string>? RequestBookkeepingForShortage { get; set; }

    partial void OnSelectedRecipeOptionChanged(ProductionRecipeOptionViewModel? value)
    {
        RefreshDraftPreview();
        OnPropertyChanged(nameof(CanCreateOrder));
        OnPropertyChanged(nameof(DraftTitleText));
        OnPropertyChanged(nameof(DraftRecipeGuideText));
        OnPropertyChanged(nameof(DraftBatchGuideText));
        OnPropertyChanged(nameof(DraftBatchOutputText));
        OnPropertyChanged(nameof(DraftReadinessText));
        OnPropertyChanged(nameof(DraftActionText));
    }

    partial void OnDraftProductionDateChanged(string value)
    {
        OnPropertyChanged(nameof(DraftDateGuideText));
        OnPropertyChanged(nameof(DraftReadinessText));
    }

    partial void OnDraftBatchCountChanged(int value)
    {
        RefreshDraftPreview();
        OnPropertyChanged(nameof(CanCreateOrder));
        OnPropertyChanged(nameof(DraftBatchGuideText));
        OnPropertyChanged(nameof(DraftBatchOutputText));
        OnPropertyChanged(nameof(DraftReadinessText));
    }

    partial void OnSelectedOrderChanged(ProductionOrderCardViewModel? value)
    {
        RefreshSelectedOrderPreview();
        OnPropertyChanged(nameof(HasSelectedOrder));
        OnPropertyChanged(nameof(CanConfirmSelectedOrder));
        OnPropertyChanged(nameof(SelectedOrderTitleText));
        OnPropertyChanged(nameof(SelectedOrderStatusText));
        OnPropertyChanged(nameof(SelectedOrderScheduleText));
        OnPropertyChanged(nameof(SelectedOrderHintText));
        OnPropertyChanged(nameof(SelectedOrderActionText));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var materials = DataService.Materials
            .Where(x => x.ProfileId == profileId)
            .ToDictionary(x => x.Id, x => x);

        var previousRecipeId = SelectedRecipeOption?.Id;
        var previousOrderId = SelectedOrder?.Id ?? _focusedOrderId;

        var recipeOptions = DataService.Recipes
            .Where(x => x.ProfileId == profileId)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(recipe => BuildRecipeOption(recipe, materials))
            .ToList();

        RecipeOptions.Clear();
        foreach (var recipeOption in recipeOptions)
        {
            RecipeOptions.Add(recipeOption);
        }

        SelectedRecipeOption = recipeOptions.FirstOrDefault(x => string.Equals(x.Id, previousRecipeId, StringComparison.Ordinal))
            ?? recipeOptions.FirstOrDefault();

        var orderCards = DataService.ProductionOrders
            .Where(x => x.ProfileId == profileId)
            .Select(order => BuildOrderCard(order, materials))
            .OrderBy(x => x.SortRank)
            .ThenBy(x => x.DateValue)
            .ThenBy(x => x.RecipeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProductionQueue.Clear();
        foreach (var orderCard in orderCards)
        {
            ProductionQueue.Add(orderCard);
        }

        SelectedOrder = orderCards.FirstOrDefault(x => string.Equals(x.Id, previousOrderId, StringComparison.Ordinal))
            ?? orderCards.FirstOrDefault();

        RefreshDraftPreview();
        RefreshSelectedOrderPreview();
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(QueueCountText));
        OnPropertyChanged(nameof(PendingCountText));
        OnPropertyChanged(nameof(ReadyCountText));
        OnPropertyChanged(nameof(BlockedCountText));
        OnPropertyChanged(nameof(CompletedCountText));
        OnPropertyChanged(nameof(DraftTitleText));
        OnPropertyChanged(nameof(DraftRecipeGuideText));
        OnPropertyChanged(nameof(DraftDateGuideText));
        OnPropertyChanged(nameof(DraftBatchGuideText));
        OnPropertyChanged(nameof(DraftBatchOutputText));
        OnPropertyChanged(nameof(DraftReadinessText));
        OnPropertyChanged(nameof(DraftActionText));
        OnPropertyChanged(nameof(QueueInsightText));
        OnPropertyChanged(nameof(SelectedOrderTitleText));
        OnPropertyChanged(nameof(SelectedOrderStatusText));
        OnPropertyChanged(nameof(SelectedOrderScheduleText));
        OnPropertyChanged(nameof(SelectedOrderHintText));
        OnPropertyChanged(nameof(SelectedOrderActionText));
        OnPropertyChanged(nameof(CanCreateOrder));
        OnPropertyChanged(nameof(CanConfirmSelectedOrder));
    }

    public void PrepareDraftFromRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            return;
        }

        SelectedRecipeOption = RecipeOptions.FirstOrDefault(x => string.Equals(x.Id, recipeId, StringComparison.Ordinal));
        DraftBatchCount = 1;
        DraftProductionDate = DateTime.Today.ToString("yyyy-MM-dd");
    }

    [RelayCommand]
    private void UseToday()
    {
        DraftProductionDate = DateTime.Today.ToString("yyyy-MM-dd");
    }

    [RelayCommand]
    private async Task CreateOrderAsync()
    {
        if (SelectedRecipeOption?.Recipe is null)
        {
            Error("Pilih resep dulu sebelum membuat order produksi.");
            return;
        }

        if (DraftBatchCount <= 0)
        {
            Error("Jumlah batch minimal 1.");
            return;
        }

        if (!TryParseDraftDate(out var productionDate))
        {
            Error("Tanggal produksi belum valid. Gunakan format yyyy-MM-dd.");
            return;
        }

        var order = new ProductionOrder
        {
            Id = Guid.NewGuid().ToString("N"),
            RecipeId = SelectedRecipeOption.Id,
            RecipeName = SelectedRecipeOption.Name,
            PortionsPerBatch = SelectedRecipeOption.PortionsValue,
            BatchCount = DraftBatchCount,
            ProductionDate = productionDate.ToString("yyyy-MM-dd"),
            Status = "pending",
            Notes = $"Order produksi dibuat untuk {SelectedRecipeOption.Name}.",
            ProfileId = DataService.Settings.ActiveProfileId,
            Requirements = BuildRequirementSnapshots(SelectedRecipeOption.Recipe)
        };

        _focusedOrderId = order.Id;
        await DataService.SaveProductionOrderAsync(order);
        Success($"Order produksi {order.RecipeName} masuk ke list produksi.");
    }

    [RelayCommand]
    private async Task ConfirmOrderAsync(ProductionOrderCardViewModel? order)
    {
        if (order?.Order is null)
        {
            Error("Pilih order produksi dulu.");
            return;
        }

        if (order.IsCompleted)
        {
            Error("Order produksi ini sudah selesai.");
            return;
        }

        var shortages = BuildRequirementRows(order.Order).Where(x => x.HasShortage).ToList();
        if (shortages.Count > 0)
        {
            Error($"Produksi belum bisa dikonfirmasi: {string.Join("; ", shortages.Select(x => x.GuidanceText))}.");
            return;
        }

        var materials = DataService.Materials
            .Where(x => x.ProfileId == DataService.Settings.ActiveProfileId)
            .ToList();

        foreach (var requirement in order.Order.Requirements)
        {
            var totalQuantity = requirement.QuantityPerBatch * order.Order.BatchCount;
            var familyVariants = MaterialVariantPlanner.GetFamilyVariants(materials, MaterialVariantPlanner.BuildFamilyKey(requirement));
            var consumptionPlan = MaterialVariantPlanner.BuildConsumptionPlan(totalQuantity, familyVariants);
            foreach (var consumption in consumptionPlan)
            {
                await DataService.ApplyMaterialStockAdjustmentAsync(new MaterialStockAdjustment
                {
                    MaterialId = consumption.Material.Id,
                    QuantityDelta = -consumption.Quantity,
                    SourceType = "production-order",
                    SourceId = order.Id,
                    SourceLabel = order.RecipeName,
                    Notes = $"Konfirmasi produksi {order.RecipeName} untuk {order.Order.BatchCount} batch."
                });
            }
        }

        var completedOrder = CloneOrder(order.Order);
        completedOrder.Status = "completed";
        completedOrder.CompletedAt = DateTime.UtcNow.ToString("O");
        _focusedOrderId = completedOrder.Id;
        await DataService.SaveProductionOrderAsync(completedOrder);
        Success($"Produksi {order.RecipeName} selesai dan stok gudang sudah dikurangi.");
    }

    [RelayCommand]
    private async Task RemoveOrderAsync(ProductionOrderCardViewModel? order)
    {
        if (order?.Order is null)
        {
            return;
        }

        await DataService.DeleteProductionOrderAsync(order.Id);
        _focusedOrderId = null;
        Success($"Order produksi {order.RecipeName} dihapus dari list.");
    }

    [RelayCommand]
    private void OpenBookkeepingForShortage(ProductionOrderCardViewModel? order)
    {
        if (order?.Order is null)
        {
            Error("Pilih order produksi dulu.");
            return;
        }

        if (!order.IsBlocked)
        {
            Error("Order ini belum butuh template belanja shortage.");
            return;
        }

        RequestBookkeepingForShortage?.Invoke(order.Id);
    }

    private void RefreshDraftPreview()
    {
        DraftRequirements.Clear();
        if (SelectedRecipeOption?.Recipe is null || DraftBatchCount <= 0)
        {
            return;
        }

        foreach (var requirement in BuildRequirementRows(SelectedRecipeOption.Recipe, DraftBatchCount))
        {
            DraftRequirements.Add(requirement);
        }
    }

    private void RefreshSelectedOrderPreview()
    {
        SelectedOrderRequirements.Clear();
        if (SelectedOrder?.Order is null)
        {
            return;
        }

        _focusedOrderId = SelectedOrder.Id;
        foreach (var requirement in BuildRequirementRows(SelectedOrder.Order))
        {
            SelectedOrderRequirements.Add(requirement);
        }
    }

    private List<ProductionMaterialRequirement> BuildRequirementSnapshots(Recipe recipe)
    {
        var profileMaterials = DataService.Materials
            .Where(x => x.ProfileId == DataService.Settings.ActiveProfileId)
            .ToDictionary(x => x.Id, x => x);

        return recipe.IngredientGroups
            .SelectMany(x => x.Ingredients)
            .Where(x => !string.IsNullOrWhiteSpace(x.MaterialId) && x.Quantity > 0)
            .GroupBy(x => x.MaterialId)
            .Select(group =>
            {
                profileMaterials.TryGetValue(group.Key, out var material);
                return new ProductionMaterialRequirement
                {
                    MaterialId = group.Key,
                    MaterialName = material?.Name ?? $"Material {group.Key}",
                    MaterialBrand = material?.Brand ?? string.Empty,
                    Unit = material?.Unit ?? string.Empty,
                    QuantityPerBatch = group.Sum(x => x.Quantity)
                };
            })
            .OrderBy(x => x.MaterialLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<ProductionRequirementRowViewModel> BuildRequirementRows(Recipe recipe, int batchCount)
    {
        var tempOrder = new ProductionOrder
        {
            BatchCount = batchCount,
            Requirements = BuildRequirementSnapshots(recipe)
        };

        return BuildRequirementRows(tempOrder);
    }

    private IEnumerable<ProductionRequirementRowViewModel> BuildRequirementRows(ProductionOrder order)
    {
        var materials = DataService.Materials
            .Where(x => x.ProfileId == DataService.Settings.ActiveProfileId)
            .ToList();

        return order.Requirements
            .OrderBy(x => x.MaterialLabel, StringComparer.OrdinalIgnoreCase)
            .Select(requirement =>
            {
                var familyVariants = MaterialVariantPlanner.GetFamilyVariants(materials, MaterialVariantPlanner.BuildFamilyKey(requirement));
                var primaryMaterial = familyVariants.FirstOrDefault();
                var requiredQuantity = requirement.QuantityPerBatch * order.BatchCount;
                var currentStock = MaterialVariantPlanner.GetTrackedFamilyStock(familyVariants);
                var hasTrackedFamily = familyVariants.Any(material => material.IsTrackedInWarehouse);
                var hasShortage = primaryMaterial is null || !hasTrackedFamily || currentStock < requiredQuantity;

                var guidance = primaryMaterial is null
                    ? $"{requirement.MaterialLabel} sudah tidak ada di katalog"
                    : !hasTrackedFamily
                        ? $"{requirement.MaterialLabel} belum aktif di Gudang"
                    : currentStock < requiredQuantity
                            ? $"{requirement.MaterialLabel} butuh {requiredQuantity:0.##} {requirement.Unit}, stok keluarga tersedia {currentStock:0.##} {requirement.Unit}"
                            : $"{requirement.MaterialLabel} siap dipakai";

                return new ProductionRequirementRowViewModel
                {
                    MaterialLabel = requirement.MaterialLabel,
                    Unit = requirement.Unit,
                    QuantityPerBatchValue = requirement.QuantityPerBatch,
                    RequiredQuantityValue = requiredQuantity,
                    CurrentStockValue = currentStock,
                    QuantityPerBatchText = $"{requirement.QuantityPerBatch:0.##} {requirement.Unit}",
                    RequiredQuantityText = $"{requiredQuantity:0.##} {requirement.Unit}",
                    CurrentStockText = $"{currentStock:0.##} {requirement.Unit}",
                    StatusText = hasShortage ? "Tertahan" : "Ready",
                    HasShortage = hasShortage,
                    GuidanceText = guidance
                };
            })
            .ToList();
    }

    private ProductionRecipeOptionViewModel BuildRecipeOption(Recipe recipe, IReadOnlyDictionary<string, HPPSystem.Models.Material> materials)
    {
        var hpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, materials.Values);
        return new ProductionRecipeOptionViewModel
        {
            Id = recipe.Id,
            Name = recipe.Name,
            PortionsValue = Math.Max(recipe.Portions, 1),
            PortionsText = $"{Math.Max(recipe.Portions, 1)} porsi / batch",
            StructureText = $"{recipe.IngredientGroups.SelectMany(x => x.Ingredients).Count()} bahan | {recipe.IngredientGroups.Count} kelompok | {recipe.OverheadCosts.Count} overhead",
            RecommendedPriceText = FormattingHelper.FormatCurrency(CostCalculator.CalculateRecommendedSellingPrice(hpp, recipe.TargetMargin)),
            Recipe = recipe
        };
    }

    private ProductionOrderCardViewModel BuildOrderCard(ProductionOrder order, IReadOnlyDictionary<string, HPPSystem.Models.Material> materials)
    {
        var requirementRows = order.Requirements
            .Select(requirement =>
            {
                var familyVariants = MaterialVariantPlanner.GetFamilyVariants(materials.Values, MaterialVariantPlanner.BuildFamilyKey(requirement));
                var requiredQuantity = requirement.QuantityPerBatch * order.BatchCount;
                var currentStock = MaterialVariantPlanner.GetTrackedFamilyStock(familyVariants);
                return familyVariants.Count == 0 || !familyVariants.Any(material => material.IsTrackedInWarehouse) || currentStock < requiredQuantity;
            })
            .ToList();

        var isCompleted = string.Equals(order.Status, "completed", StringComparison.OrdinalIgnoreCase);
        var isBlocked = !isCompleted && requirementRows.Any(x => x);
        var isReady = !isCompleted && !isBlocked;
        return new ProductionOrderCardViewModel
        {
            Id = order.Id,
            RecipeId = order.RecipeId,
            RecipeName = order.RecipeName,
            Order = order,
            DateText = FormatDate(order.ProductionDate),
            DateValue = ParseDateOrDefault(order.ProductionDate),
            BatchText = $"{Math.Max(order.BatchCount, 0)} batch",
            OutputText = $"{Math.Max(order.BatchCount, 0) * Math.Max(order.PortionsPerBatch, 0)} porsi",
            StatusText = isCompleted ? "Selesai" : isBlocked ? "Tertahan Stok" : "Siap Produksi",
            CompletedAtText = string.IsNullOrWhiteSpace(order.CompletedAt) ? "-" : FormatDateTime(order.CompletedAt),
            RequirementCountText = $"{order.Requirements.Count} bahan",
            IsCompleted = isCompleted,
            IsBlocked = isBlocked,
            IsReady = isReady,
            SortRank = isCompleted ? 2 : isBlocked ? 1 : 0
        };
    }

    private bool TryParseDraftDate(out DateTime value)
    {
        return DateTime.TryParseExact(
            DraftProductionDate?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    private static DateTime ParseDateOrDefault(string rawDate)
    {
        return DateTime.TryParse(rawDate, out var value) ? value : DateTime.MaxValue;
    }

    private static string FormatDate(string rawDate)
    {
        return DateTime.TryParse(rawDate, out var value)
            ? value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : rawDate;
    }

    private static string FormatDateTime(string rawDate)
    {
        return DateTime.TryParse(rawDate, out var value)
            ? value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : rawDate;
    }

    private static ProductionOrder CloneOrder(ProductionOrder order)
    {
        return new ProductionOrder
        {
            Id = order.Id,
            RecipeId = order.RecipeId,
            RecipeName = order.RecipeName,
            PortionsPerBatch = order.PortionsPerBatch,
            BatchCount = order.BatchCount,
            ProductionDate = order.ProductionDate,
            Status = order.Status,
            CompletedAt = order.CompletedAt,
            Notes = order.Notes,
            ProfileId = order.ProfileId,
            Requirements = order.Requirements
                .Select(requirement => new ProductionMaterialRequirement
                {
                    MaterialId = requirement.MaterialId,
                    MaterialName = requirement.MaterialName,
                    MaterialBrand = requirement.MaterialBrand,
                    Unit = requirement.Unit,
                    QuantityPerBatch = requirement.QuantityPerBatch
                })
                .ToList()
        };
    }
}

public sealed partial class ProductionRecipeOptionViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int PortionsValue { get; init; }
    public string PortionsText { get; init; } = string.Empty;
    public string StructureText { get; init; } = string.Empty;
    public string RecommendedPriceText { get; init; } = string.Empty;
    public Recipe Recipe { get; init; } = new();
}

public sealed partial class ProductionOrderCardViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string RecipeId { get; init; } = string.Empty;
    public string RecipeName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public DateTime DateValue { get; init; }
    public string BatchText { get; init; } = string.Empty;
    public string OutputText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string CompletedAtText { get; init; } = string.Empty;
    public string RequirementCountText { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsReady { get; init; }
    public int SortRank { get; init; }
    public ProductionOrder Order { get; init; } = new();
}

public sealed partial class ProductionRequirementRowViewModel : ObservableObject
{
    public string MaterialLabel { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal QuantityPerBatchValue { get; init; }
    public decimal RequiredQuantityValue { get; init; }
    public decimal CurrentStockValue { get; init; }
    public string QuantityPerBatchText { get; init; } = string.Empty;
    public string RequiredQuantityText { get; init; } = string.Empty;
    public string CurrentStockText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool HasShortage { get; init; }
    public string GuidanceText { get; init; } = string.Empty;
}
