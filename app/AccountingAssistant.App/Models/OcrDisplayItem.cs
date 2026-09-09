namespace AccountingAssistant.App.Models;

public sealed record OcrDisplayItem(
    int Index,
    string Text,
    decimal Confidence,
    string ConfidenceText,
    bool IsCorrected = false)
{
    public string CorrectionStatus => IsCorrected ? "Edited" : string.Empty;
}
