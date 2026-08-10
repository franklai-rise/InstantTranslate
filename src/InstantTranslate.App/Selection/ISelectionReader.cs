using InstantTranslate.Models;

namespace InstantTranslate.Selection;

internal interface ISelectionReader
{
    Task<string?> TryReadSelectedTextAsync(ScreenPoint point, CancellationToken cancellationToken);
}
