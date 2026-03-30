using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using HPPSystem.ViewModels;

namespace HPPSystem.Views;

public partial class BookkeepingView : UserControl
{
    private BookkeepingViewModel? _viewModel;
    private bool _deleteDialogOpen;

    public BookkeepingView()
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

        _viewModel = DataContext as BookkeepingViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookkeepingViewModel.PendingDelete) && _viewModel?.PendingDelete is not null && !_deleteDialogOpen)
        {
            Dispatcher.UIThread.Post(async () => await ShowDeleteDialogAsync());
        }
    }

    private async System.Threading.Tasks.Task ShowDeleteDialogAsync()
    {
        if (_viewModel?.PendingDelete is null)
        {
            return;
        }

        _deleteDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Transaction",
                Content = $"Hapus transaksi {_viewModel.PendingDelete.Description} senilai {_viewModel.PendingDelete.Amount:N0}?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this)!);
            if (result == ContentDialogResult.Primary)
            {
                _viewModel.DeleteCommand.Execute(_viewModel.PendingDelete);
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
