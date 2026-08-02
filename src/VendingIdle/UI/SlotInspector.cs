using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Wheel scroll for the drink list -- twelve drinks no longer fit the panel.</summary>
    private static float _scroll;

    /// <summary>Total room across every slot an action would touch.</summary>
    private static int RoomAcross(GameState state, IReadOnlyList<int> targets)
    {
        var room = 0;
        foreach (var index in targets)
            if (state.SlotAt(index) is { } slot && slot.DrinkId is not null)
                room += state.RoomIn(slot);
        return room;
    }

    /// <summary>
    /// What restocking <paramref name="units"/> into every target would cost.
    /// Summed per slot because unit price climbs with how full that slot already
    /// is -- one price times the count would be wrong in both directions.
    /// </summary>
    private static double RestockCostAcross(GameState state, IReadOnlyList<int> targets, int units)
    {
        var total = 0.0;
        foreach (var index in targets)
        {
            if (state.SlotAt(index) is not { } slot || slot.DrinkId is null) continue;

            var want = Math.Min(units, state.RoomIn(slot));
            if (want > 0) total += state.RestockCost(slot, want);
        }

        return total;
    }



    /// <param name="focusIndex">
    /// Keyboard-focused drink row, or -1 when the pointer owns this panel. The
    /// list is the only thing keys drive here: the buttons above it are a handful
    /// of one-shot actions that already have their own bindings, and threading
    /// focus through them would make W/S walk past things it cannot press.
    /// </param>
    /// <param name="activate">Enter was pressed while this panel held focus.</param>
    /// <param name="targets">
    /// Every slot the panel's actions apply to -- just the selection normally,
    /// the whole row while shift is held. Prices shown are the total for these,
    /// not for one slot: a Fill button that charges four times what it advertises
    /// is worse than having no row mode at all.
    /// </param>
    public static InspectorAction Draw(Ui ui, GameState state, Rectangle bounds, int selectedIndex,
                                       int focusIndex = -1, bool activate = false,
                                       IReadOnlyList<int>? targets = null)
    {
        var action = InspectorAction.None;

        ui.Panel(bounds, "SELECTED SLOT");
        var body = Ui.PanelBody(bounds);

        // Only unlocked slots can be acted on, so a row containing locked cells
        // simply acts on fewer of them rather than refusing outright.
        var acting = (targets ?? new[] { selectedIndex })
            .Where(i => state.SlotAt(i) is { Unlocked: true })
            .ToList();

        if (acting.Count == 0) acting.Add(selectedIndex);
        var rowMode = acting.Count > 1;

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

            if (drink.Effect is { } headerEffect)
            {
                var level = state.EffectLevelOf(drink);
                ui.T.Draw(ui.Sb,
                    $"{EffectDatabase.Get(headerEffect).Describe(level)}  •  Lv {level}",
                    new Vector2(body.X, y), Theme.Accent, FontSize.Small);
                y += 20;
            }

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
            var room = RoomAcross(state, acting);
            var buttonWidth = (body.Width - 12) / 3;
            var buttonRect = new Rectangle(body.X, y, buttonWidth, 30);

            var scope = rowMode ? "the row" : "the slot";

            var cost1 = RestockCostAcross(state, acting, 1);
            if (ui.Button(buttonRect, "+1", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy,
                          $"Restock 1 bottle in {scope} for {Money.Cash(cost1)}"))
                action.RestockUnits = 1;

            var cost5 = RestockCostAcross(state, acting, 5);
            buttonRect.X += buttonWidth + 6;
            if (ui.Button(buttonRect, "+5", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy,
                          $"Restock up to 5 bottles in {scope}, {Money.Cash(cost5)}"))
                action.RestockUnits = 5;

            var costFull = RestockCostAcross(state, acting, int.MaxValue);
            buttonRect.X += buttonWidth + 6;
            if (ui.Button(buttonRect, "Fill", room > 0 && state.Money >= cost1,
                          ButtonStyle.Buy, $"Fill {scope}, {Money.Cash(costFull)}"))
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
            // Each restocker costs more than the last, so a row of them is the sum
            // of an escalating run rather than one price times the count.
            var missing = acting.Count(i => state.SlotAt(i) is { Unlocked: true, HasAutoRestocker: false });
            var cost = state.AutoRestockerCostFor(Math.Max(1, missing));

            var label = rowMode && missing > 1
                ? $"Auto-restock row ({missing})  {Money.Cash(cost)}"
                : $"Auto-restocker  {Money.Cash(cost)}";

            if (ui.Button(autoRect, label, state.Money >= cost, ButtonStyle.Buy,
                          rowMode && missing > 1
                              ? $"Automates {missing} slots in this row"
                              : "Keeps this slot topped up on its own",
                          FontSize.Small))
                action.BuyAutoRestocker = true;
        }

        y += 40;
        ui.Separator(body.X, y, body.Width);
        y += 10;

        // ---- Drink picker -----------------------------------------------------
        ui.T.Draw(ui.Sb, "LOAD DRINK", new Vector2(body.X, y), Theme.TextDim, FontSize.Small);
        y += 20;

        const int rowStride = 44;
        var footer = slot.DrinkId is not null && slot.Stock > 0 ? 18 : 0;
        var listArea = new Rectangle(body.X, y, body.Width, Math.Max(0, body.Bottom - y - footer));
        var contentHeight = DrinkDatabase.All.Count * rowStride;
        var maxScroll = Math.Max(0, contentHeight - listArea.Height);

        if (ui.Hovering(listArea) && ui.WheelDelta != 0)
            _scroll -= ui.WheelDelta * 0.35f;
        _scroll = MathHelper.Clamp(_scroll, 0f, maxScroll);

        ui.PushClip(listArea);
        var mouseInList = listArea.Contains(ui.Mouse);
        var rowY = listArea.Y - (int)_scroll;

        // Keeps the focused row on screen when the keys walk off the end of the
        // visible window -- a focus you cannot see is worse than none.
        if (focusIndex >= 0)
        {
            var focusTop = focusIndex * rowStride;
            var focusBottom = focusTop + rowStride;

            if (focusTop < _scroll) _scroll = focusTop;
            else if (focusBottom > _scroll + listArea.Height)
                _scroll = focusBottom - listArea.Height;

            _scroll = MathHelper.Clamp(_scroll, 0f, maxScroll);
            rowY = listArea.Y - (int)_scroll;
        }

        var row = -1;
        foreach (var def in DrinkDatabase.All)
        {
            row++;
            var rowRect = new Rectangle(body.X, rowY, body.Width, 40);
            rowY += rowStride;

            if (rowRect.Bottom < listArea.Y || rowRect.Y > listArea.Bottom) continue;

            var unlocked = DrinkDatabase.IsUnlocked(def, state);
            var isCurrent = slot.DrinkId == def.Id;
            var isPack = def.Source == DrinkSource.Pack;

            var hovered = unlocked && mouseInList && !ui.ClickConsumed && ui.Hovering(rowRect);
            var bg = isCurrent ? Theme.ButtonActive
                   : hovered ? Theme.ButtonHover
                   : unlocked ? Theme.PanelAlt
                   : Theme.ButtonDisabled;

            ui.P.FillRounded(ui.Sb, rowRect, 6, bg);

            if (row == focusIndex)
            {
                ui.P.OutlineRounded(ui.Sb, rowRect, 6, Theme.Accent, 2);
                if (activate && unlocked && !isCurrent) action.AssignDrinkId = def.Id;
            }

            var swatch = new Rectangle(rowRect.X + 8, rowRect.Y + 11, 12, 18);
            ui.P.FillRounded(ui.Sb, swatch, 3,
                unlocked ? Theme.FromPacked(def.Color) : Theme.TextFaint);

            var nameColor = unlocked ? Theme.Text : Theme.TextFaint;

            var tag = isCurrent ? "loaded"
                    : isPack && unlocked
                        ? (state.EffectLevelOf(def) >= Balance.EffectLevelMax
                            ? "MAX" : $"Lv {state.EffectLevelOf(def)}")
                        : null;

            // Swatch on the left, status tag on the right; everything written in
            // between shares what is left over. Rows with no tag get that space
            // back rather than truncating against a reservation nothing uses.
            var tagWidth = tag is null ? 0f : ui.T.Measure(tag, FontSize.Small).X + 12f;
            var textWidth = rowRect.Width - 28 - 10 - tagWidth;

            ui.T.DrawWithin(ui.Sb, def.Name, new Vector2(rowRect.X + 28, rowRect.Y + 4),
                            nameColor, textWidth, FontSize.Small);

            if (isPack && def.Effect is { } effect)
            {
                if (unlocked)
                {
                    var level = state.EffectLevelOf(def);
                    ui.T.DrawWithin(ui.Sb, EffectDatabase.Get(effect).Describe(level),
                        new Vector2(rowRect.X + 28, rowRect.Y + 21),
                        Theme.Accent, textWidth, FontSize.Small);
                }
                else
                {
                    ui.T.DrawWithin(ui.Sb, "found in supply crates",
                        new Vector2(rowRect.X + 28, rowRect.Y + 21),
                        Theme.TextFaint, textWidth, FontSize.Small);
                }
            }
            else if (unlocked)
            {
                ui.T.DrawWithin(ui.Sb,
                    $"{Money.Cash(def.Value)} each  •  {Money.Cash(def.RestockUnitCost)} restock",
                    new Vector2(rowRect.X + 28, rowRect.Y + 21), Theme.TextDim, textWidth,
                    FontSize.Small);
            }
            else
            {
                ui.T.DrawWithin(ui.Sb,
                    $"earn {Money.Cash(def.UnlockAtEarned)} total to unlock",
                    new Vector2(rowRect.X + 28, rowRect.Y + 21), Theme.TextFaint, textWidth,
                    FontSize.Small);
            }

            if (tag is not null)
                ui.T.DrawIn(ui.Sb, tag, rowRect,
                            isCurrent ? Theme.Accent : Theme.TextDim,
                            FontSize.Small, Align.Right, padX: 10);

            if (hovered && isPack && unlocked)
                ui.SetTooltip(
                    $"{Money.Cash(def.Value)} each  •  {Money.Cash(def.RestockUnitCost)} restock",
                    rowRect);

            if (hovered && ui.MousePressed && !isCurrent)
            {
                ui.ClickConsumed = true;
                action.AssignDrinkId = def.Id;
            }
        }

        ui.PopClip();

        // Swapping wipes the shelf, so say so before the player finds out.
        if (footer > 0)
            ui.T.DrawIn(ui.Sb, "Changing drink empties the slot.",
                new Rectangle(body.X, body.Bottom - 16, body.Width, 16),
                Theme.TextFaint, FontSize.Small, Align.Center);

        return action;
    }
}
