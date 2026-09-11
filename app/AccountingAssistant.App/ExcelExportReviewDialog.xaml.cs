using AccountingAssistant.App.Models;
using AccountingAssistant.App.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AccountingAssistant.App;

public partial class ExcelExportReviewDialog : Window, INotifyPropertyChanged
{
    public ExcelExportReviewDialog(
        string targetPath,
        IReadOnlyList<ExcelExportReviewItem> items)
    {
        InitializeComponent();
        TargetPath = targetPath;
        Items = new ObservableCollection<ExcelExportReviewItem>(items);
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExcelExportReviewItem.ShouldAppend))
                {
                    RefreshSummary();
                }
            };
        }

        DataContext = this;
    }

    public string TargetPath { get; }

    public ObservableCollection<ExcelExportReviewItem> Items { get; }

    public int TotalCount => Items.Count;

    public int DuplicateCount => Items.Count(item => item.IsDuplicateDocumentNumber);

    public int NewCount => TotalCount - DuplicateCount;

    public int SelectedCount => Items.Count(item => item.ShouldAppend);

    public bool HasDuplicates => DuplicateCount > 0;

    public bool CanExport => SelectedCount > 0;

    public string ExportButtonText => $"Export {SelectedCount} selected";

    public string SummaryText
    {
        get
        {
            var duplicateCount = Items.Count(item => item.IsDuplicateDocumentNumber);
            var selectedCount = Items.Count(item => item.ShouldAppend);
            return $"{Items.Count} approved receipt(s), {duplicateCount} duplicate document number(s), {selectedCount} selected for append.";
        }
    }

    public IReadOnlyList<ExcelExportReviewItem> SelectedItems => Items
        .Where(item => item.ShouldAppend)
        .ToList();

    private void AppendNewOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.ShouldAppend = !item.IsDuplicateDocumentNumber;
        }

        RefreshSummary();
    }

    private void AppendAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.ShouldAppend = true;
        }

        RefreshSummary();
    }

    private void AppendNoneButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.ShouldAppend = false;
        }

        RefreshSummary();
    }

    private void ExportSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport)
        {
            return;
        }

        DialogResult = true;
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(TargetPath);
        CopyPathButton.Content = "Copied";
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportButtonText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ExcelExportReviewItem : INotifyPropertyChanged
{
    private bool _shouldAppend;

    public ExcelExportReviewItem(
        ReceiptImageItem receipt,
        ExcelReceiptRow row,
        bool isDuplicateDocumentNumber)
    {
        Receipt = receipt;
        Row = row;
        IsDuplicateDocumentNumber = isDuplicateDocumentNumber;
        _shouldAppend = !isDuplicateDocumentNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReceiptImageItem Receipt { get; }

    public ExcelReceiptRow Row { get; }

    public bool IsDuplicateDocumentNumber { get; }

    public bool ShouldAppend
    {
        get => _shouldAppend;
        set
        {
            if (_shouldAppend == value)
            {
                return;
            }

            _shouldAppend = value;
            OnPropertyChanged();
        }
    }

    public string DuplicateStatus => IsDuplicateDocumentNumber ? "重复单号" : "新单号";

    public string DocumentNumber => Row.DocumentNumber;

    public string FileName => Receipt.FileName;

    public string IssueDate => GetValue(0);

    public string TotalAmount => GetValue(4);

    public string Counterparty => GetValue(3);

    private string GetValue(int index)
    {
        return Row.Values.Count > index ? Row.Values[index] : string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
