using System;
using System.Globalization;

namespace HPPSystem.Helpers;

public static class FormattingHelper
{
    private static readonly CultureInfo IndonesianCulture = new("id-ID");

    public static string FormatCurrency(decimal value)
    {
        return string.Format(IndonesianCulture, "{0:C0}", value);
    }

    public static string FormatDateTime(string? isoString)
    {
        if (string.IsNullOrWhiteSpace(isoString) || !DateTime.TryParse(isoString, out var date))
        {
            return "-";
        }

        return date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", IndonesianCulture);
    }

    public static string FormatShortDate(string? isoString)
    {
        if (string.IsNullOrWhiteSpace(isoString) || !DateTime.TryParse(isoString, out var date))
        {
            return "-";
        }

        return date.ToString("dd MMM yyyy", IndonesianCulture);
    }
}
