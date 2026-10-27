using System.Runtime.InteropServices;

namespace DuoDesk;

internal static class IdleMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    internal static uint GetIdleMilliseconds()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return 0;
        return unchecked((uint)Environment.TickCount - input.Time);
    }

    internal static bool ShouldLock(bool hasPin, bool locked, int minutes, uint idleMilliseconds)
        => hasPin && !locked && minutes > 0 && (ulong)idleMilliseconds >= (ulong)minutes * 60_000UL;
}
