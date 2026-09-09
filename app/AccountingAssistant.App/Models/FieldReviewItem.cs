namespace AccountingAssistant.App.Models;

public sealed class FieldReviewItem
{
    public required string FieldName { get; init; }

    public required string Label { get; init; }

    public string Value { get; set; } = string.Empty;

    public decimal Confidence { get; init; }

    public string Source { get; init; } = "manual";

    public string? Warning { get; init; }

    public string? BaselineValue { get; init; }

    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    public string ConfidenceText => Source switch
    {
        "batch" => "批量",
        "batch_conflict" => "冲突",
        "review_conflict" => "冲突",
        "semantic_conflict" => "冲突",
        "review" => "已审",
        _ => Confidence > 0 ? $"{Confidence:P0}" : string.Empty
    };

    public IReadOnlyList<int> OcrRefs { get; init; } = [];

    public IReadOnlyList<string> Suggestions { get; init; } = [];

    public string SourceText => OcrRefs.Count > 0 ? string.Join(", ", OcrRefs) : string.Empty;

    public string ReviewNote
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Warning))
            {
                parts.Add(Warning);
            }

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
