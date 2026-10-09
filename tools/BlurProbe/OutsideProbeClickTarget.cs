using System.Diagnostics;
using Island.Core;

namespace BlurProbe;

/// <summary>
/// Starts a second copy of this probe as the click target of glass mode (<see cref="Win.RunClickTarget"/>) and talks to it.
/// Lives in a file whose name begins "Outside" and asks <see cref="OutsideGate"/> first: it starts a process, even if only our own.
/// </summary>
internal sealed class ClickTargetProcess : IDisposable
{
    readonly Process process;

    public nint Hwnd { get; }

    public int Pid { get; }

    ClickTargetProcess(Process p, nint hwnd, int pid)
    {
        process = p;
        Hwnd = hwnd;
        Pid = pid;
    }

    /// <summary>The target's window covers the given screen rectangle. Null when the gate refused or the copy did not answer.</summary>
    public static ClickTargetProcess? Start(int x, int y, int w, int h)
    {
        var exe = Environment.ProcessPath;
        if (exe == null || !OutsideGate.Current.Allow(OutsideKind.StartOwnCopy)) return null;
        var psi = new ProcessStartInfo(exe, $"--click-target {x} {y} {w} {h}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        var p = Process.Start(psi);
        if (p == null) return null;
        var first = ReadLine(p, 10_000)?.Split(' ');
        if (first is not { Length: 4 } || first[0] != "hwnd")
        {
            Stop(p);
            return null;
        }
        return new ClickTargetProcess(p, (nint)long.Parse(first[1]), int.Parse(first[3]));
    }

    /// <summary>What WindowFromPoint finds at a screen point when asked from the target's own process: (root window, its process id).</summary>
    public (nint Root, int Pid)? At(int x, int y)
    {
        process.StandardInput.WriteLine($"at {x} {y}");
        process.StandardInput.Flush();
        var a = ReadLine(process, 5_000)?.Split(' ');
        if (a is not { Length: 3 } || a[0] != "at") return null;
        return ((nint)long.Parse(a[1]), int.Parse(a[2]));
    }

    public void Dispose() => Stop(process);

    static string? ReadLine(Process p, int timeoutMs)
    {
        // Keep pumping while waiting: the target's WindowFromPoint sends hit-test messages to our own windows, which only a
        // pumping thread answers.
        var t = p.StandardOutput.ReadLineAsync();
        var end = Environment.TickCount64 + timeoutMs;
        while (!t.IsCompleted && Environment.TickCount64 < end) Win.Pump(10);
        return t.IsCompleted ? t.Result : null;
    }

    static void Stop(Process p)
    {
        try
        {
            p.StandardInput.WriteLine("quit");
            p.StandardInput.Flush();
            if (!p.WaitForExit(3_000)) p.Kill();
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        catch (IOException)
        {
            if (!p.HasExited) p.Kill();
        }
        p.Dispose();
    }
}
