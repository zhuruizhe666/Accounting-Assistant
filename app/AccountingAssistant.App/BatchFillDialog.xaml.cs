using AccountingAssistant.App.Models;
using System.Windows;

namespace AccountingAssistant.App;

public partial class BatchFillDialog : Window
{
    public BatchFillDialog(ProjectProfile profile)
    {
        InitializeComponent();
        DocumentTypeComboBox.ItemsSource = profile.DocumentTypes;
        CounterpartyComboBox.ItemsSource = profile.Counterparties;
        ExpenseCategoryComboBox.ItemsSource = profile.ExpenseCategories;
        ProjectNameComboBox.ItemsSource = profile.ProjectNames;
        DepartmentComboBox.ItemsSource = profile.Departments;
        HandlerComboBox.ItemsSource = profile.Handlers;
    }

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
}
