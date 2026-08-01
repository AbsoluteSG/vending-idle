using Microsoft.Xna.Framework;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// The room. Without somewhere to stand, the cabinet reads as another panel
/// rather than as an object -- a wall, a floor and a contact shadow are what sell
/// it as a thing in a place.
/// </summary>
public static class Backdrop
{
    public static void Draw(Ui ui, Rectangle screen, Rectangle machine, int floorY)
    {
        // Wall, lit slightly from behind the machine.
        ui.P.GradientV(ui.Sb, new Rectangle(screen.X, screen.Y, screen.Width, floorY),
                       Theme.WallTop, Theme.WallBottom);

        var glow = new Rectangle(
            machine.Center.X - machine.Width,
            machine.Y - 140,
            machine.Width * 2,
            machine.Height + 180);

        ui.P.GlowRect(ui.Sb, glow, Theme.WallGlow * 0.42f);

        // Floor, receding away from the viewer.
        ui.P.GradientV(ui.Sb, new Rectangle(screen.X, floorY, screen.Width, screen.Bottom - floorY),
                       Theme.FloorFar, Theme.FloorNear);

        ui.P.Fill(ui.Sb, new Rectangle(screen.X, floorY, screen.Width, 1), Theme.FloorLine);

        // Contact shadow pooling under the cabinet.
        var shadow = new Rectangle(
            machine.X - 54,
            floorY - 20,
            machine.Width + 108,
            62);

        ui.P.GlowRect(ui.Sb, shadow, Theme.Shadow * 0.70f);

        // A tighter core right where the plinth meets the floor.
        ui.P.GlowRect(ui.Sb,
            new Rectangle(machine.X - 10, floorY - 12, machine.Width + 20, 26),
            Theme.Shadow * 0.55f);
    }
}
