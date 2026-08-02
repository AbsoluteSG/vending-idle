using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VendingIdle.Render;

/// <summary>
/// The size ladder, smallest first. Every rung is rasterised at its own point
/// size by the content pipeline rather than scaled at draw time: the whole scene
/// is drawn with PointClamp, and a fractionally scaled bitmap glyph under point
/// sampling loses whole rows of pixels and turns to mush. Auto-fitting therefore
/// steps *down the ladder* instead of multiplying by a scale factor.
///
/// The order of these members is the ladder, so they must stay sorted by size.
/// </summary>
public enum FontSize
{
    Tiny,
    Small,
    Normal,
    Medium,
    Large,
    Huge
}

public enum Align
{
    Left,
    Center,
    Right
}

/// <summary>Thin wrapper over the three pipeline-built SpriteFonts.</summary>
public sealed class TextRenderer
{
    /// <summary>Indexed by <see cref="FontSize"/>, so the ladder is walkable by integer.</summary>
    private readonly SpriteFont[] _fonts;

    private static readonly FontSize Smallest = FontSize.Tiny;

    public TextRenderer(SpriteFont tiny, SpriteFont small, SpriteFont normal,
                        SpriteFont medium, SpriteFont large, SpriteFont huge)
    {
        _fonts = new[] { tiny, small, normal, medium, large, huge };
    }

    public SpriteFont Font(FontSize size) =>
        _fonts[Math.Clamp((int)size, 0, _fonts.Length - 1)];

    public float LineHeight(FontSize size = FontSize.Normal) => Font(size).LineSpacing;

    public Vector2 Measure(string text, FontSize size = FontSize.Normal) =>
        Font(size).MeasureString(text ?? string.Empty);

    public void Draw(SpriteBatch sb, string text, Vector2 position, Color color,
                     FontSize size = FontSize.Normal)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Rounding to whole pixels keeps the bitmap glyphs crisp.
        position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        sb.DrawString(Font(size), text, position, color);
    }

    /// <summary>
    /// The largest rung at or below <paramref name="size"/> whose text fits
    /// <paramref name="maxWidth"/>. Never steps *up*: a caller asking for Small
    /// has made a decision about visual hierarchy, and silently promoting it
    /// would redesign the screen rather than protect it.
    /// </summary>
    public FontSize FitSize(string text, float maxWidth, FontSize size = FontSize.Normal)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return size;

        while (size > Smallest && Measure(text, size).X > maxWidth)
            size--;

        return size;
    }

    /// <summary>
    /// Draws inside a rect with horizontal alignment and vertical centring.
    ///
    /// Shrinks to fit by default, then ellipsises if even the smallest rung
    /// overflows -- so no caller can spill text out of its container by
    /// accident. Only width is fitted: plenty of call sites hand over a tight
    /// rect and lean on the vertical centring to let glyphs bleed a little,
    /// and fitting height too would shrink half the UI for no reason.
    /// </summary>
    /// <param name="shrinkToFit">
    /// False pins the size, for the few places where a row has to line up with
    /// its neighbours more than it has to fit.
    /// </param>
    /// <param name="maxWidth">
    /// A tighter budget than the rect, for text sharing its row with something
    /// else -- the rect still positions it, but this is what it must fit inside.
    /// </param>
    public void DrawIn(SpriteBatch sb, string text, Rectangle rect, Color color,
                       FontSize size = FontSize.Normal, Align align = Align.Left, int padX = 0,
                       bool shrinkToFit = true, float? maxWidth = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (shrinkToFit)
        {
            var avail = maxWidth ?? rect.Width - padX * 2;
            size = FitSize(text, avail, size);
            text = Fit(text, avail, size);
        }

        var m = Measure(text, size);
        var x = align switch
        {
            Align.Center => rect.X + (rect.Width - m.X) / 2f,
            Align.Right => rect.Right - m.X - padX,
            _ => rect.X + padX
        };
        var y = rect.Y + (rect.Height - m.Y) / 2f;

        Draw(sb, text, new Vector2(x, y), color, size);
    }

    /// <summary>
    /// Positioned draw with a width budget: shrinks down the ladder first and
    /// only ellipsises once the smallest rung still will not fit. The rect-based
    /// <see cref="DrawIn"/> does this on its own; this is for the places that
    /// draw from a corner rather than inside a box.
    /// </summary>
    public void DrawWithin(SpriteBatch sb, string text, Vector2 position, Color color,
                           float maxWidth, FontSize size = FontSize.Normal)
    {
        if (string.IsNullOrEmpty(text)) return;

        size = FitSize(text, maxWidth, size);
        Draw(sb, Fit(text, maxWidth, size), position, color, size);
    }

    /// <summary>Truncates with an ellipsis so long drink names cannot overflow a card.</summary>
    public string Fit(string text, float maxWidth, FontSize size = FontSize.Normal)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (Measure(text, size).X <= maxWidth) return text;

        var trimmed = text;
        while (trimmed.Length > 1 && Measure(trimmed + "...", size).X > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + "...";
    }
}
