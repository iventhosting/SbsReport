using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using SbsReport.Models;

namespace SbsReport.Services;

/// <summary>
/// Çoklu format dışa aktarım motoru.
/// Harici ücretli bağımlılık olmadan saf C# ile PDF, Excel, Word, HTML, CSV, JSON, XML ve SVG üretir.
/// </summary>
public class ReportExportService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public byte[] CreateSnapshot(RenderedReport rendered)
        => SbsSnapshotCodec.Encode(rendered);

    public RenderedReport ReadSnapshot(byte[] snapshotBytes)
    {
        if (SbsSnapshotCodec.IsBinarySnapshot(snapshotBytes))
            return SbsSnapshotCodec.Decode(snapshotBytes);

        using var ms = new MemoryStream(snapshotBytes);
        // GZip magic bytes (0x1f 0x8b) kontrolü; eski JSON snapshot'lar da desteklenir
        if (snapshotBytes.Length >= 2 && snapshotBytes[0] == 0x1f && snapshotBytes[1] == 0x8b)
        {
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<RenderedReport>(gz, SnapshotJsonOptions) ?? new RenderedReport();
        }
        return JsonSerializer.Deserialize<RenderedReport>(ms, SnapshotJsonOptions) ?? new RenderedReport();
    }

    public (byte[] Bytes, string ContentType, string Extension) Export(RenderedReport rendered, string format)
    {
        return (format ?? "pdf").Trim().ToLowerInvariant() switch
        {
            "pdf" => (ExportPdf(rendered), "application/pdf", ".pdf"),
            "xlsx" or "xls" or "excel" => (ExportExcelXml(rendered), "application/vnd.ms-excel", ".xls"),
            "docx" or "doc" or "word" => (ExportWordHtml(rendered), "application/msword", ".doc"),
            "html" or "htm" => (ExportHtml(rendered), "text/html; charset=utf-8", ".html"),
            "csv" => (ExportCsv(rendered), "text/csv; charset=utf-8", ".csv"),
            "xml" => (ExportXml(rendered), "application/xml; charset=utf-8", ".xml"),
            "json" => (ExportJson(rendered), "application/json; charset=utf-8", ".json"),
            "svg" => (ExportSvg(rendered), "image/svg+xml; charset=utf-8", ".svg"),
            "rtf" => (ExportRtf(rendered), "application/rtf", ".rtf"),
            "txt" => (ExportTxt(rendered), "text/plain; charset=utf-8", ".txt"),
            "snapshot" or "sbsdoc" => (CreateSnapshot(rendered), "application/octet-stream", ".sbsdoc"),
            _ => (ExportPdf(rendered), "application/pdf", ".pdf")
        };
    }

    public byte[] ExportFromSnapshot(byte[] snapshotBytes, string format)
    {
        var rendered = ReadSnapshot(snapshotBytes);
        return Export(rendered, format).Bytes;
    }

    // =========================================================================
    // 1. Kendi Saf C# Streaming PDF 1.4 Motorumuz (SbsPdfWriter — Sıfır Bağımlılık, Düşük RAM)
    // =========================================================================
    public byte[] ExportPdf(RenderedReport r) => SbsPdfWriter.Generate(r);

    // =========================================================================
    // 2. HTML5 Standalone Document (Tam görsel, Türkçe UTF-8, Yazdırmaya Hazır)
    // =========================================================================
    public byte[] ExportHtml(RenderedReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(r.ReportName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"@page {{ size: {r.PageWidthMm.ToString(CultureInfo.InvariantCulture)}mm {r.PageHeightMm.ToString(CultureInfo.InvariantCulture)}mm; margin: 0; }}");
        sb.AppendLine("body{margin:0;padding:16px;background:#e2e8f0;font-family:'Segoe UI',Inter,Arial,sans-serif;display:flex;flex-direction:column;align-items:center;gap:16px;}");
        sb.AppendLine(".sbs-page{position:relative;background:#fff;box-shadow:0 4px 20px rgba(0,0,0,.15);overflow:hidden;page-break-after:always;}");
        sb.AppendLine(".sbs-el{position:absolute;box-sizing:border-box;display:flex;padding:1px 3px;overflow:hidden;white-space:pre-wrap;word-break:break-word;}");
        sb.AppendLine(".sbs-wm{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;pointer-events:none;font-weight:800;text-transform:uppercase;}");
        sb.AppendLine("@media print{body{background:#fff;padding:0;gap:0;}.sbs-page{box-shadow:none;}}");
        sb.AppendLine("</style></head><body>");

        foreach (var p in r.Pages)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<div class=\"sbs-page\" style=\"width:{0}mm;height:{1}mm;\">\n", r.PageWidthMm, r.PageHeightMm);

            if (r.Watermark?.Enabled == true && !string.IsNullOrWhiteSpace(r.Watermark.Text))
            {
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<div class=\"sbs-wm\" style=\"color:{0};opacity:{1};font-size:{2}pt;transform:rotate({3}deg);\">{4}</div>\n",
                    r.Watermark.Color, r.Watermark.Opacity, r.Watermark.FontSizePt, r.Watermark.Angle, WebUtility.HtmlEncode(r.Watermark.Text));
            }

            foreach (var el in p.Elements)
            {
                var jc = el.HAlign == "center" ? "center" : el.HAlign == "right" ? "flex-end" : "flex-start";
                var ai = el.VAlign == "top" ? "flex-start" : el.VAlign == "bottom" ? "flex-end" : "center";
                var border = el.BorderWidthPx > 0 && el.BorderSides != "None"
                    ? $"border:{el.BorderWidthPx.ToString(CultureInfo.InvariantCulture)}px {(el.BorderStyle ?? "solid").ToLowerInvariant()} {el.BorderColor};"
                    : "";
                if (el.Type == "Line")
                {
                    border = el.LineOrientation == "Vertical"
                        ? $"border-left:{Math.Max(1, el.BorderWidthPx).ToString(CultureInfo.InvariantCulture)}px solid {el.Color};"
                        : $"border-bottom:{Math.Max(1, el.BorderWidthPx).ToString(CultureInfo.InvariantCulture)}px solid {el.Color};";
                }
                var style = string.Format(CultureInfo.InvariantCulture,
                    "left:{0}mm;top:{1}mm;width:{2}mm;height:{3}mm;background:{4};color:{5};font-family:{6};font-size:{7}pt;font-weight:{8};font-style:{9};justify-content:{10};align-items:{11};{12}",
                    el.XMm, el.YMm, el.WidthMm, el.HeightMm,
                    el.BackgroundColor ?? "transparent", el.Color ?? "#111827",
                    el.FontFamily ?? "Segoe UI,sans-serif", el.FontSizePt,
                    el.Bold ? "700" : "400", el.Italic ? "italic" : "normal",
                    jc, ai, border);

                string inner = el.Type switch
                {
                    "Image" when !string.IsNullOrWhiteSpace(el.ImageUrl) => $"<img src=\"{WebUtility.HtmlEncode(el.ImageUrl)}\" style=\"max-width:100%;max-height:100%;object-fit:contain;\"/>",
                    "CheckBox" => $"{(el.Checked ? "☑" : "☐")} {WebUtility.HtmlEncode(el.Text ?? el.Label ?? "")}",
                    "Radio" => $"{(el.Checked ? "⦿" : "○")} {WebUtility.HtmlEncode(el.Text ?? el.Label ?? "")}",
                    "Shape" or "Line" => "",
                    _ => WebUtility.HtmlEncode(el.Text ?? "")
                };
                sb.AppendLine($"<div class=\"sbs-el\" style=\"{style}\">{inner}</div>");
            }
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // =========================================================================
    // 3. Microsoft Excel (.xls SpreadsheetML — Türkçe UTF-8 tam destekli)
    // =========================================================================
    public byte[] ExportExcelXml(RenderedReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine("<Styles>");
        sb.AppendLine("<Style ss:ID=\"Default\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\"/></Style>");
        sb.AppendLine("<Style ss:ID=\"Bold\"><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\"/></Style>");
        sb.AppendLine("</Styles>");

        foreach (var p in r.Pages)
        {
            sb.AppendLine($"<Worksheet ss:Name=\"Sayfa_{p.PageNumber}\"><Table>");
            var rows = p.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .GroupBy(e => Math.Round(e.YMm / 4.0))
                .OrderBy(g => g.Key);

            foreach (var row in rows)
            {
                sb.Append("<Row>");
                foreach (var cell in row.OrderBy(e => e.XMm))
                {
                    var styleId = cell.Bold ? "Bold" : "Default";
                    sb.Append($"<Cell ss:StyleID=\"{styleId}\"><Data ss:Type=\"String\">{WebUtility.HtmlEncode(cell.Text)}</Data></Cell>");
                }
                sb.AppendLine("</Row>");
            }
            sb.AppendLine("</Table></Worksheet>");
        }
        sb.AppendLine("</Workbook>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // =========================================================================
    // 4. Microsoft Word (.doc — Word-compatible HTML/Office XML, Türkçe UTF-8)
    // =========================================================================
    public byte[] ExportWordHtml(RenderedReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" xmlns=\"http://www.w3.org/TR/REC-html40\">");
        sb.AppendLine("<head><meta charset=\"utf-8\"/><title>" + WebUtility.HtmlEncode(r.ReportName) + "</title>");
        sb.AppendLine("<style>body{font-family:'Segoe UI',Arial,sans-serif;font-size:10pt;} table{width:100%;border-collapse:collapse;margin-bottom:12px;} td{padding:4px 6px;border:1px solid #e2e8f0;}</style></head><body>");

        foreach (var p in r.Pages)
        {
            var rows = p.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .GroupBy(e => Math.Round(e.YMm / 4.5))
                .OrderBy(g => g.Key);

            sb.AppendLine("<table>");
            foreach (var row in rows)
            {
                sb.Append("<tr>");
                foreach (var cell in row.OrderBy(e => e.XMm))
                {
                    var bg = !string.IsNullOrWhiteSpace(cell.BackgroundColor) && cell.BackgroundColor != "transparent"
                        ? $"background:{cell.BackgroundColor};" : "";
                    var fw = cell.Bold ? "font-weight:bold;" : "";
                    sb.Append($"<td style=\"{bg}{fw}color:{cell.Color};text-align:{cell.HAlign};\">{WebUtility.HtmlEncode(cell.Text)}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
            if (p.PageNumber < r.Pages.Count)
                sb.AppendLine("<br clear=\"all\" style=\"page-break-before:always\" />");
        }
        sb.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // =========================================================================
    // 5. CSV (UTF-8 BOM — Türkçe Excel doğrudan bozulmadan açar)
    // =========================================================================
    public byte[] ExportCsv(RenderedReport r)
    {
        var sb = new StringBuilder();
        foreach (var p in r.Pages)
        {
            var rows = p.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .GroupBy(e => Math.Round(e.YMm / 4.0))
                .OrderBy(g => g.Key);

            foreach (var row in rows)
            {
                var cols = row.OrderBy(e => e.XMm)
                    .Select(e => "\"" + (e.Text ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"");
                sb.AppendLine(string.Join(";", cols));
            }
        }
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(body, 0, result, bom.Length, body.Length);
        return result;
    }

    // =========================================================================
    // 6. XML & JSON & SVG
    // =========================================================================
    public byte[] ExportXml(RenderedReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<Report Name=\"{WebUtility.HtmlEncode(r.ReportName)}\" Pages=\"{r.Pages.Count}\">");
        foreach (var p in r.Pages)
        {
            sb.AppendLine($"  <Page Number=\"{p.PageNumber}\">");
            foreach (var el in p.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Text)))
            {
                sb.AppendLine($"    <Item Type=\"{el.Type}\" X=\"{el.XMm.ToString(CultureInfo.InvariantCulture)}\" Y=\"{el.YMm.ToString(CultureInfo.InvariantCulture)}\">{WebUtility.HtmlEncode(el.Text)}</Item>");
            }
            sb.AppendLine("  </Page>");
        }
        sb.AppendLine("</Report>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] ExportJson(RenderedReport r)
    {
        return JsonSerializer.SerializeToUtf8Bytes(r, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    public byte[] ExportSvg(RenderedReport r)
    {
        var page = r.Pages.FirstOrDefault() ?? new RenderedPage { PageNumber = 1 };
        const double scale = 3.779527559; // mm to px
        double w = r.PageWidthMm * scale;
        double h = r.PageHeightMm * scale;
        var sb = new StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0:F1}\" height=\"{1:F1}\" viewBox=\"0 0 {0:F1} {1:F1}\">\n", w, h);
        sb.AppendFormat(CultureInfo.InvariantCulture, "<rect x=\"0\" y=\"0\" width=\"{0:F1}\" height=\"{1:F1}\" fill=\"#ffffff\"/>\n", w, h);

        foreach (var el in page.Elements)
        {
            double x = el.XMm * scale;
            double y = el.YMm * scale;
            double ew = el.WidthMm * scale;
            double eh = el.HeightMm * scale;
            if (!string.IsNullOrWhiteSpace(el.BackgroundColor) && el.BackgroundColor != "transparent")
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "<rect x=\"{0:F1}\" y=\"{1:F1}\" width=\"{2:F1}\" height=\"{3:F1}\" fill=\"{4}\"/>\n",
                    x, y, ew, eh, WebUtility.HtmlEncode(el.BackgroundColor));
            }
            if (el.BorderWidthPx > 0 && el.BorderSides != "None")
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "<rect x=\"{0:F1}\" y=\"{1:F1}\" width=\"{2:F1}\" height=\"{3:F1}\" fill=\"none\" stroke=\"{4}\" stroke-width=\"{5:F1}\"/>\n",
                    x, y, ew, eh, WebUtility.HtmlEncode(el.BorderColor ?? "#cbd5e1"), el.BorderWidthPx);
            }
            if (!string.IsNullOrWhiteSpace(el.Text) && el.Type != "Shape")
            {
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<text x=\"{0:F1}\" y=\"{1:F1}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"{2:F1}pt\" font-weight=\"{3}\" fill=\"{4}\">{5}</text>\n",
                    x + 4, y + (eh / 2) + (el.FontSizePt * 0.45), el.FontSizePt, el.Bold ? "700" : "400",
                    WebUtility.HtmlEncode(el.Color ?? "#111827"), WebUtility.HtmlEncode(el.Text));
            }
        }
        sb.AppendLine("</svg>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] ExportTxt(RenderedReport r)
    {
        var sb = new StringBuilder();
        foreach (var p in r.Pages)
        {
            if (r.Pages.Count > 1) sb.AppendLine($"=== Sayfa {p.PageNumber} ===");
            var rows = p.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .GroupBy(e => Math.Round(e.YMm / 4.0))
                .OrderBy(g => g.Key);
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join("\t", row.OrderBy(e => e.XMm).Select(e => e.Text.Replace("\r", " ").Replace("\n", " "))));
            }
            sb.AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] ExportRtf(RenderedReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"{\rtf1\ansi\ansicpg1254\deff0{\fonttbl{\f0 Segoe UI;}}\fs20");
        foreach (var p in r.Pages)
        {
            var rows = p.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                .GroupBy(e => Math.Round(e.YMm / 4.5))
                .OrderBy(g => g.Key);
            foreach (var row in rows)
            {
                var line = string.Join("   |   ", row.OrderBy(e => e.XMm).Select(e => RtfEscape(e.Text)));
                sb.AppendLine(line + @"\par");
            }
            if (p.PageNumber < r.Pages.Count) sb.AppendLine(@"\page");
        }
        sb.AppendLine("}");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string RtfEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (ch == '\\' || ch == '{' || ch == '}') sb.Append('\\').Append(ch);
            else if (ch > 127) sb.Append(@"\u").Append((short)ch).Append('?');
            else if (ch == '\n') sb.Append(@"\line ");
            else if (ch != '\r') sb.Append(ch);
        }
        return sb.ToString();
    }

    // ---------- Helpers ----------
    private static (double R, double G, double B) ParseHexColor(string? hex, double dr, double dg, double db)
    {
        if (string.IsNullOrWhiteSpace(hex)) return (dr, dg, db);
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length >= 6 &&
            int.TryParse(hex[..2], NumberStyles.HexNumber, null, out int r) &&
            int.TryParse(hex[2..4], NumberStyles.HexNumber, null, out int g) &&
            int.TryParse(hex[4..6], NumberStyles.HexNumber, null, out int b))
        {
            return (r / 255.0, g / 255.0, b / 255.0);
        }
        return (dr, dg, db);
    }

    private static string PdfEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    // Type1 Helvetica WinAnsi encoding fallback for Turkish letters so PDF renders cleanly without garbled bytes
    private static string ToPdfLatin(string s) =>
        s.Replace('İ', 'I').Replace('ı', 'i')
         .Replace('Ş', 'S').Replace('ş', 's')
         .Replace('Ğ', 'G').Replace('ğ', 'g')
         .Replace('₺', ' ').Replace("✦", "*");
}
