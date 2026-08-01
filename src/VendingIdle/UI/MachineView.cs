using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>What the player did to the machine this frame (at most one thing).</summary>
public struct MachineAction
{
    public bool Vend;
    public int BuySlot;
    public int SelectSlot;

    public static MachineAction None => new() { BuySlot = -1, SelectSlot = -1 };
}

/// <summary>
/// Draws the machine and its stock grid. Row 0 is the bottom row; the grid grows
/// upward without limit and scrolls with the wheel once it outruns the window.
/// </summary>
public sealed class MachineView
{
    private const int CellPad = 8;
    private const int CellHeight = 88;
    private const int HeaderHeight = 40;
    private const int TrayHeight = 84;

    public int SelectedSlot { get; set; }
    public float Scroll { get; private set; }

    private Rectangle _glass;
    private Rectangle _tray;
    private readonly Dictionary<int, Rectangle> _cellRects = new();

    /// <summary>Screen position of a slot, for spawning effects. False if off-screen.</summary>
    public bool TryGetCellRect(int index, out Rectangle rect) =>
        _cellRects.TryGetValue(index, out rect);

    public Rectangle TrayRect => _tray;

    /// <summary>The stock area behind the glass. Empty space here is where hints go.</summary>
    public Rectangle GlassRect => _glass;

    /// <summary>Where cans land: the floor of the dispense tray.</summary>
    public float TrayFloorY => _tray.Bottom - 18;

    public MachineAction Draw(Ui ui, GameState state, Rectangle bounds, Effects fx, double now,
                              string? hint = null)
    {
        var action = MachineAction.None;

        // ---- Shell --------------------------------------------------------
        ui.P.FillRounded(ui.Sb, bounds, 12, Theme.MachineShell);
        ui.P.OutlineRounded(ui.Sb, bounds, 12, Theme.PanelEdge);

        // The brand plate doubles as the hint line. It is the one strip of the
        // machine the grid never grows into, so a hint can never collide with a slot.
        var header = new Rectangle(bounds.X, bounds.Y, bounds.Width, HeaderHeight);
        ui.T.DrawIn(ui.Sb, hint ?? "VEND-O-MATIC", header,
                    hint is null ? Theme.TextDim : Theme.Accent,
                    FontSize.Normal, Align.Center);

        _glass = new Rectangle(
            bounds.X + 12,
            bounds.Y + HeaderHeight,
            bounds.Width - 24,
            bounds.Height - HeaderHeight - TrayHeight - 12);

        ui.P.FillRounded(ui.Sb, _glass, 8, Theme.MachineShellDark);

        _tray = new Rectangle(
            bounds.X + 12,
            _glass.Bottom + 6,
            bounds.Width - 24,
            TrayHeight - 12);

        // ---- Grid ---------------------------------------------------------
        var rows = state.RowCount;
        var contentHeight = rows * (CellHeight + CellPad) + CellPad;
        var maxScroll = Math.Max(0f, contentHeight - _glass.Height);

        if (ui.Hovering(_glass) && ui.WheelDelta != 0)
            Scroll += ui.WheelDelta * 0.35f;

        Scroll = MathHelper.Clamp(Scroll, 0f, maxScroll);

        var columns = Balance.Columns;
        var cellWidth = (_glass.Width - CellPad * (columns + 1)) / columns;

        _cellRects.Clear();
        ui.PushClip(_glass);

        // The mouse only counts as being over a cell when it is inside the glass,
        // so clipped-off rows are not secretly clickable.
        var mouseInGlass = _glass.Contains(ui.Mouse);

        for (var row = 0; row < rows; row++)
        {
            var y = _glass.Bottom - CellPad - (row + 1) * CellHeight - row * CellPad + (int)Scroll;

            if (y > _glass.Bottom || y + CellHeight < _glass.Y) continue;

            for (var col = 0; col < columns; col++)
            {
                var index = row * columns + col;
                var slot = state.SlotAt(index);
                if (slot is null) continue;

                var rect = new Rectangle(
                    _glass.X + CellPad + col * (cellWidth + CellPad),
                    y, cellWidth, CellHeight);

                _cellRects[index] = rect;
                DrawCell(ui, state, slot, rect, mouseInGlass, now, ref action);
            }
        }

        ui.PopClip();

        // Scroll affordances, drawn outside the clip so they are never cut off.
        if (Scroll < maxScroll - 0.5f)
            ui.T.DrawIn(ui.Sb, "more above",
                new Rectangle(_glass.X, _glass.Y + 2, _glass.Width, 16),
                Theme.TextFaint, FontSize.Small, Align.Center);

        if (Scroll > 0.5f)
            ui.T.DrawIn(ui.Sb, "more below",
                new Rectangle(_glass.X, _glass.Bottom - 18, _glass.Width, 16),
                Theme.TextFaint, FontSize.Small, Align.Center);

        // ---- Tray / vend button -------------------------------------------
        DrawTray(ui, state, fx, ref action);

        return action;
    }

    private void DrawCell(Ui ui, GameState state, Slot slot, Rectangle rect,
                          bool mouseInGlass, double now, ref MachineAction action)
    {
        var hovered = mouseInGlass && !ui.ClickConsumed && rect.Contains(ui.Mouse);
        var selected = slot.Index == SelectedSlot;

        if (!slot.Unlocked)
        {
            var purchasable = state.IsSlotPurchasable(slot);
            var affordable = purchasable && state.Money >= state.NextSlotCost;

            var bg = purchasable
                ? (hovered && affordable ? Theme.BuyHover : Theme.SlotBuyable)
                : Theme.SlotLocked;

            ui.P.FillRounded(ui.Sb, rect, 6, bg);

            if (purchasable)
            {
                ui.T.DrawIn(ui.Sb, "Buy slot",
                    new Rectangle(rect.X, rect.Y + 20, rect.Width, 18),
                    Theme.TextDim, FontSize.Small, Align.Center);

                ui.T.DrawIn(ui.Sb, Money.Cash(state.NextSlotCost),
                    new Rectangle(rect.X, rect.Y + 40, rect.Width, 20),
                    affordable ? Theme.Money : Theme.TextFaint, FontSize.Normal, Align.Center);

                if (hovered && ui.MousePressed)
                {
                    ui.ClickConsumed = true;
                    action.BuySlot = slot.Index;
                }
            }
            else
            {
                ui.T.DrawIn(ui.Sb, "locked", rect, Theme.TextFaint, FontSize.Small, Align.Center);
            }

            return;
        }

        // ---- Unlocked slot ------------------------------------------------
        var drink = slot.Drink;
        ui.P.FillRounded(ui.Sb, rect, 6, hovered ? Theme.PanelAlt : Theme.SlotEmpty);

        if (selected)
            ui.P.OutlineRounded(ui.Sb, rect, 6, Theme.Accent, 2);

        if (drink is null)
        {
            ui.T.DrawIn(ui.Sb, "empty", rect, Theme.TextFaint, FontSize.Small, Align.Center);
        }
        else
        {
            var color = Theme.FromPacked(drink.Color);
            var capacity = state.SlotCapacity;

            // Stack of cans: a compact read of "how full is this coil".
            var canArea = new Rectangle(rect.X + 8, rect.Y + 8, rect.Width - 16, 34);
            DrawCanStack(ui, canArea, slot.Stock, capacity, color);

            ui.T.DrawIn(ui.Sb, ui.T.Fit(drink.Name, rect.Width - 10, FontSize.Small),
                new Rectangle(rect.X, rect.Y + 44, rect.Width, 16),
                Theme.Text, FontSize.Small, Align.Center);

            var barRect = new Rectangle(rect.X + 8, rect.Y + 64, rect.Width - 16, 6);
            var fraction = capacity > 0 ? slot.Stock / (float)capacity : 0f;
            var barColor = slot.Stock == 0 ? Theme.Negative
                         : fraction < 0.25f ? Theme.Money
                         : Theme.Positive;
            ui.ProgressBar(barRect, fraction, barColor);

            ui.T.DrawIn(ui.Sb, $"{slot.Stock}/{capacity}",
                new Rectangle(rect.X, rect.Y + 70, rect.Width, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);
        }

        if (slot.HasAutoRestocker)
        {
            // Pulsing dot marks an automated slot without stealing space.
            var pulse = 0.6f + 0.4f * (float)Math.Sin(now * 3.0);
            ui.P.FillRounded(ui.Sb, new Rectangle(rect.Right - 12, rect.Y + 6, 6, 6), 3,
                             Theme.Accent * pulse);
        }

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.SelectSlot = slot.Index;
        }
    }

    private static void DrawCanStack(Ui ui, Rectangle area, int stock, int capacity, Color color)
    {
        if (capacity <= 0) return;

        // Cap the drawn cans so a 60-capacity slot does not turn into mush.
        const int maxDrawn = 8;
        var drawn = Math.Min(maxDrawn, capacity);
        var filled = capacity == 0 ? 0 : (int)Math.Round(stock / (double)capacity * drawn);
        if (stock > 0 && filled == 0) filled = 1;

        var gap = 3;
        var canWidth = Math.Max(4, (area.Width - gap * (drawn - 1)) / drawn);
        var totalWidth = canWidth * drawn + gap * (drawn - 1);
        var x = area.X + (area.Width - totalWidth) / 2;

        for (var i = 0; i < drawn; i++)
        {
            var rect = new Rectangle(x + i * (canWidth + gap), area.Y, canWidth, area.Height);
            ui.P.FillRounded(ui.Sb, rect, 2, i < filled ? color : Theme.MachineShellDark);
        }
    }

    private void DrawTray(Ui ui, GameState state, Effects fx, ref MachineAction action)
    {
        var hovered = !ui.ClickConsumed && ui.Hovering(_tray);

        var baseColor = hovered ? Theme.PanelAlt : Theme.Tray;
        ui.P.FillRounded(ui.Sb, _tray, 8, baseColor);

        if (fx.TrayFlash > 0f)
            ui.P.FillRounded(ui.Sb, _tray, 8, Theme.Accent * (fx.TrayFlash * 0.25f));

        ui.P.OutlineRounded(ui.Sb, _tray, 8, Theme.PanelEdge);

        var label = state.TotalStock > 0 ? "PUSH TO VEND" : "SHAKE FOR CHANGE";
        ui.T.DrawIn(ui.Sb, label,
            new Rectangle(_tray.X, _tray.Y + 8, _tray.Width, 22),
            Theme.Text, FontSize.Normal, Align.Center);

        ui.T.DrawIn(ui.Sb, "click here or press Space",
            new Rectangle(_tray.X, _tray.Y + 30, _tray.Width, 16),
            Theme.TextFaint, FontSize.Small, Align.Center);

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.Vend = true;
        }
    }
}
