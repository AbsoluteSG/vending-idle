using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>What the player did to the machine this frame (at most one thing).</summary>
public struct MachineAction
{
    public bool Shake;
    public bool RestockAll;
    public bool Save;
    public int BuySlot;
    public int SelectSlot;

    public static MachineAction None => new() { BuySlot = -1, SelectSlot = -1 };
}

/// <summary>
/// A compartment holds one drink, not a row of them. <see cref="Front"/> is the
/// placeholder rectangle a drink sprite will occupy; <see cref="StackLayers"/> is
/// how many silhouettes sit behind it, a stacked-deck cue for how full the slot is.
/// The exact figure is the printed number, not something you count.
/// </summary>
public readonly struct DrinkDisplay
{
    public Rectangle Front { get; init; }
    public int StackLayers { get; init; }

    /// <summary>Each layer sits up and back from the one in front of it.</summary>
    public static readonly Point LayerOffset = new(4, -4);

    public Rectangle Layer(int depth) =>
        new(Front.X + LayerOffset.X * depth,
            Front.Y + LayerOffset.Y * depth,
            Front.Width, Front.Height);
}

/// <summary>
/// The cabinet: a physical object standing in the room, not a UI panel.
///
/// It is built in three parts, because it grows. The <b>base</b> is a fixed height
/// bolted to the floor and holds everything you need constantly -- the till
/// readout, the service controls and the delivery tray. The <b>body</b> is the
/// glass and the service column, and it is the part that stretches: every row of
/// compartments makes the machine taller. The <b>crown</b> caps it off with the
/// branding, and on a tall machine it ends up far above the top of the screen,
/// which is the point. You scroll up to look at what you have built.
///
/// Nothing here scrolls internally. The compartments are laid out at their true
/// height above the floor and the camera pans to reach them, so a bottle from the
/// top row genuinely falls the whole way down into the tray.
/// </summary>
public sealed class MachineView
{
    private const int Bezel = 12;
    private const int Gap = 6;

    private const int CrownHeight = 58;
    private const int TillHeight = 44;
    private const int TrayHeight = 78;
    private const int PlinthHeight = 18;
    private const int ColumnWidth = 122;

    // Sized so that Balance.DefaultRows rows fill the shipped cabinet:
    // 4 * (86 + 7) + 7 = 379.
    private const int CellPad = 7;
    private const int CellHeight = 86;

    private const int DrinkWidth = 32;
    private const int DrinkHeight = 46;
    private const int MaxStackLayers = 4;

    /// <summary>Room the service column needs for its controls, stacked from the floor up.</summary>
    private const int ControlStackHeight = 196;

    public int SelectedSlot { get; set; }

    private Rectangle _glass;
    private Rectangle _tray;
    private readonly Dictionary<int, Rectangle> _cellRects = new();
    private readonly Dictionary<int, DrinkDisplay> _displays = new();

    public Rectangle TrayRect => _tray;
    public Rectangle GlassRect => _glass;

    /// <summary>Where a dropped drink comes to rest.</summary>
    public float TrayFloorY => _tray.Bottom - 16;

    public static int GlassHeightFor(int rows) => rows * (CellHeight + CellPad) + CellPad;

    /// <summary>
    /// Total height of a cabinet with this many rows. The caller uses it to place
    /// the machine's top edge relative to a fixed floor line, which is what makes
    /// the machine grow upward rather than the floor sink.
    /// </summary>
    public static int HeightFor(int rows) =>
        Bezel + CrownHeight + Gap + GlassHeightFor(rows) + Gap
        + TillHeight + Gap + TrayHeight + PlinthHeight;

    public bool TryGetCellRect(int index, out Rectangle rect) =>
        _cellRects.TryGetValue(index, out rect);

    /// <summary>
    /// Where the nth drink dispensed this click was sitting. A double drop takes
    /// the front drink and the one stacked behind it.
    /// </summary>
    public bool TryGetDispensedBottle(int slotIndex, int nth, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!_displays.TryGetValue(slotIndex, out var display)) return false;

        rect = display.Layer(Math.Min(nth, display.StackLayers));
        return rect != Rectangle.Empty;
    }

    public MachineAction Draw(Ui ui, GameState state, Rectangle bounds, Effects fx, double now,
                              double incomePerSecond)
    {
        var action = MachineAction.None;

        DrawChassis(ui, bounds);

        var innerWidth = bounds.Width - Bezel * 2;

        var crown = new Rectangle(bounds.X + Bezel, bounds.Y + Bezel, innerWidth, CrownHeight);
        DrawCrown(ui, crown);

        var glassHeight = GlassHeightFor(state.RowCount);

        _glass = new Rectangle(bounds.X + Bezel, crown.Bottom + Gap,
                               innerWidth - ColumnWidth - Gap, glassHeight);

        var column = new Rectangle(_glass.Right + Gap, _glass.Y, ColumnWidth, glassHeight);

        var till = new Rectangle(bounds.X + Bezel, _glass.Bottom + Gap, innerWidth, TillHeight);
        _tray = new Rectangle(bounds.X + Bezel, till.Bottom + Gap, innerWidth, TrayHeight);

        DrawShelves(ui, state, now, ref action);
        DrawGlassFront(ui);
        DrawServiceColumn(ui, state, column, now, ref action);
        DrawTill(ui, state, till, incomePerSecond);
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

    /// <summary>
    /// The branding, at the very top of the cabinet. On a machine of any height
    /// this is off-screen until you pan up to it -- it is the thing you have been
    /// building toward, not something you need to read.
    /// </summary>
    private static void DrawCrown(Ui ui, Rectangle crown)
    {
        ui.P.FillRounded(ui.Sb, crown, 6, Theme.ChassisDark);

        ui.T.DrawIn(ui.Sb, "VEND-O-MATIC",
            new Rectangle(crown.X, crown.Y + 6, crown.Width, 30),
            Theme.ChassisTrim, FontSize.Large, Align.Center);

        ui.T.DrawIn(ui.Sb, "SIPHON GAMES",
            new Rectangle(crown.X, crown.Y + 38, crown.Width, 14),
            Theme.ChassisLight, FontSize.Small, Align.Center);
    }

    /// <summary>
    /// The till, on the fascia just above the tray. It used to live up on the brand
    /// plate, which was fine on a fixed-height box and useless the moment the
    /// cabinet grew past the screen -- your money has to be where your eyes are.
    /// </summary>
    private static void DrawTill(Ui ui, GameState state, Rectangle till, double incomePerSecond)
    {
        ui.P.FillRounded(ui.Sb, till, 6, Theme.ChassisDark);

        var led = new Rectangle(till.X + 8, till.Y + 5, till.Width - 16, till.Height - 10);
        ui.P.FillRounded(ui.Sb, led, 4, Theme.Led);
        ui.P.OutlineRounded(ui.Sb, led, 4, Theme.ChassisDark);

        ui.T.DrawIn(ui.Sb, Money.Cash(state.Money), led,
            Theme.LedText, FontSize.Large, Align.Right, padX: 12);

        var potential = Simulation.PotentialIncomePerSecond(state);
        var starved = potential > 0 && incomePerSecond < potential * 0.6;

        var rate = potential > 0
            ? $"{Money.FormatRate(incomePerSecond)} of {Money.FormatRate(potential)}"
            : "no customers yet";

        ui.T.DrawIn(ui.Sb, rate, led,
            starved ? Theme.Negative : Theme.LedDim, FontSize.Small, Align.Left, padX: 12);
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

        var columns = Balance.Columns;
        var cellWidth = (_glass.Width - CellPad * (columns + 1)) / columns;

        _cellRects.Clear();
        _displays.Clear();
        ui.PushClip(_glass);

        var visible = ui.VisibleWorld;
        var mouseInGlass = _glass.Contains(ui.Mouse);

        for (var row = 0; row < state.RowCount; row++)
        {
            // Rows are placed at their true height above the tray. Nothing is
            // offset by a scroll value -- the camera does that job now.
            var y = _glass.Bottom - CellPad - (row + 1) * CellHeight - row * CellPad;

            // Cull against what the camera can actually see. A hundred-row machine
            // is mostly off-screen at any given moment.
            if (y > visible.Bottom || y + CellHeight < visible.Y) continue;

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
    }

    /// <summary>
    /// Places the single drink a compartment shows, and decides how deep the
    /// stack behind it looks. The stack is a fullness cue only -- it saturates
    /// well before capacity does, because the number carries the real figure.
    /// </summary>
    private static DrinkDisplay ComputeDisplay(Rectangle cell, int stock, int capacity)
    {
        var layers = 0;
        if (stock > 1 && capacity > 0)
        {
            var fill = stock / (double)capacity;
            layers = Math.Min(MaxStackLayers, (int)Math.Ceiling(fill * MaxStackLayers));
            layers = Math.Min(layers, stock - 1);
        }

        // Sit the front drink low enough that the stack has room to rise behind it.
        var stackRise = MaxStackLayers * -DrinkDisplay.LayerOffset.Y;
        var front = new Rectangle(
            cell.Center.X - DrinkWidth / 2 - MaxStackLayers * DrinkDisplay.LayerOffset.X / 2,
            cell.Y + stackRise - 2,
            DrinkWidth, DrinkHeight);

        return new DrinkDisplay { Front = front, StackLayers = layers };
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
                var ticket = new Rectangle(cell.Center.X - 38, cell.Y + 20, 76, 30);
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
            var display = ComputeDisplay(cell, slot.Stock, capacity);
            _displays[slot.Index] = display;

            var color = Theme.FromPacked(drink.Color);

            if (slot.Stock > 0)
            {
                // Stack behind the drink first, back to front, each layer sunk
                // further toward the colour of the unlit cabinet interior.
                for (var depth = display.StackLayers; depth >= 1; depth--)
                {
                    var shade = 0.30f + 0.14f * depth;
                    DrawStackLayer(ui, display.Layer(depth),
                                   Color.Lerp(color, Theme.Glass, shade));
                }

                DrawDrink(ui, display.Front, color);
            }

            var label = slot.Stock == 0 ? "sold out" : slot.Stock.ToString();

            ui.T.DrawIn(ui.Sb, label,
                new Rectangle(cell.X, cell.Bottom - 24, cell.Width, 18),
                slot.Stock == 0 ? Theme.Negative : Color.Lerp(color, Color.White, 0.5f),
                slot.Stock == 0 ? FontSize.Small : FontSize.Normal, Align.Center);

            if (hovered)
                ui.SetTooltip(
                    $"{drink.Name} - {slot.Stock}/{capacity} in stock, " +
                    $"{Money.Cash(drink.Value * state.ClickValueMultiplier)} each",
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
    /// The drink itself: the rectangle a sprite will eventually occupy, dressed
    /// with a cap, a label band and a highlight so it reads as product rather
    /// than a colour swatch.
    /// </summary>
    public static void DrawDrink(Ui ui, Rectangle rect, Color color)
    {
        if (rect.Width <= 0) return;

        var capWidth = Math.Max(6, rect.Width / 3);
        var cap = new Rectangle(rect.Center.X - capWidth / 2, rect.Y, capWidth, 5);
        ui.P.FillRounded(ui.Sb, cap, 2, Color.Lerp(color, Color.White, 0.55f));

        var body = new Rectangle(rect.X, rect.Y + 5, rect.Width, rect.Height - 5);
        ui.P.FillRounded(ui.Sb, body, 4, color);

        ui.P.Fill(ui.Sb,
            new Rectangle(body.X, body.Y + body.Height / 3, body.Width, body.Height / 3),
            Color.Lerp(color, Color.Black, 0.30f));

        ui.P.Fill(ui.Sb, new Rectangle(body.X + 3, body.Y + 4, 3, body.Height - 10),
                  Color.Lerp(color, Color.White, 0.40f));

        ui.P.OutlineRounded(ui.Sb, body, 4, Color.Lerp(color, Color.Black, 0.45f));
    }

    /// <summary>
    /// One of the drinks stacked behind the front one. Only its silhouette shows,
    /// so the stack reads as depth without competing with the drink in front.
    /// </summary>
    private static void DrawStackLayer(Ui ui, Rectangle rect, Color color)
    {
        if (rect.Width <= 0) return;

        var body = new Rectangle(rect.X, rect.Y + 5, rect.Width, rect.Height - 5);
        ui.P.FillRounded(ui.Sb, body, 4, color);
        ui.P.OutlineRounded(ui.Sb, body, 4, Color.Lerp(color, Color.Black, 0.35f));
    }

    // ---------------------------------------------------------------------
    // Service column and delivery flap
    // ---------------------------------------------------------------------

    /// <summary>
    /// The column runs the full height of the glass, but its controls are stacked
    /// up from the bottom rather than down from the top. On a tall machine that
    /// keeps RESTOCK and SAVE beside the tray where your hand already is, and
    /// leaves the rest of the column as cooling vents -- which is exactly what a
    /// twenty-foot vending machine ought to look like.
    /// </summary>
    private static void DrawServiceColumn(Ui ui, GameState state, Rectangle column, double now,
                                          ref MachineAction action)
    {
        ui.P.FillRounded(ui.Sb, column, 6, Theme.ChassisDark);
        ui.P.OutlineRounded(ui.Sb, column, 6, Theme.ChassisTrim);

        // Status lamp, low on the fascia.
        var lampY = column.Bottom - 26;
        var lit = state.TotalStock > 0;
        var pulse = lit ? 0.55f + 0.45f * (float)Math.Sin(now * 2.0) : 1f;

        ui.P.FillRounded(ui.Sb, new Rectangle(column.X + 12, lampY, 8, 8), 4,
                         (lit ? Theme.Positive : Theme.Negative) * pulse);

        ui.T.Draw(ui.Sb, lit ? "READY" : "EMPTY",
                  new Vector2(column.X + 26, lampY - 3),
                  lit ? Theme.ChassisLight : Theme.Negative, FontSize.Small);

        var save = new Rectangle(column.X + 8, column.Bottom - 56, column.Width - 16, 24);
        if (ui.Button(save, "SAVE", true, ButtonStyle.Subtle, "Save now", FontSize.Small))
            action.Save = true;

        var restock = new Rectangle(column.X + 8, column.Bottom - 90, column.Width - 16, 28);
        if (ui.Button(restock, "RESTOCK", CanRestockAnything(state), ButtonStyle.Buy,
                      "Fill every loaded slot as far as your money goes", FontSize.Small))
            action.RestockAll = true;

        // Keypad. Decorative, but it is most of what makes the column read as a
        // machine front rather than a sidebar.
        const int keyW = 22;
        const int keyH = 16;
        var padX = column.X + (column.Width - (keyW * 3 + 8)) / 2;
        var padY = column.Bottom - 156;

        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
        {
            var key = new Rectangle(padX + c * (keyW + 4), padY + r * (keyH + 4), keyW, keyH);
            ui.P.FillRounded(ui.Sb, key, 3, Theme.Chassis);
            ui.P.Fill(ui.Sb, new Rectangle(key.X + 1, key.Y + 1, key.Width - 2, 1),
                      Theme.ChassisLight);
        }

        ui.T.DrawIn(ui.Sb, "INSERT COIN",
            new Rectangle(column.X, column.Bottom - 174, column.Width, 12), Theme.ChassisLight,
            FontSize.Small, Align.Center);

        var coin = new Rectangle(column.X + 22, column.Bottom - 188, column.Width - 44, 8);
        ui.P.FillRounded(ui.Sb, coin, 3, Theme.Tray);
        ui.P.Fill(ui.Sb, new Rectangle(coin.X, coin.Y - 2, coin.Width, 2), Theme.ChassisTrim);

        // Cooling vents fill everything above the controls, however tall that is.
        var ventTop = column.Y + 8;
        var ventBottom = column.Bottom - ControlStackHeight;

        var visible = ui.VisibleWorld;
        for (var vy = ventTop; vy < ventBottom; vy += 7)
        {
            if (vy < visible.Y - 8 || vy > visible.Bottom + 8) continue;

            ui.P.FillRounded(ui.Sb, new Rectangle(column.X + 16, vy, column.Width - 32, 3), 1,
                             Theme.Chassis);
            ui.P.Fill(ui.Sb, new Rectangle(column.X + 16, vy + 3, column.Width - 32, 1),
                      Theme.ChassisDark);
        }
    }

    private static int CountStockedSlots(GameState state)
    {
        var count = 0;
        foreach (var slot in state.Slots)
            if (slot.CanDispense)
                count++;
        return count;
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

        ui.T.DrawIn(ui.Sb, "SHAKE", flap, Theme.Text, FontSize.Small, Align.Center);

        ui.P.OutlineRounded(ui.Sb, _tray, 6, Theme.ChassisDark);

        if (hovered)
        {
            var loaded = CountStockedSlots(state);
            ui.SetTooltip(
                loaded > 0
                    ? $"Shake out {state.ShakeBottlesPerSlot} bottle" +
                      (state.ShakeBottlesPerSlot == 1 ? "" : "s") +
                      $" from each of {loaded} stocked slot" + (loaded == 1 ? "" : "s")
                    : "Nothing loaded - shake the machine for spare change",
                _tray);
        }

        if (hovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            action.Shake = true;
        }
    }
}
