using System.Diagnostics;

namespace Island.Core.Speed;

/// <summary>
/// WORK-ORDER-12 section 1: how long a thread did not answer. A thread of its own posts a small piece of work to the watched thread (the drawing thread) and times
/// how long it takes to be run; the longest wait is the longest the watched thread was busy with something else. There was nothing of the kind before. It only
/// measures: the posted piece of work does nothing.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    /// <summary>How soon after a reply the next piece of work is posted (Claude): a thread that is silent for a whole block is seen within about this much of its start.</summary>
    public const int PostEveryMs = 1;

    private readonly Action<Action> _post;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _replied = new(false);
    private volatile bool _stop;
    private long _sentAt;
    private long _longestTicks;
    private long _replies;

    /// <param name="post">Runs the given action on the watched thread, as soon as that thread is free (for WPF: <c>Dispatcher.BeginInvoke(DispatcherPriority.Send, action)</c>).</param>
    public UiWatchdog(Action<Action> post)
    {
        _post = post;
        _thread = new Thread(Loop) { IsBackground = true, Name = "island-speed-watchdog" };
    }

    /// <summary>Starts watching. The longest silence is counted from now.</summary>
    public void Start()
    {
        Reset();
        _thread.Start();
    }

    /// <summary>Forgets what was seen: the longest silence is counted again from now.</summary>
    public void Reset() => Interlocked.Exchange(ref _longestTicks, 0);

    /// <summary>The longest time, since the start or the last reset, that a posted piece of work waited to be run, in milliseconds.</summary>
    public double LongestSilenceMs
    {
        get
        {
            // A piece of work that is still waiting counts for how long it has waited so far: a block that is still going on is seen.
            var sent = Interlocked.Read(ref _sentAt);
            var pending = _replied.IsSet || sent == 0 ? 0 : Stopwatch.GetTimestamp() - sent; // sent == 0: nothing was posted yet
            return Math.Max(Interlocked.Read(ref _longestTicks), pending) * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>How many pieces of work were run (a check that the watched thread was served at all).</summary>
    public long Replies => Interlocked.Read(ref _replies);

    private void Loop()
    {
        while (!_stop)
        {
            _replied.Reset();
            var sent = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _sentAt, sent);
            try
            {
                _post(() =>
                {
                    var waited = Stopwatch.GetTimestamp() - Interlocked.Read(ref _sentAt);
                    long seen;
                    while (waited > (seen = Interlocked.Read(ref _longestTicks)) && Interlocked.CompareExchange(ref _longestTicks, waited, seen) != seen)
                    {
                    }

                    Interlocked.Increment(ref _replies);
                    _replied.Set();
                });
            }
            catch (Exception e) when (e is InvalidOperationException or OperationCanceledException)
            {
                return; // the watched thread's queue is gone
            }

            while (!_stop && !_replied.Wait(20))
            {
            }

            if (!_stop) Thread.Sleep(PostEveryMs);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _replied.Set();
        if (_thread.IsAlive) _thread.Join(500);
        _replied.Dispose();
    }
}
