using System;
using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>How loudly a moment announces itself.</summary>
public enum BannerTier
{
    /// <summary>Text near the machine. No overlay, nothing obscured.</summary>
    Near,

    /// <summary>Full-screen slam with a darkened surround.</summary>
    Slam,

    /// <summary>The slam, plus speed lines and a longer hold.</summary>
    Mega
}

/// <summary>
/// The announcement layer: the "CRAZY FIZZ" that lands across the screen when a
/// cascade runs long or something rare comes out of a crate.
///
/// Exactly one banner is on screen at a time, and that is the whole design. Late
/// game a shake across twenty slots can set off several qualifying cascades in
/// the same frame; six overlapping full-screen slams is not six times the impact,
/// it is an unreadable smear. A new banner either replaces the current one
/// (because it is louder) or is dropped -- it never stacks.
///
/// The overlay is a pair of gradients from the top and bottom edges rather than a
/// flat wash. The cabinet stays visible through the middle of the screen, which
/// is the difference between a moment of spectacle and simply losing the game
/// behind a black rectangle.
/// </summary>
public sealed class Banners
{
    private sealed class Banner
    {
        public string Text = "";
        public string? Subtitle;
        public Color Color;
        public BannerTier Tier;
        public float Life;
        public float MaxLife;
    }

    private Banner? _current;

    /// <summary>Drives the speed lines, so they sweep rather than sitting still.</summary>
    private float _clock;

    /// <summary>
    /// A full-screen colour wash that decays fast -- the hit at the instant a
    /// slam lands, separate from the banner's own fade.
    /// </summary>
    private Color _flashColor = Color.Transparent;
    private float _flash;

    public bool HasSlam => _current is { Tier: BannerTier.Slam or BannerTier.Mega };

    /// <summary>
    /// Queues an announcement. Louder tiers win outright; equal or quieter ones
    /// are dropped while something is already on screen, which is what stops a
    /// cascade storm from queueing thirty banners to play out over ten seconds
    /// long after the moment has passed.
    /// </summary>
    public void Show(string text, BannerTier tier, Color color, string? subtitle = null)
    {
        if (_current is not null && tier <= _current.Tier && _current.Life < _current.MaxLife * 0.6f)
            return;

        var banner = new Banner
        {
            Text = text,
            Subtitle = subtitle,
            Color = color,
            Tier = tier,
            Life = 0f,
            MaxLife = tier switch
            {
                BannerTier.Mega => 1.9f,
                BannerTier.Slam => 1.25f,
                _ => 0.85f
            }
        };

        // A louder banner takes the screen immediately; the old one is gone, not
        // deferred, because the thing it was announcing has been superseded.
        _current = banner;

        if (tier != BannerTier.Near)
        {
            _flashColor = color;
            _flash = tier == BannerTier.Mega ? 0.55f : 0.35f;
        }
    }

    public void Update(float dt)
    {
        _clock += dt;
        _flash = Math.Max(0f, _flash - dt * 2.6f);

        if (_current is null) return;

        _current.Life += dt;
        if (_current.Life >= _current.MaxLife) _current = null;
    }

    public void Clear()
    {
        _current = null;
        _flash = 0f;
    }

    /// <summary>
    /// Draws in screen space, above everything. <paramref name="machine"/> is
    /// where a Near banner sits, so it reads as belonging to the cabinet rather
    /// than to the window.
    /// </summary>
    public void Draw(Ui ui, Rectangle screen, Rectangle machine)
    {
        if (_flash > 0f)
            ui.P.Fill(ui.Sb, screen, _flashColor * (_flash * 0.30f));

        if (_current is null) return;

        var b = _current;
        var t = MathHelper.Clamp(b.Life / b.MaxLife, 0f, 1f);

        // Slam in fast, hold, then leave. The entry is the part that sells the
        // hit, so it gets the first eighth of the life and everything after the
        // three-quarter mark is the exit.
        var entry = MathHelper.Clamp(b.Life / (b.MaxLife * 0.12f), 0f, 1f);
        var exit = 1f - MathHelper.Clamp((t - 0.75f) / 0.25f, 0f, 1f);
        var alpha = Math.Min(entry, exit);

        if (alpha <= 0f) return;

        if (b.Tier == BannerTier.Near)
        {
            DrawNear(ui, machine, b, alpha);
            return;
        }

        DrawOverlay(ui, screen, b, alpha, entry);

        if (b.Tier == BannerTier.Mega) DrawSpeedLines(ui, screen, b, alpha);

        DrawSlamText(ui, screen, b, alpha, entry);
    }

    private static void DrawNear(Ui ui, Rectangle machine, Banner b, float alpha)
    {
        var rect = new Rectangle(machine.X, machine.Y - 46, machine.Width, 30);
        ui.T.DrawIn(ui.Sb, b.Text, rect, b.Color * alpha, FontSize.Medium, Align.Center);
    }

    /// <summary>
    /// Darkens the top and bottom of the screen and leaves the middle band clear.
    /// A flat wash would hide the cabinet, which is the thing the player is
    /// actually reacting to.
    /// </summary>
    private static void DrawOverlay(Ui ui, Rectangle screen, Banner b, float alpha, float entry)
    {
        var strength = (b.Tier == BannerTier.Mega ? 0.80f : 0.62f) * alpha;
        var band = (int)(screen.Height * 0.42f * entry);

        if (band <= 0) return;

        var top = new Rectangle(screen.X, screen.Y, screen.Width, band);
        var bottom = new Rectangle(screen.X, screen.Bottom - band, screen.Width, band);

        ui.P.GradientV(ui.Sb, top, Color.Black * strength, Color.Transparent);
        ui.P.GradientV(ui.Sb, bottom, Color.Transparent, Color.Black * strength);

        // A wash of the drink's own colour at the inner edge of each band, so the
        // overlay is tinted by whatever caused it rather than being generic.
        // Faded into the band rather than drawn as a hard rule: at full strength
        // the two edges read as scanlines slicing the cabinet in three.
        var edge = 10;
        ui.P.GradientV(ui.Sb, new Rectangle(top.X, top.Bottom - edge, top.Width, edge),
                       Color.Transparent, b.Color * (0.22f * alpha));
        ui.P.GradientV(ui.Sb, new Rectangle(bottom.X, bottom.Y, bottom.Width, edge),
                       b.Color * (0.22f * alpha), Color.Transparent);
    }

    private void DrawSpeedLines(Ui ui, Rectangle screen, Banner b, float alpha)
    {
        // Sixteen streaks sweeping across the bands. Cheap, and they read as
        // motion without needing a particle system of their own.
        const int lines = 16;

        for (var i = 0; i < lines; i++)
        {
            var phase = (_clock * 1.6f + i / (float)lines) % 1f;
            var y = screen.Y + (int)(phase * screen.Height);

            // Skipped through the middle band, which is the part kept readable.
            if (y > screen.Height * 0.35f && y < screen.Height * 0.65f) continue;

            var width = (int)(60 + 180 * ((i * 37) % 100) / 100f);
            var x = (int)((phase * 2.3f + i * 0.13f) % 1f * screen.Width) - width / 2;

            ui.P.Fill(ui.Sb, new Rectangle(x, y, width, 2), b.Color * (0.22f * alpha));
        }
    }

    private static void DrawSlamText(Ui ui, Rectangle screen, Banner b, float alpha, float entry)
    {
        // Overshoots and settles. The text arrives slightly oversized and snaps
        // back, which is what makes it land rather than simply appear.
        var overshoot = 1f + (1f - entry) * (1f - entry) * 0.6f;

        var size = overshoot > 1.25f ? FontSize.Large : FontSize.Huge;

        var band = new Rectangle(screen.X, screen.Center.Y - 60, screen.Width, 70);

        // Drawn twice: a dark pass offset down-right, then the colour on top. A
        // hard shadow is what keeps big text legible over a busy cabinet.
        var shadow = new Rectangle(band.X + 3, band.Y + 3, band.Width, band.Height);
        ui.T.DrawIn(ui.Sb, b.Text, shadow, Color.Black * (0.75f * alpha), size, Align.Center);
        ui.T.DrawIn(ui.Sb, b.Text, band, b.Color * alpha, size, Align.Center);

        if (b.Subtitle is null) return;

        var sub = new Rectangle(screen.X, band.Bottom + 2, screen.Width, 26);
        ui.T.DrawIn(ui.Sb, b.Subtitle, sub, Theme.Text * (0.9f * alpha),
                    FontSize.Medium, Align.Center);
    }
}
