using System;

namespace HPPSystem.Helpers;

public static class FontSizingHelper
{
    public const string ExtraSmall = "extra-small";
    public const string Small = "small";
    public const string Normal = "normal";
    public const string Large = "large";
    public const string ExtraLarge = "extra-large";

    public static string NormalizePreset(string? preset)
    {
        return preset?.Trim().ToLowerInvariant() switch
        {
            ExtraSmall => ExtraSmall,
            Small => Small,
            Large => Large,
            ExtraLarge => ExtraLarge,
            _ => Normal
        };
    }

    public static string GetPreset(int index)
    {
        return Math.Clamp(index, 0, 4) switch
        {
            0 => ExtraSmall,
            1 => Small,
            3 => Large,
            4 => ExtraLarge,
            _ => Normal
        };
    }

    public static int GetIndex(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 0,
            Small => 1,
            Large => 3,
            ExtraLarge => 4,
            _ => 2
        };
    }

    public static string GetLabel(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => "Extra Small",
            Small => "Small",
            Large => "Large",
            ExtraLarge => "Extra Large",
            _ => "Normal"
        };
    }

    public static double GetBaseFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 12,
            Small => 13,
            Large => 15,
            ExtraLarge => 16.5,
            _ => 14
        };
    }

    public static double GetCaptionFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 10,
            Small => 11,
            Large => 13,
            ExtraLarge => 14,
            _ => 12
        };
    }

    public static double GetSectionFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 16,
            Small => 17,
            Large => 20,
            ExtraLarge => 22,
            _ => 18
        };
    }

    public static double GetCardTitleFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 18,
            Small => 19,
            Large => 21,
            ExtraLarge => 22,
            _ => 20
        };
    }

    public static double GetDisplaySmallFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 20,
            Small => 21,
            Large => 23,
            ExtraLarge => 24,
            _ => 22
        };
    }

    public static double GetDisplayFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 22,
            Small => 23,
            Large => 25,
            ExtraLarge => 27,
            _ => 24
        };
    }

    public static double GetPageTitleFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 24,
            Small => 26,
            Large => 30,
            ExtraLarge => 32,
            _ => 28
        };
    }

    public static double GetHeroFontSize(string? preset)
    {
        return NormalizePreset(preset) switch
        {
            ExtraSmall => 26,
            Small => 29,
            Large => 35,
            ExtraLarge => 38,
            _ => 32
        };
    }
}
