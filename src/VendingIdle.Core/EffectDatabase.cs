using System;
using System.Collections.Generic;
using System.Globalization;

namespace VendingIdle.Core;

/// <summary>
/// What a pack drink does. The design rule these all obey: no effect multiplies
/// raw value. Both previous balance disasters came from a second system
/// compounding into the value curve, so effects only push the *other* levers the
/// simulation already has -- click routing, stock consumption, restock cost,
/// crit chance and customer speed.
/// </summary>
public enum EffectKind
{
    /// <summary>Chance a dispense immediately dispenses from the next stocked slot too.</summary>
    ChainDispense,

    /// <summary>Chance a dispense does not consume stock.</summary>
    StockPreserve,

    /// <summary>Chance a dispense drops one free bottle into a dry slot.</summary>
    CourierDrop,

    /// <summary>Aura: +crit chance machine-wide while loaded and stocked.</summary>
    CritAura,

    /// <summary>Aura: customers click faster while loaded and stocked.</summary>
    CustomerSpeedAura,

    /// <summary>Aura: restocks cheaper machine-wide while loaded and stocked.</summary>
    RestockDiscountAura
}

public sealed class EffectDef
{
    public required EffectKind Kind { get; init; }

    /// <summary>True for passive-while-stocked effects, false for on-dispense rolls.</summary>
    public required bool IsAura { get; init; }

    /// <summary>Human-readable effect at a given level, for the collection rows.</summary>
    public required Func<int, string> Describe { get; init; }
}

/// <summary>
/// Turns an effect level (1..Balance.EffectLevelMax, from duplicate copies) into
/// the numbers the simulation uses. Kept beside the defs so the sim and the UI
/// read one source, mirroring UpgradeDatabase/Modifiers.
/// </summary>
public static class EffectStrength
{
    public static double ChainChance(int level) => level <= 0 ? 0.0 : 0.06 + 0.04 * level;
    public static double PreserveChance(int level) => level <= 0 ? 0.0 : 0.05 + 0.04 * level;
    public static double CourierChance(int level) => level <= 0 ? 0.0 : 0.04 + 0.03 * level;

    /// <summary>Flat crit-chance bonus; the global CritChanceMax cap applies after.</summary>
    public static double CritBonus(int level) => level <= 0 ? 0.0 : 0.01 + 0.01 * level;

    /// <summary>Fraction shaved off the customer interval; interval floor applies after.</summary>
    public static double CustomerSpeedup(int level) => level <= 0 ? 0.0 : 0.03 + 0.02 * level;

    /// <summary>Fraction shaved off restock prices; the discount floor applies after.</summary>
    public static double RestockCut(int level) => level <= 0 ? 0.0 : 0.04 + 0.03 * level;
}

public static class EffectDatabase
{
    private static string Pct(double v) =>
        (v * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static readonly IReadOnlyList<EffectDef> All = new List<EffectDef>
    {
        new()
        {
            Kind = EffectKind.ChainDispense,
            IsAura = false,
            Describe = l => Pct(EffectStrength.ChainChance(l)) + " to vend a second slot"
        },
        new()
        {
            Kind = EffectKind.StockPreserve,
            IsAura = false,
            Describe = l => Pct(EffectStrength.PreserveChance(l)) + " to keep the bottle"
        },
        new()
        {
            Kind = EffectKind.CourierDrop,
            IsAura = false,
            Describe = l => Pct(EffectStrength.CourierChance(l)) + " to refill a dry slot"
        },
        new()
        {
            Kind = EffectKind.CritAura,
            IsAura = true,
            Describe = l => "+" + Pct(EffectStrength.CritBonus(l)) + " double drop while stocked"
        },
        new()
        {
            Kind = EffectKind.CustomerSpeedAura,
            IsAura = true,
            Describe = l => Pct(EffectStrength.CustomerSpeedup(l)) + " faster customers while stocked"
        },
        new()
        {
            Kind = EffectKind.RestockDiscountAura,
            IsAura = true,
            Describe = l => Pct(EffectStrength.RestockCut(l)) + " off restocks while stocked"
        }
    };

    public static EffectDef Get(EffectKind kind) => All[(int)kind];
}
