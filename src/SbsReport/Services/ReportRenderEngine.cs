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
    public bool AllowHtmlTags { get; set; }
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
    public string ImageFit { get; set; } = "Contain";
    public string? ShapeType { get; set; }
    public string? LineOrientation { get; set; }
    public string BarcodeFormat { get; set; } = "Code128";
    public string? ChartType { get; set; }
    public List<ChartPoint>? ChartData { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PrintOn { get; set; }
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

    [GeneratedRegex(@"<(?:br\s*/?|/?(?:b|strong|i|em|u|color(?:\s*=\s*[""']?#[0-9a-fA-F]{3,8}[""']?)?))>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHtmlTagRegex();

    public static string StripInlineHtmlTags(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<')) return text ?? string.Empty;
        var withBreaks = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        return InlineHtmlTagRegex().Replace(withBreaks, string.Empty);
    }

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
            FinalizePages(combinedPages, totalCombined);

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
        FinalizePages(single.Pages, single.Pages.Count);
        sw.Stop();
        single.RenderTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        return single;
    }

    private static void FinalizePages(List<RenderedPage> pages, int totalPages)
    {
        var totalStr = totalPages.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            bool isFirst = (i == 0);
            bool isLast = (i == pages.Count - 1);
            for (int e = p.Elements.Count - 1; e >= 0; e--)
            {
                var el = p.Elements[e];
                if (!ShouldPrintOn(el.PrintOn, isFirst, isLast))
                {
                    p.Elements.RemoveAt(e);
                    continue;
                }
                if (el.Text.Contains("__TOTAL_PAGES__", StringComparison.Ordinal))
                    el.Text = el.Text.Replace("__TOTAL_PAGES__", totalStr);
            }
        }
    }

    private static bool ShouldPrintOn(string? printOn, bool isFirst, bool isLast)
    {
        if (string.IsNullOrEmpty(printOn) || printOn.Equals("AllPages", StringComparison.OrdinalIgnoreCase))
            return true;
        return printOn switch
        {
            "ExceptFirstPage" => !isFirst,
            "ExceptLastPage" => !isLast,
            "OnlyFirstPage" => isFirst,
            "OnlyLastPage" => isLast,
            "ExceptFirstAndLastPage" => !isFirst && !isLast,
            _ => true
        };
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
                {
                    ApplyCalculatedColumns(report, dataSourceName, exact);
                    return exact;
                }
                var norm = ExpressionEvaluator.NormalizeTurkishKey(dataSourceName);
                foreach (var kv in directTables)
                    if (ExpressionEvaluator.NormalizeTurkishKey(kv.Key) == norm)
                    {
                        ApplyCalculatedColumns(report, dataSourceName, kv.Value);
                        return kv.Value;
                    }
            }
            if (directTables.Count == 1)
            {
                var singleTable = directTables.Values.First();
                ApplyCalculatedColumns(report, dataSourceName ?? directTables.Keys.First(), singleTable);
                return singleTable;
            }
        }
        var resolved = _dataService.ResolveRows(report, dataSourceName, runtimeData);
        ApplyCalculatedColumns(report, dataSourceName, resolved);
        return resolved;
    }

    private static void ApplyCalculatedColumns(
        SbsReportDocument report,
        string? dataSourceName,
        List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0 || report.DataSources is not { Count: > 0 }) return;
        var ds = !string.IsNullOrWhiteSpace(dataSourceName)
            ? report.DataSources.FirstOrDefault(d =>
                string.Equals(d.Name, dataSourceName, StringComparison.OrdinalIgnoreCase) ||
                ExpressionEvaluator.NormalizeTurkishKey(d.Name) == ExpressionEvaluator.NormalizeTurkishKey(dataSourceName))
            : report.DataSources.FirstOrDefault();
        if (ds?.Columns is not { Count: > 0 }) return;

        var calcCols = ds.Columns.Where(c => c.IsCalculated && !string.IsNullOrWhiteSpace(c.Expression) && !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (calcCols.Count == 0) return;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var ctx = new EvalContext { Report = report, Row = row, AllRows = rows, LineNumber = i + 1, PageNumber = 1 };
            foreach (var col in calcCols)
            {
                var expr = col.Expression!.Trim();
                if (expr.StartsWith('{') && expr.EndsWith('}'))
                    expr = expr[1..^1].Trim();
                row[col.Name] = ExpressionEvaluator.EvaluateExpression(expr, ctx);
            }
        }
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
            ? masterReport.Bands.Where(b => b.Enabled && b.Type == "PageHeader").ToList()
            : new List<SbsBand>();
        var masterPageFooters = (masterReport != null && report.InheritMasterFooter)
            ? masterReport.Bands.Where(b => b.Enabled && b.Type == "PageFooter").ToList()
            : new List<SbsBand>();
        var masterOverlays = masterReport?.Bands.Where(b => b.Enabled && b.Type is "Overlay" or "OverlayBand").ToList() ?? new();

        var reportTitleBands = report.Bands.Where(b => b.Enabled && b.Type == "ReportTitle").ToList();
        var pageHeaderBands = masterPageHeaders.Count > 0
            ? masterPageHeaders
            : report.Bands.Where(b => b.Enabled && b.Type == "PageHeader").ToList();
        var headerBands = report.Bands.Where(b => b.Enabled && b.Type is "Header" or "ColumnHeader").ToList();
        var groupHeaderBands = report.Bands.Where(b => b.Enabled && b.Type == "GroupHeader").ToList();
        var dataBands = report.Bands.Where(b => b.Enabled && b.Type is "Data" or "Table" or "HierarchicalData" or "ChildBand").ToList();
        var groupFooterBands = report.Bands.Where(b => b.Enabled && b.Type == "GroupFooter").ToList();
        var emptyBands = report.Bands.Where(b => b.Enabled && b.Type == "EmptyBand").ToList();
        var footerBands = report.Bands.Where(b => b.Enabled && b.Type is "Footer" or "ColumnFooter").ToList();
        var pageFooterBands = masterPageFooters.Count > 0
            ? masterPageFooters
            : report.Bands.Where(b => b.Enabled && b.Type == "PageFooter").ToList();
        var summaryBands = report.Bands.Where(b => b.Enabled && b.Type == "ReportSummary").ToList();
        var overlayBands = masterOverlays.Concat(report.Bands.Where(b => b.Enabled && b.Type is "Overlay" or "OverlayBand")).ToList();

        var pageFooterHeight = pageFooterBands.Sum(b => b.HeightMm);
        var maxBodyY = contentBottom - pageFooterHeight;

        var pages = new List<RenderedPage>();
        int displayPageNumber = pageNumberOffset + 1;
        var currentPage = new RenderedPage { PageNumber = displayPageNumber };
        pages.Add(currentPage);

        double cursorY = contentTop;
        var dupTracker = new Dictionary<string, (string LastText, RenderedElement? LastEl, int PageIdx)>(StringComparer.Ordinal);

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

        void NewPage(bool resetPageNo = false)
        {
            displayPageNumber = resetPageNo ? 1 : (displayPageNumber + 1);
            currentPage = new RenderedPage { PageNumber = displayPageNumber };
            pages.Add(currentPage);
            dupTracker.Clear();
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
        // Raporda veri kaynağına bağlı bir Data bandı varsa, kaynağı boş bırakılmış Data bandı o kaynağı
        // devralmaz: statik bant gibi bir kez basılır. (Aksi halde boş bant kayıt sayısı kadar tekrar eder.)
        // Hiçbir Data bandı bağlı değilse eski davranış korunur: boş bant rapordaki ilk veri kaynağını kullanır.
        var anyBoundDataBand = dataBands.Any(b => !string.IsNullOrWhiteSpace(b.DataSource));
        foreach (var dataBand in dataBands)
        {
            var unbound = string.IsNullOrWhiteSpace(dataBand.DataSource) && dataBand.Type != "ChildBand";
            var rows = unbound && anyBoundDataBand
                ? new List<Dictionary<string, object?>>()
                : ResolveRowsFast(report, unbound ? primaryDsName : dataBand.DataSource, runtimeData, directTables);

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
                    bool shouldResetPage = grpIdx > 0 && groupHeaderBands.Any(g => g.ResetPageNumber);
                    if (shouldResetPage || (grpIdx > 0 && groupHeaderBands.Any(g => g.NewPageBefore)))
                        NewPage(shouldResetPage);

                    dupTracker.Clear();
                    foreach (var gh in groupHeaderBands)
                    {
                        var ghH = MeasureBandHeight(gh, report, grpRows[0], grpRows, 0, currentPage.PageNumber);
                        var firstRowH = (gh.KeepGroupTogether && grpRows.Count > 0)
                            ? MeasureBandHeight(dataBand, report, grpRows[0], grpRows, 1, currentPage.PageNumber)
                            : 0;
                        if (cursorY + ghH + firstRowH > maxBodyY) NewPage();
                        AddBandElements(currentPage, gh, contentLeft, cursorY, report, grpRows[0], grpRows, 0, currentPage.PageNumber, 0, ghH);
                        cursorY += ghH;
                        if (gh.NewPageAfter) NewPage();
                    }

                    int grpLine = 0;
                    foreach (var row in grpRows)
                    {
                        lineNo++;
                        grpLine++;
                        var rowH = MeasureBandHeight(dataBand, report, row, grpRows, grpLine, currentPage.PageNumber);
                        if (cursorY + rowH > maxBodyY) NewPage();
                        AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, row, grpRows, grpLine, currentPage.PageNumber, 0, rowH, lineNo, dupTracker, pages.Count);
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
                    AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, row, rows, i + 1, currentPage.PageNumber, indentMm, rowH, i + 1, dupTracker, pages.Count);
                    cursorY += rowH;
                }
            }
            else if (dataBand.Columns > 1)
            {
                dupTracker.Clear();
                int cols = Math.Clamp(dataBand.Columns, 1, 12);
                double gap = Math.Max(0, dataBand.ColumnGapsMm);
                double printableW = page.WidthMm - page.MarginLeftMm - page.MarginRightMm;
                double colW = (printableW - (cols - 1) * gap) / cols;

                for (int i = 0; i < rows.Count; i += cols)
                {
                    double rowH = dataBand.HeightMm;
                    int sliceCount = Math.Min(cols, rows.Count - i);
                    for (int c = 0; c < sliceCount; c++)
                    {
                        var h = MeasureBandHeight(dataBand, report, rows[i + c], rows, i + c + 1, currentPage.PageNumber);
                        if (h > rowH) rowH = h;
                    }

                    if (cursorY + rowH > maxBodyY)
                    {
                        NewPage();
                        foreach (var hb in headerBands.Where(h => h.PrintOnAllPages))
                        {
                            AddBandElements(currentPage, hb, contentLeft, cursorY, report, null, rows, 0, currentPage.PageNumber);
                            cursorY += hb.HeightMm;
                        }
                    }

                    for (int c = 0; c < sliceCount; c++)
                    {
                        double colX = contentLeft + c * (colW + gap);
                        AddBandElements(currentPage, dataBand, colX, cursorY, report, rows[i + c], rows, i + c + 1, currentPage.PageNumber, 0, rowH, i + c + 1, null, pages.Count);
                    }
                    cursorY += rowH;
                }
            }
            else
            {
                dupTracker.Clear();
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

                    AddBandElements(currentPage, dataBand, contentLeft, cursorY, report, rows[i], rows, i + 1, currentPage.PageNumber, 0, rowH, i + 1, dupTracker, pages.Count);
                    cursorY += rowH;
                    if (dataBand.NewPageAfter && i < rows.Count - 1) NewPage();
                }
            }
        }

        // EmptyBand: fill remaining space before bottom-aligned footers / summary
        var trailingFootersH = footerBands.Sum(b => b.HeightMm) + summaryBands.Sum(b => b.HeightMm);
        foreach (var eb in emptyBands)
        {
            if (eb.HeightMm <= 0.5) continue;
            var fillLimit = Math.Max(cursorY, maxBodyY - trailingFootersH);
            int emptyRowIdx = 0;
            while (cursorY + eb.HeightMm <= fillLimit + 0.01 && emptyRowIdx < 200)
            {
                emptyRowIdx++;
                AddBandElements(currentPage, eb, contentLeft, cursorY, report, null, primaryRows, emptyRowIdx, currentPage.PageNumber);
                cursorY += eb.HeightMm;
            }
        }

        // Footer bands (with PrintAtBottom support)
        for (int fi = 0; fi < footerBands.Count; fi++)
        {
            var band = footerBands[fi];
            if (cursorY + band.HeightMm > maxBodyY) NewPage();
            if (band.PrintAtBottom)
            {
                var remH = footerBands.Skip(fi).Sum(b => b.HeightMm) + summaryBands.Where(s => s.PrintAtBottom).Sum(s => s.HeightMm);
                var bottomY = maxBodyY - remH;
                if (bottomY > cursorY) cursorY = bottomY;
            }
            AddBandElements(currentPage, band, contentLeft, cursorY, report, null, primaryRows, 0, currentPage.PageNumber);
            cursorY += band.HeightMm;
        }

        // ReportSummary bands (with PrintAtBottom & NewPageBefore support)
        for (int si = 0; si < summaryBands.Count; si++)
        {
            var band = summaryBands[si];
            if (band.NewPageBefore || cursorY + band.HeightMm > maxBodyY) NewPage();
            if (band.PrintAtBottom)
            {
                var remH = summaryBands.Skip(si).Sum(b => b.HeightMm);
                var bottomY = maxBodyY - remH;
                if (bottomY > cursorY) cursorY = bottomY;
            }
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
        double? resolvedBandHeightMm = null,
        int lineThrough = 0,
        Dictionary<string, (string LastText, RenderedElement? LastEl, int PageIdx)>? dupTracker = null,
        int currentPageIdx = 0)
    {
        if (!band.Enabled) return;
        var effectiveBandH = resolvedBandHeightMm ?? band.HeightMm;
        var bandPrintOn = string.IsNullOrWhiteSpace(band.PrintOn) ? "AllPages" : band.PrintOn;
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
                BorderWidthPx = 0,
                PrintOn = bandPrintOn
            });
        }

        var evalCtx = new EvalContext
        {
            Report = report,
            Row = row,
            AllRows = allRows,
            LineNumber = lineNumber,
            GroupLine = lineNumber,
            LineThrough = lineThrough > 0 ? lineThrough : lineNumber,
            PageNumber = pageNumber
        };

        foreach (var comp in band.Components)
        {
            if (!comp.Enabled || !comp.Printable) continue;

            var color = comp.Color;
            var bg = comp.BackgroundColor;
            var bold = comp.Bold;
            var italic = comp.Italic;
            var underline = comp.Underline;
            var borderColor = comp.BorderColor;
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
                        if (cond.Italic.HasValue) italic = cond.Italic.Value;
                        if (cond.Underline.HasValue) underline = cond.Underline.Value;
                        if (!string.IsNullOrWhiteSpace(cond.BorderColor)) borderColor = cond.BorderColor;
                    }
                }
            }

            if (!visible) continue;

            var effectivePrintOn = (!string.IsNullOrWhiteSpace(comp.PrintOn) && !comp.PrintOn.Equals("AllPages", StringComparison.OrdinalIgnoreCase))
                ? comp.PrintOn
                : bandPrintOn;

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

            // HideZeros (Sakla Sıfırlar): hide 0 / 0,00 / 0.00 / 0 ₺ values cleanly
            if (comp.HideZeros && !string.IsNullOrWhiteSpace(evaluated))
            {
                var cleanedZero = evaluated.Trim().Replace("₺", "").Replace("TL", "", StringComparison.OrdinalIgnoreCase).Replace("€", "").Replace("$", "").Replace("%", "").Trim();
                if ((decimal.TryParse(cleanedZero, NumberStyles.Number, TrCulture, out var zv) ||
                     decimal.TryParse(cleanedZero, NumberStyles.Number, CultureInfo.InvariantCulture, out zv)) && zv == 0m)
                {
                    evaluated = string.Empty;
                }
            }

            // MaxNumberOfLines (Maksimum Satır Sayısı)
            if (comp.MaxNumberOfLines > 0 && !string.IsNullOrEmpty(evaluated))
            {
                var splitLines = evaluated.Replace("\r", "").Split('\n');
                if (splitLines.Length > comp.MaxNumberOfLines)
                    evaluated = string.Join("\n", splitLines.Take(comp.MaxNumberOfLines));
            }

            // TextInCells (TC Kimlik No / IBAN / Vergi No karakter kutucukları — Preview, PDF, HTML, Word, SVG birebir!)
            if (comp.Type == "TextInCells")
            {
                double cellW = Math.Max(2.5, comp.CellWidthMm > 0 ? comp.CellWidthMm : 5);
                double cellGap = Math.Max(0, comp.CellGapMm);
                double availW = Math.Max(cellW, comp.WidthMm - indentMm);
                int boxCount = comp.CellCount > 0
                    ? Math.Clamp(comp.CellCount, 1, 64)
                    : Math.Max(1, (int)Math.Floor((availW + cellGap) / (cellW + cellGap)));
                var chars = evaluated ?? string.Empty;
                for (int ci = 0; ci < boxCount; ci++)
                {
                    page.Elements.Add(new RenderedElement
                    {
                        Type = "Text",
                        XMm = offsetXMm + comp.XMm + indentMm + ci * (cellW + cellGap),
                        YMm = offsetYMm + comp.YMm,
                        WidthMm = cellW,
                        HeightMm = comp.HeightMm,
                        Text = ci < chars.Length ? chars[ci].ToString() : string.Empty,
                        FontFamily = comp.FontFamily,
                        FontSizePt = comp.FontSizePt,
                        Bold = bold,
                        Italic = italic,
                        Underline = underline,
                        Color = color,
                        BackgroundColor = bg == "transparent" ? "#ffffff" : bg,
                        BorderColor = string.IsNullOrWhiteSpace(borderColor) ? "#64748b" : borderColor,
                        BorderWidthPx = Math.Max(1, comp.BorderWidthPx),
                        BorderStyle = comp.BorderStyle,
                        BorderSides = "All",
                        CornerRadiusPx = comp.CornerRadiusPx,
                        HAlign = "center",
                        VAlign = "middle",
                        WordWrap = false,
                        PrintOn = effectivePrintOn
                    });
                }
                continue;
            }
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

            var compH = (comp.CanGrow || comp.GrowToHeight || band.CanGrow) ? Math.Max(comp.HeightMm, effectiveBandH - comp.YMm) : comp.HeightMm;
            var compW = Math.Max(2, comp.WidthMm - indentMm);

            // ProcessingDuplicates (None | Hide | Merge)
            var dupMode = comp.ProcessingDuplicates ?? "None";
            if (dupTracker != null && !dupMode.Equals("None", StringComparison.OrdinalIgnoreCase) && comp.Type is "Text" or "RichText" or "SystemText")
            {
                if (dupTracker.TryGetValue(comp.Id, out var prev) &&
                    prev.PageIdx == currentPageIdx &&
                    string.Equals(prev.LastText, evaluated, StringComparison.Ordinal))
                {
                    if (dupMode.Equals("Merge", StringComparison.OrdinalIgnoreCase) && prev.LastEl != null)
                    {
                        prev.LastEl.HeightMm += effectiveBandH;
                        continue;
                    }
                    if (dupMode.Equals("Hide", StringComparison.OrdinalIgnoreCase))
                    {
                        evaluated = string.Empty;
                    }
                }
            }

            // ShrinkFontToFit (automatically scales FontSizePt down to ShrinkFontToFitMinPt if text overflows box)
            var fontSizePt = comp.FontSizePt;
            if (comp.ShrinkFontToFit && !string.IsNullOrEmpty(evaluated) && comp.Type is "Text" or "RichText" or "SystemText")
            {
                double minPt = Math.Max(4, comp.ShrinkFontToFitMinPt > 0 ? comp.ShrinkFontToFitMinPt : 6);
                while (fontSizePt > minPt)
                {
                    double charWmm = Math.Max(0.8, fontSizePt * 0.21);
                    int charsPerLine = Math.Max(1, (int)Math.Floor(Math.Max(2, compW - 1.5) / charWmm));
                    int lines = 0;
                    foreach (var rawLine in evaluated.Replace("\r", "").Split('\n'))
                        lines += comp.WordWrap ? Math.Max(1, (int)Math.Ceiling(rawLine.Length / (double)charsPerLine)) : 1;
                    double neededH = (lines * fontSizePt * 0.3528 * 1.25) + 0.8;
                    double singleLineW = evaluated.Length * charWmm;
                    bool fits = comp.WordWrap ? (neededH <= compH) : (singleLineW <= compW - 1.5 && neededH <= compH);
                    if (fits) break;
                    fontSizePt = Math.Max(minPt, fontSizePt - 0.5);
                }
            }

            var renderedEl = new RenderedElement
            {
                Type = comp.Type,
                XMm = offsetXMm + comp.XMm + indentMm,
                YMm = offsetYMm + comp.YMm,
                WidthMm = compW,
                HeightMm = compH,
                Text = evaluated,
                FontFamily = comp.FontFamily,
                FontSizePt = fontSizePt,
                Bold = bold,
                Italic = italic,
                Underline = underline,
                Strike = comp.Strike,
                Color = color,
                BackgroundColor = bg,
                BorderColor = borderColor,
                BorderWidthPx = comp.BorderWidthPx,
                BorderStyle = comp.BorderStyle,
                BorderSides = comp.BorderSides,
                CornerRadiusPx = comp.CornerRadiusPx,
                HAlign = comp.HAlign,
                VAlign = comp.VAlign,
                WordWrap = comp.WordWrap,
                AllowHtmlTags = comp.AllowHtmlTags,
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
                ImageFit = string.IsNullOrWhiteSpace(comp.ImageFit) ? "Contain" : comp.ImageFit,
                ShapeType = comp.ShapeType,
                LineOrientation = comp.LineOrientation,
                BarcodeFormat = comp.BarcodeFormat,
                ChartType = comp.ChartType,
                ChartData = chartData,
                PrintOn = effectivePrintOn
            };
            page.Elements.Add(renderedEl);

            if (dupTracker != null && !dupMode.Equals("None", StringComparison.OrdinalIgnoreCase) && comp.Type is "Text" or "RichText" or "SystemText")
            {
                if (!dupMode.Equals("Hide", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(evaluated))
                    dupTracker[comp.Id] = (evaluated, renderedEl, currentPageIdx);
            }
        }
    }
}
