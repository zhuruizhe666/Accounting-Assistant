using System.Windows;

namespace AccountingAssistant.App;

public partial class EditOcrTextDialog : Window
{
    public EditOcrTextDialog(int index, string currentText)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Edit recognized text at OCR index {index}.";
        OcrTextBox.Text = currentText;
        OcrTextBox.SelectAll();
        OcrTextBox.Focus();
    }

    public string EditedText => OcrTextBox.Text;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
