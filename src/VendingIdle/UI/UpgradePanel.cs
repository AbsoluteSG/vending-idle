using System;
using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// The left drawer: lifetime stats on top, then the global upgrade list. Returns
/// the upgrade clicked this frame, if any.
/// </summary>
public static class UpgradePanel
{
    private const int CardHeight = 62;
    private const int CardGap = 5;

    public static UpgradeId? Draw(Ui ui, GameState state, Rectangle bounds)
    {
        UpgradeId? bought = null;

        ui.Panel(bounds, "UPGRADES");
        var body = Ui.PanelBody(bounds);

        var y = DrawStats(ui, state, body);

        foreach (var def in UpgradeDatabase.All)
        {
            var rect = new Rectangle(body.X, y, body.Width, CardHeight);
            if (rect.Bottom > body.Bottom) break;

            var level = state.UpgradeLevel(def.Id);
            var maxed = def.IsMaxed(level);
            var cost = def.CostAt(level);
            var affordable = !maxed && state.Money >= cost;

            var hovered = !ui.ClickConsumed && ui.Hovering(rect) && affordable;

            var bg = maxed ? Theme.ButtonDisabled
                   : hovered ? Theme.BuyHover
                   : affordable ? Theme.BuyIdle
                   : Theme.PanelAlt;

            ui.P.FillRounded(ui.Sb, rect, 6, bg);

            ui.T.Draw(ui.Sb, ui.T.Fit(def.Name, rect.Width - 54, FontSize.Small),
                      new Vector2(rect.X + 9, rect.Y + 5), Theme.Text, FontSize.Small);

            ui.T.DrawIn(ui.Sb, maxed ? "MAX" : $"Lv {level}",
                new Rectangle(rect.X, rect.Y + 5, rect.Width - 9, 16),
                maxed ? Theme.Accent : Theme.TextDim, FontSize.Small, Align.Right);

            ui.T.Draw(ui.Sb, ui.T.Fit(def.EffectText(level), rect.Width - 18, FontSize.Small),
                      new Vector2(rect.X + 9, rect.Y + 23), Theme.TextDim, FontSize.Small);

            if (maxed)
            {
                ui.T.Draw(ui.Sb, "fully upgraded",
                          new Vector2(rect.X + 9, rect.Y + 41), Theme.TextFaint, FontSize.Small);
            }
            else
            {
                var costText = Money.Cash(cost);
                ui.T.Draw(ui.Sb, costText,
                          new Vector2(rect.X + 9, rect.Y + 41),
                          affordable ? Theme.Money : Theme.TextFaint, FontSize.Small);

                // Give the "next level" preview whatever the price left over,
                // rather than a fixed fraction that truncates on long effects.
                var costWidth = ui.T.Measure(costText, FontSize.Small).X;
                var room = rect.Width - 18 - costWidth - 10;

                ui.T.DrawIn(ui.Sb, ui.T.Fit("→ " + def.EffectText(level + 1), room, FontSize.Small),
                    new Rectangle(rect.X, rect.Y + 41, rect.Width - 9, 16),
                    Theme.TextFaint, FontSize.Small, Align.Right);
            }

            if (ui.Hovering(rect) && !ui.ClickConsumed)
                ui.SetTooltip(def.Description, rect);

            if (hovered && ui.MousePressed)
            {
                ui.ClickConsumed = true;
                bought = def.Id;
            }

            y += CardHeight + CardGap;
        }

        return bought;
    }

    /// <summary>Lifetime counters. Returns the y to carry on drawing from.</summary>
    private static int DrawStats(Ui ui, GameState state, Rectangle body)
    {
        var y = body.Y;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Lifetime",
                   Money.Cash(state.TotalEarned), Theme.Money);
        y += 17;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Customers",
                   state.Customers.ToString());
        y += 17;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Slots owned",
                   state.SlotsOwned.ToString());
        y += 17;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Bottles sold",
                   state.TotalCansSold.ToString());
        y += 17;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Bottles in stock",
                   $"{state.TotalStock} / {state.SlotsOwned * state.SlotCapacity}");
        y += 17;

        ui.StatRow(new Rectangle(body.X, y, body.Width, 16), "Crate tokens",
                   $"{Money.Format(state.Tokens)} / {Money.Format((long)Math.Ceiling(state.NextPackCost))}",
                   state.CanOpenPack ? Theme.Money : null);
        y += 22;

        ui.Separator(body.X, y, body.Width);
        return y + 9;
    }
}
