using ChronoLith.Brocal.Application.Abstractions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace ChronoLith.Brocal.Infrastructure.Documents;

/// <summary>
/// Renders <see cref="BusinessDocument"/>s with MigraDoc (PDFsharp, MIT license): header band with the company logo,
/// name and colors, folio and status, field sections in two columns, tables, totals, texts, photo grid and signatures.
/// </summary>
internal sealed class PdfRenderer : IPdfRenderer
{
    private const string FontFamily = "Brocal Sans";

    public byte[] Render(BusinessDocument document, DocumentBranding branding)
    {
        var primary = ParseColor(branding.PrimaryColor, new Color(11, 54, 91));
        var accent = ParseColor(branding.AccentColor, new Color(229, 142, 56));
        var light = Blend(primary, 0.92);

        var doc = new Document();
        doc.Info.Title = $"{document.Title} {document.Folio}";
        doc.Info.Author = branding.CompanyName;
        DefineStyles(doc, primary);

        var section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.6);

        AddHeader(section, document, branding, primary, accent);
        AddFooter(section, document, branding);

        if (document.Watermark is { } watermark)
        {
            var mark = section.AddParagraph(watermark);
            mark.Format.Font.Size = 26;
            mark.Format.Font.Bold = true;
            mark.Format.Font.Color = new Color(200, 60, 60);
            mark.Format.Alignment = ParagraphAlignment.Center;
            mark.Format.SpaceAfter = Unit.FromPoint(6);
        }

        foreach (var (heading, fields) in document.Sections.Where(s => s.Fields.Any(f => !string.IsNullOrWhiteSpace(f.Value))))
        {
            AddHeading(section, heading, primary);
            AddFields(section, fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToList(), light);
        }

        foreach (var table in document.Tables)
        {
            if (table.Title is not null)
            {
                AddHeading(section, table.Title, primary);
            }

            AddTable(section, table, primary, light);
        }

        if (document.Totals is { } totals)
        {
            AddTotals(section, totals, primary);
        }

        foreach (var (heading, text) in document.Texts)
        {
            AddHeading(section, heading, primary);
            var paragraph = section.AddParagraph(text);
            paragraph.Format.SpaceAfter = Unit.FromPoint(6);
        }

        if (document.Signatures.Count > 0)
        {
            AddSignatures(section, document.Signatures, primary);
        }

        if (document.Images.Count > 0)
        {
            AddHeading(section, "Evidencia fotográfica", primary);
            AddImages(section, document.Images);
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static void DefineStyles(Document doc, Color primary)
    {
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontFamily;
        normal.Font.Size = 8.5;
        normal.Font.Color = new Color(33, 37, 41);

        var heading = doc.Styles.AddStyle("SectionHeading", StyleNames.Normal);
        heading.Font.Size = 10;
        heading.Font.Bold = true;
        heading.Font.Color = primary;
        heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);
        heading.ParagraphFormat.Borders.Bottom.Width = 0.75;
        heading.ParagraphFormat.Borders.Bottom.Color = primary;
        heading.ParagraphFormat.KeepWithNext = true;
    }

    private static void AddHeader(Section section, BusinessDocument document, DocumentBranding branding, Color primary, Color accent)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromCentimeter(3.4));
        table.AddColumn(Unit.FromCentimeter(9.4));
        table.AddColumn(Unit.FromCentimeter(5.8));

        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        row.Height = Unit.FromCentimeter(2.1);

        if (branding.Logo is { Length: > 0 } logo)
        {
            var image = row.Cells[0].AddImage("base64:" + Convert.ToBase64String(logo));
            image.LockAspectRatio = true;
            image.Width = Unit.FromCentimeter(3.0);
            image.Height = Unit.FromCentimeter(1.8);
        }
        else
        {
            var initials = row.Cells[0].AddParagraph(Initials(branding.CompanyName));
            initials.Format.Font.Size = 20;
            initials.Format.Font.Bold = true;
            initials.Format.Font.Color = primary;
        }

        var company = row.Cells[1].AddParagraph();
        company.AddFormattedText(branding.CompanyName, new Font { Bold = true, Size = 12, Color = primary });
        if (!string.IsNullOrWhiteSpace(branding.LegalName) && branding.LegalName != branding.CompanyName)
        {
            company.AddLineBreak();
            company.AddFormattedText(branding.LegalName, new Font { Size = 8 });
        }

        if (!string.IsNullOrWhiteSpace(branding.TaxId))
        {
            company.AddLineBreak();
            company.AddFormattedText($"RFC: {branding.TaxId}", new Font { Size = 8 });
        }

        var band = row.Cells[2];
        band.Shading.Color = primary;
        band.Format.Alignment = ParagraphAlignment.Right;
        band.Format.RightIndent = Unit.FromPoint(6);
        var title = band.AddParagraph(document.Title.ToUpperInvariant());
        title.Format.Font.Color = Colors.White;
        title.Format.Font.Size = 9;
        title.Format.Font.Bold = true;
        var folio = band.AddParagraph(document.Folio);
        folio.Format.Font.Color = Colors.White;
        folio.Format.Font.Size = 15;
        folio.Format.Font.Bold = true;
        if (document.Date is not null || document.Status is not null)
        {
            var meta = band.AddParagraph(string.Join(" · ", new[] { document.Date, document.Status }.Where(s => !string.IsNullOrWhiteSpace(s))));
            meta.Format.Font.Color = Colors.White;
            meta.Format.Font.Size = 8;
        }

        var rule = section.AddParagraph();
        rule.Format.Borders.Bottom.Width = 2;
        rule.Format.Borders.Bottom.Color = accent;
        rule.Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static void AddFooter(Section section, BusinessDocument document, DocumentBranding branding)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = new Color(110, 117, 125);
        footer.Format.Borders.Top.Width = 0.5;
        footer.Format.Borders.Top.Color = new Color(200, 205, 210);
        footer.AddText(branding.Footer ?? branding.CompanyName);
        footer.AddTab();
        footer.AddText("Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();
        footer.Format.TabStops.AddTabStop(Unit.FromCentimeter(18.6), TabAlignment.Right);

        if (document.QrText is not null)
        {
            var reference = section.Footers.Primary.AddParagraph(document.QrText);
            reference.Format.Font.Size = 6;
            reference.Format.Font.Color = new Color(150, 155, 160);
        }
    }

    private static void AddHeading(Section section, string text, Color primary) => section.AddParagraph(text, "SectionHeading");

    private static void AddFields(Section section, IReadOnlyList<DocumentField> fields, Color light)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = new Color(222, 226, 230);
        table.AddColumn(Unit.FromCentimeter(3.3));
        table.AddColumn(Unit.FromCentimeter(6.0));
        table.AddColumn(Unit.FromCentimeter(3.3));
        table.AddColumn(Unit.FromCentimeter(6.0));

        for (var i = 0; i < fields.Count; i += 2)
        {
            var row = table.AddRow();
            Fill(row.Cells[0], row.Cells[1], fields[i], light);
            if (i + 1 < fields.Count)
            {
                Fill(row.Cells[2], row.Cells[3], fields[i + 1], light);
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void Fill(Cell labelCell, Cell valueCell, DocumentField field, Color light)
    {
        labelCell.Shading.Color = light;
        var label = labelCell.AddParagraph(field.Label);
        label.Format.Font.Bold = true;
        label.Format.Font.Size = 7.5;
        valueCell.AddParagraph(field.Value ?? string.Empty);
    }

    private static void AddTable(Section section, DocumentTable data, Color primary, Color light)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = new Color(222, 226, 230);

        var widths = Distribute(data);
        foreach (var width in widths)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = primary;
        for (var i = 0; i < data.Headers.Count; i++)
        {
            var paragraph = header.Cells[i].AddParagraph(data.Headers[i]);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.Font.Color = Colors.White;
            paragraph.Format.Alignment = IsRight(data, i) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var index = 0;
        foreach (var values in data.Rows)
        {
            var row = table.AddRow();
            if (index++ % 2 == 1)
            {
                row.Shading.Color = light;
            }

            for (var i = 0; i < data.Headers.Count && i < values.Count; i++)
            {
                var paragraph = row.Cells[i].AddParagraph(values[i] ?? string.Empty);
                paragraph.Format.Alignment = IsRight(data, i) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
            }
        }

        if (data.Rows.Count == 0)
        {
            var row = table.AddRow();
            row.Cells[0].MergeRight = data.Headers.Count - 1;
            row.Cells[0].AddParagraph("Sin registros.");
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static bool IsRight(DocumentTable table, int column) => table.RightAligned is { } right && column < right.Count && right[column];

    /// <summary>18.6 cm of usable width split by the longest text of each column (bounded so no column starves).</summary>
    private static double[] Distribute(DocumentTable table)
    {
        var weights = table.Headers
            .Select((header, column) =>
            {
                var longest = table.Rows.Select(r => column < r.Count ? r[column]?.Length ?? 0 : 0).DefaultIfEmpty(0).Max();
                return Math.Clamp(Math.Max(header.Length, longest), 6, 45);
            })
            .Select(w => (double)w)
            .ToArray();

        var total = weights.Sum();
        return weights.Select(w => Math.Round(18.6 * w / total, 2)).ToArray();
    }

    private static void AddTotals(Section section, DocumentTotals totals, Color primary)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromCentimeter(12.6));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));

        for (var i = 0; i < totals.Lines.Count; i++)
        {
            var line = totals.Lines[i];
            var isLast = i == totals.Lines.Count - 1;
            var row = table.AddRow();
            var label = row.Cells[1].AddParagraph(line.Label);
            label.Format.Alignment = ParagraphAlignment.Right;
            label.Format.Font.Bold = isLast;
            var value = row.Cells[2].AddParagraph(line.Value ?? string.Empty);
            value.Format.Alignment = ParagraphAlignment.Right;
            value.Format.Font.Bold = isLast;
            if (isLast)
            {
                row.Cells[1].Shading.Color = primary;
                row.Cells[2].Shading.Color = primary;
                label.Format.Font.Color = Colors.White;
                value.Format.Font.Color = Colors.White;
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static void AddSignatures(Section section, IReadOnlyList<DocumentSignature> signatures, Color primary)
    {
        section.AddParagraph().Format.SpaceBefore = Unit.FromPoint(10);
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Rows.LeftIndent = 0;
        var width = 18.6 / signatures.Count;
        foreach (var _ in signatures)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        var images = table.AddRow();
        images.Height = Unit.FromCentimeter(2.2);
        images.VerticalAlignment = VerticalAlignment.Bottom;
        var lines = table.AddRow();

        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            images.Cells[i].Format.Alignment = ParagraphAlignment.Center;
            if (signature.Image is { Length: > 0 } image)
            {
                var picture = images.Cells[i].AddImage("base64:" + Convert.ToBase64String(image));
                picture.LockAspectRatio = true;
                picture.Height = Unit.FromCentimeter(2.0);
                picture.Width = Unit.FromCentimeter(Math.Min(width - 0.6, 6));
            }

            var cell = lines.Cells[i];
            cell.Format.Alignment = ParagraphAlignment.Center;
            cell.Borders.Top.Width = 0.75;
            cell.Borders.Top.Color = primary;
            cell.Format.LeftIndent = Unit.FromPoint(8);
            cell.Format.RightIndent = Unit.FromPoint(8);
            var label = cell.AddParagraph(signature.Label);
            label.Format.Font.Bold = true;
            cell.AddParagraph(signature.Name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(signature.Position))
            {
                cell.AddParagraph(signature.Position).Format.Font.Size = 7.5;
            }

            if (!string.IsNullOrWhiteSpace(signature.Date))
            {
                cell.AddParagraph(signature.Date).Format.Font.Size = 7.5;
            }
        }
    }

    private static void AddImages(Section section, IReadOnlyList<DocumentImage> images)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        const int columns = 3;
        for (var i = 0; i < columns; i++)
        {
            table.AddColumn(Unit.FromCentimeter(6.2));
        }

        for (var i = 0; i < images.Count; i += columns)
        {
            var row = table.AddRow();
            row.KeepWith = 0;
            for (var c = 0; c < columns && i + c < images.Count; c++)
            {
                var item = images[i + c];
                var cell = row.Cells[c];
                cell.Format.Alignment = ParagraphAlignment.Center;
                try
                {
                    var image = cell.AddImage("base64:" + Convert.ToBase64String(item.Content));
                    image.LockAspectRatio = true;
                    image.Width = Unit.FromCentimeter(5.8);
                    image.Height = Unit.FromCentimeter(4.4);
                }
                catch (Exception)
                {
                    cell.AddParagraph("(imagen no disponible)");
                }

                if (!string.IsNullOrWhiteSpace(item.Caption))
                {
                    var caption = cell.AddParagraph(item.Caption);
                    caption.Format.Font.Size = 6.5;
                    caption.Format.SpaceAfter = Unit.FromPoint(6);
                }
            }
        }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (hex is not { Length: 7 } || hex[0] != '#')
        {
            return fallback;
        }

        try
        {
            return new Color(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>Mixes the color with white (amount 0..1 of white).</summary>
    private static Color Blend(Color color, double white) => new(
        (byte)(color.R + (255 - color.R) * white),
        (byte)(color.G + (255 - color.G) * white),
        (byte)(color.B + (255 - color.B) * white));

    private static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
}
