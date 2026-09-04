using System.Collections.Concurrent;
using System.Diagnostics;
using InstantTranslate.Interop;
using InstantTranslate.Models;
using InstantTranslate.Services;

namespace InstantTranslate.Selection;

/// <summary>
/// Edge keeps its full renderer accessibility tree disabled until assistive
/// technology is detected. A short, reversible screen-reader notification
/// activates the tree for the current Edge process without changing browser
/// shortcuts, injecting input, or touching the clipboard.
/// </summary>
internal static class EdgeAccessibilityActivator
{
    private const int ActivationPulseMilliseconds = 100;
    private static readonly ConcurrentDictionary<uint, byte> ActivatedProcesses = new();
    private static readonly object ActivationSyncRoot = new();

    internal static void ActivateIfNeeded(ScreenPoint point)
    {
        var processId = WindowProcessResolver.TryGetExternalProcessIdAt(point);
        if (processId is not { } targetProcessId
            || ActivatedProcesses.ContainsKey(targetProcessId)
            || !ShouldActivateProcessName(TryGetProcessName(targetProcessId)))
        {
            return;
        }

        lock (ActivationSyncRoot)
        {
            if (ActivatedProcesses.ContainsKey(targetProcessId))
            {
                return;
            }

            var originalScreenReaderState = 0;
            if (!NativeMethods.SystemParametersInfoGet(
                    NativeMethods.SpiGetScreenReader,
                    0,
                    ref originalScreenReaderState,
                    0))
            {
                return;
            }

            if (originalScreenReaderState != 0)
            {
                ActivatedProcesses.TryAdd(targetProcessId, 0);
                return;
            }

            var activated = NativeMethods.SystemParametersInfoSet(
                NativeMethods.SpiSetScreenReader,
                1,
                IntPtr.Zero,
                NativeMethods.SpifSendChange);
            if (!activated)
            {
                return;
            }

            try
            {
                Thread.Sleep(ActivationPulseMilliseconds);
                ActivatedProcesses.TryAdd(targetProcessId, 0);
            }
            finally
            {
                // Restore the exact state observed before our short activation.
                // Edge keeps accessibility enabled for its process once it has
                // responded to the notification.
                _ = NativeMethods.SystemParametersInfoSet(
                    NativeMethods.SpiSetScreenReader,
                    0,
                    IntPtr.Zero,
                    NativeMethods.SpifSendChange);
            }
        }
    }

    internal static bool ShouldActivateProcessName(string? processName)
    {
        return string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
