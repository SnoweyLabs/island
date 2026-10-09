using System.Windows.Controls;
using System.Windows.Threading;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-12 section 3: the first summon after a silent start (Windows' start-up list, or the setup's end) used to find none of its code compiled and none of its text and
/// templates loaded, so the first Ctrl+Q waited 100 to 165 ms for the first frame. This does once, while the island is hidden and the computer idle, what that first summon does: an
/// island of invented pages is built over canvases that are in no window, summoned, moved to other pages, searched, and drawn once into a bitmap, then thrown away. It shows nothing, reads
/// nothing of the person's, writes nothing, starts no program and never touches the keyboard; it runs at the idle priority of the drawing thread, in small steps, so a key that comes in
/// the meantime waits for one step at most.
/// </summary>
internal static class IslandWarmUp
{
    /// <summary>The steps the warm-up goes through, each its own piece of work on the drawing thread (Claude).</summary>
    private const int Steps = 6;

    /// <summary>Starts the warm-up. Returns a task that ends when it is over (the self-test waits for it; the app does not).</summary>
    /// <param name="warmSettings">Builds the settings screen's view once, laid out and drawn into a bitmap and never shown (the first opening of Settings used to find its code cold too); null leaves it out.</param>
    public static Task RunAsync(Dispatcher ui, Func<System.Windows.FrameworkElement>? warmSettings = null) => RunCoreAsync(ui, warmSettings);

    private static async Task RunCoreAsync(Dispatcher ui, Func<System.Windows.FrameworkElement>? warmSettings)
    {
        IslandController? controller = null;
        try
        {
            var width = WindowMetrics.Width;
            var height = WindowMetrics.Height;
            var back = new Canvas { Width = width, Height = height };
            var front = new Canvas { Width = width, Height = height };
            var view = new IslandView(back, front, width, height);
            var machine = new IslandMachine();
            controller = new IslandController(view, machine, width);
            // Search with three invented entries, so that the first typed letter does not find the search code cold either.
            SearchEntry[] entries = [.. new[] { "Alpha", "Alpine", "Beta" }.Select(name => new SearchEntry(name, "program:" + name.ToLowerInvariant(), true, false, new Item(name, "invented", name[..2].ToUpperInvariant(), 200, "program:" + name.ToLowerInvariant())))];
            controller.Search = new SearchSession(controller, () => entries, _ => { }, (_, _) => { });
            for (var step = 0; step < Steps; step++)
            {
                await ui.InvokeAsync(() => Step(controller, view, front, step), DispatcherPriority.ApplicationIdle);
                await Task.Delay(250); // the island needs time between the steps to reach the page the next one asks for
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A warm-up that fails changes nothing: the first summon is then as slow as it was.
        }
        finally
        {
            if (controller is not null) await ui.InvokeAsync(controller.Dispose, DispatcherPriority.ApplicationIdle);
        }

        if (warmSettings is null) return;
        try
        {
            await ui.InvokeAsync(() =>
            {
                var view = warmSettings();
                view.Width = 1920;
                view.Height = 1080;
                view.Measure(new System.Windows.Size(1920, 1080));
                view.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                view.UpdateLayout();
                _ = Snapshot.Of(view, 1, 1, 96);
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // as above: nothing changes if it fails
        }
    }

    private static void Step(IslandController controller, IslandView view, Canvas front, int step)
    {
        switch (step)
        {
            case 0:
                controller.MainKey(); // the fly-in and its first frames
                break;
            case 1:
                controller.PageKey(Pages.BuiltIn[1].Id);
                break;
            case 2:
                controller.PageKey(Pages.BuiltIn[2].Id);
                break;
            case 3:
                controller.HandleText("a"); // search, with the invented pages' own tiles
                break;
            case 4:
                controller.HandleKey(0x1B);
                controller.HandleKey(0x1B);
                break;
            default:
                // The elements are laid out and drawn once, into a bitmap: the text, the templates and the effects are loaded as the first real frame needs them.
                front.Measure(new System.Windows.Size(front.Width, front.Height));
                front.Arrange(new System.Windows.Rect(0, 0, front.Width, front.Height));
                front.UpdateLayout();
                _ = Snapshot.Of(front, 1, 1, 96);
                break;
        }
    }
}
