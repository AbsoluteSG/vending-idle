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
    private const int DrawerWidth = 286;

    /// <summary>
    /// World Y of the floor line. The cabinet's base is bolted here and every row
    /// added grows it upward from this point, so the tray never moves and the
    /// camera does the travelling.
    /// </summary>
    private const int FloorY = 640;

    /// <summary>Gap left above the crown when the camera is panned all the way up.</summary>
    private const int CrownMargin = 40;

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
    private readonly Sfx _sfx = new();
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

    /// <summary>
    /// How far the camera has climbed the cabinet, in pixels. Zero is resting on
    /// the tray. Smoothed toward <see cref="_panTarget"/> so the wheel glides
    /// rather than snapping.
    /// </summary>
    private float _pan;
    private float _panTarget;

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

        _sfx.Load(Content);
        _sfx.SetMuted(_options.Muted || _state.Muted);

        // Starts the bed going. Safe while muted -- it begins paused, so the
        // loop is already in position the moment the player turns sound on.
        _sfx.StartMusic();

        // The clink is owned by the effect, not the click: a bottle sounds when
        // it actually lands, which is a few hundred milliseconds after the shake
        // that threw it and later still if it bounces twice.
        _fx.BottleLanded += _sfx.Bottle;
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

        _sfx.BeginFrame();
        _fx.Update((float)dt);
        _crate.Update((float)dt, _state);

        // Ease the camera toward where the wheel asked for. Exponential, so it
        // arrives quickly without ever quite snapping.
        _panTarget = MathHelper.Clamp(_panTarget, 0f, MaxPan());
        _pan += (_panTarget - _pan) * (1f - (float)Math.Exp(-dt * 14.0));
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
            PlayerShake();

        if (WasPressed(keyboard, Keys.R))
        {
            if (_state.RestockAll() > 0) _sfx.Purchase();
            else _sfx.Denied();
        }

        if (WasPressed(keyboard, Keys.S))
            SaveSystem.Save(_state, _options.SavePath);

        if (WasPressed(keyboard, Keys.M)) ToggleMute();

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

    /// <summary>
    /// Flips the sound and remembers the choice. Unmuting plays a cue on the way
    /// out: the music bed fades up under it, and without a click there is no
    /// confirmation that anything happened.
    /// </summary>
    private void ToggleMute()
    {
        if (!_sfx.Available) return;

        _sfx.ToggleMute();
        _state.Muted = _sfx.Muted;

        if (!_sfx.Muted) _sfx.Purchase();
    }

    private bool WasPressed(KeyboardState now, Keys key) =>
        now.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

    /// <summary>Machine bounds in world space: base on the floor, growing upward.</summary>
    private Rectangle MachineBounds()
    {
        var height = MachineView.HeightFor(_state.RowCount);
        return new Rectangle((ScreenWidth - MachineWidth) / 2, FloorY - height,
                             MachineWidth, height);
    }

    /// <summary>
    /// Furthest the camera can climb: enough to bring the crown into view, and no
    /// further. Zero while the whole cabinet already fits on screen.
    /// </summary>
    private float MaxPan()
    {
        var top = MachineBounds().Y;
        return Math.Max(0f, CrownMargin - top);
    }

    private void PlayerShake()
    {
        var shake = Simulation.Shake(_state, _rng);

        // A loaded cabinet has weight behind it; an empty one just rattles.
        _fx.Shake(shake.SpareChange ? 0.45f : 0.85f);
        _sfx.Shake(paidOut: !shake.SpareChange);

        foreach (var drop in shake.Drops)
            OnDispense(drop, fromCustomer: false);
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
            var pitch = drink is not null ? (float)drink.SoundPitch : 0f;

            for (var i = 0; i < result.Cans; i++)
            {
                if (_machine.TryGetDispensedBottle(result.SlotIndex, i, out var from))
                    _fx.SpawnBottle(from, _machine.TrayFloorY, bottleColor, pitch);
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

        // Two cameras in one. The pan climbs the cabinet and input follows it, so
        // a click lands on the compartment you can see. The shake is layered on
        // top and input deliberately ignores it, so a rattle cannot jog a click
        // off the button you aimed at.
        _ui.BeginFrame(_sb, Mouse.GetState(), screen,
                       new Vector2(0f, _pan), _fx.CameraOffset);

        var machineBounds = MachineBounds();
        var pan = (int)Math.Round(_pan);

        var drawerTop = 24;
        var drawerHeight = ScreenHeight - drawerTop * 2;
        var upgradeBounds = _upgradeDrawer.Bounds(screen, drawerTop, drawerHeight);
        var inspectorBounds = _inspectorDrawer.Bounds(screen, drawerTop, drawerHeight);

        // The wall sits behind everything and does not travel with the camera.
        _ui.Begin(Space.Screen);
        Backdrop.DrawWall(_ui, screen, machineBounds, pan);

        // The floor and the crate standing on it share the cabinet's ground plane,
        // so they ride the camera. Both are painted before the drawers, which are
        // meant to slide over the crate rather than under it.
        _ui.Begin(Space.World);
        Backdrop.DrawFloor(_ui, screen, machineBounds, FloorY, pan);

        var crateBounds = new Rectangle(machineBounds.X - 190, FloorY - 108, 128, 108);
        _crate.Draw(_ui, _state, crateBounds, _elapsed);

        _ui.Begin(Space.Screen);

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

        // Everything from here rides the camera again. Crate input runs after the
        // drawers so an open menu over it wins the click, even though the crate
        // was painted underneath them.
        _ui.Begin(Space.World);
        var crateAction = _crate.HandleInput(_ui, _state);

        var machineAction = _machine.Draw(_ui, _state, machineBounds, _fx, _elapsed, _smoothedIncome);

        // Effects are clipped to the cabinet plus the crate's airspace, so
        // falling drinks and redeem popups cannot spill over the rest of the room.
        _ui.PushClip(Rectangle.Union(machineBounds, _crate.EffectBounds));
        _fx.Draw(_ui);
        _ui.PopClip();

        HandleCameraScroll(machineBounds);

        _ui.Begin(Space.Screen);
        DrawHint();

        if (_offlineReport is not null) DrawOfflineToast(screen);

        // Drawn last, so it stays on top of a drawer that has slid out under it.
        // The corner belongs to the player, not to whichever menu is open.
        var muteRect = new Rectangle(screen.Right - 46, 16, 30, 30);
        if (_ui.MuteButton(muteRect, _sfx.Muted, _sfx.Available)) ToggleMute();

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
        // A click on a greyed-out button never reaches the state at all, so the
        // refusal has to come from the widget layer.
        var bought = false;
        var refused = _ui.ClickDenied;

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

        if (machine.RestockAll)
        {
            if (_state.RestockAll() > 0) bought = true;
            else refused = true;
        }

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
                var added = inspector.RestockUnits < 0
                    ? _state.RestockToFull(slot)
                    : _state.Restock(slot, inspector.RestockUnits);

                if (added > 0) bought = true;
                else refused = true;
            }
        }

        if (inspector.BuyAutoRestocker)
        {
            if (_state.TryBuyAutoRestocker(_machine.SelectedSlot)) bought = true;
            else refused = true;
        }

        if (upgrade is not null)
        {
            if (_state.TryBuyUpgrade(upgrade.Value)) bought = true;
            else refused = true;
        }

        // Touching a slot is what you do right before you want to act on it, so
        // the inspector comes out to meet you.
        if (machine.BuySlot >= 0)
        {
            if (_state.TryBuySlot(machine.BuySlot))
            {
                bought = true;
                _machine.SelectedSlot = machine.BuySlot;
                _inspectorDrawer.Open();
            }
            else
            {
                // The price ticket stays clickable when it is unaffordable, so
                // this is the main way a player hears the refusal.
                refused = true;
            }
        }

        if (machine.SelectSlot >= 0)
        {
            _machine.SelectedSlot = machine.SelectSlot;
            _inspectorDrawer.Open();
        }

        if (machine.Shake) PlayerShake();

        // One cue per frame, and success wins: a "Restock all" that fills three
        // slots and runs dry on the fourth is a purchase, not a refusal.
        if (bought) _sfx.Purchase();
        else if (refused) _sfx.Denied();
    }

    /// <summary>
    /// The wheel drives the camera whenever the pointer is over the cabinet or the
    /// room beside it, but not over an open drawer -- those scroll their own lists.
    /// </summary>
    private void HandleCameraScroll(Rectangle machineBounds)
    {
        if (_ui.WheelDelta == 0) return;

        var overDrawer =
            (_upgradeDrawer.Visible && _upgradeDrawer.Bounds(_ui.Screen, 24, ScreenHeight - 48)
                .Contains(_ui.MouseScreen)) ||
            (_inspectorDrawer.Visible && _inspectorDrawer.Bounds(_ui.Screen, 24, ScreenHeight - 48)
                .Contains(_ui.MouseScreen));

        if (overDrawer) return;

        _panTarget = MathHelper.Clamp(_panTarget + _ui.WheelDelta * 0.6f, 0f, MaxPan());
    }

    /// <summary>
    /// Contextual nudges, printed near the top of the wall. Screen space: the hint
    /// is a note to the player, not a sign hung on a machine that may be scrolled
    /// a thousand pixels away.
    /// </summary>
    private void DrawHint()
    {
        var hint = CurrentHint();
        if (hint is null) return;

        var rect = new Rectangle(0, 22, ScreenWidth, 20);
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

    private void DrawOfflineToast(Rectangle screen)
    {
        var report = _offlineReport!;

        // Screen space, between the two drawers, so it neither covers them nor
        // rides away when the camera climbs the cabinet.
        const int width = 440;
        var rect = new Rectangle(screen.Center.X - width / 2, 110, width, 96);

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
