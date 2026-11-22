# C# Kodundan Rapor Basma, Snapshot ve Performans

**SbsReport**, tarayıcıya veya harici bir PDF aracına ihtiyaç duymadan sunucu tarafında (headless) yüksek hızda PDF ve 10 farklı formatta çıktı üretebilir.

---

## 1. `SbsReportEngine` ile Temel Kullanım

`AddSbsReport()` çağrısı `SbsReportEngine` servisini `Singleton` olarak DI konteynerine kaydeder. Herhangi bir Controller, Minimal API endpoint'i veya servis içine enjekte edebilirsiniz.

```csharp
using System.Data;
using Microsoft.AspNetCore.Mvc;
using SbsReport.Services;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly SbsReportEngine _engine;
    private readonly IWebHostEnvironment _env;

    public ReportsController(SbsReportEngine engine, IWebHostEnvironment env)
    {
        _engine = engine;
        _env = env;
    }

    [HttpGet("fatura/{id:int}")]
    public IActionResult FaturaPdf(int id)
    {
        // 1. Veri kaynağını DataSet olarak hazırlayın
        var dt = new DataTable("Kalemler");
        dt.Columns.Add("URUN_AD", typeof(string));
        dt.Columns.Add("ADET", typeof(int));
        dt.Columns.Add("BIRIM_FIYAT", typeof(decimal));
        dt.Columns.Add("TUTAR", typeof(decimal));

        dt.Rows.Add("Sunucu Barındırma", 1, 4500.00m, 4500.00m);
        dt.Rows.Add("SSL Sertifikası", 2, 750.00m, 1500.00m);

        var ds = new DataSet();
        ds.Tables.Add(dt);

        // 2. Parametreleri ekleyin (şablonda {FaturaNo}, {MusteriAd} olarak kullanılır)
        var parameters = new Dictionary<string, object>
        {
            ["FaturaNo"] = $"FTR-2026-{id:D6}",
            ["MusteriAd"] = "Örnek Teknoloji A.Ş.",
            ["Tarih"] = DateTime.Now.ToString("dd.MM.yyyy")
        };

        // 3. RenderHeadless ile Snapshot + PDF üretin
        var templatePath = Path.Combine(_env.ContentRootPath, "Reports", "SatisFaturasi.sbs");
        var (snapshotBytes, pdfBytes) = _engine.RenderHeadless(templatePath, parameters, ds);

        return File(pdfBytes, "application/pdf", $"Fatura_{id}.pdf");
    }
}
```

---

## 2. Minimal API Örneği

```csharp
app.MapGet("/rapor/pdf", (SbsReportEngine engine, IWebHostEnvironment env) =>
{
    var dt = new DataTable("Data");
    dt.Columns.Add("AD_SOYAD");
    dt.Columns.Add("UNVAN");
    dt.Rows.Add("Ayşe Yılmaz", "Uzman Tabip");

    var ds = new DataSet();
    ds.Tables.Add(dt);

    var path = Path.Combine(env.ContentRootPath, "Reports", "PersonelListesi.sbs");
    var (_, pdfBytes) = engine.RenderHeadless(path, null, ds);

    return Results.File(pdfBytes, "application/pdf", "PersonelListesi.pdf");
});
```

---

## 3. Snapshot (`.sbsdoc`) ile Çoklu Format Dışa Aktarma

`RenderHeadless` size hem `SnapshotBytes` hem de `PdfBytes` döndürür.

**Neden Snapshot önemli?**
Kullanıcı bir raporu önce PDF olarak açıp ardından Excel, Word veya CSV olarak indirmek istediğinde veritabanı sorgusunu (SQL / ORM) tekrar çalıştırmanıza gerek yoktur. `SnapshotBytes` verisini Redis, MemoryCache veya diskte saklayıp `ExportFromSnapshot` ile milisaniyeler içinde diğer formatlara dönüştürebilirsiniz:

```csharp
// İlk üretimde snapshot'ı saklayın
var (snapshotBytes, pdfBytes) = _engine.RenderHeadless(templatePath, parameters, dataSet);

// Daha sonra SQL çalıştırmadan farklı formatlar üretin:
byte[] excelBytes = _engine.ExportFromSnapshot(snapshotBytes, "xlsx");
byte[] wordBytes  = _engine.ExportFromSnapshot(snapshotBytes, "docx");
byte[] htmlBytes  = _engine.ExportFromSnapshot(snapshotBytes, "html");
byte[] csvBytes   = _engine.ExportFromSnapshot(snapshotBytes, "csv");
byte[] rtfBytes   = _engine.ExportFromSnapshot(snapshotBytes, "rtf");
byte[] txtBytes   = _engine.ExportFromSnapshot(snapshotBytes, "txt");
byte[] xmlBytes   = _engine.ExportFromSnapshot(snapshotBytes, "xml");
byte[] jsonBytes  = _engine.ExportFromSnapshot(snapshotBytes, "json");
byte[] svgBytes   = _engine.ExportFromSnapshot(snapshotBytes, "svg");
```

---

## 4. Çoklu Tablo (`DataSet`) ve Türkçe Duyarsız Eşleştirme

- Bir `DataSet` içinde birden fazla `DataTable` gönderebilirsiniz. Her tablo adı (`TableName`), şablondaki veri kaynağı adıyla eşleşir (ör. `{Kalemler.URUN_AD}`, `{Ozet.TOPLAM}`).
- Kolon ve parametre isimleri **Türkçe karakter ve büyük/küçük harf duyarsız** eşleşir:
  - Şablondaki `{Data.Ünvan}` ifadesi `DataTable` içindeki `UNVAN` veya `unvan` kolonuyla otomatik eşleşir.
  - `{Şehir}` ↔ `SEHIR`, `{İsim}` ↔ `ISIM`.

---

## 5. Master Form Otomatik Birleştirme

Rapor şablonunuz bir Master Form'a (`MasterReportName`) bağlıysa, `RenderHeadless` çalıştığında Master Form aynı klasörden otomatik olarak yüklenir, önbelleğe alınır ve sabit üstbilgi/altbilgi tüm sayfalara uygulanır. Ekstra kod yazmanız gerekmez.

---

## 6. Konsol / Worker Service (ASP.NET Core Olmadan) Kullanım

```csharp
using System.Data;
using SbsReport.Services;

var engine = new SbsReportEngine(
    new ReportRenderEngine(new DemoDataService()),
    new ReportExportService());

var ds = new DataSet();
var dt = new DataTable("Data");
dt.Columns.Add("BASLIK");
dt.Rows.Add("Aylık Özet Raporu");
ds.Tables.Add(dt);

var (snapshot, pdf) = engine.RenderHeadless("Reports/Ozet.sbs", null, ds);
File.WriteAllBytes("Ozet.pdf", pdf);
```

---

## 7. Mimari ve Performans Özellikleri

- **Şablon Önbelleği:** `SbsReportEngine`, `.sbs` dosyalarını son değiştirilme zamanına (`LastWriteTimeUtc`) göre bellekte tutar; dosya değişmediği sürece disk okuması ve JSON parse maliyeti oluşmaz.
- **Derlenmiş AST İfade Motoru:** CodeDOM derleme gecikmesi olmadan her ifade yalnızca bir kez derlenir ve satır başına mikrosaniye hızında değerlendirilir.
- **Saf C# PDF 1.4 (`SbsPdfWriter`):**
  - Gerçek TrueType `glyf`/`loca` font alt kümeleme (font subsetting) ile yalnızca kullanılan glifler gömülür.
  - 1-byte Windows-1254 Türkçe kodlaması hızlı yolunu kullanır; tablo dışı Unicode karakter varsa otomatik 2-byte `CIDFontType2` moduna geçer.
  - `ArrayPool<byte>` + `ZLibStream(Optimal)` ile düşük GC/LOH baskısı sağlar.
