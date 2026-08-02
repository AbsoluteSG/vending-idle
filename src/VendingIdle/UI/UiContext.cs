using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using VendingIdle.Render;

namespace VendingIdle.UI;

/// <summary>
/// Immediate-mode UI context. There is no retained widget tree: every panel is a
/// function of the game state, redrawn from scratch each frame. For a prototype
/// whose numbers and layout change constantly, that is far less to keep in sync.
/// </summary>
/// <summary>
/// Which coordinate space a batch is drawing in. World content (the room, the
/// cabinet, falling bottles) is laid out around a fixed floor line and viewed
/// through a camera that pans as the machine grows past the screen. Screen
/// content (drawers, tooltips, wall text) ignores the camera and stays put.
/// </summary>
public enum Space
{
    World,
    Screen
}

public sealed partial class Ui
{
    public SpriteBatch Sb { get; private set; } = null!;
    public Primitives P { get; }
    public TextRenderer T { get; }

    /// <summary>
    /// Cursor in the space of the batch currently open, so a hit test reads the
    /// same coordinates the drawing did. World-space tests see the pan applied;
    /// screen-space ones do not.
    /// </summary>
    public Point Mouse => _space == Space.World ? _mouseWorld : _mouseScreen;

    /// <summary>Raw cursor, never adjusted for the camera.</summary>
    public Point MouseScreen => _mouseScreen;
    public bool MouseDown { get; private set; }
    public bool MousePressed { get; private set; }
    public bool MouseReleased { get; private set; }
    public int WheelDelta { get; private set; }

    /// <summary>
    /// Set once a widget has taken this frame's click, so a click on a panel
    /// cannot also fall through to the machine behind it.
    /// </summary>
    public bool ClickConsumed { get; set; }

    /// <summary>
    /// Set when this frame's click landed on a widget that refused it -- a button
    /// greyed out because the player cannot afford what is behind it. Read once
    /// per frame to sound the refusal.
    /// </summary>
    public bool ClickDenied { get; set; }

    private MouseState _prev;
    private int _prevWheel;
    private bool _hasPrev;

    private string? _tooltip;
    private Rectangle _tooltipAnchor;

    private readonly GraphicsDevice _device;
    private readonly RasterizerState _scissorState = new() { ScissorTestEnable = true };
    private Rectangle _screen;

    private Point _mouseScreen;
    private Point _mouseWorld;

    /// <summary>
    /// Camera pan. Input follows this -- the machine can be thousands of pixels
    /// tall and a click has to land on the compartment you can actually see.
    /// </summary>
    private Point _pan;

    /// <summary>
    /// Shake displacement, deliberately kept out of input: a rattle must never
    /// jog a click off the button you aimed at.
    /// </summary>
    private Point _shake;

    private Space _space = Space.Screen;
    private bool _batchOpen;

    public Ui(GraphicsDevice device, Primitives primitives, TextRenderer text)
    {
        _device = device;
        P = primitives;
        T = text;
    }

    public Rectangle Screen => _screen;

    /// <summary>The part of the world the camera can currently see, for culling.</summary>
    public Rectangle VisibleWorld =>
        new(_screen.X - _pan.X, _screen.Y - _pan.Y, _screen.Width, _screen.Height);

    public int Pan => _pan.Y;

    /// <summary>
    /// Rounded to whole pixels: the whole scene is drawn with PointClamp, and a
    /// fractional camera would smear every hard edge in it.
    /// </summary>
    public void BeginFrame(SpriteBatch sb, MouseState mouse, Rectangle screen,
                           Vector2 pan = default, Vector2 shake = default)
    {
        Sb = sb;
        _screen = screen;
        _pan = new Point((int)MathF.Round(pan.X), (int)MathF.Round(pan.Y));
        _shake = new Point((int)MathF.Round(shake.X), (int)MathF.Round(shake.Y));
        _space = Space.Screen;
        _batchOpen = false;

        _mouseScreen = new Point(mouse.X, mouse.Y);
        _mouseWorld = new Point(mouse.X - _pan.X, mouse.Y - _pan.Y);
        MouseDown = mouse.LeftButton == ButtonState.Pressed;

        // First frame has no history; treat it as "nothing happened" so a click
        // held from launch does not register.
        MousePressed = _hasPrev && MouseDown && _prev.LeftButton == ButtonState.Released;
        MouseReleased = _hasPrev && !MouseDown && _prev.LeftButton == ButtonState.Pressed;
        WheelDelta = _hasPrev ? mouse.ScrollWheelValue - _prevWheel : 0;

        _prev = mouse;
        _prevWheel = mouse.ScrollWheelValue;
        _hasPrev = true;

        ClickConsumed = false;
        ClickDenied = false;
        _tooltip = null;
    }

    // ---- Batching ---------------------------------------------------------
    // All drawing goes through here so scissor clipping (used by the scrolling
    // machine grid) is a one-line push/pop rather than manual batch juggling.

    /// <summary>
    /// Opens a batch in the given space. Everything drawn until the next call is
    /// transformed (or not) to match. Switching space mid-frame closes whatever
    /// batch is open first -- callers flip between world and screen several times
    /// a frame and should not have to pair every one with an End.
    /// </summary>
    public void Begin(Space space, Rectangle? clip = null)
    {
        if (_batchOpen) End();

        _space = space;
        _batchOpen = true;

        // Screen content still rides the shake -- the whole picture shakes, drawers
        // included -- but never the pan, which is what keeps the UI anchored while
        // the camera climbs the cabinet.
        var offset = space == Space.World
            ? new Point(_pan.X + _shake.X, _pan.Y + _shake.Y)
            : _shake;

        // The scissor rectangle is not covered by the sprite transform, so an
        // explicit clip -- given in the current space -- has to be moved by hand to
        // stay over the content it is clipping.
        //
        // With no clip the scissor is the whole viewport and is NOT offset. It is
        // already in screen coordinates, and translating it by the camera would
        // shrink the drawable area to wherever the camera happened to be pointing:
        // at full pan up a tall cabinet that left a narrow band at the bottom of
        // the screen as the only place anything could be painted.
        var scissor = _device.Viewport.Bounds;

        if (clip.HasValue)
        {
            var c = clip.Value;
            c.Offset(offset);
            scissor = Rectangle.Intersect(c, scissor);
        }

        _device.ScissorRectangle = scissor;

        Sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                 null, _scissorState, null,
                 Matrix.CreateTranslation(offset.X, offset.Y, 0f));
    }

    public void Begin() => Begin(_space);

    public void End()
    {
        if (!_batchOpen) return;

        _batchOpen = false;
        Sb.End();
    }

    /// <summary>Clips within the current space. The rect is in that space's coordinates.</summary>
    public void PushClip(Rectangle rect)
    {
        End();

        // Intersect against the visible region expressed in the current space, so
        // a world clip high above the screen does not survive as a stale rect.
        var bounds = _space == Space.World
            ? new Rectangle(_screen.X - _pan.X, _screen.Y - _pan.Y, _screen.Width, _screen.Height)
            : _screen;

        Begin(_space, Rectangle.Intersect(rect, bounds));
    }

    public void PopClip()
    {
        End();
        Begin(_space);
    }

    public bool Hovering(Rectangle rect) => rect.Contains(Mouse);

    /// <summary>
    /// Queues a tooltip; only the last one queued in a frame is drawn. The anchor
    /// is converted to screen space here, at the point where the space it was
    /// measured in is still known -- tooltips are always drawn last, on top of
    /// everything, long after the batch that produced the anchor has closed.
    /// </summary>
    public void SetTooltip(string text, Rectangle anchor)
    {
        if (_space == Space.World) anchor.Offset(_pan);

        _tooltip = text;
        _tooltipAnchor = anchor;
    }

    /// <summary>Drawn last so it sits above every panel.</summary>
    public void DrawTooltip(Rectangle screen)
    {
        if (_tooltip is null) return;

        const int padX = 8;
        const int padY = 5;
        var size = T.Measure(_tooltip, FontSize.Small);
        var w = (int)size.X + padX * 2;
        var h = (int)size.Y + padY * 2;

        var x = _tooltipAnchor.X;
        var y = _tooltipAnchor.Y - h - 6;

        // Keep it on screen: flip below the anchor if it would clip the top edge.
        if (y < screen.Y) y = _tooltipAnchor.Bottom + 6;
        if (x + w > screen.Right) x = screen.Right - w - 4;
        if (x < screen.X) x = screen.X + 4;

        var rect = new Rectangle(x, y, w, h);
        P.FillRounded(Sb, rect, 5, new Color(12, 14, 20));
        P.OutlineRounded(Sb, rect, 5, Theme.PanelEdge);
        T.DrawIn(Sb, _tooltip, rect, Theme.Text, FontSize.Small, Align.Center);
    }
}
