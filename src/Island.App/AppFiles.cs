using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// Where the app keeps its two files: settings in %APPDATA%\Island\settings.json and the log in
/// %LOCALAPPDATA%\Island\island.log. With <c>--data-dir</c> both go into that one folder instead, so
/// the self-test never touches the real ones.
/// </summary>
internal sealed class AppFiles
{
    public AppFiles(string? dataDirOverride)
    {
        if (dataDirOverride is not null)
        {
            SettingsPath = Path.Combine(dataDirOverride, "settings.json");
            PicksPath = Path.Combine(dataDirOverride, "picks.json");
            PagesPath = Path.Combine(dataDirOverride, "pages.json");
            ScenesPath = Path.Combine(dataDirOverride, "scenes.json");
            LogPath = Path.Combine(dataDirOverride, "island.log");
        }
        else
        {
            SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "settings.json");
            PicksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "picks.json");
            PagesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "pages.json");
            ScenesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "scenes.json");
            LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Island", "island.log");
        }
    }

    public string SettingsPath { get; }

    /// <summary>The picks: %APPDATA%\Island\picks.json. Names only, never a path.</summary>
    public string PicksPath { get; }

    /// <summary>The pages Dan made (name, colour, key): %APPDATA%\Island\pages.json.</summary>
    public string PagesPath { get; }

    /// <summary>The scenes (names and things, never a path): %APPDATA%\Island\scenes.json.</summary>
    public string ScenesPath { get; }

    public string LogPath { get; }

    private const long MaxLogBytes = 512 * 1024;

    private readonly LogThrottle _throttle = new();

    /// <summary>One line per event: time, then text. Never a window title, a process list or a user name.</summary>
    public void Log(string line)
    {
        if (!_throttle.ShouldWrite(line, Environment.TickCount64)) return; // the same line over and over (an error on every frame) is written once in a few seconds
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (new FileInfo(LogPath) is { Exists: true, Length: > MaxLogBytes }) File.Move(LogPath, LogPath + ".old", overwrite: true); // one earlier log is kept, so the log never grows without end
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must never take the app down.
        }
    }
}
