using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// The room. Without somewhere to stand, the cabinet reads as another panel
/// rather than as an object -- a wall, a floor and a contact shadow are what sell
/// it as a thing in a place.
///
/// Split across the two spaces on purpose. The wall is drawn in screen space: it
/// stands for a surface far enough back that panning the camera up a tall cabinet
/// should not run off the top of its gradient. The floor and the shadow are drawn
/// in world space, because they are attached to the base of the machine and have
/// to slide away as the camera climbs.
/// </summary>
public static class Backdrop
{
    /// <summary>
    /// How far the room is painted past the edge of the screen. The camera shake
    /// slides the whole scene around by a few pixels, and without this the wall
    /// and floor would peel away from the edges and flash the clear colour.
    /// </summary>
    private const int Bleed = 24;

    /// <summary>Screen space: the far wall, and the glow the cabinet throws on it.</summary>
    public static void DrawWall(Ui ui, Rectangle screen, Rectangle machine, int pan)
    {
        var area = screen;
        area.Inflate(Bleed, Bleed);

        ui.P.GradientV(ui.Sb, area, Theme.WallTop, Theme.WallBottom);

        // The glow tracks the cabinet even though the wall does not, so a tall
        // machine stays lit along its whole height as you pan up it.
        var glow = new Rectangle(
            machine.Center.X - machine.Width,
            machine.Y - 140 + pan,
            machine.Width * 2,
            machine.Height + 180);

        ui.P.GlowRect(ui.Sb, glow, Theme.WallGlow * 0.42f);
    }

    /// <summary>World space: the floor the cabinet stands on, and its contact shadow.</summary>
    public static void DrawFloor(Ui ui, Rectangle screen, Rectangle machine, int floorY, int pan)
    {
        var area = screen;
        area.Inflate(Bleed, Bleed);

        // Expressed in world coordinates: the visible strip moves as the camera pans.
        var top = floorY;
        var bottom = area.Bottom - pan;
        if (bottom <= top) return;

        ui.P.GradientV(ui.Sb, new Rectangle(area.X, top, area.Width, bottom - top),
                       Theme.FloorFar, Theme.FloorNear);

        ui.P.Fill(ui.Sb, new Rectangle(area.X, floorY, area.Width, 1), Theme.FloorLine);

        // Contact shadow pooling under the cabinet.
        ui.P.GlowRect(ui.Sb,
            new Rectangle(machine.X - 54, floorY - 20, machine.Width + 108, 62),
            Theme.Shadow * 0.70f);

        // A tighter core right where the plinth meets the floor.
        ui.P.GlowRect(ui.Sb,
            new Rectangle(machine.X - 10, floorY - 12, machine.Width + 20, 26),
            Theme.Shadow * 0.55f);
    }
}
