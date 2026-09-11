namespace AccountingAssistant.App.Models;

public sealed record SuggestionCategory(
    string FieldName,
    string Label,
    List<string> Values);
