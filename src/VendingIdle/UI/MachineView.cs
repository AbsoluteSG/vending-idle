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
/// How the bottles of one slot are packed into its cell: a grid of squares, one
/// square per bottle the slot can hold, filled from the bottom-left up.
/// </summary>
public readonly struct BottleGrid
{
    public Rectangle Area { get; init; }
    public int Columns { get; init; }
    public int Size { get; init; }
    public int Gap { get; init; }

    /// <summary>
    /// Rectangle of the nth bottle, counting from the bottom-left. Bottles stack
    /// upward so the one that leaves on the next click is always the top one.
    /// </summary>
    public Rectangle BottleAt(int n)
    {
        if (Columns <= 0) return Rectangle.Empty;

        var col = n % Columns;
        var row = n / Columns;
        var stride = Size + Gap;

        return new Rectangle(
            Area.X + col * stride,
            Area.Bottom - Size - row * stride,
            Size, Size);
    }
}

/// <summary>
/// The machine: a centred stage holding a grid of slots, each slot a rack of
/// bottle squares. Row 0 is the bottom row and the rack grows upward without
/// limit, scrolling with the wheel once it outruns the window.
/// </summary>
public sealed class MachineView
{
    private const int CellPad = 7;
    private const int CellHeight = 96;
    private const int HeaderHeight = 38;
    private const int TrayHeight = 92;
    private const int MaxBottleSize = 15;

    public int SelectedSlot { get; set; }
    public float Scroll { get; private set; }

    private Rectangle _glass;
    private Rectangle _tray;
    private readonly Dictionary<int, Rectangle> _cellRects = new();
    private readonly Dictionary<int, BottleGrid> _bottleGrids = new();

    public Rectangle TrayRect => _tray;
    public Rectangle GlassRect => _glass;

    /// <summary>Where a dropped bottle comes to rest.</summary>
    public float TrayFloorY => _tray.Bottom - 14;

    public bool TryGetCellRect(int index, out Rectangle rect) =>
        _cellRects.TryGetValue(index, out rect);

    /// <summary>
    /// Screen rectangle of a specific bottle in a slot, so a dispensed bottle can
    /// start falling from exactly where it was sitting.
    /// </summary>
    public bool TryGetBottleRect(int slotIndex, int bottleIndex, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!_bottleGrids.TryGetValue(slotIndex, out var grid)) return false;

        rect = grid.BottleAt(bottleIndex);
        return rect != Rectangle.Empty;
    }

    public MachineAction Draw(Ui ui, GameState state, Rectangle bounds, Effects fx, double now,
                              string? hint = null)
    {
        var action = MachineAction.None;

        // ---- Shell --------------------------------------------------------
        ui.P.FillRounded(ui.Sb, bounds, 14, Theme.MachineShell);
        ui.P.OutlineRounded(ui.Sb, bounds, 14, Theme.PanelEdge);

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
            TrayHeight - 14);

        // ---- Rack ---------------------------------------------------------
        var rows = state.RowCount;
        var contentHeight = rows * (CellHeight + CellPad) + CellPad;
        var maxScroll = Math.Max(0f, contentHeight - _glass.Height);

        if (ui.Hovering(_glass) && ui.WheelDelta != 0)
            Scroll += ui.WheelDelta * 0.35f;

        Scroll = MathHelper.Clamp(Scroll, 0f, maxScroll);

        var columns = Balance.Columns;
        var cellWidth = (_glass.Width - CellPad * (columns + 1)) / columns;

        _cellRects.Clear();
        _bottleGrids.Clear();
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

        if (Scroll < maxScroll - 0.5f)
            ui.T.DrawIn(ui.Sb, "more above",
                new Rectangle(_glass.X, _glass.Y + 2, _glass.Width, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);

        if (Scroll > 0.5f)
            ui.T.DrawIn(ui.Sb, "more below",
                new Rectangle(_glass.X, _glass.Bottom - 16, _glass.Width, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);

        DrawTray(ui, state, fx, ref action);

        return action;
    }

    /// <summary>
    /// Packs <paramref name="capacity"/> squares into the area as large as they
    /// will go, by trying every column count and keeping the best.
    /// </summary>
    private static BottleGrid ComputeGrid(Rectangle area, int capacity)
    {
        if (capacity <= 0 || area.Width <= 0 || area.Height <= 0)
            return default;

        var bestSize = 0;
        var bestColumns = 1;
        var gap = 2;

        for (var columns = 1; columns <= capacity; columns++)
        {
            var rows = (capacity + columns - 1) / columns;

            var w = (area.Width - gap * (columns - 1)) / (float)columns;
            var h = (area.Height - gap * (rows - 1)) / (float)rows;
            var size = (int)MathF.Floor(MathF.Min(w, h));

            if (size > bestSize)
            {
                bestSize = size;
                bestColumns = columns;
            }
        }

        // Very high capacities squeeze the squares to nothing; a 1px bottle with a
        // 2px gap is worse than a 1px bottle flush against its neighbour.
        if (bestSize < 3) gap = 1;
        bestSize = Math.Clamp(bestSize, 1, MaxBottleSize);

        var stride = bestSize + gap;
        var usedWidth = bestColumns * stride - gap;

        return new BottleGrid
        {
            // Centre the rack horizontally; it hangs from the bottom of the area.
            Area = new Rectangle(area.X + (area.Width - usedWidth) / 2, area.Y,
                                 usedWidth, area.Height),
            Columns = bestColumns,
            Size = bestSize,
            Gap = gap
        };
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
                    new Rectangle(rect.X, rect.Y + 26, rect.Width, 18),
                    Theme.TextDim, FontSize.Small, Align.Center);

                ui.T.DrawIn(ui.Sb, Money.Cash(state.NextSlotCost),
                    new Rectangle(rect.X, rect.Y + 46, rect.Width, 20),
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

            // One square per bottle. The rack is the readout -- no bar needed.
            var rackArea = new Rectangle(rect.X + 5, rect.Y + 5, rect.Width - 10, rect.Height - 23);
            var grid = ComputeGrid(rackArea, capacity);
            _bottleGrids[slot.Index] = grid;

            var emptyColor = Theme.MachineShellDark;
            for (var i = 0; i < capacity; i++)
            {
                var bottle = grid.BottleAt(i);
                if (bottle.Width <= 0) break;

                if (i < slot.Stock)
                {
                    ui.P.Fill(ui.Sb, bottle, color);

                    // A lighter cap reads as a bottle neck once squares are big enough.
                    if (grid.Size >= 7)
                        ui.P.Fill(ui.Sb,
                            new Rectangle(bottle.X, bottle.Y, bottle.Width, 2),
                            Color.Lerp(color, Color.White, 0.45f));
                }
                else
                {
                    ui.P.Fill(ui.Sb, bottle, emptyColor);
                }
            }

            // The squares and their colour already say what is loaded and how
            // much; the strip underneath only needs the count. The name lives in
            // the hover tooltip so nothing has to be truncated to "Cola Clas...".
            var label = slot.Stock == 0 ? "empty" : $"{slot.Stock}/{capacity}";

            ui.T.DrawIn(ui.Sb, label,
                new Rectangle(rect.X, rect.Bottom - 18, rect.Width, 15),
                slot.Stock == 0 ? Theme.Negative : Color.Lerp(color, Color.White, 0.35f),
                FontSize.Small, Align.Center);

            if (hovered)
                ui.SetTooltip($"{drink.Name} - {Money.Cash(drink.Value * state.ClickValueMultiplier)} each",
                              rect);
        }

        if (slot.HasAutoRestocker)
        {
            var pulse = 0.6f + 0.4f * (float)Math.Sin(now * 3.0);
            ui.P.FillRounded(ui.Sb, new Rectangle(rect.Right - 11, rect.Y + 5, 6, 6), 3,
                             Theme.Accent * pulse);
        }

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.SelectSlot = slot.Index;
        }
    }

    private void DrawTray(Ui ui, GameState state, Effects fx, ref MachineAction action)
    {
        var hovered = !ui.ClickConsumed && ui.Hovering(_tray);

        ui.P.FillRounded(ui.Sb, _tray, 8, hovered ? Theme.PanelAlt : Theme.Tray);

        if (fx.TrayFlash > 0f)
            ui.P.FillRounded(ui.Sb, _tray, 8, Theme.Accent * (fx.TrayFlash * 0.25f));

        ui.P.OutlineRounded(ui.Sb, _tray, 8, Theme.PanelEdge);

        var label = state.TotalStock > 0 ? "PUSH TO VEND" : "SHAKE FOR CHANGE";
        ui.T.DrawIn(ui.Sb, label,
            new Rectangle(_tray.X, _tray.Y + 6, _tray.Width, 20),
            Theme.Text, FontSize.Normal, Align.Center);

        ui.T.DrawIn(ui.Sb, "click here or press Space",
            new Rectangle(_tray.X, _tray.Y + 26, _tray.Width, 14),
            Theme.TextFaint, FontSize.Small, Align.Center);

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.Vend = true;
        }
    }
}
