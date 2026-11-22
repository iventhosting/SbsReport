using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SbsReport.Models;

namespace SbsReport.Services;

public class RenderedReport
{
    public string ReportName { get; set; } = string.Empty;
    public double PageWidthMm { get; set; }
    public double PageHeightMm { get; set; }
    public double RenderTimeMs { get; set; }
    public SbsWatermark? Watermark { get; set; }
    public List<RenderedPage> Pages { get; set; } = new();
}

public class RenderedPage
{
    public int PageNumber { get; set; }
    public List<RenderedElement> Elements { get; set; } = new();
}

public class RenderedElement
{
    public string Type { get; set; } = "Text"; // Text | Input | CheckBox | Radio | DatePicker | Image | Shape | Line | Barcode | Chart | Table
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Inter, Arial, sans-serif";
    public double FontSizePt { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Color { get; set; } = "#111827";
    public string BackgroundColor { get; set; } = "transparent";
    public string BorderColor { get; set; } = "#cbd5e1";
    public double BorderWidthPx { get; set; }
    public string BorderStyle { get; set; } = "Solid";
    public string BorderSides { get; set; } = "All";
    public double CornerRadiusPx { get; set; }
    public string HAlign { get; set; } = "left";
    public string VAlign { get; set; } = "middle";
    public bool WordWrap { get; set; } = true;
    public double Angle { get; set; }
    public double PaddingLeftMm { get; set; }
    public double PaddingTopMm { get; set; }
    public double PaddingRightMm { get; set; }
    public double PaddingBottomMm { get; set; }
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public bool Checked { get; set; }
    public string? GroupName { get; set; }
    public string? Items { get; set; }
    public string? SignatureData { get; set; }
    public string? ButtonAction { get; set; }
    public bool ReadOnly { get; set; }
    public bool Required { get; set; }
    public string? ImageUrl { get; set; }
    public string? ShapeType { get; set; }
    public string? LineOrientation { get; set; }
    public string BarcodeFormat { get; set; } = "Code128";
    public string? ChartType { get; set; }
    public List<ChartPoint>? ChartData { get; set; }
}

public class ChartPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public partial class ReportRenderEngine
{
    private readonly DemoDataService _dataService;
    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex ExprRegex();

    [GeneratedRegex(@"^(Sum|Avg|Min|Max|Count)\s*\(([^)]*)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex AggRegex();

    public ReportRenderEngine(DemoDataService dataService)
    {
        _dataService = dataService;
    }

    /// <summary>
    /// Sobis.SobisPersonel ve harici C# uygulamaları için doğrudan DataSet + Parametre ile sıfır-JSON yüksek hızlı render metodu.
    /// Türkçe karakter ve büyük/küçük harf duyarsız tablo/kolon/parametre eşleştirmesi yapar.
    /// </summary>
    public RenderedReport Render(
        SbsReportDocument report,
        DataSet? dataSet,
        Dictionary<string, object>? parameters,
        SbsReportDocument? masterReport = null)
    {
        if (parameters != null && parameters.Count > 0)
        {
            report.Variables ??= new Dictionary<string, string>();
            foreach (var kv in parameters)
            {
                report.Variables[kv.Key] = kv.Value?.ToString() ?? string.Empty;
            }
        }

        Dictionary<string, List<Dictionary<string, object?>>>? directTables = null;
        if (dataSet != null && dataSet.Tables.Count > 0)
        {
            directTables = new Dictionary<string, List<Dictionary<string, object?>>>(dataSet.Tables.Count, StringComparer.OrdinalIgnoreCase);
            foreach (DataTable dt in dataSet.Tables)
            {
                var tableName = string.IsNullOrWhiteSpace(dt.TableName) ? "Data" : dt.TableName;
                int colCount = dt.Columns.Count;
                var colNames = new string[colCount];
                for (int c = 0; c < colCount; c++) colNames[c] = dt.Columns[c].ColumnName;

                var rowList = new List<Dictionary<string, object?>>(dt.Rows.Count);
                foreach (DataRow dr in dt.Rows)
                {
                    var dict = new Dictionary<string, object?>(colCount, StringComparer.OrdinalIgnoreCase);
                    var items = dr.ItemArray;
                    for (int c = 0; c < colCount; c++)
                    {
                        var val = items[c];
                        dict[colNames[c]] = val == DBNull.Value ? null : val;
                    }
                    rowList.Add(dict);
                }
                directTables[tableName] = rowList;
            }
        }

        return RenderCore(report, null, directTables, masterReport);
    }

    public RenderedReport Render(
        SbsReportDocument report,
        Dictionary<string, List<Dictionary<string, JsonElement>>>? runtimeData = null,
        SbsReportDocument? masterReport = null)
        => RenderCore(report, runtimeData, null, masterReport);

    private RenderedReport RenderCore(
        SbsReportDocument report,
        Dictionary<string, List<Dictionary<string, JsonElement>>>? runtimeData,
        Dictionary<string, List<Dictionary<string, object?>>>? directTables,
        SbsReportDocument? masterReport)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Multi-page support: if report.Pages has multiple pages, render each page section sequentially
        if (report.Pages is { Count: > 1 })
        {
            var combinedPages = new List<RenderedPage>();
            var firstPageSettings = report.Pages[0].Page ?? report.Page ?? new SbsPageSettings();
            foreach (var repPage in report.Pages)
            {
                var subDoc = new SbsReportDocument
                {
                    Version = report.Version,
                    Name = report.Name,
                    Page = repPage.Page ?? report.Page ?? new SbsPageSettings(),
                    DataSources = report.DataSources,
                    Variables = report.Variables,
                    Bands = repPage.Bands ?? new List<SbsBand>()
                };
                var subResult = RenderSingleSection(subDoc, runtimeData, directTables, masterReport, combinedPages.Count);
                combinedPages.AddRange(subResult.Pages);
            }

            var totalCombined = combinedPages.Count;
            foreach (var p in combinedPages)
            {
                foreach (var el in p.Elements)
                {
                    if (el.Text.Contains("__TOTAL_PAGES__", StringComparison.Ordinal))
                        el.Text = el.Text.Replace("__TOTAL_PAGES__", totalCombined.ToString(CultureInfo.InvariantCulture));
                }
            }

            sw.Stop();
            var effWm = (firstPageSettings.Watermark?.Enabled == true)
                ? firstPageSettings.Watermark
                : masterReport?.Page?.Watermark;
            return new RenderedReport
            {
                ReportName = report.Name,
                PageWidthMm = firstPageSettings.WidthMm,
                PageHeightMm = firstPageSettings.HeightMm,
                Watermark = effWm,
                RenderTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                Pages = combinedPages
            };
        }

        var single = RenderSingleSection(report, runtimeData, directTables, masterReport, 0);
        var totalSingle = single.Pages.Count;
        foreach (var p in single.Pages)
        {
            foreach (var el in p.Elements)
            {
                if (el.Text.Contains("__TOTAL_PAGES__", StringComparison.Ordinal))
                    el.Text = el.Text.Replace("__TOTAL_PAGES__", totalSingle.ToString(CultureInfo.InvariantCulture));
            }
        }
        sw.Stop();
        single.RenderTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        return single;
    }

    private List<Dictionary<string, object?>> ResolveRowsFast(
        SbsReportDocument report,
        string? dataSourceName,
        Dictionary<string, List<Dictionary<string, JsonElement>>>? runtimeData,
        Dictionary<string, List<Dictionary<string, object?>>>? directTables)
    {
        if (directTables != null && directTables.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                if (directTables.TryGetValue(dataSourceName, out var exact))
                    return exact;
                var norm = ExpressionEvaluator.NormalizeTurkishKey(dataSourceName);
                foreach (var kv in directTables)
                    if (ExpressionEvaluator.NormalizeTurkishKey(kv.Key) == norm)
                        return kv.Value;
            }
            if (directTables.Count == 1)
                return directTables.Values.First();
        }
        return _dataService.ResolveRows(report, dataSourceName, runtimeData);
    }

    private RenderedReport RenderSingleSection(
        SbsReportDocument report,
        Dictionary<string, List<Dictionary<string, JsonElement>>>? runtimeData,
        Dictionary<string, List<Dictionary<string, object?>>>? directTables,
        SbsReportDocument? masterReport,
        int pageNumberOffset)
    {
        var page = report.Page ?? new SbsPageSettings();
        var contentLeft = page.MarginLeftMm;
        var contentTop = page.MarginTopMm;
        var contentBottom = page.HeightMm - page.MarginBottomMm;

        // If a Master Report is assigned, PageHeader & PageFooter come fixed from Master (respecting InheritMasterHeader/Footer)!
        var masterPageHeaders = (masterReport != null && report.InheritMasterHeader)
            ? masterReport.Bands.Where(b => b.Type == "PageHeader").ToList()
            : new List<SbsBand>();
        var masterPageFooters = (masterReport != null && report.InheritMasterFooter)
            ? masterReport.Bands.Where(b => b.Type == "PageFooter").ToList()
            : new List<SbsBand>();
        var masterOverlays = masterReport?.Bands.Where(b => b.Type == "Overlay").ToList() ?? new();

        var reportTitleBands = report.Bands.Where(b => b.Type == "ReportTitle").ToList();
        var pageHeaderBands = masterPageHeaders.Count > 0
            ? masterPageHeaders
            : report.Bands.Where(b => b.Type == "PageHeader").ToList();
        var headerBands = report.Bands.Where(b => b.Type is "Header" or "ColumnHeader").ToList();
        var groupHeaderBands = report.Bands.Where(b => b.Type == "GroupHeader").ToList();
        var dataBands = report.Bands.Where(b => b.Type is "Data" or "Table" or "HierarchicalData" or "ChildBand").ToList();
        var groupFooterBands = report.Bands.Where(b => b.Type == "GroupFooter").ToList();
        var footerBands = report.Bands.Where(b => b.Type is "Footer" or "ColumnFooter" or "EmptyBand").ToList();
        var pageFooterBands = masterPageFooters.Count > 0
            ? masterPageFooters
            : report.Bands.Where(b => b.Type == "PageFooter").ToList();
        var summaryBands = report.Bands.Where(b => b.Type == "ReportSummary").ToList();
        var overlayBands = masterOverlays.Concat(report.Bands.Where(b => b.Type == "Overlay")).ToList();

        var pageFooterHeight = pageFooterBands.Sum(b => b.HeightMm);
        var maxBodyY = contentBottom - pageFooterHeight;

        var pages = new List<RenderedPage>();
        var currentPage = new RenderedPage { PageNumber = pageNumberOffset + 1 };
        pages.Add(currentPage);

        double cursorY = contentTop;

        void PlacePageHeader(RenderedPage targetPage, ref double y)
        {
            foreach (var band in pageHeaderBands)
            {
                AddBandElements(targetPage, band, contentLeft, y, report, null, null, 0, targetPage.PageNumber);
                y += band.HeightMm;
            }
        }

        void PlacePageFooter(RenderedPage targetPage)
        {
            double fy = contentBottom - pageFooterHeight;
            foreach (var band in pageFooterBands)
            {
                AddBandElements(targetPage, band, contentLeft, fy, report, null, null, 0, targetPage.PageNumber);
                fy += band.HeightMm;
            }
        }

        void NewPage()
        {
            currentPage = new RenderedPage { PageNumber = pageNumberOffset + pages.Count + 1 };
            pages.Add(currentPage);
            cursorY = contentTop;
            PlacePageHeader(currentPage, ref cursorY);
        }

        // Page 1 header & report title
        PlacePageHeader(currentPage, ref cursorY);

        var primaryDsName = dataBands.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.DataSource))?.DataSource
                            ?? report.DataSources.FirstOrDefault()?.Name;
        var primaryRows = ResolveRowsFast(report, primaryDsName, runtimeData, directTables);

        foreach (var band in reportTitleBands)
        {
            if (cursorY + band.HeightMm > maxBodyY) NewPage();
            AddBandElements(currentPage, band, contentLeft, cursorY, report, null, primaryRows, 0, currentPage.PageNumber);
            cursorY += band.HeightMm;
        }

        // Header bands
        foreach (var band in headerBands)
        {
            if (cursorY + band.HeightMm > maxBodyY) NewPage();
            AddBandElements(currentPage, band, contentLeft, cursorY, report, null, primaryRows, 0, currentPage.PageNumber);
            cursorY += band.HeightMm;
        }

        // Data bands (with FilterExpression, SortBy, GroupHeader/GroupFooter, EvenBackgroundColor)
        foreach (var dataBand in dataBands)
        {
            var dsName = !string.IsNullOrWhiteSpace(dataBand.DataSource) ? dataBand.DataSource : primaryDsName;
            var rows = ResolveRowsFast(report, dsName, runtimeData, directTables);

            if (!string.IsNullOrWhiteSpace(dataBand.FilterExpression))
            {
                rows = rows.Where((r, idx) => ExpressionEvaluator.EvaluateCondition(dataBand.FilterExpression, new EvalContext
                {
                    Report = report,
                    Row = r,
                    AllRows = rows,
                    LineNumber = idx + 1,
                    PageNumber = currentPage.PageNumber
                })).ToList();
            }

            if (!string.IsNullOrWhiteSpace(dataBand.SortBy))
            {
                var sortCol = dataBand.SortBy.Contains('.') ? dataBand.SortBy.Split('.').Last() : dataBand.SortBy;
                rows = dataBand.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                    ? rows.OrderByDescending(r => ExpressionEvaluator.TryGetRowValue(r, sortCol, out var v) ? v?.ToString() : string.Empty).ToList()
                    : rows.OrderBy(r => ExpressionEvaluator.TryGetRowValue(r, sortCol, out var v) ? v?.ToString() : string.Empty).ToList();
            }

            if (dataBand.CountData > 0)
            {
                if (rows.Count > 0)
                    rows = rows.Take(dataBand.CountData).ToList();
                else
                    rows = Enumerable.Range(1, dataBand.CountData).Select(_ => new Dictionary<string, object?>()).ToList();
            }

            if (rows.Count == 0)
            {
                if (cursorY + dataBand.HeightMm > maxBodyY) NewPage();
                AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, null, rows, 0, currentPage.PageNumber);
                cursorY += dataBand.HeightMm;
            }
            else if (groupHeaderBands.Count > 0 && !string.IsNullOrWhiteSpace(groupHeaderBands[0].GroupBy))
            {
                var grpField = groupHeaderBands[0].GroupBy!;
                var grpCol = grpField.Contains('.') ? grpField.Split('.').Last() : grpField;
                var rawGroups = rows.GroupBy(r => ExpressionEvaluator.TryGetRowValue(r, grpCol, out var v) ? v?.ToString() ?? "" : "");
                var groups = string.Equals(groupHeaderBands[0].SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                    ? rawGroups.OrderByDescending(g => g.Key, StringComparer.Create(TrCulture, true))
                    : rawGroups.OrderBy(g => g.Key, StringComparer.Create(TrCulture, true));
                int lineNo = 0;

                int grpIdx = 0;
                foreach (var grp in groups)
                {
                    var grpRows = grp.ToList();
                    if (grpIdx > 0 && groupHeaderBands.Any(g => g.NewPageBefore))
                        NewPage();

                    foreach (var gh in groupHeaderBands)
                    {
                        var ghH = MeasureBandHeight(gh, report, grpRows[0], grpRows, 0, currentPage.PageNumber);
                        if (cursorY + ghH > maxBodyY) NewPage();
                        AddBandElements(currentPage, gh, contentLeft, cursorY, report, grpRows[0], grpRows, 0, currentPage.PageNumber, 0, ghH);
                        cursorY += ghH;
                        if (gh.NewPageAfter) NewPage();
                    }

                    foreach (var row in grpRows)
                    {
                        lineNo++;
                        var rowH = MeasureBandHeight(dataBand, report, row, grpRows, lineNo, currentPage.PageNumber);
                        if (cursorY + rowH > maxBodyY) NewPage();
                        AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, row, grpRows, lineNo, currentPage.PageNumber, 0, rowH);
                        cursorY += rowH;
                        if (dataBand.NewPageAfter) NewPage();
                    }

                    foreach (var gf in groupFooterBands)
                    {
                        var gfH = MeasureBandHeight(gf, report, grpRows[0], grpRows, 0, currentPage.PageNumber);
                        if (cursorY + gfH > maxBodyY) NewPage();
                        AddBandElements(currentPage, gf, contentLeft, cursorY, report, grpRows[0], grpRows, 0, currentPage.PageNumber, 0, gfH);
                        cursorY += gfH;
                        if (gf.NewPageAfter) NewPage();
                    }
                    grpIdx++;
                }
            }
            else if (dataBand.Type == "HierarchicalData" &&
                     !string.IsNullOrWhiteSpace(dataBand.KeyDataColumn) &&
                     !string.IsNullOrWhiteSpace(dataBand.MasterKeyDataColumn))
            {
                var ordered = BuildHierarchyOrder(rows, dataBand.KeyDataColumn!, dataBand.MasterKeyDataColumn!);
                for (int i = 0; i < ordered.Count; i++)
                {
                    var (row, level) = ordered[i];
                    var indentMm = level * Math.Max(0, dataBand.IndentMm);
                    var rowH = MeasureBandHeight(dataBand, report, row, rows, i + 1, currentPage.PageNumber);
                    if (cursorY + rowH > maxBodyY) NewPage();
                    AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, row, rows, i + 1, currentPage.PageNumber, indentMm, rowH);
                    cursorY += rowH;
                }
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i > 0 && dataBand.NewPageBefore) NewPage();
                    var rowH = MeasureBandHeight(dataBand, report, rows[i], rows, i + 1, currentPage.PageNumber);
                    if (cursorY + rowH > maxBodyY)
                    {
                        NewPage();
                        foreach (var hb in headerBands.Where(h => h.PrintOnAllPages))
                        {
                            AddBandElements(currentPage, hb, contentLeft, cursorY, report, null, rows, 0, currentPage.PageNumber);
                            cursorY += hb.HeightMm;
                        }
                    }

                    AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, rows[i], rows, i + 1, currentPage.PageNumber, 0, rowH);
                    cursorY += rowH;
                    if (dataBand.NewPageAfter && i < rows.Count - 1) NewPage();
                }
            }
        }

        // Footer bands
        foreach (var band in footerBands)
        {
            if (cursorY + band.HeightMm > maxBodyY) NewPage();
            AddBandElements(currentPage, band, contentLeft, cursorY, report, null, primaryRows, 0, currentPage.PageNumber);
            cursorY += band.HeightMm;
        }

        // ReportSummary bands
        foreach (var band in summaryBands)
        {
            if (cursorY + band.HeightMm > maxBodyY) NewPage();
            AddBandElements(currentPage, band, contentLeft, cursorY, report, null, primaryRows, 0, currentPage.PageNumber);
            cursorY += band.HeightMm;
        }

        // Place PageFooters & Overlay bands on all pages (TotalPageCount resolved by caller)
        foreach (var p in pages)
        {
            foreach (var ob in overlayBands)
            {
                AddBandElements(p, ob, contentLeft, contentTop, report, null, primaryRows, 0, p.PageNumber);
            }
            PlacePageFooter(p);
        }

        var effWm = (page.Watermark?.Enabled == true) ? page.Watermark : masterReport?.Page?.Watermark;
        return new RenderedReport
        {
            ReportName = report.Name,
            PageWidthMm = page.WidthMm,
            PageHeightMm = page.HeightMm,
            Watermark = effWm,
            Pages = pages
        };
    }

    private static double MeasureBandHeight(
        SbsBand band,
        SbsReportDocument report,
        Dictionary<string, object?>? row,
        List<Dictionary<string, object?>>? allRows,
        int lineNumber,
        int pageNumber)
    {
        double h = band.HeightMm;
        if (!band.CanGrow && !band.Components.Any(c => c.CanGrow))
            return h;

        var ctx = new EvalContext { Report = report, Row = row, AllRows = allRows, LineNumber = lineNumber, PageNumber = pageNumber };
        foreach (var comp in band.Components)
        {
            if (!comp.CanGrow && !band.CanGrow) continue;
            if (comp.Type is not ("Text" or "RichText" or "SystemText")) continue;
            var text = ExpressionEvaluator.EvaluateTemplate(comp.Text, comp.Format, ctx);
            if (string.IsNullOrEmpty(text)) continue;

            // Fast line-wrap height estimation in mm (no GDI/Skia overhead!)
            double charWmm = Math.Max(1.0, comp.FontSizePt * 0.21);
            int charsPerLine = Math.Max(1, (int)Math.Floor(Math.Max(4, comp.WidthMm - 2) / charWmm));
            int totalLines = 0;
            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                totalLines += Math.Max(1, (int)Math.Ceiling(rawLine.Length / (double)charsPerLine));
            }
            double lineHmm = comp.FontSizePt * 0.3528 * 1.32;
            double neededH = Math.Max(comp.HeightMm, (totalLines * lineHmm) + 1.5);
            double bottom = comp.YMm + neededH;
            if (bottom > h) h = bottom;
        }
        return h;
    }

    private static List<(Dictionary<string, object?> Row, int Level)> BuildHierarchyOrder(
        List<Dictionary<string, object?>> rows,
        string keyCol,
        string parentCol)
    {
        var kCol = keyCol.Contains('.') ? keyCol.Split('.').Last() : keyCol;
        var pCol = parentCol.Contains('.') ? parentCol.Split('.').Last() : parentCol;

        var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byParent = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            var k = ExpressionEvaluator.TryGetRowValue(r, kCol, out var kv) ? kv?.ToString() ?? "" : "";
            if (!string.IsNullOrEmpty(k)) keySet.Add(k);
        }

        var roots = new List<Dictionary<string, object?>>();
        foreach (var r in rows)
        {
            var p = ExpressionEvaluator.TryGetRowValue(r, pCol, out var pv) ? pv?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(p) || p == "0" || !keySet.Contains(p))
            {
                roots.Add(r);
            }
            else
            {
                if (!byParent.TryGetValue(p, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    byParent[p] = list;
                }
                list.Add(r);
            }
        }

        var result = new List<(Dictionary<string, object?>, int)>(rows.Count);
        var visited = new HashSet<Dictionary<string, object?>>();
        void Visit(Dictionary<string, object?> node, int level)
        {
            if (!visited.Add(node) || level > 20) return;
            result.Add((node, level));
            var k = ExpressionEvaluator.TryGetRowValue(node, kCol, out var kv) ? kv?.ToString() ?? "" : "";
            if (!string.IsNullOrEmpty(k) && byParent.TryGetValue(k, out var children))
            {
                foreach (var ch in children) Visit(ch, level + 1);
            }
        }

        foreach (var root in roots) Visit(root, 0);
        foreach (var r in rows) if (!visited.Contains(r)) result.Add((r, 0));
        return result;
    }

    private static void AddBandElements(
        RenderedPage page,
        SbsBand band,
        double offsetXMm,
        double offsetYMm,
        SbsReportDocument report,
        Dictionary<string, object?>? row,
        List<Dictionary<string, object?>>? allRows,
        int lineNumber,
        int pageNumber,
        double indentMm = 0,
        double? resolvedBandHeightMm = null)
    {
        var effectiveBandH = resolvedBandHeightMm ?? band.HeightMm;
        var bandBg = (lineNumber > 0 && lineNumber % 2 == 0 && !string.IsNullOrWhiteSpace(band.EvenBackgroundColor))
            ? band.EvenBackgroundColor
            : band.BackgroundColor;

        if (!string.IsNullOrWhiteSpace(bandBg) &&
            !bandBg.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            var printableWidth = report.Page.WidthMm - report.Page.MarginLeftMm - report.Page.MarginRightMm;
            page.Elements.Add(new RenderedElement
            {
                Type = "Shape",
                ShapeType = "Rectangle",
                XMm = offsetXMm,
                YMm = offsetYMm,
                WidthMm = printableWidth,
                HeightMm = effectiveBandH,
                BackgroundColor = bandBg,
                BorderWidthPx = 0
            });
        }

        var evalCtx = new EvalContext
        {
            Report = report,
            Row = row,
            AllRows = allRows,
            LineNumber = lineNumber,
            PageNumber = pageNumber
        };

        foreach (var comp in band.Components)
        {
            var color = comp.Color;
            var bg = comp.BackgroundColor;
            var bold = comp.Bold;
            bool visible = true;

            if (comp.Conditions is { Count: > 0 })
            {
                foreach (var cond in comp.Conditions)
                {
                    if (ExpressionEvaluator.EvaluateCondition(cond.Expression, evalCtx))
                    {
                        if (cond.Visible == false) { visible = false; break; }
                        if (!string.IsNullOrWhiteSpace(cond.TextColor)) color = cond.TextColor;
                        if (!string.IsNullOrWhiteSpace(cond.BackgroundColor)) bg = cond.BackgroundColor;
                        if (cond.Bold.HasValue) bold = cond.Bold.Value;
                    }
                }
            }

            if (!visible) continue;

            // Real Table Component cell matrix expansion (renders identically in Preview, PDF, Excel, Word, HTML, CSV!)
            if (comp.Type == "Table" && !string.IsNullOrWhiteSpace(comp.TableColumnsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(comp.TableColumnsJson);
                    if (doc.RootElement.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                    {
                        int rCount = rowsEl.GetArrayLength();
                        if (rCount > 0)
                        {
                            double cellH = comp.HeightMm / rCount;
                            int ri = 0;
                            foreach (var rowEl in rowsEl.EnumerateArray())
                            {
                                int cCount = rowEl.GetArrayLength();
                                if (cCount > 0)
                                {
                                    double cellW = comp.WidthMm / cCount;
                                    int ci = 0;
                                    foreach (var cellEl in rowEl.EnumerateArray())
                                    {
                                        string cText = cellEl.TryGetProperty("text", out var te) ? te.GetString() ?? "" : "";
                                        bool cBold = cellEl.TryGetProperty("bold", out var be) && be.ValueKind == JsonValueKind.True;
                                        string cBg = cellEl.TryGetProperty("bg", out var bge) ? bge.GetString() ?? "transparent" : "transparent";
                                        string cAlign = cellEl.TryGetProperty("hAlign", out var hae) ? hae.GetString() ?? "left" : "left";
                                        string cColor = cellEl.TryGetProperty("color", out var ce) ? ce.GetString() ?? color : color;

                                        page.Elements.Add(new RenderedElement
                                        {
                                            Type = "Text",
                                            XMm = offsetXMm + comp.XMm + (ci * cellW),
                                            YMm = offsetYMm + comp.YMm + (ri * cellH),
                                            WidthMm = cellW,
                                            HeightMm = cellH,
                                            Text = ExpressionEvaluator.EvaluateTemplate(cText, comp.Format, evalCtx),
                                            FontFamily = comp.FontFamily,
                                            FontSizePt = comp.FontSizePt,
                                            Bold = cBold || (ri == 0 && cBg != "transparent"),
                                            Color = cColor,
                                            BackgroundColor = cBg,
                                            BorderColor = string.IsNullOrWhiteSpace(comp.BorderColor) ? "#94a3b8" : comp.BorderColor,
                                            BorderWidthPx = Math.Max(1, comp.BorderWidthPx),
                                            BorderStyle = comp.BorderStyle,
                                            BorderSides = "All",
                                            HAlign = cAlign,
                                            VAlign = "middle",
                                            WordWrap = true
                                        });
                                        ci++;
                                    }
                                }
                                ri++;
                            }
                            continue;
                        }
                    }
                }
                catch { /* fall back to standard component element */ }
            }

            var evaluated = ExpressionEvaluator.EvaluateTemplate(comp.Text, comp.Format, evalCtx);
            var labelEvaluated = string.IsNullOrEmpty(comp.Label) ? null : ExpressionEvaluator.EvaluateTemplate(comp.Label, null, evalCtx);
            List<ChartPoint>? chartData = null;
            if (comp.Type == "Chart" && allRows != null && !string.IsNullOrWhiteSpace(comp.CategoryField) && !string.IsNullOrWhiteSpace(comp.ValueField))
            {
                var catCol = comp.CategoryField.Contains('.') ? comp.CategoryField.Split('.').Last() : comp.CategoryField;
                var valCol = comp.ValueField.Contains('.') ? comp.ValueField.Split('.').Last() : comp.ValueField;
                chartData = allRows
                    .GroupBy(r => r.TryGetValue(catCol, out var c) ? c?.ToString() ?? "" : "")
                    .Select(g => new ChartPoint
                    {
                        Label = g.Key,
                        Value = g.Sum(r => ExpressionEvaluator.ToDecimal(r.TryGetValue(valCol, out var v) ? v : null))
                    })
                    .ToList();
            }

            var compH = (comp.CanGrow || band.CanGrow) ? Math.Max(comp.HeightMm, effectiveBandH - comp.YMm) : comp.HeightMm;
            page.Elements.Add(new RenderedElement
            {
                Type = comp.Type,
                XMm = offsetXMm + comp.XMm + indentMm,
                YMm = offsetYMm + comp.YMm,
                WidthMm = Math.Max(2, comp.WidthMm - indentMm),
                HeightMm = compH,
                Text = evaluated,
                FontFamily = comp.FontFamily,
                FontSizePt = comp.FontSizePt,
                Bold = bold,
                Italic = comp.Italic,
                Underline = comp.Underline,
                Strike = comp.Strike,
                Color = color,
                BackgroundColor = bg,
                BorderColor = comp.BorderColor,
                BorderWidthPx = comp.BorderWidthPx,
                BorderStyle = comp.BorderStyle,
                BorderSides = comp.BorderSides,
                CornerRadiusPx = comp.CornerRadiusPx,
                HAlign = comp.HAlign,
                VAlign = comp.VAlign,
                WordWrap = comp.WordWrap,
                Angle = comp.Angle,
                PaddingLeftMm = comp.PaddingLeftMm,
                PaddingTopMm = comp.PaddingTopMm,
                PaddingRightMm = comp.PaddingRightMm,
                PaddingBottomMm = comp.PaddingBottomMm,
                Label = labelEvaluated,
                Placeholder = comp.Placeholder,
                Checked = comp.Checked,
                GroupName = comp.GroupName,
                Items = comp.Items,
                SignatureData = comp.SignatureData,
                ButtonAction = comp.ButtonAction,
                ReadOnly = comp.ReadOnly,
                Required = comp.Required,
                ImageUrl = comp.ImageUrl,
                ShapeType = comp.ShapeType,
                LineOrientation = comp.LineOrientation,
                BarcodeFormat = comp.BarcodeFormat,
                ChartType = comp.ChartType,
                ChartData = chartData
            });
        }
    }
}
