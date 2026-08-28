using InstantTranslate.Models;
using InstantTranslate.Translation;

namespace InstantTranslate.Windows;

internal sealed record PopupRetranslateRequest(
    long RequestId,
    string SourceText,
    string SourceLanguage,
    string TargetLanguage,
    ScreenPoint AnchorPoint);

internal sealed record PopupTranslationCorrection(
    long RequestId,
    string SourceText,
    string CorrectedTranslation,
    string SourceLanguage,
    string TargetLanguage);

internal sealed record PopupExplanationRequest(
    long RequestId,
    string SubjectText,
    string SourceText,
    string TranslationText,
    string SourceLanguage,
    string TargetLanguage,
    ExplanationScope Scope);

internal interface IPopupPresenter
{
    event Action<PopupRetranslateRequest>? RetranslateRequested;

    event Action<long>? PopupClosed;

    event Action<long, bool>? PinStateChanged;

    event Func<PopupTranslationCorrection, bool>? TranslationCorrectionRequested;

    event Action<PopupExplanationRequest>? ExplanationRequested;

    event Action<long>? ExplanationDismissed;

    void ShowLoading(long requestId, ScreenPoint anchorPoint);

    void ShowTranslation(
        long requestId,
        string sourceText,
        string translatedText,
        string targetLanguage,
        ScreenPoint anchorPoint,
        string sourceLanguage = "自动检测");

    void FailRequest(long requestId, string? message = null);

    void CompleteRequest(long requestId);

    void ShowExplanationLoading(long requestId);

    void ShowExplanation(long requestId, string explanation);

    void FailExplanation(long requestId, string message);

    void HideExplanation(long requestId);

    bool IsPointOverPopup(ScreenPoint point);

    void HideTransientPopup();

    void HideTransientPopupIfOutside(ScreenPoint point);

}
