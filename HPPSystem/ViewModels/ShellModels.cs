using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;

namespace HPPSystem.ViewModels;

public enum AppPage
{
    Dashboard,
    Materials,
    Warehouse,
    Recipes,
    Production,
    Combos,
    Pos,
    Bookkeeping,
    Simulation,
    Profile,
    Settings
}

public sealed partial class NavigationItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    public NavigationItemViewModel(AppPage page, string label, Action<AppPage> navigate)
    {
        Page = page;
        Label = label;
        Icon = page switch
        {
            AppPage.Dashboard => LucideIconKind.LayoutDashboard,
            AppPage.Materials => LucideIconKind.Package,
            AppPage.Warehouse => LucideIconKind.Warehouse,
            AppPage.Recipes => LucideIconKind.ChefHat,
            AppPage.Production => LucideIconKind.Factory,
            AppPage.Combos => LucideIconKind.Layers2,
            AppPage.Pos => LucideIconKind.ReceiptText,
            AppPage.Bookkeeping => LucideIconKind.Wallet,
            AppPage.Simulation => LucideIconKind.TrendingUp,
            AppPage.Profile => LucideIconKind.CircleUser,
            AppPage.Settings => LucideIconKind.Settings2,
            _ => LucideIconKind.AppWindow
        };
        NavigateCommand = new RelayCommand(() => navigate(Page));
    }

    public AppPage Page { get; }
    public string Label { get; }
    public LucideIconKind Icon { get; }
    public IRelayCommand NavigateCommand { get; }
}
