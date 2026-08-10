using InstantTranslate.Interop;
using InstantTranslate.Models;

namespace InstantTranslate.Services;

internal static class WindowProcessResolver
{
    public static bool IsCurrentProcessAt(ScreenPoint point)
    {
        var windowHandle = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y,
        });
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId == (uint)Environment.ProcessId;
    }
}
