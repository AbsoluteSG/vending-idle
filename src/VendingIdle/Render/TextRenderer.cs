using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VendingIdle.Render;

public enum FontSize
{
    Small,
    Normal,
    Large
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
    private readonly SpriteFont _small;
    private readonly SpriteFont _normal;
    private readonly SpriteFont _large;

    public TextRenderer(SpriteFont small, SpriteFont normal, SpriteFont large)
    {
        _small = small;
        _normal = normal;
        _large = large;
    }

    public SpriteFont Font(FontSize size) => size switch
    {
        FontSize.Small => _small,
        FontSize.Large => _large,
        _ => _normal
    };

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

    /// <summary>Draws inside a rect with horizontal alignment and vertical centring.</summary>
    public void DrawIn(SpriteBatch sb, string text, Rectangle rect, Color color,
                       FontSize size = FontSize.Normal, Align align = Align.Left, int padX = 0)
    {
        if (string.IsNullOrEmpty(text)) return;

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
