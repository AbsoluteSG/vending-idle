using System;
using System.Collections.Generic;
using System.Globalization;

namespace VendingIdle.Core;

/// <summary>
/// What a pack drink does.
///
/// Every effect fires when the drink is *sold* -- shaken out, or bought by a
/// customer. None of them are passive-while-stocked any more. Auras asked you to
/// keep every slot topped up all the time to get anything out of them, and this
/// is not a game about maintaining a full machine; it is a game about shaking one.
/// An effect you have to babysit is an effect nobody uses.
///
/// The chain effects are scoped to the cascade the drink *starts*, so a deck is
/// built by choosing what sits in the slots you shake rather than by blanketing
/// the cabinet.
///
/// The design rule they all still obey: no effect multiplies raw value. Both previous balance disasters came from a second system
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

    /// <summary>This drink's own dispenses crit more often.</summary>
    CritBoost,

    /// <summary>Selling it pulls a customer purchase forward on the spot.</summary>
    CustomerPull,

    /// <summary>Selling it sometimes refunds what the bottle cost to stock.</summary>
    Rebate,

    // ---- Combo pieces ----------------------------------------------------
    // These exist to be worth *more together than apart*. Each one is weak read
    // on its own card and only pays off once a cascade is actually running, so
    // the interesting decision is which of them share the glass at once.

    /// <summary>Cascades this drink starts get extra hops. The enabler the rest build on.</summary>
    ChainExtend,

    /// <summary>Hops in its cascades may crit, which they otherwise never do.</summary>
    ChainCrit,

    /// <summary>Every hop in its cascades pays bonus crate tokens.</summary>
    ChainToken,

    /// <summary>Hops in its cascades do not consume stock.</summary>
    ChainPreserve,

    /// <summary>Chance a dispense knocks an extra bottle out of the same slot.</summary>
    DoubleDrop,

    /// <summary>Chance a dispense seeds a cascade even when the drink cannot chain.</summary>
    SparkChain,

    // ---- Position, layout and time ---------------------------------------
    // The grid had no meaning before these: a slot was a slot. Now height,
    // neighbours and neglect all change what a drink is worth, so *where* you
    // put something is a decision rather than a formality.

    /// <summary>Worth more the further it falls to the tray.</summary>
    FallValue,

    /// <summary>Worth more while it sits in the highest unlocked row.</summary>
    TopRow,

    /// <summary>Every drink in its row crits more often.</summary>
    RowForeman,

    /// <summary>Worth more when the same drink sits next to it.</summary>
    TwinBonus,

    /// <summary>Worth more when nothing sits next to it.</summary>
    LonerBonus,

    /// <summary>Its cascades hop to neighbouring slots rather than round-robin.</summary>
    DominoRouting,

    /// <summary>Its cascades take one last hop back to where they started.</summary>
    Boomerang,

    /// <summary>Banks value while its slot sits empty, and pays it out on the next sale.</summary>
    ChargeUp,

    /// <summary>Bottles gain value the longer they sit unsold.</summary>
    Ageing,

    /// <summary>Pays bonus tokens for every distinct pack drink loaded in the machine.</summary>
    Curator,

    /// <summary>Counts as any drink when a neighbour checks what is beside it.</summary>
    Mimic
}

public sealed class EffectDef
{
    public required EffectKind Kind { get; init; }

    /// <summary>True when the effect shapes a cascade rather than the dispense itself.</summary>
    public required bool ShapesChain { get; init; }

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

    // ---- Combo pieces ----------------------------------------------------

    /// <summary>
    /// Extra hops granted to every cascade. Deliberately the slowest-scaling
    /// number in the game: hops multiply every other chain effect at once, so a
    /// second one is worth far more than a second of anything else.
    /// </summary>
    public static int ChainHops(int level) => level <= 0 ? 0 : (level + 1) / 2;

    /// <summary>Chance an individual chain hop rolls a crit.</summary>
    public static double ChainCritChance(int level) => level <= 0 ? 0.0 : 0.08 + 0.06 * level;

    /// <summary>Bonus tokens per chain hop.</summary>
    public static long ChainTokens(int level) => level <= 0 ? 0 : level;

    /// <summary>Chance a chain hop leaves the bottle on the shelf.</summary>
    public static double ChainPreserveChance(int level) => level <= 0 ? 0.0 : 0.10 + 0.07 * level;

    /// <summary>Chance to knock a second bottle out of the slot being served.</summary>
    public static double DoubleDropChance(int level) => level <= 0 ? 0.0 : 0.05 + 0.035 * level;

    /// <summary>Chance to start a cascade from a drink that has no chain of its own.</summary>
    public static double SparkChance(int level) => level <= 0 ? 0.0 : 0.05 + 0.03 * level;

    // ---- Position, layout and time ---------------------------------------

    /// <summary>Value multiplier per row of fall. Capped by the grid, then by Balance.</summary>
    public static double FallValue(int level, int rows) =>
        level <= 0 ? 0.0
        : Math.Min(Balance.FallValueMax,
                   Balance.FallValuePerRow * rows * (0.5 + 0.1 * level));

    public static double TopRowValue(int level) =>
        level <= 0 ? 0.0 : Balance.TopRowBonus * (0.5 + 0.1 * level);

    /// <summary>Crit chance handed to every drink sharing the foreman's row.</summary>
    public static double RowCritBonus(int level) => level <= 0 ? 0.0 : 0.03 + 0.02 * level;

    public static double TwinValue(int level) => level <= 0 ? 0.0 : 0.25 + 0.15 * level;
    public static double LonerValue(int level) => level <= 0 ? 0.0 : 0.30 + 0.18 * level;

    /// <summary>Chance a finished cascade loops back to its origin.</summary>
    public static double BoomerangChance(int level) => level <= 0 ? 0.0 : 0.15 + 0.09 * level;

    /// <summary>Flat value banked per idle second, before the time cap.</summary>
    public static double ChargeRate(int level) =>
        level <= 0 ? 0.0 : Balance.ChargePerSecond * (0.4 + 0.12 * level);

    /// <summary>Value multiplier added per idle second, before the time cap.</summary>
    public static double AgeingRate(int level) =>
        level <= 0 ? 0.0 : Balance.AgeingPerSecond * (0.4 + 0.12 * level);

    /// <summary>
    /// Bonus tokens per distinct pack drink loaded. Small per drink on purpose:
    /// it multiplies against however many slots the player runs, and at 0.3+0.25
    /// per level a three-slot machine was already earning five times the base
    /// token rate -- which quadrupled crates in a greedy half hour and dragged
    /// the whole money curve up behind it.
    /// </summary>
    public static double CuratorTokens(int level) => level <= 0 ? 0.0 : 0.06 + 0.04 * level;
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
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.ChainChance(l)) + " to vend a second slot"
        },
        new()
        {
            Kind = EffectKind.StockPreserve,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.PreserveChance(l)) + " to keep the bottle"
        },
        new()
        {
            Kind = EffectKind.CourierDrop,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.CourierChance(l)) + " to refill a dry slot"
        },
        new()
        {
            Kind = EffectKind.CritBoost,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.CritBonus(l)) + " double drop on its own sales"
        },
        new()
        {
            Kind = EffectKind.CustomerPull,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.CustomerSpeedup(l)) + " to pull a customer in on sale"
        },
        new()
        {
            Kind = EffectKind.Rebate,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.RestockCut(l)) + " to refund the bottle it sold"
        },
        new()
        {
            Kind = EffectKind.ChainExtend,
            ShapesChain = true,
            Describe = l => "+" + EffectStrength.ChainHops(l) + " hop"
                            + (EffectStrength.ChainHops(l) == 1 ? "" : "s") + " on its chains"
        },
        new()
        {
            Kind = EffectKind.ChainCrit,
            ShapesChain = true,
            Describe = l => Pct(EffectStrength.ChainCritChance(l)) + " crit on its chain hops"
        },
        new()
        {
            Kind = EffectKind.ChainToken,
            ShapesChain = true,
            Describe = l => "+" + EffectStrength.ChainTokens(l) + " tk per hop it causes"
        },
        new()
        {
            Kind = EffectKind.ChainPreserve,
            ShapesChain = true,
            Describe = l => Pct(EffectStrength.ChainPreserveChance(l)) + " of its hops keep stock"
        },
        new()
        {
            Kind = EffectKind.DoubleDrop,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.DoubleDropChance(l)) + " for a second bottle"
        },
        new()
        {
            Kind = EffectKind.FallValue,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.FallValue(l, 1)) + " value per row of fall"
        },
        new()
        {
            Kind = EffectKind.TopRow,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.TopRowValue(l)) + " value on the top row"
        },
        new()
        {
            Kind = EffectKind.RowForeman,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.RowCritBonus(l)) + " crit for its whole row"
        },
        new()
        {
            Kind = EffectKind.TwinBonus,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.TwinValue(l)) + " value beside its twin"
        },
        new()
        {
            Kind = EffectKind.LonerBonus,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.LonerValue(l)) + " value with no neighbours"
        },
        new()
        {
            Kind = EffectKind.DominoRouting,
            ShapesChain = true,
            Describe = _ => "its chains travel slot to slot"
        },
        new()
        {
            Kind = EffectKind.Boomerang,
            ShapesChain = true,
            Describe = l => Pct(EffectStrength.BoomerangChance(l)) + " for a chain to loop back"
        },
        new()
        {
            Kind = EffectKind.ChargeUp,
            ShapesChain = false,
            Describe = l => "banks " + Money.Cash(EffectStrength.ChargeRate(l)) + "/s while empty"
        },
        new()
        {
            Kind = EffectKind.Ageing,
            ShapesChain = false,
            Describe = l => "+" + Pct(EffectStrength.AgeingRate(l)) + " value per second unsold"
        },
        new()
        {
            Kind = EffectKind.Curator,
            ShapesChain = false,
            Describe = l => "+" + EffectStrength.CuratorTokens(l)
                                      .ToString("0.##", CultureInfo.InvariantCulture)
                            + " tk per distinct drink loaded"
        },
        new()
        {
            Kind = EffectKind.Mimic,
            ShapesChain = false,
            Describe = _ => "counts as any drink to its neighbours"
        },
        new()
        {
            Kind = EffectKind.SparkChain,
            ShapesChain = false,
            Describe = l => Pct(EffectStrength.SparkChance(l)) + " to start a chain"
        }
    };

    /// <summary>
    /// Built by scanning rather than indexing <see cref="All"/> by enum value.
    /// The list and the enum used to have to stay in the same order by hand, and
    /// inserting a kind anywhere but the end silently shifted every description
    /// onto the wrong effect -- a mistake with no compiler error and no obvious
    /// symptom.
    /// </summary>
    private static readonly EffectDef[] ByKind = BuildLookup();

    private static EffectDef[] BuildLookup()
    {
        var count = Enum.GetValues<EffectKind>().Length;
        var table = new EffectDef[count];

        foreach (var def in All) table[(int)def.Kind] = def;

        foreach (var kind in Enum.GetValues<EffectKind>())
            if (table[(int)kind] is null)
                throw new InvalidOperationException($"EffectDatabase has no entry for {kind}.");

        return table;
    }

    public static EffectDef Get(EffectKind kind) => ByKind[(int)kind];
}
