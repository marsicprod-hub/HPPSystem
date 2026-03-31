using System;
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

public sealed partial class CombosViewModel : PageViewModelBase
{
    private const decimal RiskMarginThreshold = 20m;
    private const decimal HealthyMarginThreshold = 35m;

    public CombosViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Paket Bundling";

    public ObservableCollection<Recipe> AvailableRecipes { get; } = new();
    public ObservableCollection<ComboCardViewModel> ComboCards { get; } = new();
    public ObservableCollection<ComboRecipeEntryViewModel> FormRecipes { get; } = new();

    [ObservableProperty]
    private bool _showForm;

    [ObservableProperty]
    private string _formId = string.Empty;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private decimal _formSellingPrice;

    [ObservableProperty]
    private ComboCardViewModel? _pendingDelete;

    [ObservableProperty]
    private ComboCardViewModel? _selectedCombo;

    [ObservableProperty]
    private bool _isLibraryTableMode;

    public bool HasCombos => ComboCards.Count > 0;
    public bool HasSelectedCombo => SelectedCombo is not null;
    public bool IsLibraryCardMode => !IsLibraryTableMode;
    public bool ShowDeletePrompt => PendingDelete is not null;
    public string ComboCountText { get; private set; } = "0 paket";
    public string AverageMarginText { get; private set; } = "0%";
    public string BestComboNameText { get; private set; } = "-";
    public string BestComboMarginText { get; private set; } = "0%";
    public string PortfolioInsightText { get; private set; } = "Belum ada bundling aktif pada profil ini.";
    public string AnalyticsHeadlineText { get; private set; } = "Belum ada portofolio bundle yang bisa dibaca.";
    public string AnalyticsSupportText { get; private set; } = "Tambahkan paket aktif untuk membentuk benchmark margin.";
    public string RiskComboCountText { get; private set; } = "0 paket rawan";
    public string HealthyComboCountText { get; private set; } = "0 paket sehat";
    public string AverageHppText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string BestComboSellingPriceText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string BestComboGuideText { get; private set; } = "Belum ada best bundle.";
    public string SelectedComboName => SelectedCombo?.Name ?? "Belum ada bundle dipilih";
    public string SelectedComboAuditText => SelectedCombo is null
        ? "Pilih bundle untuk audit HPP, harga jual, dan margin dalam mode tabel."
        : $"{SelectedCombo.RecipeCountText} | HPP {SelectedCombo.HppText} | jual {SelectedCombo.SellingPriceText} | margin {SelectedCombo.MarginText}";
    public string FormTitleText => string.IsNullOrWhiteSpace(FormId) ? "Bundle Builder" : $"Bundle Builder: {FormName}";
    public string FormActionText => string.IsNullOrWhiteSpace(FormId) ? "Simpan Bundling" : "Update Bundling";
    public string FormRecipeCountText => $"{FormRecipes.Count(x => !string.IsNullOrWhiteSpace(x.RecipeId) && x.Qty > 0)} resep aktif";
    public string FormPricingGuideText
    {
        get
        {
            var combo = BuildFormCombo();
            var hpp = CalculateFormHpp(combo);
            var margin = combo.SellingPrice <= 0 ? 0 : ((combo.SellingPrice - hpp) / combo.SellingPrice) * 100;
            return $"Bundle HPP {FormattingHelper.FormatCurrency(hpp)} dengan margin {margin:0.#}% pada harga jual saat ini.";
        }
    }
    public string FormHealthText
    {
        get
        {
            var combo = BuildFormCombo();
            var hpp = CalculateFormHpp(combo);
            var margin = combo.SellingPrice <= 0 ? 0 : ((combo.SellingPrice - hpp) / combo.SellingPrice) * 100;
            return margin switch
            {
                < RiskMarginThreshold => "Margin bundling masih rawan. Naikkan harga atau rapikan isi paket.",
                < HealthyMarginThreshold => "Margin bundling cukup, tetapi belum punya buffer promo yang tebal.",
                _ => "Margin bundling sehat dan cukup aman untuk dipakai sebagai anchor promo."
            };
        }
    }
    public string DeletePromptText => PendingDelete is null
        ? string.Empty
        : $"Hapus paket {PendingDelete.Name} secara permanen? Riwayat margin dan pricing bundle ini akan ikut hilang.";

    public string FormHppText
    {
        get
        {
            var combo = BuildFormCombo();
            return FormattingHelper.FormatCurrency(CalculateFormHpp(combo));
        }
    }

    public string FormMarginText
    {
        get
        {
            var combo = BuildFormCombo();
            var hpp = CalculateFormHpp(combo);
            var margin = combo.SellingPrice <= 0 ? 0 : ((combo.SellingPrice - hpp) / combo.SellingPrice) * 100;
            return $"{margin:0.#}%";
        }
    }

    partial void OnFormIdChanged(string value) => NotifyFormStateChanged();
    partial void OnFormNameChanged(string value) => NotifyFormStateChanged();
    partial void OnFormSellingPriceChanged(decimal value) => NotifyFormStateChanged();
    partial void OnSelectedComboChanged(ComboCardViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCombo));
        OnPropertyChanged(nameof(SelectedComboName));
        OnPropertyChanged(nameof(SelectedComboAuditText));
    }
    partial void OnIsLibraryTableModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLibraryCardMode));
    }
    partial void OnPendingDeleteChanged(ComboCardViewModel? value)
    {
        OnPropertyChanged(nameof(ShowDeletePrompt));
        OnPropertyChanged(nameof(DeletePromptText));
    }

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;

        AvailableRecipes.Clear();
        foreach (var recipe in DataService.Recipes.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name))
        {
            AvailableRecipes.Add(recipe);
        }

        ComboCards.Clear();
        foreach (var combo in DataService.Combos.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name))
        {
            var hpp = CostCalculator.CalculateComboHpp(combo, AvailableRecipes, DataService.Materials.Where(x => x.ProfileId == profileId));
            var margin = combo.SellingPrice <= 0 ? 0 : ((combo.SellingPrice - hpp) / combo.SellingPrice) * 100;
            ComboCards.Add(new ComboCardViewModel
            {
                Id = combo.Id,
                Name = combo.Name,
                HppValue = hpp,
                SellingPriceValue = combo.SellingPrice,
                MarginValue = margin,
                Combo = combo,
                HppText = FormattingHelper.FormatCurrency(hpp),
                SellingPriceText = FormattingHelper.FormatCurrency(combo.SellingPrice),
                MarginText = $"{margin:0.#}%",
                HealthLabelText = margin < RiskMarginThreshold ? "Rawan" : margin < HealthyMarginThreshold ? "Butuh buffer" : "Sehat",
                RecipeCountText = $"{combo.Recipes.Sum(recipe => recipe.Qty)} item / {combo.Recipes.Count} resep",
                IsRisky = margin < RiskMarginThreshold,
                IsHealthy = margin >= HealthyMarginThreshold
            });
        }

        if (SelectedCombo is null || !ComboCards.Any(card => card.Id == SelectedCombo.Id))
        {
            SelectedCombo = ComboCards.FirstOrDefault();
        }
        else
        {
            SelectedCombo = ComboCards.First(card => card.Id == SelectedCombo.Id);
        }

        ComboCountText = $"{ComboCards.Count} paket";

        var margins = ComboCards
            .Select(card => card.MarginValue)
            .ToList();
        AverageMarginText = margins.Count == 0 ? "0%" : $"{margins.Average():0.#}%";
        AverageHppText = ComboCards.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(ComboCards.Average(card => card.HppValue));
        RiskComboCountText = $"{ComboCards.Count(card => card.IsRisky)} paket rawan";
        HealthyComboCountText = $"{ComboCards.Count(card => card.IsHealthy)} paket sehat";

        var bestCombo = ComboCards
            .OrderByDescending(card => card.MarginValue)
            .ThenBy(card => card.Name)
            .FirstOrDefault();
        BestComboNameText = bestCombo?.Name ?? "-";
        BestComboMarginText = bestCombo?.MarginText ?? "0%";
        BestComboSellingPriceText = bestCombo?.SellingPriceText ?? FormattingHelper.FormatCurrency(0);
        BestComboGuideText = bestCombo is null
            ? "Belum ada bundle benchmark."
            : $"{bestCombo.Name} berjalan di harga {bestCombo.SellingPriceText} dengan HPP {bestCombo.HppText}.";
        PortfolioInsightText = ComboCards.Count switch
        {
            0 => "Belum ada bundling aktif pada profil ini. Paket promo akan membantu menggerakkan volume dan average basket.",
            _ when bestCombo is null => "Bundling tersedia, tetapi insight margin belum terbentuk.",
            _ => $"Bundle dengan margin terbaik saat ini adalah {bestCombo.Name} di {bestCombo.MarginText}. Gunakan sebagai benchmark untuk promo berikutnya."
        };
        AnalyticsHeadlineText = ComboCards.Count switch
        {
            0 => "Belum ada portofolio bundle aktif.",
            _ when ComboCards.Any(card => card.IsRisky) => "Sebagian bundle masih terlalu tipis untuk dijadikan motor promo.",
            _ => "Portofolio bundle mulai siap dipakai untuk strategi upsell dan promo."
        };
        AnalyticsSupportText = ComboCards.Count switch
        {
            0 => "Bangun satu bundle unggulan dulu untuk membentuk benchmark margin.",
            _ when ComboCards.Any(card => card.IsRisky) => $"Ada {RiskComboCountText} yang perlu review harga atau isi paket.",
            _ => $"Mayoritas bundle sudah sehat. Gunakan {BestComboNameText} sebagai anchor promo."
        };

        OnPropertyChanged(nameof(HasCombos));
        OnPropertyChanged(nameof(ComboCountText));
        OnPropertyChanged(nameof(AverageMarginText));
        OnPropertyChanged(nameof(AverageHppText));
        OnPropertyChanged(nameof(BestComboNameText));
        OnPropertyChanged(nameof(BestComboMarginText));
        OnPropertyChanged(nameof(BestComboSellingPriceText));
        OnPropertyChanged(nameof(BestComboGuideText));
        OnPropertyChanged(nameof(PortfolioInsightText));
        OnPropertyChanged(nameof(AnalyticsHeadlineText));
        OnPropertyChanged(nameof(AnalyticsSupportText));
        OnPropertyChanged(nameof(RiskComboCountText));
        OnPropertyChanged(nameof(HealthyComboCountText));
        OnPropertyChanged(nameof(HasSelectedCombo));
        OnPropertyChanged(nameof(SelectedComboName));
        OnPropertyChanged(nameof(SelectedComboAuditText));
        NotifyFormStateChanged();
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
    private void StartCreate()
    {
        FormId = string.Empty;
        FormName = string.Empty;
        FormSellingPrice = 0;
        FormRecipes.Clear();
        ShowForm = true;
        AddRecipeEntry();
        NotifyFormStateChanged();
    }

    [RelayCommand]
    private void Edit(ComboCardViewModel card)
    {
        FormId = card.Combo.Id;
        FormName = card.Combo.Name;
        FormSellingPrice = card.Combo.SellingPrice;
        FormRecipes.Clear();
        foreach (var recipe in card.Combo.Recipes)
        {
            var entry = new ComboRecipeEntryViewModel
            {
                RecipeId = recipe.RecipeId,
                Qty = recipe.Qty
            };
            entry.PropertyChanged += OnEntryChanged;
            FormRecipes.Add(entry);
        }

        ShowForm = true;
        NotifyFormStateChanged();
    }

    [RelayCommand]
    private void Cancel()
    {
        ShowForm = false;
        FormId = string.Empty;
        FormName = string.Empty;
        FormSellingPrice = 0;
        FormRecipes.Clear();
        NotifyFormStateChanged();
    }

    [RelayCommand]
    private void AddRecipeEntry()
    {
        var entry = new ComboRecipeEntryViewModel();
        entry.PropertyChanged += OnEntryChanged;
        FormRecipes.Add(entry);
        NotifyFormStateChanged();
    }

    [RelayCommand]
    private void RemoveRecipeEntry(ComboRecipeEntryViewModel entry)
    {
        FormRecipes.Remove(entry);
        NotifyFormStateChanged();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var combo = BuildFormCombo();
        if (string.IsNullOrWhiteSpace(combo.Name) || !combo.Recipes.Any())
        {
            Error("Nama paket dan isi bundling wajib diisi.");
            return;
        }

        await DataService.SaveComboAsync(combo);
        Success("Paket bundling disimpan.");
        Cancel();
    }

    [RelayCommand]
    private async Task DeleteAsync(ComboCardViewModel combo)
    {
        await DataService.DeleteComboAsync(combo.Id);
        Success($"Bundling {combo.Name} dihapus.");
        PendingDelete = null;
    }

    [RelayCommand]
    private void RequestDelete(ComboCardViewModel combo)
    {
        PendingDelete = combo;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
    }

    private Combo BuildFormCombo()
    {
        return new Combo
        {
            Id = string.IsNullOrWhiteSpace(FormId) ? Guid.NewGuid().ToString("N") : FormId,
            Name = FormName.Trim(),
            SellingPrice = FormSellingPrice,
            ProfileId = DataService.Settings.ActiveProfileId,
            Recipes = FormRecipes
                .Where(x => !string.IsNullOrWhiteSpace(x.RecipeId) && x.Qty > 0)
                .Select(x => new ComboRecipe
                {
                    RecipeId = x.RecipeId,
                    Qty = x.Qty
                })
                .ToList()
        };
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyFormStateChanged();
    }

    private decimal CalculateFormHpp(Combo combo)
    {
        return CostCalculator.CalculateComboHpp(
            combo,
            AvailableRecipes,
            DataService.Materials.Where(x => x.ProfileId == DataService.Settings.ActiveProfileId));
    }

    private void NotifyFormStateChanged()
    {
        OnPropertyChanged(nameof(FormTitleText));
        OnPropertyChanged(nameof(FormActionText));
        OnPropertyChanged(nameof(FormRecipeCountText));
        OnPropertyChanged(nameof(FormHppText));
        OnPropertyChanged(nameof(FormMarginText));
        OnPropertyChanged(nameof(FormPricingGuideText));
        OnPropertyChanged(nameof(FormHealthText));
    }
}
