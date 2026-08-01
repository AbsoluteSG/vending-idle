using Microsoft.Xna.Framework;

namespace VendingIdle.Render;

/// <summary>
/// Single palette for the whole UI. (Not to be confused with the machine "themes"
/// from the design doc -- those are a gameplay system and out of scope for v1.)
/// </summary>
public static class Theme
{
    public static readonly Color Background = new(18, 20, 28);
    public static readonly Color Panel = new(30, 34, 46);
    public static readonly Color PanelAlt = new(38, 43, 58);
    public static readonly Color PanelEdge = new(56, 63, 84);

    public static readonly Color MachineShell = new(46, 52, 70);
    public static readonly Color MachineShellDark = new(30, 34, 48);
    public static readonly Color MachineGlass = new(74, 88, 120);
    public static readonly Color Tray = new(22, 25, 34);

    public static readonly Color SlotEmpty = new(44, 49, 66);
    public static readonly Color SlotLocked = new(26, 29, 40);
    public static readonly Color SlotBuyable = new(52, 74, 66);

    public static readonly Color Text = new(232, 236, 245);
    public static readonly Color TextDim = new(146, 156, 178);
    public static readonly Color TextFaint = new(96, 105, 126);

    public static readonly Color Money = new(255, 208, 84);
    public static readonly Color Positive = new(88, 214, 141);
    public static readonly Color Negative = new(232, 106, 96);
    public static readonly Color Crit = new(255, 152, 200);
    public static readonly Color Accent = new(92, 172, 255);

    public static readonly Color ButtonIdle = new(52, 60, 82);
    public static readonly Color ButtonHover = new(68, 80, 108);
    public static readonly Color ButtonActive = new(88, 104, 140);
    public static readonly Color ButtonDisabled = new(36, 40, 52);

    public static readonly Color BuyIdle = new(40, 96, 72);
    public static readonly Color BuyHover = new(52, 124, 92);
    public static readonly Color BuyActive = new(64, 148, 110);

    /// <summary>Converts a packed 0xRRGGBB drink colour into a MonoGame colour.</summary>
    public static Color FromPacked(uint rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
