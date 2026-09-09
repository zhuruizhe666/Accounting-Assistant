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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        _workerClient.DebugOutputReceived += AppendDebugDump;
        StatusTextBlock.Text = "Ready. Select receipt images to start.";
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AnalyzeButton.IsEnabled = false;
        AnalyzeAllPendingButton.IsEnabled = false;
        StatusTextBlock.Text = "OCR worker warming up...";
        AppendDebugDump("UI startup warmup started. Analyze buttons disabled.");

        try
        {
            await _workerClient.WarmupAsync();
            StatusTextBlock.Text = "OCR worker ready.";
            AppendDebugDump("UI startup warmup completed. Analyze buttons enabled.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "OCR worker warmup failed. Check Debug Dump.";
            AppendDebugDump($"UI startup warmup failed: {ex}");
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            AnalyzeAllPendingButton.IsEnabled = true;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _workerClient.DebugOutputReceived -= AppendDebugDump;
        _workerClient.Dispose();
    }

    private void SelectImagesButton_Click(object sender, RoutedEventArgs e)
    {
        AppendDebugDump("UI select images requested.");
        LoadImages(SelectImageFiles(), "No images selected.");
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        AppendDebugDump("UI select folder requested.");
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
        AppendDebugDump($"UI queue updated: added={addedCount}, skipped_duplicates={skippedCount}, total={_images.Count}.");
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
            .Where(path => extensions.Contains(System.IO.Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            ReceiptImage.Source = null;
            OcrOverlayCanvas.Children.Clear();
            ResultTextBox.Clear();
            return;
        }

        ReceiptImage.Source = LoadBitmap(item.FullPath);
        UpdateReceiptImageLayout();
        if (item.AnalysisResult is not null)
        {
            ResultTextBox.Text = JsonSerializer.Serialize(item.AnalysisResult, JsonOptions);
            RenderOcrHighlights(item.AnalysisResult);
        }
        else
        {
            ResultTextBox.Clear();
            OcrOverlayCanvas.Children.Clear();
        }
        StatusTextBlock.Text = $"Selected {item.FileName}.";
        AppendDebugDump($"UI selected receipt: {item.FullPath}");
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
        AppendDebugDump($"UI receipt marked censored: {item.FullPath}");
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
        AppendDebugDump($"UI moved to next receipt: index={nextIndex}, path={_images[nextIndex].FullPath}");
    }

    private async Task AnalyzeItemAsync(ReceiptImageItem item, bool manageButtons = true)
    {
        if (manageButtons)
        {
            SetAnalysisButtonsEnabled(false);
        }

        item.Status = ReceiptQueueStatus.Processing;
        StatusTextBlock.Text = $"Analyzing {item.FileName}...";
        AppendDebugDump($"UI analyze started: {item.FullPath}");

        try
        {
            var result = await _workerClient.AnalyzeAsync(item.FullPath);
            item.AnalysisResult = result;
            ResultTextBox.Text = JsonSerializer.Serialize(result, JsonOptions);
            RenderOcrHighlights(result);
            item.Status = ReceiptQueueStatus.Analyzed;
            StatusTextBlock.Text = $"{item.FileName} analyzed. Awaiting human review.";
            AppendDebugDump($"UI analyze completed: ocr_items={result.OcrItems.Count}, semantic_status={result.SemanticStatus?.Status ?? "none"}.");
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.ToString();
            item.Status = ReceiptQueueStatus.Error;
            StatusTextBlock.Text = $"{item.FileName} failed. Check worker output.";
            AppendDebugDump($"UI analyze failed: {ex}");
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

    private void ReceiptScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateReceiptImageLayout();

        if (ImageListBox.SelectedItem is ReceiptImageItem { AnalysisResult: not null } item)
        {
            RenderOcrHighlights(item.AnalysisResult);
        }
    }

    private void UpdateReceiptImageLayout()
    {
        if (ReceiptImage.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return;
        }

        var viewportWidth = ReceiptScrollViewer.ViewportWidth;
        if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
        {
            viewportWidth = ReceiptScrollViewer.ActualWidth;
        }

        if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
        {
            return;
        }

        var renderedWidth = viewportWidth;
        var renderedHeight = renderedWidth * bitmap.PixelHeight / bitmap.PixelWidth;

        ReceiptImageHost.Width = renderedWidth;
        ReceiptImageHost.Height = renderedHeight;
        ReceiptImage.Width = renderedWidth;
        ReceiptImage.Height = renderedHeight;
        OcrOverlayCanvas.Width = renderedWidth;
        OcrOverlayCanvas.Height = renderedHeight;
    }

    private void RenderOcrHighlights(ReceiptAnalysisResult result)
    {
        OcrOverlayCanvas.Children.Clear();

        if (ReceiptImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        UpdateReceiptImageLayout();

        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 || ReceiptImage.Width <= 0 || ReceiptImage.Height <= 0)
        {
            return;
        }

        var scaleX = ReceiptImage.Width / bitmap.PixelWidth;
        var scaleY = ReceiptImage.Height / bitmap.PixelHeight;

        foreach (var item in result.OcrItems)
        {
            if (item.BBox.Count == 0)
            {
                continue;
            }

            var xs = item.BBox.Select(point => point[0]).ToList();
            var ys = item.BBox.Select(point => point[1]).ToList();
            var left = xs.Min() * scaleX;
            var top = ys.Min() * scaleY;
            var width = Math.Max(1, (xs.Max() - xs.Min()) * scaleX);
            var height = Math.Max(1, (ys.Max() - ys.Min()) * scaleY);

            var rectangle = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Stroke = System.Windows.Media.Brushes.LimeGreen,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 50, 205, 50))
            };

            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            OcrOverlayCanvas.Children.Add(rectangle);
        }

        AppendDebugDump($"UI OCR highlights rendered: boxes={OcrOverlayCanvas.Children.Count}");
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

    private void AppendDebugDump(string message)
    {
        Dispatcher.Invoke(() =>
        {
            DebugDumpTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
            DebugDumpTextBox.ScrollToEnd();
        });
    }
}
