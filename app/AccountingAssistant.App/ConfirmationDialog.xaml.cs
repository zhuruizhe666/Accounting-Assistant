using System.Windows;
using System.Windows.Input;

namespace AccountingAssistant.App;

public partial class ConfirmationDialog : Window
{
    private ConfirmationDialog(
        string title,
        string message,
        string primaryAction,
        string? secondaryAction,
        bool isDestructive,
        bool allowCopy)
    {
        InitializeComponent();

        Title = title;
        PromptTitleTextBlock.Text = title;
        PromptMessageTextBox.Text = message;
        PromptMessageTextBox.IsTabStop = allowCopy;
        PromptMessageTextBox.Focusable = allowCopy;
        CopyActionButton.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
        PrimaryActionButton.Content = primaryAction;
        PrimaryActionButton.Style = (Style)FindResource(
            isDestructive ? "DangerButtonStyle" : "PrimaryButtonStyle");

        if (string.IsNullOrWhiteSpace(secondaryAction))
        {
            SecondaryActionButton.Visibility = Visibility.Collapsed;
            PrimaryActionButton.Margin = new Thickness(0);
        }
        else
        {
            SecondaryActionButton.Content = secondaryAction;
        }
    }

    public static bool ShowConfirmation(
        Window owner,
        string title,
        string message,
        string primaryAction,
        string secondaryAction,
        bool isDestructive = false)
    {
        var dialog = new ConfirmationDialog(
            title,
            message,
            primaryAction,
            secondaryAction,
            isDestructive,
            allowCopy: false)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    public static void ShowNotice(
        Window owner,
        string title,
        string message,
        string primaryAction = "Got it")
    {
        var dialog = new ConfirmationDialog(
            title,
            message,
            primaryAction,
            secondaryAction: null,
            isDestructive: false,
            allowCopy: true)
        {
            Owner = owner
        };

        dialog.ShowDialog();
    }

    private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CopyActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(PromptMessageTextBox.Text);
            CopyActionButton.Content = "Copied";
        }
        catch (Exception)
        {
            CopyActionButton.Content = "Select text and press Ctrl+C";
            CopyActionButton.Width = 196;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        e.Handled = true;
    }
}
