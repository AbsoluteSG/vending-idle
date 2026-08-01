using System;
using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

public enum PanelSide
{
    Left,
    Right
}

/// <summary>
/// A drawer that slides in from a screen edge, with a permanent tab you grab it
/// by. The machine is the stage; this is the chrome that gets out of its way.
/// </summary>
public sealed class SlidePanel
{
    private const int TabWidth = 26;
    private const int TabHeight = 172;
    private const float SlideSpeed = 5.5f;

    private readonly PanelSide _side;
    private readonly int _width;
    private readonly string _label;

    /// <summary>0 = fully tucked away, 1 = fully out.</summary>
    private float _openness;

    public bool IsOpen { get; private set; }

    public SlidePanel(PanelSide side, int width, string label, bool open = false)
    {
        _side = side;
        _width = width;
        _label = label;
        IsOpen = open;
        _openness = open ? 1f : 0f;
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Toggle() => IsOpen = !IsOpen;

    public void Update(float dt)
    {
        var target = IsOpen ? 1f : 0f;
        var delta = SlideSpeed * dt;

        if (Math.Abs(target - _openness) <= delta) _openness = target;
        else _openness += Math.Sign(target - _openness) * delta;
    }

    /// <summary>True once the panel is far enough out to be worth drawing.</summary>
    public bool Visible => _openness > 0.001f;

    /// <summary>Smoothstep so the drawer eases into place instead of arriving flat.</summary>
    private float Eased => _openness * _openness * (3f - 2f * _openness);

    public Rectangle Bounds(Rectangle screen, int top, int height)
    {
        var offset = (int)((1f - Eased) * _width);

        var x = _side == PanelSide.Left
            ? screen.X - offset
            : screen.Right - _width + offset;

        return new Rectangle(x, top, _width, height);
    }

    /// <summary>
    /// Draws the grab tab and returns true when it was clicked. The tab rides the
    /// edge of the panel so it always looks attached to it.
    /// </summary>
    public bool DrawTab(Ui ui, Rectangle panel, Rectangle screen)
    {
        var y = panel.Y + (panel.Height - TabHeight) / 2;

        var x = _side == PanelSide.Left
            ? Math.Min(panel.Right, screen.Right - TabWidth)
            : Math.Max(panel.X - TabWidth, screen.X);

        var rect = new Rectangle(x, y, TabWidth, TabHeight);
        var hovered = !ui.ClickConsumed && ui.Hovering(rect);

        ui.P.FillRounded(ui.Sb, rect, 6, hovered ? Theme.ButtonHover : Theme.Panel);
        ui.P.OutlineRounded(ui.Sb, rect, 6, Theme.PanelEdge);

        // Chevron points the way the panel will move when clicked.
        var chevron = _side == PanelSide.Left
            ? (IsOpen ? "<" : ">")
            : (IsOpen ? ">" : "<");

        ui.T.DrawIn(ui.Sb, chevron,
            new Rectangle(rect.X, rect.Y + 6, rect.Width, 16),
            Theme.TextDim, FontSize.Small, Align.Center);

        // The label runs down the tab a character at a time -- SpriteBatch text
        // cannot rotate, and stacking reads fine at this width.
        var charHeight = (int)ui.T.LineHeight(FontSize.Small) - 5;
        var startY = rect.Y + 26;

        for (var i = 0; i < _label.Length; i++)
        {
            var slotRect = new Rectangle(rect.X, startY + i * charHeight, rect.Width, charHeight);
            if (slotRect.Bottom > rect.Bottom - 4) break;

            ui.T.DrawIn(ui.Sb, _label[i].ToString(), slotRect,
                        hovered ? Theme.Text : Theme.TextFaint, FontSize.Small, Align.Center);
        }

        if (!hovered || !ui.MousePressed) return false;

        ui.ClickConsumed = true;
        return true;
    }
}
