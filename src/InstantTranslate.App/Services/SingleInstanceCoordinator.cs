using System.Diagnostics;
using System.Security.Principal;

namespace InstantTranslate.Services;

/// <summary>
/// Keeps one application instance alive per Windows user session and lets later
/// instances ask the primary instance to activate itself.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string KernelObjectNamespace = @"Local\";
    private readonly object _syncRoot = new();
    private readonly Mutex _lifetimeMutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.ManualReset);
    private Thread? _listenerThread;
    private Action? _activationCallback;
    private bool _disposed;

    private SingleInstanceCoordinator(
        Mutex lifetimeMutex,
        EventWaitHandle activationEvent,
        bool isPrimary)
    {
        _lifetimeMutex = lifetimeMutex;
        _activationEvent = activationEvent;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    /// <summary>
    /// Acquires the lifetime slot for <paramref name="instanceName"/>. A later
    /// instance signals the primary instance before this method returns.
    /// </summary>
    public static SingleInstanceCoordinator Acquire(string? instanceName = null)
    {
        var normalizedName = NormalizeInstanceName(instanceName ?? CreateDefaultInstanceName());
        var activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{KernelObjectNamespace}{normalizedName}.Activate");

        Mutex? lifetimeMutex = null;
        try
        {
            // The mutex handle itself is the lifetime claim. Avoiding mutex ownership
            // makes disposal safe even when application shutdown changes threads.
            lifetimeMutex = new Mutex(
                false,
                $"{KernelObjectNamespace}{normalizedName}.Lifetime",
                out var createdNew);

            var coordinator = new SingleInstanceCoordinator(
                lifetimeMutex,
                activationEvent,
                createdNew);

            if (!createdNew)
            {
                activationEvent.Set();
            }

            return coordinator;
        }
        catch
        {
            lifetimeMutex?.Dispose();
            activationEvent.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the primary instance's background activation listener.
    /// The callback is dispatched on a ThreadPool thread and should marshal to
    /// the UI dispatcher when it needs to touch WPF state.
    /// </summary>
    public void StartListening(Action activationCallback)
    {
        ArgumentNullException.ThrowIfNull(activationCallback);

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!IsPrimary)
            {
                throw new InvalidOperationException(
                    "Only the primary application instance can listen for activation requests.");
            }

            if (_listenerThread is not null)
            {
                throw new InvalidOperationException("The activation listener has already started.");
            }

            _activationCallback = activationCallback;
            _listenerThread = new Thread(ListenForActivation)
            {
                IsBackground = true,
                Name = "InstantTranslate.SingleInstanceListener"
            };
            _listenerThread.Start();
        }
    }

    public void Dispose()
    {
        Thread? listenerThread;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activationCallback = null;
            listenerThread = _listenerThread;
            _stopEvent.Set();
        }

        if (listenerThread is not null && listenerThread != Thread.CurrentThread)
        {
            listenerThread.Join();
        }

        _stopEvent.Dispose();
        _activationEvent.Dispose();
        _lifetimeMutex.Dispose();
    }

    internal static string CreateDefaultInstanceName()
    {
        var userScope = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(userScope))
        {
            userScope = $"{Environment.UserDomainName}.{Environment.UserName}";
        }

        return $"InstantTranslate.{userScope}.Session{Process.GetCurrentProcess().SessionId}";
    }

    private void ListenForActivation()
    {
        var waitHandles = new WaitHandle[] { _stopEvent, _activationEvent };

        while (WaitHandle.WaitAny(waitHandles) == 1)
        {
            Action? callback;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                callback = _activationCallback;
            }

            if (callback is not null)
            {
                ThreadPool.QueueUserWorkItem(
                    static state =>
                    {
                        try
                        {
                            ((Action)state!).Invoke();
                        }
                        catch
                        {
                            // A UI activation failure must not terminate the listener.
                        }
                    },
                    callback,
                    preferLocal: false);
            }
        }
    }

    private static string NormalizeInstanceName(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            throw new ArgumentException("An instance name is required.", nameof(instanceName));
        }

        var normalizedName = instanceName.Trim();
        if (normalizedName.Contains('\\'))
        {
            throw new ArgumentException(
                "The instance name cannot contain a namespace separator.",
                nameof(instanceName));
        }

        return normalizedName;
    }
}
