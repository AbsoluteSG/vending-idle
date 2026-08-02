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
            UnlockAtEarned = 0.0,
            SoundPitch = 0.38
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
            UnlockAtEarned = 2_000.0,
            SoundPitch = 0.16
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
            UnlockAtEarned = 90_000.0,
            SoundPitch = 0.0
        },
        new()
        {
            Id = "ginger_snap",
            Name = "Ginger Snap",
            Rarity = Rarity.Uncommon,
            Color = 0xD9A441,
            Value = 19.0,
            RestockUnitCost = 7.4,
            RestockGrowth = 1.015,
            UnlockAtEarned = 600_000.0,
            SoundPitch = -0.06
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
            UnlockAtEarned = 5_000_000.0,
            SoundPitch = -0.14
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
            UnlockAtEarned = 100_000_000.0,
            SoundPitch = -0.28
        },
        new()
        {
            Id = "arctic_tonic",
            Name = "Arctic Tonic",
            Rarity = Rarity.Epic,
            Color = 0x4FC3D9,
            Value = 260.0,
            RestockUnitCost = 112.0,
            RestockGrowth = 1.035,
            UnlockAtEarned = 550_000_000.0,
            SoundPitch = -0.35
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
            UnlockAtEarned = 1_500_000_000.0,
            SoundPitch = -0.42
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
            Value = 0.95,
            RestockUnitCost = 0.4,
            RestockGrowth = 1.0,
            SoundPitch = 0.30
        },
        new()
        {
            Id = "bulk_bottle",
            Name = "Bulk Bottle",
            Rarity = Rarity.Common,
            Source = DrinkSource.Pack,
            Effect = EffectKind.Rebate,
            Color = 0x9A7B5B,
            Value = 0.9,
            RestockUnitCost = 0.38,
            RestockGrowth = 1.0,
            SoundPitch = 0.24
        },
        new()
        {
            Id = "bottomless_cup",
            Name = "Bottomless Cup",
            Rarity = Rarity.Uncommon,
            Source = DrinkSource.Pack,
            Effect = EffectKind.StockPreserve,
            Color = 0x2E86DE,
            Value = 2.7,
            RestockUnitCost = 1.15,
            RestockGrowth = 1.01,
            SoundPitch = 0.08
        },
        new()
        {
            Id = "loyalty_lager",
            Name = "Loyalty Lager",
            Rarity = Rarity.Uncommon,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CustomerPull,
            Color = 0xD4A017,
            Value = 2.9,
            RestockUnitCost = 1.25,
            RestockGrowth = 1.01,
            SoundPitch = 0.04
        },
        new()
        {
            Id = "static_shock",
            Name = "Static Shock",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CritBoost,
            Color = 0xE45FE8,
            Value = 8.4,
            RestockUnitCost = 3.4,
            RestockGrowth = 1.02,
            SoundPitch = -0.20
        },
        new()
        {
            Id = "courier_cola",
            Name = "Courier Cola",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CourierDrop,
            Color = 0x1ABC9C,
            Value = 8.8,
            RestockUnitCost = 3.6,
            RestockGrowth = 1.02,
            SoundPitch = -0.22
        },

        // The combo set. Every one of these is deliberately underwhelming read
        // on its own card -- Relay Rum does nothing at all without something to
        // chain, and Jumper Juice only starts cascades it cannot itself extend.
        // They are priced as utility, below the value tier they sit in, because
        // the payoff is meant to come from what they are next to.
        new()
        {
            Id = "relay_rum",
            Name = "Relay Rum",
            Rarity = Rarity.Epic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainExtend,
            Color = 0xE67E22,
            Value = 16.0,
            RestockUnitCost = 6.6,
            RestockGrowth = 1.02,
            SoundPitch = -0.18
        },
        new()
        {
            Id = "jumper_juice",
            Name = "Jumper Juice",
            Rarity = Rarity.Common,
            Source = DrinkSource.Pack,
            Effect = EffectKind.SparkChain,
            Color = 0xA3D94F,
            Value = 1.0,
            RestockUnitCost = 0.42,
            RestockGrowth = 1.0,
            SoundPitch = 0.27
        },
        new()
        {
            Id = "twin_tap",
            Name = "Twin Tap",
            Rarity = Rarity.Common,
            Source = DrinkSource.Pack,
            Effect = EffectKind.DoubleDrop,
            Color = 0x5DADE2,
            Value = 0.95,
            RestockUnitCost = 0.4,
            RestockGrowth = 1.0,
            SoundPitch = 0.21
        },
        new()
        {
            Id = "surge_syrup",
            Name = "Surge Syrup",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainCrit,
            Color = 0xF15BB5,
            Value = 8.5,
            RestockUnitCost = 3.5,
            RestockGrowth = 1.02,
            SoundPitch = -0.24
        },
        new()
        {
            Id = "loyalty_lemon",
            Name = "Loyalty Lemon",
            Rarity = Rarity.Uncommon,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainToken,
            Color = 0xF4E04D,
            Value = 2.8,
            RestockUnitCost = 1.2,
            RestockGrowth = 1.01,
            SoundPitch = 0.06
        },
        new()
        {
            Id = "echo_elixir",
            Name = "Echo Elixir",
            Rarity = Rarity.Rare,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainPreserve,
            Color = 0x9B8AFB,
            Value = 8.2,
            RestockUnitCost = 3.3,
            RestockGrowth = 1.02,
            SoundPitch = -0.19
        },

        // ---- The tail ----------------------------------------------------
        // Epics upward are the chase. Each one is a chain piece strong enough to
        // reshape a deck, which is the only thing that justifies pull rates this
        // steep -- a Mythic is roughly one crate in seven thousand and there is
        // no pity anywhere, so it has to be worth the wait on sight.
        new()
        {
            Id = "splice_soda",
            Name = "Splice Soda",
            Rarity = Rarity.Epic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainExtend,
            Color = 0xFF8A5B,
            Value = 15.5,
            RestockUnitCost = 6.4,
            RestockGrowth = 1.02,
            SoundPitch = -0.16
        },
        new()
        {
            Id = "kick_start",
            Name = "Kick Start",
            Rarity = Rarity.Epic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.SparkChain,
            Color = 0xC8F04A,
            Value = 15.0,
            RestockUnitCost = 6.2,
            RestockGrowth = 1.02,
            SoundPitch = -0.12
        },
        new()
        {
            Id = "double_dutch",
            Name = "Double Dutch",
            Rarity = Rarity.Epic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.DoubleDrop,
            Color = 0x4FD1C5,
            Value = 16.5,
            RestockUnitCost = 6.8,
            RestockGrowth = 1.02,
            SoundPitch = -0.15
        },
        new()
        {
            Id = "circuit_cider",
            Name = "Circuit Cider",
            Rarity = Rarity.Legendary,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainCrit,
            Color = 0xFF4D9D,
            Value = 30.0,
            RestockUnitCost = 12.5,
            RestockGrowth = 1.03,
            SoundPitch = -0.31
        },
        new()
        {
            Id = "cascade_cordial",
            Name = "Cascade Cordial",
            Rarity = Rarity.Legendary,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainPreserve,
            Color = 0x7B6CF6,
            Value = 29.0,
            RestockUnitCost = 12.0,
            RestockGrowth = 1.03,
            SoundPitch = -0.33
        },
        new()
        {
            Id = "vault_vintage",
            Name = "Vault Vintage",
            Rarity = Rarity.Legendary,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainToken,
            Color = 0xE8C547,
            Value = 31.0,
            RestockUnitCost = 12.8,
            RestockGrowth = 1.03,
            SoundPitch = -0.29
        },
        new()
        {
            Id = "perpetual_punch",
            Name = "Perpetual Punch",
            Rarity = Rarity.Mythic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.ChainExtend,
            Color = 0xFF2E63,
            Value = 110.0,
            RestockUnitCost = 46.0,
            RestockGrowth = 1.035,
            SoundPitch = -0.46
        },
        new()
        {
            Id = "midas_mixer",
            Name = "Midas Mixer",
            Rarity = Rarity.Mythic,
            Source = DrinkSource.Pack,
            Effect = EffectKind.CritBoost,
            Color = 0xFFD700,
            Value = 108.0,
            RestockUnitCost = 45.0,
            RestockGrowth = 1.035,
            SoundPitch = -0.48
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
