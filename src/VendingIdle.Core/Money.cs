using System;
using System.Globalization;

namespace VendingIdle.Core;

/// <summary>
/// Money is a plain double -- an idle prototype does not need bignum yet, and a
/// double carries us well past what this balance curve can reach. This is just
/// the formatting layer.
///
/// Two rules, and no suffix tier in between them: below
/// <see cref="ScientificThreshold"/> every figure is written out in full with its
/// cents, because a till that hides the pennies makes the early game -- where a
/// bottle sells for less than a dollar -- unreadable. At and above the threshold
/// the cents are noise, so the value switches to scientific notation, which keeps
/// the readout a fixed width however far the curve runs.
/// </summary>
public static class Money
{
    /// <summary>Where full notation gives way to scientific.</summary>
    public const double ScientificThreshold = 1e6;

    /// <summary>Display form: 0.00, 942.50, 12,345.67, 1.23e6.</summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value)) return "0.00";
        if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

        var sign = value < 0 ? "-" : "";
        value = Math.Abs(value);

        // Tested against the rounded figure, so 999,999.999 crosses over rather
        // than printing as "1,000,000.00" one frame before the switch.
        if (Math.Round(value, 2) < ScientificThreshold)
            return sign + value.ToString("#,##0.00", CultureInfo.InvariantCulture);

        return sign + Scientific(value);
    }

    private static string Scientific(double value)
    {
        var exponent = (int)Math.Floor(Math.Log10(value));
        var mantissa = value / Math.Pow(10.0, exponent);

        // 9.999 rounds to "10.00", which is not a mantissa -- carry it.
        if (Math.Round(mantissa, 2) >= 10.0)
        {
            mantissa /= 10.0;
            exponent++;
        }

        return mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + exponent;
    }

    /// <summary>Format with a currency symbol, e.g. "$1,234.56" or "-$12.50".</summary>
    public static string Cash(double value) =>
        value < 0 ? "-$" + Format(-value) : "$" + Format(value);

    /// <summary>Formats a rate, e.g. "$12.4/s".</summary>
    public static string FormatRate(double perSecond) => Cash(perSecond) + "/s";

    /// <summary>Formats a duration for the offline-earnings summary.</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds < 60) return $"{seconds:0}s";
        if (seconds < 3600) return $"{seconds / 60:0}m";
        var hours = (int)(seconds / 3600);
        var minutes = (int)((seconds - hours * 3600) / 60);
        return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
    }
}
