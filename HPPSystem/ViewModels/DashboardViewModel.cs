using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace HPPSystem.ViewModels;

public sealed partial class DashboardViewModel : PageViewModelBase
{
    public DashboardViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        Refresh();
    }

    public override string Title => "Dashboard Analitik";

    public ObservableCollection<RecipeCardViewModel> TopMarginRecipes { get; } = new();
    public ObservableCollection<HPPSystem.Models.Material> LowStockMaterials { get; } = new();
    public ObservableCollection<SaleRowViewModel> RecentSales { get; } = new();
    public ObservableCollection<SalesPointViewModel> Last7DaysSales { get; } = new();

    public int MaterialCount { get; private set; }
    public int RecipeCount { get; private set; }
    public int TotalItemsSold { get; private set; }
    public string TotalSalesText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TotalProfitText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TotalItemsSoldText => $"{TotalItemsSold} porsi";
    public string LowStockCountText => $"{LowStockMaterials.Count} bahan kritis";
    public string RecentTransactionCountText => $"{RecentSales.Count} transaksi terbaru";
    public string TotalInventoryText { get; private set; } = "0 unit";
    public string AvgDailySalesText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string AverageTicketText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string AverageUnitsPerTransactionText { get; private set; } = "0 porsi/transaksi";
    public string ProfitabilityRateText { get; private set; } = "0%";
    public string BestDayLabel { get; private set; } = "-";
    public string BestDaySalesText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TopRecipeName { get; private set; } = "-";
    public string TopRecipeMarginText { get; private set; } = "0%";
    public string TopRecipeRecommendedPriceText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string TopRecipeHppText { get; private set; } = FormattingHelper.FormatCurrency(0);
    public string InventoryHealthText { get; private set; } = "Stok aman";
    public string DashboardInsightText { get; private set; } = "Belum ada cukup data untuk analitik.";
    public string ExecutiveHeadlineText { get; private set; } = "Belum ada data penjualan aktif.";
    public string ExecutiveSupportText { get; private set; } = "Mulai catat transaksi untuk membentuk baseline insight.";
    public string RevenuePulseText { get; private set; } = "Belum ada momentum penjualan.";
    public string RevenuePulseDetailText { get; private set; } = "Perlu histori transaksi untuk membaca tren omzet.";
    public string BestDaySummaryText { get; private set; } = "Belum ada hari terbaik yang bisa dipetakan.";
    public string TopRecipeGuideText { get; private set; } = "Belum ada recipe benchmark.";
    public string InventoryActionText { get; private set; } = "Belum ada tindakan inventaris yang mendesak.";
    public string LowStockSummaryText { get; private set; } = "Tidak ada bahan kritis.";
    public string RecentSalesSummaryText { get; private set; } = "Belum ada transaksi terbaru.";
    public bool HasCriticalInventory => LowStockMaterials.Count >= 3;
    public ISeries[] SalesSeries { get; private set; } = [];
    public Axis[] XAxes { get; private set; } = [];
    public Axis[] YAxes { get; private set; } = [];
    public bool HasTopRecipes => TopMarginRecipes.Count > 0;
    public bool HasLowStockMaterials => LowStockMaterials.Count > 0;
    public bool HasRecentSales => RecentSales.Count > 0;
    public bool HasSalesHistory => Last7DaysSales.Any(x => x.Total > 0);
    public bool IsAdvancedMode => DataService.Settings.IsAdvancedMode;
    public bool ShowAdvancedSection => IsAdvancedMode;
    public bool ShowPromoSection => !IsAdvancedMode;

    public override void Refresh()
    {
        var profileId = DataService.Settings.ActiveProfileId;
        var materials = DataService.Materials.Where(x => x.ProfileId == profileId).ToList();
        var recipes = DataService.Recipes.Where(x => x.ProfileId == profileId).ToList();
        var sales = DataService.Sales.Where(x => x.ProfileId == profileId).ToList();
        var totalSales = sales.Sum(x => x.TotalPrice);
        var totalProfit = sales.Sum(x => x.TotalProfit);

        MaterialCount = materials.Count;
        RecipeCount = recipes.Count;
        TotalItemsSold = sales.Sum(x => x.Qty);
        TotalSalesText = FormattingHelper.FormatCurrency(totalSales);
        TotalProfitText = FormattingHelper.FormatCurrency(totalProfit);
        TotalInventoryText = $"{materials.Sum(x => x.Stock):0.##} unit";
        AverageTicketText = sales.Count == 0
            ? FormattingHelper.FormatCurrency(0)
            : FormattingHelper.FormatCurrency(totalSales / sales.Count);
        AverageUnitsPerTransactionText = sales.Count == 0
            ? "0 porsi/transaksi"
            : $"{(decimal)TotalItemsSold / sales.Count:0.#} porsi/transaksi";
        ProfitabilityRateText = totalSales <= 0
            ? "0%"
            : $"{(totalProfit / totalSales) * 100m:0.#}%";

        TopMarginRecipes.Clear();
        foreach (var recipe in recipes.OrderByDescending(x => x.TargetMargin).Take(5))
        {
            var costBreakdown = CostCalculator.CalculateRecipeCost(recipe, materials);
            var total = costBreakdown.TotalCost;
            var hpp = CostCalculator.CalculateRecipeHppPerPortion(recipe, materials);
            var recommendedPrice = CostCalculator.CalculateRecommendedSellingPrice(hpp, recipe.TargetMargin);
            TopMarginRecipes.Add(new RecipeCardViewModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Recipe = recipe,
                HppValue = hpp,
                MarginValue = recipe.TargetMargin,
                HppText = FormattingHelper.FormatCurrency(hpp),
                MaterialCostText = FormattingHelper.FormatCurrency(costBreakdown.MaterialCost),
                OverheadCostText = FormattingHelper.FormatCurrency(costBreakdown.OverheadCost),
                TotalCostText = FormattingHelper.FormatCurrency(total),
                RecommendedPriceText = FormattingHelper.FormatCurrency(recommendedPrice),
                MarginText = $"{recipe.TargetMargin:0.#}%",
                PortionsText = $"{recipe.Portions} porsi",
                IngredientCountText = $"{recipe.IngredientGroups.SelectMany(x => x.Ingredients).Count()} bahan",
                GroupCountText = $"{recipe.IngredientGroups.Count} kelompok",
                OverheadCountText = $"{recipe.OverheadCosts.Count} overhead"
            });
        }

        var topRecipe = TopMarginRecipes.FirstOrDefault();
        TopRecipeName = topRecipe?.Name ?? "-";
        TopRecipeMarginText = topRecipe?.MarginText ?? "0%";
        TopRecipeRecommendedPriceText = topRecipe?.RecommendedPriceText ?? FormattingHelper.FormatCurrency(0);
        TopRecipeHppText = topRecipe?.HppText ?? FormattingHelper.FormatCurrency(0);

        LowStockMaterials.Clear();
        foreach (var item in materials.Where(x => x.Stock <= 5).OrderBy(x => x.Stock).Take(6))
        {
            LowStockMaterials.Add(item);
        }

        InventoryHealthText = LowStockMaterials.Count switch
        {
            0 => "Stok aman",
            <= 2 => "Perlu monitor",
            _ => "Perlu restock"
        };
        LowStockSummaryText = LowStockMaterials.Count switch
        {
            0 => "Semua bahan utama masih di atas batas kritis.",
            1 => $"Fokus restock pertama: {LowStockMaterials[0].Name}.",
            _ => $"Ada {LowStockMaterials.Count} bahan dengan stok kritis yang perlu diprioritaskan."
        };
        InventoryActionText = LowStockMaterials.FirstOrDefault() is { } focusMaterial
            ? $"{focusMaterial.Name} tersisa {focusMaterial.Stock:0.##} {focusMaterial.Unit}."
            : "Belum ada restock prioritas untuk hari ini.";

        RecentSales.Clear();
        foreach (var sale in sales.OrderByDescending(x => DateTime.TryParse(x.Date, out var dt) ? dt : DateTime.MinValue).Take(8))
        {
            RecentSales.Add(new SaleRowViewModel
            {
                ItemName = sale.ItemName,
                Qty = sale.Qty,
                DateText = FormattingHelper.FormatDateTime(sale.Date),
                TotalPriceText = FormattingHelper.FormatCurrency(sale.TotalPrice),
                TotalProfitText = FormattingHelper.FormatCurrency(sale.TotalProfit)
            });
        }
        RecentSalesSummaryText = RecentSales.Count switch
        {
            0 => "Belum ada transaksi terbaru yang bisa dibaca.",
            _ => $"{RecentSales.Count} transaksi terbaru sudah siap dibaca untuk validasi ritme penjualan."
        };

        Last7DaysSales.Clear();
        var today = DateTime.Today;
        var dailyTotals = new List<decimal>();
        for (var i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var total = sales
                .Where(x => DateTime.TryParse(x.Date, out var parsed) && parsed.Date == date.Date)
                .Sum(x => x.TotalPrice);
            dailyTotals.Add(total);
        }

        var max = dailyTotals.DefaultIfEmpty(0).Max();
        for (var i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var total = dailyTotals[6 - i];

            Last7DaysSales.Add(new SalesPointViewModel
            {
                Label = date.ToString("ddd dd"),
                Total = total
                ,
                BarValue = max <= 0 ? 0 : (double)(total / max * 100m),
                PercentText = max <= 0 ? "0%" : $"{(total / max * 100m):0.#}%"
            });
        }

        SalesSeries =
        [
            new LineSeries<decimal>
            {
                Values = dailyTotals.ToArray(),
                LineSmoothness = 0.7,
                GeometrySize = 10,
                GeometryStroke = new SolidColorPaint(new SKColor(0xFF, 0xF4, 0xEC)) { StrokeThickness = 3 },
                GeometryFill = new SolidColorPaint(new SKColor(0xFF, 0xFF, 0xFF)),
                Fill = new LinearGradientPaint(
                [
                    new SKColor(0x96, 0x6B, 0x9D, 90),
                    new SKColor(0x96, 0x6B, 0x9D, 15)
                ],
                new SKPoint(0, 0),
                new SKPoint(0, 1)),
                Stroke = new SolidColorPaint(new SKColor(0x96, 0x6B, 0x9D)) { StrokeThickness = 4 },
                AnimationsSpeed = TimeSpan.FromMilliseconds(650),
                EasingFunction = EasingFunctions.CubicOut,
                Name = "Sales"
            }
        ];

        XAxes =
        [
            new Axis
            {
                Labels = Last7DaysSales.Select(x => x.Label).ToArray(),
                LabelsPaint = new SolidColorPaint(new SKColor(0x96, 0x7E, 0x87)),
                TextSize = 13,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0xEE, 0xDD, 0xD0)) { StrokeThickness = 1 },
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(new SKColor(0xEE, 0xDD, 0xD0)) { StrokeThickness = 1 }
            }
        ];

        YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(new SKColor(0x96, 0x7E, 0x87)),
                TextSize = 12,
                Labeler = value => FormattingHelper.FormatCurrency((decimal)value),
                MinStep = max <= 0 ? 1 : (double)(max / 4m),
                SeparatorsPaint = new SolidColorPaint(new SKColor(0xF0, 0xE2, 0xD7)) { StrokeThickness = 1 }
            }
        ];

        AvgDailySalesText = FormattingHelper.FormatCurrency(dailyTotals.DefaultIfEmpty(0).Average());
        var bestDayIndex = dailyTotals.IndexOf(max);
        if (bestDayIndex >= 0)
        {
            var bestDay = today.AddDays(-(6 - bestDayIndex));
            BestDayLabel = bestDay.ToString("dddd, dd MMM");
            BestDaySalesText = FormattingHelper.FormatCurrency(max);
        }
        else
        {
            BestDayLabel = "-";
            BestDaySalesText = FormattingHelper.FormatCurrency(0);
        }
        BestDaySummaryText = max <= 0
            ? "Belum ada peak day yang terbentuk."
            : $"{BestDayLabel} menjadi puncak omzet dengan {BestDaySalesText}.";

        var lastThreeDaysAverage = dailyTotals.Skip(Math.Max(0, dailyTotals.Count - 3)).DefaultIfEmpty(0).Average();
        RevenuePulseText = sales.Count == 0
            ? "Belum ada momentum penjualan."
            : lastThreeDaysAverage >= dailyTotals.DefaultIfEmpty(0).Average()
                ? "Momentum penjualan stabil"
                : "Momentum penjualan melambat";
        RevenuePulseDetailText = sales.Count == 0
            ? "Mulai checkout di POS untuk membaca pulse omzet harian."
            : $"Rata-rata 3 hari terakhir {FormattingHelper.FormatCurrency(lastThreeDaysAverage)} per hari.";

        TopRecipeGuideText = topRecipe is null
            ? "Belum ada resep benchmark yang bisa dibaca."
            : $"{topRecipe.Name} memimpin dengan margin {topRecipe.MarginText} dan rekomendasi jual {topRecipe.RecommendedPriceText}.";

        ExecutiveHeadlineText = sales.Count switch
        {
            0 => "Belum ada data transaksi aktif.",
            _ when LowStockMaterials.Count >= 3 => "Penjualan berjalan, tetapi inventaris mulai menekan operasi.",
            _ when totalProfit <= 0 => "Omzet sudah masuk, namun profit belum sehat.",
            _ => "Operasi harian terlihat stabil dan bisa diputuskan lebih cepat."
        };
        ExecutiveSupportText = sales.Count switch
        {
            0 => "Aktifkan POS dan pembukuan untuk membentuk baseline analitik.",
            _ when LowStockMaterials.Count >= 3 => $"Prioritas hari ini: restock {LowStockMaterials[0].Name} dan review bahan kritis lainnya.",
            _ when totalProfit <= 0 => "Periksa pricing menu dan struktur biaya karena laba belum terbentuk.",
            _ => $"Fokus berikutnya: pertahankan menu unggulan {TopRecipeName} sambil menjaga stok aman."
        };

        DashboardInsightText = sales.Count == 0
            ? "Belum ada transaksi POS pada profil ini. Dashboard akan makin informatif setelah penjualan mulai tercatat."
            : $"Hari terbaik saat ini {BestDayLabel} dengan omzet {BestDaySalesText}. Resep margin tertinggi adalah {TopRecipeName} di {TopRecipeMarginText}.";

        OnPropertyChanged(nameof(MaterialCount));
        OnPropertyChanged(nameof(RecipeCount));
        OnPropertyChanged(nameof(TotalItemsSold));
        OnPropertyChanged(nameof(TotalItemsSoldText));
        OnPropertyChanged(nameof(LowStockCountText));
        OnPropertyChanged(nameof(RecentTransactionCountText));
        OnPropertyChanged(nameof(TotalSalesText));
        OnPropertyChanged(nameof(TotalProfitText));
        OnPropertyChanged(nameof(TotalInventoryText));
        OnPropertyChanged(nameof(AvgDailySalesText));
        OnPropertyChanged(nameof(AverageTicketText));
        OnPropertyChanged(nameof(AverageUnitsPerTransactionText));
        OnPropertyChanged(nameof(ProfitabilityRateText));
        OnPropertyChanged(nameof(BestDayLabel));
        OnPropertyChanged(nameof(BestDaySalesText));
        OnPropertyChanged(nameof(TopRecipeName));
        OnPropertyChanged(nameof(TopRecipeMarginText));
        OnPropertyChanged(nameof(TopRecipeRecommendedPriceText));
        OnPropertyChanged(nameof(TopRecipeHppText));
        OnPropertyChanged(nameof(InventoryHealthText));
        OnPropertyChanged(nameof(DashboardInsightText));
        OnPropertyChanged(nameof(ExecutiveHeadlineText));
        OnPropertyChanged(nameof(ExecutiveSupportText));
        OnPropertyChanged(nameof(RevenuePulseText));
        OnPropertyChanged(nameof(RevenuePulseDetailText));
        OnPropertyChanged(nameof(BestDaySummaryText));
        OnPropertyChanged(nameof(TopRecipeGuideText));
        OnPropertyChanged(nameof(InventoryActionText));
        OnPropertyChanged(nameof(LowStockSummaryText));
        OnPropertyChanged(nameof(RecentSalesSummaryText));
        OnPropertyChanged(nameof(HasCriticalInventory));
        OnPropertyChanged(nameof(SalesSeries));
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
        OnPropertyChanged(nameof(HasTopRecipes));
        OnPropertyChanged(nameof(HasLowStockMaterials));
        OnPropertyChanged(nameof(HasRecentSales));
        OnPropertyChanged(nameof(HasSalesHistory));
        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(ShowAdvancedSection));
        OnPropertyChanged(nameof(ShowPromoSection));
    }
}
