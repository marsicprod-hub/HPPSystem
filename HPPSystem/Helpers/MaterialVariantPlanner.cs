using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HPPSystem.Models;

namespace HPPSystem.Helpers;

public static class MaterialVariantPlanner
{
    public static string BuildFamilyKey(HPPSystem.Models.Material material) => BuildFamilyKey(material.Name, material.Brand, material.Unit);

    public static string BuildFamilyKey(ProductionMaterialRequirement requirement) =>
        BuildFamilyKey(requirement.MaterialName, requirement.MaterialBrand, requirement.Unit);

    public static string BuildFamilyKey(string name, string brand, string unit) =>
        $"{Normalize(name)}|{Normalize(brand)}|{Normalize(unit)}";

    public static IReadOnlyList<HPPSystem.Models.Material> GetFamilyVariants(IEnumerable<HPPSystem.Models.Material> materials, string familyKey)
    {
        return materials
            .Where(material => string.Equals(BuildFamilyKey(material), familyKey, StringComparison.Ordinal))
            .OrderByDescending(material => material.Weight)
            .ThenBy(material => material.Price)
            .ToList();
    }

    public static decimal GetTrackedFamilyStock(IEnumerable<HPPSystem.Models.Material> familyVariants)
    {
        return familyVariants
            .Where(material => material.IsTrackedInWarehouse)
            .Sum(material => material.Stock);
    }

    public static IReadOnlyList<MaterialVariantConsumption> BuildConsumptionPlan(decimal requiredQuantity, IEnumerable<HPPSystem.Models.Material> familyVariants)
    {
        var plan = new List<MaterialVariantConsumption>();
        var remaining = requiredQuantity;

        foreach (var material in familyVariants
                     .Where(material => material.IsTrackedInWarehouse && material.Stock > 0)
                     .OrderByDescending(material => material.Stock)
                     .ThenByDescending(material => material.Weight))
        {
            if (remaining <= 0)
            {
                break;
            }

            var consumed = Math.Min(material.Stock, remaining);
            if (consumed <= 0)
            {
                continue;
            }

            plan.Add(new MaterialVariantConsumption(material, consumed));
            remaining -= consumed;
        }

        return remaining > 0 ? [] : plan;
    }

    public static MaterialPurchasePlan? BuildPurchasePlan(decimal shortageQuantity, IEnumerable<HPPSystem.Models.Material> familyVariants)
    {
        var candidates = familyVariants
            .Where(material => material.Weight > 0 && material.Price > 0)
            .DistinctBy(material => material.Id)
            .ToList();

        if (shortageQuantity <= 0 || candidates.Count == 0)
        {
            return null;
        }

        var decimals = candidates
            .Select(material => GetDecimalPlaces(material.Weight))
            .Append(GetDecimalPlaces(shortageQuantity))
            .DefaultIfEmpty(0)
            .Max();
        var scale = (int)Math.Pow(10, Math.Min(decimals, 3));

        var target = ToScaledInteger(shortageQuantity, scale);
        var sizes = candidates.Select(material => ToScaledInteger(material.Weight, scale)).ToArray();
        var maxSize = sizes.Max();
        var upperBound = target + maxSize;

        var states = new PurchaseState?[upperBound + 1];
        states[0] = new PurchaseState(0, 0m, -1, -1);

        for (var current = 0; current <= upperBound; current++)
        {
            if (states[current] is not PurchaseState state)
            {
                continue;
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                var next = current + sizes[index];
                if (next > upperBound)
                {
                    continue;
                }

                var candidateState = new PurchaseState(
                    PackCount: state.PackCount + 1,
                    TotalPrice: state.TotalPrice + candidates[index].Price,
                    PreviousSum: current,
                    VariantIndex: index);

                if (states[next] is null || IsBetter(candidateState, states[next]!.Value))
                {
                    states[next] = candidateState;
                }
            }
        }

        var bestSum = Enumerable.Range(target, upperBound - target + 1)
            .FirstOrDefault(sum => states[sum] is not null);

        if (bestSum == 0 && states[0] is null)
        {
            return null;
        }

        var selectedSum = bestSum == 0 ? target : bestSum;
        if (states[selectedSum] is null)
        {
            return null;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var cursor = selectedSum;
        while (cursor > 0)
        {
            var state = states[cursor]!.Value;
            var material = candidates[state.VariantIndex];
            counts[material.Id] = counts.TryGetValue(material.Id, out var currentCount) ? currentCount + 1 : 1;
            cursor = state.PreviousSum;
        }

        var items = candidates
            .Where(material => counts.ContainsKey(material.Id))
            .Select(material =>
            {
                var packCount = counts[material.Id];
                return new MaterialPurchasePlanItem(material, packCount);
            })
            .OrderByDescending(item => item.Material.Weight)
            .ThenBy(item => item.Material.Price)
            .ToList();

        return new MaterialPurchasePlan(
            TotalQuantity: items.Sum(item => item.TotalQuantity),
            TotalPrice: items.Sum(item => item.TotalPrice),
            Items: items);
    }

    private static bool IsBetter(PurchaseState candidate, PurchaseState existing)
    {
        if (candidate.PackCount != existing.PackCount)
        {
            return candidate.PackCount < existing.PackCount;
        }

        return candidate.TotalPrice < existing.TotalPrice;
    }

    private static int ToScaledInteger(decimal value, int scale)
    {
        return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
    }

    private static int GetDecimalPlaces(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var separatorIndex = text.IndexOf('.');
        return separatorIndex < 0 ? 0 : text.Length - separatorIndex - 1;
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", (value ?? string.Empty)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private readonly record struct PurchaseState(int PackCount, decimal TotalPrice, int PreviousSum, int VariantIndex);
}

public sealed record MaterialPurchasePlan(decimal TotalQuantity, decimal TotalPrice, IReadOnlyList<MaterialPurchasePlanItem> Items);

public sealed record MaterialPurchasePlanItem(HPPSystem.Models.Material Material, int PackCount)
{
    public decimal TotalQuantity => Material.Weight * PackCount;
    public decimal TotalPrice => Material.Price * PackCount;
}

public sealed record MaterialVariantConsumption(HPPSystem.Models.Material Material, decimal Quantity);
