using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace AccountingAssistant.App.Models;

public sealed class ReceiptImageItem : INotifyPropertyChanged
{
    private ReceiptQueueStatus _status = ReceiptQueueStatus.Pending;

    public ReceiptImageItem(string fullPath)
    {
        FullPath = fullPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public ReceiptQueueStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public string StatusText => Status switch
    {
        ReceiptQueueStatus.Pending => "Pending",
        ReceiptQueueStatus.Processing => "Processing",
        ReceiptQueueStatus.Analyzed => "Analyzed",
        ReceiptQueueStatus.Error => "Error",
        _ => "Unknown"
    };

    public MediaBrush StatusBrush => Status switch
    {
        ReceiptQueueStatus.Pending => MediaBrushes.Gray,
        ReceiptQueueStatus.Processing => MediaBrushes.Orange,
        ReceiptQueueStatus.Analyzed => MediaBrushes.Green,
        ReceiptQueueStatus.Error => MediaBrushes.Red,
        _ => MediaBrushes.Gray
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum ReceiptQueueStatus
{
    Pending,
    Processing,
    Analyzed,
    Error
}
