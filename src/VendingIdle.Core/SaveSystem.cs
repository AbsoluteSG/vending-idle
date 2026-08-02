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

    /// <summary>
    /// Where the save lives, per platform convention:
    /// <list type="bullet">
    /// <item>Windows: %LOCALAPPDATA%\VendingIdle</item>
    /// <item>macOS: ~/Library/Application Support/VendingIdle</item>
    /// <item>Linux: ~/.local/share/VendingIdle</item>
    /// </list>
    /// macOS is special-cased because .NET maps LocalApplicationData to
    /// ~/.local/share there, which is a Linux convention no Mac user expects.
    /// </summary>
    public static string DefaultDirectory
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    return Path.Combine(home, "Library", "Application Support", "VendingIdle");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VendingIdle");
        }
    }

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

            // Migrate before normalizing: Normalize reads slot indices against the
            // current grid width, which an older save's indices predate.
            state.Migrate();
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

    /// <summary>
    /// Copies the save aside before something destroys it. Returns the backup
    /// path, or null when there was nothing to copy.
    ///
    /// Deliberately best-effort: this exists to make an irreversible action
    /// recoverable, so a failure to write the copy must never stop the caller or
    /// take the game down with it.
    /// </summary>
    public static string? Backup(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;

        var backup = path + ".bak";

        try
        {
            File.Copy(path, backup, overwrite: true);
            return backup;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
