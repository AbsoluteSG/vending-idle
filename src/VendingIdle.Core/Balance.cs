namespace VendingIdle.Core;

/// <summary>
/// Every tuning number in the game lives here. Nothing else should hard-code a
/// cost, a rate or a multiplier -- if you want to re-balance the prototype, this
/// is the only file you need to open.
/// </summary>
public static class Balance
{
    // ---- Grid ------------------------------------------------------------
    public const int Columns = 3;

    /// <summary>
    /// Rows the cabinet ships with. The glass is sized to show exactly this many,
    /// so a new machine reads as a machine with empty shelves rather than as a
    /// single slot floating in a void. Rows beyond this are still allocated on
    /// demand as you expand upward, and then the grid scrolls.
    /// </summary>
    public const int DefaultRows = 4;

    /// <summary>
    /// What <see cref="Columns"/> was in save version 1. Slot indices are
    /// row-major, so the grid width is baked into every index on disk and an old
    /// save has to be re-laid rather than read straight back.
    /// </summary>
    public const int LegacyColumnsV1 = 4;

    /// <summary>Cost of the Nth slot (N = slots already owned).</summary>
    public const double SlotBaseCost = 45.0;
    public const double SlotCostGrowth = 1.78;

    // ---- Clicking --------------------------------------------------------
    /// <summary>
    /// Payout when every slot is empty. A floor that keeps a dry machine playable,
    /// deliberately kept to roughly a tenth of the cheapest drink -- any higher and
    /// shaking the machine competes with actually stocking it.
    /// </summary>
    public const double SpareChange = 0.1;

    /// <summary>
    /// Bottles a shake knocks out of each stocked slot. The default is one from
    /// every slot at once -- a shake rattles the whole cabinet, so every loaded
    /// coil gives something up, unlike a customer's single purchase.
    /// </summary>
    public const int ShakeBottlesPerSlot = 1;

    // ---- Chains ----------------------------------------------------------
    /// <summary>
    /// Hops a single cascade may take beyond the slot that started it, before
    /// upgrades and Relay Rum add more. A cascade also stops when a roll fails or
    /// it runs out of stocked slots it has not already visited, so this is the
    /// ceiling rather than the usual length.
    /// </summary>
    public const int ChainHopsBase = 1;

    /// <summary>
    /// Each hop multiplies the chance of the next one. Chains are the design
    /// pillar, so they have to be able to run -- but a flat re-roll makes the
    /// expected length a hyperbola in the chance, which spikes without warning as
    /// upgrades push it up. Decay keeps the tail finite and the curve legible.
    /// </summary>
    public const double ChainDecay = 0.55;

    /// <summary>
    /// Share of a drink's value a chain hop pays in cash. Cascades multiply
    /// bottles, and bottles are money, so an unshaded hop makes chains a value
    /// multiplier by the back door -- which is the exact failure both earlier
    /// balance passes were undone by. Hops pay full tokens and full stock
    /// consumption; what they pay less of is cash, so a cascade is worth
    /// building for the collection rather than for the till.
    /// </summary>
    public const double ChainHopPayoutShare = 0.4;

    /// <summary>Machine-wide chain chance per level of the Live Wire upgrade.</summary>
    public const double ChainChancePerLevel = 0.015;
    public const double ChainChanceMax = 0.75;

    /// <summary>Extra hops per level of the Longer Coils upgrade.</summary>
    public const int ChainHopsPerLevel = 1;

    // ---- Spectacle -------------------------------------------------------
    // Thresholds for the feedback ladder. They live here rather than in the draw
    // code because they are balance, not presentation: what counts as a big
    // moment moves whenever the chain economy moves.

    /// <summary>Cascade length that earns a banner near the machine.</summary>
    public const int ChainBannerHops = 3;

    /// <summary>Cascade length that earns the full-screen slam.</summary>
    public const int ChainSlamHops = 4;

    /// <summary>Cascade length that earns the slam plus speed lines and a held beat.</summary>
    public const int ChainMegaHops = 8;

    public const double CritMultiplier = 2.0;
    public const double CritChanceBase = 0.02;
    public const double CritChancePerLevel = 0.02;
    public const double CritChanceMax = 0.60;

    // ---- Stock -----------------------------------------------------------
    public const int SlotCapacityBase = 10;
    public const int SlotCapacityPerLevel = 5;

    // ---- Customers (auto-clickers) ---------------------------------------
    /// <summary>Seconds between clicks for a single customer at zero speed upgrades.</summary>
    public const double CustomerIntervalBase = 2.5;
    public const double CustomerSpeedPerLevel = 0.92;
    /// <summary>Interval floor so upgrades can never divide by ~zero.</summary>
    public const double CustomerIntervalMin = 0.05;
    /// <summary>Safety valve: most clicks resolved in a single simulation step.</summary>
    public const int MaxClicksPerStep = 2000;

    // ---- Restocking ------------------------------------------------------
    public const double RestockDiscountPerLevel = 0.96;
    public const double RestockDiscountMin = 0.25;

    public const double AutoRestockerBaseCost = 200.0;
    public const double AutoRestockerCostGrowth = 1.7;
    /// <summary>Seconds per unit refilled by an auto-restocker at zero speed upgrades.</summary>
    public const double AutoRestockIntervalBase = 3.0;
    public const double AutoRestockSpeedPerLevel = 0.90;
    public const double AutoRestockIntervalMin = 0.05;

    // ---- Supply crates (packs) -------------------------------------------
    /// <summary>Crate tokens earned per bottle sold. Deliberately flat: pacing
    /// tracks bottles vended, not the (exponential) money curve.</summary>
    public const long TokensPerBottle = 1;
    /// <summary>Extra tokens when a dispense crits.</summary>
    public const long CritTokenBonus = 1;

    /// <summary>Extra tokens per bottle per level of the Loyalty Scheme upgrade.</summary>
    public const double TokensPerBottlePerLevel = 0.25;


/// <summary>
    /// Crate price. Flat, forever -- a crate costs what a crate costs, the way a
    /// pack does in every game that sells them. Pacing lives in
    /// the pull table, not in the price and not behind a clock.
    /// </summary>
    public const double PackCost = 250.0;

    /// <summary>Seconds in a day, for rate reporting.</summary>
    public const double SecondsPerDay = 86_400.0;

    /// <summary>
    /// Fraction of the crate price refunded when a pull is already at its level
    /// ceiling. Without it the late collection is mostly dead pulls; with it a
    /// duplicate still feeds the next crate.
    /// </summary>
    public const double DuplicateRefund = 0.35;

    /// <summary>
    /// Level ceiling for the commonest tier. Rarer drinks cap lower -- a Mythic
    /// that needed 55 copies would never leave level 1 -- so the real ceiling is
    /// <see cref="DrinkDef.MaxEffectLevel"/> per drink.
    /// </summary>
    public const int EffectLevelMax = 10;

    /// <summary>
    /// Copies to climb from level L-1 to L is L, so reaching level L costs
    /// L(L+1)/2 in total: 55 copies for a maxed common, 6 for a maxed Mythic.
    /// </summary>
    public static int CopiesForLevel(int level) => level <= 0 ? 0 : level * (level + 1) / 2;

    /// <summary>Highest level a drink of this tier can reach.</summary>
    public static int MaxLevelFor(Rarity rarity) => rarity switch
    {
        Rarity.Common => 10,
        Rarity.Uncommon => 9,
        Rarity.Rare => 7,
        Rarity.Epic => 5,
        Rarity.Legendary => 4,
        _ => 3
    };

    // ---- Time ------------------------------------------------------------
    /// <summary>Live simulation runs at a fixed 20 Hz.</summary>
    public const double TickSeconds = 0.05;
    /// <summary>Offline catch-up replays the same Step() at 1 s granularity...</summary>
    public const double OfflineStepSeconds = 1.0;
    /// <summary>...for at most 8 hours.</summary>
    public const double OfflineMaxSeconds = 8 * 60 * 60;

    public const double AutosaveIntervalSeconds = 15.0;

    /// <summary>Standard idle-game exponential price curve.</summary>
    public static double Cost(double baseCost, double growth, int level) =>
        baseCost * System.Math.Pow(growth, level);
}
