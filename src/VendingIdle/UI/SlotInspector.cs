using System;
using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

public struct InspectorAction
{
    public string? AssignDrinkId;
    public bool ClearAssignment;
    /// <summary>0 = none, -1 = fill to capacity, otherwise a unit count.</summary>
    public int RestockUnits;
    public bool BuyAutoRestocker;

    public static InspectorAction None => new();
}

/// <summary>Everything you can do to the currently selected slot.</summary>
public static class SlotInspector
{
    public static InspectorAction Draw(Ui ui, GameState state, Rectangle bounds, int selectedIndex)
    {
        var action = InspectorAction.None;

        ui.Panel(bounds, "SELECTED SLOT");
        var body = Ui.PanelBody(bounds);

        var slot = state.SlotAt(selectedIndex);
        if (slot is null || !slot.Unlocked)
        {
            ui.T.DrawIn(ui.Sb, "Buy a slot to get started.",
                new Rectangle(body.X, body.Y + 20, body.Width, 20),
                Theme.TextDim, FontSize.Small, Align.Center);
            return action;
        }

        var y = body.Y;

        // ---- Header -------------------------------------------------------
        ui.T.Draw(ui.Sb, $"Row {slot.Row + 1}, column {slot.Column + 1}",
                  new Vector2(body.X, y), Theme.TextDim, FontSize.Small);
        y += 22;

        var drink = slot.Drink;
        var capacity = state.SlotCapacity;

        if (drink is null)
        {
            ui.T.Draw(ui.Sb, "No drink loaded", new Vector2(body.X, y), Theme.TextFaint);
            y += 28;
        }
        else
        {
            var swatch = new Rectangle(body.X, y + 2, 14, 18);
            ui.P.FillRounded(ui.Sb, swatch, 3, Theme.FromPacked(drink.Color));

            ui.T.Draw(ui.Sb, drink.Name, new Vector2(body.X + 22, y), Theme.Text);
            y += 26;

            ui.StatRow(new Rectangle(body.X, y, body.Width, 16),
                "Value per bottle",
                Money.Cash(drink.Value * state.ClickValueMultiplier),
                Theme.Money);
            y += 18;

            var nextUnit = state.UnitCost(slot);
            ui.StatRow(new Rectangle(body.X, y, body.Width, 16),
                "Next bottle costs", Money.Cash(nextUnit));
            y += 18;

            ui.StatRow(new Rectangle(body.X, y, body.Width, 16),
                "Profit per bottle",
                Money.Cash(drink.Value * state.ClickValueMultiplier - nextUnit),
                Theme.Positive);
            y += 24;

            // ---- Stock ----------------------------------------------------
            var fraction = capacity > 0 ? slot.Stock / (float)capacity : 0f;
            ui.ProgressBar(new Rectangle(body.X, y, body.Width, 10), fraction,
                           slot.Stock == 0 ? Theme.Negative : Theme.Positive);
            y += 14;

            ui.StatRow(new Rectangle(body.X, y, body.Width, 16),
                "Stock", $"{slot.Stock} / {capacity}");
            y += 24;

            // ---- Restock buttons -------------------------------------------
            var room = state.RoomIn(slot);
            var buttonWidth = (body.Width - 12) / 3;
            var buttonRect = new Rectangle(body.X, y, buttonWidth, 30);

            var cost1 = state.RestockCost(slot, Math.Min(1, room));
            if (ui.Button(buttonRect, "+1", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy, $"Restock 1 bottle for {Money.Cash(cost1)}"))
                action.RestockUnits = 1;

            var cost5 = state.RestockCost(slot, Math.Min(5, room));
            buttonRect.X += buttonWidth + 6;
            if (ui.Button(buttonRect, "+5", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy, $"Restock up to 5 bottles, {Money.Cash(cost5)}"))
                action.RestockUnits = 5;

            var costFull = state.RestockCost(slot, room);
            buttonRect.X += buttonWidth + 6;
            if (ui.Button(buttonRect, "Fill", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy, $"Fill the slot, {Money.Cash(costFull)}"))
                action.RestockUnits = -1;

            y += 38;
        }

        // ---- Automation ------------------------------------------------------
        var autoRect = new Rectangle(body.X, y, body.Width, 30);
        if (slot.HasAutoRestocker)
        {
            ui.P.FillRounded(ui.Sb, autoRect, 6, Theme.PanelAlt);
            ui.T.DrawIn(ui.Sb,
                $"Automated  •  {state.AutoRestockInterval:0.##}s per bottle",
                autoRect, Theme.Accent, FontSize.Small, Align.Center);
        }
        else
        {
            var cost = state.NextAutoRestockerCost;
            if (ui.Button(autoRect, $"Auto-restocker  {Money.Cash(cost)}",
                          state.Money >= cost, ButtonStyle.Buy,
                          "Keeps this slot topped up on its own", FontSize.Small))
                action.BuyAutoRestocker = true;
        }

        y += 40;
        ui.Separator(body.X, y, body.Width);
        y += 10;

        // ---- Drink picker -----------------------------------------------------
        ui.T.Draw(ui.Sb, "LOAD DRINK", new Vector2(body.X, y), Theme.TextDim, FontSize.Small);
        y += 20;

        foreach (var def in DrinkDatabase.All)
        {
            var rowRect = new Rectangle(body.X, y, body.Width, 40);
            var unlocked = DrinkDatabase.IsUnlocked(def, state);
            var isCurrent = slot.DrinkId == def.Id;

            var hovered = unlocked && !ui.ClickConsumed && ui.Hovering(rowRect);
            var bg = isCurrent ? Theme.ButtonActive
                   : hovered ? Theme.ButtonHover
                   : unlocked ? Theme.PanelAlt
                   : Theme.ButtonDisabled;

            ui.P.FillRounded(ui.Sb, rowRect, 6, bg);

            var swatch = new Rectangle(rowRect.X + 8, rowRect.Y + 11, 12, 18);
            ui.P.FillRounded(ui.Sb, swatch, 3,
                unlocked ? Theme.FromPacked(def.Color) : Theme.TextFaint);

            var nameColor = unlocked ? Theme.Text : Theme.TextFaint;
            ui.T.Draw(ui.Sb, def.Name, new Vector2(rowRect.X + 28, rowRect.Y + 4),
                      nameColor, FontSize.Small);

            if (unlocked)
            {
                ui.T.Draw(ui.Sb,
                    $"{Money.Cash(def.Value)} each  •  {Money.Cash(def.RestockUnitCost)} restock",
                    new Vector2(rowRect.X + 28, rowRect.Y + 21), Theme.TextDim, FontSize.Small);
            }
            else
            {
                ui.T.Draw(ui.Sb,
                    $"earn {Money.Cash(def.UnlockAtEarned)} total to unlock",
                    new Vector2(rowRect.X + 28, rowRect.Y + 21), Theme.TextFaint, FontSize.Small);
            }

            if (isCurrent)
                ui.T.DrawIn(ui.Sb, "loaded", rowRect, Theme.Accent, FontSize.Small,
                            Align.Right, padX: 10);

            if (hovered && ui.MousePressed && !isCurrent)
            {
                ui.ClickConsumed = true;
                action.AssignDrinkId = def.Id;
            }

            y += 44;
        }

        // Swapping wipes the shelf, so say so before the player finds out.
        if (slot.DrinkId is not null && slot.Stock > 0)
            ui.T.DrawIn(ui.Sb, "Changing drink empties the slot.",
                new Rectangle(body.X, y, body.Width, 16),
                Theme.TextFaint, FontSize.Small, Align.Center);

        return action;
    }
}
