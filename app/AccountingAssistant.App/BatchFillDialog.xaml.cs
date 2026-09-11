using AccountingAssistant.App.Models;
using System.Windows;

namespace AccountingAssistant.App;

public partial class BatchFillDialog : Window
{
    private readonly ProjectProfile _profile;
    private readonly string _repoRoot;

    public BatchFillDialog(ProjectProfile profile, string repoRoot)
    {
        InitializeComponent();
        _profile = profile;
        _repoRoot = repoRoot;
        RefreshSuggestionSources();
    }

    public event Action? SuggestionsUpdated;

    public bool ApplyToAllReviewable => AllReviewableRadioButton.IsChecked == true;

    public bool OverwriteExistingValues => OverwriteCheckBox.IsChecked == true;

    public IReadOnlyDictionary<string, string> FieldValues => new Dictionary<string, string>
    {
        ["document_type"] = DocumentTypeComboBox.Text.Trim(),
        ["counterparty_name"] = CounterpartyComboBox.Text.Trim(),
        ["expense_category"] = ExpenseCategoryComboBox.Text.Trim(),
        ["project_name"] = ProjectNameComboBox.Text.Trim(),
        ["department"] = DepartmentComboBox.Text.Trim(),
        ["handler"] = HandlerComboBox.Text.Trim(),
        ["notes"] = NotesTextBox.Text.Trim(),
    };

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void ManageSuggestionsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SuggestionManagerDialog(_profile)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var category in dialog.Categories)
        {
            var target = _profile.GetMutableSuggestions(category.FieldName);
            target.Clear();
            target.AddRange(category.Values);
        }

        _profile.Save(_repoRoot);
        _profile.ReplaceWith(ProjectProfile.Load(_repoRoot));
        RefreshSuggestionSources();
        SuggestionsUpdated?.Invoke();
    }

    private void RefreshSuggestionSources()
    {
        DocumentTypeComboBox.ItemsSource = null;
        CounterpartyComboBox.ItemsSource = null;
        ExpenseCategoryComboBox.ItemsSource = null;
        ProjectNameComboBox.ItemsSource = null;
        DepartmentComboBox.ItemsSource = null;
        HandlerComboBox.ItemsSource = null;

        DocumentTypeComboBox.ItemsSource = _profile.DocumentTypes;
        CounterpartyComboBox.ItemsSource = _profile.Counterparties;
        ExpenseCategoryComboBox.ItemsSource = _profile.ExpenseCategories;
        ProjectNameComboBox.ItemsSource = _profile.ProjectNames;
        DepartmentComboBox.ItemsSource = _profile.Departments;
        HandlerComboBox.ItemsSource = _profile.Handlers;
    }
}
