using System.Runtime.InteropServices;
using System.Windows.Automation;
using InstantTranslate.Models;

namespace InstantTranslate.Selection;

internal sealed class UiaSelectionReader : ISelectionReader
{
    private const int MaximumTextLength = 2000;
    private const int MaximumAncestorDepth = 10;

    public Task<string?> TryReadSelectedTextAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        return Task.Run(() => ReadSelection(point, cancellationToken), cancellationToken);
    }

    private static string? ReadSelection(ScreenPoint point, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetCandidateElements(point))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedText = TryReadFromElement(candidate);
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText;
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

        try
        {
            focusedElement = AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
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

    private static string? TryReadFromElement(AutomationElement element)
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
            }

            return selectedParts.Count == 0
                ? null
                : string.Join(Environment.NewLine, selectedParts);
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
