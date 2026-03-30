using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HPPSystem.Services;
using HPPSystem.ViewModels;

namespace HPPSystem;

public partial class App : Application
{
    public void ApplyTheme(bool isDarkMode)
    {
        RequestedThemeVariant = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
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
            var recipes = new RecipesViewModel(dataService, notifications);
            var combos = new CombosViewModel(dataService, notifications);
            var pos = new PosViewModel(dataService, notifications);
            var bookkeeping = new BookkeepingViewModel(dataService, notifications);
            var simulation = new SimulationViewModel(dataService, notifications);
            var profile = new ProfileViewModel(dataService, notifications);
            var settings = new SettingsViewModel(dataService, notifications);
            ApplyTheme(dataService.Settings.IsDarkMode);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    dataService,
                    notifications,
                    dashboard,
                    materials,
                    recipes,
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
