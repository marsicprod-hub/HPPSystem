using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using HPPSystem.ViewModels;

namespace HPPSystem.Views;

public partial class WarehouseView : UserControl
{
    private WarehouseViewModel? _viewModel;
    private bool _deleteDialogOpen;

    public WarehouseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as WarehouseViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WarehouseViewModel.PendingWarehouseRemoval)
            && _viewModel?.PendingWarehouseRemoval is not null
            && !_deleteDialogOpen)
        {
            Dispatcher.UIThread.Post(async () => await ShowDeleteDialogAsync());
        }
    }

    private async System.Threading.Tasks.Task ShowDeleteDialogAsync()
    {
        if (_viewModel?.PendingWarehouseRemoval is null)
        {
            return;
        }

        _deleteDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Hapus Dari Gudang",
                Content = _viewModel.DeletePromptText,
                PrimaryButtonText = "Hapus",
                CloseButtonText = "Batal",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this)!);
            if (result == ContentDialogResult.Primary)
            {
                await _viewModel.DeleteFromWarehouseCommand.ExecuteAsync(_viewModel.PendingWarehouseRemoval);
            }
            else
            {
                _viewModel.CancelDeleteCommand.Execute(null);
            }
        }
        finally
        {
            _deleteDialogOpen = false;
        }
    }
}
