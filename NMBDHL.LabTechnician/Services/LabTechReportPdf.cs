using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NMBHDL.LabTechnician.Services;

public sealed class LabTechReportPdf
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double Margin = 42;
    private const double RowSpacing = 23;

    // Build the report while retaining its existing layout and adding exporter details and pagination.
    public byte[] Build(
        string laboratoryName,
        DateTime startDate,
        DateTime endDate,
        DateTime generatedAt,
        string exportedByName,
        int labTechnicianId,
        IReadOnlyList<LabTechReportPdfRow> rows)
    {
        var reportRows = rows ?? Array.Empty<LabTechReportPdfRow>();
        const int firstPageRows = 24;
        const int continuationPageRows = 26;

        var pages = new List<List<LabTechReportPdfRow>>();
        var offset = 0;

        var firstCount = Math.Min(firstPageRows, reportRows.Count);
        pages.Add(reportRows.Skip(offset).Take(firstCount).ToList());
        offset += firstCount;

        while (offset < reportRows.Count)
        {
            var count = Math.Min(continuationPageRows, reportRows.Count - offset);
            pages.Add(reportRows.Skip(offset).Take(count).ToList());
            offset += count;
        }

        var streams = new List<string>();
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var content = new StringBuilder();

            void Text(double x, double y, double size, string value)
            {
                content.AppendLine(
                    $"BT /F1 {size.ToString(CultureInfo.InvariantCulture)} Tf " +
                    $"{x.ToString(CultureInfo.InvariantCulture)} " +
                    $"{y.ToString(CultureInfo.InvariantCulture)} Td " +
                    $"({Escape(value)}) Tj ET");
            }

            // Preserve the existing dark-blue report banner on the first page.
            if (pageIndex == 0)
            {
                content.AppendLine("0.08 0.20 0.38 rg");
                content.AppendLine($"0 {PageHeight - 115} {PageWidth} 115 re f");
                content.AppendLine("0 0 0 rg");

                Text(Margin, PageHeight - 48, 19, laboratoryName);
                Text(Margin, PageHeight - 76, 13, "TAT Performance Report");
                Text(Margin, PageHeight - 145, 10,
                    $"Period: {startDate:dd MMM yyyy} - {endDate:dd MMM yyyy}");
                Text(Margin, PageHeight - 163, 9,
                    $"Generated: {generatedAt:dd MMM yyyy HH:mm}");
                Text(Margin, PageHeight - 181, 9,
                    $"Exported by: {Fit(exportedByName, 48)} | Technician ID: {labTechnicianId}");
            }
            else
            {
                Text(Margin, PageHeight - 48, 16, laboratoryName);
                Text(Margin, PageHeight - 72, 12, "TAT Performance Report (continued)");
                Text(Margin, PageHeight - 96, 9,
                    $"Period: {startDate:dd MMM yyyy} - {endDate:dd MMM yyyy}");
                Text(Margin, PageHeight - 114, 9,
                    $"Exported by: {Fit(exportedByName, 48)} | Technician ID: {labTechnicianId}");
            }

            var y = pageIndex == 0 ? PageHeight - 215 : PageHeight - 155;

            Text(Margin, y, 9, "Test Type");
            Text(255, y, 9, "Target");
            Text(330, y, 9, "Average");
            Text(415, y, 9, "Compliance");
            Text(510, y, 9, "Breaches");

            y -= 10;
            content.AppendLine($"{Margin} {y} m {PageWidth - Margin} {y} l S");
            y -= 22;

            foreach (var row in pages[pageIndex])
            {
                Text(Margin, y, 8, Fit(row.TestName, 27));
                Text(255, y, 8, $"{row.TargetMinutes} min");
                Text(330, y, 8,
                    row.AverageMinutes.HasValue
                        ? $"{row.AverageMinutes.Value:0.0} min"
                        : "N/A");
                Text(415, y, 8, $"{row.CompliancePercent:0.0}%");
                Text(510, y, 8, row.BreachCount.ToString(CultureInfo.InvariantCulture));
                y -= RowSpacing;
            }

            Text(Margin, Margin, 8,
                "NMB-HLabSys | Laboratory Technician | TAT Performance");
            Text(PageWidth - Margin - 45, Margin, 8,
                $"Page {pageIndex + 1} of {pages.Count}");

            streams.Add(content.ToString());
        }

        return WritePdf(streams,
            $"{laboratoryName} - TAT Performance Report");
    }

    private static string Fit(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unspecified"
            : value.Length <= maxLength
                ? value
                : value[..(maxLength - 3)] + "...";

    // Escape special characters and replace unsupported characters before writing PDF text.
    private static string Escape(string value)
    {
        var result = new StringBuilder();

        foreach (char c in value ?? string.Empty)
        {
            char safe = c switch
            {
                '(' or ')' or '\\' => '\\',
                '\r' or '\n' => ' ',
                _ => '\0'
            };

            if (safe != '\0')
                result.Append(safe);

            if (c is not '\r' and not '\n')
                result.Append(c <= 255 ? c : '?');
        }

        return result.ToString();
    }

    // Assemble a valid multi-page PDF with a shared Helvetica font resource.
    private static byte[] WritePdf(IReadOnlyList<string> streams, string title)
    {
        var objects = new List<string>();
        var pageCount = streams.Count;
        var firstPageObject = 3;
        var firstContentObject = firstPageObject + pageCount;
        var fontObject = firstContentObject + pageCount;
        var infoObject = fontObject + 1;

        objects.Add($"<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(" ",
            Enumerable.Range(firstPageObject, pageCount)
                .Select(id => $"{id} 0 R"));
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");

        for (var i = 0; i < pageCount; i++)
        {
            var contentId = firstContentObject + i;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R " +
                "/MediaBox [0 0 595.28 841.89] " +
                $"/Resources << /Font << /F1 {fontObject} 0 R >> >> " +
                $"/Contents {contentId} 0 R >>");
        }

        foreach (var stream in streams)
        {
            objects.Add(
                $"<< /Length {Encoding.Latin1.GetByteCount(stream)} >>\n" +
                $"stream\n{stream}endstream");
        }

        objects.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
            "/Encoding /WinAnsiEncoding >>");
        objects.Add($"<< /Title ({Escape(title)}) /Producer (NMB-HLabSys) >>");

        using var output = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var offsets = new List<long>();

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefPosition = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n");
        Write("0000000000 65535 f \n");

        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {objects.Count + 1} " +
              $"/Root 1 0 R /Info {infoObject} 0 R >>\n");
        Write($"startxref\n{xrefPosition}\n%%EOF\n");

        return output.ToArray();
    }
}

public sealed class LabTechReportPdfRow
{
    public string TestName { get; set; } = string.Empty;
    public int TargetMinutes { get; set; }
    public double? AverageMinutes { get; set; }
    public double CompliancePercent { get; set; }
    public int BreachCount { get; set; }
}
