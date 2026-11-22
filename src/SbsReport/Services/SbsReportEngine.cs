using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using SbsReport.Models;

namespace SbsReport.Services;

/// <summary>
/// Sobis.SobisPersonel'deki IReportEngineAdapter ile birebir uyumlu yüksek hızlı headless C# rapor motoru.
/// Şablonları dosya zaman damgasına göre bellek önbelleğinde tutar (0 ms şablon okuma).
/// </summary>
public class SbsReportEngine
{
    private readonly ReportRenderEngine _renderEngine;
    private readonly ReportExportService _exportService;
    private static readonly ConcurrentDictionary<string, (DateTime UtcTicks, SbsReportDocument Doc)> TemplateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string TemplateFileExtension => ".sbs";

    public SbsReportEngine(ReportRenderEngine renderEngine, ReportExportService exportService)
    {
        _renderEngine = renderEngine;
        _exportService = exportService;
    }

    private static SbsReportDocument LoadTemplateCached(string path)
    {
        var mtime = File.GetLastWriteTimeUtc(path);
        if (TemplateCache.TryGetValue(path, out var cached) && cached.UtcTicks == mtime)
            return cached.Doc;

        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<SbsReportDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException($"Geçersiz .sbs şablon dosyası: {path}");
        TemplateCache[path] = (mtime, doc);
        return doc;
    }

    /// <summary>
    /// Şablonu diskten yükler, DataSet ve parametrelerle render eder, Snapshot ve PDF byte[] döndürür.
    /// </summary>
    public (byte[] SnapshotBytes, byte[] PdfBytes) RenderHeadless(
        string reportTemplatePath,
        Dictionary<string, object>? parameters,
        DataSet? dataSet)
    {
        if (!File.Exists(reportTemplatePath))
            throw new FileNotFoundException($"Rapor şablonu bulunamadı: {reportTemplatePath}");

        var template = LoadTemplateCached(reportTemplatePath);

        // Thread-safe render kopyası (sadece Variables kopyalanır, bant/bileşen ağacı paylaşımlı okunur)
        var doc = new SbsReportDocument
        {
            Version = template.Version,
            Name = template.Name,
            Description = template.Description,
            IsMasterTemplate = template.IsMasterTemplate,
            MasterReportName = template.MasterReportName,
            InheritMasterHeader = template.InheritMasterHeader,
            InheritMasterFooter = template.InheritMasterFooter,
            Page = template.Page,
            DataSources = template.DataSources,
            Bands = template.Bands,
            Pages = template.Pages,
            Variables = template.Variables != null
                ? new Dictionary<string, string>(template.Variables, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        // Master Form bağlıysa aynı klasörden önbellekli yükle
        SbsReportDocument? masterDoc = null;
        if (!string.IsNullOrWhiteSpace(doc.MasterReportName))
        {
            var dir = Path.GetDirectoryName(reportTemplatePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var masterPath = Path.Combine(dir, doc.MasterReportName + ".sbs");
                if (File.Exists(masterPath))
                    masterDoc = LoadTemplateCached(masterPath);
            }
        }

        var rendered = _renderEngine.Render(doc, dataSet, parameters, masterDoc);
        var snapshot = _exportService.CreateSnapshot(rendered);
        var pdf = _exportService.ExportPdf(rendered);
        return (snapshot, pdf);
    }

    /// <summary>
    /// Snapshot byte[] üzerinden yeniden SQL/DataProvider çalıştırmadan anında PDF, Excel, Word, HTML, CSV, XML, JSON, SVG üretir.
    /// </summary>
    public byte[] ExportFromSnapshot(byte[] snapshotBytes, string format)
        => _exportService.ExportFromSnapshot(snapshotBytes, format);
}
