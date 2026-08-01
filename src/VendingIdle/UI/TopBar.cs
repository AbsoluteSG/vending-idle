using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

public struct TopBarAction
{
    public bool RestockAll;
    public bool Save;
}

public static class TopBar
{
    public static TopBarAction Draw(Ui ui, GameState state, Rectangle bounds,
                                    double actualIncomePerSecond, double savedSecondsAgo)
    {
        var action = new TopBarAction();

        ui.P.FillRounded(ui.Sb, bounds, 8, Theme.Panel);
        ui.P.OutlineRounded(ui.Sb, bounds, 8, Theme.PanelEdge);

        // ---- Money ---------------------------------------------------------
        ui.T.Draw(ui.Sb, Money.Cash(state.Money),
                  new Vector2(bounds.X + 16, bounds.Y + 8), Theme.Money, FontSize.Large);

        ui.T.Draw(ui.Sb, "in the till",
                  new Vector2(bounds.X + 16, bounds.Y + 38), Theme.TextFaint, FontSize.Small);

        // ---- Rates ---------------------------------------------------------
        var potential = Simulation.PotentialIncomePerSecond(state);
        var starved = potential > 0 && actualIncomePerSecond < potential * 0.6;

        ui.T.Draw(ui.Sb, Money.FormatRate(actualIncomePerSecond),
                  new Vector2(bounds.X + 200, bounds.Y + 10),
                  starved ? Theme.Negative : Theme.Positive);

        // The gap between actual and potential is the "you are out of stock" tell.
        ui.T.Draw(ui.Sb, potential > 0 ? $"of {Money.FormatRate(potential)} possible" : "no customers yet",
                  new Vector2(bounds.X + 200, bounds.Y + 34), Theme.TextFaint, FontSize.Small);

        // ---- Counters ------------------------------------------------------
        var stats = new Rectangle(bounds.X + 400, bounds.Y + 5, 240, 17);
        ui.StatRow(stats, "Customers", state.Customers.ToString());

        stats.Y += 17;
        ui.StatRow(stats, "Slots", state.SlotsOwned.ToString());

        stats.Y += 17;
        ui.StatRow(stats, "Cans sold", state.TotalCansSold.ToString());

        var stats2 = new Rectangle(bounds.X + 664, bounds.Y + 5, 200, 17);
        ui.StatRow(stats2, "Total stock", state.TotalStock.ToString());

        stats2.Y += 17;
        ui.StatRow(stats2, "Double drop", (state.CritChance * 100).ToString("0.#") + "%");

        stats2.Y += 17;
        ui.StatRow(stats2, "Lifetime", Money.Cash(state.TotalEarned), Theme.Money);

        // ---- Buttons -------------------------------------------------------
        var saveRect = new Rectangle(bounds.Right - 96, bounds.Y + 16, 80, 32);
        if (ui.Button(saveRect, "Save", true, ButtonStyle.Subtle,
                      savedSecondsAgo < 3600
                          ? $"Last saved {Money.FormatDuration(savedSecondsAgo)} ago"
                          : "Save now"))
            action.Save = true;

        var restockRect = new Rectangle(bounds.Right - 232, bounds.Y + 16, 128, 32);
        var canRestock = CanRestockAnything(state);
        if (ui.Button(restockRect, "Restock all", canRestock, ButtonStyle.Buy,
                      "Fill every loaded slot as far as your money goes"))
            action.RestockAll = true;

        return action;
    }

    private static bool CanRestockAnything(GameState state)
    {
        foreach (var slot in state.Slots)
        {
            if (!slot.Unlocked || slot.Drink is null) continue;
            if (state.RoomIn(slot) <= 0) continue;
            if (state.Money >= state.UnitCost(slot))
                return true;
        }

        return false;
    }
}
