namespace VendingIdle.Core;

/// <summary>
/// Every tuning number in the game lives here. Nothing else should hard-code a
/// cost, a rate or a multiplier -- if you want to re-balance the prototype, this
/// is the only file you need to open.
/// </summary>
public static class Balance
{
    // ---- Grid ------------------------------------------------------------
    public const int Columns = 4;

    /// <summary>Cost of the Nth slot (N = slots already owned).</summary>
    public const double SlotBaseCost = 25.0;
    public const double SlotCostGrowth = 1.55;

    // ---- Clicking --------------------------------------------------------
    /// <summary>
    /// Payout when every slot is empty. A floor that keeps a dry machine playable,
    /// deliberately kept to roughly a tenth of the cheapest drink -- any higher and
    /// shaking the machine competes with actually stocking it.
    /// </summary>
    public const double SpareChange = 0.1;

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

    /// <summary>Token price of the Nth crate (N = crates already opened).</summary>
    public const double PackBaseCost = 300.0;
    public const double PackCostGrowth = 1.12;

    /// <summary>Duplicate copies past this stop raising the drink's effect level.</summary>
    public const int EffectLevelMax = 5;

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
