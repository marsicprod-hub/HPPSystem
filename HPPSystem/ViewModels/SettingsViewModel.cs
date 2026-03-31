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
    public string WorkspaceModeText => IsAdvancedMode ? "Enterprise" : "Basic";
    public string WorkspaceModeSummaryText => IsAdvancedMode
        ? "Modul POS, pembukuan, simulasi, dan bundling sedang aktif."
        : "Workspace sedang berjalan di mode dasar dengan modul inti saja.";
    public string ModeActionText => IsAdvancedMode ? "Turunkan ke Basic" : "Naikkan ke Enterprise";
    public string BackupInsightText => $"Data aktif disimpan di {CurrentStorePath}. Gunakan export rutin sebelum import atau perubahan besar.";
    public string ExportGuideText => string.IsNullOrWhiteSpace(ExportPath)
        ? "Isi path export untuk membuat backup snapshot."
        : $"Backup akan dibuat ke {ExportPath}.";
    public string ImportGuideText => string.IsNullOrWhiteSpace(ImportPath)
        ? "Isi path import untuk memulihkan snapshot."
        : $"Import akan membaca data dari {ImportPath}.";
    public bool CanExport => !string.IsNullOrWhiteSpace(ExportPath);
    public bool CanImport => !string.IsNullOrWhiteSpace(ImportPath);

    partial void OnExportPathChanged(string value)
    {
        OnPropertyChanged(nameof(ExportGuideText));
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnImportPathChanged(string value)
    {
        OnPropertyChanged(nameof(ImportGuideText));
        OnPropertyChanged(nameof(CanImport));
    }

    public override void Refresh()
    {
        IsAdvancedMode = DataService.Settings.IsAdvancedMode;
        OnPropertyChanged(nameof(CurrentStorePath));
        OnPropertyChanged(nameof(WorkspaceModeText));
        OnPropertyChanged(nameof(WorkspaceModeSummaryText));
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(BackupInsightText));
        OnPropertyChanged(nameof(ExportGuideText));
        OnPropertyChanged(nameof(ImportGuideText));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanImport));
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
