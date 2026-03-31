using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using HPPSystem.ViewModels;

namespace HPPSystem.Views;

public partial class MaterialsView : UserControl
{
    private MaterialsViewModel? _viewModel;
    private bool _deleteDialogOpen;

    public MaterialsView()
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

        _viewModel = DataContext as MaterialsViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialsViewModel.PendingDelete) && _viewModel?.PendingDelete is not null && !_deleteDialogOpen)
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
                Title = "Hapus Bahan",
                Content = _viewModel.DeletePromptText,
                PrimaryButtonText = "Hapus",
                CloseButtonText = "Batal",
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

    private async void BrowseImport_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MaterialsViewModel vm)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Pilih File Excel Material",
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(@"C:\Users\hazel\Documents"),
            FileTypeFilter =
            [
                new FilePickerFileType("Excel")
                {
                    Patterns = ["*.xlsx"]
                }
            ]
        });

        var file = result.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            var preview = vm.PreviewImport(file.Path.LocalPath);
            var dialog = new ContentDialog
            {
                Title = "Import Material dari Excel",
                PrimaryButtonText = $"Import {preview.Entries.Count} Item",
                CloseButtonText = "Batal",
                DefaultButton = ContentDialogButton.Primary,
                Content = string.Join(
                    System.Environment.NewLine + System.Environment.NewLine,
                    new[]
                    {
                        preview.SummaryText,
                        string.Join(System.Environment.NewLine, preview.Notes)
                    })
            };

            var choice = await dialog.ShowAsync(topLevel);
            if (choice == ContentDialogResult.Primary)
            {
                await vm.ImportPreviewAsync(preview);
            }
        }
        catch (System.Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Import Gagal",
                Content = ex.Message,
                CloseButtonText = "Tutup"
            };
            await dialog.ShowAsync(topLevel);
        }
    }
}
