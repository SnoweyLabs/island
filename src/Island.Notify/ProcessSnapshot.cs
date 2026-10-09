using System.Runtime.InteropServices;

namespace Island.Notify;

/// <summary>
/// Reads the process list, read only, to learn who started whom: a snapshot of all processes
/// (CreateToolhelp32Snapshot with TH32CS_SNAPPROCESS, then Process32FirstW and Process32NextW, both in kernel32;
/// the calls and the structure are as on Microsoft Learn, tlhelp32.h). It starts nothing, opens no process, and
/// fails quietly: a failed call gives an empty map.
/// </summary>
internal static class ProcessSnapshot
{
    private const uint SnapProcess = 0x00000002; // TH32CS_SNAPPROCESS
    private static readonly IntPtr InvalidHandle = new(-1); // INVALID_HANDLE_VALUE

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Process id to parent process id, for every process in the snapshot.</summary>
    public static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero) return map;

        var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>(), szExeFile = "" };
        for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;

        CloseHandle(snapshot);
        return map;
    }
}
