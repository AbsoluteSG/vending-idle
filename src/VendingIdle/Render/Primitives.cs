using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VendingIdle.Render;

/// <summary>
/// The prototype ships no art. Every shape is drawn from a handful of textures
/// generated at startup, which keeps the repo asset-free while leaving the
/// content pipeline wired up for real sprites later.
/// </summary>
public sealed class Primitives : IDisposable
{
    private const int CornerSize = 24;

    private readonly Texture2D _pixel;
    private readonly Texture2D _roundedCorner;
    private readonly Texture2D _radial;

    public Primitives(GraphicsDevice device)
    {
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _roundedCorner = BuildCorner(device, CornerSize);
        _radial = BuildRadial(device, 64);
    }

    /// <summary>Top-left quarter-disc, alpha-antialiased; mirrored for the other three.</summary>
    private static Texture2D BuildCorner(GraphicsDevice device, int size)
    {
        var data = new Color[size * size];
        var r = size;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            // Distance from the circle centre, which sits at the inner corner.
            var dx = r - (x + 0.5f);
            var dy = r - (y + 0.5f);
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            // One-pixel smoothstep band gives a clean edge at any scale.
            var alpha = MathHelper.Clamp(r - dist + 0.5f, 0f, 1f);
            data[y * size + x] = Color.White * alpha;
        }

        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }

    private static Texture2D BuildRadial(GraphicsDevice device, int size)
    {
        var data = new Color[size * size];
        var c = (size - 1) / 2f;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x - c) / c;
            var dy = (y - c) / c;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            var a = MathHelper.Clamp(1f - d, 0f, 1f);
            data[y * size + x] = Color.White * (a * a);
        }

        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }

    public void Fill(SpriteBatch sb, Rectangle rect, Color color) =>
        sb.Draw(_pixel, rect, color);

    public void Fill(SpriteBatch sb, float x, float y, float w, float h, Color color) =>
        sb.Draw(_pixel, new Rectangle((int)x, (int)y, (int)w, (int)h), color);

    public void Outline(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        Fill(sb, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        Fill(sb, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        Fill(sb, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        Fill(sb, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    /// <summary>Rounded rectangle: four mirrored corner quads plus three fill rects.</summary>
    public void FillRounded(SpriteBatch sb, Rectangle rect, int radius, Color color)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        if (radius <= 0)
        {
            Fill(sb, rect, color);
            return;
        }

        var d = radius;
        var src = new Rectangle(0, 0, CornerSize, CornerSize);

        sb.Draw(_roundedCorner, new Rectangle(rect.X, rect.Y, d, d), src, color);
        sb.Draw(_roundedCorner, new Rectangle(rect.Right - d, rect.Y, d, d), src, color,
                0f, Vector2.Zero, SpriteEffects.FlipHorizontally, 0f);
        sb.Draw(_roundedCorner, new Rectangle(rect.X, rect.Bottom - d, d, d), src, color,
                0f, Vector2.Zero, SpriteEffects.FlipVertically, 0f);
        sb.Draw(_roundedCorner, new Rectangle(rect.Right - d, rect.Bottom - d, d, d), src, color,
                0f, Vector2.Zero, SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 0f);

        Fill(sb, new Rectangle(rect.X + d, rect.Y, rect.Width - d * 2, d), color);
        Fill(sb, new Rectangle(rect.X + d, rect.Bottom - d, rect.Width - d * 2, d), color);
        Fill(sb, new Rectangle(rect.X, rect.Y + d, rect.Width, rect.Height - d * 2), color);
    }

    public void OutlineRounded(SpriteBatch sb, Rectangle rect, int radius, Color color, int thickness = 1)
    {
        // Cheap but effective: a slightly larger rounded rect with the interior
        // punched out by the caller's background colour is fiddly, so instead we
        // draw straight edges and accept square-ish joins under the corner arcs.
        radius = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        Fill(sb, new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, thickness), color);
        Fill(sb, new Rectangle(rect.X + radius, rect.Bottom - thickness, rect.Width - radius * 2, thickness), color);
        Fill(sb, new Rectangle(rect.X, rect.Y + radius, thickness, rect.Height - radius * 2), color);
        Fill(sb, new Rectangle(rect.Right - thickness, rect.Y + radius, thickness, rect.Height - radius * 2), color);
    }

    /// <summary>Vertical two-stop gradient, drawn as one-pixel rows.</summary>
    public void GradientV(SpriteBatch sb, Rectangle rect, Color top, Color bottom)
    {
        if (rect.Height <= 0) return;

        for (var y = 0; y < rect.Height; y++)
        {
            var t = rect.Height == 1 ? 0f : y / (float)(rect.Height - 1);
            Fill(sb, new Rectangle(rect.X, rect.Y + y, rect.Width, 1), Color.Lerp(top, bottom, t));
        }
    }

    /// <summary>
    /// Axis-free square, used for bottles tumbling down the machine. Rotation is
    /// why these are drawn from the raw pixel rather than through FillRounded.
    /// </summary>
    public void FillRotated(SpriteBatch sb, Vector2 center, float size, float rotation, Color color)
    {
        sb.Draw(_pixel, center, null, color, rotation,
                new Vector2(0.5f), size, SpriteEffects.None, 0f);
    }

    /// <summary>Rotated rectangle -- a tumbling bottle is taller than it is wide.</summary>
    public void FillRotated(SpriteBatch sb, Vector2 center, Vector2 size, float rotation, Color color)
    {
        sb.Draw(_pixel, center, null, color, rotation,
                new Vector2(0.5f), size, SpriteEffects.None, 0f);
    }

    /// <summary>Soft glow, used for crit pops and the dispense flash.</summary>
    public void Glow(SpriteBatch sb, Vector2 center, float radius, Color color)
    {
        var d = (int)(radius * 2);
        sb.Draw(_radial, new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d), color);
    }

    /// <summary>Stretched radial falloff -- the cabinet's contact shadow and wall glow.</summary>
    public void GlowRect(SpriteBatch sb, Rectangle rect, Color color) =>
        sb.Draw(_radial, rect, color);

    public Texture2D Pixel => _pixel;

    public void Dispose()
    {
        _pixel.Dispose();
        _roundedCorner.Dispose();
        _radial.Dispose();
    }
}
