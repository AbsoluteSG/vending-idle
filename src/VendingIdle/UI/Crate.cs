using System;
using Microsoft.Xna.Framework;
using VendingIdle.Core;
using VendingIdle.Render;

namespace VendingIdle.UI;

public enum CrateAction
{
    None,
    Open,
    Redeem
}

/// <summary>
/// The supply crate on the floor beside the cabinet, and its mystery-box reveal:
/// click the crate and a drink floats out of the lid, shuffling rapidly through
/// the pack roster and slowing as it climbs; at the top it locks onto the actual
/// roll and bobs there until clicked to redeem. The crate is dead until the
/// bobbing drink is claimed -- that gate lives in GameState (PendingRevealId),
/// this class is only the animation over it.
///
/// Drawn behind the slide-in menus but hit-tested after them, so an open drawer
/// over the crate wins the click. Hence the two-phase Draw / HandleInput split.
/// </summary>
public sealed class Crate
{
    private const int DrinkW = 28;
    private const int DrinkH = 52;

    private const float RiseDuration = 2.1f;
    private const float HoverHeight = 118f;
    private const float BobAmplitude = 7f;
    private const float BobSpeed = 3.0f;

    private enum Phase
    {
        Idle,
        Rising,
        Bobbing
    }

    private readonly Random _rng = new();

    private Phase _phase = Phase.Idle;
    private float _timer;
    private float _bobTimer;
    private float _lockFlash;
    private string _shuffleId = "";
    private float _shuffleClock;

    private Rectangle _bounds;
    private Rectangle _revealRect;
    private bool _crateHovered;
    private bool _revealHovered;

    /// <summary>Where the reveal drink currently sits, for redeem popups.</summary>
    public Rectangle RevealRect => _revealRect;

    /// <summary>The whole region the crate can paint, for widening the fx clip.</summary>
    public Rectangle EffectBounds => new(
        _bounds.X - 30, _bounds.Y - (int)(HoverHeight + DrinkH + 40),
        _bounds.Width + 60, (int)(HoverHeight + DrinkH + 40) + _bounds.Height + 10);

    /// <summary>Called when a crate is opened, to start the shuffle from the lid.</summary>
    public void BeginReveal()
    {
        _phase = Phase.Rising;
        _timer = 0f;
        _shuffleClock = 0f;
        _shuffleId = RandomPackId();
    }

    private string RandomPackId() =>
        DrinkDatabase.PackDrinks[_rng.Next(DrinkDatabase.PackDrinks.Count)].Id;

    public void Update(float dt, GameState state)
    {
        _lockFlash = Math.Max(0f, _lockFlash - dt * 2.2f);

        switch (_phase)
        {
            case Phase.Idle:
                // A pending reveal with no animation running means we loaded a
                // save mid-reveal: the roll is known, go straight to the bob.
                if (state.PendingRevealId is not null)
                {
                    _phase = Phase.Bobbing;
                    _bobTimer = 0f;
                }
                break;

            case Phase.Rising:
                _timer += dt;

                // The shuffle slows as the drink climbs, mystery-box style.
                _shuffleClock += dt;
                var t = Math.Clamp(_timer / RiseDuration, 0f, 1f);
                var interval = 0.055f + 0.28f * t * t;
                if (_shuffleClock >= interval)
                {
                    _shuffleClock = 0f;
                    _shuffleId = RandomPackId();
                }

                if (_timer >= RiseDuration)
                {
                    _phase = Phase.Bobbing;
                    _bobTimer = 0f;
                    _lockFlash = 1f;
                }
                break;

            case Phase.Bobbing:
                _bobTimer += dt;
                if (state.PendingRevealId is null)
                    _phase = Phase.Idle;   // redeemed (or cleared externally)
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Draw (phase 1 -- visuals only, behind the drawers)
    // ---------------------------------------------------------------------

    public void Draw(Ui ui, GameState state, Rectangle bounds, double now)
    {
        _bounds = bounds;

        // Hover states are computed at input time; remembered from last frame
        // purely for paint. Fine at 60 fps.
        DrawBox(ui, state, bounds, now);
        DrawReveal(ui, state, bounds);
    }

    private void DrawBox(Ui ui, GameState state, Rectangle rect, double now)
    {
        var affordable = state.CanOpenPack;

        // Ready-to-open glow behind the crate.
        if (affordable)
        {
            var pulse = 0.5f + 0.5f * (float)Math.Sin(now * 2.4);
            ui.P.Glow(ui.Sb, new Vector2(rect.Center.X, rect.Center.Y),
                      rect.Width * 0.95f, Theme.Money * (0.18f + 0.14f * pulse));
        }

        // The box itself: planks, a lid, stencilled label.
        ui.P.FillRounded(ui.Sb, rect, 4, Theme.CrateWood);

        var lid = new Rectangle(rect.X - 4, rect.Y, rect.Width + 8, 12);
        ui.P.FillRounded(ui.Sb, lid, 3, Theme.CrateLight);
        ui.P.Fill(ui.Sb, new Rectangle(lid.X, lid.Bottom - 2, lid.Width, 2), Theme.CrateDark);

        for (var y = rect.Y + 20; y < rect.Bottom - 8; y += 14)
            ui.P.Fill(ui.Sb, new Rectangle(rect.X + 4, y, rect.Width - 8, 2), Theme.CrateDark);

        // Corner braces.
        ui.P.Fill(ui.Sb, new Rectangle(rect.X + 3, rect.Y + 14, 5, rect.Height - 18), Theme.CrateDark);
        ui.P.Fill(ui.Sb, new Rectangle(rect.Right - 8, rect.Y + 14, 5, rect.Height - 18), Theme.CrateDark);

        if (_crateHovered && affordable)
            ui.P.OutlineRounded(ui.Sb, rect, 4, Theme.Money);
        else
            ui.P.OutlineRounded(ui.Sb, rect, 4, Theme.CrateDark);

        ui.T.DrawIn(ui.Sb, "SUPPLY",
            new Rectangle(rect.X, rect.Center.Y - 8, rect.Width, 16),
            Theme.CrateLight, FontSize.Small, Align.Center);

        // Token gauge under the crate, on the floor.
        var cost = (long)Math.Ceiling(state.NextPackCost);
        var fraction = cost <= 0 ? 1f : MathHelper.Clamp(state.Tokens / (float)cost, 0f, 1f);

        var gauge = new Rectangle(rect.X, rect.Bottom + 8, rect.Width, 6);
        ui.ProgressBar(gauge, fraction, affordable ? Theme.Money : Theme.Accent);

        var label = state.PendingRevealId is not null
            ? "claim your drink"
            : $"{Money.Format(state.Tokens)} / {Money.Format(cost)} tk";

        ui.T.DrawIn(ui.Sb, label,
            new Rectangle(rect.X - 30, gauge.Bottom + 3, rect.Width + 60, 14),
            state.PendingRevealId is not null ? Theme.Accent
                : affordable ? Theme.Money : Theme.TextFaint,
            FontSize.Small, Align.Center);
    }

    private void DrawReveal(Ui ui, GameState state, Rectangle crate)
    {
        _revealRect = Rectangle.Empty;
        if (_phase == Phase.Idle) return;

        string? shownId;
        float height;

        if (_phase == Phase.Rising)
        {
            var t = Math.Clamp(_timer / RiseDuration, 0f, 1f);
            var eased = t * t * (3f - 2f * t);
            height = HoverHeight * eased;
            shownId = _shuffleId;
        }
        else
        {
            height = HoverHeight + BobAmplitude * (float)Math.Sin(_bobTimer * BobSpeed);
            shownId = state.PendingRevealId;
        }

        var drink = DrinkDatabase.Get(shownId);
        if (drink is null) return;

        var color = Theme.FromPacked(drink.Color);
        var rect = new Rectangle(
            crate.Center.X - DrinkW / 2,
            (int)(crate.Y - height) - DrinkH,
            DrinkW, DrinkH);

        _revealRect = rect;

        var center = new Vector2(rect.Center.X, rect.Center.Y);

        // Lock-in flash, then a steady rarity-tinted halo while it waits.
        if (_lockFlash > 0f)
            ui.P.Glow(ui.Sb, center, 70f, Color.White * (_lockFlash * 0.55f));

        if (_phase == Phase.Bobbing)
        {
            ui.P.Glow(ui.Sb, center, 44f, color * 0.35f);

            if (_revealHovered)
                ui.P.Glow(ui.Sb, center, 56f, Color.White * 0.18f);
        }

        MachineView.DrawDrink(ui, rect, color);

        if (_phase == Phase.Bobbing)
        {
            ui.T.DrawIn(ui.Sb, drink.Name,
                new Rectangle(rect.Center.X - 90, rect.Y - 20, 180, 16),
                Theme.Text, FontSize.Small, Align.Center);

            ui.T.DrawIn(ui.Sb, "click to claim",
                new Rectangle(rect.Center.X - 90, rect.Bottom + 4, 180, 14),
                Theme.TextFaint, FontSize.Small, Align.Center);
        }
    }

    // ---------------------------------------------------------------------
    // Input (phase 2 -- after the drawers have had their chance)
    // ---------------------------------------------------------------------

    public CrateAction HandleInput(Ui ui, GameState state)
    {
        _crateHovered = !ui.ClickConsumed && ui.Hovering(_bounds);
        _revealHovered = _phase == Phase.Bobbing &&
                         !ui.ClickConsumed && ui.Hovering(_revealRect);

        if (_revealHovered && ui.MousePressed)
        {
            ui.ClickConsumed = true;
            return CrateAction.Redeem;
        }

        if (_crateHovered)
        {
            if (state.PendingRevealId is not null)
                ui.SetTooltip("Claim the floating drink first", _bounds);
            else if (!state.CanOpenPack)
                ui.SetTooltip("Sell bottles to earn crate tokens", _bounds);
            else
                ui.SetTooltip("Open a supply crate", _bounds);

            if (ui.MousePressed)
            {
                ui.ClickConsumed = true;
                if (state.CanOpenPack) return CrateAction.Open;
            }
        }

        return CrateAction.None;
    }
}
