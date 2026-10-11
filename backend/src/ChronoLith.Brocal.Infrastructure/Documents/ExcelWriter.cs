using ChronoLith.Brocal.Application.Abstractions;
using ClosedXML.Excel;

namespace ChronoLith.Brocal.Infrastructure.Documents;

/// <summary>Workbooks for exports and reports: one sheet per dataset, styled header, filters and frozen first row.</summary>
internal sealed class ExcelWriter : IExcelWriter
{
    public byte[] Write(IReadOnlyList<ExcelSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            var name = SafeName(sheet.Name, workbook);
            var worksheet = workbook.Worksheets.Add(name);
            var firstRow = 1;

            if (!string.IsNullOrWhiteSpace(sheet.Title))
            {
                var title = worksheet.Cell(1, 1);
                title.Value = sheet.Title;
                title.Style.Font.Bold = true;
                title.Style.Font.FontSize = 13;
                firstRow = 3;
            }

            for (var c = 0; c < sheet.Headers.Count; c++)
            {
                var cell = worksheet.Cell(firstRow, c + 1);
                cell.Value = sheet.Headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B365B");
            }

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var values = sheet.Rows[r];
                for (var c = 0; c < values.Length && c < sheet.Headers.Count; c++)
                {
                    SetValue(worksheet.Cell(firstRow + 1 + r, c + 1), values[c]);
                }
            }

            if (sheet.Headers.Count > 0)
            {
                var range = worksheet.Range(firstRow, 1, firstRow + Math.Max(sheet.Rows.Count, 1), sheet.Headers.Count);
                range.SetAutoFilter();
                worksheet.SheetView.FreezeRows(firstRow);
                worksheet.Columns(1, sheet.Headers.Count).AdjustToContents(firstRow, Math.Min(firstRow + 200, firstRow + sheet.Rows.Count), 8, 60);
            }
        }

        if (sheets.Count == 0)
        {
            workbook.Worksheets.Add("Datos");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case decimal number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case double or float or int or long:
                cell.Value = Convert.ToDouble(value);
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd/mm/yyyy";
                break;
            case DateTimeOffset moment:
                cell.Value = moment.DateTime;
                cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                break;
            case bool flag:
                cell.Value = flag ? "Sí" : "No";
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    private static string SafeName(string name, XLWorkbook workbook)
    {
        var clean = new string(name.Where(ch => !"[]*?/\\:".Contains(ch)).ToArray()).Trim();
        clean = string.IsNullOrEmpty(clean) ? "Hoja" : clean[..Math.Min(clean.Length, 31)];
        var candidate = clean;
        var index = 2;
        while (workbook.Worksheets.Contains(candidate))
        {
            var suffix = $" ({index++})";
            candidate = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
        }

        return candidate;
    }
}
