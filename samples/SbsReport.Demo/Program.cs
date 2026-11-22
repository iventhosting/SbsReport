using SbsReport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSbsReport(options =>
{
    options.RoutePrefix = "/sbs-report";
});

var app = builder.Build();

app.UseSbsReport();
app.MapGet("/", () => Results.Redirect("/sbs-report/"));

app.Run();
