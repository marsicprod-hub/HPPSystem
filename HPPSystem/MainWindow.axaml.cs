using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using HPPSystem.ViewModels;

namespace HPPSystem;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnWorkspaceDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var content = new StackPanel
        {
            Spacing = 10,
            Width = 420
        };

        content.Children.Add(CreateDetailRow("Profil aktif", vm.ActiveProfileName));
        content.Children.Add(CreateDetailRow("Owner / PIC", vm.ActiveOwnerName));
        content.Children.Add(CreateDetailRow("Edition", vm.EditionLabel));
        content.Children.Add(CreateDetailRow("Theme", vm.ThemeLabel));
        content.Children.Add(CreateDetailRow("Modul aktif", vm.NavigationSummaryText));
        content.Children.Add(CreateDetailRow("Data store", vm.DataStorePath));

        var dialog = new ContentDialog
        {
            Title = "Workspace Control Center",
            PrimaryButtonText = "Tutup",
            SecondaryButtonText = "Buka Profil",
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        var result = await dialog.ShowAsync(this);
        if (result == ContentDialogResult.Secondary)
        {
            vm.Navigate(AppPage.Profile);
        }
    }

    private static Grid CreateDetailRow(string label, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            ColumnSpacing = 10
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#8F7D88"))
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        return row;
    }
}
