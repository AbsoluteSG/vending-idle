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
    TokenRate = 9,
    ChainDecay = 10,
    ChainFork = 11,
    BulkCrates = 12,
    Salvage = 13,
    ShakeYield = 14,
    FollowThrough = 15,
    RestockGrowthCut = 16,
    AutoRestockerPrice = 17,
    OfflineHours = 18,
    RushHour = 19,
    SlotPrice = 20,
    SpareChange = 21
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
            EffectText = l => Modifiers.ChainHops(l) +
                              (Modifiers.ChainHops(l) == 1 ? " hop per chain" : " hops per chain")
        },
        new()
        {
            Id = UpgradeId.TokenRate,
            Name = "Loyalty Scheme",
            Description = "Every bottle earns more crate tokens.",
            BaseCost = 1_200,
            Growth = 1.55,
            MaxLevel = 20,
            EffectText = l => Modifiers.TokensPerBottle(l)
                                  .ToString("0.##", CultureInfo.InvariantCulture) + " tk per bottle"
        },
        new()
        {
            Id = UpgradeId.ChainDecay,
            Name = "Jump Leads",
            Description = "Chains keep their voltage, so each hop is likelier to reach the next.",
            BaseCost = 2_500,
            Growth = 1.75,
            MaxLevel = 6,
            EffectText = l => Pct(Modifiers.ChainDecay(l)) + " carry per hop"
        },
        new()
        {
            Id = UpgradeId.ChainFork,
            Name = "Split Coil",
            Description = "A hop can jump two ways at once.",
            BaseCost = 9_000,
            Growth = 1.8,
            MaxLevel = 15,
            EffectText = l => Pct(Modifiers.ChainFork(l)) + " to fork a hop"
        },
        new()
        {
            Id = UpgradeId.BulkCrates,
            Name = "Bulk Crates",
            Description = "The supplier drops off a pallet instead of a box.",
            BaseCost = 3_000,
            Growth = 2.1,
            MaxLevel = 6,
            EffectText = l => Modifiers.CratesPerOpen(l) +
                              (Modifiers.CratesPerOpen(l) == 1 ? " crate per open" : " crates per open")
        },
        new()
        {
            Id = UpgradeId.Salvage,
            Name = "Salvage Rights",
            Description = "Duplicates you cannot use are worth more back.",
            BaseCost = 5_000,
            Growth = 1.6,
            MaxLevel = 12,
            EffectText = l => Pct(Modifiers.DuplicateRefund(l)) + " back on a maxed pull"
        },
        new()
        {
            Id = UpgradeId.ShakeYield,
            Name = "Double Rattle",
            Description = "A shake knocks more out of every coil at once.",
            // Two levels only, and priced like the end of a track. This is the
            // single strongest lever in the game: it multiplies bottles, and
            // bottles are money *and* crate tokens at the same time.
            BaseCost = 250_000,
            Growth = 12.0,
            MaxLevel = 2,
            EffectText = l => Modifiers.ShakeBottles(l) +
                              (Modifiers.ShakeBottles(l) == 1 ? " bottle per slot" : " bottles per slot")
        },
        new()
        {
            Id = UpgradeId.FollowThrough,
            Name = "Follow-Through",
            Description = "The cabinet rocks back and gives you a second shake for free.",
            BaseCost = 12_000,
            Growth = 1.7,
            MaxLevel = 8,
            EffectText = l => Pct(Modifiers.FollowThrough(l)) + " to shake twice"
        },
        new()
        {
            Id = UpgradeId.RestockGrowthCut,
            Name = "Wholesale Pallets",
            Description = "Filling a deep slot stops costing more per bottle.",
            BaseCost = 1_800,
            Growth = 1.55,
            MaxLevel = 12,
            EffectText = l => Pct(Modifiers.RestockGrowthCut(l)) + " flatter restock pricing"
        },
        new()
        {
            Id = UpgradeId.AutoRestockerPrice,
            Name = "Fleet Contract",
            Description = "Auto-restockers stop getting so much dearer each time.",
            BaseCost = 4_000,
            Growth = 1.65,
            MaxLevel = 20,
            EffectText = l => "x" + Modifiers.AutoRestockerGrowth(l)
                                  .ToString("0.##", CultureInfo.InvariantCulture) + " price each"
        },
        new()
        {
            Id = UpgradeId.OfflineHours,
            Name = "Night Shift",
            Description = "The machine keeps selling for longer while you are away.",
            BaseCost = 2_000,
            Growth = 1.9,
            MaxLevel = 8,
            EffectText = l => Modifiers.OfflineHours(l)
                                  .ToString("0.#", CultureInfo.InvariantCulture) + "h away"
        },
        new()
        {
            Id = UpgradeId.RushHour,
            Name = "Rush Hour",
            Description = "Every so often the crowd surges and buys in a burst.",
            BaseCost = 7_500,
            Growth = 1.68,
            MaxLevel = 10,
            EffectText = l => l == 0 ? "no rush"
                            : "x" + Modifiers.RushMultiplier(l)
                                  .ToString("0.#", CultureInfo.InvariantCulture) + " every 90s"
        },
        new()
        {
            Id = UpgradeId.SlotPrice,
            Name = "Corner Shop",
            Description = "Each new compartment costs less to add than the last would have.",
            BaseCost = 6_000,
            Growth = 1.72,
            MaxLevel = 16,
            EffectText = l => "x" + Modifiers.SlotCostGrowth(l)
                                  .ToString("0.##", CultureInfo.InvariantCulture) + " price each"
        },
        new()
        {
            Id = UpgradeId.SpareChange,
            Name = "Loose Change",
            Description = "More coins rattle loose when there is nothing to sell.",
            // Small growth, as asked -- this one is a floor, not a track.
            BaseCost = 400,
            Growth = 1.18,
            MaxLevel = 25,
            EffectText = l => Money.Cash(Modifiers.SpareChange(l)) + " per empty shake"
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

    /// <summary>
    /// Crate tokens earned per bottle sold. Linear, like every other rate in
    /// here: packs are the loop, so this is the upgrade that makes the loop run
    /// faster, and an exponential one would outrun the pull table.
    /// </summary>
    public static double TokensPerBottle(int level) =>
        Balance.TokensPerBottle + Balance.TokensPerBottlePerLevel * level;

    public static double ChainDecay(int level) =>
        Math.Min(Balance.ChainDecayMax, Balance.ChainDecay + Balance.ChainDecayPerLevel * level);

    public static double ChainFork(int level) =>
        Math.Min(Balance.ChainForkMax, Balance.ChainForkPerLevel * level);

    public static int CratesPerOpen(int level) => 1 + Balance.BulkCratesPerLevel * level;

    public static double DuplicateRefund(int level) =>
        Math.Min(Balance.DuplicateRefundMax, Balance.DuplicateRefund + Balance.SalvagePerLevel * level);

    public static int ShakeBottles(int level) =>
        Balance.ShakeBottlesPerSlot + Balance.ShakeBottlesPerLevel * level;

    public static double FollowThrough(int level) =>
        Math.Min(Balance.FollowThroughMax, Balance.FollowThroughPerLevel * level);

    /// <summary>How far restock growth is pulled toward flat pricing.</summary>
    public static double RestockGrowthCut(int level) =>
        Math.Min(Balance.RestockGrowthCutMax, Balance.RestockGrowthCutPerLevel * level);

    public static double AutoRestockerGrowth(int level) =>
        Math.Max(Balance.AutoRestockerGrowthMin,
                 Balance.AutoRestockerCostGrowth - Balance.AutoRestockerGrowthPerLevel * level);

    public static double OfflineHours(int level) =>
        Math.Min(Balance.OfflineMaxHoursCap,
                 Balance.OfflineMaxSeconds / 3600.0 + Balance.OfflineHoursPerLevel * level);

    public static double RushMultiplier(int level) =>
        level <= 0 ? 1.0 : 1.0 + Balance.RushMultiplierPerLevel * level;

    public static double SlotCostGrowth(int level) =>
        Math.Max(Balance.SlotCostGrowthMin,
                 Balance.SlotCostGrowth - Balance.SlotCostGrowthPerLevel * level);

    public static double SpareChange(int level) =>
        Balance.SpareChange + Balance.SpareChangePerLevel * level;
}
