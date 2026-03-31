using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HPPSystem.Helpers;
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

    [ObservableProperty]
    private int _fontSizePresetIndex;

    private bool _isSyncingFontSizePreset;

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
    public string FontSizePresetLabel => FontSizingHelper.GetLabel(FontSizingHelper.GetPreset(FontSizePresetIndex));
    public string FontSizePresetSummaryText => $"Ukuran teks aktif: {FontSizePresetLabel}. Berlaku lintas halaman utama.";
    public string ClearDataHeadlineText => "Hapus semua data workspace";
    public string ClearDataSummaryText => "Menghapus material, gudang, resep, produksi, bundle, POS, pembukuan, histori stok, dan mengembalikan workspace ke profil default kosong.";
    public string ClearDataImpactText => "Sebelum clear, aplikasi otomatis membuat backup JSON ke folder Documents. Mode, tema, dan ukuran font tetap dipertahankan.";
    public string ClearDataPromptText => "Hapus semua data sekarang? Sebelum data dibersihkan, aplikasi akan membuat backup otomatis ke Documents.";
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

    partial void OnFontSizePresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(FontSizePresetLabel));
        OnPropertyChanged(nameof(FontSizePresetSummaryText));
        if (_isSyncingFontSizePreset)
        {
            return;
        }

        _ = UpdateFontSizePresetAsync(FontSizingHelper.GetPreset(value));
    }

    public override void Refresh()
    {
        IsAdvancedMode = DataService.Settings.IsAdvancedMode;
        _isSyncingFontSizePreset = true;
        FontSizePresetIndex = FontSizingHelper.GetIndex(DataService.Settings.FontSizePreset);
        _isSyncingFontSizePreset = false;
        OnPropertyChanged(nameof(CurrentStorePath));
        OnPropertyChanged(nameof(WorkspaceModeText));
        OnPropertyChanged(nameof(WorkspaceModeSummaryText));
        OnPropertyChanged(nameof(ModeActionText));
        OnPropertyChanged(nameof(BackupInsightText));
        OnPropertyChanged(nameof(ExportGuideText));
        OnPropertyChanged(nameof(ImportGuideText));
        OnPropertyChanged(nameof(FontSizePresetLabel));
        OnPropertyChanged(nameof(FontSizePresetSummaryText));
        OnPropertyChanged(nameof(ClearDataHeadlineText));
        OnPropertyChanged(nameof(ClearDataSummaryText));
        OnPropertyChanged(nameof(ClearDataImpactText));
        OnPropertyChanged(nameof(ClearDataPromptText));
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
            IsDarkMode = DataService.Settings.IsDarkMode,
            FontSizePreset = DataService.Settings.FontSizePreset
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

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        var backupPath = BuildPreClearBackupPath();
        await DataService.ExportSnapshotAsync(backupPath);
        await DataService.ClearAllDataAsync();
        Success($"Semua data workspace sudah dibersihkan. Backup otomatis dibuat di {backupPath}.");
    }

    private async Task UpdateFontSizePresetAsync(string preset)
    {
        var normalizedPreset = FontSizingHelper.NormalizePreset(preset);
        if (string.Equals(DataService.Settings.FontSizePreset, normalizedPreset, StringComparison.Ordinal))
        {
            return;
        }

        await DataService.UpdateSettingsAsync(new Models.AppSettings
        {
            ActiveProfileId = DataService.Settings.ActiveProfileId,
            IsAdvancedMode = DataService.Settings.IsAdvancedMode,
            IsDarkMode = DataService.Settings.IsDarkMode,
            FontSizePreset = normalizedPreset
        });

        Success($"Ukuran font diubah ke {FontSizingHelper.GetLabel(normalizedPreset)}.");
    }

    private static string BuildPreClearBackupPath()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, $"hppsystem-backup-before-clear-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    }
}
