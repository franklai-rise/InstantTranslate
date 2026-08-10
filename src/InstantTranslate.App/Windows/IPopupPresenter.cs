using InstantTranslate.Models;

namespace InstantTranslate.Windows;

internal sealed record PopupRetranslateRequest(
    long RequestId,
    string SourceText,
    string TargetLanguage,
    ScreenPoint AnchorPoint);

internal interface IPopupPresenter
{
    event Action<PopupRetranslateRequest>? RetranslateRequested;

    event Action<long>? PopupClosed;

    void ShowLoading(long requestId, ScreenPoint anchorPoint);

    void ShowTranslation(
        long requestId,
        string sourceText,
        string translatedText,
        string targetLanguage,
        ScreenPoint anchorPoint);

    void FailRequest(long requestId);

    void HideTransientPopup();
}
