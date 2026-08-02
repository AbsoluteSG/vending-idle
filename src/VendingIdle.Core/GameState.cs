using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VendingIdle.Core;

/// <summary>
/// The entire savegame. Player actions live here as Try* methods so the UI stays
/// a thin renderer -- it asks, the state decides whether it can be afforded.
/// </summary>
public sealed class GameState
{
    /// <summary>
    /// 2: the grid narrowed from 4 columns to 3, which invalidates every stored
    /// slot index -- <see cref="Migrate"/> re-lays version 1 saves. The supply
    /// crate fields arrived in the same version and need no re-lay of their own:
    /// a save without them deserializes to empty, and defaulting IS the
    /// migration (see the crate section of <see cref="Normalize"/>).
    /// </summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    public double Money { get; set; }
    public double TotalEarned { get; set; }

    /// <summary>
    /// Sound silenced by the player. A setting rather than game state, but the
    /// save file is the only thing this prototype persists, and a mute that
    /// forgets itself every launch is worse than no mute at all. Absent from an
    /// older save it deserialises to false, which is the right default anyway,
    /// so this costs no save version.
    /// </summary>
    public bool Muted { get; set; }

    // ---- Supply crates ---------------------------------------------------
    /// <summary>
    /// Crate tokens, earned per bottle sold. Fractional because the Loyalty
    /// Scheme upgrade buys quarter-tokens; the gauge already rendered decimals
    /// before it could produce them.
    /// </summary>
    public double Tokens { get; set; }

    public int PacksOpened { get; set; }

    /// <summary>
    /// Seconds into the current Rush Hour cycle. Persisted so quitting mid-burst
    /// does not hand out a fresh one on load.
    /// </summary>
    public double RushTimer { get; set; }


    /// <summary>Copies owned per pack drink id. First copy unlocks; more raise the effect level.</summary>
    public Dictionary<string, int> DrinkCopies { get; set; } = new();

    /// <summary>
    /// A crate roll that has been paid for but not yet redeemed -- the drink
    /// bobbing above the crate. While set, no further crate can be opened.
    /// Lives in the save so quitting mid-reveal cannot eat the roll.
    /// </summary>
    public string? PendingRevealId { get; set; }

    public List<Slot> Slots { get; set; } = new();

    /// <summary>Indexed by <see cref="UpgradeId"/>.</summary>
    public int[] UpgradeLevels { get; set; } = new int[UpgradeDatabase.Count];

    /// <summary>Round-robin cursor so clicks dispense from slots in sequence.</summary>
    public int DispenseCursor { get; set; }

    /// <summary>Fractional customer clicks carried between simulation steps.</summary>
    public double CustomerClickAccumulator { get; set; }

    public long LastSavedUnixSeconds { get; set; }

    // ---- Lifetime stats (display only) -----------------------------------
    public long TotalClicks { get; set; }
    public long TotalCansSold { get; set; }
    public long TotalCrits { get; set; }

    // ---------------------------------------------------------------------
    // Derived helpers
    // ---------------------------------------------------------------------

    [JsonIgnore] public int Customers => UpgradeLevels[(int)UpgradeId.Customers];

    [JsonIgnore] public int SlotCapacity =>
        Modifiers.SlotCapacity(UpgradeLevels[(int)UpgradeId.SlotCapacity]);

    [JsonIgnore] public double ClickValueMultiplier =>
        Modifiers.ClickValueMultiplier(UpgradeLevels[(int)UpgradeId.ClickValue]);

    // Auras fold into the existing derived getters, so the simulation and the
    // UI pick them up with zero new call sites. The global caps are re-applied
    // These are the machine-wide baselines. Drink effects no longer feed into
    // them: an effect belongs to the drink being sold, and is applied at the
    // point of sale rather than aggregated across the cabinet.

    [JsonIgnore] public double CritChance =>
        Math.Min(Balance.CritChanceMax,
                 Modifiers.CritChance(UpgradeLevels[(int)UpgradeId.CritChance]));

    [JsonIgnore] public double CustomerInterval =>
        Math.Max(Balance.CustomerIntervalMin,
                 Modifiers.CustomerInterval(UpgradeLevels[(int)UpgradeId.CustomerSpeed]));

    [JsonIgnore] public double RestockDiscount =>
        Math.Max(Balance.RestockDiscountMin,
                 Modifiers.RestockDiscount(UpgradeLevels[(int)UpgradeId.RestockDiscount]));

    [JsonIgnore] public double AutoRestockInterval =>
        Modifiers.AutoRestockInterval(UpgradeLevels[(int)UpgradeId.AutoRestockSpeed]);

    /// <summary>
    /// Bottles a single shake takes from each stocked slot. Reads from
    /// <see cref="Balance"/> today; this is the seam a drink effect or an upgrade
    /// hangs off when one wants to knock out more than one per slot.
    /// </summary>
    [JsonIgnore] public int ShakeBottlesPerSlot => ShakeBottlesPerSlotUpgraded;

    /// <summary>
    /// Machine-wide chance for any dispense to start a cascade. Chain Fizz adds
    /// its own on top of this per-slot; this is the floor the upgrade buys, so
    /// chains are reachable without waiting on a crate roll.
    /// </summary>
    [JsonIgnore] public double ChainChance =>
        Math.Min(Balance.ChainChanceMax,
                 Modifiers.ChainChance(UpgradeLevels[(int)UpgradeId.ChainChance]));

    /// <summary>Hop ceiling from upgrades alone; the starting drink may add more.</summary>
    [JsonIgnore] public int MaxChainHops =>
        Modifiers.ChainHops(UpgradeLevels[(int)UpgradeId.ChainHops]);

    /// <summary>How much of its charge a cascade carries into the next hop.</summary>
    [JsonIgnore] public double ChainDecay =>
        Modifiers.ChainDecay(UpgradeLevels[(int)UpgradeId.ChainDecay]);

    /// <summary>Chance a hop forks and takes a second slot with it.</summary>
    [JsonIgnore] public double ChainForkChance =>
        Modifiers.ChainFork(UpgradeLevels[(int)UpgradeId.ChainFork]);

    [JsonIgnore] public int ShakeBottlesPerSlotUpgraded =>
        Modifiers.ShakeBottles(UpgradeLevels[(int)UpgradeId.ShakeYield]);

    [JsonIgnore] public double FollowThroughChance =>
        Modifiers.FollowThrough(UpgradeLevels[(int)UpgradeId.FollowThrough]);

    [JsonIgnore] public int CratesPerOpen =>
        Modifiers.CratesPerOpen(UpgradeLevels[(int)UpgradeId.BulkCrates]);

    [JsonIgnore] public double DuplicateRefundRate =>
        Modifiers.DuplicateRefund(UpgradeLevels[(int)UpgradeId.Salvage]);

    /// <summary>Multiplier pulling per-slot restock growth toward flat pricing.</summary>
    [JsonIgnore] public double RestockGrowthFactor =>
        1.0 - Modifiers.RestockGrowthCut(UpgradeLevels[(int)UpgradeId.RestockGrowthCut]);

    [JsonIgnore] public double OfflineMaxSeconds =>
        Modifiers.OfflineHours(UpgradeLevels[(int)UpgradeId.OfflineHours]) * 3600.0;

    [JsonIgnore] public double RushMultiplier =>
        Modifiers.RushMultiplier(UpgradeLevels[(int)UpgradeId.RushHour]);

    [JsonIgnore] public double SpareChangePayout =>
        Modifiers.SpareChange(UpgradeLevels[(int)UpgradeId.SpareChange]);

    /// <summary>True while a Rush Hour burst is running.</summary>
    [JsonIgnore] public bool RushActive =>
        RushMultiplier > 1.0 && RushTimer < Balance.RushDurationSeconds;

    [JsonIgnore] public double TokensPerBottle =>
        Modifiers.TokensPerBottle(UpgradeLevels[(int)UpgradeId.TokenRate]);

    /// <summary>
    /// Banks tokens from a sale. Everything that earns them comes through here --
    /// bottles, crits, chain hops -- so there is one seam if a limit is ever
    /// wanted again.
    ///
    /// There is no limit now, and that is deliberate. Crates were behind a
    /// regenerating daily quota; opening packs is the loop this game is built
    /// around, and a clock telling you to come back tomorrow is the one thing
    /// that stops a loop being a loop. Pacing is the pull table's job.
    /// </summary>
    public double EarnTokens(double amount)
    {
        if (amount <= 0.0) return 0.0;

        Tokens += amount;
        return amount;
    }

    [JsonIgnore] public int SlotsOwned => Slots.Count(s => s.Unlocked);

    [JsonIgnore] public int AutoRestockersOwned => Slots.Count(s => s.HasAutoRestocker);

    /// <summary>Number of allocated rows (always one spare row above the top unlocked one).</summary>
    [JsonIgnore] public int RowCount => Slots.Count / Balance.Columns;

    [JsonIgnore] public double NextSlotCost =>
        Balance.Cost(Balance.SlotBaseCost,
                     Modifiers.SlotCostGrowth(UpgradeLevels[(int)UpgradeId.SlotPrice]),
                     SlotsOwned);

    [JsonIgnore] public double NextAutoRestockerCost => AutoRestockerCostFor(1);

    /// <summary>
    /// Cost of the next <paramref name="count"/> auto-restockers together. The
    /// price climbs with how many are already owned, so buying a row of them is
    /// the sum of an escalating run and never one price times the count -- which
    /// is what a row-mode button would otherwise advertise.
    /// </summary>
    public double AutoRestockerCostFor(int count)
    {
        if (count <= 0) return 0.0;

        var owned = AutoRestockersOwned;
        var total = 0.0;

        for (var i = 0; i < count; i++)
            total += Balance.Cost(Balance.AutoRestockerBaseCost,
                                  Modifiers.AutoRestockerGrowth(
                                      UpgradeLevels[(int)UpgradeId.AutoRestockerPrice]),
                                  owned + i);

        return total;
    }

    [JsonIgnore] public int TotalStock => Slots.Sum(s => s.Stock);

    /// <summary>
    /// Flat, forever. Kept as a property rather than inlining the constant so
    /// the UI and the crate keep one thing to read.
    /// </summary>
    [JsonIgnore] public double NextPackCost => Balance.PackCost;

    /// <summary>Blocked while a rolled drink is still bobbing above the crate.</summary>
    [JsonIgnore] public bool CanOpenPack =>
        PendingRevealId is null && Tokens >= NextPackCost;

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    /// <summary>Highest row holding an unlocked slot, or 0 when none do.</summary>
    [JsonIgnore] public int TopUnlockedRow
    {
        get
        {
            var top = 0;
            foreach (var slot in Slots)
                if (slot.Unlocked && slot.Row > top) top = slot.Row;
            return top;
        }
    }

    /// <summary>Distinct pack drinks currently loaded anywhere in the machine.</summary>
    [JsonIgnore] public int DistinctPackDrinksLoaded
    {
        get
        {
            Span<bool> seen = stackalloc bool[DrinkDatabase.PackDrinks.Count];
            var count = 0;

            foreach (var slot in Slots)
            {
                if (!slot.Unlocked || slot.Drink is not { Source: DrinkSource.Pack } drink) continue;

                var index = IndexInPackRoster(drink.Id);
                if (index < 0 || seen[index]) continue;

                seen[index] = true;
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// The four orthogonally adjacent slots. Diagonals deliberately do not count:
    /// the cabinet reads as a grid of shelves, and "next to" on a shelf means
    /// beside or directly above, not at a corner.
    /// </summary>
    public IEnumerable<Slot> NeighboursOf(Slot slot)
    {
        if (slot.Column > 0 && SlotAt(slot.Index - 1) is { } left) yield return left;
        if (slot.Column < Balance.Columns - 1 && SlotAt(slot.Index + 1) is { } right) yield return right;
        if (SlotAt(slot.Index - Balance.Columns) is { } below) yield return below;
        if (SlotAt(slot.Index + Balance.Columns) is { } above) yield return above;
    }

    /// <summary>
    /// True when <paramref name="other"/> counts as the same drink as
    /// <paramref name="drink"/> for a neighbour check. Mimic Mist answers yes to
    /// everything, which is the entire drink.
    /// </summary>
    public static bool CountsAsSameDrink(DrinkDef drink, DrinkDef? other)
    {
        if (other is null) return false;
        if (other.Id == drink.Id) return true;

        // One-way on purpose: a Mimic satisfies its neighbour's check, but two
        // Mimics beside each other do not resolve into anything, which is what a
        // copy-of-a-copy rule would need a depth guard for.
        return other.Effect == EffectKind.Mimic;
    }

    /// <summary>Crit bonus lent to this slot by a foreman sharing its row.</summary>
    public double RowForemanBonus(Slot slot)
    {
        var bonus = 0.0;

        foreach (var other in Slots)
        {
            if (other.Row != slot.Row || other.Index == slot.Index) continue;
            if (!other.Unlocked || other.Drink is not { } drink) continue;
            if (drink.Effect != EffectKind.RowForeman) continue;

            // Layout, not upkeep: the foreman has to be *placed* in the row, but
            // it does not have to be kept stocked to run it.
            bonus += EffectStrength.RowCritBonus(EffectLevelOf(drink));
        }

        return bonus;
    }

    // ---------------------------------------------------------------------
    // Per-drink effects
    // ---------------------------------------------------------------------

    /// <summary>
    /// The strength of <paramref name="kind"/> on this drink, or 0 when it does
    /// not carry that effect. Effects belong to the drink being sold now, not to
    /// the cabinet -- there is no machine-wide aggregation left to compute.
    /// </summary>
    public double EffectStrengthOf(DrinkDef? drink, EffectKind kind)
    {
        if (drink?.Effect != kind) return 0.0;

        var level = EffectLevelOf(drink);
        if (level <= 0) return 0.0;

        return kind switch
        {
            EffectKind.CritBoost => EffectStrength.CritBonus(level),
            EffectKind.CustomerPull => EffectStrength.CustomerSpeedup(level),
            EffectKind.Rebate => EffectStrength.RestockCut(level),
            EffectKind.ChainCrit => EffectStrength.ChainCritChance(level),
            EffectKind.ChainPreserve => EffectStrength.ChainPreserveChance(level),
            EffectKind.ChainToken => EffectStrength.ChainTokens(level),
            EffectKind.ChainExtend => EffectStrength.ChainHops(level),
            EffectKind.Boomerang => EffectStrength.BoomerangChance(level),
            EffectKind.TopRow => EffectStrength.TopRowValue(level),
            EffectKind.TwinBonus => EffectStrength.TwinValue(level),
            EffectKind.LonerBonus => EffectStrength.LonerValue(level),
            EffectKind.ChargeUp => EffectStrength.ChargeRate(level),
            EffectKind.Ageing => EffectStrength.AgeingRate(level),
            EffectKind.Curator => EffectStrength.CuratorTokens(level),
            EffectKind.DominoRouting => 1.0,
            _ => 0.0
        };
    }

    private static int IndexInPackRoster(string id)
    {
        for (var i = 0; i < DrinkDatabase.PackDrinks.Count; i++)
            if (DrinkDatabase.PackDrinks[i].Id == id)
                return i;
        return -1;
    }

    public int CopiesOf(string id) =>
        DrinkCopies.TryGetValue(id, out var n) ? n : 0;

    /// <summary>
    /// Effect level from duplicate copies. Levels cost progressively more copies
    /// (L copies to reach level L, so 55 for a maxed common), and the ceiling is
    /// the drink's own tier cap.
    /// </summary>
    public int EffectLevelOf(DrinkDef drink)
    {
        if (drink.Effect is null) return 0;

        var copies = CopiesOf(drink.Id);
        var max = drink.MaxEffectLevel;

        var level = 0;
        while (level < max && copies >= Balance.CopiesForLevel(level + 1)) level++;

        return level;
    }

    // ---------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------

    public static GameState NewGame()
    {
        var state = new GameState();
        state.EnsureRow(Balance.DefaultRows - 1);
        state.Slots[0].Unlocked = true;      // bottom-left, as designed
        state.Slots[0].DrinkId = DrinkDatabase.All[0].Id;
        state.EnsureSpareRow();

        state.LastSavedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return state;
    }

    /// <summary>
    /// Brings a save forward from an older layout. Slot indices are row-major, so
    /// narrowing the grid changes what every stored index means -- read back
    /// as-is, a version 1 save would scatter its slots across the wrong cells and
    /// could strand them above an empty row, where the purchase rule would never
    /// let the player reconnect them.
    ///
    /// Rather than guess at a geometric remap, the unlocked slots are compacted
    /// into the bottom of the new grid in their old order. Positions are not
    /// meaningful in themselves -- only the count, the contents, and the rule that
    /// a slot needs one below it -- and packing from index 0 satisfies all three.
    /// </summary>
    public void Migrate()
    {
        // Runs ahead of the version gate on purpose. The upgrade array's length
        // is a schema fact, not a version fact: adding an upgrade widens it for
        // every save on disk, including ones already at the current version,
        // and a short array from yesterday's build would throw the moment
        // anything read the new index.
        NormalizeUpgradeLevels();

        if (Version >= CurrentVersion)
        {
            Version = CurrentVersion;
            return;
        }

        // Version 3 made the crate price flat and put the pacing in a quota.
        // A balance banked against the old escalating price (which reached tens
        // of thousands of tokens) would buy a wall of crates at the new flat 250,
        // so it is clamped to a few crates' worth rather than carried across --
        // the collection is the long game now, and handing over a hundred pulls
        // on upgrade would skip the part that was just built.
        if (Version < 3)
        {
            Tokens = Math.Min(Tokens, Balance.PackCost * 5.0);
        }

        var carried = Slots
            .Where(s => s.Unlocked)
            .OrderBy(s => s.Index)
            .ToList();

        Slots = new List<Slot>();
        EnsureRow(Math.Max(Balance.DefaultRows, (carried.Count + Balance.Columns - 1) / Balance.Columns));

        for (var i = 0; i < carried.Count; i++)
        {
            Slots[i].Unlocked = true;
            Slots[i].DrinkId = carried[i].DrinkId;
            Slots[i].Stock = carried[i].Stock;
            Slots[i].HasAutoRestocker = carried[i].HasAutoRestocker;
        }

        Version = CurrentVersion;
    }

    /// <summary>
    /// Grows (or replaces) the upgrade array so every <see cref="UpgradeId"/> has
    /// a slot. Levels already stored keep their index, because the enum only ever
    /// gains members at the end -- reordering it would silently rewrite people's
    /// purchases into different upgrades.
    /// </summary>
    private void NormalizeUpgradeLevels()
    {
        if (UpgradeLevels.Length == UpgradeDatabase.Count) return;

        var grown = new int[UpgradeDatabase.Count];
        var carry = Math.Min(UpgradeLevels.Length, grown.Length);
        Array.Copy(UpgradeLevels, grown, carry);
        UpgradeLevels = grown;
    }

    /// <summary>Allocates rows up to and including <paramref name="row"/>.</summary>
    public void EnsureRow(int row)
    {
        while (RowCount <= row)
        {
            var baseIndex = Slots.Count;
            for (var c = 0; c < Balance.Columns; c++)
                Slots.Add(new Slot { Index = baseIndex + c });
        }
    }

    /// <summary>
    /// Keeps exactly one empty row above the highest unlocked row, which is what
    /// makes vertical expansion endless without ever allocating infinity.
    /// </summary>
    public void EnsureSpareRow()
    {
        var topUnlocked = -1;
        foreach (var s in Slots)
            if (s.Unlocked && s.Row > topUnlocked)
                topUnlocked = s.Row;

        EnsureRow(topUnlocked + 1);
    }

    public Slot? SlotAt(int index) =>
        index >= 0 && index < Slots.Count ? Slots[index] : null;

    // ---------------------------------------------------------------------
    // Player actions
    // ---------------------------------------------------------------------

    /// <summary>True when any slot on the given row has been bought.</summary>
    public bool IsRowOccupied(int row)
    {
        var baseIndex = row * Balance.Columns;
        if (row < 0 || baseIndex + Balance.Columns > Slots.Count) return false;

        for (var c = 0; c < Balance.Columns; c++)
            if (Slots[baseIndex + c].Unlocked)
                return true;

        return false;
    }

    /// <summary>A slot is buyable on the bottom row, or once the row below has any slot.</summary>
    public bool IsSlotPurchasable(Slot slot) =>
        !slot.Unlocked && (slot.Row == 0 || IsRowOccupied(slot.Row - 1));

    public bool TryBuySlot(int index)
    {
        var slot = SlotAt(index);
        if (slot is null || !IsSlotPurchasable(slot)) return false;

        var cost = NextSlotCost;
        if (Money < cost) return false;

        Money -= cost;
        slot.Unlocked = true;
        EnsureSpareRow();
        return true;
    }

    public bool TryAssignDrink(int index, string? drinkId)
    {
        var slot = SlotAt(index);
        if (slot is null || !slot.Unlocked) return false;

        var drink = DrinkDatabase.Get(drinkId);
        if (drinkId is not null && (drink is null || !DrinkDatabase.IsUnlocked(drink, this)))
            return false;

        // Swapping product empties the slot -- you cannot launder cheap stock
        // into an expensive drink by re-assigning a full shelf.
        if (slot.DrinkId != drinkId)
            slot.Stock = 0;

        slot.DrinkId = drinkId;
        return true;
    }

    public double RestockCost(Slot slot, int units)
    {
        var drink = slot.Drink;
        if (drink is null || units <= 0) return 0.0;
        return drink.RestockCost(slot.Stock, units, SlotCapacity, RestockGrowthFactor) * RestockDiscount;
    }

    /// <summary>Price of the next single can for this slot, discount included.</summary>
    public double UnitCost(Slot slot)
    {
        var drink = slot.Drink;
        return drink is null ? 0.0
            : drink.UnitCostAt(slot.Stock, SlotCapacity, RestockGrowthFactor) * RestockDiscount;
    }

    /// <summary>Units still needed to fill this slot.</summary>
    public int RoomIn(Slot slot) => Math.Max(0, SlotCapacity - slot.Stock);

    /// <summary>
    /// Restocks up to <paramref name="requestedUnits"/>, buying only as many as
    /// money and capacity allow. Returns the number actually added.
    /// </summary>
    public int Restock(Slot slot, int requestedUnits)
    {
        var drink = slot.Drink;
        if (drink is null || !slot.Unlocked) return 0;

        var room = RoomIn(slot);
        var wanted = Math.Min(requestedUnits, room);
        if (wanted <= 0) return 0;

        // Bought one can at a time so a partial restock is priced exactly the same
        // as the equivalent run of single purchases, however little money is left.
        var bought = 0;
        for (var i = 0; i < wanted; i++)
        {
            var unitCost = drink.UnitCostAt(slot.Stock, SlotCapacity, RestockGrowthFactor) * RestockDiscount;
            if (Money < unitCost) break;
            Money -= unitCost;
            slot.Stock++;
            bought++;
        }

        return bought;
    }

    public int RestockToFull(Slot slot) => Restock(slot, RoomIn(slot));

    public int RestockAll()
    {
        var total = 0;
        foreach (var slot in Slots)
            if (slot.Unlocked && slot.DrinkId is not null)
                total += RestockToFull(slot);
        return total;
    }

    public bool TryBuyAutoRestocker(int index)
    {
        var slot = SlotAt(index);
        if (slot is null || !slot.Unlocked || slot.HasAutoRestocker) return false;

        var cost = NextAutoRestockerCost;
        if (Money < cost) return false;

        Money -= cost;
        slot.HasAutoRestocker = true;
        return true;
    }

    public int UpgradeLevel(UpgradeId id) => UpgradeLevels[(int)id];

    public double UpgradeCost(UpgradeId id) =>
        UpgradeDatabase.Get(id).CostAt(UpgradeLevel(id));

    public bool CanBuyUpgrade(UpgradeId id)
    {
        var def = UpgradeDatabase.Get(id);
        return !def.IsMaxed(UpgradeLevel(id)) && Money >= UpgradeCost(id);
    }

    public bool TryBuyUpgrade(UpgradeId id)
    {
        if (!CanBuyUpgrade(id)) return false;
        Money -= UpgradeCost(id);
        UpgradeLevels[(int)id]++;
        return true;
    }

    /// <summary>
    /// Pays for a crate and rolls its drink. The drink is NOT granted yet -- it
    /// sits in <see cref="PendingRevealId"/> (bobbing above the crate) until
    /// <see cref="RedeemReveal"/> claims it. Returns the rolled id, or null when
    /// unaffordable or a reveal is already pending.
    /// </summary>
    public string? TryOpenPack(Random rng)
    {
        if (!CanOpenPack) return null;

        Tokens -= Balance.PackCost;
        PacksOpened++;
        PendingRevealId = PackSystem.Roll(rng).Id;
        return PendingRevealId;
    }

    /// <summary>
    /// Opens several crates at once and grants them outright, returning what came
    /// out. Only the caller's chosen highlight gets the reveal animation -- at a
    /// thousand crates an hour, twenty-five mystery-box reveals per press would be
    /// a minute of watching rather than a reward.
    /// </summary>
    public List<PackRedeem> OpenPacksBulk(Random rng, int count)
    {
        var results = new List<PackRedeem>();

        for (var i = 0; i < count; i++)
        {
            if (PendingRevealId is not null || Tokens < Balance.PackCost) break;

            Tokens -= Balance.PackCost;
            PacksOpened++;

            PendingRevealId = PackSystem.Roll(rng).Id;
            if (RedeemReveal() is { } redeem) results.Add(redeem);
        }

        return results;
    }

    /// <summary>Claims the pending reveal, granting the copy and unblocking the crate.</summary>
    public PackRedeem? RedeemReveal()
    {
        if (PendingRevealId is null) return null;

        var id = PendingRevealId;
        PendingRevealId = null;

        var before = CopiesOf(id);
        DrinkCopies[id] = before + 1;

        var drink = DrinkDatabase.Get(id);
        var level = drink is null ? 0 : EffectLevelOf(drink);
        var atMax = drink is not null && level >= drink.MaxEffectLevel;

        // A pull that cannot raise the level any further hands part of the crate
        // price back, so the long tail of the collection is never entirely dead.
        var refund = 0.0;
        if (atMax)
        {
            refund = Balance.PackCost * DuplicateRefundRate;
            Tokens += refund;
        }

        return new PackRedeem
        {
            DrinkId = id,
            WasNew = before == 0,
            Level = level,
            AtMax = atMax,
            Refund = refund
        };
    }

    /// <summary>
    /// Repairs a state loaded from disk: array resizes from older versions,
    /// slot indices, and the invariant spare row.
    /// </summary>
    public void Normalize()
    {
        if (UpgradeLevels.Length != UpgradeDatabase.Count)
        {
            var resized = new int[UpgradeDatabase.Count];
            Array.Copy(UpgradeLevels, resized, Math.Min(UpgradeLevels.Length, resized.Length));
            UpgradeLevels = resized;
        }

        for (var i = 0; i < Slots.Count; i++)
        {
            Slots[i].Index = i;
            if (Slots[i].Drink is null) Slots[i].DrinkId = null;
            Slots[i].Stock = Math.Clamp(Slots[i].Stock, 0, SlotCapacity);
        }

        // Trailing partial row from a corrupted save.
        while (Slots.Count % Balance.Columns != 0)
            Slots.RemoveAt(Slots.Count - 1);

        if (Slots.Count == 0)
        {
            EnsureRow(0);
            Slots[0].Unlocked = true;
        }

        EnsureRow(Balance.DefaultRows - 1);
        EnsureSpareRow();
        Money = Math.Max(0.0, Money);
        TotalEarned = Math.Max(Money, TotalEarned);

        // ---- v1 -> v2 migration: crate fields ----------------------------
        // A v1 save simply has none of these; defaulting to empty IS the
        // migration. The rest is defence against a hand-edited file.
        DrinkCopies ??= new Dictionary<string, int>();

        foreach (var key in DrinkCopies.Keys.ToList())
        {
            var drink = DrinkDatabase.Get(key);
            if (drink is null || drink.Source != DrinkSource.Pack)
                DrinkCopies.Remove(key);
            else if (DrinkCopies[key] < 0)
                DrinkCopies[key] = 0;
        }

        Tokens = Math.Max(0.0, Tokens);
        PacksOpened = Math.Max(0, PacksOpened);

        // A pending reveal must be a real pack drink or it is dropped -- a
        // stuck invalid pending id would deadlock the crate forever.
        if (PendingRevealId is not null &&
            DrinkDatabase.Get(PendingRevealId)?.Source != DrinkSource.Pack)
            PendingRevealId = null;

        // A pack drink loaded in a slot without an owned copy (hand-edited
        // save) would be an un-earned unlock; clear it.
        foreach (var slot in Slots)
        {
            var drink = slot.Drink;
            if (drink is { Source: DrinkSource.Pack } && CopiesOf(drink.Id) < 1)
            {
                slot.DrinkId = null;
                slot.Stock = 0;
            }
        }

        Version = CurrentVersion;
    }
}
