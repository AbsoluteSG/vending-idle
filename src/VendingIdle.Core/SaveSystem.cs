using System;
using System.IO;
using System.Text.Json;

namespace VendingIdle.Core;

public static class SaveSystem
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = false
    };

    /// <summary>%LOCALAPPDATA%\VendingIdle\save.json (or ~/.local/share on Linux).</summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VendingIdle");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "save.json");

    public static void Save(GameState state, string? path = null)
    {
        path ??= DefaultPath;
        state.LastSavedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Write to a temp file first so a crash mid-write cannot shred a save.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Returns null when there is no save, or when the file is unreadable.</summary>
    public static GameState? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), Options);
            if (state is null) return null;

            state.Normalize();
            return state;
        }
        catch (Exception)
        {
            // A corrupt save should drop you into a new game, not crash the exe.
            return null;
        }
    }

    /// <summary>Seconds since the save was written, clamped at zero for clock skew.</summary>
    public static double SecondsSinceSave(GameState state)
    {
        if (state.LastSavedUnixSeconds <= 0) return 0.0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Math.Max(0.0, now - state.LastSavedUnixSeconds);
    }

    public static void Delete(string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path)) File.Delete(path);
    }
}
