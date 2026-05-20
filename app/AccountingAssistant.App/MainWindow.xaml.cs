using AccountingAssistant.App.Models;
using AccountingAssistant.App.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AccountingAssistant.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ObservableCollection<ReceiptImageItem> _images = [];
    private readonly PythonWorkerClient _workerClient = new();

    public MainWindow()
    {
        InitializeComponent();
        ImageListBox.ItemsSource = _images;
        StatusTextBlock.Text = "Ready. Select receipt images to start.";
    }

    private void SelectImagesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select receipt images",
            Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _images.Clear();
        foreach (var fileName in dialog.FileNames)
        {
            _images.Add(new ReceiptImageItem(fileName));
        }

        ImageListBox.SelectedIndex = _images.Count > 0 ? 0 : -1;
        StatusTextBlock.Text = $"Loaded {_images.Count} image(s).";
    }

    private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            ReceiptImage.Source = null;
            return;
        }

        ReceiptImage.Source = LoadBitmap(item.FullPath);
        StatusTextBlock.Text = $"Selected {item.FileName}.";
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            StatusTextBlock.Text = "Select an image first.";
            return;
        }

        AnalyzeButton.IsEnabled = false;
        StatusTextBlock.Text = "Calling Python worker mock...";

        try
        {
            var result = await _workerClient.AnalyzeMockAsync(item.FullPath);
            ResultTextBox.Text = JsonSerializer.Serialize(result, JsonOptions);
            StatusTextBlock.Text = "Worker mock returned successfully.";
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.ToString();
            StatusTextBlock.Text = "Worker call failed. Check Python installation and worker path.";
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
