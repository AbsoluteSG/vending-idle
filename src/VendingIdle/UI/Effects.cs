using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// Click feedback: bottles tumbling out of the rack into the tray, and rising
/// payout text. Purely cosmetic -- the simulation never reads any of this.
/// </summary>
public sealed class Effects
{
    private const int MaxPopups = 32;
    private const int MaxBottles = 64;
    private const float Gravity = 1900f;

    private struct Popup
    {
        public string Text;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public Color Color;
        public FontSize Size;
    }

    private struct Bottle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Size;
        public float Rotation;
        public float Spin;
        public float Life;
        public float MaxLife;
        public Color Color;
        public float FloorY;
        public int Bounces;
        public bool Resting;
    }

    private readonly List<Popup> _popups = new();
    private readonly List<Bottle> _bottles = new();
    private readonly Random _rng = new();

    public float TrayFlash { get; private set; }

    public void SpawnPopup(string text, Vector2 position, Color color, FontSize size = FontSize.Normal)
    {
        if (_popups.Count >= MaxPopups) return;

        var drift = (float)(_rng.NextDouble() * 22.0 - 11.0);
        _popups.Add(new Popup
        {
            Text = text,
            Position = position,
            Velocity = new Vector2(drift, -50f),
            Life = 0f,
            MaxLife = size == FontSize.Large ? 1.1f : 0.8f,
            Color = color,
            Size = size
        });
    }

    /// <summary>
    /// Drops a bottle from where it sat in the rack. It falls under gravity,
    /// bounces off the tray floor and settles there before fading.
    /// </summary>
    public void SpawnBottle(Rectangle from, float floorY, Color color)
    {
        if (_bottles.Count >= MaxBottles) return;

        var size = new Vector2(Math.Max(5f, from.Width), Math.Max(5f, from.Height));

        _bottles.Add(new Bottle
        {
            Position = new Vector2(from.Center.X, from.Center.Y),
            Velocity = new Vector2((float)(_rng.NextDouble() * 90.0 - 45.0),
                                   (float)(_rng.NextDouble() * -60.0)),
            Size = size,
            Rotation = 0f,
            Spin = (float)(_rng.NextDouble() * 10.0 - 5.0),
            Life = 0f,
            MaxLife = 2.0f,
            Color = color,
            FloorY = floorY - size.X * 0.5f,
            Bounces = 0,
            Resting = false
        });
    }

    public void FlashTray() => TrayFlash = 1f;

    public void Update(float dt)
    {
        TrayFlash = Math.Max(0f, TrayFlash - dt * 3.5f);

        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            p.Life += dt;
            if (p.Life >= p.MaxLife)
            {
                _popups.RemoveAt(i);
                continue;
            }

            p.Position += p.Velocity * dt;
            p.Velocity *= 1f - 1.6f * dt;
            _popups[i] = p;
        }

        for (var i = _bottles.Count - 1; i >= 0; i--)
        {
            var b = _bottles[i];
            b.Life += dt;
            if (b.Life >= b.MaxLife)
            {
                _bottles.RemoveAt(i);
                continue;
            }

            if (!b.Resting)
            {
                b.Velocity.Y += Gravity * dt;
                b.Position += b.Velocity * dt;
                b.Rotation += b.Spin * dt;

                if (b.Position.Y >= b.FloorY)
                {
                    b.Position.Y = b.FloorY;
                    b.Bounces++;

                    if (b.Bounces >= 2 || Math.Abs(b.Velocity.Y) < 140f)
                    {
                        // Settle flat in the tray rather than resting on a corner.
                        b.Resting = true;
                        b.Velocity = Vector2.Zero;
                        b.Spin = 0f;

                        // Drinks come to rest lying on their side in the tray,
                        // which is the only way a tall bottle settles believably.
                        b.Rotation = MathHelper.PiOver2;
                    }
                    else
                    {
                        b.Velocity.Y *= -0.42f;
                        b.Velocity.X *= 0.6f;
                        b.Spin *= 0.5f;
                    }
                }
            }

            _bottles[i] = b;
        }
    }

    public void Draw(Ui ui)
    {
        foreach (var b in _bottles)
        {
            // Bottles stay solid through the fall and only fade once they land.
            var remaining = b.MaxLife - b.Life;
            var alpha = MathHelper.Clamp(remaining / 0.5f, 0f, 1f);
            if (alpha <= 0f) continue;

            ui.P.FillRotated(ui.Sb, b.Position, b.Size, b.Rotation, b.Color * alpha);
        }

        foreach (var p in _popups)
        {
            var t = p.Life / p.MaxLife;
            var alpha = MathHelper.Clamp(1f - t * t, 0f, 1f);
            ui.T.Draw(ui.Sb, p.Text, p.Position, p.Color * alpha, p.Size);
        }
    }

    public void Clear()
    {
        _popups.Clear();
        _bottles.Clear();
        TrayFlash = 0f;
    }
}
