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
        T.DrawIn(Sb, T.Fit(label, rect.Width - 12, size), rect, fg, size, Align.Center);

        if (hover && tooltip is not null) SetTooltip(tooltip, rect);

        if (!enabled || !hover || !MousePressed) return false;

        ClickConsumed = true;
        return true;
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

    /// <summary>Label on the left, value on the right -- the workhorse stat row.</summary>
    public void StatRow(Rectangle rect, string label, string value,
                        Color? valueColor = null, FontSize size = FontSize.Small)
    {
        T.DrawIn(Sb, label, rect, Theme.TextDim, size, Align.Left);
        T.DrawIn(Sb, value, rect, valueColor ?? Theme.Text, size, Align.Right);
    }

    public void Separator(int x, int y, int width) =>
        P.Fill(Sb, new Rectangle(x, y, width, 1), Theme.PanelEdge);
}
