using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Island.Redteam.Ease.Tests.Harness;

/// <summary>
/// Runs work on ONE single-threaded-apartment thread that lives as long as the test run. WPF elements belong to the thread that made them, and the settings view keeps cached templates in
/// static fields (as it does in the app, which has one interface thread): a thread for each test would hand one test another's templates. Nothing is ever shown: elements are only measured
/// and arranged.
/// </summary>
internal static class Sta
{
    private static readonly BlockingCollection<Action> Queue = [];

    private static readonly Thread Worker = Start();

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            foreach (var job in Queue.GetConsumingEnumerable()) job();
        })
        {
            IsBackground = true,
            Name = "ease-sta",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    public static T Run<T>(Func<T> work)
    {
        _ = Worker;
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim();
        Queue.Add(() =>
        {
            try { result = work(); }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
            finally { done.Set(); }
        });
        done.Wait();
        failure?.Throw();
        return result!;
    }

    public static void Run(Action work) => Run<object?>(() =>
    {
        work();
        return null;
    });
}
