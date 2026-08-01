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
    public bool RestockAll;
    public bool Save;
    public int BuySlot;
    public int SelectSlot;

    public static MachineAction None => new() { BuySlot = -1, SelectSlot = -1 };
}

/// <summary>
/// A drink hanging in a compartment. Each one is a placeholder rectangle standing
/// in for a future drink sprite.
/// </summary>
public readonly struct HangingRack
{
    public Rectangle First { get; init; }
    public int Stride { get; init; }
    public int Positions { get; init; }
    public int Occupied { get; init; }

    public Rectangle At(int index) =>
        index < 0 || index >= Positions
            ? Rectangle.Empty
            : new Rectangle(First.X + index * Stride, First.Y, First.Width, First.Height);

    /// <summary>The next one to drop: the front of the row.</summary>
    public Rectangle Front => Occupied <= 0 ? Rectangle.Empty : At(Occupied - 1);
}

/// <summary>
/// The cabinet: a physical object standing in the room, not a UI panel. A branded
/// header with an LED till readout, a glass front with drinks hanging free in their
/// compartments, a service column down the side, and a delivery flap at the bottom.
/// </summary>
public sealed class MachineView
{
    private const int PlateHeight = 56;
    private const int TrayHeight = 78;
    private const int PlinthHeight = 18;
    private const int ColumnWidth = 122;
    private const int Bezel = 12;
    private const int Gap = 6;

    private const int CellPad = 7;
    private const int CellHeight = 84;

    private const int BottleWidth = 16;
    private const int BottleHeight = 38;
    private const int BottleGap = 5;

    public int SelectedSlot { get; set; }
    public float Scroll { get; private set; }

    private Rectangle _glass;
    private Rectangle _tray;
    private readonly Dictionary<int, Rectangle> _cellRects = new();
    private readonly Dictionary<int, HangingRack> _racks = new();

    public Rectangle TrayRect => _tray;
    public Rectangle GlassRect => _glass;

    /// <summary>Where a dropped drink comes to rest.</summary>
    public float TrayFloorY => _tray.Bottom - 16;

    public bool TryGetCellRect(int index, out Rectangle rect) =>
        _cellRects.TryGetValue(index, out rect);

    /// <summary>
    /// Where the nth drink hangs, so a dispensed one can start falling from
    /// exactly the hook it was on.
    /// </summary>
    public bool TryGetBottleRect(int slotIndex, int position, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!_racks.TryGetValue(slotIndex, out var rack)) return false;

        rect = rack.At(position);
        return rect != Rectangle.Empty;
    }

    /// <summary>
    /// Where the nth drink dispensed this click was hanging, counting back from
    /// the front of the row. Dispensing reads from the front rather than by stock
    /// index because a row shows proportional fill, not one hook per bottle.
    /// </summary>
    public bool TryGetDispensedBottle(int slotIndex, int nth, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!_racks.TryGetValue(slotIndex, out var rack)) return false;

        rect = rack.At(rack.Occupied - 1 - nth);
        return rect != Rectangle.Empty;
    }

    public MachineAction Draw(Ui ui, GameState state, Rectangle bounds, Effects fx, double now,
                              double incomePerSecond)
    {
        var action = MachineAction.None;

        DrawChassis(ui, bounds);

        var plate = new Rectangle(bounds.X + Bezel, bounds.Y + Bezel,
                                  bounds.Width - Bezel * 2, PlateHeight);
        DrawBrandPlate(ui, state, plate, incomePerSecond);

        _glass = new Rectangle(
            bounds.X + Bezel,
            plate.Bottom + Gap,
            bounds.Width - Bezel * 2 - ColumnWidth - Gap,
            bounds.Height - Bezel - PlateHeight - Gap - TrayHeight - Gap - PlinthHeight);

        var column = new Rectangle(_glass.Right + Gap, _glass.Y, ColumnWidth, _glass.Height);

        _tray = new Rectangle(_glass.X, _glass.Bottom + Gap, _glass.Width, TrayHeight);

        DrawShelves(ui, state, now, ref action);
        DrawGlassFront(ui);
        DrawServiceColumn(ui, state, column, now, ref action);
        DrawDeliveryFlap(ui, state, fx, ref action);

        return action;
    }

    // ---------------------------------------------------------------------
    // Cabinet
    // ---------------------------------------------------------------------

    private static void DrawChassis(Ui ui, Rectangle bounds)
    {
        ui.P.FillRounded(ui.Sb, bounds, 12, Theme.Chassis);

        // Bevels: a lit top edge and a shaded base give the box some depth.
        ui.P.Fill(ui.Sb, new Rectangle(bounds.X + 12, bounds.Y + 2, bounds.Width - 24, 2),
                  Theme.ChassisLight);

        var plinth = new Rectangle(bounds.X + 4, bounds.Bottom - PlinthHeight,
                                   bounds.Width - 8, PlinthHeight);
        ui.P.FillRounded(ui.Sb, plinth, 6, Theme.ChassisDark);

        ui.P.OutlineRounded(ui.Sb, bounds, 12, Theme.ChassisTrim);
    }

    private static void DrawBrandPlate(Ui ui, GameState state, Rectangle plate, double incomePerSecond)
    {
        ui.P.FillRounded(ui.Sb, plate, 6, Theme.ChassisDark);

        ui.T.Draw(ui.Sb, "VEND-O-MATIC", new Vector2(plate.X + 14, plate.Y + 10),
                  Theme.ChassisTrim, FontSize.Normal);

        ui.T.Draw(ui.Sb, "SIPHON GAMES", new Vector2(plate.X + 14, plate.Y + 31),
                  Theme.ChassisLight, FontSize.Small);

        // The till readout is part of the machine rather than a HUD element.
        var led = new Rectangle(plate.Right - 246, plate.Y + 5, 236, plate.Height - 10);
        ui.P.FillRounded(ui.Sb, led, 4, Theme.Led);
        ui.P.OutlineRounded(ui.Sb, led, 4, Theme.ChassisDark);

        ui.T.DrawIn(ui.Sb, Money.Cash(state.Money),
            new Rectangle(led.X, led.Y + 2, led.Width, 26),
            Theme.LedText, FontSize.Large, Align.Right, padX: 10);

        var potential = Simulation.PotentialIncomePerSecond(state);
        var starved = potential > 0 && incomePerSecond < potential * 0.6;

        var rate = potential > 0
            ? $"{Money.FormatRate(incomePerSecond)} of {Money.FormatRate(potential)}"
            : "no customers yet";

        ui.T.DrawIn(ui.Sb, rate,
            new Rectangle(led.X, led.Bottom - 18, led.Width, 14),
            starved ? Theme.Negative : Theme.LedDim, FontSize.Small, Align.Right, padX: 10);
    }

    /// <summary>Glazing drawn over the stock: an edge, and a diagonal sheen.</summary>
    private void DrawGlassFront(Ui ui)
    {
        var sheen = new Rectangle(_glass.X + 18, _glass.Y, 54, _glass.Height);
        ui.P.Fill(ui.Sb, sheen, Theme.GlassSheen * 0.035f);

        var sheen2 = new Rectangle(_glass.X + 84, _glass.Y, 20, _glass.Height);
        ui.P.Fill(ui.Sb, sheen2, Theme.GlassSheen * 0.025f);

        ui.P.OutlineRounded(ui.Sb, _glass, 6, Theme.GlassEdge, 2);
    }

    // ---------------------------------------------------------------------
    // Stock
    // ---------------------------------------------------------------------

    private void DrawShelves(Ui ui, GameState state, double now, ref MachineAction action)
    {
        ui.P.FillRounded(ui.Sb, _glass, 6, Theme.Glass);

        var rows = state.RowCount;
        var contentHeight = rows * (CellHeight + CellPad) + CellPad;
        var maxScroll = Math.Max(0f, contentHeight - _glass.Height);

        if (ui.Hovering(_glass) && ui.WheelDelta != 0)
            Scroll += ui.WheelDelta * 0.35f;

        Scroll = MathHelper.Clamp(Scroll, 0f, maxScroll);

        var columns = Balance.Columns;
        var cellWidth = (_glass.Width - CellPad * (columns + 1)) / columns;

        _cellRects.Clear();
        _racks.Clear();
        ui.PushClip(_glass);

        var mouseInGlass = _glass.Contains(ui.Mouse);

        for (var row = 0; row < rows; row++)
        {
            var y = _glass.Bottom - CellPad - (row + 1) * CellHeight - row * CellPad + (int)Scroll;

            if (y > _glass.Bottom || y + CellHeight < _glass.Y) continue;

            // The shelf the row's drinks stand on, spanning the whole cabinet.
            var shelfY = y + CellHeight - 3;
            ui.P.Fill(ui.Sb, new Rectangle(_glass.X + 4, shelfY, _glass.Width - 8, 3), Theme.Shelf);
            ui.P.Fill(ui.Sb, new Rectangle(_glass.X + 4, shelfY + 3, _glass.Width - 8, 4),
                      Theme.ShelfShade);

            for (var col = 0; col < columns; col++)
            {
                var index = row * columns + col;
                var slot = state.SlotAt(index);
                if (slot is null) continue;

                var rect = new Rectangle(
                    _glass.X + CellPad + col * (cellWidth + CellPad),
                    y, cellWidth, CellHeight);

                _cellRects[index] = rect;
                DrawCompartment(ui, state, slot, rect, mouseInGlass, now, ref action);
            }
        }

        ui.PopClip();

        if (Scroll < maxScroll - 0.5f)
            ui.T.DrawIn(ui.Sb, "more above",
                new Rectangle(_glass.X, _glass.Y + 3, _glass.Width, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);

        if (Scroll > 0.5f)
            ui.T.DrawIn(ui.Sb, "more below",
                new Rectangle(_glass.X, _glass.Bottom - 17, _glass.Width, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);
    }

    /// <summary>
    /// Lays out the row of drinks hanging in a compartment. Up to capacity, one
    /// rectangle really is one bottle; past that the row shows proportional fill,
    /// because no compartment can legibly hang ninety of them.
    /// </summary>
    private static HangingRack ComputeRack(Rectangle cell, int stock, int capacity)
    {
        var innerWidth = cell.Width - 10;
        var stride = BottleWidth + BottleGap;
        var positions = Math.Max(1, (innerWidth + BottleGap) / stride);

        if (capacity > 0) positions = Math.Min(positions, capacity);

        var occupied = capacity <= 0
            ? 0
            : (int)Math.Round(stock / (double)capacity * positions);

        if (stock > 0 && occupied == 0) occupied = 1;
        occupied = Math.Min(occupied, positions);

        var used = positions * stride - BottleGap;
        var startX = cell.X + (cell.Width - used) / 2;

        return new HangingRack
        {
            First = new Rectangle(startX, cell.Y + 12, BottleWidth, BottleHeight),
            Stride = stride,
            Positions = positions,
            Occupied = occupied
        };
    }

    private void DrawCompartment(Ui ui, GameState state, Slot slot, Rectangle cell,
                                 bool mouseInGlass, double now, ref MachineAction action)
    {
        var hovered = mouseInGlass && !ui.ClickConsumed && cell.Contains(ui.Mouse);
        var selected = slot.Index == SelectedSlot;

        if (!slot.Unlocked)
        {
            var purchasable = state.IsSlotPurchasable(slot);
            var affordable = purchasable && state.Money >= state.NextSlotCost;

            // An unbought compartment is just dark, empty space behind the glass.
            ui.P.FillRounded(ui.Sb, cell, 4, Theme.ShelfShade * 0.7f);

            if (purchasable)
            {
                // A price ticket hanging where the drinks would be.
                var ticket = new Rectangle(cell.Center.X - 34, cell.Y + 22, 68, 30);
                ui.P.FillRounded(ui.Sb, ticket, 4,
                    hovered && affordable ? Theme.BuyHover : Theme.ShelfShade);
                ui.P.OutlineRounded(ui.Sb, ticket, 4,
                    affordable ? Theme.Money : Theme.Shelf);

                ui.T.DrawIn(ui.Sb, Money.Cash(state.NextSlotCost), ticket,
                    affordable ? Theme.Money : Theme.TextFaint, FontSize.Small, Align.Center);

                ui.T.DrawIn(ui.Sb, "empty slot",
                    new Rectangle(cell.X, cell.Bottom - 22, cell.Width, 14),
                    Theme.TextFaint, FontSize.Small, Align.Center);

                if (hovered && ui.MousePressed)
                {
                    ui.ClickConsumed = true;
                    action.BuySlot = slot.Index;
                }
            }

            return;
        }

        if (selected)
        {
            ui.P.FillRounded(ui.Sb, cell, 4, Theme.Accent * 0.12f);
            ui.P.OutlineRounded(ui.Sb, cell, 4, Theme.Accent);
        }
        else if (hovered)
        {
            ui.P.FillRounded(ui.Sb, cell, 4, Theme.GlassSheen * 0.05f);
        }

        var drink = slot.Drink;

        if (drink is null)
        {
            ui.T.DrawIn(ui.Sb, "no product", cell, Theme.TextFaint, FontSize.Small, Align.Center);
        }
        else
        {
            var capacity = state.SlotCapacity;
            var rack = ComputeRack(cell, slot.Stock, capacity);
            _racks[slot.Index] = rack;

            var color = Theme.FromPacked(drink.Color);

            // The dispensing coil the drinks hang from.
            ui.P.Fill(ui.Sb, new Rectangle(rack.First.X - 3, cell.Y + 8,
                                           rack.Positions * rack.Stride - BottleGap + 6, 2),
                      Theme.Shelf);

            for (var i = 0; i < rack.Occupied; i++)
                DrawBottle(ui, rack.At(i), color);

            var label = slot.Stock == 0 ? "sold out" : $"{slot.Stock}/{capacity}";

            ui.T.DrawIn(ui.Sb, label,
                new Rectangle(cell.X, cell.Bottom - 22, cell.Width, 14),
                slot.Stock == 0 ? Theme.Negative : Color.Lerp(color, Color.White, 0.4f),
                FontSize.Small, Align.Center);

            if (hovered)
                ui.SetTooltip(
                    $"{drink.Name} - {Money.Cash(drink.Value * state.ClickValueMultiplier)} each",
                    cell);
        }

        if (slot.HasAutoRestocker)
        {
            var pulse = 0.5f + 0.5f * (float)Math.Sin(now * 3.0);
            ui.P.FillRounded(ui.Sb, new Rectangle(cell.Right - 10, cell.Y + 4, 5, 5), 2,
                             Theme.Accent * pulse);
        }

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.SelectSlot = slot.Index;
        }
    }

    /// <summary>
    /// One drink. A plain rectangle standing in for a sprite, with a cap and a
    /// label band so it reads as a bottle rather than a swatch.
    /// </summary>
    private static void DrawBottle(Ui ui, Rectangle rect, Color color)
    {
        if (rect.Width <= 0) return;

        var capWidth = Math.Max(4, rect.Width / 2);
        var cap = new Rectangle(rect.Center.X - capWidth / 2, rect.Y, capWidth, 4);
        ui.P.Fill(ui.Sb, cap, Color.Lerp(color, Color.White, 0.55f));

        var body = new Rectangle(rect.X, rect.Y + 4, rect.Width, rect.Height - 4);
        ui.P.FillRounded(ui.Sb, body, 3, color);

        // Label band and a highlight down one side.
        ui.P.Fill(ui.Sb, new Rectangle(body.X, body.Y + body.Height / 3, body.Width, body.Height / 3),
                  Color.Lerp(color, Color.Black, 0.28f));

        ui.P.Fill(ui.Sb, new Rectangle(body.X + 2, body.Y + 3, 2, body.Height - 8),
                  Color.Lerp(color, Color.White, 0.35f));
    }

    // ---------------------------------------------------------------------
    // Service column and delivery flap
    // ---------------------------------------------------------------------

    private static void DrawServiceColumn(Ui ui, GameState state, Rectangle column, double now,
                                          ref MachineAction action)
    {
        ui.P.FillRounded(ui.Sb, column, 6, Theme.ChassisDark);
        ui.P.OutlineRounded(ui.Sb, column, 6, Theme.ChassisTrim);

        var y = column.Y + 10;

        // Coin slot.
        var coin = new Rectangle(column.X + 22, y, column.Width - 44, 8);
        ui.P.FillRounded(ui.Sb, coin, 3, Theme.Tray);
        ui.P.Fill(ui.Sb, new Rectangle(coin.X, coin.Y - 2, coin.Width, 2), Theme.ChassisTrim);
        y += 20;

        ui.T.DrawIn(ui.Sb, "INSERT COIN",
            new Rectangle(column.X, y, column.Width, 12), Theme.ChassisLight,
            FontSize.Small, Align.Center);
        y += 20;

        // Keypad. Decorative, but it is most of what makes the column read as a
        // machine front rather than a sidebar.
        const int keyW = 22;
        const int keyH = 16;
        var padX = column.X + (column.Width - (keyW * 3 + 8)) / 2;

        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
        {
            var key = new Rectangle(padX + c * (keyW + 4), y + r * (keyH + 4), keyW, keyH);
            ui.P.FillRounded(ui.Sb, key, 3, Theme.Chassis);
            ui.P.Fill(ui.Sb, new Rectangle(key.X + 1, key.Y + 1, key.Width - 2, 1),
                      Theme.ChassisLight);
        }

        y += 3 * (keyH + 4) + 14;

        // Service buttons, styled as part of the fascia.
        var restock = new Rectangle(column.X + 8, y, column.Width - 16, 28);
        if (ui.Button(restock, "RESTOCK", CanRestockAnything(state), ButtonStyle.Buy,
                      "Fill every loaded slot as far as your money goes", FontSize.Small))
            action.RestockAll = true;

        y += 34;

        var save = new Rectangle(column.X + 8, y, column.Width - 16, 24);
        if (ui.Button(save, "SAVE", true, ButtonStyle.Subtle, "Save now", FontSize.Small))
            action.Save = true;

        // Cooling vent: pure texture, but empty fascia reads as an unfinished panel.
        var ventY = y + 34;
        var ventBottom = column.Bottom - 34;
        for (var vy = ventY; vy < ventBottom; vy += 7)
        {
            ui.P.FillRounded(ui.Sb, new Rectangle(column.X + 16, vy, column.Width - 32, 3), 1,
                             Theme.Chassis);
            ui.P.Fill(ui.Sb, new Rectangle(column.X + 16, vy + 3, column.Width - 32, 1),
                      Theme.ChassisDark);
        }

        // Status lamp, low on the fascia.
        var lampY = column.Bottom - 26;
        var lit = state.TotalStock > 0;
        var pulse = lit ? 0.55f + 0.45f * (float)Math.Sin(now * 2.0) : 1f;

        ui.P.FillRounded(ui.Sb, new Rectangle(column.X + 12, lampY, 8, 8), 4,
                         (lit ? Theme.Positive : Theme.Negative) * pulse);

        ui.T.Draw(ui.Sb, lit ? "READY" : "EMPTY",
                  new Vector2(column.X + 26, lampY - 3),
                  lit ? Theme.ChassisLight : Theme.Negative, FontSize.Small);
    }

    private static bool CanRestockAnything(GameState state)
    {
        foreach (var slot in state.Slots)
        {
            if (!slot.Unlocked || slot.Drink is null) continue;
            if (state.RoomIn(slot) <= 0) continue;
            if (state.Money >= state.UnitCost(slot)) return true;
        }

        return false;
    }

    private void DrawDeliveryFlap(Ui ui, GameState state, Effects fx, ref MachineAction action)
    {
        var hovered = !ui.ClickConsumed && ui.Hovering(_tray);

        // The recess drinks drop into.
        ui.P.FillRounded(ui.Sb, _tray, 6, Theme.Tray);

        if (fx.TrayFlash > 0f)
            ui.P.FillRounded(ui.Sb, _tray, 6, Theme.Accent * (fx.TrayFlash * 0.22f));

        // Hinged flap across the opening, lifted slightly when you hover it.
        // Lip shadow so the opening reads as a recess cut into the fascia.
        ui.P.Fill(ui.Sb, new Rectangle(_tray.X + 4, _tray.Y, _tray.Width - 8, 3),
                  Theme.ChassisDark);

        var flapHeight = 28;
        var flap = new Rectangle(_tray.X + 10, _tray.Y + (hovered ? 5 : 9),
                                 _tray.Width - 20, flapHeight);

        ui.P.FillRounded(ui.Sb, flap, 4, hovered ? Theme.ChassisLight : Theme.Chassis);
        ui.P.Fill(ui.Sb, new Rectangle(flap.X + 6, flap.Y + 2, flap.Width - 12, 1),
                  Theme.ChassisTrim);

        ui.T.DrawIn(ui.Sb, state.TotalStock > 0 ? "PUSH" : "SHAKE", flap,
                    Theme.Text, FontSize.Small, Align.Center);

        ui.P.OutlineRounded(ui.Sb, _tray, 6, Theme.ChassisDark);

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.Vend = true;
        }
    }
}
