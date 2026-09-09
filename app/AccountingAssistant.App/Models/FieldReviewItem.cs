namespace AccountingAssistant.App.Models;

public sealed class FieldReviewItem
{
    public required string FieldName { get; init; }

    public required string Label { get; init; }

    public string Value { get; set; } = string.Empty;

    public decimal Confidence { get; init; }

    public string ConfidenceText => Confidence > 0 ? $"{Confidence:P0}" : string.Empty;

    public IReadOnlyList<int> OcrRefs { get; init; } = [];

    public IReadOnlyList<string> Suggestions { get; init; } = [];

    public string SourceText => OcrRefs.Count > 0 ? string.Join(", ", OcrRefs) : string.Empty;

    public string ReviewNote
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ConfidenceText))
            {
                parts.Add(ConfidenceText);
            }

            if (!string.IsNullOrWhiteSpace(SourceText))
            {
                parts.Add($"OCR {SourceText}");
            }

            return string.Join(" / ", parts);
        }
    }
}
