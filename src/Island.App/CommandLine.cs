namespace Island.App;

/// <summary>
/// Parsed command line: <c>--selftest &lt;folder&gt;</c>, <c>--data-dir &lt;folder&gt;</c> and <c>--quit</c> (asks the running copy to close).
/// Two more switches are used by the self-test on its own helpers, and both carry "--selftest" in their name:
/// <c>--selftest-copy</c> (the second copy of the one-copy check) and <c>--selftest-hold</c> (holds the real lock, key and port for a moment).
/// <c>--autostart</c> is what Windows's start-up list passes: the app comes up silently (tray icon and key, no island on the screen).
/// <c>--selftest &lt;folder&gt; --cost</c> is the measuring run of WORK-ORDER-6 section 6 (the island hidden for a minute with every reader on); it is not part of the ordinary self-test.
/// <c>--selftest &lt;folder&gt; --open</c> is the measuring run of WORK-ORDER-11 section 4 (what the open island costs; <c>--open-case</c>, <c>--open-label</c>); it is not part of the ordinary self-test.
/// <c>--selftest &lt;folder&gt; --speed</c> is the measuring run of WORK-ORDER-12 section 1 (how quickly the island answers; <c>--speed-label</c>); it is not part of the ordinary self-test.
/// <c>--render-assets &lt;root&gt;</c> draws the pictures of ship/ (the icon, the Store images) into the repository's folder and ends; it opens no window and starts nothing.
/// <c>--uninstall-cleanup</c> is what the uninstaller runs first (WORK-ORDER-14 section 3): Disconnect for every connected helper and "Start with Windows" off, then the program ends.
/// <c>--only looks</c> runs only the stage that draws the looks (for working on them; the full self-test is what counts at a checkpoint).
/// </summary>
internal sealed record CommandLine(string? SelfTestFolder, string? DataDir, bool Quit, bool SelfTestCopy = false, bool SelfTestHold = false, string? OnlyStage = null, bool Autostart = false, bool Cost = false, string? RenderAssets = null, bool Open = false, string? OpenCase = null, string OpenLabel = "before", bool Speed = false, string SpeedLabel = "before", string? OpenLight = null, bool SpeedWarm = false, bool UninstallCleanup = false)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        string? selfTest = null;
        string? dataDir = null;
        var quit = false;
        var copy = false;
        var hold = false;
        string? only = null;
        var autostart = false;
        var cost = false;
        string? assets = null;
        var open = false;
        string? openCase = null;
        var openLabel = "before";
        var speed = false;
        var speedLabel = "before";
        string? openLight = null;
        var speedWarm = false;
        var uninstallCleanup = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--selftest" when i + 1 < args.Count:
                    selfTest = args[++i];
                    break;
                case "--data-dir" when i + 1 < args.Count:
                    dataDir = args[++i];
                    break;
                case "--quit":
                    quit = true;
                    break;
                case "--autostart":
                    autostart = true;
                    break;
                case "--cost":
                    cost = true;
                    break;
                case "--open":
                    open = true;
                    break;
                case "--open-case" when i + 1 < args.Count:
                    openCase = args[++i];
                    break;
                case "--open-label" when i + 1 < args.Count:
                    openLabel = args[++i];
                    break;
                case "--open-light" when i + 1 < args.Count:
                    openLight = args[++i];
                    break;
                case "--speed-warm":
                    speedWarm = true;
                    break;
                case "--speed":
                    speed = true;
                    break;
                case "--speed-label" when i + 1 < args.Count:
                    speedLabel = args[++i];
                    break;
                case "--render-assets" when i + 1 < args.Count:
                    assets = args[++i];
                    break;
                case Island.Core.UninstallCleanup.Argument:
                    uninstallCleanup = true;
                    break;
                case "--selftest-copy":
                    copy = true;
                    break;
                case "--selftest-hold":
                    hold = true;
                    break;
                case "--only" when i + 1 < args.Count:
                    only = args[++i];
                    break;
            }
        }

        return new CommandLine(selfTest, dataDir, quit, copy, hold, only, autostart, cost, assets, open, openCase, openLabel, speed, speedLabel, openLight, speedWarm, uninstallCleanup);
    }
}
