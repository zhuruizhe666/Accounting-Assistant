using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountingAssistant.App.Models;

public sealed record ProjectProfile(
    [property: JsonPropertyName("document_types")] IReadOnlyList<string> DocumentTypes,
    [property: JsonPropertyName("counterparties")] IReadOnlyList<string> Counterparties,
    [property: JsonPropertyName("expense_categories")] IReadOnlyList<string> ExpenseCategories,
    [property: JsonPropertyName("project_names")] IReadOnlyList<string> ProjectNames,
    [property: JsonPropertyName("departments")] IReadOnlyList<string> Departments,
    [property: JsonPropertyName("handlers")] IReadOnlyList<string> Handlers)
{
    public static ProjectProfile Empty { get; } = new([], [], [], [], [], []);

    public static ProjectProfile Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "data", "project_profile.json");
        if (!File.Exists(path))
        {
            return Empty;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProjectProfile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? Empty;
    }

    public IReadOnlyList<string> GetSuggestions(string fieldName)
    {
        return fieldName switch
        {
            "document_type" => DocumentTypes,
            "counterparty_name" => Counterparties,
            "expense_category" => ExpenseCategories,
            "project_name" => ProjectNames,
            "department" => Departments,
            "handler" => Handlers,
            _ => []
        };
    }
}
