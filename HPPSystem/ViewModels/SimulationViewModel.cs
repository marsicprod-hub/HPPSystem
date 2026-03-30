using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HPPSystem.Helpers;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class SimulationViewModel : PageViewModelBase
{
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

            Results.Add(new SimulationResultViewModel
            {
                RecipeName = recipe.Name,
                CurrentHppText = FormattingHelper.FormatCurrency(currentHpp),
                SimulatedHppText = FormattingHelper.FormatCurrency(simulatedHpp),
                RemainingMarginText = $"{remainingMargin:0.#}%",
                IsDanger = remainingMargin < 15
            });
        }

        RecipeCountText = $"{Results.Count} resep";
        DangerCountText = $"{Results.Count(x => x.IsDanger)} resep rawan";
        SafeCountText = $"{Results.Count(x => !x.IsDanger)} resep aman";

        var simulatedValues = Results
            .Select(result => ParseCurrency(result.SimulatedHppText))
            .ToList();
        AverageSimulatedHppText = simulatedValues.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(simulatedValues.Average());

        var worstCase = Results
            .OrderBy(result => ParsePercent(result.RemainingMarginText))
            .ThenBy(result => result.RecipeName)
            .FirstOrDefault();
        WorstRecipeNameText = worstCase?.RecipeName ?? "-";
        WorstRecipeMarginText = worstCase?.RemainingMarginText ?? "0%";
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
        OnPropertyChanged(nameof(WorstRecipeNameText));
        OnPropertyChanged(nameof(WorstRecipeMarginText));
        OnPropertyChanged(nameof(SimulationInsightText));
    }

    private static decimal ParseCurrency(string value)
    {
        var digits = new string(value.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return 0;
        }

        digits = digits.Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(digits, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static decimal ParsePercent(string value)
    {
        var trimmed = value.Replace("%", string.Empty);
        return decimal.TryParse(trimmed, out var parsed) ? parsed : 0;
    }
}
