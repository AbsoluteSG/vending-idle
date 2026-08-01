using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// Click feedback: rising payout popups and cans tumbling into the tray.
/// Purely cosmetic -- the simulation never reads any of this.
/// </summary>
public sealed class Effects
{
    private const int MaxPopups = 40;
    private const int MaxCans = 40;

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

    private struct Can
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public Color Color;
        public float TargetY;
        public bool Landed;
    }

    private readonly List<Popup> _popups = new();
    private readonly List<Can> _cans = new();
    private readonly Random _rng = new();

    /// <summary>Tray flash intensity, 0..1, decaying each frame.</summary>
    public float TrayFlash { get; private set; }

    public void SpawnPopup(string text, Vector2 position, Color color, FontSize size = FontSize.Normal)
    {
        if (_popups.Count >= MaxPopups) return;

        var drift = (float)(_rng.NextDouble() * 24.0 - 12.0);
        _popups.Add(new Popup
        {
            Text = text,
            Position = position,
            Velocity = new Vector2(drift, -46f),
            Life = 0f,
            MaxLife = size == FontSize.Large ? 1.15f : 0.85f,
            Color = color,
            Size = size
        });
    }

    public void SpawnCan(Vector2 from, float trayY, Color color)
    {
        if (_cans.Count >= MaxCans) return;

        _cans.Add(new Can
        {
            Position = from,
            Velocity = new Vector2((float)(_rng.NextDouble() * 60.0 - 30.0), 20f),
            Life = 0f,
            MaxLife = 1.4f,
            Color = color,
            TargetY = trayY,
            Landed = false
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
            p.Velocity *= 1f - 1.6f * dt;   // ease out as it rises
            _popups[i] = p;
        }

        for (var i = _cans.Count - 1; i >= 0; i--)
        {
            var c = _cans[i];
            c.Life += dt;
            if (c.Life >= c.MaxLife)
            {
                _cans.RemoveAt(i);
                continue;
            }

            if (!c.Landed)
            {
                c.Velocity.Y += 1400f * dt;
                c.Position += c.Velocity * dt;

                if (c.Position.Y >= c.TargetY)
                {
                    c.Position.Y = c.TargetY;
                    c.Landed = true;
                    c.Velocity = Vector2.Zero;
                }
            }

            _cans[i] = c;
        }
    }

    public void Draw(Ui ui)
    {
        foreach (var c in _cans)
        {
            var t = c.Life / c.MaxLife;
            // Cans stay solid while falling and only fade once they have landed.
            var alpha = c.Landed ? MathHelper.Clamp(1f - (t - 0.5f) * 2.4f, 0f, 1f) : 1f;
            if (alpha <= 0f) continue;

            var rect = new Rectangle((int)c.Position.X - 5, (int)c.Position.Y - 8, 10, 16);
            ui.P.FillRounded(ui.Sb, rect, 3, c.Color * alpha);
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
        _cans.Clear();
        TrayFlash = 0f;
    }
}
