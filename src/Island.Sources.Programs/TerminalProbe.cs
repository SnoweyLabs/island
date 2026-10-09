using System.Runtime.InteropServices;
using Island.Core;
using Island.Core.Terminals;

namespace Island.Sources.Programs;

/// <summary>
/// The real <see cref="ITerminalProbe"/> (WORK-ORDER-11 section 2): the top-level windows of class PseudoConsoleWindow (hidden ones included) and
/// ConsoleWindowClass with their owning process and owner window, and the process list (id, parent id, file name). Read only: it attaches to no
/// console, opens no process, reads no command line, environment or memory. Nothing it reads is written anywhere. Fails soft.
/// </summary>
public sealed class TerminalProbe : ITerminalProbe
{
    private const string PseudoConsoleClass = "PseudoConsoleWindow";
    private const string ClassicConsoleClass = "ConsoleWindowClass";

    public TerminalProbeFacts Read()
    {
        try
        {
            return new TerminalProbeFacts(ConsoleWindows(), ProcessList.Read());
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return TerminalProbeFacts.Empty;
        }
    }

    /// <summary>The console windows alone (the probe command of the smoke program uses this).</summary>
    public static IReadOnlyList<ConsoleWindowFact> ConsoleWindows()
    {
        var found = new List<ConsoleWindowFact>();
        Native.EnumWindows((hwnd, _) =>
        {
            var className = Native.ClassNameOf(hwnd);
            if (className is PseudoConsoleClass or ClassicConsoleClass)
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                found.Add(new ConsoleWindowFact(hwnd, className, (int)pid, Native.GetWindow(hwnd, Native.GwOwner)));
            }

            return true;
        }, 0);
        return found;
    }
}

/// <summary>The process list: a snapshot of all processes (CreateToolhelp32Snapshot, Process32FirstW, Process32NextW; kernel32, tlhelp32.h). Id, parent id and file name only.</summary>
internal static class ProcessList
{
    private const uint SnapProcess = 0x00000002; // TH32CS_SNAPPROCESS
    private static readonly nint InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    public static IReadOnlyList<ProcessFact> Read()
    {
        var list = new List<ProcessFact>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == 0) return list;
        try
        {
            var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>(), szExeFile = string.Empty };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                list.Add(new ProcessFact((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return list;
    }
}
