using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

public struct TopBarAction
{
    public bool RestockAll;
    public bool Save;
}

/// <summary>
/// A compact readout floating above the machine. Only the numbers you need at a
/// glance live here -- everything else moved into the side drawers.
/// </summary>
public static class TopBar
{
    public static TopBarAction Draw(Ui ui, GameState state, Rectangle bounds,
                                    double actualIncomePerSecond, double savedSecondsAgo)
    {
        var action = new TopBarAction();

        ui.P.FillRounded(ui.Sb, bounds, 10, Theme.Panel);
        ui.P.OutlineRounded(ui.Sb, bounds, 10, Theme.PanelEdge);

        // ---- Money ---------------------------------------------------------
        ui.T.Draw(ui.Sb, Money.Cash(state.Money),
                  new Vector2(bounds.X + 16, bounds.Y + 5), Theme.Money, FontSize.Large);

        // ---- Rate ----------------------------------------------------------
        var potential = Simulation.PotentialIncomePerSecond(state);
        var starved = potential > 0 && actualIncomePerSecond < potential * 0.6;

        var rateText = potential > 0
            ? $"{Money.FormatRate(actualIncomePerSecond)} of {Money.FormatRate(potential)}"
            : "no customers yet";

        ui.T.Draw(ui.Sb, rateText,
                  new Vector2(bounds.X + 16, bounds.Y + 34),
                  starved ? Theme.Negative : Theme.TextDim, FontSize.Small);

        // ---- Buttons -------------------------------------------------------
        var saveRect = new Rectangle(bounds.Right - 80, bounds.Y + 11, 64, 30);
        if (ui.Button(saveRect, "Save", true, ButtonStyle.Subtle,
                      savedSecondsAgo < 3600
                          ? $"Last saved {Money.FormatDuration(savedSecondsAgo)} ago"
                          : "Save now",
                      FontSize.Small))
            action.Save = true;

        var restockRect = new Rectangle(bounds.Right - 202, bounds.Y + 11, 114, 30);
        if (ui.Button(restockRect, "Restock all", CanRestockAnything(state), ButtonStyle.Buy,
                      "Fill every loaded slot as far as your money goes", FontSize.Small))
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
