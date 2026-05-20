using System.Text.Json.Serialization;

namespace AccountingAssistant.App.Models;

public sealed record ReceiptAnalysisResult(
    [property: JsonPropertyName("image_path")] string ImagePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ocr_items")] IReadOnlyList<OcrItem> OcrItems,
    [property: JsonPropertyName("candidates")] Dictionary<string, IReadOnlyList<FieldCandidate>> Candidates);

public sealed record OcrItem(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("bbox")] IReadOnlyList<IReadOnlyList<int>> BBox);

public sealed record FieldCandidate(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("source_text")] string SourceText,
    [property: JsonPropertyName("bbox_refs")] IReadOnlyList<int> BBoxRefs);
