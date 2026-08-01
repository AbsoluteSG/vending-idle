using System;

namespace VendingIdle.Core;

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

/// <summary>
/// A drink you can load into a slot. Immutable definition -- per-save progress
/// (which drinks are unlocked) lives on <see cref="GameState"/>.
/// </summary>
public sealed class DrinkDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public Rarity Rarity { get; init; } = Rarity.Common;

    /// <summary>0xRRGGBB. Core has no MonoGame reference, so colours are packed ints.</summary>
    public uint Color { get; init; }

    /// <summary>Money earned per unit dispensed, before global multipliers.</summary>
    public double Value { get; init; }

    /// <summary>Cost of the first unit restocked into an empty slot.</summary>
    public double RestockUnitCost { get; init; }

    /// <summary>
    /// Per-drink cost scaling, expressed as the price multiplier between an empty
    /// slot and a full one, spread over <see cref="ScalingSteps"/> steps. 1.0 means
    /// flat pricing (cheap drinks); the premium drinks climb, so topping a slot
    /// right to the brim is a real decision rather than an automatic yes.
    ///
    /// Crucially this is measured against the slot's *capacity*, not its raw count.
    /// Compounding per absolute can made Deeper Shelves actively harmful -- at 110
    /// capacity the last can of Midnight Brew cost 76x the first, which flattened
    /// margins to nothing and froze progression outright.
    /// </summary>
    public double RestockGrowth { get; init; } = 1.0;

    /// <summary>How many compounding steps span empty-to-full, at any capacity.</summary>
    public const int ScalingSteps = 10;

    /// <summary>Per-can price ratio for a slot of the given capacity.</summary>
    private double StepRatio(int capacity) =>
        capacity <= 0 ? 1.0 : Math.Pow(RestockGrowth, ScalingSteps / (double)capacity);

    /// <summary>Lifetime earnings needed before this drink can be loaded.</summary>
    public double UnlockAtEarned { get; init; }

    /// <summary>Reserved for the pack/duplicate system that is out of scope for v1.</summary>
    public string? EffectId { get; init; }

    /// <summary>Cost of one unit when the slot currently holds <paramref name="currentStock"/>.</summary>
    public double UnitCostAt(int currentStock, int capacity) =>
        RestockUnitCost * Math.Pow(StepRatio(capacity), currentStock);

    /// <summary>
    /// Closed-form cost of adding <paramref name="units"/> starting from
    /// <paramref name="currentStock"/> (a geometric series, so bulk restock does
    /// not need to loop).
    /// </summary>
    public double RestockCost(int currentStock, int units, int capacity)
    {
        if (units <= 0) return 0.0;

        var first = UnitCostAt(currentStock, capacity);
        var ratio = StepRatio(capacity);

        if (Math.Abs(ratio - 1.0) < 1e-12)
            return first * units;

        return first * (Math.Pow(ratio, units) - 1.0) / (ratio - 1.0);
    }

    /// <summary>Margin on the first unit, shown in the UI to make the trade-off legible.</summary>
    public double BaseMargin => Value / RestockUnitCost;
}
