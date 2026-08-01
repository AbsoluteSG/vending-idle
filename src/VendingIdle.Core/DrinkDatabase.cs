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
        },

        // ---- Pack drinks -------------------------------------------------
        // Found in supply crates, never bought. Each carries one effect, and
        // each is worth *less* per bottle than the purchase drink of its tier,
        // so loading one trades a value slot for utility -- effects must never
        // become a second inflation axis on top of the value ladder.
        new()
        {
            Id = "chain_fizz",
            Name = "Chain Fizz",
            Rarity = Rarity.Common,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainDispense,
            Color = 0xF2D43D,
            Value = 2.2,
            RestockUnitCost = 0.9,
            RestockGrowth = 1.0
        },
        new()
        {
            Id = "bulk_bottle",
            Name = "Bulk Bottle",
            Rarity = Rarity.Common,
            Source = DrinkSource.Pack,
            Effect = EffectKind.RestockDiscountAura,
            Color = 0x9A7B5B,
            Value = 2.0,
            RestockUnitCost = 0.8,
            RestockGrowth = 1.0
        },
        new()
        {
            Id = "bottomless_cup",
            Name = "Bottomless Cup",
            Rarity = Rarity.Uncommon,
            Source = DrinkSource.Pack,
            Effect = EffectKind.StockPreserve,
            Color = 0x2E86DE,
            Value = 6.0,
            RestockUnitCost = 2.6,
            RestockGrowth = 1.01
        },
        new()
        {
            Id = "loyalty_lager",
            Name = "Loyalty Lager",
            Rarity = Rarity.Uncommon,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CustomerSpeedAura,
            Color = 0xD4A017,
            Value = 7.0,
            RestockUnitCost = 3.0,
            RestockGrowth = 1.01
        },
        new()
        {
            Id = "static_shock",
            Name = "Static Shock",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CritAura,
            Color = 0xE45FE8,
            Value = 20.0,
            RestockUnitCost = 9.0,
            RestockGrowth = 1.02
        },
        new()
        {
            Id = "courier_cola",
            Name = "Courier Cola",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CourierDrop,
            Color = 0x1ABC9C,
            Value = 22.0,
            RestockUnitCost = 10.0,
            RestockGrowth = 1.02
        }
    };

    public static readonly IReadOnlyList<DrinkDef> PackDrinks =
        All.Where(d => d.Source == DrinkSource.Pack).ToList();

    private static readonly Dictionary<string, DrinkDef> ById =
        All.ToDictionary(d => d.Id);

    public static DrinkDef? Get(string? id) =>
        id is not null && ById.TryGetValue(id, out var d) ? d : null;

    /// <summary>Purchase drinks gate on lifetime earnings; pack drinks on owning a copy.</summary>
    public static bool IsUnlocked(DrinkDef drink, GameState state) =>
        drink.Source == DrinkSource.Pack
            ? state.CopiesOf(drink.Id) >= 1
            : state.TotalEarned >= drink.UnlockAtEarned;

    public static IEnumerable<DrinkDef> UnlockedFor(GameState state) =>
        All.Where(d => IsUnlocked(d, state));

    /// <summary>The next purchase drink you have not reached yet, for the "coming up" hint.</summary>
    public static DrinkDef? NextLocked(GameState state) =>
        All.FirstOrDefault(d => d.Source == DrinkSource.Purchase && !IsUnlocked(d, state));
}
