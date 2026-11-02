#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Services;

public interface ILabManagerReportPdf
{
    /// <summary>AC-08-04 / AC-08-05: builds the PDF for a report that has already been run.</summary>
    byte[] Build(ReportsModel report, DateTime generatedAt);
}

/// <summary>
/// Writes the management report as a PDF without any PDF library (no NuGet package,
/// no licence key, nothing that can clash with the group's packages).
/// It uses the fonts every PDF reader has built in (Helvetica), draws an A4 page with:
/// laboratory name, date range, a table of categories with test counts and share of
/// total, and a total row (AC-08-04). An empty range prints
/// "No tests found for the selected period" (AC-08-02). Long reports continue on more pages.
/// </summary>
public class LabManagerReportPdf : ILabManagerReportPdf
{
    private const float PageWidth = 595.28f;   // A4 in points (1/72 inch)
    private const float PageHeight = 841.89f;
    private const float Margin = 50f;
    private const float RowHeight = 22f;

    // column positions
    private const float ColCategoryX = Margin + 8;
    private const float ColCountRight = 400f;
    private const float ColShareRight = PageWidth - Margin - 8;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public byte[] Build(ReportsModel report, DateTime generatedAt)
    {
        var pages = new List<StringBuilder>();
        StringBuilder page = NewPage(pages, report, generatedAt, out float y);

        if (report.Rows.Count == 0)
        {
            // AC-08-02 / TC-LM-08-02
            y -= 40;
            FillRect(page, Margin, y - 30, PageWidth - 2 * Margin, 60, 0.96f, 0.97f, 0.98f);
            CenterText(page, "F3", 12, y - 4, ReportsModel.NoTestsMessage, 0.35f);
        }
        else
        {
            y = TableHeader(page, y);
            int total = report.TotalTests;
            bool shade = false;

            foreach (var row in report.Rows)
            {
                if (y - RowHeight < Margin + 60)
                {
                    page = NewPage(pages, report, generatedAt, out y);
                    y = TableHeader(page, y);
                }

                if (shade) FillRect(page, Margin, y - RowHeight, PageWidth - 2 * Margin, RowHeight, 0.97f, 0.98f, 0.99f);
                shade = !shade;

                float baseline = y - 15;
                Text(page, "F1", 10, ColCategoryX, baseline, Fit(row.CategoryName, "F1", 10, ColCountRight - ColCategoryX - 30));
                RightText(page, "F1", 10, ColCountRight, baseline, row.TestCount.ToString("N0", Inv));
                RightText(page, "F1", 10, ColShareRight, baseline, row.SharePercent.ToString("0.0", Inv) + "%");
                Line(page, Margin, y - RowHeight, PageWidth - Margin, y - RowHeight, 0.88f, 0.5f);
                y -= RowHeight;
            }

            // Total row (AC-08-04)
            if (y - RowHeight < Margin + 60)
            {
                page = NewPage(pages, report, generatedAt, out y);
                y = TableHeader(page, y);
            }
            FillRect(page, Margin, y - RowHeight, PageWidth - 2 * Margin, RowHeight, 0.90f, 0.93f, 0.97f);
            Line(page, Margin, y, PageWidth - Margin, y, 0.2f, 1.2f);
            float totalBaseline = y - 15;
            Text(page, "F2", 10, ColCategoryX, totalBaseline, "Total tests performed");
            RightText(page, "F2", 10, ColCountRight, totalBaseline, total.ToString("N0", Inv));
            RightText(page, "F2", 10, ColShareRight, totalBaseline, "100%");
        }

        // Footer on every page: "Page 1 of 2"
        for (int i = 0; i < pages.Count; i++)
        {
            Line(pages[i], Margin, Margin + 18, PageWidth - Margin, Margin + 18, 0.85f, 0.5f);
            Text(pages[i], "F1", 8, Margin, Margin + 6, "NMB-HLabSys · Laboratory Manager · Tests Performed by Category", 0.45f);
            RightText(pages[i], "F1", 8, PageWidth - Margin, Margin + 6, $"Page {i + 1} of {pages.Count}", 0.45f);
        }

        return WritePdf(pages, report.LaboratoryName);
    }

    // ------------------------------------------------------------------ layout pieces

    private static StringBuilder NewPage(List<StringBuilder> pages, ReportsModel report, DateTime generatedAt, out float y)
    {
        var page = new StringBuilder();
        pages.Add(page);

        // Letterhead band
        float bandTop = PageHeight - Margin;
        FillRect(page, Margin, bandTop - 70, PageWidth - 2 * Margin, 70, 0.11f, 0.23f, 0.42f);
        Text(page, "F2", 14, Margin + 16, bandTop - 28, Fit(report.LaboratoryName, "F2", 14, PageWidth - 2 * Margin - 32), 1f);
        Text(page, "F1", 11, Margin + 16, bandTop - 50, "Tests Performed by Category", 1f);

        y = bandTop - 70 - 28;
        Text(page, "F2", 10, Margin, y, "Reporting period:");
        Text(page, "F1", 10, Margin + 95, y, report.PeriodText);
        y -= 15;
        Text(page, "F2", 10, Margin, y, "Generated:");
        Text(page, "F1", 10, Margin + 95, y, generatedAt.ToString("d MMMM yyyy, HH:mm", Inv));
        y -= 15;
        Text(page, "F2", 10, Margin, y, "Counted:");
        Text(page, "F1", 10, Margin + 95, y, "Completed tests only");
        y -= 25;
        return page;
    }

    private static float TableHeader(StringBuilder page, float y)
    {
        FillRect(page, Margin, y - RowHeight, PageWidth - 2 * Margin, RowHeight, 0.93f, 0.94f, 0.96f);
        float baseline = y - 15;
        Text(page, "F2", 10, ColCategoryX, baseline, "Test Category");
        RightText(page, "F2", 10, ColCountRight, baseline, "Tests Completed");
        RightText(page, "F2", 10, ColShareRight, baseline, "Share of Total");
        Line(page, Margin, y - RowHeight, PageWidth - Margin, y - RowHeight, 0.6f, 0.8f);
        return y - RowHeight;
    }

    // ------------------------------------------------------------------ drawing commands

    private static void Text(StringBuilder s, string font, float size, float x, float y, string text, float gray = 0.1f)
    {
        // white text on the letterhead uses gray = 1
        s.Append(F(gray)).Append(' ').Append(F(gray)).Append(' ').Append(F(gray)).Append(" rg\n");
        s.Append("BT /").Append(font).Append(' ').Append(F(size)).Append(" Tf ")
         .Append(F(x)).Append(' ').Append(F(y)).Append(" Td (").Append(Escape(text)).Append(") Tj ET\n");
    }

    private static void RightText(StringBuilder s, string font, float size, float right, float y, string text, float gray = 0.1f) =>
        Text(s, font, size, right - Width(text, font, size), y, text, gray);

    private static void CenterText(StringBuilder s, string font, float size, float y, string text, float gray = 0.1f) =>
        Text(s, font, size, (PageWidth - Width(text, font, size)) / 2, y, text, gray);

    private static void FillRect(StringBuilder s, float x, float y, float w, float h, float r, float g, float b) =>
        s.Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" rg ")
         .Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re f\n");

    private static void Line(StringBuilder s, float x1, float y1, float x2, float y2, float gray, float width) =>
        s.Append(F(gray)).Append(' ').Append(F(gray)).Append(' ').Append(F(gray)).Append(" RG ")
         .Append(F(width)).Append(" w ")
         .Append(F(x1)).Append(' ').Append(F(y1)).Append(" m ").Append(F(x2)).Append(' ').Append(F(y2)).Append(" l S\n");

    private static string F(float v) => v.ToString("0.##", Inv);

    /// <summary>Shortens text with "..." so a very long category name can't run into the next column.</summary>
    private static string Fit(string text, string font, float size, float maxWidth)
    {
        if (Width(text, font, size) <= maxWidth) return text;
        while (text.Length > 1 && Width(text + "...", font, size) > maxWidth)
            text = text[..^1];
        return text.TrimEnd() + "...";
    }

    // ------------------------------------------------------------------ fonts and text encoding

    // Standard Helvetica / Helvetica-Bold character widths (1/1000 em) for characters 32..126.
    private static readonly short[] HelveticaWidths =
    {
        278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
        556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
        1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
        333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
        556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
    };

    private static readonly short[] HelveticaBoldWidths =
    {
        278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,
        556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
        975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
        333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,
        611,611,389,556,333,611,556,778,556,556,500,389,280,389,584
    };

    private static float Width(string text, string font, float size)
    {
        var widths = font == "F2" ? HelveticaBoldWidths : HelveticaWidths;
        float units = 0;
        foreach (char c in text)
            units += c >= 32 && c <= 126 ? widths[c - 32] : 556;
        return units * size / 1000f;
    }

    /// <summary>Text is written in WinAnsi (Windows-1252), the encoding the built-in fonts use.</summary>
    private static byte ToWinAnsi(char c) => c switch
    {
        '–' => 0x96,   // en dash  –
        '—' => 0x97,   // em dash  —
        '•' => 0x95,   // bullet   •
        '’' => 0x92,   // right quote ’
        '‘' => 0x91,   // left quote  ‘
        _ when c < 256 => (byte)c,
        _ => (byte)'?'
    };

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char ch in text)
        {
            byte b = ToWinAnsi(ch);
            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ PDF file structure

    private static byte[] WritePdf(List<StringBuilder> pages, string title)
    {
        // Object numbers: 1 catalog, 2 pages, 3-5 fonts, 6 info, then (page, content) pairs
        int firstPageObj = 7;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" +
                string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{firstPageObj + i * 2} 0 R")) +
                $"] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>",
            $"<< /Title ({Escape(title + " - Tests Performed by Category")}) /Producer (NMB-HLabSys) /Creator (NMB-HLabSys Lab Manager) >>"
        };

        foreach (var content in pages)
        {
            int contentObj = firstPageObj + objects.Count - 6 + 1;   // the object right after this page
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                        $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentObj} 0 R >>");
            string stream = content.ToString();
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        }

        // Every character in these strings is 0-255, so Latin-1 writes each as exactly one byte
        var latin1 = Encoding.Latin1;
        using var ms = new MemoryStream();
        void Write(string s) { var bytes = latin1.GetBytes(s); ms.Write(bytes, 0, bytes.Length); }

        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        long xrefStart = ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        xref.Append("0000000000 65535 f \n");
        foreach (long offset in offsets) xref.Append(offset.ToString("D10", Inv)).Append(" 00000 n \n");
        xref.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R /Info 6 0 R >>\n");
        xref.Append("startxref\n").Append(xrefStart).Append("\n%%EOF\n");
        Write(xref.ToString());

        return ms.ToArray();
    }
}
