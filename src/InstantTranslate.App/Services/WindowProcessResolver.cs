using System.Diagnostics;
using InstantTranslate.Interop;
using InstantTranslate.Models;

namespace InstantTranslate.Services;

internal static class WindowProcessResolver
{
    private static readonly HashSet<string> ClipboardFallbackBlockedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "conhost",
        "powershell",
        "pwsh",
        "WindowsTerminal",
        "OpenConsole",
        "mintty",
        "alacritty",
        "wezterm",
    };

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

    public static bool ArePointsInSameExternalProcess(ScreenPoint first, ScreenPoint second)
    {
        return TryGetExternalProcessIdAt(first, out var firstProcessId)
            && TryGetExternalProcessIdAt(second, out var secondProcessId)
            && firstProcessId == secondProcessId;
    }

    public static bool IsClipboardFallbackAllowedAt(ScreenPoint point)
    {
        var windowHandle = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y,
        });

        if (!IsExternalClipboardTarget(windowHandle))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return IsClipboardFallbackProcessAllowed(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static bool IsClipboardFallbackProcessAllowed(string? processName)
    {
        return !string.IsNullOrWhiteSpace(processName)
            && !ClipboardFallbackBlockedProcesses.Contains(processName);
    }

    private static bool TryGetExternalProcessIdAt(ScreenPoint point, out uint processId)
    {
        var windowHandle = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y,
        });
        processId = 0;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out processId);
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }

    private static bool IsExternalClipboardTarget(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }
}
