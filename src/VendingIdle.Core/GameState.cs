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
    /// slot index. <see cref="Migrate"/> re-lays version 1 saves.
    /// </summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public double Money { get; set; }
    public double TotalEarned { get; set; }

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

    [JsonIgnore] public double CritChance =>
        Modifiers.CritChance(UpgradeLevels[(int)UpgradeId.CritChance]);

    [JsonIgnore] public double CustomerInterval =>
        Modifiers.CustomerInterval(UpgradeLevels[(int)UpgradeId.CustomerSpeed]);

    [JsonIgnore] public double RestockDiscount =>
        Modifiers.RestockDiscount(UpgradeLevels[(int)UpgradeId.RestockDiscount]);

    [JsonIgnore] public double AutoRestockInterval =>
        Modifiers.AutoRestockInterval(UpgradeLevels[(int)UpgradeId.AutoRestockSpeed]);

    /// <summary>
    /// Bottles a single shake takes from each stocked slot. Reads from
    /// <see cref="Balance"/> today; this is the seam a drink effect or an upgrade
    /// hangs off when one wants to knock out more than one per slot.
    /// </summary>
    [JsonIgnore] public int ShakeBottlesPerSlot => Balance.ShakeBottlesPerSlot;

    [JsonIgnore] public int SlotsOwned => Slots.Count(s => s.Unlocked);

    [JsonIgnore] public int AutoRestockersOwned => Slots.Count(s => s.HasAutoRestocker);

    /// <summary>Number of allocated rows (always one spare row above the top unlocked one).</summary>
    [JsonIgnore] public int RowCount => Slots.Count / Balance.Columns;

    [JsonIgnore] public double NextSlotCost =>
        Balance.Cost(Balance.SlotBaseCost, Balance.SlotCostGrowth, SlotsOwned);

    [JsonIgnore] public double NextAutoRestockerCost =>
        Balance.Cost(Balance.AutoRestockerBaseCost, Balance.AutoRestockerCostGrowth, AutoRestockersOwned);

    [JsonIgnore] public int TotalStock => Slots.Sum(s => s.Stock);

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
        if (Version >= CurrentVersion)
        {
            Version = CurrentVersion;
            return;
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
        return drink.RestockCost(slot.Stock, units, SlotCapacity) * RestockDiscount;
    }

    /// <summary>Price of the next single can for this slot, discount included.</summary>
    public double UnitCost(Slot slot)
    {
        var drink = slot.Drink;
        return drink is null ? 0.0 : drink.UnitCostAt(slot.Stock, SlotCapacity) * RestockDiscount;
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
            var unitCost = drink.UnitCostAt(slot.Stock, SlotCapacity) * RestockDiscount;
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
    }
}
