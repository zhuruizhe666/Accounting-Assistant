using System.Text.Json.Serialization;

namespace AccountingAssistant.App.Models;

public sealed record ReceiptAnalysisResult(
    [property: JsonPropertyName("image_path")] string ImagePath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ocr_items")] IReadOnlyList<OcrItem> OcrItems,
    [property: JsonPropertyName("candidates")] Dictionary<string, IReadOnlyList<FieldCandidate>> Candidates,
    [property: JsonPropertyName("semantic_fields")] Dictionary<string, IReadOnlyList<SemanticField>>? SemanticFields = null,
    [property: JsonPropertyName("semantic_status")] SemanticStatus? SemanticStatus = null,
    [property: JsonPropertyName("error")] string? Error = null);

public sealed record OcrItem(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("bbox")] IReadOnlyList<IReadOnlyList<int>> BBox);

public sealed record FieldCandidate(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("source_text")] string SourceText,
    [property: JsonPropertyName("bbox_refs")] IReadOnlyList<int> BBoxRefs);

public sealed record SemanticField(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("ocr_refs")] IReadOnlyList<int> OcrRefs,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record SemanticStatus(
    [property: JsonPropertyName("engine")] string Engine,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("reason")] string? Reason = null);
