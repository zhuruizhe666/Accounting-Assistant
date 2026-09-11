using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountingAssistant.App.Models;

public sealed class ProjectProfile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [JsonPropertyName("document_types")]
    public List<string> DocumentTypes { get; set; } = [];

    [JsonPropertyName("counterparties")]
    public List<string> Counterparties { get; set; } = [];

    [JsonPropertyName("expense_categories")]
    public List<string> ExpenseCategories { get; set; } = [];

    [JsonPropertyName("project_names")]
    public List<string> ProjectNames { get; set; } = [];

    [JsonPropertyName("departments")]
    public List<string> Departments { get; set; } = [];

    [JsonPropertyName("handlers")]
    public List<string> Handlers { get; set; } = [];

    public static ProjectProfile Empty => new();

    public static ProjectProfile Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "data", "project_profile.json");
        if (!File.Exists(path))
        {
            return Empty;
        }

        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<ProjectProfile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? Empty;
        profile.Normalize();
        return profile;
    }

    public void Save(string repoRoot)
    {
        Normalize();
        var dataDirectory = Path.Combine(repoRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "project_profile.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void ReplaceWith(ProjectProfile profile)
    {
        DocumentTypes = [.. profile.DocumentTypes];
        Counterparties = [.. profile.Counterparties];
        ExpenseCategories = [.. profile.ExpenseCategories];
        ProjectNames = [.. profile.ProjectNames];
        Departments = [.. profile.Departments];
        Handlers = [.. profile.Handlers];
        Normalize();
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

    public List<string> GetMutableSuggestions(string fieldName)
    {
        return fieldName switch
        {
            "counterparty_name" => Counterparties,
            "expense_category" => ExpenseCategories,
            "project_name" => ProjectNames,
            "department" => Departments,
            "handler" => Handlers,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "Unsupported suggestion field.")
        };
    }

    private void Normalize()
    {
        NormalizeList(DocumentTypes);
        NormalizeList(Counterparties);
        NormalizeList(ExpenseCategories);
        NormalizeList(ProjectNames);
        NormalizeList(Departments);
        NormalizeList(Handlers);
    }

    private static void NormalizeList(List<string> values)
    {
        var normalized = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        values.Clear();
        values.AddRange(normalized);
    }
}
