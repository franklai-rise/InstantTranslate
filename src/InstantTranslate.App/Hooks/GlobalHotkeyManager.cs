using System.ComponentModel;
using System.Runtime.InteropServices;
using InstantTranslate.Interop;

namespace InstantTranslate.Hooks;

/// <summary>
/// Provides a keyboard-only, non-injecting fallback for applications whose
/// custom text renderer cannot expose a selection to accessibility APIs.
/// </summary>
internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int TranslateClipboardHotkeyId = 0x4954;
    private const uint VirtualKeyT = 0x54;
    private readonly ManualResetEventSlim _started = new(false);
    private Thread? _messageThread;
    private uint _messageThreadId;
    private Exception? _startupException;
    private bool _disposed;

    public event Action? TranslateClipboardRequested;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_messageThread is not null)
        {
            return;
        }

        _messageThread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "InstantTranslate.GlobalHotkey",
        };
        _messageThread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new TimeoutException("全局快捷键启动超时。");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "无法注册 Ctrl+Shift+T，可能已被其他程序占用。",
                _startupException);
        }
    }

    private void MessageThreadMain()
    {
        var isRegistered = false;
        try
        {
            _messageThreadId = NativeMethods.GetCurrentThreadId();
            isRegistered = NativeMethods.RegisterHotKey(
                IntPtr.Zero,
                TranslateClipboardHotkeyId,
                NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
                VirtualKeyT);
            if (!isRegistered)
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
        try
        {
            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Value == NativeMethods.WmHotKey
                    && unchecked((int)message.WParam.ToUInt64()) == TranslateClipboardHotkeyId)
                {
                    try
                    {
                        TranslateClipboardRequested?.Invoke();
                    }
                    catch (Exception)
                    {
                        // A subscriber failure must not terminate the native loop.
                    }
                }
            }
        }
        finally
        {
            if (isRegistered)
            {
                NativeMethods.UnregisterHotKey(IntPtr.Zero, TranslateClipboardHotkeyId);
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
        if (_messageThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_messageThreadId, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        _messageThread?.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }
}
