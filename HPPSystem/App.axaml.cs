using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HPPSystem.Helpers;
using HPPSystem.Services;
using HPPSystem.ViewModels;

namespace HPPSystem;

public partial class App : Application
{
    public void ApplyTheme(bool isDarkMode)
    {
        RequestedThemeVariant = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public void ApplyFontSize(string preset)
    {
        Resources["AppBodyFontSize"] = FontSizingHelper.GetBaseFontSize(preset);
        Resources["AppCaptionFontSize"] = FontSizingHelper.GetCaptionFontSize(preset);
        Resources["AppSectionFontSize"] = FontSizingHelper.GetSectionFontSize(preset);
        Resources["AppCardTitleFontSize"] = FontSizingHelper.GetCardTitleFontSize(preset);
        Resources["AppDisplaySmallFontSize"] = FontSizingHelper.GetDisplaySmallFontSize(preset);
        Resources["AppDisplayFontSize"] = FontSizingHelper.GetDisplayFontSize(preset);
        Resources["AppPageTitleFontSize"] = FontSizingHelper.GetPageTitleFontSize(preset);
        Resources["AppHeroFontSize"] = FontSizingHelper.GetHeroFontSize(preset);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataService = new LocalJsonDataService();
            var notifications = new NotificationService();

            var dashboard = new DashboardViewModel(dataService, notifications);
            var materials = new MaterialsViewModel(dataService, notifications);
            var warehouse = new WarehouseViewModel(dataService, notifications);
            var recipes = new RecipesViewModel(dataService, notifications);
            var production = new ProductionViewModel(dataService, notifications);
            var combos = new CombosViewModel(dataService, notifications);
            var pos = new PosViewModel(dataService, notifications);
            var bookkeeping = new BookkeepingViewModel(dataService, notifications);
            var simulation = new SimulationViewModel(dataService, notifications);
            var profile = new ProfileViewModel(dataService, notifications);
            var settings = new SettingsViewModel(dataService, notifications);
            ApplyTheme(dataService.Settings.IsDarkMode);
            ApplyFontSize(dataService.Settings.FontSizePreset);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    dataService,
                    notifications,
                    dashboard,
                    materials,
                    warehouse,
                    recipes,
                    production,
                    combos,
                    pos,
                    bookkeeping,
                    simulation,
                    profile,
                    settings)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
