using AccountingAssistant.App.Commands;
using AccountingAssistant.App.Models;
using AccountingAssistant.App.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private bool _isMarkCensoredCoolingDown;

    public ICommand MarkCensoredCommand { get; }

    public ICommand NextReceiptCommand { get; }

    public MainWindow()
    {
        MarkCensoredCommand = new RelayCommand(_ => MarkSelectedCensored());
        NextReceiptCommand = new RelayCommand(_ => MoveToNextReceipt());

        InitializeComponent();
        DataContext = this;
        ImageListBox.ItemsSource = _images;
        StatusTextBlock.Text = "Ready. Select receipt images to start.";
    }

    private void SelectImagesButton_Click(object sender, RoutedEventArgs e)
    {
        LoadImages(SelectImageFiles(), "No images selected.");
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        LoadImages(SelectImagesFromFolder(), "No supported images found in folder.");
    }

    private void LoadImages(IReadOnlyList<string> selectedFiles, string emptyMessage)
    {
        if (selectedFiles.Count == 0)
        {
            StatusTextBlock.Text = emptyMessage;
            return;
        }

        var existingPaths = _images
            .Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var skippedCount = 0;

        foreach (var fileName in selectedFiles)
        {
            if (!existingPaths.Add(fileName))
            {
                skippedCount++;
                continue;
            }

            _images.Add(new ReceiptImageItem(fileName));
            addedCount++;
        }

        if (ImageListBox.SelectedIndex < 0 && _images.Count > 0)
        {
            ImageListBox.SelectedIndex = 0;
        }

        StatusTextBlock.Text = $"Added {addedCount} image(s). Skipped {skippedCount} duplicate(s). Queue total: {_images.Count}.";
    }

    private IReadOnlyList<string> SelectImageFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select receipt images",
            Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return [];
        }

        return dialog.FileNames;
    }

    private IReadOnlyList<string> SelectImagesFromFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing receipt images",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return [];
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png"
        };

        return Directory
            .EnumerateFiles(dialog.SelectedPath)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        await AnalyzeItemAsync(item);
    }

    private async void AnalyzeAllPendingButton_Click(object sender, RoutedEventArgs e)
    {
        var pendingItems = _images
            .Where(item => item.Status == ReceiptQueueStatus.Pending)
            .ToList();

        if (pendingItems.Count == 0)
        {
            StatusTextBlock.Text = "No pending images to analyze.";
            return;
        }

        SetAnalysisButtonsEnabled(false);
        try
        {
            foreach (var item in pendingItems)
            {
                ImageListBox.SelectedItem = item;
                await AnalyzeItemAsync(item, manageButtons: false);
            }

            StatusTextBlock.Text = $"Analyzed {pendingItems.Count} pending image(s).";
        }
        finally
        {
            SetAnalysisButtonsEnabled(true);
        }
    }

    private void MarkCensoredButton_Click(object sender, RoutedEventArgs e)
    {
        MarkSelectedCensored();
    }

    private void NextReceiptButton_Click(object sender, RoutedEventArgs e)
    {
        MoveToNextReceipt();
    }

    private void MarkSelectedCensored()
    {
        if (_isMarkCensoredCoolingDown)
        {
            StatusTextBlock.Text = "Mark Censored is cooling down.";
            return;
        }

        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            StatusTextBlock.Text = "Select an analyzed image first.";
            return;
        }

        if (item.Status != ReceiptQueueStatus.Analyzed)
        {
            StatusTextBlock.Text = "Only analyzed images can be marked censored.";
            return;
        }

        item.Status = ReceiptQueueStatus.Censored;
        StatusTextBlock.Text = $"{item.FileName} marked censored.";
        StartMarkCensoredCooldown();
        MoveToNextReceipt();
    }

    private async void StartMarkCensoredCooldown()
    {
        _isMarkCensoredCoolingDown = true;
        MarkCensoredButton.IsEnabled = false;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            _isMarkCensoredCoolingDown = false;
            MarkCensoredButton.IsEnabled = AnalyzeButton.IsEnabled;
        }
    }

    private void MoveToNextReceipt()
    {
        if (_images.Count == 0)
        {
            StatusTextBlock.Text = "No receipts loaded.";
            return;
        }

        var currentIndex = ImageListBox.SelectedIndex;
        var nextIndex = currentIndex < 0 ? 0 : currentIndex + 1;

        if (nextIndex >= _images.Count)
        {
            StatusTextBlock.Text = "Already at the last receipt.";
            return;
        }

        ImageListBox.SelectedIndex = nextIndex;
        ImageListBox.ScrollIntoView(_images[nextIndex]);
        ImageListBox.Focus();
        StatusTextBlock.Text = $"Moved to {_images[nextIndex].FileName}.";
    }

    private async Task AnalyzeItemAsync(ReceiptImageItem item, bool manageButtons = true)
    {
        if (manageButtons)
        {
            SetAnalysisButtonsEnabled(false);
        }

        item.Status = ReceiptQueueStatus.Processing;
        StatusTextBlock.Text = $"Analyzing {item.FileName}...";

        try
        {
            var result = await _workerClient.AnalyzeMockAsync(item.FullPath);
            ResultTextBox.Text = JsonSerializer.Serialize(result, JsonOptions);
            item.Status = ReceiptQueueStatus.Analyzed;
            StatusTextBlock.Text = $"{item.FileName} analyzed. Awaiting human review.";
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.ToString();
            item.Status = ReceiptQueueStatus.Error;
            StatusTextBlock.Text = $"{item.FileName} failed. Check worker output.";
        }
        finally
        {
            if (manageButtons)
            {
                SetAnalysisButtonsEnabled(true);
            }
        }
    }

    private void SetAnalysisButtonsEnabled(bool isEnabled)
    {
        AnalyzeButton.IsEnabled = isEnabled;
        AnalyzeAllPendingButton.IsEnabled = isEnabled;
        MarkCensoredButton.IsEnabled = isEnabled;
        NextReceiptButton.IsEnabled = isEnabled;
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
