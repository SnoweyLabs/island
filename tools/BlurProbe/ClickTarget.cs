using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlurProbe;

/// <summary>
/// Glass mode, second process: a plain grey window of ANOTHER program, under the glass layer, so the click check can ask
/// whether the mouse would reach a different program through the glass. It never asks for the foreground and is shown without
/// activation. Protocol on stdin/stdout (one line each): it prints "hwnd N pid P" once; "at X Y" answers "at ROOT PID" with
/// what WindowFromPoint finds from this process; "quit" (or end of input) closes it. It closes itself after two minutes anyway.
/// </summary>
internal static unsafe partial class Win
{
    [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint thread, uint msg, nint w, nint l);

    static volatile bool targetQuit;

    /// <summary>The top-level window WindowFromPoint finds at a screen point, asked from this process, and its process id.</summary>
    public static (nint Root, int Pid) RootAt(int x, int y)
    {
        var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);
        GetWindowThreadProcessId(root, out var pid);
        return (root, (int)pid);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint TargetProc(nint h, uint m, nint w, nint l)
    {
        switch (m)
        {
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
            case WM_ERASEBKGND: return 1;
            case WM_PAINT:
                var dc = BeginPaint(h, out var ps);
                GetClientRect(h, out var c);
                FillRect(dc, ref c, GetStockObject(2));   // GRAY_BRUSH
                EndPaint(h, ref ps);
                return 0;
        }
        return DefWindowProcW(h, m, w, l);
    }

    public static int RunClickTarget(string[] args)
    {
        int i = Array.IndexOf(args, "--click-target");
        if (i < 0 || i + 4 >= args.Length) return 3;
        int x = int.Parse(args[i + 1]), y = int.Parse(args[i + 2]), w = int.Parse(args[i + 3]), h = int.Parse(args[i + 4]);
        var cls = Register("BlurProbeClickTarget", &TargetProc);
        var hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls, 0, WS_POPUP, x, y, w, h, 0, 0, GetModuleHandleW(0), 0);
        if (hwnd == 0) return 4;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        UpdateWindow(hwnd);
        Console.Out.WriteLine($"hwnd {(long)hwnd} pid {Environment.ProcessId}");
        Console.Out.Flush();

        uint mainThread = GetCurrentThreadId();
        var reader = new Thread(() =>
        {
            string? line;
            while ((line = Console.In.ReadLine()) != null && line != "quit")
            {
                var parts = line.Split(' ');
                if (parts.Length != 3 || parts[0] != "at") continue;
                var root = GetAncestor(WindowFromPoint(new POINT { X = int.Parse(parts[1]), Y = int.Parse(parts[2]) }), GA_ROOT);
                GetWindowThreadProcessId(root, out var pid);
                Console.Out.WriteLine($"at {(long)root} {pid}");
                Console.Out.Flush();
            }
            targetQuit = true;
            PostThreadMessageW(mainThread, 0, 0, 0);   // wake the pump
        }) { IsBackground = true };
        reader.Start();

        var end = Environment.TickCount64 + 120_000;
        while (!targetQuit && Environment.TickCount64 < end) Pump(20);
        DestroyWindow(hwnd);
        return 0;
    }
}
