using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CinePresence.App.Services;

internal static class WindowTitleReader
{
    public static IReadOnlyList<string> ReadCaptions(string sourceId)
    {
        var processName = WindowTitlePolicy.ProcessName(sourceId);
        if (processName is null) return [];
        var pids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process) pids.Add((uint)process.Id);
        }
        var captions = new List<string>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (!pids.Contains(pid) || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
            var text = new StringBuilder(1024);
            if (GetWindowText(window, text, text.Capacity) > 0) captions.Add(text.ToString());
            return true;
        }, IntPtr.Zero);
        return captions;
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
