using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HPPSystem.Helpers;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class SimulationViewModel : PageViewModelBase
{
    private const decimal DangerMarginThreshold = 15m;
    private const decimal WarningMarginThreshold = 25m;

    public SimulationViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Simulasi Inflasi";

    public ObservableCollection<SimulationResultViewModel> Results { get; } = new();

    [ObservableProperty]
    private decimal _inflationRate = 15;

    partial void OnInflationRateChanged(decimal value) => Refresh();

    public bool HasResults => Results.Count > 0;
    public string InflationRateText => $"{InflationRate:0.#}%";
    public string RecipeCountText { get; private set; } = "0 resep";
    public string DangerCountText { get; private set; } = "0 resep";
    public string SafeCountText { get; private set; } = "0 resep";
    public string AverageSimulatedHppText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string WorstRecipeNameText { get; private set; } = "-";
    public string WorstRecipeMarginText { get; private set; } = "0%";
    public string SimulationInsightText { get; private set; } = "Belum ada resep yang bisa dianalisis.";
    public string SimulationHeadlineText { get; private set; } = "Belum ada tekanan biaya yang bisa dibaca.";
    public string SimulationSupportText { get; private set; } = "Tambahkan resep aktif untuk mengukur dampak inflasi.";
    public string MarginSafetyText { get; private set; } = "Belum ada safety cue.";
    public string AverageDeltaText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string BestShieldRecipeText { get; private set; } = "-";
    public string BestShieldMarginText { get; private set; } = "0%";

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var materials = DataService.Materials.Where(x => x.ProfileId == profileId).ToList();
        var recipes = DataService.Recipes.Where(x => x.ProfileId == profileId).OrderBy(x => x.Name).ToList();

        Results.Clear();
        foreach (var recipe in recipes)
        {
            var currentHpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, materials);
            var simulatedMaterials = materials
                .Select(x => new Models.Material
                {
                    Id = x.Id,
                    Name = x.Name,
                    Price = x.Price,
                    Weight = x.Weight,
                    Unit = x.Unit,
                    Stock = x.Stock,
                    ProfileId = x.ProfileId,
                    PricePerUnit = x.PricePerUnit * (1 + (InflationRate / 100m))
                })
                .ToList();

            var simulatedHpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, simulatedMaterials);
            var fixedSellingPrice = CostCalculator.CalculateRecommendedSellingPrice(currentHpp, recipe.TargetMargin);
            var remainingMargin = fixedSellingPrice <= 0 ? 0 : ((fixedSellingPrice - simulatedHpp) / fixedSellingPrice) * 100;
            var delta = simulatedHpp - currentHpp;
            var isDanger = remainingMargin < DangerMarginThreshold;
            var isWarning = !isDanger && remainingMargin < WarningMarginThreshold;

            Results.Add(new SimulationResultViewModel
            {
                RecipeName = recipe.Name,
                CurrentHppValue = currentHpp,
                SimulatedHppValue = simulatedHpp,
                RemainingMarginValue = remainingMargin,
                HppDeltaValue = delta,
                CurrentHppText = FormattingHelper.FormatCurrency(currentHpp),
                SimulatedHppText = FormattingHelper.FormatCurrency(simulatedHpp),
                RemainingMarginText = $"{remainingMargin:0.#}%",
                HppDeltaText = $"+{FormattingHelper.FormatCurrency(delta)}",
                StatusText = isDanger ? "Rawan" : isWarning ? "Waspada" : "Aman",
                IsDanger = isDanger,
                IsWarning = isWarning,
                IsSafe = !isDanger && !isWarning
            });
        }

        SortResultsByRisk();
        RecipeCountText = $"{Results.Count} resep";
        DangerCountText = $"{Results.Count(x => x.IsDanger)} resep rawan";
        SafeCountText = $"{Results.Count(x => x.IsSafe)} resep aman";

        var simulatedValues = Results
            .Select(result => result.SimulatedHppValue)
            .ToList();
        AverageSimulatedHppText = simulatedValues.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(simulatedValues.Average());
        AverageDeltaText = Results.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(Results.Average(x => x.HppDeltaValue));

        var worstCase = Results
            .OrderBy(result => result.RemainingMarginValue)
            .ThenBy(result => result.RecipeName)
            .FirstOrDefault();
        WorstRecipeNameText = worstCase?.RecipeName ?? "-";
        WorstRecipeMarginText = worstCase?.RemainingMarginText ?? "0%";

        var bestShield = Results
            .OrderByDescending(result => result.RemainingMarginValue)
            .ThenBy(result => result.RecipeName)
            .FirstOrDefault();
        BestShieldRecipeText = bestShield?.RecipeName ?? "-";
        BestShieldMarginText = bestShield?.RemainingMarginText ?? "0%";

        MarginSafetyText = Results.Count switch
        {
            0 => "Belum ada margin yang bisa diklasifikasikan.",
            _ when Results.Any(x => x.IsDanger) => $"Ada {DangerCountText} pada zona rawan di bawah {DangerMarginThreshold:0.#}%.",
            _ when Results.Any(x => x.IsWarning) => $"Belum ada yang rawan, tetapi beberapa resep mendekati batas aman {WarningMarginThreshold:0.#}%.",
            _ => "Seluruh resep masih berada di zona aman."
        };

        SimulationHeadlineText = Results.Count switch
        {
            0 => "Belum ada resep untuk dibaca dalam simulasi.",
            _ when Results.Any(x => x.IsDanger) => "Inflasi mulai menekan sebagian resep secara nyata.",
            _ when Results.Any(x => x.IsWarning) => "Margin masih bertahan, tetapi buffer mulai menipis.",
            _ => "Skenario inflasi saat ini masih aman untuk library resep aktif."
        };
        SimulationSupportText = Results.Count switch
        {
            0 => "Tambahkan resep aktif untuk melihat tekanan HPP dan sisa margin.",
            _ when worstCase is null => "Belum ada recipe risk cue yang bisa diturunkan.",
            _ => $"Resep paling tertekan saat ini {WorstRecipeNameText} dengan sisa margin {WorstRecipeMarginText}, sementara penyangga terbaik dipegang {BestShieldRecipeText}."
        };

        SimulationInsightText = Results.Count switch
            {
                0 => "Belum ada resep yang bisa dianalisis. Tambahkan resep aktif untuk melihat tekanan margin saat inflasi bahan naik.",
                _ when Results.All(x => !x.IsDanger) => $"Pada skenario inflasi {InflationRateText}, seluruh resep masih berada di zona aman.",
                _ => $"Pada skenario inflasi {InflationRateText}, resep paling tertekan adalah {WorstRecipeNameText} dengan sisa margin {WorstRecipeMarginText}."
        };

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(InflationRateText));
        OnPropertyChanged(nameof(RecipeCountText));
        OnPropertyChanged(nameof(DangerCountText));
        OnPropertyChanged(nameof(SafeCountText));
        OnPropertyChanged(nameof(AverageSimulatedHppText));
        OnPropertyChanged(nameof(AverageDeltaText));
        OnPropertyChanged(nameof(WorstRecipeNameText));
        OnPropertyChanged(nameof(WorstRecipeMarginText));
        OnPropertyChanged(nameof(BestShieldRecipeText));
        OnPropertyChanged(nameof(BestShieldMarginText));
        OnPropertyChanged(nameof(SimulationInsightText));
        OnPropertyChanged(nameof(SimulationHeadlineText));
        OnPropertyChanged(nameof(SimulationSupportText));
        OnPropertyChanged(nameof(MarginSafetyText));
    }

    private void SortResultsByRisk()
    {
        var ordered = Results
            .OrderByDescending(x => x.IsDanger)
            .ThenByDescending(x => x.IsWarning)
            .ThenBy(x => x.RemainingMarginValue)
            .ThenBy(x => x.RecipeName)
            .ToList();

        Results.Clear();
        foreach (var result in ordered)
        {
            Results.Add(result);
        }
    }
}
