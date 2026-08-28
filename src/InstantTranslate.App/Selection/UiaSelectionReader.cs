using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using InstantTranslate.Models;
using InstantTranslate.Interop;
using InstantTranslate.Services;

namespace InstantTranslate.Selection;

internal sealed class UiaSelectionReader : IContextualSelectionReader
{
    private const int MaximumTextLength = 20_000;
    private const int MaximumContextLength = 3000;
    private const int MaximumAncestorDepth = 10;
    private const int MaximumConcurrentReads = 2;
    private readonly SemaphoreSlim _readSlots = new(MaximumConcurrentReads, MaximumConcurrentReads);
    private readonly ConcurrentDictionary<uint, byte> _activeTargetProcesses = new();
    private readonly Func<ScreenPoint, bool, CancellationToken, SelectionCapture?> _readSelection;
    private readonly Func<ScreenPoint, uint?> _getTargetProcessId;
    private int _activeReadCount;
    private long _rejectedReadCount;

    public UiaSelectionReader()
        : this(ReadSelection, WindowProcessResolver.TryGetExternalProcessIdAt)
    {
    }

    internal UiaSelectionReader(
        Func<ScreenPoint, bool, CancellationToken, SelectionCapture?> readSelection,
        Func<ScreenPoint, uint?>? getTargetProcessId = null)
    {
        _readSelection = readSelection ?? throw new ArgumentNullException(nameof(readSelection));
        _getTargetProcessId = getTargetProcessId ?? (_ => null);
    }

    internal int ActiveReadCount => Volatile.Read(ref _activeReadCount);

    internal long RejectedReadCount => Interlocked.Read(ref _rejectedReadCount);

    public string CreateStatusReport(bool useChinese)
    {
        return useChinese
            ? string.Join(
                Environment.NewLine,
                "UI Automation 取词状态",
                $"正在读取：{ActiveReadCount}/{MaximumConcurrentReads}",
                $"为避免卡死已跳过：{RejectedReadCount}")
            : string.Join(
                Environment.NewLine,
                "UI Automation selection status",
                $"Active reads: {ActiveReadCount}/{MaximumConcurrentReads}",
                $"Skipped to avoid blocking: {RejectedReadCount}");
    }

    public async Task<string?> TryReadSelectedTextAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        var capture = await TryReadSelectionAsync(point, includeContext: false, cancellationToken)
            .ConfigureAwait(false);
        return capture?.Text;
    }

    public Task<SelectionCapture?> TryReadSelectionAsync(
        ScreenPoint point,
        bool includeContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetProcessId = _getTargetProcessId(point);
        if (targetProcessId is { } processId
            && !_activeTargetProcesses.TryAdd(processId, 0))
        {
            Interlocked.Increment(ref _rejectedReadCount);
            return Task.FromResult<SelectionCapture?>(null);
        }

        // UI Automation providers run in other processes. A broken provider can
        // remain blocked even after the caller's timeout expires, because COM
        // calls cannot be cancelled safely from managed code. Bound the number
        // of physical reads so repeated selections in such an application never
        // consume an unbounded number of ThreadPool threads. One remaining slot
        // still allows another healthy application to recover automatically.
        if (!_readSlots.Wait(0))
        {
            if (targetProcessId is { } rejectedProcessId)
            {
                _activeTargetProcesses.TryRemove(rejectedProcessId, out _);
            }

            Interlocked.Increment(ref _rejectedReadCount);
            return Task.FromResult<SelectionCapture?>(null);
        }

        Interlocked.Increment(ref _activeReadCount);
        var completion = new TaskCompletionSource<SelectionCapture?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var readThread = new Thread(
                () => ExecutePhysicalRead(
                    point,
                    includeContext,
                    targetProcessId,
                    cancellationToken,
                    completion))
            {
                IsBackground = true,
                Name = "InstantTranslate.UIAutomationRead",
            };
            readThread.Start();
        }
        catch
        {
            if (targetProcessId is { } failedProcessId)
            {
                _activeTargetProcesses.TryRemove(failedProcessId, out _);
            }

            Interlocked.Decrement(ref _activeReadCount);
            _readSlots.Release();
            throw;
        }

        var readTask = completion.Task;

        // WaitAsync cancellation deliberately does not release the physical-read
        // slot. The slot belongs to the underlying COM operation and is released
        // only when that operation actually ends. The worker also requests COM
        // call cancellation as a best effort so a responsive provider can unwind.
        ObserveFault(readTask);
        return readTask.WaitAsync(cancellationToken);
    }

    private void ExecutePhysicalRead(
        ScreenPoint point,
        bool includeContext,
        uint? targetProcessId,
        CancellationToken cancellationToken,
        TaskCompletionSource<SelectionCapture?> completion)
    {
        var coInitializeResult = NativeMethods.CoInitializeEx(
            IntPtr.Zero,
            NativeMethods.CoInitMultithreaded);
        var shouldUninitializeCom = coInitializeResult >= 0;
        var cancellationEnabled = coInitializeResult >= 0
                                  && NativeMethods.CoEnableCallCancellation(IntPtr.Zero) >= 0;
        CancellationTokenRegistration cancellationRegistration = default;
        SelectionCapture? result = null;
        Exception? failure = null;
        try
        {
            if (cancellationEnabled)
            {
                var threadId = NativeMethods.GetCurrentThreadId();
                cancellationRegistration = cancellationToken.UnsafeRegister(
                    static state =>
                    {
                        var targetThreadId = (uint)state!;
                        _ = NativeMethods.CoCancelCall(targetThreadId, 0);
                    },
                    threadId);
            }

            result = _readSelection(point, includeContext, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            failure = exception;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            cancellationRegistration.Dispose();
            if (cancellationEnabled)
            {
                _ = NativeMethods.CoDisableCallCancellation(IntPtr.Zero);
            }

            if (shouldUninitializeCom)
            {
                NativeMethods.CoUninitialize();
            }

            if (targetProcessId is { } processId)
            {
                _activeTargetProcesses.TryRemove(processId, out _);
            }

            Interlocked.Decrement(ref _activeReadCount);
            _readSlots.Release();
        }

        if (failure is OperationCanceledException canceled)
        {
            completion.TrySetCanceled(canceled.CancellationToken);
        }
        else if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else
        {
            completion.TrySetResult(result);
        }
    }

    private static void ObserveFault(Task<SelectionCapture?> task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static SelectionCapture? ReadSelection(
        ScreenPoint point,
        bool includeContext,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in GetCandidateElements(point))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = TryReadFromElement(candidate, includeContext);
            if (!string.IsNullOrWhiteSpace(capture?.Text))
            {
                return capture;
            }
        }

        return null;
    }

    private static IEnumerable<AutomationElement> GetCandidateElements(ScreenPoint point)
    {
        var seenRuntimeIds = new HashSet<string>(StringComparer.Ordinal);
        var expectedRootOwner = GetRootOwnerAt(point);
        AutomationElement? hitElement = null;
        AutomationElement? focusedElement = null;

        try
        {
            hitElement = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }

        if (IsPasswordOrUnknown(hitElement))
        {
            yield break;
        }

        var hitProcessId = TryGetProcessId(hitElement);
        if (hitProcessId is > 0)
        {
            try
            {
                var candidate = AutomationElement.FocusedElement;
                if (ShouldIncludeFocusedElementInTargetWindow(
                        hitProcessId,
                        TryGetProcessId(candidate),
                        expectedRootOwner,
                        TryGetRootOwnerHandle(candidate)))
                {
                    focusedElement = candidate;
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (COMException)
            {
            }
        }

        foreach (var root in new[] { hitElement, focusedElement })
        {
            foreach (var current in GetSafeCandidatePath(root))
            {
                var identity = TryGetIdentity(current);
                if (identity is null || seenRuntimeIds.Add(identity))
                {
                    yield return current;
                }
            }
        }
    }

    internal static bool ShouldIncludeFocusedElement(int? hitProcessId, int? focusedProcessId)
    {
        return hitProcessId is > 0 && hitProcessId == focusedProcessId;
    }

    internal static bool ShouldIncludeFocusedElementInTargetWindow(
        int? hitProcessId,
        int? focusedProcessId,
        IntPtr expectedRootOwner,
        IntPtr focusedRootOwner)
    {
        if (!ShouldIncludeFocusedElement(hitProcessId, focusedProcessId))
        {
            return false;
        }

        // Chromium/Electron document hosts commonly omit NativeWindowHandle
        // on the focused accessibility node. A same-process focus is still
        // the active document the user just selected in; reject only when a
        // concrete root owner proves it belongs to another top-level window.
        return expectedRootOwner == IntPtr.Zero
               || focusedRootOwner == IntPtr.Zero
               || focusedRootOwner == expectedRootOwner;
    }

    private static IReadOnlyList<AutomationElement> GetSafeCandidatePath(AutomationElement? root)
    {
        var path = new List<AutomationElement>(MaximumAncestorDepth);
        var current = root;
        for (var depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            // Check the complete path before yielding any node. This prevents a
            // child of a password control from leaking its selection before the
            // protected ancestor is discovered.
            if (IsPasswordOrUnknown(current))
            {
                return Array.Empty<AutomationElement>();
            }

            path.Add(current);
            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
            catch (COMException)
            {
                return Array.Empty<AutomationElement>();
            }
        }

        return path;
    }

    private static bool IsPasswordOrUnknown(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            return element.Current.IsPassword;
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (COMException)
        {
            return true;
        }
    }

    private static IntPtr GetRootOwnerAt(ScreenPoint point)
    {
        var windowHandle = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y,
        });
        return windowHandle == IntPtr.Zero
            ? IntPtr.Zero
            : GetRootOwner(windowHandle);
    }

    private static IntPtr TryGetRootOwnerHandle(AutomationElement? element)
    {
        var current = element;
        for (var depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            try
            {
                var windowHandle = new IntPtr(current.Current.NativeWindowHandle);
                if (windowHandle != IntPtr.Zero)
                {
                    return GetRootOwner(windowHandle);
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return IntPtr.Zero;
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }
            catch (COMException)
            {
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr GetRootOwner(IntPtr windowHandle)
    {
        var rootOwner = NativeMethods.GetAncestor(windowHandle, NativeMethods.GaRootOwner);
        return rootOwner != IntPtr.Zero
            ? rootOwner
            : NativeMethods.GetAncestor(windowHandle, NativeMethods.GaRoot);
    }

    private static int? TryGetProcessId(AutomationElement? element)
    {
        if (element is null)
        {
            return null;
        }

        try
        {
            return element.Current.ProcessId;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static SelectionCapture? TryReadFromElement(AutomationElement element, bool includeContext)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject)
                || patternObject is not TextPattern textPattern
                || textPattern.SupportedTextSelection == SupportedTextSelection.None)
            {
                return null;
            }

            var ranges = textPattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
            {
                return null;
            }

            var remaining = MaximumTextLength;
            var selectedParts = new List<string>(ranges.Length);
            var contexts = includeContext
                ? new BoundedTextAccumulator(MaximumContextLength)
                : null;
            foreach (var range in ranges)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var value = TextNormalizer.Normalize(range.GetText(remaining));
                if (value.Length == 0)
                {
                    continue;
                }

                selectedParts.Add(value);
                remaining -= value.Length;

                if (contexts is not null
                    && contexts.MaximumNextPartLength > 0
                    && TryReadParagraphContext(range, contexts.MaximumNextPartLength) is { Length: > 0 } context
                    && !string.Equals(context, value, StringComparison.Ordinal))
                {
                    contexts.TryAdd(context);
                }
            }

            return selectedParts.Count == 0
                ? null
                : new SelectionCapture(
                    string.Join(Environment.NewLine, selectedParts),
                    contexts?.Build());
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? TryReadParagraphContext(
        TextPatternRange selectionRange,
        int maximumLength)
    {
        try
        {
            var paragraphRange = selectionRange.Clone();
            paragraphRange.ExpandToEnclosingUnit(TextUnit.Paragraph);
            var context = TextNormalizer.Normalize(paragraphRange.GetText(maximumLength));
            return context.Length == 0 ? null : context;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? TryGetIdentity(AutomationElement element)
    {
        try
        {
            return string.Join('.', element.GetRuntimeId());
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
