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
    string Brand,
    decimal PackPrice,
    decimal PackQuantity,
    string PackUnit,
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
    private const string UnitTokenPattern = "kg|gr|gram|g|ml|liter|l|m|meter|lembar|lembaran|butir|btr|batang|ikat|pcs|pc|piece|pieces|sachet|tray|roll|lusin|oz|box|karton|ctn|pail|pack|pak|botol|bottle|jar|cup|kaleng|can|tube";

    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Regex SimplePackPattern = new(
        $@"^\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>{UnitTokenPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiPackPattern = new(
        $@"^\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>{UnitTokenPattern})\s*(?:x|×|\*)\s*(?<count>[0-9]+(?:[.,][0-9]+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReverseMultiPackPattern = new(
        $@"^\s*(?<count>[0-9]+(?:[.,][0-9]+)?)\s*(?:x|×|\*)\s*(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>{UnitTokenPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlashPackPattern = new(
        $@"^\s*(?<left>[0-9]+(?:[.,][0-9]+)?)\s*(?<leftUnit>{UnitTokenPattern})\s*/\s*(?<right>[0-9]+(?:[.,][0-9]+)?)\s*(?<rightUnit>{UnitTokenPattern})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ValueUnitSearchPattern = new(
        $@"(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>{UnitTokenPattern})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ParentheticalContentPattern = new(@"\((?<content>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static MaterialImportPreview CreatePreview(string path, IReadOnlyCollection<HPPSystem.Models.Material> existingMaterials, string profileId)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File Excel tidak ditemukan.", path);
        }

        var workbookRows = ReadFirstWorksheetRows(path);
        var columnMap = ResolveColumnMap(workbookRows);
        var profileMaterials = existingMaterials
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
            .ToList();
        var materialLookup = BuildMaterialLookup(profileMaterials);

        var parsedEntries = new List<MaterialImportEntry>();
        var skippedRows = 0;
        var normalizedPackCount = 0;

        foreach (var row in workbookRows)
        {
            if (row.RowNumber == 1)
            {
                continue;
            }

            var name = row.GetValueOrDefault(columnMap.NameColumn)?.Trim() ?? string.Empty;
            var brand = row.GetValueOrDefault(columnMap.BrandColumn)?.Trim() ?? string.Empty;
            var packText = row.GetValueOrDefault(columnMap.PackColumn)?.Trim() ?? string.Empty;
            var priceText = row.GetValueOrDefault(columnMap.PriceColumn)?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(packText) || string.IsNullOrWhiteSpace(priceText))
            {
                skippedRows++;
                continue;
            }

            if (!TryParsePrice(priceText, out var packPrice) || packPrice <= 0)
            {
                skippedRows++;
                continue;
            }

            if (!TryParsePackSpecification(packText, out var packQuantity, out var packUnit, out var wasNormalized))
            {
                skippedRows++;
                continue;
            }

            if (wasNormalized)
            {
                normalizedPackCount++;
            }

            parsedEntries.Add(new MaterialImportEntry(
                RowNumber: row.RowNumber,
                Name: name,
                Brand: brand,
                PackPrice: packPrice,
                PackQuantity: packQuantity,
                PackUnit: packUnit,
                OriginalPackText: packText,
                WillUpdateExisting: false));
        }

        var importedFamilyVariantCounts = parsedEntries
            .GroupBy(x => NormalizeMaterialIdentityKey(x.Name, x.Brand), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => NormalizeMaterialVariantKey(x.Name, x.Brand, x.PackQuantity, x.PackUnit)).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        var entries = new List<MaterialImportEntry>();
        foreach (var entry in parsedEntries)
        {
            var familyVariantCount = importedFamilyVariantCounts[NormalizeMaterialIdentityKey(entry.Name, entry.Brand)];
            var willUpdateExisting = TryResolveExistingMaterial(materialLookup, entry.Name, entry.Brand, entry.PackQuantity, entry.PackUnit, familyVariantCount, out var existing);
            entries.Add(entry with { WillUpdateExisting = willUpdateExisting });

            if (!willUpdateExisting || existing is null)
            {
                continue;
            }

            materialLookup.Remove(existing);
            materialLookup.Add(new HPPSystem.Models.Material
            {
                Id = existing.Id,
                Name = ChooseImportedText(existing.Name, entry.Name),
                Brand = ChooseImportedText(existing.Brand, entry.Brand),
                Price = entry.PackPrice,
                Weight = entry.PackQuantity,
                Unit = entry.PackUnit,
                Stock = existing.Stock,
                IsTrackedInWarehouse = existing.IsTrackedInWarehouse,
                ProfileId = existing.ProfileId,
                PriceHistory = existing.PriceHistory.ToList()
            });
        }

        var createCount = entries.Count(x => !x.WillUpdateExisting);
        var updateCount = entries.Count - createCount;
        var notes = new List<string>
        {
            "Kolom dibaca berdasarkan header, bukan huruf kolom tetap. Importer akan mencari Nama Bahan, Merek, Netto / Isi per Pack, dan Harga.",
            "Jumlah isi per pack dan satuan pack disimpan terpisah agar lebih mudah diurutkan, diaudit, dan dipakai untuk rekomendasi belanja lintas varian pack.",
            "Parser pack membaca format sederhana, multipack, range netto, dan net content di dalam tanda kurung seperti 4 Pcs (750 gr) atau 1 Tray (30 btr).",
            "Satuan dinormalisasi ke base unit proyek: kg->gram, liter->ml, meter->m, dan alias unit umum diseragamkan.",
            "Material baru masuk sebagai katalog dengan stok 0 dan belum aktif di Gudang.",
            "Re-import akan update varian yang identitasnya sama: nama + merek + netto + satuan. Varian pack berbeda akan tetap disimpan sebagai item terpisah.",
            "Fallback legacy tetap ada untuk data lama yang dulu belum dipisah per varian, selama family material itu masih satu varian dan tidak bentrok."
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

    public static bool TryParsePackSpecification(string rawPackText, out decimal packQuantity, out string packUnit)
        => TryParsePackSpecification(rawPackText, out packQuantity, out packUnit, out _);

    public static bool TryParsePackSpecification(string rawPackText, out decimal packQuantity, out string packUnit, out bool wasNormalized)
    {
        packQuantity = 0;
        packUnit = string.Empty;
        wasNormalized = false;

        if (string.IsNullOrWhiteSpace(rawPackText))
        {
            return false;
        }

        var normalizedText = NormalizePackText(rawPackText);
        var sections = SplitPackSections(normalizedText);
        var candidates = sections
            .Select(section => TryParseSection(section, normalizedText, out var quantity, out var unit, out var normalized)
                ? new PackCandidate(section, quantity, unit, normalized, GetCandidateScore(unit, section == normalizedText, section.Contains('/', StringComparison.Ordinal), section != normalizedText))
                : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => GetOverbuyRisk(candidate.Unit))
            .ThenByDescending(candidate => candidate.Quantity)
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        var bestCandidate = candidates[0];
        packQuantity = bestCandidate.Quantity;
        packUnit = bestCandidate.Unit;
        wasNormalized = bestCandidate.WasNormalized;
        return true;
    }

    public static async System.Threading.Tasks.Task<MaterialImportResult> ApplyPreviewAsync(MaterialImportPreview preview, IDataService dataService, string profileId)
    {
        var existingMaterials = dataService.Materials
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
            .ToList();
        var materialLookup = BuildMaterialLookup(existingMaterials);
        var importedFamilyVariantCounts = preview.Entries
            .GroupBy(x => NormalizeMaterialIdentityKey(x.Name, x.Brand), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => NormalizeMaterialVariantKey(x.Name, x.Brand, x.PackQuantity, x.PackUnit)).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        var materialsToSave = new List<HPPSystem.Models.Material>();
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var entry in preview.Entries)
        {
            var familyVariantCount = importedFamilyVariantCounts[NormalizeMaterialIdentityKey(entry.Name, entry.Brand)];
            if (TryResolveExistingMaterial(materialLookup, entry.Name, entry.Brand, entry.PackQuantity, entry.PackUnit, familyVariantCount, out var existing) && existing is not null)
            {
                var updated = new HPPSystem.Models.Material
                {
                    Id = existing.Id,
                    Name = ChooseImportedText(existing.Name, entry.Name),
                    Brand = ChooseImportedText(existing.Brand, entry.Brand),
                    Price = entry.PackPrice,
                    Weight = entry.PackQuantity,
                    Unit = entry.PackUnit,
                    Stock = existing.Stock,
                    IsTrackedInWarehouse = existing.IsTrackedInWarehouse,
                    ProfileId = existing.ProfileId,
                    PriceHistory = existing.PriceHistory.ToList()
                };
                materialsToSave.Add(updated);
                materialLookup.Remove(existing);
                materialLookup.Add(updated);
                updatedCount++;
                continue;
            }

            var created = new HPPSystem.Models.Material
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = entry.Name.Trim(),
                Brand = entry.Brand.Trim(),
                Price = entry.PackPrice,
                Weight = entry.PackQuantity,
                Unit = entry.PackUnit,
                Stock = 0,
                IsTrackedInWarehouse = false,
                ProfileId = profileId
            };
            materialsToSave.Add(created);
            materialLookup.Add(created);
            createdCount++;
        }

        await dataService.SaveMaterialsAsync(materialsToSave);
        return new MaterialImportResult(materialsToSave.Count, createdCount, updatedCount);
    }

    private static bool TryParsePrice(string raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim()
            .Replace("Rp", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("IDR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty);

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        normalized = normalized.Replace(".", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim().Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeMaterialNameKey(string rawName)
        => NormalizeTextKey(rawName);

    private static string NormalizeMaterialIdentityKey(string rawName, string rawBrand)
        => $"{NormalizeTextKey(rawName)}::{NormalizeTextKey(rawBrand)}";

    private static string NormalizeMaterialVariantKey(string rawName, string rawBrand, decimal packQuantity, string packUnit)
        => $"{NormalizeMaterialIdentityKey(rawName, rawBrand)}::{packQuantity.ToString("G29", CultureInfo.InvariantCulture)}::{NormalizeTextKey(packUnit)}";

    private static string NormalizeTextKey(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var collapsedWhitespace = MultiWhitespacePattern.Replace(rawText.Trim(), " ");
        return collapsedWhitespace.ToUpperInvariant();
    }

    private static string ChooseImportedText(string existingText, string importedText)
    {
        var normalizedImported = NormalizeTextKey(importedText);
        if (string.IsNullOrWhiteSpace(normalizedImported))
        {
            return existingText?.Trim() ?? string.Empty;
        }

        var normalizedExisting = NormalizeTextKey(existingText);
        if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            return importedText.Trim();
        }

        return string.Equals(normalizedExisting, normalizedImported, StringComparison.Ordinal)
            ? existingText.Trim()
            : importedText.Trim();
    }

    private static MaterialLookup BuildMaterialLookup(IEnumerable<HPPSystem.Models.Material> materials)
        => new(materials.ToList());

    private static bool TryResolveExistingMaterial(
        MaterialLookup lookup,
        string rawName,
        string rawBrand,
        decimal packQuantity,
        string packUnit,
        int importedFamilyVariantCount,
        out HPPSystem.Models.Material? material)
    {
        var exactVariantKey = NormalizeMaterialVariantKey(rawName, rawBrand, packQuantity, packUnit);
        material = lookup.Materials.FirstOrDefault(x =>
            string.Equals(
                NormalizeMaterialVariantKey(x.Name, x.Brand, x.Weight, x.Unit),
                exactVariantKey,
                StringComparison.Ordinal));
        if (material is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(rawBrand))
        {
            var blankBrandVariantKey = NormalizeMaterialVariantKey(rawName, string.Empty, packQuantity, packUnit);
            material = lookup.Materials.FirstOrDefault(x =>
                string.IsNullOrWhiteSpace(x.Brand) &&
                string.Equals(
                    NormalizeMaterialVariantKey(x.Name, string.Empty, x.Weight, x.Unit),
                    blankBrandVariantKey,
                    StringComparison.Ordinal));
            if (material is not null)
            {
                return true;
            }
        }

        if (importedFamilyVariantCount == 1)
        {
            var familyMaterials = lookup.Materials
                .Where(x => string.Equals(NormalizeMaterialIdentityKey(x.Name, x.Brand), NormalizeMaterialIdentityKey(rawName, rawBrand), StringComparison.Ordinal))
                .ToList();
            if (familyMaterials.Count == 1)
            {
                material = familyMaterials[0];
                return true;
            }

            if (!string.IsNullOrWhiteSpace(rawBrand))
            {
                var blankBrandFamily = lookup.Materials
                    .Where(x => string.IsNullOrWhiteSpace(x.Brand) &&
                                string.Equals(NormalizeMaterialNameKey(x.Name), NormalizeMaterialNameKey(rawName), StringComparison.Ordinal))
                    .ToList();
                if (blankBrandFamily.Count == 1)
                {
                    material = blankBrandFamily[0];
                    return true;
                }
            }
        }

        material = null;
        return false;
    }

    private static bool NormalizeUnit(
        decimal sourceValue,
        string rawUnit,
        out decimal packQuantity,
        out string packUnit,
        out bool wasNormalized,
        bool isMultipack)
    {
        wasNormalized = isMultipack;
        packQuantity = sourceValue;
        packUnit = rawUnit.Trim().ToLowerInvariant();

        switch (packUnit)
        {
            case "kg":
                packQuantity = sourceValue * 1000;
                packUnit = "gram";
                wasNormalized = true;
                return true;
            case "gr":
            case "g":
            case "gram":
                packQuantity = sourceValue;
                packUnit = "gram";
                wasNormalized = wasNormalized || !string.Equals(rawUnit.Trim(), "gram", StringComparison.OrdinalIgnoreCase);
                return true;
            case "l":
            case "liter":
                packQuantity = sourceValue * 1000;
                packUnit = "ml";
                wasNormalized = true;
                return true;
            case "ml":
                packQuantity = sourceValue;
                packUnit = "ml";
                return true;
            case "meter":
                packUnit = "m";
                wasNormalized = true;
                return true;
            case "m":
                packUnit = "m";
                return true;
            case "lembar":
            case "lembaran":
                packQuantity = sourceValue;
                packUnit = "lembar";
                wasNormalized = wasNormalized || !string.Equals(rawUnit.Trim(), "lembar", StringComparison.OrdinalIgnoreCase);
                return true;
            case "btr":
                packQuantity = sourceValue;
                packUnit = "butir";
                wasNormalized = true;
                return true;
            case "butir":
                packQuantity = sourceValue;
                packUnit = "butir";
                return true;
            case "pc":
            case "piece":
            case "pieces":
                packQuantity = sourceValue;
                packUnit = "pcs";
                wasNormalized = true;
                return true;
            case "pcs":
            case "batang":
            case "ikat":
            case "sachet":
            case "tray":
            case "roll":
            case "lusin":
            case "oz":
            case "box":
            case "karton":
            case "ctn":
            case "pail":
            case "pack":
            case "pak":
            case "botol":
            case "bottle":
            case "jar":
            case "cup":
            case "kaleng":
            case "can":
            case "tube":
                packQuantity = sourceValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseSection(string sectionText, string normalizedText, out decimal packQuantity, out string packUnit, out bool wasNormalized)
    {
        packQuantity = 0;
        packUnit = string.Empty;
        wasNormalized = false;

        if (string.IsNullOrWhiteSpace(sectionText))
        {
            return false;
        }

        var cleaned = MultiWhitespacePattern.Replace(sectionText.Trim(), " ")
            .Replace("×", "x", StringComparison.Ordinal)
            .Replace("*", "x", StringComparison.Ordinal);

        if (TryParseExactSection(cleaned, out packQuantity, out packUnit, out wasNormalized))
        {
            return true;
        }

        var extractedCandidates = ValueUnitSearchPattern.Matches(cleaned)
            .Select(match => match.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extractedCandidates.Count == 0)
        {
            return false;
        }

        var candidate = extractedCandidates
            .Select(value => TryParseExactSection(value, out var quantity, out var unit, out var normalized)
                ? new PackCandidate(value, quantity, unit, normalized, GetCandidateScore(unit, value.Equals(normalizedText, StringComparison.OrdinalIgnoreCase), value.Contains('/', StringComparison.Ordinal), value != cleaned))
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => GetOverbuyRisk(item.Unit))
            .ThenByDescending(item => item.Quantity)
            .FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        packQuantity = candidate.Quantity;
        packUnit = candidate.Unit;
        wasNormalized = true || candidate.WasNormalized;
        return true;
    }

    private static bool TryParseExactSection(string cleaned, out decimal packQuantity, out string packUnit, out bool wasNormalized)
    {
        packQuantity = 0;
        packUnit = string.Empty;
        wasNormalized = false;

        var multiMatch = MultiPackPattern.Match(cleaned);
        if (multiMatch.Success)
        {
            if (!TryParseDecimal(multiMatch.Groups["value"].Value, out var eachValue) ||
                !TryParseDecimal(multiMatch.Groups["count"].Value, out var count))
            {
                return false;
            }

            return NormalizeUnit(eachValue * count, multiMatch.Groups["unit"].Value, out packQuantity, out packUnit, out wasNormalized, isMultipack: true);
        }

        var reverseMultiMatch = ReverseMultiPackPattern.Match(cleaned);
        if (reverseMultiMatch.Success)
        {
            if (!TryParseDecimal(reverseMultiMatch.Groups["value"].Value, out var eachValue) ||
                !TryParseDecimal(reverseMultiMatch.Groups["count"].Value, out var count))
            {
                return false;
            }

            return NormalizeUnit(eachValue * count, reverseMultiMatch.Groups["unit"].Value, out packQuantity, out packUnit, out wasNormalized, isMultipack: true);
        }

        var slashMatch = SlashPackPattern.Match(cleaned);
        if (slashMatch.Success &&
            TryParseDecimal(slashMatch.Groups["left"].Value, out var leftValue) &&
            TryParseDecimal(slashMatch.Groups["right"].Value, out var rightValue))
        {
            if (NormalizeUnit(leftValue, slashMatch.Groups["leftUnit"].Value, out var leftQuantity, out var leftUnit, out var leftNormalized, isMultipack: false) &&
                NormalizeUnit(rightValue, slashMatch.Groups["rightUnit"].Value, out var rightQuantity, out var rightUnit, out var rightNormalized, isMultipack: false) &&
                string.Equals(leftUnit, rightUnit, StringComparison.OrdinalIgnoreCase))
            {
                packQuantity = Math.Max(leftQuantity, rightQuantity);
                packUnit = leftUnit;
                wasNormalized = leftNormalized || rightNormalized || leftQuantity != rightQuantity;
                return true;
            }
        }

        var simpleMatch = SimplePackPattern.Match(cleaned);
        if (simpleMatch.Success && TryParseDecimal(simpleMatch.Groups["value"].Value, out var value))
        {
            return NormalizeUnit(value, simpleMatch.Groups["unit"].Value, out packQuantity, out packUnit, out wasNormalized, isMultipack: false);
        }

        return false;
    }

    private static string NormalizePackText(string rawPackText)
    {
        return MultiWhitespacePattern.Replace(rawPackText.Trim(), " ");
    }

    private static List<string> SplitPackSections(string normalizedText)
    {
        var sections = new List<string> { ParentheticalContentPattern.Replace(normalizedText, string.Empty).Trim() };
        sections.AddRange(ParentheticalContentPattern.Matches(normalizedText)
            .Select(match => match.Groups["content"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return sections
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetCandidateScore(string unit, bool isOuterSection, bool isSlashRange, bool isExtractedFromLargerSection)
    {
        var normalizedUnit = NormalizeTextKey(unit);
        var baseScore = normalizedUnit switch
        {
            "GRAM" or "ML" or "M" or "LEMBAR" or "BUTIR" or "BATANG" or "IKAT" => 80,
            "PCS" or "SACHET" or "TRAY" or "ROLL" or "LUSIN" or "OZ" => 45,
            "BOX" or "KARTON" or "CTN" or "PAIL" or "PACK" or "PAK" or "BOTOL" or "BOTTLE" or "JAR" or "CUP" or "KALENG" or "CAN" or "TUBE" => 20,
            _ => 10
        };

        if (isOuterSection)
        {
            baseScore += 5;
        }

        if (isSlashRange)
        {
            baseScore -= 2;
        }

        if (isExtractedFromLargerSection)
        {
            baseScore -= 1;
        }

        return baseScore;
    }

    private static int GetOverbuyRisk(string unit)
    {
        return NormalizeTextKey(unit) switch
        {
            "GRAM" or "ML" or "M" or "LEMBAR" or "BUTIR" or "BATANG" or "IKAT" => 0,
            "PCS" or "SACHET" or "TRAY" or "ROLL" or "LUSIN" or "OZ" => 1,
            _ => 2
        };
    }

    private static WorksheetColumnMap ResolveColumnMap(IReadOnlyList<WorksheetRow> rows)
    {
        var headerRow = rows.FirstOrDefault()
            ?? throw new InvalidOperationException("Worksheet Excel kosong.");

        var normalizedHeaders = headerRow.Cells
            .ToDictionary(
                pair => pair.Key,
                pair => NormalizeHeaderText(pair.Value),
                StringComparer.OrdinalIgnoreCase);

        static string ResolveHeaderColumn(IReadOnlyDictionary<string, string> normalizedHeaders, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeHeaderText(candidate);
                var column = normalizedHeaders
                    .FirstOrDefault(cell => cell.Value.Contains(normalizedCandidate, StringComparison.Ordinal))
                    .Key;

                if (!string.IsNullOrWhiteSpace(column))
                {
                    return column;
                }
            }

            throw new InvalidOperationException($"Kolom Excel dengan kandidat '{string.Join(", ", candidates)}' tidak ditemukan.");
        }

        return new WorksheetColumnMap(
            NameColumn: ResolveHeaderColumn(normalizedHeaders, "Nama Bahan", "Nama Material", "Material"),
            BrandColumn: ResolveHeaderColumn(normalizedHeaders, "Merek Contoh", "Merek Produk", "Brand", "Merek"),
            PackColumn: ResolveHeaderColumn(normalizedHeaders, "Berat per Pack", "Netto per Pack", "Isi per Pack", "Netto", "Berat", "Isi"),
            PriceColumn: ResolveHeaderColumn(normalizedHeaders, "Estimasi Harga", "Harga", "Harga Pack"));
    }

    private static string NormalizeHeaderText(string rawHeader)
    {
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawHeader.ToUpperInvariant(), @"[^A-Z0-9]+", " ");
        return MultiWhitespacePattern.Replace(cleaned, " ").Trim();
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

    private sealed class MaterialLookup
    {
        public MaterialLookup(List<HPPSystem.Models.Material> materials)
        {
            Materials = materials;
        }

        public List<HPPSystem.Models.Material> Materials { get; }

        public void Remove(HPPSystem.Models.Material material)
            => Materials.Remove(material);

        public void Add(HPPSystem.Models.Material material)
            => Materials.Add(material);
    }

    private sealed record WorksheetRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
    {
        public string? GetValueOrDefault(string column)
            => Cells.TryGetValue(column, out var value) ? value : null;
    }

    private sealed record WorksheetColumnMap(string NameColumn, string BrandColumn, string PackColumn, string PriceColumn);
    private sealed record PackCandidate(string SourceText, decimal Quantity, string Unit, bool WasNormalized, int Score);
}
