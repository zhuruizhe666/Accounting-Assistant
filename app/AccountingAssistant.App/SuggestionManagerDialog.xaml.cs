using AccountingAssistant.App.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AccountingAssistant.App;

public partial class SuggestionManagerDialog : Window
{
    private readonly ObservableCollection<SuggestionCategory> _categories;

    public SuggestionManagerDialog(ProjectProfile profile)
    {
        InitializeComponent();
        _categories =
        [
            new("counterparty_name", "交易方", profile.Counterparties.ToList()),
            new("expense_category", "费用类别", profile.ExpenseCategories.ToList()),
            new("project_name", "项目名", profile.ProjectNames.ToList()),
            new("department", "部门", profile.Departments.ToList()),
            new("handler", "经办人", profile.Handlers.ToList())
        ];
        SuggestionTabControl.ItemsSource = _categories;
    }

    public IReadOnlyList<SuggestionCategory> Categories => _categories;

    private void AddSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentSuggestion();
    }

    private void NewSuggestionTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        AddCurrentSuggestion();
        e.Handled = true;
    }

    private void AddCurrentSuggestion()
    {
        if (SuggestionTabControl.SelectedItem is not SuggestionCategory category)
        {
            return;
        }

        var textBox = FindVisualChild<System.Windows.Controls.TextBox>(SuggestionTabControl);
        var value = textBox?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!category.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            category.Values.Add(value);
            category.Values.Sort(StringComparer.OrdinalIgnoreCase);
        }

        if (textBox is not null)
        {
            textBox.Clear();
            textBox.Focus();
        }

        SuggestionTabControl.Items.Refresh();
    }

    private void DeleteSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (SuggestionTabControl.SelectedItem is not SuggestionCategory category)
        {
            return;
        }

        var listBox = FindVisualChild<System.Windows.Controls.ListBox>(SuggestionTabControl);
        if (listBox is null)
        {
            return;
        }

        var selectedValues = listBox.SelectedItems
            .OfType<string>()
            .ToList();

        foreach (var value in selectedValues)
        {
            category.Values.Remove(value);
        }

        SuggestionTabControl.Items.Refresh();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private static T? FindVisualChild<T>(DependencyObject? current)
        where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
