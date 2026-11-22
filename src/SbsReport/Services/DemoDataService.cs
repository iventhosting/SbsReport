using System.Text.Json;
using SbsReport.Models;

namespace SbsReport.Services;

public class DemoDataService
{
    private static readonly Dictionary<string, List<Dictionary<string, object?>>> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Categories"] = new()
        {
            new() { ["CategoryID"] = 1, ["CategoryName"] = "İçecekler", ["Description"] = "Kahve, çay ve soğuk içecekler" },
            new() { ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["Description"] = "Bilgisayar ve çevre birimleri" },
            new() { ["CategoryID"] = 3, ["CategoryName"] = "Ofis Malzemeleri", ["Description"] = "Kağıt, kalem ve kırtasiye" },
            new() { ["CategoryID"] = 4, ["CategoryName"] = "Mobilya", ["Description"] = "Ofis koltukları ve masalar" }
        },
        ["Products"] = new()
        {
            new() { ["ProductID"] = 101, ["ProductName"] = "Sobis Pro Monitör 27\"", ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["UnitPrice"] = 8450.00m, ["UnitsInStock"] = 24, ["Quantity"] = 2 },
            new() { ["ProductID"] = 102, ["ProductName"] = "Mekanik Klavye RGB", ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["UnitPrice"] = 2150.50m, ["UnitsInStock"] = 58, ["Quantity"] = 5 },
            new() { ["ProductID"] = 103, ["ProductName"] = "Kablosuz Ergonomik Mouse", ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["UnitPrice"] = 1290.00m, ["UnitsInStock"] = 72, ["Quantity"] = 8 },
            new() { ["ProductID"] = 104, ["ProductName"] = "A4 Fotokopi Kağıdı (5'li Koli)", ["CategoryID"] = 3, ["CategoryName"] = "Ofis Malzemeleri", ["UnitPrice"] = 640.00m, ["UnitsInStock"] = 140, ["Quantity"] = 15 },
            new() { ["ProductID"] = 105, ["ProductName"] = "Ergonomik Çalışma Koltuğu", ["CategoryID"] = 4, ["CategoryName"] = "Mobilya", ["UnitPrice"] = 12400.00m, ["UnitsInStock"] = 11, ["Quantity"] = 3 },
            new() { ["ProductID"] = 106, ["ProductName"] = "Filtre Kahve 1 Kg", ["CategoryID"] = 1, ["CategoryName"] = "İçecekler", ["UnitPrice"] = 485.00m, ["UnitsInStock"] = 39, ["Quantity"] = 10 },
            new() { ["ProductID"] = 107, ["ProductName"] = "USB-C Çoklayıcı Hub 8-in-1", ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["UnitPrice"] = 1750.00m, ["UnitsInStock"] = 31, ["Quantity"] = 4 },
            new() { ["ProductID"] = 108, ["ProductName"] = "Yüksekliği Ayarlanabilir Masa", ["CategoryID"] = 4, ["CategoryName"] = "Mobilya", ["UnitPrice"] = 16900.00m, ["UnitsInStock"] = 6, ["Quantity"] = 1 },
            new() { ["ProductID"] = 109, ["ProductName"] = "Termal Barkod Yazıcı", ["CategoryID"] = 2, ["CategoryName"] = "Elektronik", ["UnitPrice"] = 4320.00m, ["UnitsInStock"] = 19, ["Quantity"] = 2 },
            new() { ["ProductID"] = 110, ["ProductName"] = "Beyaz Tahta Kalem Seti (12'li)", ["CategoryID"] = 3, ["CategoryName"] = "Ofis Malzemeleri", ["UnitPrice"] = 215.00m, ["UnitsInStock"] = 95, ["Quantity"] = 12 }
        },
        ["Customers"] = new()
        {
            new() { ["CustomerID"] = "SOBIS", ["CompanyName"] = "Sobis Teknoloji A.Ş.", ["ContactName"] = "Ahmet Yılmaz", ["City"] = "İstanbul", ["Country"] = "Türkiye", ["Phone"] = "+90 212 555 0101" },
            new() { ["CustomerID"] = "IVENT", ["CompanyName"] = "Ivent Hosting Ltd.", ["ContactName"] = "Mehmet Kaya", ["City"] = "Ankara", ["Country"] = "Türkiye", ["Phone"] = "+90 312 555 0202" },
            new() { ["CustomerID"] = "ANADL", ["CompanyName"] = "Anadolu Lojistik", ["ContactName"] = "Zeynep Demir", ["City"] = "İzmir", ["Country"] = "Türkiye", ["Phone"] = "+90 232 555 0303" },
            new() { ["CustomerID"] = "MARM", ["CompanyName"] = "Marmara Perakende", ["ContactName"] = "Elif Şahin", ["City"] = "Bursa", ["Country"] = "Türkiye", ["Phone"] = "+90 224 555 0404" }
        },
        ["Orders"] = new()
        {
            new() { ["OrderID"] = 5001, ["CustomerID"] = "SOBIS", ["CompanyName"] = "Sobis Teknoloji A.Ş.", ["OrderDate"] = "2026-11-01", ["Freight"] = 185.50m, ["TotalAmount"] = 24650.00m, ["Status"] = "Teslim Edildi" },
            new() { ["OrderID"] = 5002, ["CustomerID"] = "IVENT", ["CompanyName"] = "Ivent Hosting Ltd.", ["OrderDate"] = "2026-11-05", ["Freight"] = 240.00m, ["TotalAmount"] = 38900.00m, ["Status"] = "Hazırlanıyor" },
            new() { ["OrderID"] = 5003, ["CustomerID"] = "ANADL", ["CompanyName"] = "Anadolu Lojistik", ["OrderDate"] = "2026-11-10", ["Freight"] = 120.00m, ["TotalAmount"] = 9600.00m, ["Status"] = "Kargoda" },
            new() { ["OrderID"] = 5004, ["CustomerID"] = "MARM", ["CompanyName"] = "Marmara Perakende", ["OrderDate"] = "2026-11-15", ["Freight"] = 310.75m, ["TotalAmount"] = 41250.00m, ["Status"] = "Teslim Edildi" }
        }
    };

    public List<SbsDataSource> GetAvailableDemoDataSources()
    {
        var result = new List<SbsDataSource>();
        foreach (var (name, rows) in Tables)
        {
            var cols = new List<SbsColumn>();
            if (rows.Count > 0)
            {
                foreach (var kv in rows[0])
                {
                    var dtype = kv.Value switch
                    {
                        int or long or decimal or double or float => "number",
                        bool => "boolean",
                        DateTime => "date",
                        _ => "string"
                    };
                    cols.Add(new SbsColumn { Name = kv.Key, DataType = dtype });
                }
            }
            result.Add(new SbsDataSource
            {
                Name = name,
                Alias = name,
                Type = "demo",
                Columns = cols
            });
        }
        return result;
    }

    public List<Dictionary<string, object?>> ResolveRows(
        SbsReportDocument report,
        string? dataSourceName,
        Dictionary<string, List<Dictionary<string, JsonElement>>>? runtimeData = null)
    {
        if (runtimeData != null && runtimeData.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                if (runtimeData.TryGetValue(dataSourceName, out var extRows))
                    return extRows.Select(ConvertJsonRow).ToList();

                // Türkçe karakter ve büyük/küçük harf duyarsız tablo adı eşleştirmesi
                var normTarget = ExpressionEvaluator.NormalizeTurkishKey(dataSourceName);
                foreach (var kv in runtimeData)
                {
                    if (ExpressionEvaluator.NormalizeTurkishKey(kv.Key) == normTarget)
                        return kv.Value.Select(ConvertJsonRow).ToList();
                }
            }
            // DataSet'te tek tablo varsa otomatik bağla
            if (runtimeData.Count == 1)
                return runtimeData.Values.First().Select(ConvertJsonRow).ToList();
        }

        if (string.IsNullOrWhiteSpace(dataSourceName))
            return new();

        var normName = ExpressionEvaluator.NormalizeTurkishKey(dataSourceName);
        var ds = report.DataSources.FirstOrDefault(d =>
            ExpressionEvaluator.NormalizeTurkishKey(d.Name) == normName ||
            ExpressionEvaluator.NormalizeTurkishKey(d.Alias) == normName);
        if (ds?.InlineData is { Count: > 0 })
            return ds.InlineData.Select(ConvertJsonRow).ToList();

        if (Tables.TryGetValue(dataSourceName, out var demoRows))
            return demoRows;

        return new();
    }

    private static Dictionary<string, object?> ConvertJsonRow(Dictionary<string, JsonElement> row)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in row)
        {
            dict[k] = v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetDecimal(out var dec) ? dec : v.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.ToString()
            };
        }
        return dict;
    }
}
