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
}

/// <summary>The crate roll: one drink per open, weighted by rarity.</summary>
public static class PackSystem
{
    public static int Weight(Rarity rarity) => rarity switch
    {
        Rarity.Common => 60,
        Rarity.Uncommon => 30,
        Rarity.Rare => 10,
        Rarity.Epic => 4,
        Rarity.Legendary => 1,
        _ => 0
    };

    public static readonly int TotalWeight =
        DrinkDatabase.PackDrinks.Sum(d => Weight(d.Rarity));

    public static DrinkDef Roll(Random rng)
    {
        var pick = rng.Next(TotalWeight);

        foreach (var drink in DrinkDatabase.PackDrinks)
        {
            pick -= Weight(drink.Rarity);
            if (pick < 0) return drink;
        }

        // Unreachable while TotalWeight is the sum above; belt and braces.
        return DrinkDatabase.PackDrinks[^1];
    }
}
