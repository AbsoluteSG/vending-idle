using System;
using System.Globalization;

namespace VendingIdle;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        using var game = new VendingGame(options);
        game.Run();
    }
}

/// <summary>Command-line switches, used mostly for headless smoke testing.</summary>
public sealed class LaunchOptions
{
    /// <summary>When set, the game renders <see cref="ScreenshotFrames"/> frames, writes a PNG and exits.</summary>
    public string? ScreenshotPath { get; private set; }

    public int ScreenshotFrames { get; private set; } = 60;

    /// <summary>Override the save location (handy for testing without touching a real save).</summary>
    public string? SavePath { get; private set; }

    /// <summary>Start from scratch, ignoring any existing save.</summary>
    public bool FreshStart { get; private set; }

    public static LaunchOptions Parse(string[] args)
    {
        var o = new LaunchOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--screenshot" when i + 1 < args.Length:
                    o.ScreenshotPath = args[++i];
                    break;

                case "--frames" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var f))
                        o.ScreenshotFrames = Math.Max(1, f);
                    break;

                case "--save" when i + 1 < args.Length:
                    o.SavePath = args[++i];
                    break;

                case "--fresh":
                    o.FreshStart = true;
                    break;
            }
        }

        return o;
    }
}
