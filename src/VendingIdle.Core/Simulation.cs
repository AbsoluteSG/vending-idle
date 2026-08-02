using System;
using System.Collections.Generic;

namespace VendingIdle.Core;

/// <summary>One follow-up dispense in a cascade, in the order it fired.</summary>
public readonly struct ChainHop
{
    public int SlotIndex { get; init; }
    public string? DrinkId { get; init; }
    public double Payout { get; init; }
    public bool Crit { get; init; }

    /// <summary>Echo Elixir fired: the hop paid out but took nothing off the shelf.</summary>
    public bool Preserved { get; init; }
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

    /// <summary>
    /// The cascade this dispense set off, in firing order. Empty far more often
    /// than not, so it is null rather than an empty list when nothing chained --
    /// this allocates on a path that runs thousands of times a second.
    /// </summary>
    public IReadOnlyList<ChainHop>? Chain { get; init; }

    /// <summary>Hops in the cascade, 0 when none fired.</summary>
    public int ChainLength => Chain?.Count ?? 0;
}

/// <summary>
/// What one shake of the cabinet produced. <see cref="Drops"/> holds one entry per
/// slot that gave something up, so the presentation layer can drop a bottle out of
/// each compartment rather than guessing where they came from. It is never empty:
/// a shake of a dry machine reports a single spare-change drop.
/// </summary>
public readonly struct ShakeResult
{
    public IReadOnlyList<ClickResult> Drops { get; init; }
    public double Payout { get; init; }
    public int Cans { get; init; }

    /// <summary>True when nothing was stocked and the machine paid coins instead.</summary>
    public bool SpareChange { get; init; }

    /// <summary>Extra passes Follow-Through added to this press.</summary>
    public int Repeats { get; init; }

    /// <summary>How many slots gave up a bottle -- 0 on a spare-change shake.</summary>
    public int SlotsHit => SpareChange ? 0 : Drops.Count;
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

        var slot = NextStockedSlot(state);
        if (slot is null) return PaySpareChange(state, rng);

        var result = DispenseFrom(state, slot, rng, units: 1);

        // A cascade vended slots further along, so the cursor has to clear the
        // last of them or the round-robin would serve it again immediately.
        var served = result.Chain is { Count: > 0 } hops
            ? hops[^1].SlotIndex
            : slot.Index;

        AdvanceCursor(state, served);
        return result;
    }

    /// <summary>
    /// The player's own interaction with the cabinet. A shake rattles every coil
    /// at once, so each stocked slot gives up <see cref="GameState.ShakeBottlesPerSlot"/>
    /// bottles -- not just the one the round-robin cursor happens to be pointing at.
    /// Customers still buy one drink at a time through <see cref="Click"/>; only the
    /// player shakes.
    /// </summary>
    /// <param name="bottlesPerSlot">
    /// Overrides the state's default, for effects that knock out more than one.
    /// </param>
    public static ShakeResult Shake(GameState state, Random rng, int? bottlesPerSlot = null)
    {
        state.TotalClicks++;

        var units = Math.Max(1, bottlesPerSlot ?? state.ShakeBottlesPerSlot);

        var drops = new List<ClickResult>();
        var payout = 0.0;
        var cans = 0;
        var repeats = 0;

        // Follow-Through can rock the cabinet again off one press. Capped: the
        // roll repeats after every pass, so without a ceiling a high enough
        // chance turns one press into an unbounded run.
        while (true)
        {
            foreach (var slot in state.Slots)
            {
                if (!slot.CanDispense) continue;

                var drop = DispenseFrom(state, slot, rng, units);
                drops.Add(drop);
                payout += drop.Payout;
                cans += drop.Cans;
            }

            if (repeats >= Balance.FollowThroughMaxRepeats) break;
            if (rng.NextDouble() >= state.FollowThroughChance) break;

            repeats++;
        }

        if (drops.Count == 0)
        {
            var change = PaySpareChange(state, rng);
            return new ShakeResult
            {
                Drops = new[] { change },
                Payout = change.Payout,
                Cans = 0,
                SpareChange = true
            };
        }

        return new ShakeResult
        {
            Drops = drops,
            Payout = payout,
            Cans = cans,
            SpareChange = false,
            Repeats = repeats
        };
    }

    /// <summary>
    /// Nothing loaded anywhere: you shake the machine and get coins back. Spare
    /// change is not a sale, so it earns no crate tokens and rolls no effects.
    /// </summary>
    private static ClickResult PaySpareChange(GameState state, Random rng)
    {
        var crit = RollCrit(state, rng);
        var change = state.SpareChangePayout * state.ClickValueMultiplier
                     * (crit ? Balance.CritMultiplier : 1.0);

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

    /// <summary>
    /// Takes up to <paramref name="units"/> bottles out of one slot and banks them.
    /// Deliberately does not touch <see cref="GameState.TotalClicks"/>: a shake is
    /// one player action however many slots it empties.
    /// </summary>
    private static ClickResult DispenseFrom(GameState state, Slot slot, Random rng, int units)
    {
        var drink = slot.Drink!;
        var level = state.EffectLevelOf(drink);

        // Static Shock lifts the crit chance of its own sales rather than the
        // whole cabinet's, so the drink has to be in the slot being shaken.
        // The drink's own boost, plus anything a foreman in its row is lending it.
        var crit = RollCrit(state, rng,
                            state.EffectStrengthOf(drink, EffectKind.CritBoost)
                            + state.RowForemanBonus(slot));
        var critMult = crit ? Balance.CritMultiplier : 1.0;

        // A crit always pays double; it takes an extra can with it when the coil
        // still has one to give beyond what was asked for.
        var sold = Math.Min(units, slot.Stock);
        var bonus = crit && slot.Stock > sold ? 1 : 0;

        // Twin Tap: a second bottle out of this same slot, where a chain would
        // have gone looking for a different one.
        if (drink.Effect == EffectKind.DoubleDrop && level > 0 &&
            slot.Stock > sold + bonus &&
            rng.NextDouble() < EffectStrength.DoubleDropChance(level))
            bonus++;

        var cans = sold + bonus;

        // Bottomless Cup: the sale happens, the shelf keeps its bottles.
        var preserved = drink.Effect == EffectKind.StockPreserve && level > 0 &&
                        rng.NextDouble() < EffectStrength.PreserveChance(level);
        if (!preserved) slot.Stock -= cans;

        var payout = drink.Value * state.ClickValueMultiplier * sold * critMult
                     * PositionMultiplier(state, slot, drink, level)
                     + ChargeBonus(state, slot, drink, level) * sold;

        // Everything that reads "how long since this slot last gave anything up"
        // has now been paid, so the clock restarts.
        slot.IdleSeconds = 0.0;
        state.Money += payout;
        state.TotalEarned += payout;
        state.TotalCansSold += cans;
        var curator = state.EffectStrengthOf(drink, EffectKind.Curator);
        var curatorTokens = curator > 0.0 ? curator * state.DistinctPackDrinksLoaded : 0.0;

        state.EarnTokens(cans * state.TokensPerBottle
                         + (crit ? Balance.CritTokenBonus : 0)
                         + curatorTokens);

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

        // Bulk Bottle: the sale hands back what the bottle cost to stock.
        var rebate = state.EffectStrengthOf(drink, EffectKind.Rebate);
        if (rebate > 0.0 && rng.NextDouble() < rebate)
        {
            var back = drink.UnitCostAt(slot.Stock, state.SlotCapacity, state.RestockGrowthFactor)
                       * state.RestockDiscount * sold;
            state.Money += back;
        }

        // Loyalty Lager: the sale pulls the next customer purchase forward.
        var pull = state.EffectStrengthOf(drink, EffectKind.CustomerPull);
        if (pull > 0.0 && rng.NextDouble() < pull)
            state.CustomerClickAccumulator += 1.0;

        var chain = RunCascade(state, slot, drink, level, rng);

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
    /// How much the slot's position and history are worth on top of the drink's
    /// face value. Every term here is bounded -- by how tall the cabinet is, or by
    /// a hard ceiling on banked time -- so none of them can compound away the
    /// way an open-ended multiplier would.
    /// </summary>
    private static double PositionMultiplier(GameState state, Slot slot, DrinkDef drink, int level)
    {
        if (level <= 0 || drink.Effect is not { } effect) return 1.0;

        switch (effect)
        {
            case EffectKind.FallValue:
                // Row 0 is the tray shelf and falls nowhere; the multiplier grows
                // with the drop and stops at the Balance ceiling.
                return 1.0 + EffectStrength.FallValue(level, slot.Row);

            case EffectKind.TopRow:
                return slot.Row >= state.TopUnlockedRow
                    ? 1.0 + EffectStrength.TopRowValue(level)
                    : 1.0;

            case EffectKind.TwinBonus:
                foreach (var neighbour in state.NeighboursOf(slot))
                    if (GameState.CountsAsSameDrink(drink, neighbour.Drink))
                        return 1.0 + EffectStrength.TwinValue(level);
                return 1.0;

            case EffectKind.LonerBonus:
                foreach (var neighbour in state.NeighboursOf(slot))
                    if (neighbour.Unlocked && neighbour.DrinkId is not null)
                        return 1.0;
                return 1.0 + EffectStrength.LonerValue(level);

            case EffectKind.Ageing:
                var aged = Math.Min(slot.IdleSeconds, Balance.AgeingMaxSeconds);
                return 1.0 + EffectStrength.AgeingRate(level) * aged;

            default:
                return 1.0;
        }
    }

    /// <summary>Static Cell's banked payout, released by this sale.</summary>
    private static double ChargeBonus(GameState state, Slot slot, DrinkDef drink, int level)
    {
        if (level <= 0 || drink.Effect != EffectKind.ChargeUp) return 0.0;

        var banked = Math.Min(slot.IdleSeconds, Balance.ChargeMaxSeconds);
        return drink.Value * EffectStrength.ChargeRate(level) * banked * state.ClickValueMultiplier;
    }

    /// <summary>
    /// Runs the cascade a dispense may set off, and returns the hops in firing
    /// order (null when none fired, which is the common case).
    ///
    /// Chains are the design pillar, so this is where the combo pieces meet: the
    /// seed chance comes from the slot's own drink plus the machine-wide upgrade,
    /// the hop ceiling comes from upgrades plus Relay Rum, and whether a hop can
    /// crit, keep its stock or pay bonus tokens comes from whatever auras are on
    /// the glass. None of them multiply raw value -- the payoff is length.
    ///
    /// Termination does not rest on the probabilities. Each hop must land on a
    /// slot the cascade has not already visited, and the visited list is bounded
    /// by the hop ceiling, so a cascade always ends even at 100% chance.
    /// </summary>
    private static List<ChainHop>? RunCascade(GameState state, Slot origin, DrinkDef drink,
                                              int level, Random rng)
    {
        var chance = state.ChainChance;

        if (level > 0)
        {
            // Chain Fizz seeds cascades; Jumper Juice only starts them, which is
            // why it is a common and Chain Fizz is not.
            if (drink.Effect == EffectKind.ChainDispense)
                chance += EffectStrength.ChainChance(level);
            else if (drink.Effect == EffectKind.SparkChain)
                chance += EffectStrength.SparkChance(level);
        }

        if (chance <= 0.0) return null;

        // Relay Rum and friends extend the cascades *they* start, so the ceiling
        // is read from the originating drink rather than from the cabinet.
        var maxHops = state.MaxChainHops
                      + (int)state.EffectStrengthOf(drink, EffectKind.ChainExtend);

        if (maxHops <= 0) return null;

        // Rolled before anything is allocated: the overwhelming majority of
        // dispenses never chain, and this runs thousands of times a second
        // during offline catch-up.
        if (rng.NextDouble() >= Math.Min(chance, Balance.ChainChanceMax)) return null;

        // Every hop-shaping effect belongs to the drink that started this
        // cascade. Read once here rather than per hop.
        var hopCritChance = state.EffectStrengthOf(drink, EffectKind.ChainCrit);
        var hopPreserveChance = state.EffectStrengthOf(drink, EffectKind.ChainPreserve);
        var hopTokenBonus = state.EffectStrengthOf(drink, EffectKind.ChainToken);

        var decay = state.ChainDecay;
        var forkChance = state.ChainForkChance;

        // Domino Drop routes its cascades through neighbouring slots instead of
        // the round-robin cursor. That is the difference between a chain you can
        // follow across the glass and one that lands on three unrelated cells.
        var domino = drink.Effect == EffectKind.DominoRouting && level > 0;
        var boomerang = state.EffectStrengthOf(drink, EffectKind.Boomerang);

        List<ChainHop>? hops = null;
        Span<int> visited = stackalloc int[maxHops * 2 + 2];
        visited[0] = origin.Index;
        var seen = 1;

        var hopChance = Math.Min(chance, Balance.ChainChanceMax);

        for (var hop = 0; hop < maxHops; hop++)
        {
            var from = seen > 0 ? state.SlotAt(visited[seen - 1]) : origin;
            var next = domino
                ? NextAdjacentStockedSlot(state, from, visited[..seen])
                : NextStockedSlot(state, visited[..seen]);

            if (next is null) break;

            hops ??= new List<ChainHop>(maxHops + 2);
            VendHop(state, next, rng, hopCritChance, hopPreserveChance, hopTokenBonus, hops);
            visited[seen++] = next.Index;

            // Split Coil: the hop takes a second slot with it. The fork is a free
            // rider -- it does not consume a hop from the budget and does not
            // extend the chain -- so forking widens a cascade without letting it
            // outlive its own decay.
            if (forkChance > 0.0 && seen < visited.Length && rng.NextDouble() < forkChance)
            {
                var fork = NextStockedSlot(state, visited[..seen]);
                if (fork is not null)
                {
                    VendHop(state, fork, rng, hopCritChance, hopPreserveChance, hopTokenBonus, hops);
                    visited[seen++] = fork.Index;
                }
            }

            // Decays every hop, so a long tail costs exponentially more chance
            // to reach rather than arriving all at once as the number climbs.
            hopChance *= decay;
            if (rng.NextDouble() >= hopChance) break;
        }

        // Boomerang Brew: one last hop back to where it all started. Deliberately
        // outside the loop and outside the visited rule -- the origin is the one
        // slot a cascade is otherwise guaranteed never to revisit, which is what
        // makes coming back to it feel like a loop closing.
        if (hops is { Count: > 0 } && boomerang > 0.0 &&
            rng.NextDouble() < boomerang && origin.CanDispense)
        {
            VendHop(state, origin, rng, hopCritChance, hopPreserveChance, hopTokenBonus, hops);
        }

        return hops;
    }

    /// <summary>
    /// The nearest stocked slot orthogonally adjacent to <paramref name="from"/>,
    /// skipping anything the cascade already took. Falls back to nothing rather
    /// than to the cursor: a Domino chain that jumps across the cabinet when it
    /// runs out of neighbours would undo the one thing the drink is for.
    /// </summary>
    private static Slot? NextAdjacentStockedSlot(GameState state, Slot? from, ReadOnlySpan<int> exclude)
    {
        if (from is null) return null;

        foreach (var neighbour in state.NeighboursOf(from))
        {
            if (!neighbour.CanDispense) continue;

            var skip = false;
            foreach (var index in exclude)
                if (index == neighbour.Index) { skip = true; break; }

            if (!skip) return neighbour;
        }

        return null;
    }

    /// <summary>
    /// Vends one slot as part of a cascade and records it. Deliberately does not
    /// re-enter <see cref="DispenseFrom"/>: a hop must not fire the hopped
    /// drink's own effects, or cascades would seed cascades and the hop budget
    /// would stop bounding anything.
    /// </summary>
    private static void VendHop(GameState state, Slot slot, Random rng,
                                double critChance, double preserveChance, double tokenBonus,
                                List<ChainHop> hops)
    {
        var drink = slot.Drink!;

        var crit = critChance > 0.0 && rng.NextDouble() < critChance;
        var preserved = preserveChance > 0.0 && rng.NextDouble() < preserveChance;

        if (!preserved) slot.Stock -= 1;

        var payout = drink.Value * state.ClickValueMultiplier
                     * (crit ? Balance.CritMultiplier : 1.0)
                     * Balance.ChainHopPayoutShare;

        state.Money += payout;
        state.TotalEarned += payout;
        state.TotalCansSold += 1;
        state.EarnTokens(state.TokensPerBottle + tokenBonus
                         + (crit ? Balance.CritTokenBonus : 0));

        hops.Add(new ChainHop
        {
            SlotIndex = slot.Index,
            DrinkId = drink.Id,
            Payout = payout,
            Crit = crit,
            Preserved = preserved
        });
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

    private static bool RollCrit(GameState state, Random rng, double bonus = 0.0)
    {
        var chance = Math.Min(Balance.CritChanceMax, state.CritChance + bonus);
        var crit = rng.NextDouble() < chance;
        if (crit) state.TotalCrits++;
        return crit;
    }

    /// <summary>
    /// Round-robin scan from the cursor, so the machine empties evenly.
    /// <paramref name="excludeIndex"/> skips the slot currently being served, which
    /// is what a chain needs: the cursor has not moved off it yet.
    /// </summary>
    private static Slot? NextStockedSlot(GameState state, int excludeIndex = -1)
    {
        var count = state.Slots.Count;
        if (count == 0) return null;

        var start = ((state.DispenseCursor % count) + count) % count;
        for (var i = 0; i < count; i++)
        {
            var slot = state.Slots[(start + i) % count];
            if (slot.Index == excludeIndex) continue;
            if (slot.CanDispense) return slot;
        }

        return null;
    }

    /// <summary>
    /// The cascade's version: skips every slot the chain has already been
    /// through, which is what bounds a cascade independently of its rolls.
    /// </summary>
    private static Slot? NextStockedSlot(GameState state, ReadOnlySpan<int> exclude)
    {
        var count = state.Slots.Count;
        if (count == 0) return null;

        var start = ((state.DispenseCursor % count) + count) % count;
        for (var i = 0; i < count; i++)
        {
            var slot = state.Slots[(start + i) % count];
            if (!slot.CanDispense) continue;

            var skip = false;
            foreach (var index in exclude)
                if (index == slot.Index) { skip = true; break; }

            if (!skip) return slot;
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

        TickRush(state, dt);
        TickIdle(state, dt);
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

    /// <summary>
    /// Rush Hour: a burst every 90 seconds. The timer is on the state and saved,
    /// so quitting mid-burst does not hand out a fresh one on load.
    /// </summary>
    private static void TickRush(GameState state, double dt)
    {
        if (state.RushMultiplier <= 1.0)
        {
            state.RushTimer = 0.0;
            return;
        }

        state.RushTimer += dt;
        if (state.RushTimer >= Balance.RushIntervalSeconds) state.RushTimer = 0.0;
    }

    /// <summary>
    /// Ages every slot that has not just dispensed. Static Cell banks against
    /// this while dry and Vintage Vial ages bottles against it while stocked;
    /// both reset it on sale.
    /// </summary>
    private static void TickIdle(GameState state, double dt)
    {
        foreach (var slot in state.Slots)
        {
            if (!slot.Unlocked || slot.DrinkId is null) continue;
            slot.IdleSeconds = Math.Min(slot.IdleSeconds + dt,
                                        Math.Max(Balance.ChargeMaxSeconds, Balance.AgeingMaxSeconds));
        }
    }

    private static void TickCustomers(GameState state, double dt, Random rng, ISimEvents? events)
    {
        var customers = state.Customers;
        if (customers <= 0) return;

        var clicksPerSecond = customers / state.CustomerInterval;
        if (state.RushActive) clicksPerSecond *= state.RushMultiplier;
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
        var limit = state.OfflineMaxSeconds;
        var capped = elapsedSeconds > limit;
        var seconds = Math.Clamp(elapsedSeconds, 0.0, limit);

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
