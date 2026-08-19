using System.Runtime.InteropServices;
using System.Text;
using InstantTranslate.Interop;
using InstantTranslate.Models;

namespace InstantTranslate.Selection;

/// <summary>
/// Reads selections directly from classic Win32/RichEdit and Scintilla
/// controls. These controls expose their selected range through messages, so
/// this reader never sends WM_COPY and never changes the system clipboard.
/// </summary>
internal sealed class NativeSelectionReader : ISelectionReader
{
    private const int MaximumTextLength = 20000;
    private const uint MessageTimeoutMilliseconds = 60;
    private const int MaximumAncestorDepth = 8;

    public Task<string?> TryReadSelectedTextAsync(
        ScreenPoint point,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ReadSelection(point, cancellationToken), cancellationToken);
    }

    private static string? ReadSelection(ScreenPoint point, CancellationToken cancellationToken)
    {
        var hitWindow = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y,
        });
        if (!IsExternalWindow(hitWindow))
        {
            return null;
        }

        var current = hitWindow;
        for (var depth = 0; current != IntPtr.Zero && depth < MaximumAncestorDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var className = GetClassName(current);
            if (IsEditControl(className))
            {
                if (IsPasswordStyle(NativeMethods.GetWindowLongPtr(current, NativeMethods.GwlStyle).ToInt64()))
                {
                    return null;
                }

                var selectedText = TryReadEditSelection(current);
                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    return selectedText;
                }
            }
            else if (IsScintillaControl(className))
            {
                var selectedText = TryReadScintillaSelection(current);
                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    return selectedText;
                }
            }

            current = NativeMethods.GetParent(current);
        }

        return null;
    }

    internal static bool IsEditControl(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return false;
        }

        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
            || className.Equals("RichEdit20A", StringComparison.OrdinalIgnoreCase)
            || className.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase)
            || className.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase)
            || className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsScintillaControl(string? className)
    {
        return className?.Equals("Scintilla", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static bool IsPasswordStyle(long windowStyle)
    {
        return (windowStyle & NativeMethods.EsPassword) != 0;
    }

    private static string? TryReadEditSelection(IntPtr windowHandle)
    {
        var buffer = Marshal.AllocHGlobal((MaximumTextLength + 1) * sizeof(char));
        try
        {
            var bufferSize = (MaximumTextLength + 1) * sizeof(char);
            Marshal.Copy(new byte[bufferSize], 0, buffer, bufferSize);
            var sent = NativeMethods.SendMessageTimeout(
                windowHandle,
                NativeMethods.EmGetSelText,
                UIntPtr.Zero,
                buffer,
                NativeMethods.SmtoAbortIfHung,
                MessageTimeoutMilliseconds,
                out _);
            if (sent == IntPtr.Zero)
            {
                return TryReadEditRangeSelection(windowHandle);
            }

            return NormalizeBuffer(buffer, MaximumTextLength)
                ?? TryReadEditRangeSelection(windowHandle);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? TryReadEditRangeSelection(IntPtr windowHandle)
    {
        var startPointer = Marshal.AllocHGlobal(sizeof(int));
        var endPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            var sent = NativeMethods.SendMessageTimeout(
                windowHandle,
                NativeMethods.EmGetSel,
                ToUIntPtr(startPointer),
                endPointer,
                NativeMethods.SmtoAbortIfHung,
                MessageTimeoutMilliseconds,
                out _);
            if (sent == IntPtr.Zero)
            {
                return null;
            }

            var start = Marshal.ReadInt32(startPointer);
            var end = Marshal.ReadInt32(endPointer);
            if (end <= start)
            {
                return null;
            }

            var textLength = TryGetWindowTextLength(windowHandle);
            if (textLength <= start)
            {
                return null;
            }

            var bufferLength = Math.Min(textLength, MaximumTextLength * 4) + 1;
            var textBuffer = Marshal.AllocHGlobal(bufferLength * sizeof(char));
            try
            {
                var textBufferSize = bufferLength * sizeof(char);
                Marshal.Copy(new byte[textBufferSize], 0, textBuffer, textBufferSize);
                var textSent = NativeMethods.SendMessageTimeout(
                    windowHandle,
                    NativeMethods.WmGetText,
                    new UIntPtr((uint)bufferLength),
                    textBuffer,
                    NativeMethods.SmtoAbortIfHung,
                    MessageTimeoutMilliseconds,
                    out _);
                if (textSent == IntPtr.Zero)
                {
                    return null;
                }

                var wholeText = Marshal.PtrToStringUni(textBuffer, bufferLength);
                if (string.IsNullOrEmpty(wholeText))
                {
                    return null;
                }

                var safeStart = Math.Clamp(start, 0, wholeText.Length);
                var safeEnd = Math.Clamp(end, safeStart, wholeText.Length);
                return TextNormalizer.Normalize(wholeText[safeStart..safeEnd]);
            }
            finally
            {
                Marshal.FreeHGlobal(textBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(startPointer);
            Marshal.FreeHGlobal(endPointer);
        }
    }

    private static int TryGetWindowTextLength(IntPtr windowHandle)
    {
        var sent = NativeMethods.SendMessageTimeout(
            windowHandle,
            NativeMethods.WmGetTextLength,
            UIntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SmtoAbortIfHung,
            MessageTimeoutMilliseconds,
            out var result);
        if (sent == IntPtr.Zero)
        {
            return 0;
        }

        return (int)Math.Min(result.ToUInt64(), MaximumTextLength * 4);
    }

    private static UIntPtr ToUIntPtr(IntPtr pointer)
    {
        return new UIntPtr(unchecked((ulong)pointer.ToInt64()));
    }

    private static string? TryReadScintillaSelection(IntPtr windowHandle)
    {
        // Scintilla stores document text as UTF-8, even in a Unicode process.
        var buffer = Marshal.AllocHGlobal(MaximumTextLength + 1);
        try
        {
            var bufferSize = MaximumTextLength + 1;
            Marshal.Copy(new byte[bufferSize], 0, buffer, bufferSize);
            var sent = NativeMethods.SendMessageTimeout(
                windowHandle,
                NativeMethods.SciGetSelText,
                UIntPtr.Zero,
                buffer,
                NativeMethods.SmtoAbortIfHung,
                MessageTimeoutMilliseconds,
                out _);
            if (sent == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[MaximumTextLength + 1];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            var length = Array.IndexOf(bytes, (byte)0);
            if (length <= 0)
            {
                return null;
            }

            return TextNormalizer.Normalize(Encoding.UTF8.GetString(bytes, 0, length));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? NormalizeBuffer(IntPtr buffer, int maximumLength)
    {
        var text = Marshal.PtrToStringUni(buffer, maximumLength);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : TextNormalizer.Normalize(text);
    }

    private static string GetClassName(IntPtr windowHandle)
    {
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassName(windowHandle, buffer, buffer.Length);
        return length <= 0 ? string.Empty : buffer.ToString();
    }

    private static bool IsExternalWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }
}
