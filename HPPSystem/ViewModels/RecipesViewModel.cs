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
    private const string FocusAllValue = "all";
    private const string FocusLowMarginValue = "low-margin";
    private const string FocusHighMarginValue = "high-margin";
    private const string FocusHighHppValue = "high-hpp";
    private const decimal LowMarginThreshold = 30m;
    private const decimal HighMarginThreshold = 45m;

    private readonly HashSet<IngredientGroupEditorViewModel> _trackedGroups = new();
    private readonly HashSet<IngredientEntryViewModel> _trackedIngredients = new();
    private readonly HashSet<OverheadEntryViewModel> _trackedOverheads = new();
    private bool _isEditorSubscribed;
    private string _riskRecipeName = string.Empty;

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
    public ObservableCollection<HPPSystem.Models.Material> BuilderMaterials { get; } = new();
    public ObservableCollection<RecipeCardViewModel> FilteredRecipes { get; } = new();
    public ObservableCollection<RecipeIngredientDetailViewModel> SelectedRecipeIngredients { get; } = new();
    public ObservableCollection<RecipeOverheadDetailViewModel> SelectedRecipeOverheads { get; } = new();
    public RecipeEditorViewModel Editor { get; }

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _recipeFocusMode = FocusAllValue;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private RecipeCardViewModel? _pendingDelete;

    [ObservableProperty]
    private RecipeCardViewModel? _selectedRecipe;

    [ObservableProperty]
    private bool _isLibraryTableMode;

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
    partial void OnRecipeFocusModeChanged(string value) => Refresh();
    partial void OnIsLibraryTableModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLibraryCardMode));
    }

    public bool ShowDeletePrompt => PendingDelete is not null;
    public bool HasRecipes => FilteredRecipes.Count > 0;
    public bool HasSelectedRecipe => SelectedRecipe is not null;
    public bool HasBuilderMaterials => BuilderMaterials.Count > 0;
    public bool IsLibraryCardMode => !IsLibraryTableMode;
    public string SearchSummaryText { get; private set; } = "0 resep ditampilkan";
    public string FocusAllRecipesText { get; private set; } = "Semua 0";
    public string FocusLowMarginText { get; private set; } = "Margin Rendah 0";
    public string FocusHighMarginText { get; private set; } = "Margin Tinggi 0";
    public string FocusHighHppText { get; private set; } = "HPP Tinggi 0";
    public string RecipeFocusSummaryText { get; private set; } = "Menampilkan semua resep aktif.";
    public string RecipeQuickActionText { get; private set; } = "Belum ada resep untuk aksi cepat.";
    public string SearchHelperText => string.IsNullOrWhiteSpace(SearchTerm)
        ? "Cari nama resep untuk fokus ke menu tertentu."
        : $"Filter aktif: \"{SearchTerm.Trim()}\"";
    public bool IsFocusAllRecipes => string.Equals(RecipeFocusMode, FocusAllValue, StringComparison.Ordinal);
    public bool IsFocusLowMargin => string.Equals(RecipeFocusMode, FocusLowMarginValue, StringComparison.Ordinal);
    public bool IsFocusHighMargin => string.Equals(RecipeFocusMode, FocusHighMarginValue, StringComparison.Ordinal);
    public bool IsFocusHighHpp => string.Equals(RecipeFocusMode, FocusHighHppValue, StringComparison.Ordinal);
    public bool HasSearchTerm => !string.IsNullOrWhiteSpace(SearchTerm);
    public bool HasActiveRecipeFocus => !IsFocusAllRecipes;
    public bool HasActiveSearchOrFocus => HasSearchTerm || HasActiveRecipeFocus;
    public bool HasRecipeQuickActionTarget => !string.IsNullOrWhiteSpace(_riskRecipeName);
    public string RecipeQuickActionButtonText => string.IsNullOrWhiteSpace(_riskRecipeName)
        ? "Belum ada prioritas review"
        : $"Fokus review: {_riskRecipeName}";
    public string LibraryInsightText { get; private set; } = "Belum ada resep aktif pada profil ini.";
    public string LibraryFocusText { get; private set; } = "Belum ada highlight library.";
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
    public string EditorMaterialScopeText { get; private set; } = "Builder mengambil seluruh katalog material. Ikon ! merah menandai bahan yang stok gudangnya kosong atau belum aktif.";
    public string BuilderMaterialCountText { get; private set; } = "0 material katalog";
    public string EditorStructureText =>
        $"{Editor.Groups.Count} kelompok | {Editor.Groups.SelectMany(x => x.Ingredients).Count()} bahan | {Editor.Overheads.Count} overhead | {Math.Max(Editor.Portions, 1)} porsi";
    public string EditorGroupCountText => $"{Editor.Groups.Count} kelompok";
    public string EditorIngredientCountText => $"{Editor.Groups.SelectMany(x => x.Ingredients).Count()} bahan";
    public string EditorOverheadSummaryText => $"{Editor.Overheads.Count} overhead";
    public string EditorPricingGuideText =>
        $"Target margin {Editor.TargetMargin:0.#}% menghasilkan rekomendasi jual {EditorRecommendedPriceText}.";
    public string EditorLiveCostEquationText =>
        $"{EditorMaterialCostText} bahan + {EditorOverheadCostText} overhead = {EditorTotalCostText} modal batch.";
    public string EditorOutputGuideText =>
        $"{Math.Max(Editor.Portions, 1)} porsi per batch | HPP {EditorHppText} | rekomendasi jual {EditorRecommendedPriceText}.";
    public string EditorReadinessText
    {
        get
        {
            if (!HasBuilderMaterials)
            {
                return "Belum ada material katalog. Tambahkan bahan dulu dari halaman Material.";
            }

            if (string.IsNullOrWhiteSpace(Editor.Name))
            {
                return "Isi nama resep untuk mulai menyusun blueprint.";
            }

            var validIngredients = Editor.Groups
                .SelectMany(x => x.Ingredients)
                .Count(x => !string.IsNullOrWhiteSpace(x.MaterialId) && x.Quantity > 0);

            if (validIngredients == 0)
            {
                return "Tambahkan bahan pertama untuk membentuk costing.";
            }
            var warehouseWarnings = Editor.Groups
                .SelectMany(x => x.Ingredients)
                .Count(x => x.HasWarehouseWarning);
            return warehouseWarnings == 0
                ? $"Blueprint siap dihitung dengan {validIngredients} bahan aktif."
                : $"Blueprint siap dihitung, tetapi {warehouseWarnings} bahan perlu perhatian stok gudang.";
        }
    }
    public string EditorActionText => string.IsNullOrWhiteSpace(Editor.Id) ? "Publikasikan Resep" : "Update Resep";
    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : BuildDeletePromptText(PendingDelete);
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
    public string SelectedRecipeBlueprintText => SelectedRecipe is null
        ? "Blueprint resep akan tampil di sini setelah kamu memilih item dari library."
        : $"{SelectedRecipe.GroupCountText} | {SelectedRecipe.IngredientCountText} | {SelectedRecipe.OverheadCountText}";
    public string SelectedRecipeHppText => SelectedRecipe?.HppText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeMaterialCostText => SelectedRecipe?.MaterialCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeOverheadCostText => SelectedRecipe?.OverheadCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeTotalCostText => SelectedRecipe?.TotalCostText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeRecommendedPriceText => SelectedRecipe?.RecommendedPriceText ?? FormattingHelper.FormatCurrency(0);
    public string SelectedRecipeMarginText => SelectedRecipe?.MarginText ?? "0%";
    public string SelectedRecipeOperationalText => SelectedRecipe is null
        ? "Pilih resep untuk melihat blueprint operasional."
        : $"{SelectedRecipe.IngredientCountText} | {SelectedRecipe.GroupCountText} | {SelectedRecipe.OverheadCountText}";
    public string SelectedRecipePricingGuideText => SelectedRecipe is null
        ? "Belum ada resep yang dipilih untuk dibaca struktur pricing-nya."
        : $"Rekomendasi jual {SelectedRecipe.RecommendedPriceText} dengan target margin {SelectedRecipe.MarginText}.";
    public string SelectedRecipeIngredientCountText => SelectedRecipe?.IngredientCountText ?? "0 bahan";
    public string SelectedRecipeOverheadCountText => SelectedRecipe?.OverheadCountText ?? "0 overhead";
    public Action<string>? RequestProductionSetup { get; set; }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;

        AvailableMaterials.Clear();
        BuilderMaterials.Clear();
        foreach (var material in DataService.Materials.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableMaterials.Add(material);
            BuilderMaterials.Add(material);
        }

        EditorMaterialScopeText = BuilderMaterials.Count switch
        {
            0 => "Belum ada material katalog. Tambahkan bahan dulu di halaman Material.",
            1 => "1 material katalog tersedia untuk recipe builder. Ikon ! merah menandai stok gudang yang kosong.",
            _ => $"{BuilderMaterials.Count} material katalog tersedia untuk recipe builder. Ikon ! merah menandai stok gudang yang kosong."
        };
        BuilderMaterialCountText = BuilderMaterials.Count switch
        {
            0 => "0 material katalog",
            1 => "1 material katalog",
            _ => $"{BuilderMaterials.Count} material katalog"
        };

        var allCards = DataService.Recipes
            .Where(x => x.ProfileId == profileId)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCard)
            .ToList();

        var searchCards = allCards
            .Where(x => string.IsNullOrWhiteSpace(SearchTerm) || x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hppBaseline = allCards
            .Select(x => x.HppValue)
            .DefaultIfEmpty(0)
            .Average();

        var cards = searchCards
            .Where(x => RecipeFocusMode switch
            {
                FocusLowMarginValue => x.MarginValue <= LowMarginThreshold,
                FocusHighMarginValue => x.MarginValue >= HighMarginThreshold,
                FocusHighHppValue => hppBaseline > 0 && x.HppValue >= hppBaseline,
                _ => true
            })
            .ToList();

        FilteredRecipes.Clear();
        foreach (var card in cards)
        {
            FilteredRecipes.Add(card);
        }

        FocusAllRecipesText = $"Semua {allCards.Count}";
        FocusLowMarginText = $"Margin Rendah {allCards.Count(x => x.MarginValue <= LowMarginThreshold)}";
        FocusHighMarginText = $"Margin Tinggi {allCards.Count(x => x.MarginValue >= HighMarginThreshold)}";
        FocusHighHppText = $"HPP Tinggi {allCards.Count(x => hppBaseline > 0 && x.HppValue >= hppBaseline)}";
        RecipeFocusSummaryText = RecipeFocusMode switch
        {
            FocusLowMarginValue => $"Menampilkan resep dengan margin <= {LowMarginThreshold:0.#}%.",
            FocusHighMarginValue => $"Menampilkan resep dengan margin >= {HighMarginThreshold:0.#}%.",
            FocusHighHppValue => "Menampilkan resep dengan HPP di atas rata-rata hasil filter.",
            _ => "Menampilkan semua resep aktif."
        };
        SearchSummaryText = $"{cards.Count} resep ditampilkan";
        TotalIngredientFootprintText = $"{cards.Sum(x => x.Recipe.IngredientGroups.SelectMany(g => g.Ingredients).Count())} bahan";
        TotalOverheadLineText = $"{cards.Sum(x => x.Recipe.OverheadCosts.Count)} overhead";

        var topMargin = allCards
            .OrderByDescending(x => x.Recipe.TargetMargin)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        TopRecipeNameText = topMargin?.Name ?? "-";
        TopRecipeMarginText = topMargin?.MarginText ?? "0%";
        TopRecipeRecommendedPriceText = topMargin?.RecommendedPriceText ?? FormattingHelper.FormatCurrency(0);

        var highestHpp = allCards
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
        LibraryFocusText = allCards.Count switch
        {
            0 => "Belum ada fokus recipe library untuk profil aktif.",
            _ when topMargin is null => "Belum ada resep unggulan untuk dijadikan benchmark.",
            _ => $"Fokus library: {topMargin.Name} sebagai benchmark margin, dengan HPP tertinggi library di {HighestHppText}."
        };
        var riskRecipe = allCards
            .Where(x => x.MarginValue <= LowMarginThreshold)
            .OrderBy(x => x.MarginValue)
            .ThenByDescending(x => x.HppValue)
            .FirstOrDefault();
        _riskRecipeName = riskRecipe?.Name ?? string.Empty;
        RecipeQuickActionText = riskRecipe is null
            ? "Tidak ada resep margin rendah. Fokus ke optimasi pricing menu unggulan."
            : $"Prioritas review: {riskRecipe.Name} (margin {riskRecipe.MarginText}, HPP {riskRecipe.HppText}).";

        if (SelectedRecipe is null || !FilteredRecipes.Any(x => x.Id == SelectedRecipe.Id))
        {
            SelectedRecipe = FilteredRecipes.FirstOrDefault();
        }
        else
        {
            SelectedRecipe = FilteredRecipes.First(x => x.Id == SelectedRecipe.Id);
        }

        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(HasRecipes));
        OnPropertyChanged(nameof(FocusAllRecipesText));
        OnPropertyChanged(nameof(FocusLowMarginText));
        OnPropertyChanged(nameof(FocusHighMarginText));
        OnPropertyChanged(nameof(FocusHighHppText));
        OnPropertyChanged(nameof(RecipeFocusSummaryText));
        OnPropertyChanged(nameof(RecipeQuickActionText));
        OnPropertyChanged(nameof(IsFocusAllRecipes));
        OnPropertyChanged(nameof(IsFocusLowMargin));
        OnPropertyChanged(nameof(IsFocusHighMargin));
        OnPropertyChanged(nameof(IsFocusHighHpp));
        OnPropertyChanged(nameof(HasSearchTerm));
        OnPropertyChanged(nameof(HasActiveRecipeFocus));
        OnPropertyChanged(nameof(HasActiveSearchOrFocus));
        OnPropertyChanged(nameof(HasRecipeQuickActionTarget));
        OnPropertyChanged(nameof(RecipeQuickActionButtonText));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(SearchHelperText));
        OnPropertyChanged(nameof(LibraryInsightText));
        OnPropertyChanged(nameof(LibraryFocusText));
        OnPropertyChanged(nameof(TopRecipeNameText));
        OnPropertyChanged(nameof(TopRecipeMarginText));
        OnPropertyChanged(nameof(TopRecipeRecommendedPriceText));
        OnPropertyChanged(nameof(HighestHppText));
        OnPropertyChanged(nameof(TotalIngredientFootprintText));
        OnPropertyChanged(nameof(TotalOverheadLineText));
        OnPropertyChanged(nameof(TotalRecipeCountText));
        OnPropertyChanged(nameof(TotalPortionCapacityText));
        OnPropertyChanged(nameof(AverageHppText));
        OnPropertyChanged(nameof(EditorMaterialScopeText));
        OnPropertyChanged(nameof(BuilderMaterialCountText));
        OnPropertyChanged(nameof(HasBuilderMaterials));
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
    private void FocusAllRecipes()
    {
        RecipeFocusMode = FocusAllValue;
    }

    [RelayCommand]
    private void FocusLowMargin()
    {
        RecipeFocusMode = FocusLowMarginValue;
    }

    [RelayCommand]
    private void FocusHighMargin()
    {
        RecipeFocusMode = FocusHighMarginValue;
    }

    [RelayCommand]
    private void FocusHighHpp()
    {
        RecipeFocusMode = FocusHighHppValue;
    }

    [RelayCommand]
    private void ClearSearchAndFocus()
    {
        RecipeFocusMode = FocusAllValue;
        SearchTerm = string.Empty;
    }

    [RelayCommand]
    private void FocusRecipeQuickAction()
    {
        if (string.IsNullOrWhiteSpace(_riskRecipeName))
        {
            Error("Belum ada resep margin rendah untuk diprioritaskan.");
            return;
        }

        RecipeFocusMode = FocusLowMarginValue;
        SearchTerm = _riskRecipeName;
    }

    [RelayCommand]
    private void SetLibraryCardMode()
    {
        IsLibraryTableMode = false;
    }

    [RelayCommand]
    private void SetLibraryTableMode()
    {
        IsLibraryTableMode = true;
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
    private void OpenProduction(RecipeCardViewModel? card)
    {
        if (card is null)
        {
            Error("Pilih resep dulu sebelum membuka produksi.");
            return;
        }

        SelectedRecipe = card;
        RequestProductionSetup?.Invoke(card.Id);
    }

    [RelayCommand]
    private void SelectRecipe(RecipeCardViewModel card)
    {
        SelectedRecipe = card;
    }

    [RelayCommand]
    private async Task DuplicateAsync(RecipeCardViewModel card)
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var copyName = BuildDuplicateName(card.Name, profileId);
        var clone = new Recipe
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = copyName,
            Portions = card.Recipe.Portions,
            TargetMargin = card.Recipe.TargetMargin,
            ProfileId = profileId,
            IngredientGroups = card.Recipe.IngredientGroups
                .Select(group => new IngredientGroup
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = group.Name,
                    Ingredients = group.Ingredients
                        .Select(ingredient => new Ingredient
                        {
                            MaterialId = ingredient.MaterialId,
                            Quantity = ingredient.Quantity
                        })
                        .ToList()
                })
                .ToList(),
            OverheadCosts = card.Recipe.OverheadCosts
                .Select(overhead => new OverheadCost
                {
                    Name = overhead.Name,
                    Cost = overhead.Cost
                })
                .ToList()
        };

        await DataService.SaveRecipeAsync(clone);
        Success($"Resep {card.Name} diduplikasi menjadi {copyName}.");
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
            HppValue = hpp,
            MarginValue = recipe.TargetMargin,
            HppText = FormattingHelper.FormatCurrency(hpp),
            MaterialCostText = FormattingHelper.FormatCurrency(breakdown.MaterialCost),
            OverheadCostText = FormattingHelper.FormatCurrency(breakdown.OverheadCost),
            TotalCostText = FormattingHelper.FormatCurrency(breakdown.TotalCost),
            RecommendedPriceText = FormattingHelper.FormatCurrency(CostCalculator.CalculateRecommendedSellingPrice(hpp, recipe.TargetMargin)),
            MarginText = $"{recipe.TargetMargin:0.#}%",
            PortionsText = $"{recipe.Portions} porsi",
            IngredientCountText = $"{recipe.IngredientGroups.SelectMany(x => x.Ingredients).Count()} bahan",
            GroupCountText = $"{recipe.IngredientGroups.Count} kelompok",
            OverheadCountText = $"{recipe.OverheadCosts.Count} overhead"
        };
    }

    private string BuildDuplicateName(string baseName, string profileId)
    {
        var existingNames = DataService.Recipes
            .Where(x => x.ProfileId == profileId)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidate = $"{baseName} Copy";
        if (!existingNames.Contains(candidate))
        {
            return candidate;
        }

        var index = 2;
        while (existingNames.Contains($"{baseName} Copy {index}"))
        {
            index++;
        }

        return $"{baseName} Copy {index}";
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
        OnPropertyChanged(nameof(BuilderMaterialCountText));
        OnPropertyChanged(nameof(EditorGroupCountText));
        OnPropertyChanged(nameof(EditorIngredientCountText));
        OnPropertyChanged(nameof(EditorOverheadSummaryText));
        OnPropertyChanged(nameof(EditorStructureText));
        OnPropertyChanged(nameof(EditorPricingGuideText));
        OnPropertyChanged(nameof(EditorLiveCostEquationText));
        OnPropertyChanged(nameof(EditorOutputGuideText));
        OnPropertyChanged(nameof(EditorReadinessText));
        OnPropertyChanged(nameof(EditorActionText));
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
        OnPropertyChanged(nameof(HasSelectedRecipe));
        OnPropertyChanged(nameof(SelectedRecipeName));
        OnPropertyChanged(nameof(SelectedRecipeSummary));
        OnPropertyChanged(nameof(SelectedRecipeBlueprintText));
        OnPropertyChanged(nameof(SelectedRecipeHppText));
        OnPropertyChanged(nameof(SelectedRecipeMaterialCostText));
        OnPropertyChanged(nameof(SelectedRecipeOverheadCostText));
        OnPropertyChanged(nameof(SelectedRecipeTotalCostText));
        OnPropertyChanged(nameof(SelectedRecipeRecommendedPriceText));
        OnPropertyChanged(nameof(SelectedRecipeMarginText));
        OnPropertyChanged(nameof(SelectedRecipeOperationalText));
        OnPropertyChanged(nameof(SelectedRecipePricingGuideText));
        OnPropertyChanged(nameof(SelectedRecipeIngredientCountText));
        OnPropertyChanged(nameof(SelectedRecipeOverheadCountText));
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
                    ingredient.SelectedMaterial = null;
                    ingredient.UnitName = string.Empty;
                    ingredient.UnitCostText = FormattingHelper.FormatCurrency(0);
                    ingredient.LineCostText = FormattingHelper.FormatCurrency(0);
                    ingredient.HasWarehouseWarning = false;
                    ingredient.WarehouseWarningText = string.Empty;
                    continue;
                }

                if (!string.Equals(ingredient.SelectedMaterial?.Id, material.Id, StringComparison.Ordinal))
                {
                    ingredient.SelectedMaterial = material;
                }
                ingredient.UnitName = material.Unit;
                ingredient.UnitCostText = FormattingHelper.FormatCurrency(material.PricePerUnit);
                ingredient.LineCostText = FormattingHelper.FormatCurrency(material.PricePerUnit * ingredient.Quantity);
                ingredient.HasWarehouseWarning = !material.IsTrackedInWarehouse || material.Stock <= 0;
                ingredient.WarehouseWarningText = !material.IsTrackedInWarehouse
                    ? $"{material.CatalogLabel} belum aktif di Gudang. Recipe tetap bisa disimpan, tetapi produksi akan butuh stok gudang lebih dulu."
                    : material.Stock <= 0
                        ? $"{material.CatalogLabel} sedang kosong di Gudang. Recipe tetap bisa disimpan, tetapi produksi akan tertahan sampai stok tersedia."
                        : string.Empty;
            }
        }
    }

    private string BuildDeletePromptText(RecipeCardViewModel card)
    {
        var relatedCombos = DataService.Combos
            .Where(x => x.ProfileId == card.Recipe.ProfileId)
            .Where(x => x.Recipes.Any(recipe => recipe.RecipeId == card.Id))
            .ToList();
        var combosRemoved = relatedCombos.Count(x => x.Recipes.Count == 1);
        var combosAdjusted = relatedCombos.Count - combosRemoved;
        var ingredientCount = card.Recipe.IngredientGroups.SelectMany(x => x.Ingredients).Count();

        return $"Hapus resep {card.Name} secara permanen? Dampak: {relatedCombos.Count} bundle terkait ({combosRemoved} ikut terhapus, {combosAdjusted} disesuaikan), {ingredientCount} komponen bahan, dan seluruh analitik margin resep ini.";
    }

}
