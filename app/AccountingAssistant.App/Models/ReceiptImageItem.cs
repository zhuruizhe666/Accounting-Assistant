using System.IO;

namespace AccountingAssistant.App.Models;

public sealed record ReceiptImageItem(string FullPath)
{
    public string FileName => Path.GetFileName(FullPath);
}
