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
public sealed partial class Ui
{
    public SpriteBatch Sb { get; private set; } = null!;
    public Primitives P { get; }
    public TextRenderer T { get; }

    public Point Mouse { get; private set; }
    public bool MouseDown { get; private set; }
    public bool MousePressed { get; private set; }
    public bool MouseReleased { get; private set; }
    public int WheelDelta { get; private set; }

    /// <summary>
    /// Set once a widget has taken this frame's click, so a click on a panel
    /// cannot also fall through to the machine behind it.
    /// </summary>
    public bool ClickConsumed { get; set; }

    private MouseState _prev;
    private int _prevWheel;
    private bool _hasPrev;

    private string? _tooltip;
    private Rectangle _tooltipAnchor;

    private readonly GraphicsDevice _device;
    private readonly RasterizerState _scissorState = new() { ScissorTestEnable = true };
    private Rectangle _screen;

    public Ui(GraphicsDevice device, Primitives primitives, TextRenderer text)
    {
        _device = device;
        P = primitives;
        T = text;
    }

    public Rectangle Screen => _screen;

    public void BeginFrame(SpriteBatch sb, MouseState mouse, Rectangle screen)
    {
        Sb = sb;
        _screen = screen;

        Mouse = new Point(mouse.X, mouse.Y);
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
        _tooltip = null;
    }

    // ---- Batching ---------------------------------------------------------
    // All drawing goes through here so scissor clipping (used by the scrolling
    // machine grid) is a one-line push/pop rather than manual batch juggling.

    public void Begin(Rectangle? clip = null)
    {
        _device.ScissorRectangle = clip ?? _screen;
        Sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                 null, _scissorState);
    }

    public void End() => Sb.End();

    public void PushClip(Rectangle rect)
    {
        End();
        Begin(Rectangle.Intersect(rect, _screen));
    }

    public void PopClip()
    {
        End();
        Begin();
    }

    public bool Hovering(Rectangle rect) => rect.Contains(Mouse);

    /// <summary>Queues a tooltip; only the last one queued in a frame is drawn.</summary>
    public void SetTooltip(string text, Rectangle anchor)
    {
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
