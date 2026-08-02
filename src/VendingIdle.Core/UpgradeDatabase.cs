using System;
using System.Collections.Generic;
using System.Globalization;

namespace VendingIdle.Core;

public enum UpgradeId
{
    ClickValue = 0,
    CritChance = 1,
    Customers = 2,
    CustomerSpeed = 3,
    SlotCapacity = 4,
    RestockDiscount = 5,
    AutoRestockSpeed = 6,
    ChainChance = 7,
    ChainHops = 8,
    TokenRate = 9
}

public sealed class UpgradeDef
{
    public required UpgradeId Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required double BaseCost { get; init; }
    public required double Growth { get; init; }

    /// <summary>0 means no cap.</summary>
    public int MaxLevel { get; init; }

    /// <summary>Human-readable effect at a given level, for the upgrade card.</summary>
    public required Func<int, string> EffectText { get; init; }

    public double CostAt(int level) => Balance.Cost(BaseCost, Growth, level);
    public bool IsMaxed(int level) => MaxLevel > 0 && level >= MaxLevel;
}

public static class UpgradeDatabase
{
    private static string Pct(double v) =>
        (v * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Secs(double v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture) + "s";

    public static readonly IReadOnlyList<UpgradeDef> All = new List<UpgradeDef>
    {
        new()
        {
            Id = UpgradeId.ClickValue,
            Name = "Premium Pricing",
            Description = "Every bottle dispensed is worth more.",
            BaseCost = 55,
            Growth = 1.38,
            EffectText = l => "x" + Modifiers.ClickValueMultiplier(l).ToString("0.##", CultureInfo.InvariantCulture) + " value"
        },
        new()
        {
            Id = UpgradeId.CritChance,
            Name = "Loose Coil",
            Description = "Chance to drop two bottles at once for double money.",
            BaseCost = 150,
            Growth = 1.5,
            MaxLevel = 29,
            EffectText = l => Pct(Modifiers.CritChance(l)) + " double drop"
        },
        new()
        {
            Id = UpgradeId.Customers,
            Name = "Hire Customer",
            Description = "Customers click the machine for you. They drink real stock.",
            BaseCost = 85,
            Growth = 1.46,
            EffectText = l => l + (l == 1 ? " customer" : " customers")
        },
        new()
        {
            Id = UpgradeId.CustomerSpeed,
            Name = "Thirsty Crowd",
            Description = "Customers buy more often.",
            BaseCost = 250,
            Growth = 1.7,
            // Capped: an uncapped rate multiplier compounds against every other
            // track at once, which is what broke the first balance pass.
            MaxLevel = 20,
            EffectText = l => Secs(Modifiers.CustomerInterval(l)) + " per customer"
        },
        new()
        {
            Id = UpgradeId.SlotCapacity,
            Name = "Deeper Shelves",
            Description = "Every slot holds more bottles.",
            BaseCost = 120,
            Growth = 1.45,
            EffectText = l => Modifiers.SlotCapacity(l) + " bottles per slot"
        },
        new()
        {
            Id = UpgradeId.RestockDiscount,
            Name = "Bulk Supplier",
            Description = "Wholesale deal on every restock.",
            BaseCost = 320,
            Growth = 1.5,
            MaxLevel = 34,
            EffectText = l => Pct(1.0 - Modifiers.RestockDiscount(l)) + " off restocks"
        },
        new()
        {
            Id = UpgradeId.AutoRestockSpeed,
            Name = "Faster Trucks",
            Description = "Auto-restockers refill quicker.",
            BaseCost = 450,
            Growth = 1.6,
            MaxLevel = 25,
            EffectText = l => Secs(Modifiers.AutoRestockInterval(l)) + " per bottle"
        },
        new()
        {
            Id = UpgradeId.ChainChance,
            Name = "Live Wire",
            Description = "Any bottle can jolt the next coil into vending too.",
            BaseCost = 600,
            Growth = 1.52,
            MaxLevel = 40,
            EffectText = l => Pct(Modifiers.ChainChance(l)) + " to chain"
        },
        new()
        {
            Id = UpgradeId.ChainHops,
            Name = "Longer Coils",
            Description = "Chains carry further before they run out of travel.",
            // The steepest curve in the game on purpose: a hop multiplies every
            // chain effect at once, so each one has to cost more than the last
            // by a wide margin or the cascade runs away.
            BaseCost = 4_000,
            Growth = 2.35,
            MaxLevel = 6,
            EffectText = l => Modifiers.ChainHops(l) + " hops per chain"
        },
        new()
        {
            Id = UpgradeId.TokenRate,
            Name = "Loyalty Scheme",
            Description = "Bottles earn more crate tokens.",
            BaseCost = 1_200,
            Growth = 1.55,
            MaxLevel = 20,
            EffectText = l => Modifiers.TokensPerBottle(l)
                                  .ToString("0.##", CultureInfo.InvariantCulture) + " tk per bottle"
        }
    };

    public static UpgradeDef Get(UpgradeId id) => All[(int)id];
    public static int Count => All.Count;
}

/// <summary>
/// Turns upgrade levels into the numbers the simulation actually uses. Kept
/// separate from the defs so both the sim and the UI read the same source.
/// </summary>
public static class Modifiers
{
    /// <summary>
    /// Deliberately linear, not exponential. Four exponential multiplier tracks
    /// stacked multiplicatively is what turned the first pass of this economy into
    /// hyperinflation inside fifteen minutes; a linear effect against an
    /// exponential price keeps this track firmly convergent.
    /// </summary>
    public static double ClickValueMultiplier(int level) => 1.0 + 0.25 * level;

    public static double CritChance(int level) =>
        Math.Min(Balance.CritChanceMax, Balance.CritChanceBase + Balance.CritChancePerLevel * level);

    public static double CustomerInterval(int speedLevel) =>
        Math.Max(Balance.CustomerIntervalMin,
                 Balance.CustomerIntervalBase * Math.Pow(Balance.CustomerSpeedPerLevel, speedLevel));

    public static int SlotCapacity(int level) =>
        Balance.SlotCapacityBase + Balance.SlotCapacityPerLevel * level;

    /// <summary>Multiplier applied to restock prices (less than 1 is a discount).</summary>
    public static double RestockDiscount(int level) =>
        Math.Max(Balance.RestockDiscountMin, Math.Pow(Balance.RestockDiscountPerLevel, level));

    public static double AutoRestockInterval(int level) =>
        Math.Max(Balance.AutoRestockIntervalMin,
                 Balance.AutoRestockIntervalBase * Math.Pow(Balance.AutoRestockSpeedPerLevel, level));

    /// <summary>Machine-wide chance for any dispense to chain into another slot.</summary>
    public static double ChainChance(int level) =>
        Math.Min(Balance.ChainChanceMax, Balance.ChainChancePerLevel * level);

    /// <summary>Hop ceiling for a cascade, before Relay Rum adds to it.</summary>
    public static int ChainHops(int level) =>
        Balance.ChainHopsBase + Balance.ChainHopsPerLevel * level;

    public static double TokensPerBottle(int level) =>
        Balance.TokensPerBottle + Balance.TokensPerBottlePerLevel * level;
}
