using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>The global upgrade list. Returns the upgrade clicked this frame, if any.</summary>
public static class UpgradePanel
{
    private const int CardHeight = 74;
    private const int CardGap = 6;

    public static UpgradeId? Draw(Ui ui, GameState state, Rectangle bounds)
    {
        UpgradeId? bought = null;

        ui.Panel(bounds, "UPGRADES");
        var body = Ui.PanelBody(bounds);

        var y = body.Y;

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

            // Name and level
            ui.T.Draw(ui.Sb, ui.T.Fit(def.Name, rect.Width - 60, FontSize.Small),
                      new Vector2(rect.X + 10, rect.Y + 6), Theme.Text, FontSize.Small);

            ui.T.DrawIn(ui.Sb, maxed ? "MAX" : $"Lv {level}",
                new Rectangle(rect.X, rect.Y + 6, rect.Width - 10, 16),
                maxed ? Theme.Accent : Theme.TextDim, FontSize.Small, Align.Right);

            // Current effect
            ui.T.Draw(ui.Sb, ui.T.Fit(def.EffectText(level), rect.Width - 20, FontSize.Small),
                      new Vector2(rect.X + 10, rect.Y + 26), Theme.TextDim, FontSize.Small);

            // Price
            if (maxed)
            {
                ui.T.Draw(ui.Sb, "fully upgraded",
                          new Vector2(rect.X + 10, rect.Y + 48), Theme.TextFaint, FontSize.Small);
            }
            else
            {
                ui.T.Draw(ui.Sb, Money.Cash(cost),
                          new Vector2(rect.X + 10, rect.Y + 48),
                          affordable ? Theme.Money : Theme.TextFaint);

                ui.T.DrawIn(ui.Sb, "→ " + def.EffectText(level + 1),
                    new Rectangle(rect.X, rect.Y + 50, rect.Width - 10, 16),
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
}
