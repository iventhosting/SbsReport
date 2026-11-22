using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SbsReport.Models;
using SbsReport.Services;

namespace SbsReport;

public class SbsReportOptions
{
    /// <summary>
    /// Rapor .sbs dosyalarının saklanacağı klasör yolu. Boş bırakılırsa ContentRoot/Reports kullanılır.
    /// </summary>
    public string? ReportsDirectory { get; set; }

    /// <summary>
    /// Web Designer ve Viewer UI'ının yayınlanacağı URL öneki (varsayılan: /sbs-report).
    /// </summary>
    public string RoutePrefix { get; set; } = "/sbs-report";

    /// <summary>
    /// True yapılırsa /sbs-report ve API uçları ASP.NET Core Authorization ile korunur.
    /// </summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>
    /// RequireAuthorization=true iken kullanılacak opsiyonel Policy adı (boşsa varsayılan auth policy).
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}

public static class SbsReportExtensions
{
    public static IServiceCollection AddSbsReport(this IServiceCollection services, Action<SbsReportOptions>? configure = null)
    {
        var options = new SbsReportOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<DemoDataService>();
        services.AddSingleton<ReportRenderEngine>();
        services.AddSingleton<ReportExportService>();
        services.AddSingleton<SbsReportEngine>();
        services.AddSingleton<ReportStorageService>();

        return services;
    }

    public static IApplicationBuilder UseSbsReport(this IApplicationBuilder app, string? routePrefix = null)
    {
        var options = app.ApplicationServices.GetRequiredService<SbsReportOptions>();
        var prefix = (routePrefix ?? options.RoutePrefix).TrimEnd('/');
        if (string.IsNullOrEmpty(prefix))
            prefix = "/sbs-report";

        app.UseStaticFiles();

        if (app is IEndpointRouteBuilder endpoints)
        {
            MapSbsEndpoints(endpoints, prefix, options);
        }

        return app;
    }

    public static void MapSbsEndpoints(IEndpointRouteBuilder endpoints, string prefix = "/sbs-report", SbsReportOptions? options = null)
    {
        prefix = prefix.TrimEnd('/');
        var group = endpoints.MapGroup(prefix);
        if (options?.RequireAuthorization == true)
        {
            if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
                group.RequireAuthorization(options.AuthorizationPolicy);
            else
                group.RequireAuthorization();
        }

        // Serve embedded UI files directly so NuGet consumers need zero extra static-file config
        group.MapGet("/", () => ServeEmbeddedAsset("index.html", "text/html; charset=utf-8"));
        group.MapGet("/styles.css", () => ServeEmbeddedAsset("styles.css", "text/css; charset=utf-8"));
        group.MapGet("/i18n.js", () => ServeEmbeddedAsset("i18n.js", "application/javascript; charset=utf-8"));
        group.MapGet("/app.js", () => ServeEmbeddedAsset("app.js", "application/javascript; charset=utf-8"));

        var api = group.MapGroup("/api");

        api.MapGet("/reports", (ReportStorageService storage) =>
            Results.Ok(storage.ListReports()));

        api.MapGet("/reports/{name}", (string name, ReportStorageService storage) =>
        {
            var doc = storage.LoadReport(name);
            return doc is null ? Results.NotFound(new { error = "Rapor bulunamadı." }) : Results.Ok(doc);
        });

        api.MapPost("/reports/{name}", (string name, SbsReportDocument doc, ReportStorageService storage) =>
        {
            storage.SaveReport(name, doc);
            return Results.Ok(new { saved = true, name = doc.Name, fileName = doc.Name + ".sbs" });
        });

        api.MapDelete("/reports/{name}", (string name, ReportStorageService storage) =>
        {
            return storage.DeleteReport(name)
                ? Results.Ok(new { deleted = true })
                : Results.NotFound(new { error = "Rapor bulunamadı." });
        });

        api.MapPost("/reports/render", (RenderRequest request, ReportRenderEngine engine, ReportStorageService storage) =>
        {
            SbsReportDocument? masterDoc = null;
            if (!string.IsNullOrWhiteSpace(request.Report.MasterReportName) &&
                !string.Equals(request.Report.MasterReportName, request.Report.Name, StringComparison.OrdinalIgnoreCase))
            {
                masterDoc = storage.LoadReport(request.Report.MasterReportName);
            }
            var rendered = engine.Render(request.Report, request.Data, masterDoc);
            return Results.Ok(rendered);
        });

        api.MapPost("/reports/export/{format}", (string format, RenderRequest request, ReportRenderEngine engine, ReportExportService exporter, ReportStorageService storage) =>
        {
            SbsReportDocument? masterDoc = null;
            if (!string.IsNullOrWhiteSpace(request.Report.MasterReportName) &&
                !string.Equals(request.Report.MasterReportName, request.Report.Name, StringComparison.OrdinalIgnoreCase))
            {
                masterDoc = storage.LoadReport(request.Report.MasterReportName);
            }
            var rendered = engine.Render(request.Report, request.Data, masterDoc);
            var (bytes, contentType, ext) = exporter.Export(rendered, format);
            var fileName = (string.IsNullOrWhiteSpace(request.Report.Name) ? "Rapor" : request.Report.Name) + ext;
            return Results.File(bytes, contentType, fileName);
        });

        api.MapGet("/datasources/demo", (DemoDataService demo) =>
            Results.Ok(demo.GetAvailableDemoDataSources()));

        api.MapGet("/datasources/demo/{name}/rows", (string name, DemoDataService demo) =>
        {
            var rows = demo.ResolveRows(new SbsReportDocument(), name, null);
            return Results.Ok(rows);
        });
    }

    private static IResult ServeEmbeddedAsset(string fileName, string contentType)
    {
        var assembly = typeof(SbsReportExtensions).Assembly;
        var resourceName = $"SbsReport.wwwroot.{fileName}";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return Results.NotFound($"Asset not found: {resourceName}");
        return Results.Stream(stream, contentType);
    }
}

public class RenderRequest
{
    public SbsReportDocument Report { get; set; } = new();
    public Dictionary<string, List<Dictionary<string, JsonElement>>>? Data { get; set; }
}
