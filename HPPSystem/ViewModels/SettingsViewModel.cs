using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public sealed partial class SettingsViewModel : PageViewModelBase
{
    public SettingsViewModel(IDataService dataService, NotificationService notifications)
        : base(dataService, notifications)
    {
        ExportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"hppsystem-backup-{DateTime.Today:yyyyMMdd}.json");
        ImportPath = ExportPath;
        Refresh();
    }

    public override string Title => "Pengaturan";

    [ObservableProperty]
    private bool _isAdvancedMode;

    [ObservableProperty]
    private string _exportPath = string.Empty;

    [ObservableProperty]
    private string _importPath = string.Empty;

    public string CurrentStorePath => DataService.DataStorePath;

    public override void Refresh()
    {
        IsAdvancedMode = DataService.Settings.IsAdvancedMode;
        OnPropertyChanged(nameof(CurrentStorePath));
    }

    [RelayCommand]
    private async Task ToggleAdvancedAsync()
    {
        await DataService.UpdateSettingsAsync(new Models.AppSettings
        {
            ActiveProfileId = DataService.Settings.ActiveProfileId,
            IsAdvancedMode = !DataService.Settings.IsAdvancedMode,
            IsDarkMode = DataService.Settings.IsDarkMode
        });

        Success(DataService.Settings.IsAdvancedMode ? "Mode enterprise aktif." : "Mode basic aktif.");
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportPath))
        {
            Error("Path export belum diisi.");
            return;
        }

        await DataService.ExportSnapshotAsync(ExportPath);
        Success($"Backup data dibuat ke {ExportPath}.");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportPath) || !File.Exists(ImportPath))
        {
            Error("File import tidak ditemukan.");
            return;
        }

        await DataService.ImportSnapshotAsync(ImportPath);
        Success("Import data selesai.");
    }

    public async Task SetExportPathAndRunAsync(string path)
    {
        ExportPath = path;
        await ExportAsync();
    }

    public async Task SetImportPathAndRunAsync(string path)
    {
        ImportPath = path;
        await ImportAsync();
    }
}
