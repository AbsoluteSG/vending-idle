using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using VendingIdle.Core;

namespace VendingIdle.SimTest;

/// <summary>
/// Headless checks on the economy. VendingIdle.Core has no MonoGame reference
/// precisely so this can run anywhere -- no window, no GPU, no display.
/// </summary>
public static class Program
{
    private static int _passed;
    private static int _failed;


    public static int Main(string[] args)
    {
        if (args.Contains("--curve"))
        {
            PrintProgressionCurve();
            return 0;
        }

        Console.WriteLine("Vending Idle -- simulation checks\n");

        NewGameStartsWithOneSlot();
        EmptyMachinePaysSpareChange();
        ClickSellsStockAtDrinkValue();
        DispenseRotatesBetweenSlots();
        ShakeEmptiesEverySlotAtOnce();
        ShakeYieldPerSlotIsAdjustable();
        ShakeOfADryMachinePaysSpareChange();
        MoneyFormatsCentsThenScientific();
        SwappingDrinkEmptiesTheSlot();
        RestockCostMatchesClosedForm();
        RestockNeverOverdraws();
        CustomersConsumeRealStock();
        CustomersStarveWithoutStock();
        AutoRestockerRefills();
        SlotPurchaseGatesOnRowBelow();
        OfflineMatchesLiveStepping();
        SaveRoundTripsExactly();
        CostCurvesAreMonotonic();
        CorruptSaveDoesNotThrow();
        OldSaveMigratesToTheNarrowerGrid();
        TheOpeningIsNotAGiveaway();
        TokensAccrueAndCratesOpen();
        RevealMustBeRedeemed();
        DuplicatesRaiseLevelToCap();
        RarityWeightsHold();
        AurasRequireStock();
        AuraCapsHold();
        ChainCascadesAreBounded();
        ChainCombosStack();
        ChainSustainAndTokens();
        TwinTapAndSpark();
        OldSaveGrowsTheUpgradeArray();
        BottomlessPreservesStock();
        CourierRefillsDrySlots();
        OfflineMatchesLiveWithEffects();
        ProgressionDoesNotStall();

        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------
    // Checks
    // ---------------------------------------------------------------------

    private static void NewGameStartsWithOneSlot()
    {
        var state = GameState.NewGame();

        Check("new game owns exactly one slot", state.SlotsOwned == 1);
        Check("the owned slot is bottom-left", state.Slots[0].Unlocked && state.Slots[0].Index == 0);
        Check("it starts empty of stock", state.TotalStock == 0);
        Check("the cabinet ships with its full height", state.RowCount == Balance.DefaultRows);
        Check("the shipped grid is 3 wide", Balance.Columns == 3);
        Check("a spare row exists above the top unlocked one",
              state.RowCount > state.Slots.Where(s => s.Unlocked).Max(s => s.Row) + 1);
    }

    private static void EmptyMachinePaysSpareChange()
    {
        var state = GameState.NewGame();
        var rng = new Random(1);

        var result = Simulation.Click(state, rng);

        Check("empty machine pays spare change", result.SpareChange);
        Check("spare change dispenses no cans", result.Cans == 0);
        Check("spare change is still money", state.Money > 0.0);
    }

    private static void ClickSellsStockAtDrinkValue()
    {
        var state = GameState.NewGame();
        var rng = new Random(2);
        var slot = state.Slots[0];

        state.Money = 1000.0;
        state.Restock(slot, 5);
        Check("restock adds stock", slot.Stock == 5);

        var moneyBefore = state.Money;
        var result = Simulation.Click(state, rng);

        Check("click dispenses a real can", !result.SpareChange && result.Cans >= 1);
        Check("stock is consumed", slot.Stock == 5 - result.Cans);
        Check("payout lands in the till",
              Math.Abs(state.Money - (moneyBefore + result.Payout)) < 1e-9);
    }

    private static void DispenseRotatesBetweenSlots()
    {
        var state = GameState.NewGame();
        var rng = new Random(3);

        state.Money = 100_000.0;
        state.TryBuySlot(1);
        state.TryAssignDrink(1, DrinkDatabase.All[0].Id);
        state.RestockToFull(state.Slots[0]);
        state.RestockToFull(state.Slots[1]);

        var first = Simulation.Click(state, rng).SlotIndex;
        var second = Simulation.Click(state, rng).SlotIndex;

        Check("consecutive clicks rotate slots", first != second);
    }

    /// <summary>
    /// Three loaded slots, one shake: every one of them gives up a bottle. This is
    /// what separates a shake from a customer's single purchase.
    /// </summary>
    private static void ShakeEmptiesEverySlotAtOnce()
    {
        var state = GameState.NewGame();
        var rng = new Random(21);

        state.Money = 100_000.0;
        for (var i = 1; i <= 2; i++)
        {
            state.TryBuySlot(i);
            state.TryAssignDrink(i, DrinkDatabase.All[0].Id);
        }

        for (var i = 0; i <= 2; i++) state.RestockToFull(state.Slots[i]);

        // A fourth slot is bought but left empty -- it must not report a drop.
        state.TryBuySlot(3);

        var before = new[] { state.Slots[0].Stock, state.Slots[1].Stock, state.Slots[2].Stock };
        var result = Simulation.Shake(state, rng);

        Check("shake reports one drop per stocked slot", result.SlotsHit == 3);
        Check("shake skips slots with no stock",
              result.Drops.All(d => d.SlotIndex >= 0 && d.SlotIndex <= 2));
        Check("shake takes a bottle from every stocked slot",
              state.Slots[0].Stock < before[0] &&
              state.Slots[1].Stock < before[1] &&
              state.Slots[2].Stock < before[2]);
        Check("shake payout is the sum of its drops",
              Math.Abs(result.Payout - result.Drops.Sum(d => d.Payout)) < 1e-9);
        Check("a shake counts as a single click", state.TotalClicks == 1);
    }

    private static void ShakeYieldPerSlotIsAdjustable()
    {
        var state = GameState.NewGame();
        var rng = new Random(22);

        state.Money = 100_000.0;
        state.RestockToFull(state.Slots[0]);
        var before = state.Slots[0].Stock;

        // The per-slot yield is the seam a drink effect would drive.
        Simulation.Shake(state, rng, bottlesPerSlot: 3);

        Check("an effect can raise the per-slot shake yield",
              before - state.Slots[0].Stock >= 3);
        Check("default yield comes from Balance",
              state.ShakeBottlesPerSlot == Balance.ShakeBottlesPerSlot);
    }

    private static void ShakeOfADryMachinePaysSpareChange()
    {
        var state = GameState.NewGame();
        var rng = new Random(23);

        var result = Simulation.Shake(state, rng);

        Check("shaking a dry machine pays spare change", result.SpareChange);
        Check("a spare-change shake hits no slots", result.SlotsHit == 0);
        Check("a spare-change shake still reports a drop", result.Drops.Count == 1);
        Check("a spare-change shake is still money", state.Money > 0.0);
    }

    /// <summary>
    /// The till shows cents at every scale a player reads them at, and switches to
    /// scientific notation once the figures stop being countable.
    /// </summary>
    private static void MoneyFormatsCentsThenScientific()
    {
        Check("zero shows its cents", Money.Cash(0.0) == "$0.00");
        Check("sub-dollar amounts keep both digits", Money.Cash(0.1) == "$0.10");
        Check("cents survive into the hundreds", Money.Cash(942.5) == "$942.50");
        Check("thousands are grouped, not suffixed", Money.Cash(12_345.67) == "$12,345.67");
        Check("cents hold right up to the threshold", Money.Cash(999_999.99) == "$999,999.99");

        Check("a million switches to scientific", Money.Cash(1e6) == "$1.00e6");
        Check("scientific keeps three significant figures", Money.Cash(1_234_567.0) == "$1.23e6");
        Check("scientific scales past the old suffix table", Money.Cash(4.2e42) == "$4.20e42");
        Check("a rounded-up mantissa carries", Money.Cash(9.999e8) == "$1.00e9");
        Check("negatives keep their sign", Money.Cash(-12.5) == "-$12.50");
    }

    private static void SwappingDrinkEmptiesTheSlot()
    {
        var state = GameState.NewGame();
        state.Money = 100_000.0;
        state.TotalEarned = 100_000.0;      // unlocks the mid-tier drinks
        state.RestockToFull(state.Slots[0]);

        Check("slot has stock before the swap", state.Slots[0].Stock > 0);
        state.TryAssignDrink(0, "orange_blast");
        Check("swapping drink clears the shelf", state.Slots[0].Stock == 0);
    }

    private static void RestockCostMatchesClosedForm()
    {
        // Midnight Brew has the steepest RestockGrowth, so it is the strictest
        // test that the geometric-series shortcut agrees with unit-by-unit pricing.
        var drink = DrinkDatabase.Get("midnight_brew")!;

        const int capacity = 25;
        var iterative = 0.0;
        for (var i = 0; i < capacity; i++) iterative += drink.UnitCostAt(i, capacity);

        var closedForm = drink.RestockCost(0, capacity, capacity);

        Check("closed-form restock cost matches unit-by-unit",
              Math.Abs(iterative - closedForm) < 1e-6);

        // And the flat-growth branch must not divide by zero.
        var flat = DrinkDatabase.Get("fizzy_water")!;
        Check("flat-growth drinks price linearly",
              Math.Abs(flat.RestockCost(0, 10, 10) - flat.RestockUnitCost * 10) < 1e-9);
    }

    private static void RestockNeverOverdraws()
    {
        var state = GameState.NewGame();
        var slot = state.Slots[0];

        state.Money = 1.0;                  // enough for 2 cans of Fizzy Water at 0.34
        var bought = state.Restock(slot, 100);

        Check("restock stops when money runs out", bought is > 0 and < 100);
        Check("restock never overdraws the till", state.Money >= 0.0);
        Check("stock matches what was paid for", slot.Stock == bought);

        var full = GameState.NewGame();
        full.Money = 1_000_000.0;
        full.RestockToFull(full.Slots[0]);
        Check("restock respects slot capacity", full.Slots[0].Stock == full.SlotCapacity);
    }

    private static void CustomersConsumeRealStock()
    {
        var state = GameState.NewGame();
        var rng = new Random(4);

        state.Money = 100_000.0;
        state.UpgradeLevels[(int)UpgradeId.Customers] = 4;
        state.RestockToFull(state.Slots[0]);

        var stockBefore = state.Slots[0].Stock;
        var moneyBefore = state.Money;

        StepFor(state, 30.0, rng);

        Check("customers drain stock", state.Slots[0].Stock < stockBefore);
        Check("customers earn money", state.Money > moneyBefore);
    }

    private static void CustomersStarveWithoutStock()
    {
        // The core tension: idle income is gated on stock, not decoupled from it.
        var stocked = GameState.NewGame();
        stocked.Money = 100_000.0;
        stocked.UpgradeLevels[(int)UpgradeId.Customers] = 4;
        stocked.RestockToFull(stocked.Slots[0]);
        var stockedStart = stocked.Money;
        StepFor(stocked, 20.0, new Random(5));
        var stockedEarned = stocked.Money - stockedStart;

        var dry = GameState.NewGame();
        dry.Money = 100_000.0;
        dry.UpgradeLevels[(int)UpgradeId.Customers] = 4;
        var dryStart = dry.Money;
        StepFor(dry, 20.0, new Random(5));
        var dryEarned = dry.Money - dryStart;

        Check("a dry machine earns far less than a stocked one", dryEarned < stockedEarned * 0.5);
        Check("a dry machine still trickles spare change", dryEarned > 0.0);
    }

    private static void AutoRestockerRefills()
    {
        var state = GameState.NewGame();
        var rng = new Random(6);

        state.Money = 100_000.0;
        state.Slots[0].HasAutoRestocker = true;

        Check("auto-restocker starts from an empty slot", state.Slots[0].Stock == 0);

        StepFor(state, 30.0, rng);

        Check("auto-restocker fills the slot unattended", state.Slots[0].Stock > 0);

        // With no money it must not conjure stock from nowhere.
        var broke = GameState.NewGame();
        broke.Money = 0.0;
        broke.Slots[0].HasAutoRestocker = true;
        StepFor(broke, 30.0, new Random(7));
        Check("auto-restocker cannot restock for free", broke.Slots[0].Stock == 0);
    }

    private static void SlotPurchaseGatesOnRowBelow()
    {
        var state = GameState.NewGame();
        state.Money = 10_000_000.0;

        var rowOneSlot = state.Slots[Balance.Columns];       // directly above the starter
        Check("row above an owned slot is purchasable", state.IsSlotPurchasable(rowOneSlot));

        state.EnsureRow(2);
        var rowTwoSlot = state.Slots[Balance.Columns * 2];
        Check("row two is gated until row one is owned", !state.IsSlotPurchasable(rowTwoSlot));

        state.TryBuySlot(rowOneSlot.Index);
        Check("buying row one opens row two", state.IsSlotPurchasable(state.Slots[Balance.Columns * 2]));
        Check("a spare row is always allocated on top", state.RowCount >= 3);

        var owned = state.SlotsOwned;
        var costBefore = state.NextSlotCost;
        state.TryBuySlot(state.Slots[Balance.Columns * 2].Index);
        Check("slot count grows with purchases", state.SlotsOwned == owned + 1);
        Check("slots get more expensive", state.NextSlotCost > costBefore);
    }

    private static void OfflineMatchesLiveStepping()
    {
        GameState Build()
        {
            var s = GameState.NewGame();
            s.Money = 500_000.0;
            s.UpgradeLevels[(int)UpgradeId.Customers] = 6;
            s.Slots[0].HasAutoRestocker = true;
            s.RestockToFull(s.Slots[0]);
            return s;
        }

        var live = Build();
        var liveStart = live.Money;
        StepFor(live, 3600.0, new Random(8));
        var liveEarned = live.Money - liveStart;

        var offline = Build();
        var offlineStart = offline.Money;
        var report = Simulation.RunOffline(offline, 3600.0, new Random(8));

        Check("offline earns something", report.Earned > 0.0);

        // Different step granularity (1 s vs 20 Hz) means these cannot match
        // exactly; they must agree on magnitude.
        var ratio = report.Earned / liveEarned;
        Check($"offline is within 20% of live stepping (ratio {ratio:0.###})",
              ratio is > 0.8 and < 1.2);

        Check("offline report matches the money actually banked",
              Math.Abs(report.Earned - (offline.Money - offlineStart)) < 1e-6);

        var capped = Build();
        var cappedReport = Simulation.RunOffline(capped, Balance.OfflineMaxSeconds * 3, new Random(9));
        Check("offline earnings are capped at 8 hours",
              cappedReport.Capped && Math.Abs(cappedReport.Seconds - Balance.OfflineMaxSeconds) < 1e-6);
    }

    private static void SaveRoundTripsExactly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vending-idle-test-{Guid.NewGuid():N}.json");

        try
        {
            var original = GameState.NewGame();
            original.Money = 12_345.678;
            original.TotalEarned = 98_765.4321;
            original.TotalCansSold = 4242;
            original.TotalCrits = 17;
            original.UpgradeLevels[(int)UpgradeId.ClickValue] = 9;
            original.UpgradeLevels[(int)UpgradeId.Customers] = 3;
            original.TryBuySlot(1);
            original.RestockToFull(original.Slots[0]);
            original.Slots[0].HasAutoRestocker = true;
            original.DispenseCursor = 1;
            original.Tokens = 777;
            original.PacksOpened = 4;
            original.DrinkCopies["chain_fizz"] = 3;
            original.PendingRevealId = "static_shock";

            SaveSystem.Save(original, path);
            var loaded = SaveSystem.Load(path);

            Check("save round-trips", loaded is not null);
            if (loaded is null) return;

            Check("money survives", Math.Abs(loaded.Money - original.Money) < 1e-9);
            Check("lifetime earnings survive", Math.Abs(loaded.TotalEarned - original.TotalEarned) < 1e-9);
            Check("upgrade levels survive", loaded.UpgradeLevels.SequenceEqual(original.UpgradeLevels));
            Check("slot count survives", loaded.Slots.Count == original.Slots.Count);
            Check("stock survives", loaded.Slots[0].Stock == original.Slots[0].Stock);
            Check("automation survives", loaded.Slots[0].HasAutoRestocker);
            Check("purchased slots survive", loaded.Slots[1].Unlocked);
            Check("dispense cursor survives", loaded.DispenseCursor == original.DispenseCursor);
            Check("stats survive", loaded.TotalCansSold == 4242 && loaded.TotalCrits == 17);
            Check("tokens survive", loaded.Tokens == 777 && loaded.PacksOpened == 4);
            Check("drink copies survive", loaded.CopiesOf("chain_fizz") == 3);
            Check("a pending reveal survives", loaded.PendingRevealId == "static_shock");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CostCurvesAreMonotonic()
    {
        foreach (var def in UpgradeDatabase.All)
        {
            var ok = true;
            for (var level = 0; level < 50; level++)
                if (def.CostAt(level + 1) <= def.CostAt(level))
                    ok = false;

            Check($"{def.Name} price rises every level", ok);
        }

        var slotOk = true;
        for (var owned = 0; owned < 50; owned++)
        {
            var a = Balance.Cost(Balance.SlotBaseCost, Balance.SlotCostGrowth, owned);
            var b = Balance.Cost(Balance.SlotBaseCost, Balance.SlotCostGrowth, owned + 1);
            if (b <= a || double.IsInfinity(b)) slotOk = false;
        }

        Check("slot price rises and stays finite for 50 slots", slotOk);
    }

    private static void CorruptSaveDoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vending-idle-bad-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, "{ this is not valid json ");
            var loaded = SaveSystem.Load(path);
            Check("a corrupt save loads as null rather than crashing", loaded is null);

            File.WriteAllText(path, "{\"Money\": -50, \"Slots\": [], \"UpgradeLevels\": [1,2]}");
            var repaired = SaveSystem.Load(path);
            Check("a truncated save is repaired", repaired is not null);
            if (repaired is not null)
            {
                Check("negative money is clamped", repaired.Money >= 0.0);
                Check("upgrade array is resized",
                      repaired.UpgradeLevels.Length == UpgradeDatabase.Count);
                Check("a starter slot is restored", repaired.SlotsOwned >= 1);
            }

            // A v1 save has no crate fields at all; defaulting to empty IS the migration.
            File.WriteAllText(path,
                "{\"Version\":1,\"Money\":500,\"TotalEarned\":500," +
                "\"Slots\":[{\"Index\":0,\"Unlocked\":true,\"DrinkId\":\"fizzy_water\",\"Stock\":5}]," +
                "\"UpgradeLevels\":[0,0,0,0,0,0,0]}");
            var v1 = SaveSystem.Load(path);
            Check("a v1 save loads", v1 is not null);
            if (v1 is not null)
            {
                Check("v1 migrates to empty crate state",
                      v1.Tokens == 0 && v1.PacksOpened == 0 &&
                      v1.DrinkCopies.Count == 0 && v1.PendingRevealId is null);
                Check("v1 save is stamped to the current version",
                      v1.Version == GameState.CurrentVersion);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// <summary>
    /// A version 1 save was laid out on a 4-wide grid. Every stored slot index
    /// means something different now, so the migration has to re-lay them rather
    /// than trust what is on disk.
    /// </summary>
    private static void OldSaveMigratesToTheNarrowerGrid()
    {
        // Hand-built in the old layout: four unlocked slots, one of them stranded
        // on the second row, exactly as the 4-wide grid would have written them.
        var old = new GameState { Version = 1, Money = 500.0, TotalEarned = 5_000.0 };
        old.Slots.Clear();
        for (var i = 0; i < 8; i++) old.Slots.Add(new Slot { Index = i });

        old.Slots[0].Unlocked = true; old.Slots[0].DrinkId = "cola_classic"; old.Slots[0].Stock = 7;
        old.Slots[1].Unlocked = true; old.Slots[1].DrinkId = "fizzy_water"; old.Slots[1].Stock = 3;
        old.Slots[3].Unlocked = true; old.Slots[3].DrinkId = "cola_classic"; old.Slots[3].Stock = 5;
        old.Slots[4].Unlocked = true; old.Slots[4].HasAutoRestocker = true;

        old.Migrate();
        old.Normalize();

        Check("migration stamps the current version", old.Version == GameState.CurrentVersion);
        Check("migration keeps every unlocked slot", old.SlotsOwned == 4);
        Check("migration packs them into the bottom of the grid",
              old.Slots.Take(4).All(s => s.Unlocked));
        Check("migration carries drinks across", old.Slots[0].DrinkId == "cola_classic" &&
                                                 old.Slots[1].DrinkId == "fizzy_water");
        Check("migration carries stock across", old.Slots[0].Stock == 7 && old.Slots[2].Stock == 5);
        Check("migration carries automation across", old.Slots[3].HasAutoRestocker);
        Check("migration leaves no slot stranded above a gap",
              old.Slots.Where(s => s.Unlocked).All(s => s.Row == 0 || old.IsRowOccupied(s.Row - 1)));
        Check("migrating an already-current save is a no-op",
              RoundTripsUnchanged(GameState.NewGame()));
    }

    private static bool RoundTripsUnchanged(GameState state)
    {
        var before = state.SlotsOwned;
        state.Migrate();
        return state.Version == GameState.CurrentVersion && state.SlotsOwned == before;
    }

    /// <summary>
    /// The first five minutes. This is where the previous pass fell over hardest --
    /// a greedy player had 13 slots and four of six drinks before the kettle
    /// boiled, which left nothing to want.
    /// </summary>
    private static void TheOpeningIsNotAGiveaway()
    {
        var state = GameState.NewGame();
        var rng = new Random(12);

        var clickCarry = 0.0;
        for (var second = 0.0; second < 5 * 60; second += 1.0)
        {
            clickCarry += 3.0;
            while (clickCarry >= 1.0)
            {
                Simulation.Shake(state, rng);
                clickCarry -= 1.0;
            }

            Simulation.Step(state, 1.0, rng);
            BuyGreedily(state, rng);
            state.RestockAll();
        }

        Console.WriteLine($"        [5 min greedy opening: {Money.Cash(state.TotalEarned)} earned, " +
                          $"{state.SlotsOwned} slots, {state.Customers} customers, " +
                          $"{DrinkDatabase.UnlockedFor(state).Count()} drinks]");

        Check("five minutes buys a second slot", state.SlotsOwned >= 2);
        Check("five minutes is not a fortune", state.TotalEarned < 10_000);
        Check($"five minutes does not fill the machine ({state.SlotsOwned} slots)",
              state.SlotsOwned <= 5);
        // Counts the *purchase* ladder only. Pack drinks are priced below their
        // value tier and bought with tokens, so they are a separate pacing axis
        // and folding them in here would make this guard fire on crate luck.
        Check("five minutes does not hand out the roster", PurchaseDrinksUnlocked(state) <= 2);
        Check("five minutes opens no crates", state.PacksOpened == 0);
    }

    /// <summary>
    /// Plays the game with a crude greedy policy and checks the curve actually
    /// opens up. This is the check that catches a balance change turning the
    /// opening into a wall -- the unit checks above would all still pass.
    /// </summary>
    private static void ProgressionDoesNotStall()
    {
        var state = GameState.NewGame();
        var rng = new Random(11);

        const double sessionSeconds = 30 * 60;
        var clickCarry = 0.0;

        for (var second = 0.0; second < sessionSeconds; second += 1.0)
        {
            // The player shakes ~3x a second for the first five minutes, then idles.
            if (second < 300)
            {
                clickCarry += 3.0;
                while (clickCarry >= 1.0)
                {
                    Simulation.Shake(state, rng);
                    clickCarry -= 1.0;
                }
            }

            Simulation.Step(state, 1.0, rng);

            BuyGreedily(state, rng);
            state.RestockAll();
        }

        Console.WriteLine($"        [30 min greedy session: {Money.Cash(state.TotalEarned)} earned, " +
                          $"{state.SlotsOwned} slots, {state.Customers} customers, " +
                          $"{DrinkDatabase.UnlockedFor(state).Count()} drinks]");

        Check("30 minutes of play expands the machine", state.SlotsOwned >= 4);
        Check("30 minutes of play hires customers", state.Customers >= 2);
        Check("30 minutes of play unlocks a second drink",
              DrinkDatabase.UnlockedFor(state).Count() >= 2);
        Check("30 minutes of play buys automation", state.AutoRestockersOwned >= 1);

        // Upper bounds matter as much as lower ones, and these are deliberately
        // snug rather than nominal. The pass before this one gave a greedy player
        // $6.1M, 21 slots and five of six drinks inside half an hour -- every
        // check below passed on that curve, because they were loose enough to wave
        // a giveaway through. Numbers here track the intended curve within about
        // an order of magnitude, so a change that makes the opening cheap again
        // fails loudly instead of quietly.
        Check($"30 minutes does not hyperinflate ({Money.Cash(state.TotalEarned)})",
              state.TotalEarned < 1e5);
        Check($"30 minutes does not hand over the machine ({state.SlotsOwned} slots)",
              state.SlotsOwned <= 9);
        Check("30 minutes is still on the starter drinks", PurchaseDrinksUnlocked(state) <= 2);

        // The crate track should have opened by now, but only just: a half hour
        // of greedy play is meant to buy a taste of the pack roster, not the
        // combo pieces that make cascades run.
        Check($"30 minutes opens a crate or two ({state.PacksOpened})",
              state.PacksOpened is >= 1 and <= 3);
        Check("30 minutes leaves something left to chase",
              DrinkDatabase.UnlockedFor(state).Count() < DrinkDatabase.All.Count);

        // And it must keep opening up rather than plateauing.
        var earnedAtHalfHour = state.TotalEarned;
        for (var second = 0.0; second < 30 * 60; second += 1.0)
        {
            Simulation.Step(state, 1.0, rng);
            BuyGreedily(state, rng);
            state.RestockAll();
        }

        Check("a second idle half-hour keeps earning",
              state.TotalEarned > earnedAtHalfHour * 1.5);
    }

    /// <summary>
    /// Balance instrument, not a check: run with --curve to see where the greedy
    /// player lands over a full day. Use it when re-tuning Balance.cs.
    /// </summary>
    private static void PrintProgressionCurve()
    {
        var state = GameState.NewGame();
        var rng = new Random(11);

        Console.WriteLine("Vending Idle -- progression curve (greedy player, shakes for 5 min)\n");
        Console.WriteLine($"{"time",8} {"earned",14} {"cash",12} {"slots",6} {"cust",5} " +
                          $"{"drinks",7} {"income/s",11} {"stock",7} {"tokens",8} {"crates",7} {"owned",6}");

        var clickCarry = 0.0;
        var checkpoints = new[] { 60.0, 300.0, 900.0, 1800.0, 3600.0, 7200.0, 14400.0, 28800.0, 86400.0 };
        var next = 0;

        for (var second = 0.0; second <= 86400.0; second += 1.0)
        {
            if (second < 300)
            {
                clickCarry += 3.0;
                while (clickCarry >= 1.0)
                {
                    Simulation.Shake(state, rng);
                    clickCarry -= 1.0;
                }
            }

            Simulation.Step(state, 1.0, rng);
            BuyGreedily(state, rng);
            state.RestockAll();

            if (next < checkpoints.Length && second >= checkpoints[next])
            {
                Console.WriteLine(
                    $"{Money.FormatDuration(second),8} {Money.Cash(state.TotalEarned),14} " +
                    $"{Money.Cash(state.Money),12} {state.SlotsOwned,6} {state.Customers,5} " +
                    $"{DrinkDatabase.UnlockedFor(state).Count(),7} " +
                    $"{Money.FormatRate(Simulation.PotentialIncomePerSecond(state)),11} " +
                    $"{state.TotalStock,7} {state.Tokens,8} {state.PacksOpened,7} " +
                    $"{state.DrinkCopies.Count,6}");
                next++;
            }
        }
    }

    /// <summary>Unlocked drinks from the earnings ladder, ignoring anything crate-found.</summary>
    private static int PurchaseDrinksUnlocked(GameState state) =>
        DrinkDatabase.UnlockedFor(state).Count(d => d.Source == DrinkSource.Purchase);

    /// <summary>Spends spare money on whatever is affordable, cheapest thing first.</summary>
    private static void BuyGreedily(GameState state, Random rng)
    {
        // Keep a buffer so the policy never spends the money it needs for restocks.
        const double reserveFactor = 3.0;

        // Crates cost tokens, not money, so open and redeem whenever possible.
        if (state.CanOpenPack) state.TryOpenPack(rng);
        state.RedeemReveal();

        // Keep up to three slots running distinct effect drinks, so the curve
        // report exercises auras and procs rather than ignoring them.
        var packSlots = state.Slots.Count(s2 =>
            s2.Unlocked && s2.Drink is { Source: DrinkSource.Pack });

        if (packSlots < 3)
        {
            foreach (var drink in DrinkDatabase.PackDrinks)
            {
                if (packSlots >= 3) break;
                if (state.CopiesOf(drink.Id) < 1) continue;
                if (state.Slots.Any(s2 => s2.DrinkId == drink.Id)) continue;

                var bare = state.Slots.FirstOrDefault(s2 => s2.Unlocked && s2.DrinkId is null);
                if (bare is null) break;

                if (state.TryAssignDrink(bare.Index, drink.Id)) packSlots++;
            }
        }

        foreach (var slot in state.Slots)
        {
            if (!slot.Unlocked || slot.DrinkId is not null) continue;

            // Load the most valuable unlocked drink into any bare slot.
            DrinkDef? best = null;
            foreach (var drink in DrinkDatabase.UnlockedFor(state))
                if (best is null || drink.Value > best.Value)
                    best = drink;

            if (best is not null) state.TryAssignDrink(slot.Index, best.Id);
        }

        if (state.Money > state.NextSlotCost * reserveFactor)
            foreach (var slot in state.Slots)
                if (state.IsSlotPurchasable(slot) && state.TryBuySlot(slot.Index))
                    break;

        if (state.Money > state.NextAutoRestockerCost * reserveFactor)
            foreach (var slot in state.Slots)
                if (slot.Unlocked && !slot.HasAutoRestocker &&
                    state.TryBuyAutoRestocker(slot.Index))
                    break;

        var cheapest = UpgradeDatabase.All
            .Where(d => !d.IsMaxed(state.UpgradeLevel(d.Id)))
            .OrderBy(d => d.CostAt(state.UpgradeLevel(d.Id)))
            .FirstOrDefault();

        if (cheapest is not null &&
            state.Money > cheapest.CostAt(state.UpgradeLevel(cheapest.Id)) * reserveFactor)
            state.TryBuyUpgrade(cheapest.Id);
    }

    // ---------------------------------------------------------------------
    // Supply crates and effect drinks
    // ---------------------------------------------------------------------

    private static void TokensAccrueAndCratesOpen()
    {
        var state = GameState.NewGame();
        var rng = new Random(21);

        state.Money = 10_000.0;
        state.RestockToFull(state.Slots[0]);

        var sold = 0L;
        for (var i = 0; i < 8; i++)
        {
            var r = Simulation.Click(state, rng);
            sold += r.Cans;
        }

        Check("bottles sold earn crate tokens", state.Tokens >= sold);

        // Derived from the price rather than a round number: the crate curve is
        // tuned against the shake economy and moves when that is rebalanced.
        var cost = state.NextPackCost;
        var granted = cost * 2.0;
        state.Tokens = granted;

        var rolled = state.TryOpenPack(rng);

        Check("opening a crate rolls a pack drink",
              rolled is not null && DrinkDatabase.Get(rolled)?.Source == DrinkSource.Pack);
        Check("opening deducts the token price", Math.Abs(state.Tokens - (granted - cost)) < 1e-9);
        Check("crate price rises after opening", state.NextPackCost > cost);
    }

    private static void RevealMustBeRedeemed()
    {
        var state = GameState.NewGame();
        var rng = new Random(22);

        state.Tokens = 100_000;
        var rolled = state.TryOpenPack(rng)!;

        Check("the rolled drink is pending, not granted",
              state.PendingRevealId == rolled && state.CopiesOf(rolled) == 0);
        Check("a pending reveal blocks the crate",
              !state.CanOpenPack && state.TryOpenPack(rng) is null);
        Check("a pending pack drink is still locked",
              !DrinkDatabase.IsUnlocked(DrinkDatabase.Get(rolled)!, state));

        var redeem = state.RedeemReveal();

        Check("redeeming grants the copy",
              redeem is not null && redeem.WasNew && state.CopiesOf(rolled) == 1);
        Check("redeeming unblocks the crate",
              state.PendingRevealId is null && state.CanOpenPack);
        Check("redeeming twice does nothing", state.RedeemReveal() is null);
        Check("an owned pack drink is unlocked",
              DrinkDatabase.IsUnlocked(DrinkDatabase.Get(rolled)!, state));
    }

    private static void DuplicatesRaiseLevelToCap()
    {
        var state = GameState.NewGame();
        var ok = true;

        for (var copy = 1; copy <= Balance.EffectLevelMax + 3; copy++)
        {
            state.PendingRevealId = "chain_fizz";
            var redeem = state.RedeemReveal()!;

            var expectedLevel = Math.Min(copy, Balance.EffectLevelMax);
            if (redeem.Level != expectedLevel) ok = false;
            if (redeem.WasNew != (copy == 1)) ok = false;
            if (redeem.AtMax != (copy > Balance.EffectLevelMax)) ok = false;
        }

        Check("duplicates raise the effect level and stop at the cap", ok);
        Check("effect level reads back capped",
              state.EffectLevelOf(DrinkDatabase.Get("chain_fizz")!) == Balance.EffectLevelMax);
    }

    private static void RarityWeightsHold()
    {
        var rng = new Random(23);
        var byRarity = new Dictionary<Rarity, int>();

        const int rolls = 20_000;
        for (var i = 0; i < rolls; i++)
        {
            var drink = PackSystem.Roll(rng);
            byRarity[drink.Rarity] = byRarity.GetValueOrDefault(drink.Rarity) + 1;
        }

        double Share(Rarity r) => byRarity.GetValueOrDefault(r) / (double)rolls;
        double Expected(Rarity r) =>
            DrinkDatabase.PackDrinks.Where(d => d.Rarity == r).Sum(d => PackSystem.Weight(d.Rarity))
            / (double)PackSystem.TotalWeight;

        var ok = new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare }
            .All(r => Math.Abs(Share(r) - Expected(r)) < 0.03);

        Check("rarity weights hold over 20k rolls", ok);
    }

    /// <summary>Grants copies and loads the drink into the given slot, stocked.</summary>
    private static void LoadPackDrink(GameState state, int slotIndex, string drinkId, int copies)
    {
        state.DrinkCopies[drinkId] = copies;
        state.EnsureRow(slotIndex / Balance.Columns);
        state.Slots[slotIndex].Unlocked = true;
        state.TryAssignDrink(slotIndex, drinkId);
        state.RestockToFull(state.Slots[slotIndex]);
    }

    private static void AurasRequireStock()
    {
        var state = GameState.NewGame();
        state.Money = 1_000_000.0;

        var baseCrit = state.CritChance;
        LoadPackDrink(state, 1, "static_shock", 3);

        Check("a stocked aura slot raises crit chance", state.CritChance > baseCrit);

        state.Slots[1].Stock = 0;
        Check("a dry aura slot contributes nothing",
              Math.Abs(state.CritChance - baseCrit) < 1e-12);

        // Loading the same aura drink twice must not stack it.
        state.RestockToFull(state.Slots[1]);
        var single = state.CritChance;
        LoadPackDrink(state, 2, "static_shock", 3);
        Check("duplicate slots of one aura drink do not stack",
              Math.Abs(state.CritChance - single) < 1e-12);
    }

    private static void AuraCapsHold()
    {
        var state = GameState.NewGame();
        state.Money = 100_000_000.0;

        state.UpgradeLevels[(int)UpgradeId.CritChance] = 29;        // maxed
        state.UpgradeLevels[(int)UpgradeId.CustomerSpeed] = 20;     // maxed
        state.UpgradeLevels[(int)UpgradeId.RestockDiscount] = 34;   // maxed

        LoadPackDrink(state, 1, "static_shock", Balance.EffectLevelMax);
        LoadPackDrink(state, 2, "loyalty_lager", Balance.EffectLevelMax);
        LoadPackDrink(state, 3, "bulk_bottle", Balance.EffectLevelMax);

        Check("crit chance never passes its cap",
              state.CritChance <= Balance.CritChanceMax + 1e-12);
        Check("customer interval never passes its floor",
              state.CustomerInterval >= Balance.CustomerIntervalMin - 1e-12);
        Check("restock discount never passes its floor",
              state.RestockDiscount >= Balance.RestockDiscountMin - 1e-12);
    }

    /// <summary>
    /// Cascades are the design pillar, so the guarantee that matters is not
    /// "never deeper than one" any more -- it is that a cascade is bounded by
    /// its hop ceiling and can never touch the same slot twice, whatever the
    /// probabilities do.
    /// </summary>
    private static void ChainCascadesAreBounded()
    {
        var state = GameState.NewGame();
        var rng = new Random(24);
        state.Money = 100_000_000.0;

        // Nine slots of Chain Fizz at max level, and the chain chance pinned as
        // high as it goes: the worst case for a runaway cascade.
        for (var i = 0; i < 9; i++)
            LoadPackDrink(state, i, "chain_fizz", Balance.EffectLevelMax);

        state.UpgradeLevels[(int)UpgradeId.ChainChance] = 40;
        state.UpgradeLevels[(int)UpgradeId.ChainHops] = 3;

        var ceiling = state.MaxChainHops;
        var withinCeiling = true;
        var noRepeats = true;
        var longest = 0;

        for (var i = 0; i < 4_000; i++)
        {
            for (var slot = 0; slot < 9; slot++)
                state.Slots[slot].Stock = state.SlotCapacity;

            var result = Simulation.Click(state, rng);
            var chain = result.Chain;
            if (chain is null) continue;

            longest = Math.Max(longest, chain.Count);
            if (chain.Count > ceiling) withinCeiling = false;

            // The origin plus every hop must be nine distinct slots at most.
            var seen = new HashSet<int> { result.SlotIndex };
            foreach (var hop in chain)
                if (!seen.Add(hop.SlotIndex)) noRepeats = false;
        }

        Check("cascades actually run past a single hop", longest >= 2);
        Check($"a cascade never exceeds its hop ceiling ({longest} <= {ceiling})", withinCeiling);
        Check("a cascade never vends the same slot twice", noRepeats);
    }

    /// <summary>
    /// The combo pieces only pay off on top of a cascade, so each is measured
    /// against the same cascade running without it.
    /// </summary>
    private static void ChainCombosStack()
    {
        // Relay Rum buys hops, which is the whole enabler.
        var plain = GameState.NewGame();
        plain.Money = 100_000_000.0;
        LoadPackDrink(plain, 0, "chain_fizz", Balance.EffectLevelMax);
        var baseHops = plain.MaxChainHops;

        var extended = GameState.NewGame();
        extended.Money = 100_000_000.0;
        LoadPackDrink(extended, 0, "chain_fizz", Balance.EffectLevelMax);
        LoadPackDrink(extended, 1, "relay_rum", Balance.EffectLevelMax);

        Check("Relay Rum lengthens cascades", extended.MaxChainHops > baseHops);

        // ...and stops the moment its slot runs dry, like every other aura.
        extended.Slots[1].Stock = 0;
        Check("a dry Relay Rum lends no hops", extended.MaxChainHops == baseHops);

        // Surge Syrup is the only way a hop can crit.
        var crit = GameState.NewGame();
        var rng = new Random(77);
        crit.Money = 100_000_000.0;
        for (var i = 0; i < 6; i++)
            LoadPackDrink(crit, i, "chain_fizz", Balance.EffectLevelMax);
        crit.UpgradeLevels[(int)UpgradeId.ChainChance] = 40;
        crit.UpgradeLevels[(int)UpgradeId.ChainHops] = 3;

        var hopsSeen = 0;
        var hopCrits = 0;

        for (var i = 0; i < 3_000; i++)
        {
            for (var slot = 0; slot < 6; slot++) crit.Slots[slot].Stock = crit.SlotCapacity;

            if (Simulation.Click(crit, rng).Chain is not { } chain) continue;
            foreach (var hop in chain)
            {
                hopsSeen++;
                if (hop.Crit) hopCrits++;
            }
        }

        Check("hops never crit without Surge Syrup", hopsSeen > 0 && hopCrits == 0);

        LoadPackDrink(crit, 6, "surge_syrup", Balance.EffectLevelMax);
        var withSyrup = 0;

        for (var i = 0; i < 3_000; i++)
        {
            for (var slot = 0; slot < 7; slot++) crit.Slots[slot].Stock = crit.SlotCapacity;

            if (Simulation.Click(crit, rng).Chain is not { } chain) continue;
            foreach (var hop in chain) if (hop.Crit) withSyrup++;
        }

        Check("Surge Syrup lets hops crit", withSyrup > 0);
    }

    /// <summary>Echo Elixir and Loyalty Lemon pay out on hops rather than on the primary.</summary>
    private static void ChainSustainAndTokens()
    {
        var state = GameState.NewGame();
        var rng = new Random(78);
        state.Money = 100_000_000.0;

        for (var i = 0; i < 5; i++)
            LoadPackDrink(state, i, "chain_fizz", Balance.EffectLevelMax);

        LoadPackDrink(state, 5, "echo_elixir", Balance.EffectLevelMax);
        LoadPackDrink(state, 6, "loyalty_lemon", Balance.EffectLevelMax);

        state.UpgradeLevels[(int)UpgradeId.ChainChance] = 40;
        state.UpgradeLevels[(int)UpgradeId.ChainHops] = 3;

        var preserved = 0;
        var hops = 0;

        // Checked every iteration, so it is collapsed to one assertion rather
        // than three thousand lines of report.
        var everyHopPaidBonus = true;

        for (var i = 0; i < 3_000; i++)
        {
            for (var slot = 0; slot < 7; slot++) state.Slots[slot].Stock = state.SlotCapacity;

            var tokensBefore = state.Tokens;
            if (Simulation.Click(state, rng).Chain is not { } chain) continue;

            hops += chain.Count;
            foreach (var hop in chain) if (hop.Preserved) preserved++;

            // Every hop must be worth strictly more than the plain rate, since
            // Loyalty Lemon is on the glass.
            if (chain.Count > 0 &&
                state.Tokens - tokensBefore <= chain.Count * state.TokensPerBottle)
                everyHopPaidBonus = false;
        }

        Check("Echo Elixir keeps stock on some hops", hops > 0 && preserved > 0);
        Check("Loyalty Lemon pays bonus tokens on every hop", hops > 0 && everyHopPaidBonus);
    }

    private static void TwinTapAndSpark()
    {
        // Twin Tap takes a second bottle from its own slot rather than chaining.
        var twin = GameState.NewGame();
        var rng = new Random(79);
        twin.Money = 100_000_000.0;
        LoadPackDrink(twin, 0, "twin_tap", Balance.EffectLevelMax);

        var doubles = 0;
        var neverChained = true;

        for (var i = 0; i < 1_500; i++)
        {
            twin.Slots[0].Stock = twin.SlotCapacity;
            var before = twin.Slots[0].Stock;
            var result = Simulation.Click(twin, rng);

            // Only one slot exists, so nothing can chain: every extra bottle is
            // Twin Tap or a crit, and both come out of this same slot.
            if (before - twin.Slots[0].Stock >= 2) doubles++;
            if (result.Chain is not null) neverChained = false;
        }

        Check("Twin Tap pulls a second bottle from its own slot", doubles > 0);
        Check("Twin Tap does not chain -- the bottle is its own slot's", neverChained);

        // Jumper Juice starts cascades from a drink with no chain of its own.
        var spark = GameState.NewGame();
        spark.Money = 100_000_000.0;
        LoadPackDrink(spark, 0, "jumper_juice", Balance.EffectLevelMax);

        // A plain drink to chain *into*: the point is that the spark comes from
        // Jumper Juice, not from anything the target does.
        spark.Slots[1].Unlocked = true;
        spark.TryAssignDrink(1, "fizzy_water");
        spark.RestockToFull(spark.Slots[1]);

        var sparked = false;
        for (var i = 0; i < 2_000; i++)
        {
            spark.Slots[0].Stock = spark.SlotCapacity;
            spark.Slots[1].Stock = spark.SlotCapacity;

            if (Simulation.Click(spark, rng).Chain is { Count: > 0 }) sparked = true;
        }

        Check("Jumper Juice starts chains of its own", sparked);
    }

    /// <summary>
    /// Adding an upgrade widens the saved array. A save written by yesterday's
    /// build must not throw the moment anything reads the new index.
    /// </summary>
    private static void OldSaveGrowsTheUpgradeArray()
    {
        var state = GameState.NewGame();
        state.UpgradeLevels = new int[3];
        state.UpgradeLevels[(int)UpgradeId.ClickValue] = 5;

        state.Migrate();

        Check("a short upgrade array is grown to fit",
              state.UpgradeLevels.Length == UpgradeDatabase.Count);
        Check("existing upgrade levels survive the widening",
              state.UpgradeLevels[(int)UpgradeId.ClickValue] == 5);
        Check("reading a brand-new upgrade is safe", state.MaxChainHops >= Balance.ChainHopsBase);
    }

    private static void BottomlessPreservesStock()
    {
        var state = GameState.NewGame();
        var rng = new Random(25);
        state.Money = 100_000_000.0;

        LoadPackDrink(state, 0, "bottomless_cup", Balance.EffectLevelMax);

        var sold = 0L;
        var consumed = 0;

        for (var i = 0; i < 600; i++)
        {
            state.Slots[0].Stock = state.SlotCapacity;
            var before = state.Slots[0].Stock;
            var result = Simulation.Click(state, rng);
            sold += result.Cans;
            consumed += before - state.Slots[0].Stock;

            if (result.Preserved && before != state.Slots[0].Stock)
                Check("a preserved dispense left the shelf untouched", false);
        }

        Check("Bottomless Cup sells more bottles than it consumes", consumed < sold);
    }

    private static void CourierRefillsDrySlots()
    {
        var state = GameState.NewGame();
        var rng = new Random(26);
        state.Money = 100_000_000.0;

        LoadPackDrink(state, 0, "courier_cola", Balance.EffectLevelMax);

        // A second loaded slot that is bone dry: the only legal courier target.
        state.Slots[1].Unlocked = true;
        state.TryAssignDrink(1, "fizzy_water");
        state.Slots[1].Stock = 0;

        var refills = 0;
        for (var i = 0; i < 1_500; i++)
        {
            state.Slots[0].Stock = state.SlotCapacity;
            state.Slots[1].Stock = 0;

            var result = Simulation.Click(state, rng);
            if (result.CourierSlotIndex == 1 && state.Slots[1].Stock == 1)
                refills++;
        }

        Check("Courier Cola drops free bottles into dry slots", refills > 0);

        // With nothing dry, the proc must find no target.
        state.RestockAll();
        var refillsWhenFull = 0;
        for (var i = 0; i < 500; i++)
        {
            state.Slots[0].Stock = state.SlotCapacity;
            state.Slots[1].Stock = state.SlotCapacity;
            if (Simulation.Click(state, rng).CourierSlotIndex >= 0) refillsWhenFull++;
        }

        Check("Courier Cola refuses slots that still have stock", refillsWhenFull == 0);
    }

    private static void OfflineMatchesLiveWithEffects()
    {
        GameState Build()
        {
            var s = GameState.NewGame();
            s.Money = 500_000.0;
            s.UpgradeLevels[(int)UpgradeId.Customers] = 6;

            LoadPackDrink(s, 1, "chain_fizz", Balance.EffectLevelMax);
            LoadPackDrink(s, 2, "bottomless_cup", Balance.EffectLevelMax);
            LoadPackDrink(s, 3, "static_shock", Balance.EffectLevelMax);
            s.RestockToFull(s.Slots[0]);

            foreach (var slot in s.Slots)
                if (slot.Unlocked)
                    slot.HasAutoRestocker = true;

            return s;
        }

        var live = Build();
        var liveStart = live.Money;
        StepFor(live, 3600.0, new Random(27));
        var liveEarned = live.Money - liveStart;

        var offline = Build();
        var report = Simulation.RunOffline(offline, 3600.0, new Random(27));

        var ratio = report.Earned / liveEarned;
        Check($"offline with a full effect loadout is within 20% of live (ratio {ratio:0.###})",
              ratio is > 0.8 and < 1.2);
    }

    // ---------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------

    private static void StepFor(GameState state, double seconds, Random rng)
    {
        var remaining = seconds;
        while (remaining > 0.0)
        {
            var dt = Math.Min(Balance.TickSeconds, remaining);
            Simulation.Step(state, dt, rng);
            remaining -= dt;
        }
    }

    private static void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  ok    {label}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {label}");
        }
    }
}
