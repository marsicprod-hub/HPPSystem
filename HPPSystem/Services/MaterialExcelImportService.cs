using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace HPPSystem.Services;

public sealed record MaterialImportEntry(
    int RowNumber,
    string Name,
    decimal PackPrice,
    decimal PackWeight,
    string Unit,
    string OriginalPackText,
    bool WillUpdateExisting);

public sealed record MaterialImportPreview(
    string SourcePath,
    int WorksheetRowCount,
    int CandidateRowCount,
    int SkippedRowCount,
    int CreateCount,
    int UpdateCount,
    int NormalizedPackCount,
    IReadOnlyList<string> Notes,
    IReadOnlyList<MaterialImportEntry> Entries)
{
    public string SummaryText =>
        $"{Entries.Count} baris material siap diimpor dari {Path.GetFileName(SourcePath)}. " +
        $"{CreateCount} baru, {UpdateCount} update, {SkippedRowCount} baris diabaikan, {NormalizedPackCount} pack dinormalisasi.";
}

public sealed record MaterialImportResult(int ImportedCount, int CreatedCount, int UpdatedCount);

public static class MaterialExcelImportService
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Regex SimplePackPattern = new(
        @"^\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>kg|gr|gram|g|ml|liter|l|lembar|lembaran|butir|batang|ikat)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiPackPattern = new(
        @"^\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>kg|gr|gram|g|ml|liter|l|lembar|lembaran|butir|batang|ikat)\s*x\s*(?<count>[0-9]+(?:[.,][0-9]+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ParentheticalPattern = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);

    public static MaterialImportPreview CreatePreview(string path, IReadOnlyCollection<HPPSystem.Models.Material> existingMaterials, string profileId)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File Excel tidak ditemukan.", path);
        }

        var workbookRows = ReadFirstWorksheetRows(path);
        var profileMaterials = existingMaterials
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
            .ToDictionary(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<MaterialImportEntry>();
        var skippedRows = 0;
        var normalizedPackCount = 0;

        foreach (var row in workbookRows)
        {
            if (row.RowNumber == 1)
            {
                continue;
            }

            var name = row.GetValueOrDefault("C")?.Trim() ?? string.Empty;
            var packText = row.GetValueOrDefault("E")?.Trim() ?? string.Empty;
            var priceText = row.GetValueOrDefault("F")?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(packText) || string.IsNullOrWhiteSpace(priceText))
            {
                skippedRows++;
                continue;
            }

            if (!decimal.TryParse(priceText.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var packPrice) || packPrice <= 0)
            {
                skippedRows++;
                continue;
            }

            if (!TryParsePackSpecification(packText, out var packWeight, out var unit, out var wasNormalized))
            {
                skippedRows++;
                continue;
            }

            if (wasNormalized)
            {
                normalizedPackCount++;
            }

            entries.Add(new MaterialImportEntry(
                RowNumber: row.RowNumber,
                Name: name,
                PackPrice: packPrice,
                PackWeight: packWeight,
                Unit: unit,
                OriginalPackText: packText,
                WillUpdateExisting: profileMaterials.ContainsKey(name)));
        }

        var createCount = entries.Count(x => !x.WillUpdateExisting);
        var updateCount = entries.Count - createCount;
        var notes = new List<string>
        {
            "Kolom yang diimpor: Nama Bahan, Berat per Pack, dan Estimasi Harga.",
            "Kolom kategori dan merek contoh tidak dimasukkan ke master material.",
            "Satuan dinormalisasi ke base unit proyek: kg->gram, liter->ml, format multipack dijumlahkan.",
            "Material baru masuk sebagai katalog dengan stok 0 dan belum aktif di Gudang.",
            "Jika nama material sudah ada di profil aktif, import akan update harga/isi pack sambil mempertahankan stok dan status gudang."
        };

        return new MaterialImportPreview(
            SourcePath: path,
            WorksheetRowCount: workbookRows.Count,
            CandidateRowCount: entries.Count,
            SkippedRowCount: skippedRows,
            CreateCount: createCount,
            UpdateCount: updateCount,
            NormalizedPackCount: normalizedPackCount,
            Notes: notes,
            Entries: entries);
    }

    public static bool TryParsePackSpecification(string rawPackText, out decimal packWeight, out string unit)
        => TryParsePackSpecification(rawPackText, out packWeight, out unit, out _);

    public static bool TryParsePackSpecification(string rawPackText, out decimal packWeight, out string unit, out bool wasNormalized)
    {
        packWeight = 0;
        unit = string.Empty;
        wasNormalized = false;

        if (string.IsNullOrWhiteSpace(rawPackText))
        {
            return false;
        }

        var cleaned = ParentheticalPattern.Replace(rawPackText, " ").Trim();
        var multiMatch = MultiPackPattern.Match(cleaned);
        if (multiMatch.Success)
        {
            if (!TryParseDecimal(multiMatch.Groups["value"].Value, out var eachValue) ||
                !TryParseDecimal(multiMatch.Groups["count"].Value, out var count))
            {
                return false;
            }

            var total = eachValue * count;
            return NormalizeUnit(total, multiMatch.Groups["unit"].Value, out packWeight, out unit, out wasNormalized, isMultipack: true);
        }

        var simpleMatch = SimplePackPattern.Match(cleaned);
        if (!simpleMatch.Success || !TryParseDecimal(simpleMatch.Groups["value"].Value, out var value))
        {
            return false;
        }

        return NormalizeUnit(value, simpleMatch.Groups["unit"].Value, out packWeight, out unit, out wasNormalized, isMultipack: false);
    }

    public static async System.Threading.Tasks.Task<MaterialImportResult> ApplyPreviewAsync(MaterialImportPreview preview, IDataService dataService, string profileId)
    {
        var existingMaterials = dataService.Materials
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
            .ToDictionary(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var materialsToSave = new List<HPPSystem.Models.Material>();
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var entry in preview.Entries)
        {
            if (existingMaterials.TryGetValue(entry.Name, out var existing))
            {
                materialsToSave.Add(new HPPSystem.Models.Material
                {
                    Id = existing.Id,
                    Name = existing.Name,
                    Price = entry.PackPrice,
                    Weight = entry.PackWeight,
                    Unit = entry.Unit,
                    Stock = existing.Stock,
                    IsTrackedInWarehouse = existing.IsTrackedInWarehouse,
                    ProfileId = existing.ProfileId,
                    PriceHistory = existing.PriceHistory.ToList()
                });
                updatedCount++;
                continue;
            }

            materialsToSave.Add(new HPPSystem.Models.Material
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = entry.Name,
                Price = entry.PackPrice,
                Weight = entry.PackWeight,
                Unit = entry.Unit,
                Stock = 0,
                IsTrackedInWarehouse = false,
                ProfileId = profileId
            });
            createdCount++;
        }

        await dataService.SaveMaterialsAsync(materialsToSave);
        return new MaterialImportResult(materialsToSave.Count, createdCount, updatedCount);
    }

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        var normalized = raw.Replace(",", ".").Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool NormalizeUnit(
        decimal sourceValue,
        string rawUnit,
        out decimal packWeight,
        out string unit,
        out bool wasNormalized,
        bool isMultipack)
    {
        wasNormalized = isMultipack;
        packWeight = sourceValue;
        unit = rawUnit.Trim().ToLowerInvariant();

        switch (unit)
        {
            case "kg":
                packWeight = sourceValue * 1000;
                unit = "gram";
                wasNormalized = true;
                return true;
            case "gr":
            case "g":
            case "gram":
                packWeight = sourceValue;
                unit = "gram";
                wasNormalized = wasNormalized || !string.Equals(rawUnit.Trim(), "gram", StringComparison.OrdinalIgnoreCase);
                return true;
            case "l":
            case "liter":
                packWeight = sourceValue * 1000;
                unit = "ml";
                wasNormalized = true;
                return true;
            case "ml":
                packWeight = sourceValue;
                unit = "ml";
                return true;
            case "lembar":
            case "lembaran":
                packWeight = sourceValue;
                unit = "lembar";
                wasNormalized = wasNormalized || !string.Equals(rawUnit.Trim(), "lembar", StringComparison.OrdinalIgnoreCase);
                return true;
            case "butir":
            case "batang":
            case "ikat":
                packWeight = sourceValue;
                return true;
            default:
                return false;
        }
    }

    private static List<WorksheetRow> ReadFirstWorksheetRows(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var workbook = LoadDocument(archive, "xl/workbook.xml");
        var firstSheet = workbook.Descendants(SpreadsheetNs + "sheet").FirstOrDefault()
            ?? throw new InvalidOperationException("Workbook Excel tidak memiliki worksheet.");
        var relationshipId = firstSheet.Attribute(WorkbookRelNs + "id")?.Value
            ?? throw new InvalidOperationException("Worksheet utama tidak memiliki relasi valid.");

        var relationships = LoadDocument(archive, "xl/_rels/workbook.xml.rels");
        var target = relationships
            .Descendants(PackageRelNs + "Relationship")
            .FirstOrDefault(x => string.Equals(x.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("Target worksheet tidak ditemukan.");

        var worksheet = LoadDocument(archive, $"xl/{target}");
        return worksheet.Descendants(SpreadsheetNs + "row")
            .Select(row => ReadWorksheetRow(row, sharedStrings))
            .ToList();
    }

    private static WorksheetRow ReadWorksheetRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
            if (string.IsNullOrWhiteSpace(column))
            {
                continue;
            }

            cells[column] = ReadCellValue(cell, sharedStrings);
        }

        return new WorksheetRow((int?)row.Attribute("r") ?? 0, cells);
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (string.Equals(type, "s", StringComparison.Ordinal))
        {
            var sharedIndexText = cell.Element(SpreadsheetNs + "v")?.Value;
            if (int.TryParse(sharedIndexText, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            {
                return sharedStrings[sharedIndex];
            }
        }

        var inlineString = string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(x => x.Value));
        if (!string.IsNullOrWhiteSpace(inlineString))
        {
            return inlineString;
        }

        return cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        var document = LoadDocument(archive, "xl/sharedStrings.xml");
        return document.Descendants(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(x => x.Value)))
            .ToList();
    }

    private static XDocument LoadDocument(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"Entry Excel tidak ditemukan: {entryPath}");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private sealed record WorksheetRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
    {
        public string? GetValueOrDefault(string column)
            => Cells.TryGetValue(column, out var value) ? value : null;
    }
}
