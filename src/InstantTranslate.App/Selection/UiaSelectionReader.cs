using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using InstantTranslate.Models;

namespace InstantTranslate.Selection;

internal sealed class UiaSelectionReader : IContextualSelectionReader
{
    private const int MaximumTextLength = 20_000;
    private const int MaximumContextLength = 3000;
    private const int MaximumAncestorDepth = 10;

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
        return Task.Run(() => ReadSelection(point, includeContext, cancellationToken), cancellationToken);
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

        if (TryIsPassword(hitElement))
        {
            yield break;
        }

        var hitProcessId = TryGetProcessId(hitElement);
        if (hitProcessId is > 0)
        {
            try
            {
                var candidate = AutomationElement.FocusedElement;
                if (ShouldIncludeFocusedElement(hitProcessId, TryGetProcessId(candidate)))
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
            var current = root;
            for (var depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
            {
                var identity = TryGetIdentity(current);
                if (identity is null || seenRuntimeIds.Add(identity))
                {
                    yield return current;
                }

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
                    break;
                }
            }
        }
    }

    internal static bool ShouldIncludeFocusedElement(int? hitProcessId, int? focusedProcessId)
    {
        return hitProcessId is > 0 && hitProcessId == focusedProcessId;
    }

    private static bool TryIsPassword(AutomationElement? element)
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
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
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
