using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class RecipesViewModel : PageViewModelBase
{
    private readonly HashSet<IngredientGroupEditorViewModel> _trackedGroups = new();
    private readonly HashSet<IngredientEntryViewModel> _trackedIngredients = new();
    private readonly HashSet<OverheadEntryViewModel> _trackedOverheads = new();
    private bool _isEditorSubscribed;

    public RecipesViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Editor = new RecipeEditorViewModel();
        Editor.Groups.CollectionChanged += OnEditorGroupsChanged;
        Editor.Overheads.CollectionChanged += OnEditorOverheadsChanged;
        WireEditorSubscriptions();
        Refresh();
    }

    public override string Title => "Manajemen Resep";

    public ObservableCollection<HPPSystem.Models.Material> AvailableMaterials { get; } = new();
    public ObservableCollection<RecipeCardViewModel> FilteredRecipes { get; } = new();
    public ObservableCollection<RecipeIngredientDetailViewModel> SelectedRecipeIngredients { get; } = new();
    public ObservableCollection<RecipeOverheadDetailViewModel> SelectedRecipeOverheads { get; } = new();
    public RecipeEditorViewModel Editor { get; }

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _produceBatches = 1;

    [ObservableProperty]
    private RecipeCardViewModel? _selectedRecipeForProduction;

    [ObservableProperty]
    private RecipeCardViewModel? _pendingDelete;

    [ObservableProperty]
    private RecipeCardViewModel? _selectedRecipe;

    public bool IsAdvancedMode => DataService.Settings.IsAdvancedMode;
    public string EditorMaterialCostText => FormattingHelper.FormatCurrency(CostCalculator.CalculateRecipeCost(Editor.ToRecipe(DataService.Settings.ActiveProfileId), AvailableMaterials).MaterialCost);
    public string EditorOverheadCostText => FormattingHelper.FormatCurrency(CostCalculator.CalculateRecipeCost(Editor.ToRecipe(DataService.Settings.ActiveProfileId), AvailableMaterials).OverheadCost);
    public string EditorTotalCostText => FormattingHelper.FormatCurrency(CostCalculator.CalculateRecipeCost(Editor.ToRecipe(DataService.Settings.ActiveProfileId), AvailableMaterials).TotalCost);
    public string EditorHppText => FormattingHelper.FormatCurrency(CostCalculator.CalculateRecipeHppPerPortion(Editor.ToRecipe(DataService.Settings.ActiveProfileId), AvailableMaterials));
    public string EditorRecommendedPriceText
    {
        get
        {
            var hpp = CostCalculator.CalculateRecipeHppPerPortion(Editor.ToRecipe(DataService.Settings.ActiveProfileId), AvailableMaterials);
            return FormattingHelper.FormatCurrency(CostCalculator.CalculateRecommendedSellingPrice(hpp, Editor.TargetMargin));
        }
    }

    partial void OnSearchTermChanged(string value) => Refresh();
    partial void OnSelectedRecipeForProductionChanged(RecipeCardViewModel? value)
    {
        OnPropertyChanged(nameof(CanProduce));
        OnPropertyChanged(nameof(ProductionTargetText));
    }
    partial void OnProduceBatchesChanged(int value) => OnPropertyChanged(nameof(ProductionTargetText));

    public bool CanProduce => SelectedRecipeForProduction is not null;
    public bool ShowDeletePrompt => PendingDelete is not null;
    public bool HasRecipes => FilteredRecipes.Count > 0;
    public bool HasSelectedRecipe => SelectedRecipe is not null;
    public string SearchSummaryText { get; private set; } = "0 resep ditampilkan";
    public string LibraryInsightText { get; private set; } = "Belum ada resep aktif pada profil ini.";
    public string TopRecipeNameText { get; private set; } = "-";
    public string TopRecipeMarginText { get; private set; } = "0%";
    public string TopRecipeRecommendedPriceText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string HighestHppText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TotalIngredientFootprintText { get; private set; } = "0 bahan";
    public string TotalOverheadLineText { get; private set; } = "0 overhead";
    public string EditorTitleText => string.IsNullOrWhiteSpace(Editor.Id) ? "Recipe Architect" : $"Recipe Architect: {Editor.Name}";
    public string EditorSubtitleText => string.IsNullOrWhiteSpace(Editor.Id)
        ? "Bangun resep dari nol dengan struktur bahan, overhead, margin, dan simulasi modal yang terukur."
        : "Perbarui struktur resep, komposisi bahan, overhead, dan arah pricing tanpa kehilangan kalkulasi biaya.";
    public string EditorActionText => string.IsNullOrWhiteSpace(Editor.Id) ? "Publikasikan Resep" : "Update Resep";
    public string ProductionTargetText => SelectedRecipeForProduction is null
        ? "Belum ada resep produksi yang dipilih."
        : $"{SelectedRecipeForProduction.Name} untuk {ProduceBatches} batch";
    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : $"Hapus resep {PendingDelete.Name} secara permanen? Relasi bundling dan analitik margin untuk resep ini juga akan hilang.";
    public string TotalRecipeCountText => $"{FilteredRecipes.Count} resep";
    public string TotalPortionCapacityText => $"{FilteredRecipes.Sum(x => x.Recipe.Portions)} porsi / batch";
    public string AverageHppText
    {
        get
        {
            if (FilteredRecipes.Count == 0)
            {
                return FormattingHelper.FormatCurrency(0);
            }

            var avg = FilteredRecipes
                .Select(x => CostCalculator.CalculateRecipeHppPerPortion(x.Recipe, AvailableMaterials))
                .DefaultIfEmpty(0)
                .Average();
            return FormattingHelper.FormatCurrency((decimal)avg);
        }
    }
    public string SelectedRecipeName => SelectedRecipe?.Name ?? "Belum ada resep dipilih";
    public string SelectedRecipeSummary => SelectedRecipe is null
        ? "Pilih salah satu resep untuk melihat detail bahan, biaya, dan overhead."
        : $"{SelectedRecipe.PortionsText} | {SelectedRecipe.MarginText} target margin";
    public string SelectedRecipeHppText => SelectedRecipe?.HppText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeMaterialCostText => SelectedRecipe?.MaterialCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeOverheadCostText => SelectedRecipe?.OverheadCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeTotalCostText => SelectedRecipe?.TotalCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeRecommendedPriceText => SelectedRecipe?.RecommendedPriceText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeMarginText => SelectedRecipe?.MarginText ?? "0%";
    public string SelectedRecipeOperationalText => SelectedRecipe is null
        ? "Pilih resep untuk melihat blueprint operasional."
        : $"{SelectedRecipe.IngredientCountText} | {SelectedRecipe.GroupCountText} | {SelectedRecipe.OverheadCountText}";

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;

        AvailableMaterials.Clear();
        foreach (var material in DataService.Materials.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name))
        {
            AvailableMaterials.Add(material);
        }

        var cards = DataService.Recipes
            .Where(x => x.ProfileId == profileId)
            .Where(x => string.IsNullOrWhiteSpace(SearchTerm) || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name)
            .Select(CreateCard)
            .ToList();

        FilteredRecipes.Clear();
        foreach (var card in cards)
        {
            FilteredRecipes.Add(card);
        }

        SearchSummaryText = $"{cards.Count} resep ditampilkan";
        TotalIngredientFootprintText = $"{cards.Sum(x => x.Recipe.IngredientGroups.SelectMany(g => g.Ingredients).Count())} bahan";
        TotalOverheadLineText = $"{cards.Sum(x => x.Recipe.OverheadCosts.Count)} overhead";

        var topMargin = cards
            .OrderByDescending(x => x.Recipe.TargetMargin)
            .ThenBy(x => x.Name)
            .FirstOrDefault();
        TopRecipeNameText = topMargin?.Name ?? "-";
        TopRecipeMarginText = topMargin?.MarginText ?? "0%";
        TopRecipeRecommendedPriceText = topMargin?.RecommendedPriceText ?? FormattingHelper.FormatCurrency(0);

        var highestHpp = cards
            .Select(x => CostCalculator.CalculateRecipeHppPerPortion(x.Recipe, AvailableMaterials))
            .DefaultIfEmpty(0)
            .Max();
        HighestHppText = FormattingHelper.FormatCurrency(highestHpp);
        LibraryInsightText = cards.Count switch
        {
            0 => "Belum ada resep aktif pada profil ini. Mulai dengan satu resep utama untuk membangun standar HPP dan pricing.",
            _ when topMargin is null => "Katalog resep tersedia, tetapi insight margin belum terbentuk.",
            _ => $"Resep margin tertinggi saat ini adalah {topMargin.Name} di {topMargin.MarginText}. Gunakan sebagai acuan pricing dan efisiensi struktur bahan."
        };

        if (SelectedRecipe is null || !FilteredRecipes.Any(x => x.Id == SelectedRecipe.Id))
        {
            SelectedRecipe = FilteredRecipes.FirstOrDefault();
        }
        else
        {
            SelectedRecipe = FilteredRecipes.First(x => x.Id == SelectedRecipe.Id);
        }

        if (SelectedRecipeForProduction is not null && !FilteredRecipes.Any(x => x.Id == SelectedRecipeForProduction.Id))
        {
            SelectedRecipeForProduction = null;
            ProduceBatches = 1;
        }

        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(LibraryInsightText));
        OnPropertyChanged(nameof(TopRecipeNameText));
        OnPropertyChanged(nameof(TopRecipeMarginText));
        OnPropertyChanged(nameof(TopRecipeRecommendedPriceText));
        OnPropertyChanged(nameof(HighestHppText));
        OnPropertyChanged(nameof(TotalIngredientFootprintText));
        OnPropertyChanged(nameof(TotalOverheadLineText));
        OnPropertyChanged(nameof(TotalRecipeCountText));
        OnPropertyChanged(nameof(TotalPortionCapacityText));
        OnPropertyChanged(nameof(AverageHppText));
        RefreshMetrics();
        RefreshSelectedRecipeDetails();
    }

    [RelayCommand]
    private void StartCreate()
    {
        Editor.LoadFrom(null);
        WireEditorSubscriptions();
        IsEditing = true;
        RefreshMetrics();
    }

    [RelayCommand]
    private void Edit(RecipeCardViewModel card)
    {
        SelectedRecipe = card;
        Editor.LoadFrom(card.Recipe);
        WireEditorSubscriptions();
        IsEditing = true;
        RefreshMetrics();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        Editor.LoadFrom(null);
        WireEditorSubscriptions();
        RefreshMetrics();
    }

    [RelayCommand]
    private void AddGroup()
    {
        var group = new IngredientGroupEditorViewModel();
        SubscribeGroup(group);
        Editor.Groups.Add(group);
        RefreshMetrics();
    }

    [RelayCommand]
    private void RemoveGroup(IngredientGroupEditorViewModel group)
    {
        Editor.Groups.Remove(group);
        RefreshMetrics();
    }

    [RelayCommand]
    private void AddIngredient(IngredientGroupEditorViewModel group)
    {
        var ingredient = new IngredientEntryViewModel();
        SubscribeIngredient(ingredient);
        group.Ingredients.Add(ingredient);
        RefreshMetrics();
    }

    [RelayCommand]
    private void RemoveIngredient(IngredientEntryViewModel ingredient)
    {
        var group = Editor.Groups.FirstOrDefault(x => x.Ingredients.Contains(ingredient));
        group?.Ingredients.Remove(ingredient);
        RefreshMetrics();
    }

    [RelayCommand]
    private void AddOverhead()
    {
        var overhead = new OverheadEntryViewModel();
        SubscribeOverhead(overhead);
        Editor.Overheads.Add(overhead);
        RefreshMetrics();
    }

    [RelayCommand]
    private void RemoveOverhead(OverheadEntryViewModel overhead)
    {
        Editor.Overheads.Remove(overhead);
        RefreshMetrics();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var recipe = Editor.ToRecipe(DataService.Settings.ActiveProfileId);
        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            Error("Nama resep wajib diisi.");
            return;
        }

        if (!recipe.IngredientGroups.Any() || !recipe.IngredientGroups.SelectMany(x => x.Ingredients).Any())
        {
            Error("Resep minimal harus memiliki satu bahan.");
            return;
        }

        await DataService.SaveRecipeAsync(recipe);
        Success(string.IsNullOrWhiteSpace(Editor.Id) ? "Resep baru disimpan." : "Resep diperbarui.");
        IsEditing = false;
        Editor.LoadFrom(null);
        WireEditorSubscriptions();
        RefreshMetrics();
    }

    [RelayCommand]
    private async Task DeleteAsync(RecipeCardViewModel card)
    {
        await DataService.DeleteRecipeAsync(card.Id);
        Success($"Resep {card.Name} dihapus.");
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    [RelayCommand]
    private void RequestDelete(RecipeCardViewModel card)
    {
        SelectedRecipe = card;
        PendingDelete = card;
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    [RelayCommand]
    private void PrepareProduce(RecipeCardViewModel card)
    {
        SelectedRecipe = card;
        SelectedRecipeForProduction = card;
        ProduceBatches = 1;
        OnPropertyChanged(nameof(ProductionTargetText));
    }

    [RelayCommand]
    private void SelectRecipe(RecipeCardViewModel card)
    {
        SelectedRecipe = card;
    }

    [RelayCommand]
    private async Task ProduceAsync()
    {
        if (SelectedRecipeForProduction?.Recipe is null || ProduceBatches <= 0)
        {
            return;
        }

        var needs = SelectedRecipeForProduction.Recipe.IngredientGroups
            .SelectMany(x => x.Ingredients)
            .GroupBy(x => x.MaterialId)
            .Select(x => new
            {
                MaterialId = x.Key,
                Quantity = x.Sum(v => v.Quantity) * ProduceBatches,
                Material = AvailableMaterials.FirstOrDefault(material => material.Id == x.Key)
            })
            .ToList();

        var shortages = needs
            .Where(x => x.Material is null || x.Material.Stock < x.Quantity)
            .Select(x => x.Material is null
                ? $"bahan dengan id {x.MaterialId} tidak ditemukan"
                : $"{x.Material.Name} butuh {x.Quantity:0.##} {x.Material.Unit}, stok tersedia {x.Material.Stock:0.##}")
            .ToList();

        if (shortages.Count > 0)
        {
            Error($"Produksi dibatalkan karena stok tidak cukup: {string.Join("; ", shortages)}.");
            return;
        }

        foreach (var need in needs)
        {
            need.Material!.Stock -= need.Quantity;
            await DataService.SaveMaterialAsync(need.Material);
        }

        Success($"Produksi {SelectedRecipeForProduction.Name} dicatat untuk {ProduceBatches} batch.");
        SelectedRecipeForProduction = null;
        ProduceBatches = 1;
        OnPropertyChanged(nameof(ProductionTargetText));
    }

    private RecipeCardViewModel CreateCard(Recipe recipe)
    {
        var breakdown = CostCalculator.CalculateRecipeCost(recipe, AvailableMaterials);
        var hpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, AvailableMaterials);
        return new RecipeCardViewModel
        {
            Id = recipe.Id,
            Name = recipe.Name,
            Recipe = recipe,
            HppText = FormattingHelper.FormatCurrency(hpp),
            MaterialCostText = FormattingHelper.FormatCurrency(breakdown.MaterialCost),
            OverheadCostText = FormattingHelper.FormatCurrency(breakdown.OverheadCost),
            TotalCostText = FormattingHelper.FormatCurrency(breakdown.TotalCost),
            RecommendedPriceText = FormattingHelper.FormatCurrency(CostCalculator.CalculateRecommendedSellingPrice(hpp, recipe.TargetMargin)),
            MarginText = $"{recipe.TargetMargin:0.#}%",
            PortionsText = $"{recipe.Portions} porsi"
            ,
            IngredientCountText = $"{recipe.IngredientGroups.SelectMany(x => x.Ingredients).Count()} bahan",
            GroupCountText = $"{recipe.IngredientGroups.Count} kelompok",
            OverheadCountText = $"{recipe.OverheadCosts.Count} overhead"
        };
    }

    private void WireEditorSubscriptions()
    {
        if (!_isEditorSubscribed)
        {
            Editor.PropertyChanged += OnEditorGraphChanged;
            _isEditorSubscribed = true;
        }

        foreach (var group in Editor.Groups)
        {
            SubscribeGroup(group);
        }

        foreach (var overhead in Editor.Overheads)
        {
            SubscribeOverhead(overhead);
        }
    }

    private void SubscribeGroup(IngredientGroupEditorViewModel group)
    {
        if (!_trackedGroups.Add(group))
        {
            return;
        }

        group.PropertyChanged += OnEditorGraphChanged;
        foreach (var ingredient in group.Ingredients)
        {
            SubscribeIngredient(ingredient);
        }

        group.Ingredients.CollectionChanged += OnGroupIngredientsChanged;
    }

    private void SubscribeIngredient(IngredientEntryViewModel ingredient)
    {
        if (_trackedIngredients.Add(ingredient))
        {
            ingredient.PropertyChanged += OnEditorGraphChanged;
        }
    }

    private void SubscribeOverhead(OverheadEntryViewModel overhead)
    {
        if (_trackedOverheads.Add(overhead))
        {
            overhead.PropertyChanged += OnEditorGraphChanged;
        }
    }

    private void OnEditorGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var group in e.NewItems.OfType<IngredientGroupEditorViewModel>())
            {
                SubscribeGroup(group);
            }
        }

        RefreshMetrics();
    }

    private void OnEditorOverheadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var overhead in e.NewItems.OfType<OverheadEntryViewModel>())
            {
                SubscribeOverhead(overhead);
            }
        }

        RefreshMetrics();
    }

    private void OnGroupIngredientsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var ingredient in e.NewItems.OfType<IngredientEntryViewModel>())
            {
                SubscribeIngredient(ingredient);
            }
        }

        RefreshMetrics();
    }

    private void OnEditorGraphChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        RefreshEditorIngredientCosts();
        OnPropertyChanged(nameof(EditorMaterialCostText));
        OnPropertyChanged(nameof(EditorOverheadCostText));
        OnPropertyChanged(nameof(EditorTotalCostText));
        OnPropertyChanged(nameof(EditorHppText));
        OnPropertyChanged(nameof(EditorRecommendedPriceText));
        OnPropertyChanged(nameof(EditorTitleText));
        OnPropertyChanged(nameof(EditorSubtitleText));
        OnPropertyChanged(nameof(EditorActionText));
        OnPropertyChanged(nameof(CanProduce));
        OnPropertyChanged(nameof(ProductionTargetText));
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
        OnPropertyChanged(nameof(HasSelectedRecipe));
        OnPropertyChanged(nameof(SelectedRecipeName));
        OnPropertyChanged(nameof(SelectedRecipeSummary));
        OnPropertyChanged(nameof(SelectedRecipeHppText));
        OnPropertyChanged(nameof(SelectedRecipeMaterialCostText));
        OnPropertyChanged(nameof(SelectedRecipeOverheadCostText));
        OnPropertyChanged(nameof(SelectedRecipeTotalCostText));
        OnPropertyChanged(nameof(SelectedRecipeRecommendedPriceText));
        OnPropertyChanged(nameof(SelectedRecipeMarginText));
        OnPropertyChanged(nameof(SelectedRecipeOperationalText));
    }

    partial void OnSelectedRecipeChanged(RecipeCardViewModel? value)
    {
        RefreshSelectedRecipeDetails();
        RefreshMetrics();
    }

    private void RefreshSelectedRecipeDetails()
    {
        SelectedRecipeIngredients.Clear();
        SelectedRecipeOverheads.Clear();

        if (SelectedRecipe?.Recipe is null)
        {
            return;
        }

        var materials = AvailableMaterials.ToDictionary(x => x.Id, x => x);
        foreach (var group in SelectedRecipe.Recipe.IngredientGroups)
        {
            foreach (var ingredient in group.Ingredients)
            {
                if (!materials.TryGetValue(ingredient.MaterialId, out var material))
                {
                    continue;
                }

                SelectedRecipeIngredients.Add(new RecipeIngredientDetailViewModel
                {
                    GroupName = group.Name,
                    MaterialName = material.Name,
                    QuantityText = $"{ingredient.Quantity:0.##} {material.Unit}",
                    UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit),
                    TotalCostText = FormattingHelper.FormatCurrency(material.PricePerUnit * ingredient.Quantity)
                });
            }
        }

        foreach (var overhead in SelectedRecipe.Recipe.OverheadCosts)
        {
            SelectedRecipeOverheads.Add(new RecipeOverheadDetailViewModel
            {
                Name = overhead.Name,
                CostText = FormattingHelper.FormatCurrency(overhead.Cost)
            });
        }
    }

    private void RefreshEditorIngredientCosts()
    {
        var materials = AvailableMaterials.ToDictionary(x => x.Id, x => x);
        foreach (var group in Editor.Groups)
        {
            foreach (var ingredient in group.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.MaterialId) || !materials.TryGetValue(ingredient.MaterialId, out var material))
                {
                    ingredient.UnitName = string.Empty;
                    ingredient.UnitCostText = FormattingHelper.FormatCurrency(0);
                    ingredient.LineCostText = FormattingHelper.FormatCurrency(0);
                    continue;
                }

                ingredient.UnitName = material.Unit;
                ingredient.UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit);
                ingredient.LineCostText = FormattingHelper.FormatCurrency(material.PricePerUnit * ingredient.Quantity);
            }
        }
    }
}
