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

    // ---- The room the cabinet stands in ----------------------------------
    public static readonly Color WallTop = new(21, 23, 32);
    public static readonly Color WallBottom = new(38, 42, 56);
    public static readonly Color WallGlow = new(52, 74, 104);
    public static readonly Color FloorFar = new(52, 57, 72);
    public static readonly Color FloorNear = new(25, 27, 36);
    public static readonly Color FloorLine = new(74, 82, 102);
    public static readonly Color Shadow = new(0, 0, 0);

    // ---- The cabinet itself ----------------------------------------------
    public static readonly Color Chassis = new(48, 74, 99);
    public static readonly Color ChassisDark = new(30, 47, 65);
    public static readonly Color ChassisLight = new(70, 103, 133);
    public static readonly Color ChassisTrim = new(96, 134, 168);

    public static readonly Color Glass = new(16, 24, 34);
    public static readonly Color GlassEdge = new(72, 96, 122);
    public static readonly Color GlassSheen = new(150, 190, 230);

    public static readonly Color Shelf = new(54, 66, 82);
    public static readonly Color ShelfShade = new(24, 33, 45);

    public static readonly Color Led = new(9, 16, 13);
    public static readonly Color LedText = new(255, 196, 92);
    public static readonly Color LedDim = new(120, 92, 46);

    public static readonly Color MachineShellDark = new(30, 34, 48);
    public static readonly Color Tray = new(11, 14, 19);

    // ---- The supply crate -------------------------------------------------
    public static readonly Color CrateWood = new(112, 82, 54);
    public static readonly Color CrateDark = new(76, 54, 34);
    public static readonly Color CrateLight = new(148, 112, 76);

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
