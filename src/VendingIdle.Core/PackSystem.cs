using System;
using System.Linq;

namespace VendingIdle.Core;

/// <summary>What redeeming a crate reveal produced, for the UI to present.</summary>
public sealed class PackRedeem
{
    public required string DrinkId { get; init; }
    public required bool WasNew { get; init; }
    /// <summary>Effect level after this copy, clamped to <see cref="Balance.EffectLevelMax"/>.</summary>
    public required int Level { get; init; }
    /// <summary>True when this duplicate no longer raises the level.</summary>
    public required bool AtMax { get; init; }

    /// <summary>Tokens handed back because the pull was already at its ceiling.</summary>
    public double Refund { get; init; }
}

/// <summary>The crate roll: one drink per open, weighted by rarity.</summary>
public static class PackSystem
{
    /// <summary>
    /// Relative pull weight per *drink* of a tier, not per tier -- adding another
    /// Legendary makes each Legendary rarer, which is the behaviour you want when
    /// the roster grows.
    ///
    /// Doubles rather than ints because the tail needs the resolution: Mythic is
    /// around one pull in fifty-five thousand, and integer weights cannot express
    /// that alongside a Common without absurd numbers.
    ///
    /// The tail carries the whole chase now. Crates used to be rationed by a
    /// daily quota, and when that came off, hard play went to roughly a thousand
    /// crates an hour -- which would have emptied the old table in an afternoon.
    /// Legendary and Mythic were cut by 4x and 8x to put the collection back in
    /// weeks-to-months of real play at that rate.
    /// </summary>
    public static double Weight(Rarity rarity) => rarity switch
    {
        Rarity.Common => 1000.0,
        Rarity.Uncommon => 400.0,
        Rarity.Rare => 120.0,
        Rarity.Epic => 30.0,
        Rarity.Legendary => 1.0,
        Rarity.Mythic => 0.105,
        _ => 0.0
    };

    public static readonly double TotalWeight =
        DrinkDatabase.PackDrinks.Sum(d => Weight(d.Rarity));

    /// <summary>Chance of a single named drink coming out of one crate.</summary>
    public static double ChanceOf(DrinkDef drink) => Weight(drink.Rarity) / TotalWeight;

    /// <summary>
    /// One drink per crate, weighted. Deliberately memoryless: there is no pity
    /// counter, no bad-luck protection and no history, so every crate is the same
    /// independent roll as the first.
    /// </summary>
    public static DrinkDef Roll(Random rng)
    {
        var pick = rng.NextDouble() * TotalWeight;

        foreach (var drink in DrinkDatabase.PackDrinks)
        {
            pick -= Weight(drink.Rarity);
            if (pick < 0.0) return drink;
        }

        // Only reachable through floating-point drift at the very top of the
        // range; the last drink is as good an answer as any.
        return DrinkDatabase.PackDrinks[^1];
    }
}
