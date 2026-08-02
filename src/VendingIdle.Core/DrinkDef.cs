using System;

namespace VendingIdle.Core;

/// <summary>
/// Pull tier. The roster is deliberately long-tailed: the top two tiers are
/// measured in thousands of packs, not hundreds, and there is no pity timer
/// anywhere -- a Mythic is a geometric distribution and nothing nudges it.
/// </summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythic
}

/// <summary>How a drink is obtained.</summary>
public enum DrinkSource
{
    /// <summary>Unlocked by lifetime earnings; pure value, no effects.</summary>
    Purchase,

    /// <summary>Found in supply crates; carries an effect, priced below its value tier.</summary>
    Pack
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
    /// <param name="growthFactor">
    /// Pulls growth toward flat pricing (Wholesale Pallets). 1.0 leaves the
    /// drink's own curve alone; 0 makes every bottle cost the same as the first.
    /// </param>
    private double StepRatio(int capacity, double growthFactor)
    {
        if (capacity <= 0) return 1.0;

        var growth = 1.0 + (RestockGrowth - 1.0) * Math.Clamp(growthFactor, 0.0, 1.0);
        return Math.Pow(growth, ScalingSteps / (double)capacity);
    }

    /// <summary>Lifetime earnings needed before this drink can be loaded. Purchase drinks only.</summary>
    public double UnlockAtEarned { get; init; }

    public DrinkSource Source { get; init; } = DrinkSource.Purchase;

    /// <summary>The pack drink's effect. Null for purchase drinks -- they are pure value.</summary>
    public EffectKind? Effect { get; init; }

    /// <summary>
    /// Level ceiling for this drink's tier. Rarer drinks cap lower because they
    /// are pulled less often: a Mythic held to a common's 55-copy curve would sit
    /// at level 1 for the life of the save.
    /// </summary>
    public int MaxEffectLevel => Balance.MaxLevelFor(Rarity);

    /// <summary>
    /// How this drink sounds landing in the tray, as a pitch shift in the -1..1
    /// range the audio layer takes (one octave either way). Every drink plays the
    /// same clink sample at its own pitch rather than carrying its own file: six
    /// near-identical glass hits would be six assets to source and keep in tune,
    /// and pitch alone already separates them.
    ///
    /// Light drinks ring high, premium ones land heavy and low, so the roster
    /// filling out is something you can hear and not only read.
    /// </summary>
    public double SoundPitch { get; init; }

    /// <summary>Cost of one unit when the slot currently holds <paramref name="currentStock"/>.</summary>
    public double UnitCostAt(int currentStock, int capacity, double growthFactor = 1.0) =>
        RestockUnitCost * Math.Pow(StepRatio(capacity, growthFactor), currentStock);

    /// <summary>
    /// Closed-form cost of adding <paramref name="units"/> starting from
    /// <paramref name="currentStock"/> (a geometric series, so bulk restock does
    /// not need to loop).
    /// </summary>
    public double RestockCost(int currentStock, int units, int capacity, double growthFactor = 1.0)
    {
        if (units <= 0) return 0.0;

        var first = UnitCostAt(currentStock, capacity, growthFactor);
        var ratio = StepRatio(capacity, growthFactor);

        if (Math.Abs(ratio - 1.0) < 1e-12)
            return first * units;

        return first * (Math.Pow(ratio, units) - 1.0) / (ratio - 1.0);
    }

    /// <summary>Margin on the first unit, shown in the UI to make the trade-off legible.</summary>
    public double BaseMargin => Value / RestockUnitCost;
}
