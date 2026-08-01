using System.Collections.Generic;
using System.Linq;

namespace VendingIdle.Core;

/// <summary>
/// The v1 roster. Packs are out of scope for the prototype, so drinks unlock on
/// a lifetime-earnings threshold instead of being pulled from a pack -- the pack
/// system can later replace <see cref="DrinkDef.UnlockAtEarned"/> as the gate
/// without touching anything else.
///
/// The tiers deliberately trade margin for absolute value: Fizzy Water is a 3x
/// return on a trivial outlay, Midnight Brew is barely 2.2x but each can is worth
/// hundreds. Combined with RestockGrowth (premium drinks get more expensive as the
/// slot fills) that gives "which drink goes in which slot, and how full" some teeth.
/// </summary>
public static class DrinkDatabase
{
    public static readonly IReadOnlyList<DrinkDef> All = new List<DrinkDef>
    {
        new()
        {
            Id = "fizzy_water",
            Name = "Fizzy Water",
            Rarity = Rarity.Common,
            Color = 0x7FD4F5,
            Value = 1.0,
            RestockUnitCost = 0.34,
            RestockGrowth = 1.0,
            UnlockAtEarned = 0.0
        },
        new()
        {
            Id = "cola_classic",
            Name = "Cola Classic",
            Rarity = Rarity.Common,
            Color = 0xC0392B,
            Value = 3.2,
            RestockUnitCost = 1.15,
            RestockGrowth = 1.005,
            UnlockAtEarned = 250.0
        },
        new()
        {
            Id = "orange_blast",
            Name = "Orange Blast",
            Rarity = Rarity.Uncommon,
            Color = 0xE8821E,
            Value = 10.0,
            RestockUnitCost = 3.9,
            RestockGrowth = 1.012,
            UnlockAtEarned = 5_000.0
        },
        new()
        {
            Id = "grape_rush",
            Name = "Grape Rush",
            Rarity = Rarity.Rare,
            Color = 0x8E44AD,
            Value = 34.0,
            RestockUnitCost = 14.0,
            RestockGrowth = 1.02,
            UnlockAtEarned = 100_000.0
        },
        new()
        {
            Id = "energy_surge",
            Name = "Energy Surge",
            Rarity = Rarity.Epic,
            Color = 0x27C46B,
            Value = 125.0,
            RestockUnitCost = 54.0,
            RestockGrowth = 1.03,
            UnlockAtEarned = 5_000_000.0
        },
        new()
        {
            Id = "midnight_brew",
            Name = "Midnight Brew",
            Rarity = Rarity.Legendary,
            Color = 0x34495E,
            Value = 520.0,
            RestockUnitCost = 235.0,
            RestockGrowth = 1.04,
            UnlockAtEarned = 250_000_000.0
        }
    };

    private static readonly Dictionary<string, DrinkDef> ById =
        All.ToDictionary(d => d.Id);

    public static DrinkDef? Get(string? id) =>
        id is not null && ById.TryGetValue(id, out var d) ? d : null;

    public static bool IsUnlocked(DrinkDef drink, GameState state) =>
        state.TotalEarned >= drink.UnlockAtEarned;

    public static IEnumerable<DrinkDef> UnlockedFor(GameState state) =>
        All.Where(d => IsUnlocked(d, state));

    /// <summary>The next drink you have not reached yet, for the "coming up" hint.</summary>
    public static DrinkDef? NextLocked(GameState state) =>
        All.FirstOrDefault(d => !IsUnlocked(d, state));
}
