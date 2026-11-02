
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

    // Build the PDF report using the selected date range and turnaround-time data
    public byte[] Build(
        string laboratoryName,
        DateTime startDate,
        DateTime endDate,
        DateTime generatedAt,
        IReadOnlyList<LabTechReportPdfRow> rows)
    {
        var content = new StringBuilder();

        // Add positioned text to the PDF content using PDF text commands
        void Text(double x, double y, double size, string value)
        {
            content.AppendLine(
                $"BT /F1 {size.ToString(CultureInfo.InvariantCulture)} Tf " +
                $"{x.ToString(CultureInfo.InvariantCulture)} " +
                $"{y.ToString(CultureInfo.InvariantCulture)} Td " +
                $"({Escape(value)}) Tj ET");
        }

        // Draw the report header background and add the laboratory and report details
        content.AppendLine("0.08 0.20 0.38 rg");
        content.AppendLine($"0 {PageHeight - 115} {PageWidth} 115 re f");
        content.AppendLine("0 0 0 rg");

        Text(Margin, PageHeight - 48, 19, laboratoryName);
        Text(Margin, PageHeight - 76, 13, "TAT Performance Report");
        Text(Margin, PageHeight - 145, 10,
            $"Period: {startDate:dd MMM yyyy} - {endDate:dd MMM yyyy}");
        Text(Margin, PageHeight - 163, 9,
            $"Generated: {generatedAt:dd MMM yyyy HH:mm}");

        // Set the starting position for the turnaround-time performance table
        double y = PageHeight - 205;

        Text(Margin, y, 9, "Test Type");
        Text(255, y, 9, "Target");
        Text(330, y, 9, "Average");
        Text(415, y, 9, "Compliance");
        Text(510, y, 9, "Breaches");

        y -= 10;
        content.AppendLine(
            $"{Margin} {y} m {PageWidth - Margin} {y} l S");
        y -= 22;

        // Add each test type's performance figures while space remains on the page
        foreach (var row in rows)
        {
            if (y < Margin + 30)
            {
                // Keep this initial implementation to one page.
                // Pagination can be added once the report output is verified.
                break;
            }

            Text(Margin, y, 8, Fit(row.TestName, 27));
            Text(255, y, 8, $"{row.TargetMinutes} min");
            Text(330, y, 8,
                row.AverageMinutes.HasValue
                    ? $"{row.AverageMinutes.Value:0.0} min"
                    : "N/A");
            Text(415, y, 8, $"{row.CompliancePercent:0.0}%");
            Text(510, y, 8, row.BreachCount.ToString());

            y -= 23;
        }

        Text(Margin, Margin, 8,
            "NMB-HLabSys | Laboratory Technician | TAT Performance");

        return WritePdf(content.ToString(),
            $"{laboratoryName} - TAT Performance Report");
    }

    private static string Fit(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unspecified"
            : value.Length <= maxLength
                ? value
                : value[..(maxLength - 3)] + "...";

    // Escape special characters and replace unsupported characters before writing PDF text
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

    // Assemble the PDF objects, write the document structure and return the PDF bytes
    private static byte[] WritePdf(string stream, string title)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R " +
            "/MediaBox [0 0 595.28 841.89] " +
            "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(stream)} >>\n" +
            $"stream\n{stream}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
            "/Encoding /WinAnsiEncoding >>",
            $"<< /Title ({Escape(title)}) /Producer (NMB-HLabSys) >>"
        };

        using var output = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var offsets = new List<long>();

        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        long xrefPosition = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n");
        Write("0000000000 65535 f \n");

        foreach (long offset in offsets)
            Write($"{offset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {objects.Count + 1} " +
              "/Root 1 0 R /Info 6 0 R >>\n");
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
