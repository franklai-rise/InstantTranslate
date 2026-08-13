using System.ComponentModel;
using System.Runtime.InteropServices;
using InstantTranslate.Interop;
using InstantTranslate.Models;

namespace InstantTranslate.Hooks;

internal sealed class GlobalMouseHook : IDisposable
{
    private readonly ManualResetEventSlim _started = new(false);
    private readonly NativeMethods.LowLevelMouseProc _hookCallback;
    private readonly SelectionGestureDetector _gestureDetector;
    private readonly NativeWindowDragTracker _windowDragTracker = new();
    private Thread? _hookThread;
    private IntPtr _hookHandle;
    private uint _hookThreadId;
    private Exception? _startupException;
    private bool _disposed;

    public GlobalMouseHook()
    {
        _hookCallback = HookCallback;
        _gestureDetector = new SelectionGestureDetector(
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxDrag),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCyDrag));
    }

    public event Action<ScreenPoint>? MousePressed;

    public event Action<SelectionGesture>? SelectionGestureCompleted;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookThread is not null)
        {
            return;
        }

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "InstantTranslate.MouseHook",
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new TimeoutException("全局鼠标钩子启动超时。");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException("无法安装全局鼠标钩子。", _startupException);
        }
    }

    private void HookThreadMain()
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhMouseLl,
                _hookCallback,
                NativeMethods.GetModuleHandle(null),
                0);
            if (_hookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _started.Set();
            return;
        }

        _started.Set();
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)(long)wParam);
            if (message is NativeMethods.WmLButtonDown or NativeMethods.WmLButtonUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
                if ((data.Flags & NativeMethods.LlMhfInjected) == 0)
                {
                    var point = data.Point.ToScreenPoint();
                    if (message == NativeMethods.WmLButtonDown)
                    {
                        _windowDragTracker.Press(point);
                        _gestureDetector.Press(point);
                        // Keep mouse-down and mouse-up notifications ordered. Both
                        // subscribers only enqueue work onto the WPF dispatcher;
                        // dispatching them through separate ThreadPool work items
                        // could let a stale mouse-down cancel the new translation
                        // popup after the mouse-up had already started it.
                        InvokeSafely(MousePressed, point);
                    }
                    else
                    {
                        var suppressSelection = _windowDragTracker.ReleaseShouldSuppressSelection();
                        var gesture = _gestureDetector.Release(point, DateTimeOffset.UtcNow);
                        if (!suppressSelection && gesture is not null)
                        {
                            InvokeSafely(SelectionGestureCompleted, gesture.Value);
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static void InvokeSafely<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"InstantTranslate mouse hook subscriber failed: {exception}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowDragTracker.Cancel();
        _gestureDetector.Cancel();
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }
}
