using System;
using System.Globalization;

namespace VendingIdle.Core;

/// <summary>
/// Money is a plain double -- an idle prototype does not need bignum yet, and a
/// double carries us well past what this balance curve can reach. This is just
/// the formatting layer that turns 1234567 into "1.23M".
/// </summary>
public static class Money
{
    private static readonly string[] Suffixes =
    {
        "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc"
    };

    /// <summary>Short display form: 942, 1.23K, 4.56M, 1.02e42.</summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value)) return "0";
        if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

        var sign = value < 0 ? "-" : "";
        value = Math.Abs(value);

        if (value < 1000)
        {
            // Sub-1000 keeps two decimals only while it is genuinely small, so
            // the counter does not jitter with noise once you are earning real money.
            if (value < 10 && value != Math.Floor(value))
                return sign + value.ToString("0.##", CultureInfo.InvariantCulture);
            return sign + Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
        }

        var tier = (int)Math.Floor(Math.Log10(value) / 3.0);
        if (tier >= Suffixes.Length)
            return sign + value.ToString("0.##e+0", CultureInfo.InvariantCulture);

        var scaled = value / Math.Pow(1000, tier);
        return sign + scaled.ToString("0.##", CultureInfo.InvariantCulture) + Suffixes[tier];
    }

    /// <summary>Format with a currency symbol, e.g. "$1.23K".</summary>
    public static string Cash(double value) => "$" + Format(value);

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
