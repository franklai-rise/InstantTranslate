using System.Runtime.InteropServices;
using InstantTranslate.Models;

namespace InstantTranslate.Interop;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WmQuit = 0x0012;
    internal const int WmMouseActivate = 0x0021;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmLButtonUp = 0x0202;
    internal const int MaNoActivate = 3;
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint LlMhfInjected = 0x00000001;
    internal const int SmCxDrag = 68;
    internal const int SmCyDrag = 69;
    internal static readonly IntPtr HwndTopmost = new(-1);

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;

        internal readonly ScreenPoint ToScreenPoint() => new(X, Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        internal NativePoint Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal IntPtr Hwnd;
        internal uint Value;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }


    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Message message, IntPtr windowHandle, uint minFilter, uint maxFilter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
