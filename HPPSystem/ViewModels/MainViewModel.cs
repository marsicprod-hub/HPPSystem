using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
using HPPSystem.Models;
using HPPSystem.Services;
using Lucide.Avalonia;

namespace HPPSystem.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IDataService _dataService;

    public MainViewModel(
        IDataService dataService,
        NotificationService notifications,
        DashboardViewModel dashboard,
        MaterialsViewModel materials,
        WarehouseViewModel warehouse,
        RecipesViewModel recipes,
        ProductionViewModel production,
        CombosViewModel combos,
        PosViewModel pos,
        BookkeepingViewModel bookkeeping,
        SimulationViewModel simulation,
        ProfileViewModel profile,
        SettingsViewModel settings)
    {
        _dataService = dataService;
        Notifications = notifications;

        Dashboard = dashboard;
        Materials = materials;
        Warehouse = warehouse;
        Recipes = recipes;
        Production = production;
        Combos = combos;
        Pos = pos;
        Bookkeeping = bookkeeping;
        Simulation = simulation;
        Profile = profile;
        Settings = settings;

        PrimaryNavigation = new ObservableCollection<NavigationItemViewModel>
        {
            new(AppPage.Dashboard, "Dashboard", Navigate),
            new(AppPage.Materials, "Material", Navigate),
            new(AppPage.Warehouse, "Gudang", Navigate),
            new(AppPage.Recipes, "Recipes", Navigate),
            new(AppPage.Production, "Produksi", Navigate)
        };

        recipes.RequestProductionSetup = recipeId =>
        {
            Production.PrepareDraftFromRecipe(recipeId);
            Navigate(AppPage.Production);
        };
        production.RequestBookkeepingForShortage = orderId =>
        {
            Bookkeeping.PrepareShortageTemplateFromProductionOrder(orderId);
            Navigate(AppPage.Bookkeeping);
        };

        AdvancedNavigation = new ObservableCollection<NavigationItemViewModel>
        {
            new(AppPage.Combos, "Bundles", Navigate),
            new(AppPage.Pos, "POS", Navigate),
            new(AppPage.Bookkeeping, "Bookkeeping", Navigate),
            new(AppPage.Simulation, "Simulation", Navigate)
        };

        SystemNavigation = new ObservableCollection<NavigationItemViewModel>
        {
            new(AppPage.Profile, "Profile", Navigate),
            new(AppPage.Settings, "Settings", Navigate)
        };

        _dataService.StateChanged += (_, _) => RefreshShell();
        CurrentView = Dashboard;
        CurrentPage = AppPage.Dashboard;
        _ = InitializeAsync();
    }

    public NotificationService Notifications { get; }
    public ObservableCollection<ToastMessage> Toasts => Notifications.Toasts;

    public DashboardViewModel Dashboard { get; }
    public MaterialsViewModel Materials { get; }
    public WarehouseViewModel Warehouse { get; }
    public RecipesViewModel Recipes { get; }
    public ProductionViewModel Production { get; }
    public CombosViewModel Combos { get; }
    public PosViewModel Pos { get; }
    public BookkeepingViewModel Bookkeeping { get; }
    public SimulationViewModel Simulation { get; }
    public ProfileViewModel Profile { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavigationItemViewModel> PrimaryNavigation { get; }
    public ObservableCollection<NavigationItemViewModel> AdvancedNavigation { get; }
    public ObservableCollection<NavigationItemViewModel> SystemNavigation { get; }
    public ObservableCollection<Profile> Profiles => _dataService.Profiles;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private AppPage _currentPage;

    [ObservableProperty]
    private Profile? _selectedProfile;

    public bool IsAdvancedMode => _dataService.Settings.IsAdvancedMode;
    public bool IsDarkMode => _dataService.Settings.IsDarkMode;
    public string EditionLabel => IsAdvancedMode ? "ENTERPRISE" : "BASIC";
    public string ActiveProfileName => SelectedProfile?.BusinessName ?? "Workspace";
    public string ActiveOwnerName => string.IsNullOrWhiteSpace(SelectedProfile?.OwnerName) ? "Marketing Administrator" : SelectedProfile!.OwnerName;
    public bool ShowAdvancedNavigation => IsAdvancedMode;
    public string DataStorePath => _dataService.DataStorePath;
    public string CurrentPageTitle => CurrentView is PageViewModelBase page ? page.Title : "Workspace";
    public string CurrentPageDescription => CurrentPage switch
    {
        AppPage.Dashboard => "Overview and portfolio movement.",
        AppPage.Materials => "Master catalog for materials.",
        AppPage.Warehouse => "Stock management and warehouse audit.",
        AppPage.Recipes => "Costing and recipe builder.",
        AppPage.Production => "Production queue and stock deduction control.",
        AppPage.Combos => "Bundle and offer builder.",
        AppPage.Pos => "Transaction terminal.",
        AppPage.Bookkeeping => "Cash movement and expenses.",
        AppPage.Simulation => "Price pressure simulation.",
        AppPage.Profile => "Workspace identity and branches.",
        AppPage.Settings => "System preferences and backup.",
        _ => "Desktop workspace."
    };
    public string WorkspaceStatusText => $"{EditionLabel} | {ActiveProfileName}";
    public string NavigationSummaryText => $"{PrimaryNavigation.Count + (ShowAdvancedNavigation ? AdvancedNavigation.Count : 0) + SystemNavigation.Count} modules";
    public string ProfileInitial => string.IsNullOrWhiteSpace(ActiveProfileName) ? "H" : ActiveProfileName.Substring(0, 1).ToUpperInvariant();
    public string ThemeLabel => IsDarkMode ? "Dark" : "Light";
    public LucideIconKind ThemeIcon => IsDarkMode ? LucideIconKind.SunMedium : LucideIconKind.MoonStar;
    public double AppFontSize => FontSizingHelper.GetBaseFontSize(_dataService.Settings.FontSizePreset);
    public string FontSizeLabel => FontSizingHelper.GetLabel(_dataService.Settings.FontSizePreset);

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value is null || value.Id == _dataService.Settings.ActiveProfileId)
        {
            return;
        }

        _ = _dataService.SetActiveProfileAsync(value.Id);
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        await _dataService.LoadDataAsync();
        if (Application.Current is App app)
        {
            app.ApplyTheme(_dataService.Settings.IsDarkMode);
            app.ApplyFontSize(_dataService.Settings.FontSizePreset);
        }
        RefreshShell();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        await _dataService.UpdateSettingsAsync(new AppSettings
        {
            ActiveProfileId = _dataService.Settings.ActiveProfileId,
            IsAdvancedMode = _dataService.Settings.IsAdvancedMode,
            IsDarkMode = !_dataService.Settings.IsDarkMode,
            FontSizePreset = _dataService.Settings.FontSizePreset
        });

        if (Application.Current is App app)
        {
            app.ApplyTheme(_dataService.Settings.IsDarkMode);
            app.ApplyFontSize(_dataService.Settings.FontSizePreset);
        }
    }

    public void Navigate(AppPage page)
    {
        CurrentPage = page;
        CurrentView = page switch
        {
            AppPage.Dashboard => Dashboard,
            AppPage.Materials => Materials,
            AppPage.Warehouse => Warehouse,
            AppPage.Recipes => Recipes,
            AppPage.Production => Production,
            AppPage.Combos => Combos,
            AppPage.Pos => Pos,
            AppPage.Bookkeeping => Bookkeeping,
            AppPage.Simulation => Simulation,
            AppPage.Profile => Profile,
            AppPage.Settings => Settings,
            _ => Dashboard
        };

        RefreshNavSelection();
    }

    private void RefreshShell()
    {
        SelectedProfile = Profiles.FirstOrDefault(x => x.Id == _dataService.Settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(EditionLabel));
        OnPropertyChanged(nameof(ShowAdvancedNavigation));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ActiveOwnerName));
        OnPropertyChanged(nameof(DataStorePath));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageDescription));
        OnPropertyChanged(nameof(WorkspaceStatusText));
        OnPropertyChanged(nameof(NavigationSummaryText));
        OnPropertyChanged(nameof(ProfileInitial));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(AppFontSize));
        OnPropertyChanged(nameof(FontSizeLabel));
        if (Application.Current is App app)
        {
            app.ApplyTheme(_dataService.Settings.IsDarkMode);
            app.ApplyFontSize(_dataService.Settings.FontSizePreset);
        }
        RefreshNavSelection();
    }

    private void RefreshNavSelection()
    {
        foreach (var item in PrimaryNavigation)
        {
            item.IsSelected = item.Page == CurrentPage;
        }

        foreach (var item in AdvancedNavigation)
        {
            item.IsSelected = item.Page == CurrentPage;
            item.IsVisible = IsAdvancedMode;
        }

        foreach (var item in SystemNavigation)
        {
            item.IsSelected = item.Page == CurrentPage;
        }
    }
}
