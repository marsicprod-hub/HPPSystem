using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace HPPSystem.ViewModels;

public enum AppPage
{
    Dashboard,
    Materials,
    Warehouse,
    Recipes,
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

    public NavigationItemViewModel(AppPage page, string label, MaterialIconKind icon, Action<AppPage> navigate)
    {
        Page = page;
        Label = label;
        Icon = icon;
        NavigateCommand = new RelayCommand(() => navigate(Page));
    }

    public AppPage Page { get; }
    public string Label { get; }
    public MaterialIconKind Icon { get; }
    public IRelayCommand NavigateCommand { get; }
}
