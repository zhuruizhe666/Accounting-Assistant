using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace AccountingAssistant.App.Services;

public sealed class ExcelExportService
{
    public static readonly IReadOnlyList<string> PerfectFormatHeaders =
    [
        "日期",
        "单号",
        "票据类型",
        "交易方",
        "总金额",
        "税额",
        "费用类别",
        "项目名",
        "部门",
        "经办人",
        "摘要",
        "备注",
        "图片路径",
        "OCR状态",
        "字段状态",
        "导出时间"
    ];

    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace CorePropertiesNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCoreNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DublinTermsNs = "http://purl.org/dc/terms/";
    private static readonly XNamespace AppPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public const int DocumentNumberColumnIndex = 2;

    public ExcelAppendResult AppendRows(string path, IReadOnlyList<ExcelReceiptRow> rows)
    {
        if (rows.Count == 0)
        {
            return new ExcelAppendResult(0, 0);
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(fullPath))
        {
            CreateWorkbook(fullPath);
        }

        EnsurePerfectFormat(fullPath);
        AppendRowsToWorkbook(fullPath, rows);
        return new ExcelAppendResult(rows.Count, 0);
    }

    public IReadOnlySet<string> ReadExistingDocumentNumbers(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        EnsurePerfectFormat(fullPath);
        return ReadColumnValues(fullPath, DocumentNumberColumnIndex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string BuildPerfectFormatGuide()
    {
        return "当前 Excel 文件不是 Accounting Assistant Perfect Format，因此已拒绝写入。\n\n" +
               "请新建一个导出文件，或将该 Excel normalize 为以下表头，且顺序完全一致：\n\n" +
               string.Join("\n", PerfectFormatHeaders) +
               "\n\n要求：\n" +
               "- 第一行必须是表头\n" +
               "- 表头数量、名称、顺序必须完全一致\n" +
               "- 多余列请自行迁移或删除\n" +
               "- 缺失列请补齐\n" +
               "- 本软件不会自动删除、移动或解释已有 Excel 内容";
    }

    private static void EnsurePerfectFormat(string path)
    {
        var actualHeaders = ReadFirstRow(path);
        if (actualHeaders.Count != PerfectFormatHeaders.Count ||
            !actualHeaders.SequenceEqual(PerfectFormatHeaders, StringComparer.Ordinal))
        {
            throw new PerfectFormatMismatchException(actualHeaders);
        }
    }

    private static void CreateWorkbook(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", BuildContentTypesDocument());
        WriteEntry(archive, "_rels/.rels", BuildPackageRelationshipsDocument());
        WriteEntry(archive, "docProps/core.xml", BuildCorePropertiesDocument());
        WriteEntry(archive, "docProps/app.xml", BuildAppPropertiesDocument());
        WriteEntry(archive, "xl/workbook.xml", BuildWorkbookDocument());
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsDocument());
        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetDocument([PerfectFormatHeaders]));
    }

    private static void AppendRowsToWorkbook(string path, IReadOnlyList<ExcelReceiptRow> rows)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml") ??
                    throw new PerfectFormatMismatchException([]);
        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream);
        }

        var sheetData = document.Root?.Element(SpreadsheetNs + "sheetData") ??
                        throw new PerfectFormatMismatchException([]);
        var lastRowIndex = sheetData
            .Elements(SpreadsheetNs + "row")
            .Select(row => ParseRowIndex(row.Attribute("r")?.Value))
            .DefaultIfEmpty(0)
            .Max();

        foreach (var row in rows)
        {
            lastRowIndex++;
            sheetData.Add(BuildRow(lastRowIndex, row.Values));
        }

        UpdateDimension(document, lastRowIndex);
        entry.Delete();
        WriteEntry(archive, "xl/worksheets/sheet1.xml", document);
    }

    private static IReadOnlyList<string> ReadFirstRow(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (worksheetEntry is null)
        {
            return [];
        }

        var sharedStrings = ReadSharedStrings(archive);
        XDocument document;
        using (var stream = worksheetEntry.Open())
        {
            document = XDocument.Load(stream);
        }

        var firstRow = document.Root?
            .Element(SpreadsheetNs + "sheetData")?
            .Elements(SpreadsheetNs + "row")
            .OrderBy(row => ParseRowIndex(row.Attribute("r")?.Value))
            .FirstOrDefault();
        if (firstRow is null)
        {
            return [];
        }

        return firstRow
            .Elements(SpreadsheetNs + "c")
            .OrderBy(cell => ParseColumnIndex(cell.Attribute("r")?.Value))
            .Select(cell => ReadCellText(cell, sharedStrings))
            .ToList();
    }

    private static IReadOnlyList<string> ReadColumnValues(string path, int columnIndex)
    {
        using var archive = ZipFile.OpenRead(path);
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (worksheetEntry is null)
        {
            return [];
        }

        var sharedStrings = ReadSharedStrings(archive);
        XDocument document;
        using (var stream = worksheetEntry.Open())
        {
            document = XDocument.Load(stream);
        }

        return document.Root?
            .Element(SpreadsheetNs + "sheetData")?
            .Elements(SpreadsheetNs + "row")
            .Where(row => ParseRowIndex(row.Attribute("r")?.Value) > 1)
            .Select(row => row.Elements(SpreadsheetNs + "c")
                .FirstOrDefault(cell => ParseColumnIndex(cell.Attribute("r")?.Value) == columnIndex))
            .Where(cell => cell is not null)
            .Select(cell => ReadCellText(cell!, sharedStrings))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?
            .Elements(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToList() ?? [];
    }

    private static string ReadCellText(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        }

        var value = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (type == "s" &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static XDocument BuildWorksheetDocument(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sheetData = new XElement(SpreadsheetNs + "sheetData");
        for (var index = 0; index < rows.Count; index++)
        {
            sheetData.Add(BuildRow(index + 1, rows[index]));
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(SpreadsheetNs + "worksheet",
                new XElement(SpreadsheetNs + "dimension",
                    new XAttribute("ref", $"A1:{ToColumnName(PerfectFormatHeaders.Count)}{rows.Count}")),
                sheetData));
    }

    private static XElement BuildRow(int rowIndex, IReadOnlyList<string> values)
    {
        return new XElement(SpreadsheetNs + "row",
            new XAttribute("r", rowIndex),
            values.Select((value, index) => BuildInlineStringCell(rowIndex, index + 1, value)));
    }

    private static XElement BuildInlineStringCell(int rowIndex, int columnIndex, string value)
    {
        return new XElement(SpreadsheetNs + "c",
            new XAttribute("r", $"{ToColumnName(columnIndex)}{rowIndex}"),
            new XAttribute("t", "inlineStr"),
            new XElement(SpreadsheetNs + "is",
                new XElement(SpreadsheetNs + "t", value)));
    }

    private static void UpdateDimension(XDocument document, int lastRowIndex)
    {
        var dimension = document.Root?.Element(SpreadsheetNs + "dimension");
        if (dimension is null)
        {
            document.Root?.AddFirst(new XElement(SpreadsheetNs + "dimension"));
            dimension = document.Root?.Element(SpreadsheetNs + "dimension");
        }

        dimension?.SetAttributeValue("ref", $"A1:{ToColumnName(PerfectFormatHeaders.Count)}{lastRowIndex}");
    }

    private static XDocument BuildContentTypesDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ContentTypesNs + "Types",
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/docProps/core.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/docProps/app.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml"))));
    }

    private static XDocument BuildPackageRelationshipsDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PackageRelationshipsNs + "Relationships",
                new XElement(PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")),
                new XElement(PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
                    new XAttribute("Target", "docProps/core.xml")),
                new XElement(PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId3"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"),
                    new XAttribute("Target", "docProps/app.xml"))));
    }

    private static XDocument BuildWorkbookDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(SpreadsheetNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelationshipsNs),
                new XElement(SpreadsheetNs + "sheets",
                    new XElement(SpreadsheetNs + "sheet",
                        new XAttribute("name", "Receipts"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(RelationshipsNs + "id", "rId1")))));
    }

    private static XDocument BuildWorkbookRelationshipsDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PackageRelationshipsNs + "Relationships",
                new XElement(PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));
    }

    private static XDocument BuildCorePropertiesDocument()
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(CorePropertiesNs + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "dc", DublinCoreNs),
                new XAttribute(XNamespace.Xmlns + "dcterms", DublinTermsNs),
                new XElement(DublinCoreNs + "creator", "Accounting Assistant"),
                new XElement(CorePropertiesNs + "lastModifiedBy", "Accounting Assistant"),
                new XElement(DublinTermsNs + "created",
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XAttribute("{http://www.w3.org/2001/XMLSchema-instance}type", "dcterms:W3CDTF"),
                    now),
                new XElement(DublinTermsNs + "modified",
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XAttribute("{http://www.w3.org/2001/XMLSchema-instance}type", "dcterms:W3CDTF"),
                    now)));
    }

    private static XDocument BuildAppPropertiesDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(AppPropertiesNs + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", VtNs),
                new XElement(AppPropertiesNs + "Application", "Accounting Assistant")));
    }

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static int ParseRowIndex(string? rowReference)
    {
        return int.TryParse(rowReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : 0;
    }

    private static int ParseColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        var index = 0;
        foreach (var character in cellReference.TakeWhile(char.IsLetter))
        {
            index = (index * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return index;
    }

    private static string ToColumnName(int columnIndex)
    {
        var dividend = columnIndex;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }
}

public sealed record ExcelReceiptRow(IReadOnlyList<string> Values)
{
    public string DocumentNumber => Values.Count >= ExcelExportService.DocumentNumberColumnIndex
        ? Values[ExcelExportService.DocumentNumberColumnIndex - 1]
        : string.Empty;
}

public sealed record ExcelAppendResult(int ExportedCount, int SkippedDuplicateCount);

public sealed class PerfectFormatMismatchException(IReadOnlyList<string> actualHeaders) : Exception("Excel file is not Accounting Assistant Perfect Format.")
{
    public IReadOnlyList<string> ActualHeaders { get; } = actualHeaders;
}
