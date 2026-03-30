using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using HPPSystem.ViewModels;

namespace HPPSystem;

public partial class MainWindow : AppWindow
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => SyncNavigationSelection();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncNavigationSelection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage) || e.PropertyName == nameof(MainViewModel.ShowAdvancedNavigation))
        {
            SyncNavigationSelection();
        }
    }

    private void SyncNavigationSelection()
    {
        if (_viewModel is null)
        {
            return;
        }

        ShellNav.SelectedItem = _viewModel.CurrentPage switch
        {
            AppPage.Dashboard => DashboardNavItem,
            AppPage.Materials => MaterialsNavItem,
            AppPage.Recipes => RecipesNavItem,
            AppPage.Combos => CombosNavItem,
            AppPage.Pos => PosNavItem,
            AppPage.Bookkeeping => BookkeepingNavItem,
            AppPage.Simulation => SimulationNavItem,
            AppPage.Profile => ProfileNavItem,
            AppPage.Settings => SettingsNavItem,
            _ => DashboardNavItem
        };
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        if (System.Enum.TryParse<AppPage>(tag, true, out var page))
        {
            vm.Navigate(page);
        }
    }

    private async void OnWorkspaceDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = vm.ActiveProfileName, FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold },
                new TextBlock { Text = vm.ActiveOwnerName },
                new TextBlock { Text = $"Edition: {vm.EditionLabel}" },
                new TextBlock { Text = $"Theme: {vm.ThemeLabel}" },
                new TextBlock { Text = $"Store: {vm.DataStorePath}", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            }
        };

        var dialog = new ContentDialog
        {
            Title = "Workspace Details",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        await dialog.ShowAsync(this);
    }
}
