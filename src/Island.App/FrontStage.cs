using System.Windows;
using System.Windows.Interop;
using Island.Core;
using Island.Sources.Front;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 section 2, the one stage that reads what is in front: a small borderless window of the self-test's own, handed to the
/// reading together with its own rectangle as "its screen", is a fullscreen program; handed the real screen's rectangle it is not.
/// Nothing covers the real screen. Windows' own answer at that moment goes into selftest.json as a kind (a number) and is never
/// asserted. Then the island's decision, with readings handed in: over a game in exclusive fullscreen it never appears; over a
/// fullscreen program or a presentation it appears when asked and not by itself; over nothing it appears.
/// </summary>
internal sealed class FrontStage(SelfTestReport report, TimeSpan hangLimit)
{
    public async Task RunAsync()
    {
        var window = new Window
        {
            Title = "Island self-test window",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Width = 300,
            Height = 200,
            Left = 120,
            Top = 160,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Native.GetWindowRect(handle, out var r);
            var own = new FrontRect(r.Left, r.Top, r.Right, r.Bottom);
            var asItsScreen = FrontReader.ClassifyWindow(handle, own);
            var onRealScreen = FrontReader.ClassifyWindow(handle, FrontReader.ScreenOf(handle));
            report.Check("a borderless test window handed its own rectangle as its screen is a fullscreen program",
                asItsScreen == FrontState.FullscreenProgram, asItsScreen.ToString());
            report.Check("the same window on the real screen is not", onRealScreen == FrontState.Clear, onRealScreen.ToString());
            report.Info["notificationState"] = FrontReader.ReadNotificationState()?.ToString() ?? "unreadable";
        }
        finally
        {
            window.Close();
        }

        await GateAsync();
    }

    private async Task GateAsync()
    {
        using var rt = new IslandRuntime(30);
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;

        async Task<bool> AppearsAfter(FrontState state, Action ask)
        {
            rt.Gate.Reading = () => new FrontReading(state, null);
            ask();
            await Task.Delay(120);
            var appeared = m.Phase != IslandPhase.Hidden;
            if (appeared)
            {
                c.MainKey(); // dismiss
                await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden again", hangLimit, report);
            }

            return appeared;
        }

        report.Check("over a game in exclusive fullscreen the island never appears, asked or not",
            !await AppearsAfter(FrontState.ExclusiveFullscreen, c.MainKey) && !await AppearsAfter(FrontState.ExclusiveFullscreen, c.ShowHideByItself)
            && !await AppearsAfter(FrontState.ExclusiveFullscreen, () => c.PageKey(PageIds.Apps)), "key, start-up and page key all refused");
        var fullscreenByItself = await AppearsAfter(FrontState.FullscreenProgram, c.ShowHideByItself);
        var fullscreenAsked = await AppearsAfter(FrontState.FullscreenProgram, c.MainKey);
        report.Check("over a fullscreen program it appears when asked by a key and not by itself", !fullscreenByItself && fullscreenAsked, $"by itself {fullscreenByItself}, asked {fullscreenAsked}");
        var presentationByItself = await AppearsAfter(FrontState.Presentation, c.ShowHideByItself);
        var presentationAsked = await AppearsAfter(FrontState.Presentation, c.MainKey);
        report.Check("over a presentation it appears when asked and not by itself", !presentationByItself && presentationAsked, $"by itself {presentationByItself}, asked {presentationAsked}");
        var clearByItself = await AppearsAfter(FrontState.Clear, c.ShowHideByItself);
        report.Check("over nothing it appears, also by itself", clearByItself, $"by itself {clearByItself}");
    }
}
