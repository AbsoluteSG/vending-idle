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
    private const int Margin = 16;
    private const int MachineWidth = 600;
    private const int DrawerWidth = 316;

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

    private readonly SlidePanel _upgradeDrawer =
        new(PanelSide.Left, DrawerWidth, "UPGRADES", open: true);

    private readonly SlidePanel _inspectorDrawer =
        new(PanelSide.Right, DrawerWidth, "SLOT", open: true);

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
    /// Both drawers are constructed open, so asking for anything else here makes
    /// them animate to it from the first frame rather than snapping.
    /// </summary>
    private void ApplyDrawerOption()
    {
        switch (_options.Drawers)
        {
            case "closed":
                _upgradeDrawer.Close();
                _inspectorDrawer.Close();
                break;

            case "left":
                _inspectorDrawer.Close();
                break;

            case "right":
                _upgradeDrawer.Close();
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

            // The simulation has already taken the bottles off the shelf, so the
            // ones that just left were sitting at [Stock .. Stock + Cans - 1].
            var remaining = _state.SlotAt(result.SlotIndex)?.Stock ?? 0;

            for (var i = 0; i < result.Cans; i++)
            {
                if (_machine.TryGetBottleRect(result.SlotIndex, remaining + i, out var from))
                    _fx.SpawnBottle(from, _machine.TrayFloorY, bottleColor);
            }
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

        // The machine is the stage: centred, with everything else either floating
        // above it or sliding in from an edge.
        var topBar = new Rectangle((ScreenWidth - MachineWidth) / 2, 10, MachineWidth, 52);
        var contentY = topBar.Bottom + 10;
        var contentHeight = ScreenHeight - contentY - Margin;

        var machineBounds = new Rectangle((ScreenWidth - MachineWidth) / 2, contentY,
                                          MachineWidth, contentHeight);

        var upgradeBounds = _upgradeDrawer.Bounds(screen, contentY, contentHeight);
        var inspectorBounds = _inspectorDrawer.Bounds(screen, contentY, contentHeight);

        // Drawers are drawn (and therefore hit-tested) before the machine so a
        // click on one can never fall through to the vend tray behind it.
        var topAction = TopBar.Draw(_ui, _state, topBar, _smoothedIncome,
                                    SaveSystem.SecondsSinceSave(_state));

        UpgradeId? upgradeClicked = null;
        if (_upgradeDrawer.Visible)
            upgradeClicked = UpgradePanel.Draw(_ui, _state, upgradeBounds);

        var inspectorAction = InspectorAction.None;
        if (_inspectorDrawer.Visible)
            inspectorAction = SlotInspector.Draw(_ui, _state, inspectorBounds, _machine.SelectedSlot);

        if (_upgradeDrawer.DrawTab(_ui, upgradeBounds, screen)) _upgradeDrawer.Toggle();
        if (_inspectorDrawer.DrawTab(_ui, inspectorBounds, screen)) _inspectorDrawer.Toggle();

        var machineAction = _machine.Draw(_ui, _state, machineBounds, _fx, _elapsed, CurrentHint());

        // Effects are clipped to the machine so rising payouts cannot scribble
        // over the top bar or the side panels.
        _ui.PushClip(machineBounds);
        _fx.Draw(_ui);
        _ui.PopClip();

        if (_offlineReport is not null) DrawOfflineToast(machineBounds);

        _ui.DrawTooltip(screen);
        _ui.End();

        ApplyActions(topAction, inspectorAction, upgradeClicked, machineAction);

        base.Draw(gameTime);

        _framesDrawn++;
        if (_options.ScreenshotPath is not null && _framesDrawn >= _options.ScreenshotFrames)
        {
            CaptureScreenshot(_options.ScreenshotPath);
            Exit();
        }
    }

    private void ApplyActions(TopBarAction top, InspectorAction inspector,
                              UpgradeId? upgrade, MachineAction machine)
    {
        if (top.RestockAll) _state.RestockAll();
        if (top.Save) SaveSystem.Save(_state, _options.SavePath);

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

    /// <summary>Contextual nudges, so the opening minutes are not a guessing game.</summary>
    private string? CurrentHint()
    {
        if (_state.TotalStock > 0)
            return _state.Customers == 0 && _state.Money > 60
                ? "Hire a customer - they click the machine for you."
                : null;

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
