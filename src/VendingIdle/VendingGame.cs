using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using VendingIdle.Core;
using VendingIdle.Render;
using VendingIdle.UI;

namespace VendingIdle;

public sealed class VendingGame : Game, ISimEvents
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const int MachineWidth = 640;
    private const int MachineHeight = 556;
    private const int MachineTop = 62;
    private const int DrawerWidth = 286;

    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private readonly Random _rng = new();

    private SpriteBatch _sb = null!;
    private Primitives _prims = null!;
    private TextRenderer _text = null!;
    private Ui _ui = null!;

    private GameState _state = null!;
    private readonly MachineView _machine = new();
    private readonly Effects _fx = new();
    private readonly Crate _crate = new();

    // Both start tucked away: the cabinet is the scene, and a menu only exists
    // once you have asked for it.
    private readonly SlidePanel _upgradeDrawer =
        new(PanelSide.Left, DrawerWidth, "UPGRADES");

    private readonly SlidePanel _inspectorDrawer =
        new(PanelSide.Right, DrawerWidth, "SLOT");

    private double _tickAccumulator;
    private double _autosaveTimer;
    private double _elapsed;

    // Smoothed gross income, so the readout does not flicker between ticks.
    private double _earnedLastFrame;
    private double _smoothedIncome;

    private OfflineReport? _offlineReport;
    private double _offlineToastAge;

    private KeyboardState _prevKeyboard;
    private int _framesDrawn;

    public VendingGame(LaunchOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = ScreenWidth,
            PreferredBackBufferHeight = ScreenHeight
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Vending Idle";
        Window.AllowUserResizing = false;
    }

    protected override void Initialize()
    {
        _state = (_options.FreshStart ? null : SaveSystem.Load(_options.SavePath)) ?? GameState.NewGame();

        if (!_options.FreshStart)
        {
            var away = SaveSystem.SecondsSinceSave(_state);
            if (away > 30.0)
            {
                var report = Simulation.RunOffline(_state, away, _rng);
                if (report.Earned > 0.0 || report.CansSold > 0) _offlineReport = report;
            }
        }

        _machine.SelectedSlot = FirstUnlockedSlot();
        ApplyDrawerOption();

        if (_options.ForceReveal && _state.PendingRevealId is null)
        {
            _state.Tokens = Math.Max(_state.Tokens, (long)Math.Ceiling(_state.NextPackCost));
            if (_state.TryOpenPack(_rng) is not null) _crate.BeginReveal();
        }

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _prims = new Primitives(GraphicsDevice);
        _text = new TextRenderer(
            Content.Load<SpriteFont>("Fonts/UiFontSmall"),
            Content.Load<SpriteFont>("Fonts/UiFont"),
            Content.Load<SpriteFont>("Fonts/UiFontLarge"));
        _ui = new Ui(GraphicsDevice, _prims, _text);
    }

    /// <summary>
    /// Drawers start closed, so anything asked for here animates open from the
    /// first frame rather than snapping into place.
    /// </summary>
    private void ApplyDrawerOption()
    {
        switch (_options.Drawers)
        {
            case "open":
                _upgradeDrawer.Open();
                _inspectorDrawer.Open();
                break;

            case "left":
                _upgradeDrawer.Open();
                break;

            case "right":
                _inspectorDrawer.Open();
                break;
        }
    }

    private int FirstUnlockedSlot()
    {
        foreach (var slot in _state.Slots)
            if (slot.Unlocked)
                return slot.Index;
        return 0;
    }

    // ---------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += dt;

        var earnedBefore = _state.TotalEarned;

        // Fixed 20 Hz simulation, decoupled from the render rate.
        _tickAccumulator += dt;
        var guard = 0;
        while (_tickAccumulator >= Balance.TickSeconds && guard++ < 240)
        {
            Simulation.Step(_state, Balance.TickSeconds, _rng, this);
            _tickAccumulator -= Balance.TickSeconds;
        }

        _earnedLastFrame = _state.TotalEarned - earnedBefore;
        if (dt > 0)
        {
            // Exponential moving average with a ~1.5 s window.
            var instantaneous = _earnedLastFrame / dt;
            var alpha = 1.0 - Math.Exp(-dt / 1.5);
            _smoothedIncome += (instantaneous - _smoothedIncome) * alpha;
        }

        _fx.Update((float)dt);
        _crate.Update((float)dt, _state);
        _upgradeDrawer.Update((float)dt);
        _inspectorDrawer.Update((float)dt);

        HandleKeyboard();

        _autosaveTimer += dt;
        if (_autosaveTimer >= Balance.AutosaveIntervalSeconds)
        {
            _autosaveTimer = 0.0;
            SaveSystem.Save(_state, _options.SavePath);
        }

        if (_offlineReport is not null)
        {
            _offlineToastAge += dt;
            if (_offlineToastAge > 30.0) _offlineReport = null;
        }

        base.Update(gameTime);
    }

    private void HandleKeyboard()
    {
        var keyboard = Keyboard.GetState();

        if (WasPressed(keyboard, Keys.Space) || WasPressed(keyboard, Keys.Enter))
            PlayerVend();

        if (WasPressed(keyboard, Keys.R))
            _state.RestockAll();

        if (WasPressed(keyboard, Keys.S))
            SaveSystem.Save(_state, _options.SavePath);

        if (WasPressed(keyboard, Keys.Q)) _upgradeDrawer.Toggle();
        if (WasPressed(keyboard, Keys.E)) _inspectorDrawer.Toggle();

        if (WasPressed(keyboard, Keys.Tab))
        {
            // Tab clears the chrome away entirely for a look at the machine.
            var anyOpen = _upgradeDrawer.IsOpen || _inspectorDrawer.IsOpen;
            if (anyOpen) { _upgradeDrawer.Close(); _inspectorDrawer.Close(); }
            else { _upgradeDrawer.Open(); _inspectorDrawer.Open(); }
        }

        _prevKeyboard = keyboard;
    }

    private bool WasPressed(KeyboardState now, Keys key) =>
        now.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

    private void PlayerVend()
    {
        var result = Simulation.Click(_state, _rng);
        OnDispense(result, fromCustomer: false);
    }

    // ---------------------------------------------------------------------
    // ISimEvents -- turning simulation results into screen feedback
    // ---------------------------------------------------------------------

    public void OnDispense(in ClickResult result, bool fromCustomer)
    {
        // Popups rise from the top of the cell so they clear the rack below.
        var origin = result.SlotIndex >= 0 && _machine.TryGetCellRect(result.SlotIndex, out var cell)
            ? new Vector2(cell.Center.X - 16, cell.Y - 2)
            : new Vector2(_machine.TrayRect.Center.X - 16, _machine.TrayRect.Y - 6);

        var color = result.Crit ? Theme.Crit
                  : result.SpareChange ? Theme.TextDim
                  : Theme.Money;

        _fx.SpawnPopup("+" + Money.Cash(result.Payout), origin, color,
                       result.Crit ? FontSize.Large : FontSize.Normal);

        if (result.Cans > 0)
        {
            var drink = DrinkDatabase.Get(result.DrinkId);
            var bottleColor = drink is not null ? Theme.FromPacked(drink.Color) : Theme.TextDim;

            // Drinks leave from the front of the row, so ask for them by position
            // from the front -- the row shows proportional fill, and an absolute
            // stock index would fall off the end of it.
            for (var i = 0; i < result.Cans; i++)
            {
                if (_machine.TryGetDispensedBottle(result.SlotIndex, i, out var from))
                    _fx.SpawnBottle(from, _machine.TrayFloorY, bottleColor);
            }
        }

        // ---- Effect-drink feedback ----------------------------------------
        if (result.Preserved)
            _fx.SpawnPopup("kept!", origin + new Vector2(0, 16), Theme.Accent, FontSize.Small);

        if (result.CourierSlotIndex >= 0 &&
            _machine.TryGetCellRect(result.CourierSlotIndex, out var courierCell))
            _fx.SpawnPopup("+1 delivery",
                new Vector2(courierCell.Center.X - 30, courierCell.Y - 2),
                Theme.Positive, FontSize.Small);

        if (result.Chain is { } chain)
        {
            var chainDrink = DrinkDatabase.Get(chain.DrinkId);
            var chainColor = chainDrink is not null
                ? Theme.FromPacked(chainDrink.Color) : Theme.TextDim;

            if (_machine.TryGetDispensedBottle(chain.SlotIndex, 0, out var chainFrom))
                _fx.SpawnBottle(chainFrom, _machine.TrayFloorY, chainColor);

            if (_machine.TryGetCellRect(chain.SlotIndex, out var chainCell))
                _fx.SpawnPopup("+" + Money.Cash(chain.Payout),
                    new Vector2(chainCell.Center.X - 16, chainCell.Y - 2),
                    Theme.Crit, FontSize.Normal);
        }

        _fx.FlashTray();
    }

    public void OnAutoRestock(int slotIndex, int units)
    {
        // Intentionally quiet: restocking every few seconds across a dozen slots
        // would bury the payout popups that actually matter.
    }

    // ---------------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------------

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Theme.Background);

        var screen = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
        _ui.BeginFrame(_sb, Mouse.GetState(), screen);
        _ui.Begin();

        var machineBounds = new Rectangle((ScreenWidth - MachineWidth) / 2, MachineTop,
                                          MachineWidth, MachineHeight);
        var floorY = machineBounds.Bottom;

        var drawerTop = 24;
        var drawerHeight = ScreenHeight - drawerTop * 2;
        var upgradeBounds = _upgradeDrawer.Bounds(screen, drawerTop, drawerHeight);
        var inspectorBounds = _inspectorDrawer.Bounds(screen, drawerTop, drawerHeight);

        // Room first, so the cabinet has somewhere to stand. The crate shares
        // its ground plane, drawn here so open drawers slide over it.
        Backdrop.Draw(_ui, screen, machineBounds, floorY);

        var crateBounds = new Rectangle(machineBounds.X - 190, floorY - 108, 128, 108);
        _crate.Draw(_ui, _state, crateBounds, _elapsed);

        // Drawers are hit-tested before the cabinet so a click on an open menu
        // can never fall through to the delivery flap behind it.
        UpgradeId? upgradeClicked = null;
        if (_upgradeDrawer.Visible)
            upgradeClicked = UpgradePanel.Draw(_ui, _state, upgradeBounds);

        var inspectorAction = InspectorAction.None;
        if (_inspectorDrawer.Visible)
            inspectorAction = SlotInspector.Draw(_ui, _state, inspectorBounds, _machine.SelectedSlot);

        if (_upgradeDrawer.DrawTab(_ui, upgradeBounds, screen)) _upgradeDrawer.Toggle();
        if (_inspectorDrawer.DrawTab(_ui, inspectorBounds, screen)) _inspectorDrawer.Toggle();

        // Crate input runs after the drawers so an open menu over it wins the
        // click, even though the crate is painted underneath them.
        var crateAction = _crate.HandleInput(_ui, _state);

        var machineAction = _machine.Draw(_ui, _state, machineBounds, _fx, _elapsed, _smoothedIncome);

        // Effects are clipped to the cabinet plus the crate's airspace, so
        // falling drinks and redeem popups cannot spill over the rest of the room.
        _ui.PushClip(Rectangle.Union(machineBounds, _crate.EffectBounds));
        _fx.Draw(_ui);
        _ui.PopClip();

        DrawHint(machineBounds);

        if (_offlineReport is not null) DrawOfflineToast(machineBounds);

        _ui.DrawTooltip(screen);
        _ui.End();

        ApplyActions(inspectorAction, upgradeClicked, machineAction, crateAction);

        base.Draw(gameTime);

        _framesDrawn++;
        if (_options.ScreenshotPath is not null && _framesDrawn >= _options.ScreenshotFrames)
        {
            CaptureScreenshot(_options.ScreenshotPath);
            Exit();
        }
    }

    private void ApplyActions(InspectorAction inspector, UpgradeId? upgrade, MachineAction machine,
                              CrateAction crate)
    {
        if (crate == CrateAction.Open && _state.TryOpenPack(_rng) is not null)
            _crate.BeginReveal();

        if (crate == CrateAction.Redeem && _state.RedeemReveal() is { } redeem)
        {
            var drink = DrinkDatabase.Get(redeem.DrinkId)!;
            var origin = new Vector2(_crate.RevealRect.Center.X - 24, _crate.RevealRect.Y - 6);

            _fx.SpawnPopup(drink.Name, origin, Theme.FromPacked(drink.Color), FontSize.Normal);
            _fx.SpawnPopup(
                redeem.WasNew ? "NEW!" : redeem.AtMax ? "MAX" : $"Lv {redeem.Level}",
                origin + new Vector2(8, -22),
                redeem.WasNew ? Theme.Positive : Theme.Accent,
                FontSize.Large);
        }

        if (machine.RestockAll) _state.RestockAll();
        if (machine.Save) SaveSystem.Save(_state, _options.SavePath);

        if (inspector.AssignDrinkId is not null)
            _state.TryAssignDrink(_machine.SelectedSlot, inspector.AssignDrinkId);

        if (inspector.ClearAssignment)
            _state.TryAssignDrink(_machine.SelectedSlot, null);

        if (inspector.RestockUnits != 0)
        {
            var slot = _state.SlotAt(_machine.SelectedSlot);
            if (slot is not null)
            {
                if (inspector.RestockUnits < 0) _state.RestockToFull(slot);
                else _state.Restock(slot, inspector.RestockUnits);
            }
        }

        if (inspector.BuyAutoRestocker)
            _state.TryBuyAutoRestocker(_machine.SelectedSlot);

        if (upgrade is not null)
            _state.TryBuyUpgrade(upgrade.Value);

        // Touching a slot is what you do right before you want to act on it, so
        // the inspector comes out to meet you.
        if (machine.BuySlot >= 0 && _state.TryBuySlot(machine.BuySlot))
        {
            _machine.SelectedSlot = machine.BuySlot;
            _inspectorDrawer.Open();
        }

        if (machine.SelectSlot >= 0)
        {
            _machine.SelectedSlot = machine.SelectSlot;
            _inspectorDrawer.Open();
        }

        if (machine.Vend) PlayerVend();
    }

    /// <summary>
    /// Contextual nudges, printed on the wall above the cabinet -- the one place
    /// that is always empty, and it keeps tutorial text off the machine itself.
    /// </summary>
    private void DrawHint(Rectangle machineBounds)
    {
        var hint = CurrentHint();
        if (hint is null) return;

        var rect = new Rectangle(machineBounds.X, machineBounds.Y - 34, machineBounds.Width, 20);
        _ui.T.DrawIn(_sb, hint, rect, Theme.TextDim, FontSize.Small, Align.Center);
    }

    private string? CurrentHint()
    {
        if (_state.TotalStock > 0)
        {
            if (_state.Customers == 0 && _state.Money > 60)
                return "Hire a customer - they click the machine for you.";

            if (_state.PendingRevealId is not null)
                return "A drink is waiting above the crate - click it to claim.";

            if (_state.CanOpenPack)
                return "The supply crate is full - crack it open.";

            return null;
        }

        // Machine is dry. Which of the three reasons is it?
        var anyDrinkLoaded = false;
        foreach (var slot in _state.Slots)
            if (slot.Unlocked && slot.DrinkId is not null)
                anyDrinkLoaded = true;

        if (!anyDrinkLoaded)
            return "Pick a slot, load a drink into it, then restock.";

        if (_state.Money < 1.0)
            return "Click the tray to shake out some change.";

        return "Out of stock - restock to keep the money coming.";
    }

    private void DrawOfflineToast(Rectangle machineBounds)
    {
        var report = _offlineReport!;

        // Sits over the machine rather than the screen centre, so it never covers
        // the inspector or the upgrade list.
        var width = Math.Min(440, machineBounds.Width - 24);
        var rect = new Rectangle(machineBounds.Center.X - width / 2, machineBounds.Y + 60, width, 96);

        _prims.FillRounded(_sb, rect, 10, Theme.PanelAlt);
        _prims.OutlineRounded(_sb, rect, 10, Theme.Accent);

        _text.DrawIn(_sb, $"Away for {Money.FormatDuration(report.Seconds)}",
            new Rectangle(rect.X, rect.Y + 10, rect.Width, 22), Theme.Text, FontSize.Normal, Align.Center);

        _text.DrawIn(_sb, $"+{Money.Cash(report.Earned)} from {report.CansSold} bottles",
            new Rectangle(rect.X, rect.Y + 36, rect.Width, 26), Theme.Money, FontSize.Large, Align.Center);

        var note = report.Capped
            ? "offline earnings cap is 8 hours"
            : "your customers kept buying";
        _text.DrawIn(_sb, note,
            new Rectangle(rect.X, rect.Y + 68, rect.Width, 18), Theme.TextFaint, FontSize.Small, Align.Center);

        var dismiss = new Rectangle(rect.Right - 30, rect.Y + 8, 22, 22);
        if (_ui.Button(dismiss, "x", true, ButtonStyle.Subtle, size: FontSize.Small))
            _offlineReport = null;
    }

    private void CaptureScreenshot(string path)
    {
        var w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var h = GraphicsDevice.PresentationParameters.BackBufferHeight;

        var data = new Color[w * h];
        GraphicsDevice.GetBackBufferData(data);

        using var tex = new Texture2D(GraphicsDevice, w, h);
        tex.SetData(data);

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        using var stream = System.IO.File.Create(path);
        tex.SaveAsPng(stream, w, h);
        Console.WriteLine($"screenshot written: {path} ({w}x{h})");
    }

    protected override void EndRun()
    {
        // Screenshot runs are throwaway; never let one overwrite a real save.
        if (_options.ScreenshotPath is null)
            SaveSystem.Save(_state, _options.SavePath);

        base.EndRun();
    }

    protected override void UnloadContent()
    {
        _prims.Dispose();
        base.UnloadContent();
    }
}
