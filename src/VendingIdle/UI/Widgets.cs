using System;
using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

public enum ButtonStyle
{
    Normal,
    Buy,
    Subtle
}

public sealed partial class Ui
{
    public void Panel(Rectangle rect, string? title = null)
    {
        P.FillRounded(Sb, rect, 8, Theme.Panel);
        P.OutlineRounded(Sb, rect, 8, Theme.PanelEdge);

        if (title is null) return;

        var header = new Rectangle(rect.X, rect.Y, rect.Width, 30);
        T.DrawIn(Sb, title, header, Theme.TextDim, FontSize.Small, Align.Left, padX: 12);
        P.Fill(Sb, new Rectangle(rect.X + 10, rect.Y + 30, rect.Width - 20, 1), Theme.PanelEdge);
    }

    /// <summary>Content area of a panel drawn with a title.</summary>
    public static Rectangle PanelBody(Rectangle panel, int pad = 10) =>
        new(panel.X + pad, panel.Y + 34, panel.Width - pad * 2, panel.Height - 34 - pad);

    public bool Button(Rectangle rect, string label, bool enabled = true,
                       ButtonStyle style = ButtonStyle.Normal, string? tooltip = null,
                       FontSize size = FontSize.Normal)
    {
        var hover = !ClickConsumed && Hovering(rect);
        var held = hover && MouseDown;

        Color bg;
        if (!enabled)
            bg = Theme.ButtonDisabled;
        else if (style == ButtonStyle.Buy)
            bg = held ? Theme.BuyActive : hover ? Theme.BuyHover : Theme.BuyIdle;
        else if (style == ButtonStyle.Subtle)
            bg = held ? Theme.ButtonActive : hover ? Theme.ButtonHover : Theme.Panel;
        else
            bg = held ? Theme.ButtonActive : hover ? Theme.ButtonHover : Theme.ButtonIdle;

        P.FillRounded(Sb, rect, 6, bg);
        if (enabled && hover) P.OutlineRounded(Sb, rect, 6, Theme.PanelEdge);

        var fg = enabled ? Theme.Text : Theme.TextFaint;
        T.DrawIn(Sb, label, rect, fg, size, Align.Center, padX: 6);

        if (hover && tooltip is not null) SetTooltip(tooltip, rect);

        if (!hover || !MousePressed) return false;

        // A disabled button still swallows the click and reports the refusal --
        // it used to let the click fall straight through to whatever sat behind
        // it, and there was no way for the caller to know it had been pressed.
        ClickConsumed = true;

        if (!enabled)
        {
            ClickDenied = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The audio toggle that lives in the top-right corner. Drawn from primitives
    /// rather than a sprite: it is the only icon in the game, and one shape is not
    /// worth a texture, an importer entry and a second thing to keep in sync.
    /// </summary>
    /// <param name="available">
    /// False when there is no audio device at all. The button still draws -- a
    /// corner that silently loses its control is worse than a dead one -- but it
    /// greys out and reports the refusal rather than pretending to toggle.
    /// </param>
    public bool MuteButton(Rectangle rect, bool muted, bool available)
    {
        var hover = !ClickConsumed && Hovering(rect);
        var held = hover && MouseDown;

        var bg = !available ? Theme.ButtonDisabled
               : held ? Theme.ButtonActive
               : hover ? Theme.ButtonHover
               : Theme.Panel;

        P.FillRounded(Sb, rect, rect.Height / 2, bg);
        P.OutlineRounded(Sb, rect, rect.Height / 2, Theme.PanelEdge);

        var fg = !available ? Theme.TextFaint : muted ? Theme.TextDim : Theme.Text;
        Speaker(rect, fg, muted || !available);

        if (hover)
            SetTooltip(available ? (muted ? "Sound off  (M)" : "Sound on  (M)")
                                 : "No audio device", rect);

        if (!hover || !MousePressed) return false;

        ClickConsumed = true;

        if (!available)
        {
            ClickDenied = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// A speaker glyph centred in <paramref name="rect"/>: a box, a cone built
    /// from columns, and either two waves or the slash that cancels them.
    /// </summary>
    private void Speaker(Rectangle rect, Color color, bool silenced)
    {
        var cx = rect.Center.X;
        var cy = rect.Center.Y;

        // Box, then the cone flaring out of it. Columns rather than a triangle
        // primitive -- five of them read as a clean taper at this size.
        P.Fill(Sb, new Rectangle(cx - 9, cy - 3, 4, 6), color);

        for (var i = 0; i < 5; i++)
        {
            var h = 6 + i * 2;
            P.Fill(Sb, new Rectangle(cx - 5 + i, cy - h / 2, 1, h), color);
        }

        if (silenced)
        {
            // A cross beside the cone, not a slash across it. At 30 px a slash
            // runs straight through the cone and the glyph turns to mush; the
            // cross keeps the speaker legible and still reads as "off".
            var at = new Vector2(cx + 5, cy);
            P.FillRotated(Sb, at, new Vector2(11f, 2f), MathHelper.PiOver4, color);
            P.FillRotated(Sb, at, new Vector2(11f, 2f), -MathHelper.PiOver4, color);
            return;
        }

        P.Fill(Sb, new Rectangle(cx + 1, cy - 4, 2, 8), color);
        P.Fill(Sb, new Rectangle(cx + 5, cy - 7, 2, 14), color);
    }

    /// <summary>A click target with no chrome of its own -- used for the machine and slots.</summary>
    public bool Hotspot(Rectangle rect, out bool hover)
    {
        hover = !ClickConsumed && Hovering(rect);
        if (!hover || !MousePressed) return false;

        ClickConsumed = true;
        return true;
    }

    public void ProgressBar(Rectangle rect, float t, Color fill, Color? background = null)
    {
        t = MathHelper.Clamp(t, 0f, 1f);
        P.FillRounded(Sb, rect, Math.Min(4, rect.Height / 2), background ?? Theme.MachineShellDark);

        var w = (int)(rect.Width * t);
        if (w <= 0) return;

        P.FillRounded(Sb, new Rectangle(rect.X, rect.Y, Math.Max(w, rect.Height), rect.Height),
                      Math.Min(4, rect.Height / 2), fill);
    }

    /// <summary>
    /// Label on the left, value on the right -- the workhorse stat row. The two
    /// share one rect, so they have to share its width as well: fitting each
    /// against the full row independently is what lets a long label run under
    /// its own value and produce the overlap that reads as a rendering bug.
    ///
    /// The value has the stronger claim -- it is the number the player came to
    /// read -- so it is measured first and the label shrinks into the remainder.
    /// </summary>
    public void StatRow(Rectangle rect, string label, string value,
                        Color? valueColor = null, FontSize size = FontSize.Small)
    {
        const int Gap = 8;

        // Even the value is capped, so a runaway number cannot squeeze the label
        // out of existence entirely.
        var valueBudget = rect.Width * 0.65f;
        var valueSize = T.FitSize(value, valueBudget, size);
        var valueWidth = T.Measure(T.Fit(value, valueBudget, valueSize), valueSize).X;

        T.DrawIn(Sb, value, rect, valueColor ?? Theme.Text, size, Align.Right,
                 maxWidth: valueBudget);
        T.DrawIn(Sb, label, rect, Theme.TextDim, size, Align.Left,
                 maxWidth: rect.Width - valueWidth - Gap);
    }

    public void Separator(int x, int y, int width) =>
        P.Fill(Sb, new Rectangle(x, y, width, 1), Theme.PanelEdge);
}
