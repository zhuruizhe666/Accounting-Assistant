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
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AccountingAssistant.App;

public partial class MainWindow : Window
{
    private static readonly FieldDefinition[] FieldDefinitions =
    [
        new("document_type", "票据类型"),
        new("document_number", "单据号"),
        new("issue_date", "日期"),
        new("counterparty_name", "交易方"),
        new("total_amount", "总金额"),
        new("tax_amount", "税额"),
        new("expense_category", "费用类别"),
        new("project_name", "项目名"),
        new("department", "部门"),
        new("handler", "经办人"),
        new("summary", "摘要"),
        new("notes", "备注")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ObservableCollection<ReceiptImageItem> _images = [];
    private readonly ObservableCollection<OcrDisplayItem> _ocrDisplayItems = [];
    private readonly ObservableCollection<FieldReviewItem> _fieldReviewItems = [];
    private readonly ProjectProfile _projectProfile;
    private readonly PythonWorkerClient _workerClient = new();
    private bool _isReviewActionCoolingDown;
    private bool _isReceiptLayoutUpdateQueued;
    private bool _isOcrWorkerReady;
    private bool _isAnalyzeAllRunning;
    private HashSet<int> _selectedOcrIndexes = [];

    public ICommand ConfirmOcrCommand { get; }

    public ICommand ApproveFieldsCommand { get; }

    public ICommand NextReceiptCommand { get; }

    public MainWindow()
    {
        ConfirmOcrCommand = new RelayCommand(_ => ConfirmSelectedOcrReview());
        ApproveFieldsCommand = new RelayCommand(_ => ApproveSelectedFields());
        NextReceiptCommand = new RelayCommand(_ => MoveToNextReceipt());

        InitializeComponent();
        DataContext = this;
        ImageListBox.ItemsSource = _images;
        OcrResultListBox.ItemsSource = _ocrDisplayItems;
        FieldReviewListBox.ItemsSource = _fieldReviewItems;
        _projectProfile = ProjectProfile.Load(FindRepoRoot(AppContext.BaseDirectory));
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
            _isOcrWorkerReady = true;
            StatusTextBlock.Text = "OCR worker ready.";
            AppendDebugDump("UI startup warmup completed. Analyze buttons enabled.");
        }
        catch (Exception ex)
        {
            _isOcrWorkerReady = false;
            StatusTextBlock.Text = "OCR worker warmup failed. Check Debug Dump.";
            AppendDebugDump($"UI startup warmup failed: {ex}");
        }
        finally
        {
            UpdateActionButtonsEnabled();
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
        UpdateActionButtonsEnabled();
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
            _ocrDisplayItems.Clear();
            _fieldReviewItems.Clear();
            _selectedOcrIndexes = [];
            UpdateActionButtonsEnabled();
            return;
        }

        ReceiptImage.Source = LoadBitmap(item.FullPath);
        _selectedOcrIndexes = [];
        ScheduleReceiptImageLayoutUpdate();
        if (item.AnalysisResult is not null)
        {
            PopulateOcrDisplayItems(item.AnalysisResult);
            PopulateFieldReviewItems(item.AnalysisResult);
            ScheduleReceiptImageLayoutUpdate();
        }
        else
        {
            _ocrDisplayItems.Clear();
            _fieldReviewItems.Clear();
            OcrOverlayCanvas.Children.Clear();
        }
        StatusTextBlock.Text = $"Selected {item.FileName}.";
        AppendDebugDump($"UI selected receipt: {item.FullPath}");
        UpdateActionButtonsEnabled();
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

        _isAnalyzeAllRunning = true;
        UpdateActionButtonsEnabled();
        try
        {
            foreach (var item in pendingItems)
            {
                ImageListBox.SelectedItem = item;
                await AnalyzeItemAsync(item, manageButtons: false);
            }

            StatusTextBlock.Text = $"Processed {pendingItems.Count} pending image(s).";
        }
        finally
        {
            _isAnalyzeAllRunning = false;
            UpdateActionButtonsEnabled();
        }
    }

    private void ConfirmOcrButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelectedOcrReview();
    }

    private void ApproveFieldsButton_Click(object sender, RoutedEventArgs e)
    {
        ApproveSelectedFields();
    }

    private void NextReceiptButton_Click(object sender, RoutedEventArgs e)
    {
        MoveToNextReceipt();
    }

    private void ConfirmSelectedOcrReview()
    {
        if (_isReviewActionCoolingDown)
        {
            StatusTextBlock.Text = "Review action is cooling down.";
            return;
        }

        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            StatusTextBlock.Text = "Select a receipt in OCR Review first.";
            return;
        }

        if (item.Status != ReceiptQueueStatus.OcrReview || item.IsSemanticParsing)
        {
            StatusTextBlock.Text = "Shift+Enter only confirms OCR Review receipts.";
            return;
        }

        if (item.AnalysisResult is null)
        {
            StatusTextBlock.Text = "Current receipt has no OCR result to confirm.";
            return;
        }

        var analysisResult = item.AnalysisResult;
        item.IsSemanticParsing = true;
        StatusTextBlock.Text = $"{item.FileName} OCR confirmed. Field parsing started.";
        AppendDebugDump($"UI OCR review confirmed; semantic parse requested: {item.FullPath}");
        UpdateActionButtonsEnabled();
        StartReviewActionCooldown();
        _ = ParseSemanticForReceiptAsync(item, analysisResult);
        MoveToNextReceipt();
    }

    private async Task ParseSemanticForReceiptAsync(ReceiptImageItem item, ReceiptAnalysisResult analysisResult)
    {
        try
        {
            var semanticResult = await _workerClient.ParseSemanticAsync(analysisResult.OcrItems);
            item.AnalysisResult = analysisResult with
            {
                SemanticFields = semanticResult.SemanticFields,
                ReviewFields = BuildReviewedFields(semanticResult.SemanticFields),
                SemanticStatus = semanticResult.SemanticStatus
            };
            item.Status = ReceiptQueueStatus.FieldReview;
            AppendDebugDump($"UI semantic parse attached: status={semanticResult.SemanticStatus.Status}, path={item.FullPath}");

            if (IsSelectedReceipt(item))
            {
                PopulateFieldReviewItems(item.AnalysisResult);
                StatusTextBlock.Text = $"{item.FileName} fields parsed. Awaiting field review.";
            }
        }
        catch (Exception ex)
        {
            item.Status = ReceiptQueueStatus.Error;
            AppendDebugDump($"UI semantic parse failed: {ex}");

            if (IsSelectedReceipt(item))
            {
                StatusTextBlock.Text = $"{item.FileName} field parsing failed. Check Debug Dump.";
            }
        }
        finally
        {
            item.IsSemanticParsing = false;
            UpdateActionButtonsEnabled();
        }
    }

    private void ApproveSelectedFields()
    {
        if (_isReviewActionCoolingDown)
        {
            StatusTextBlock.Text = "Review action is cooling down.";
            return;
        }

        if (ImageListBox.SelectedItem is not ReceiptImageItem item)
        {
            StatusTextBlock.Text = "Select a receipt in Field Review first.";
            return;
        }

        if (item.Status != ReceiptQueueStatus.FieldReview)
        {
            StatusTextBlock.Text = "Ctrl+Enter only approves Field Review receipts.";
            return;
        }

        SyncFieldReviewItemsToReceipt(item);
        item.Status = ReceiptQueueStatus.Approved;
        StatusTextBlock.Text = $"{item.FileName} approved.";
        AppendDebugDump($"UI field review approved: {item.FullPath}");
        StartReviewActionCooldown();
        MoveToNextReceipt();
    }

    private async void StartReviewActionCooldown()
    {
        _isReviewActionCoolingDown = true;
        ConfirmOcrButton.IsEnabled = false;
        ApproveFieldsButton.IsEnabled = false;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            _isReviewActionCoolingDown = false;
            UpdateActionButtonsEnabled();
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
        UpdateActionButtonsEnabled();
    }

    private async Task AnalyzeItemAsync(ReceiptImageItem item, bool manageButtons = true)
    {
        if (manageButtons)
        {
            UpdateActionButtonsEnabled();
        }

        item.Status = ReceiptQueueStatus.Processing;
        UpdateActionButtonsEnabled();
        StatusTextBlock.Text = $"Analyzing {item.FileName}...";
        AppendDebugDump($"UI analyze started: {item.FullPath}");

        try
        {
            var result = await _workerClient.AnalyzeAsync(item.FullPath);
            item.AnalysisResult = result;
            PopulateOcrDisplayItems(result);
            PopulateFieldReviewItems(result);
            RenderOcrHighlights(result);
            item.Status = ReceiptQueueStatus.OcrReview;
            StatusTextBlock.Text = $"{item.FileName} analyzed. Awaiting OCR review.";
            AppendDebugDump($"UI analyze completed: ocr_items={result.OcrItems.Count}. Waiting for OCR review before semantic parsing.");
        }
        catch (Exception ex)
        {
            _ocrDisplayItems.Clear();
            _fieldReviewItems.Clear();
            _ocrDisplayItems.Add(new OcrDisplayItem(0, "Analysis failed. Open Debug Dump for details.", 0, "Error"));
            item.Status = ReceiptQueueStatus.Error;
            StatusTextBlock.Text = $"{item.FileName} failed. Check worker output.";
            AppendDebugDump($"UI analyze failed: {ex}");
        }
        finally
        {
            if (manageButtons)
            {
                UpdateActionButtonsEnabled();
            }
        }
    }

    private void UpdateActionButtonsEnabled()
    {
        var selectedItem = ImageListBox.SelectedItem as ReceiptImageItem;
        var canUseWorker = _isOcrWorkerReady && !_isAnalyzeAllRunning;
        var hasPendingItems = _images.Any(item => item.Status == ReceiptQueueStatus.Pending);

        AnalyzeButton.IsEnabled = canUseWorker &&
                                  selectedItem is { Status: ReceiptQueueStatus.Pending, IsSemanticParsing: false };
        AnalyzeAllPendingButton.IsEnabled = _isOcrWorkerReady && !_isAnalyzeAllRunning && hasPendingItems;
        ConfirmOcrButton.IsEnabled = selectedItem is { Status: ReceiptQueueStatus.OcrReview, IsSemanticParsing: false } &&
                                     !_isReviewActionCoolingDown;
        ApproveFieldsButton.IsEnabled = selectedItem is { Status: ReceiptQueueStatus.FieldReview, IsSemanticParsing: false } &&
                                        !_isReviewActionCoolingDown;
        NextReceiptButton.IsEnabled = _images.Count > 0;
    }

    private void ReceiptScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleReceiptImageLayoutUpdate();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleReceiptImageLayoutUpdate();
    }

    private void ScheduleReceiptImageLayoutUpdate()
    {
        if (_isReceiptLayoutUpdateQueued)
        {
            return;
        }

        _isReceiptLayoutUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isReceiptLayoutUpdateQueued = false;
            UpdateReceiptImageLayout();

            if (ImageListBox.SelectedItem is ReceiptImageItem { AnalysisResult: not null } item)
            {
                RenderOcrHighlights(item.AnalysisResult);
            }
        }, DispatcherPriority.Loaded);
    }

    private void OcrResultListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OcrResultListBox.SelectedItem is not OcrDisplayItem item)
        {
            _selectedOcrIndexes = [];
            return;
        }

        _selectedOcrIndexes = [item.Index];

        if (ImageListBox.SelectedItem is ReceiptImageItem { AnalysisResult: not null } receipt)
        {
            RenderOcrHighlights(receipt.AnalysisResult);
        }
    }

    private void FieldReviewListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldReviewListBox.SelectedItem is not FieldReviewItem item)
        {
            _selectedOcrIndexes = [];
            return;
        }

        _selectedOcrIndexes = item.OcrRefs.ToHashSet();

        if (ImageListBox.SelectedItem is ReceiptImageItem { AnalysisResult: not null } receipt)
        {
            RenderOcrHighlights(receipt.AnalysisResult);
        }
    }

    private void FieldReviewListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var clickedComboBox = FindVisualParent<System.Windows.Controls.ComboBox>(e.OriginalSource as DependencyObject);
        if (clickedComboBox is not null)
        {
            FocusEditableComboBox(clickedComboBox);
            return;
        }

        var listBoxItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listBoxItem?.Content is not FieldReviewItem item)
        {
            return;
        }

        FieldReviewListBox.SelectedItem = item;
        Dispatcher.BeginInvoke(() =>
        {
            var container = FieldReviewListBox.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
            var comboBox = FindVisualChild<System.Windows.Controls.ComboBox>(container);
            if (comboBox is not null)
            {
                FocusEditableComboBox(comboBox);
            }
        }, DispatcherPriority.Input);
    }

    private void FieldValueComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ImageListBox.SelectedItem is ReceiptImageItem receipt)
        {
            SyncFieldReviewItemsToReceipt(receipt);
        }
    }

    private void FieldValueComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageListBox.SelectedItem is ReceiptImageItem receipt)
        {
            Dispatcher.BeginInvoke(() => SyncFieldReviewItemsToReceipt(receipt), DispatcherPriority.Background);
        }
    }

    private bool IsSelectedReceipt(ReceiptImageItem item)
    {
        return ReferenceEquals(ImageListBox.SelectedItem, item);
    }

    private static void FocusEditableComboBox(System.Windows.Controls.ComboBox comboBox)
    {
        comboBox.Focus();
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is System.Windows.Controls.TextBox textBox)
        {
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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

    private void OcrResultListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OcrResultListBox.SelectedItem is not OcrDisplayItem item ||
            ImageListBox.SelectedItem is not ReceiptImageItem { AnalysisResult: not null } receipt)
        {
            return;
        }

        var dialog = new EditOcrTextDialog(item.Index, item.Text)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        UpdateOcrTextCorrection(receipt, item.Index, dialog.EditedText);
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
        var highConfidenceCount = 0;
        var mediumConfidenceCount = 0;
        var lowConfidenceCount = 0;

        foreach (var (item, index) in result.OcrItems.Select((value, index) => (value, index)))
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
            var isSelected = _selectedOcrIndexes.Contains(index);
            var style = GetOcrHighlightStyle(item.Confidence, isSelected);

            if (item.Confidence >= 0.90m)
            {
                highConfidenceCount++;
            }
            else if (item.Confidence >= 0.60m)
            {
                mediumConfidenceCount++;
            }
            else
            {
                lowConfidenceCount++;
            }

            var rectangle = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Stroke = style.Stroke,
                StrokeThickness = isSelected ? 4 : 2,
                Fill = style.Fill,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = index
            };
            rectangle.MouseLeftButtonDown += OcrRectangle_MouseLeftButtonDown;

            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            OcrOverlayCanvas.Children.Add(rectangle);
        }

        AppendDebugDump($"UI OCR highlights rendered: boxes={OcrOverlayCanvas.Children.Count}, high={highConfidenceCount}, medium={mediumConfidenceCount}, low={lowConfidenceCount}.");
    }

    private void OcrRectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index })
        {
            return;
        }

        SelectOcrDisplayItem(index);
        AppendDebugDump($"UI OCR box clicked: index={index}");
        e.Handled = true;
    }

    private void SelectOcrDisplayItem(int index)
    {
        var displayItem = _ocrDisplayItems.FirstOrDefault(item => item.Index == index);
        if (displayItem is null)
        {
            return;
        }

        OcrResultListBox.SelectedItem = displayItem;
        OcrResultListBox.ScrollIntoView(displayItem);
    }

    private void PopulateOcrDisplayItems(ReceiptAnalysisResult result)
    {
        _ocrDisplayItems.Clear();
        foreach (var (item, index) in result.OcrItems.Select((value, index) => (value, index)))
        {
            _ocrDisplayItems.Add(new OcrDisplayItem(
                index,
                item.DisplayText,
                item.Confidence,
                $"{item.Confidence:P0}",
                item.IsCorrected));
        }
    }

    private void UpdateOcrTextCorrection(ReceiptImageItem receipt, int ocrIndex, string editedText)
    {
        var result = receipt.AnalysisResult;
        if (result is null || ocrIndex < 0 || ocrIndex >= result.OcrItems.Count)
        {
            return;
        }

        var ocrItems = result.OcrItems.ToList();
        var original = ocrItems[ocrIndex];
        var normalizedEdit = editedText.Trim();
        var correctedText = normalizedEdit == original.Text ? null : normalizedEdit;
        ocrItems[ocrIndex] = original with { CorrectedText = correctedText };

        receipt.AnalysisResult = result with { OcrItems = ocrItems };
        _selectedOcrIndexes = [ocrIndex];
        PopulateOcrDisplayItems(receipt.AnalysisResult);
        SelectOcrDisplayItem(ocrIndex);
        RenderOcrHighlights(receipt.AnalysisResult);

        StatusTextBlock.Text = $"{receipt.FileName} OCR text updated at index {ocrIndex}.";
        AppendDebugDump($"UI OCR text edited: path={receipt.FullPath}, index={ocrIndex}, corrected={ocrItems[ocrIndex].IsCorrected}.");
    }

    private void PopulateFieldReviewItems(ReceiptAnalysisResult result)
    {
        _fieldReviewItems.Clear();
        if (result.ReviewFields is null &&
            string.Equals(result.SemanticStatus?.Status, "pending_ocr_review", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reviewFields = result.ReviewFields ?? BuildReviewedFields(result.SemanticFields);

        foreach (var definition in FieldDefinitions)
        {
            reviewFields.TryGetValue(definition.FieldName, out var field);
            _fieldReviewItems.Add(new FieldReviewItem
            {
                FieldName = definition.FieldName,
                Label = definition.Label,
                Value = field?.Value ?? string.Empty,
                Confidence = field?.Confidence ?? 0,
                OcrRefs = field?.OcrRefs ?? [],
                Suggestions = _projectProfile.GetSuggestions(definition.FieldName)
            });
        }
    }

    private static Dictionary<string, ReviewedField> BuildReviewedFields(Dictionary<string, IReadOnlyList<SemanticField>>? semanticFields)
    {
        var reviewFields = new Dictionary<string, ReviewedField>();

        foreach (var definition in FieldDefinitions)
        {
            var semanticField = semanticFields is not null &&
                                semanticFields.TryGetValue(definition.FieldName, out var fields)
                ? fields.FirstOrDefault()
                : null;

            reviewFields[definition.FieldName] = new ReviewedField(
                semanticField?.Value ?? string.Empty,
                semanticField?.Confidence ?? 0,
                semanticField?.OcrRefs ?? [],
                semanticField is null ? "manual" : "semantic");
        }

        return reviewFields;
    }

    private void SyncFieldReviewItemsToReceipt(ReceiptImageItem receipt)
    {
        var result = receipt.AnalysisResult;
        if (result is null || _fieldReviewItems.Count == 0)
        {
            return;
        }

        var reviewFields = _fieldReviewItems.ToDictionary(
            item => item.FieldName,
            item => new ReviewedField(
                item.Value.Trim(),
                item.Confidence,
                item.OcrRefs,
                "review"));

        receipt.AnalysisResult = result with { ReviewFields = reviewFields };
        AppendDebugDump($"UI field review synced: path={receipt.FullPath}, fields={reviewFields.Count}.");
    }

    private static OcrHighlightStyle GetOcrHighlightStyle(decimal confidence, bool isSelected)
    {
        if (isSelected)
        {
            return new OcrHighlightStyle(
                System.Windows.Media.Brushes.DodgerBlue,
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 30, 144, 255)));
        }

        if (confidence >= 0.90m)
        {
            return new OcrHighlightStyle(
                System.Windows.Media.Brushes.LimeGreen,
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 50, 205, 50)));
        }

        if (confidence >= 0.60m)
        {
            return new OcrHighlightStyle(
                System.Windows.Media.Brushes.Goldenrod,
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 215, 0)));
        }

        return new OcrHighlightStyle(
            System.Windows.Media.Brushes.Red,
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 255, 0, 0)));
    }

    private sealed record OcrHighlightStyle(System.Windows.Media.Brush Stroke, System.Windows.Media.Brush Fill);

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

    private static string FindRepoRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private sealed record FieldDefinition(string FieldName, string Label);
}
