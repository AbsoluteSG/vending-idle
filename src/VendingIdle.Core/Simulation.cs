using System;

namespace VendingIdle.Core;

/// <summary>A Chain Fizz follow-up dispense that rode along with the primary one.</summary>
public readonly struct ChainInfo
{
    public int SlotIndex { get; init; }
    public string? DrinkId { get; init; }
    public double Payout { get; init; }
}

/// <summary>What a single dispense produced, so the UI can react to it.</summary>
public readonly struct ClickResult
{
    /// <summary>-1 when the machine was empty and paid spare change instead.</summary>
    public int SlotIndex { get; init; }
    public string? DrinkId { get; init; }
    public double Payout { get; init; }
    public int Cans { get; init; }
    public bool Crit { get; init; }
    public bool SpareChange { get; init; }

    /// <summary>Bottomless Cup fired: the bottle was paid for but stayed on the shelf.</summary>
    public bool Preserved { get; init; }

    /// <summary>Courier Cola fired into this slot (-1 when it did not).</summary>
    public int CourierSlotIndex { get; init; }

    /// <summary>Set when Chain Fizz vended a second slot along with this one.</summary>
    public ChainInfo? Chain { get; init; }
}

/// <summary>Optional hook for the presentation layer (floating text, particles, sound).</summary>
public interface ISimEvents
{
    void OnDispense(in ClickResult result, bool fromCustomer);
    void OnAutoRestock(int slotIndex, int units);
}

public sealed class OfflineReport
{
    public double Seconds { get; init; }
    public double Earned { get; init; }
    public int CansSold { get; init; }
    public bool Capped { get; init; }
}

/// <summary>
/// The whole game economy. Deliberately free of any MonoGame reference so it can
/// be exercised headlessly (see tools/VendingIdle.SimTest).
/// </summary>
public static class Simulation
{
    /// <summary>
    /// Resolves one click on the machine. Customers call this through the exact
    /// same path as the player, which is why idle income consumes real stock.
    /// </summary>
    public static ClickResult Click(GameState state, Random rng)
    {
        state.TotalClicks++;

        var crit = rng.NextDouble() < state.CritChance;
        var critMult = crit ? Balance.CritMultiplier : 1.0;
        if (crit) state.TotalCrits++;

        var slot = NextStockedSlot(state);

        if (slot is null)
        {
            // Nothing loaded anywhere: you shake the machine and get coins back.
            // Spare change is not a sale, so it earns no crate tokens and rolls
            // no effects.
            var change = Balance.SpareChange * state.ClickValueMultiplier * critMult;
            state.Money += change;
            state.TotalEarned += change;

            return new ClickResult
            {
                SlotIndex = -1,
                DrinkId = null,
                Payout = change,
                Cans = 0,
                Crit = crit,
                SpareChange = true,
                CourierSlotIndex = -1
            };
        }

        var drink = slot.Drink!;
        var level = state.EffectLevelOf(drink);

        // A crit always pays double; it takes a second can with it when the coil
        // has one to give.
        var cans = crit && slot.Stock >= 2 ? 2 : 1;

        // Bottomless Cup: the sale happens, the shelf keeps its bottles.
        var preserved = drink.Effect == EffectKind.StockPreserve && level > 0 &&
                        rng.NextDouble() < EffectStrength.PreserveChance(level);
        if (!preserved) slot.Stock -= cans;

        var payout = drink.Value * state.ClickValueMultiplier * critMult;
        state.Money += payout;
        state.TotalEarned += payout;
        state.TotalCansSold += cans;
        state.Tokens += cans * Balance.TokensPerBottle + (crit ? Balance.CritTokenBonus : 0);

        AdvanceCursor(state, slot.Index);

        // Courier Cola: chance to drop one free bottle into a dry slot elsewhere.
        var courierIndex = -1;
        if (drink.Effect == EffectKind.CourierDrop && level > 0 &&
            rng.NextDouble() < EffectStrength.CourierChance(level))
        {
            var target = FindDrySlot(state, slot.Index);
            if (target is not null)
            {
                target.Stock = 1;
                courierIndex = target.Index;
            }
        }

        // Chain Fizz: chance to vend the next stocked slot too. The chained
        // dispense is deliberately plain -- no crit, no proc rolls of its own --
        // so two Chain Fizz slots can never recurse into each other.
        ChainInfo? chain = null;
        if (drink.Effect == EffectKind.ChainDispense && level > 0 &&
            rng.NextDouble() < EffectStrength.ChainChance(level))
        {
            var next = NextStockedSlot(state);
            if (next is not null)
            {
                var nextDrink = next.Drink!;
                next.Stock -= 1;

                var chainPayout = nextDrink.Value * state.ClickValueMultiplier;
                state.Money += chainPayout;
                state.TotalEarned += chainPayout;
                state.TotalCansSold += 1;
                state.Tokens += Balance.TokensPerBottle;

                AdvanceCursor(state, next.Index);

                chain = new ChainInfo
                {
                    SlotIndex = next.Index,
                    DrinkId = nextDrink.Id,
                    Payout = chainPayout
                };
            }
        }

        return new ClickResult
        {
            SlotIndex = slot.Index,
            DrinkId = drink.Id,
            Payout = payout,
            Cans = cans,
            Crit = crit,
            SpareChange = false,
            Preserved = preserved,
            CourierSlotIndex = courierIndex,
            Chain = chain
        };
    }

    /// <summary>
    /// A loaded, unlocked slot that has run completely dry -- Courier Cola is
    /// insurance against dry slots, so it strictly refuses to top up a slot that
    /// still has anything in it.
    /// </summary>
    private static Slot? FindDrySlot(GameState state, int excludeIndex)
    {
        foreach (var slot in state.Slots)
        {
            if (slot.Index == excludeIndex) continue;
            if (slot.Unlocked && slot.DrinkId is not null && slot.Stock == 0)
                return slot;
        }

        return null;
    }

    /// <summary>Round-robin scan from the cursor, so the machine empties evenly.</summary>
    private static Slot? NextStockedSlot(GameState state)
    {
        var count = state.Slots.Count;
        if (count == 0) return null;

        var start = ((state.DispenseCursor % count) + count) % count;
        for (var i = 0; i < count; i++)
        {
            var slot = state.Slots[(start + i) % count];
            if (slot.CanDispense) return slot;
        }

        return null;
    }

    private static void AdvanceCursor(GameState state, int servedIndex)
    {
        var count = state.Slots.Count;
        state.DispenseCursor = count == 0 ? 0 : (servedIndex + 1) % count;
    }

    /// <summary>
    /// Advances the world by <paramref name="dt"/> seconds. Used identically for
    /// live 20 Hz ticks and for offline catch-up at 1 s granularity.
    /// </summary>
    public static void Step(GameState state, double dt, Random rng, ISimEvents? events = null)
    {
        if (dt <= 0.0) return;

        TickAutoRestock(state, dt, events);
        TickCustomers(state, dt, rng, events);
    }

    private static void TickAutoRestock(GameState state, double dt, ISimEvents? events)
    {
        var interval = state.AutoRestockInterval;

        foreach (var slot in state.Slots)
        {
            if (!slot.HasAutoRestocker || !slot.Unlocked || slot.DrinkId is null)
                continue;

            if (state.RoomIn(slot) <= 0)
            {
                slot.AutoTimer = 0.0;
                continue;
            }

            slot.AutoTimer += dt;

            var units = (int)(slot.AutoTimer / interval);
            if (units <= 0) continue;

            slot.AutoTimer -= units * interval;

            var added = state.Restock(slot, units);
            if (added > 0) events?.OnAutoRestock(slot.Index, added);

            // Could not afford it (or hit the cap): do not bank credit for later.
            if (added < units) slot.AutoTimer = 0.0;
        }
    }

    private static void TickCustomers(GameState state, double dt, Random rng, ISimEvents? events)
    {
        var customers = state.Customers;
        if (customers <= 0) return;

        var clicksPerSecond = customers / state.CustomerInterval;
        state.CustomerClickAccumulator += clicksPerSecond * dt;

        var clicks = (int)state.CustomerClickAccumulator;
        if (clicks <= 0) return;

        if (clicks > Balance.MaxClicksPerStep)
        {
            clicks = Balance.MaxClicksPerStep;
            state.CustomerClickAccumulator = 0.0;
        }
        else
        {
            state.CustomerClickAccumulator -= clicks;
        }

        for (var i = 0; i < clicks; i++)
        {
            var result = Click(state, rng);
            events?.OnDispense(result, fromCustomer: true);
        }
    }

    /// <summary>
    /// Replays elapsed time through the same <see cref="Step"/>, capped at
    /// <see cref="Balance.OfflineMaxSeconds"/>.
    /// </summary>
    public static OfflineReport RunOffline(GameState state, double elapsedSeconds, Random rng)
    {
        var capped = elapsedSeconds > Balance.OfflineMaxSeconds;
        var seconds = Math.Clamp(elapsedSeconds, 0.0, Balance.OfflineMaxSeconds);

        var moneyBefore = state.Money;
        var cansBefore = state.TotalCansSold;

        var remaining = seconds;
        while (remaining > 0.0)
        {
            var dt = Math.Min(Balance.OfflineStepSeconds, remaining);
            Step(state, dt, rng);
            remaining -= dt;
        }

        return new OfflineReport
        {
            Seconds = seconds,
            Earned = state.Money - moneyBefore,
            CansSold = (int)(state.TotalCansSold - cansBefore),
            Capped = capped
        };
    }

    /// <summary>
    /// Best-case income for the UI readout: what the current customers would earn
    /// if stock never ran out. Actual earnings fall below this whenever a slot is
    /// dry, which is the signal the player is meant to read.
    /// </summary>
    public static double PotentialIncomePerSecond(GameState state)
    {
        if (state.Customers <= 0) return 0.0;

        var clicksPerSecond = state.Customers / state.CustomerInterval;

        // Average over the slots that are actually loaded -- that is what the
        // round-robin will cycle through.
        var total = 0.0;
        var loaded = 0;
        foreach (var slot in state.Slots)
        {
            if (!slot.Unlocked || slot.Drink is null) continue;
            total += slot.Drink.Value;
            loaded++;
        }

        var perClick = loaded > 0
            ? total / loaded * state.ClickValueMultiplier
            : Balance.SpareChange * state.ClickValueMultiplier;

        // Crits pay double, so they scale expected value by (1 + chance).
        return clicksPerSecond * perClick * (1.0 + state.CritChance);
    }
}
